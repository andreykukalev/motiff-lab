import os
import shutil
import threading
import time
from functools import partial
from pathlib import Path

import pytest

from app import job_worker, recognise
from app.engines import Engine
from app.job_store import FolderStore, LeaseLost
from app.jobs import Jobs, Settings, message_body
from app.job_worker import Worker
from tests import stand_ins
from tests.test_jobs import Clock, drain, engines, service, submit


def states(jobs: Jobs, job_id: str, engine: str) -> list[str]:
    return [page["engines"][engine] for page in jobs.status(jobs.find(job_id, None))["pages"]]


def test_a_failing_engine_is_retried_after_10_20_and_40_s_then_the_page_is_not_read_by_it(tmp_path: Path):
    clock = Clock()
    jobs, worker = service(tmp_path, engines(tmp_path, homr=stand_ins.fails), clock)
    job_id = submit(jobs, tmp_path, 1)

    assert worker.step() and worker.step()  # homr fails, copisteria reads
    assert states(jobs, job_id, "homr") == ["retrying"]
    for delay in (10, 20, 40):
        clock.advance(delay - 0.5)
        assert not worker.step()
        clock.advance(0.5)
        assert worker.step()
    assert stand_ins.calls(tmp_path / "homr") == ["1", "1", "1", "1"]
    assert not worker.step()

    # The job goes on without it: it is done, with copisteria's reading and what homr saw.
    job = jobs.find(job_id, None)
    assert jobs.status(job)["state"] == "done"
    homr = jobs.fetch_result(job)["pages"][0]["engines"]["homr"]
    assert homr["state"] == "not_read" and "musicxml" not in homr
    assert homr["message"] == "No music staves were found on page 1."
    assert homr["report"]["noteheads"] == 4


def test_a_job_that_no_engine_could_read_has_failed(tmp_path: Path):
    jobs, worker = service(tmp_path, engines(tmp_path, homr=stand_ins.fails, copisteria=stand_ins.dies), Clock(), retry_delays=())
    job_id = submit(jobs, tmp_path, 1)
    drain(worker)
    job = jobs.find(job_id, None)
    assert jobs.status(job)["state"] == "failed"
    assert jobs.fetch_result(job)["pages"][0]["engines"]["copisteria"]["message"] == "Page 1 could not be read."


def test_an_engine_that_hangs_is_stopped_at_the_time_limit(tmp_path: Path):
    jobs, worker = service(tmp_path, engines(tmp_path, homr=stand_ins.hangs), Clock(), unit_seconds=1.0, retry_delays=())
    job_id = submit(jobs, tmp_path, 1, chosen=["homr"])
    began = time.monotonic()
    assert worker.step()
    assert time.monotonic() - began < 10
    message = jobs.fetch_result(jobs.find(job_id, None))["pages"][0]["engines"]["homr"]["message"]
    assert message == "Reading page 1 took too long and was stopped."
    # The engine's own program was stopped with it.
    (program,) = stand_ins.calls(tmp_path / "homr")
    deadline = time.monotonic() + 5
    while _alive(int(program)) and time.monotonic() < deadline:
        time.sleep(0.05)
    assert not _alive(int(program))


def _alive(pid: int) -> bool:
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    with open(f"/proc/{pid}/stat") as stat:
        return stat.read().split()[2] != "Z"


def test_a_unit_whose_worker_died_goes_back_to_the_queue_when_its_lease_runs_out(tmp_path: Path):
    clock = Clock()
    jobs, worker = service(tmp_path, engines(tmp_path), clock, lease_seconds=60)
    job_id = submit(jobs, tmp_path, 1, chosen=["homr"])
    dead = jobs.store.receive(60)  # a worker takes the unit and dies
    assert not worker.step()
    clock.advance(60)
    assert worker.step()
    assert states(jobs, job_id, "homr") == ["read"]
    with pytest.raises(LeaseLost):
        jobs.store.delete_message(dead)


class WorkerDied(BaseException):
    """The worker's process ends while the engine reads: no except clause of the worker sees it."""


def test_a_page_whose_worker_dies_on_every_try_is_given_up_after_the_last(tmp_path: Path, monkeypatch):
    clock = Clock()
    jobs, worker = service(tmp_path, engines(tmp_path), clock, lease_seconds=60)
    job_id = submit(jobs, tmp_path, 1, chosen=["homr"])
    read_within = job_worker.unit_process.read_within

    def read_and_die(*args, **kwargs):
        read_within(*args, **kwargs)
        raise WorkerDied()

    monkeypatch.setattr(job_worker.unit_process, "read_within", read_and_die)
    for _ in range(4):
        with pytest.raises(WorkerDied):
            worker.step()
        assert states(jobs, job_id, "homr") == ["reading"]
        assert not worker.step()  # its lease still holds
        clock.advance(60)
    assert worker.step()
    assert states(jobs, job_id, "homr") == ["not_read"]
    assert stand_ins.calls(tmp_path / "homr") == ["1"] * 4
    assert jobs.store.queue_length() == 0


def test_a_worker_renews_its_lease_while_it_reads(tmp_path: Path):
    readers = {"homr": Engine("homr", lambda: "0.7.0", partial(stand_ins.reads_slowly, tmp_path / "homr", 1.5))}
    jobs = Jobs(FolderStore(tmp_path / "store"), readers, Settings(lease_seconds=0.6))
    job_id = submit(jobs, tmp_path, 1, chosen=["homr"])
    reading = threading.Thread(target=Worker(jobs).step)
    reading.start()
    time.sleep(1.0)  # past the first lease
    assert jobs.store.receive(0.6) is None
    reading.join()
    assert states(jobs, job_id, "homr") == ["read"]
    assert stand_ins.calls(tmp_path / "homr") == ["1"]


def test_an_engine_failing_on_pages_in_a_row_is_switched_off_for_the_job(tmp_path: Path):
    jobs, worker = service(tmp_path, engines(tmp_path, homr=stand_ins.fails), Clock(), retry_delays=(), breaker_pages=2)
    job_id = submit(jobs, tmp_path, 4)
    drain(worker)
    assert stand_ins.calls(tmp_path / "homr") == ["1", "2"]
    assert states(jobs, job_id, "homr") == ["not_read"] * 4
    assert states(jobs, job_id, "copisteria") == ["read"] * 4
    assert jobs.status(jobs.find(job_id, None))["state"] == "done"


def test_a_unit_is_read_once_for_its_one_live_message_and_keeps_one_result(tmp_path: Path):
    clock = Clock()
    jobs, worker = service(tmp_path, engines(tmp_path), clock)
    job_id = submit(jobs, tmp_path, 1, chosen=["homr"])
    jobs.store.receive(60)  # its message is being worked
    jobs.store.send(message_body(job_id, 1, "homr", "stale"))  # one the supervisor replaced
    assert worker.step()
    assert stand_ins.calls(tmp_path / "homr") == []
    clock.advance(60)
    assert worker.step()
    jobs.store.send(message_body(job_id, 1, "homr", jobs.unit(job_id, 1, "homr").token))  # the same message again
    assert worker.step()
    assert stand_ins.calls(tmp_path / "homr") == ["1"]
    assert jobs.store.names(f"jobs/{job_id}/results/") == [f"jobs/{job_id}/results/001-homr.json"]


def test_a_restarted_server_goes_on_with_its_jobs(tmp_path: Path):
    clock = Clock()
    before, worker = service(tmp_path, engines(tmp_path), clock)
    job_id = submit(before, tmp_path, 2)
    assert worker.step()
    before.store.receive(60)  # being read when the server stopped

    after, worker = service(tmp_path, engines(tmp_path), clock)
    assert after.status(after.find(job_id, None))["state"] == "running"
    assert drain(worker) == 3
    assert after.status(after.find(job_id, None))["state"] == "done"
    assert sorted(stand_ins.calls(tmp_path / "homr") + stand_ins.calls(tmp_path / "copisteria")) == ["1", "1", "2", "2"]


def test_the_supervisor_gives_a_unit_that_lost_its_message_a_new_one(tmp_path: Path):
    jobs, worker = service(tmp_path, engines(tmp_path), Clock())
    job_id = submit(jobs, tmp_path, 1)
    while (lost := jobs.store.receive(60)) is not None:
        jobs.store.delete_message(lost)
    jobs.supervise()
    assert jobs.store.queue_length() == 2
    drain(worker)
    assert jobs.status(jobs.find(job_id, None))["state"] == "done"


def test_a_unit_retried_later_that_lost_its_message_keeps_its_pause(tmp_path: Path):
    clock = Clock()
    jobs, worker = service(tmp_path, engines(tmp_path, homr=stand_ins.fails), clock)
    job_id = submit(jobs, tmp_path, 1, chosen=["homr"])
    assert worker.step()  # fails: retried after 10 s
    shutil.rmtree(tmp_path / "store" / "queue")  # the server stopped before its message reached the disk
    jobs, worker = service(tmp_path, engines(tmp_path, homr=stand_ins.fails), clock)
    clock.advance(4)
    jobs.supervise()
    assert jobs.store.queue_length() == 1
    clock.advance(5.5)
    assert not worker.step()
    clock.advance(0.5)
    assert worker.step()
    assert stand_ins.calls(tmp_path / "homr") == ["1", "1"]
    assert jobs.unit(job_id, 1, "homr").tries == 2


def test_the_supervisor_gives_no_second_message_to_a_unit_sent_one_during_its_pass(tmp_path: Path):
    jobs, worker = service(tmp_path, engines(tmp_path), Clock())
    job_id = submit(jobs, tmp_path, 1)
    while (lost := jobs.store.receive(60)) is not None:
        jobs.store.delete_message(lost)
    unit, sent = jobs.unit, []

    def a_worker_sends_copisteria_one_meanwhile(job: str, page: int, engine: str):
        if engine == "copisteria" and not sent:
            sent.append(engine)
            jobs.enqueue(job, page, engine, unit(job, page, engine))
        return unit(job, page, engine)

    jobs.unit = a_worker_sends_copisteria_one_meanwhile
    jobs.supervise()
    jobs.unit = unit
    assert jobs.store.queue_length() == 2  # homr's new message and copisteria's own, no second one
    assert drain(worker) == 2
    assert jobs.status(jobs.find(job_id, None))["state"] == "done"


def test_the_supervisor_asks_a_busy_queue_its_length_once_a_job(tmp_path: Path):
    jobs, _ = service(tmp_path, engines(tmp_path), Clock())
    submit(jobs, tmp_path, 2)
    submit(jobs, tmp_path, 1)
    length, asked = jobs.store.queue_length, []
    jobs.store.queue_length = lambda: asked.append(1) or length()
    jobs.supervise()
    assert len(asked) == 2  # two jobs of four and two waiting units, their messages in the queue
    assert length() == 6


def test_the_supervisor_closes_a_job_past_its_deadline_with_what_was_read(tmp_path: Path):
    clock = Clock()
    jobs, worker = service(tmp_path, engines(tmp_path), clock, deadline_seconds=3600)
    job_id = submit(jobs, tmp_path, 1)
    assert worker.step()
    clock.advance(3601)
    jobs.supervise()
    job = jobs.find(job_id, None)
    assert jobs.status(job)["state"] == "done"
    assert [entry["state"] for entry in jobs.fetch_result(job)["pages"][0]["engines"].values()] == ["read", "not_read"]
    drain(worker)  # the closed unit's message is dropped
    assert stand_ins.calls(tmp_path / "copisteria") == []


def test_a_job_deleted_while_it_is_read_leaves_nothing(tmp_path: Path):
    jobs, worker = service(tmp_path, engines(tmp_path), Clock())
    job_id = submit(jobs, tmp_path, 1)
    jobs.delete(jobs.find(job_id, None))
    assert drain(worker) == 2
    assert stand_ins.calls(tmp_path / "homr") == []
    assert jobs.store.names("") == []


def test_engines_reading_side_by_side_share_the_cores(tmp_path: Path, monkeypatch):
    monkeypatch.setattr(recognise, "cores", lambda: 8)
    readers = engines(tmp_path, homr=stand_ins.notes_threads, copisteria=stand_ins.notes_threads)
    jobs, worker = service(tmp_path, readers, Clock(), workers=4)
    submit(jobs, tmp_path, 1)
    drain(worker)
    limits = (tmp_path / "homr").read_text().splitlines()[0]
    assert limits.startswith("2|") and limits.endswith("-XX:ActiveProcessorCount=2")
