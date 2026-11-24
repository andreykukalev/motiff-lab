import json
import re
import time
from functools import partial
from pathlib import Path

from fastapi.testclient import TestClient

from app import evidence, pages
from app.auth import AuthError
from app.engines import Engine
from app.job_store import FolderStore
from app.job_worker import Worker
from app.jobs import Jobs, Settings
from app.main import create_app
from tests import stand_ins
from tests.test_app import pdf, png
from tests.test_merge import numbers


class Clock:
    def __init__(self) -> None:
        self.now = 1_800_000_000.0

    def __call__(self) -> float:
        return self.now

    def advance(self, seconds: float) -> None:
        self.now += seconds


def engines(log: Path, homr=stand_ins.reads, copisteria=stand_ins.reads, homr_seconds=30.0, copisteria_seconds=25.0) -> dict[str, Engine]:
    return {
        "homr": Engine("homr", lambda: "0.7.0", partial(homr, log / "homr"), homr_seconds),
        "copisteria": Engine("copisteria", lambda: "1.3.4", partial(copisteria, log / "copisteria"), copisteria_seconds),
    }


def service(tmp_path: Path, readers: dict[str, Engine], clock: Clock, timer=time.monotonic, **settings) -> tuple[Jobs, Worker]:
    jobs = Jobs(FolderStore(tmp_path / "store", clock), readers, Settings(**settings), clock)
    return jobs, Worker(jobs, timer)


def submit(jobs: Jobs, tmp_path: Path, count: int, chosen=("homr", "copisteria"), player=None) -> str:
    folder = tmp_path / f"render-{time.monotonic_ns()}"
    folder.mkdir()
    written = pages.write_pages(pages.sniff(pdf(count)), folder, 8)
    return jobs.submit(list(enumerate(written, 1)), list(chosen), player)


def drain(worker: Worker) -> int:
    steps = 0
    while worker.step():
        steps += 1
    return steps


def app_client(tmp_path: Path, verify=None, **settings) -> TestClient:
    readers = engines(tmp_path)
    settings = {"idle_seconds": 0.02, **settings}
    return TestClient(
        create_app(
            readers["homr"].read,
            other_engines=[readers["copisteria"]],
            verify=verify,
            jobs_store=FolderStore(tmp_path / "store"),
            job_settings=Settings(**settings),
        )
    )


def wait_until_finished(client: TestClient, status: str, headers=None) -> dict:
    deadline = time.monotonic() + 20
    while time.monotonic() < deadline:
        body = client.get(status, headers=headers).json()
        if body["state"] in ("done", "failed"):
            return body
        time.sleep(0.05)
    raise AssertionError(body)


def test_a_job_reads_every_page_with_every_engine_in_the_background(tmp_path: Path):
    with app_client(tmp_path) as client:
        made = client.post("/jobs", files={"file": ("score.pdf", pdf(2), "application/pdf")})
        assert made.status_code == 202
        job_id = made.json()["id"]
        assert re.fullmatch(r"[A-Za-z0-9_-]{24}", job_id)
        assert made.json()["status"] == made.headers["location"] == f"/jobs/{job_id}"

        status = wait_until_finished(client, f"/jobs/{job_id}")
        assert status == {
            "id": job_id,
            "state": "done",
            "pages": [{"page": 1, "engines": {"homr": "read", "copisteria": "read"}}, {"page": 2, "engines": {"homr": "read", "copisteria": "read"}}],
            "progress": 1.0,
            "etaSeconds": 0,
        }

        result = client.get(f"/jobs/{job_id}/result").json()
        second = result["pages"][1]["engines"]["copisteria"]
        assert numbers(second["musicxml"].encode()) == ["2"]
        assert second["state"] == "read" and second["version"] == "1.3.4"
        assert second["report"]["page"] == 2 and second["report"]["noteheads"] == 10 and second["report"]["measures"] == 1

        health = client.get("/health").json()
        assert [(entry["engine"], entry["pagesMeasured"]) for entry in health["engines"]] == [("homr", 2), ("copisteria", 2)]
        assert health["jobs"] == {"queue": 0, "workers": 1}

        assert client.delete(f"/jobs/{job_id}").status_code == 204
        assert client.get(f"/jobs/{job_id}").status_code == 404
        assert not (tmp_path / "store" / "blobs" / "jobs" / job_id).exists()


def test_the_result_is_refused_until_every_unit_is_finished(tmp_path: Path):
    client = app_client(tmp_path)  # not started: no worker takes the units
    job_id = client.post("/jobs", files={"file": ("photo.png", png(), "image/png")}).json()["id"]
    assert client.get(f"/jobs/{job_id}").json()["state"] == "queued"
    refused = client.get(f"/jobs/{job_id}/result")
    assert refused.status_code == 409
    assert refused.json()["code"] == "running"


def test_a_job_reads_the_pages_and_engines_it_names(tmp_path: Path):
    client = app_client(tmp_path)
    job_id = client.post("/jobs", files={"file": ("score.pdf", pdf(3), "application/pdf")}, data={"pages": "3, 1", "engines": "copisteria"}).json()["id"]
    assert client.get(f"/jobs/{job_id}").json()["pages"] == [{"page": 1, "engines": {"copisteria": "waiting"}}, {"page": 3, "engines": {"copisteria": "waiting"}}]

    def refusal(**data):
        response = client.post("/jobs", files={"file": ("score.pdf", pdf(3), "application/pdf")}, data=data)
        return response.status_code, response.json().get("code")

    assert refusal(engines="audiveris") == (400, "engine")
    assert refusal(pages="two") == (400, "pages")
    assert refusal(pages="4") == (422, None)
    assert client.post("/jobs", files={"file": ("photo.png", b"", "image/png")}).status_code == 400


def test_an_id_that_was_not_given_finds_nothing(tmp_path: Path):
    client = app_client(tmp_path)
    first, second = (client.post("/jobs", files={"file": ("photo.png", png(), "image/png")}).json()["id"] for _ in range(2))
    assert first != second
    for made_up in (first[:-1] + ("A" if first[-1] != "A" else "B"), "..", "jobs"):
        assert client.get(f"/jobs/{made_up}").status_code == 404


def test_progress_and_the_estimate_come_from_each_engine_s_seconds_per_page(tmp_path: Path):
    clock = Clock()
    timer = iter([0.0, 36.0]).__next__  # homr reads page 1 in 36 s
    jobs, worker = service(tmp_path, engines(tmp_path, homr_seconds=40.0, copisteria_seconds=20.0), clock, timer)
    job_id = submit(jobs, tmp_path, 2)

    start = jobs.status(jobs.find(job_id, None))
    assert (start["state"], start["progress"], start["etaSeconds"]) == ("queued", 0.0, 120)
    assert jobs.seconds_per_page("homr") == (40.0, 0)

    assert worker.step()
    status = jobs.status(jobs.find(job_id, None))
    # homr is now measured at 36 s a page: 2 x 36 + 2 x 20 = 112 s in all, 36 of them done.
    assert status["state"] == "running"
    assert status["pages"][0]["engines"] == {"homr": "read", "copisteria": "waiting"}
    assert status["progress"] == round(36 / 112, 3)
    assert status["etaSeconds"] == 76
    assert jobs.seconds_per_page("homr") == (36.0, 1)
    assert jobs.figures() == {"queue": 3, "workers": 1}


def test_a_job_is_deleted_a_day_after_it_was_made_at_the_latest(tmp_path: Path):
    clock = Clock()
    jobs, _ = service(tmp_path, engines(tmp_path), clock)
    job_id = submit(jobs, tmp_path, 1)
    # The supervisor comes round once a minute, so it deletes what would be over a day old by its next round.
    clock.advance(24 * 3600 - 61)
    jobs.supervise()
    assert jobs.find(job_id, None) is not None
    clock.advance(1)
    jobs.supervise()
    assert jobs.store.names("jobs/") == []
    assert not (tmp_path / "store" / "blobs" / "jobs" / job_id).exists()


def test_a_fetched_result_is_deleted_soon_after(tmp_path: Path):
    clock = Clock()
    jobs, worker = service(tmp_path, engines(tmp_path), clock, fetched_seconds=600)
    job_id = submit(jobs, tmp_path, 1)
    drain(worker)
    jobs.fetch_result(jobs.find(job_id, None))
    clock.advance(599)
    assert jobs.find(job_id, None) is not None
    clock.advance(1)
    # Asked for after its time, it is not answered, even before the supervisor comes round to delete it.
    assert jobs.find(job_id, None) is None
    jobs.supervise()
    assert jobs.store.names("jobs/") == []


def test_a_hosted_job_answers_only_the_player_who_made_it(tmp_path: Path):
    def verify(authorization, choice):
        if authorization is None:
            raise AuthError("Sign in to have Motiff Lab's server read your music.")
        return authorization.removeprefix("Bearer ")

    client = app_client(tmp_path, verify=verify)
    assert client.post("/jobs", files={"file": ("photo.png", png(), "image/png")}).status_code == 401
    mine, theirs = {"Authorization": "Bearer player-a"}, {"Authorization": "Bearer player-b"}
    job_id = client.post("/jobs", files={"file": ("photo.png", png(), "image/png")}, headers=mine).json()["id"]

    assert client.get(f"/jobs/{job_id}", headers=mine).status_code == 200
    assert client.get(f"/jobs/{job_id}").status_code == 401
    for ask in (client.get, client.delete):
        assert ask(f"/jobs/{job_id}", headers=theirs).status_code == 404
    assert client.get(f"/jobs/{job_id}/result", headers=theirs).status_code == 404
    record = (tmp_path / "store" / "blobs" / "jobs" / job_id / "job.json").read_text()
    assert "player-a" not in record and json.loads(record)["owner"]


def test_the_app_may_delete_a_job_and_read_where_its_status_is(tmp_path: Path):
    client = app_client(tmp_path)
    preflight = client.options("/jobs/x", headers={"Origin": "https://motiff.example", "Access-Control-Request-Method": "DELETE"})
    assert "DELETE" in preflight.headers["access-control-allow-methods"]
    simple = client.get("/health", headers={"Origin": "https://motiff.example"})
    assert "Location" in simple.headers["access-control-expose-headers"]


def test_the_result_gives_each_page_s_evidence_from_the_engine_that_saw_it(tmp_path: Path):
    jobs, worker = service(tmp_path, engines(tmp_path, copisteria=stand_ins.reads_with_evidence), Clock())
    job_id = submit(jobs, tmp_path, 2)
    drain(worker)
    result = jobs.fetch_result(jobs.find(job_id, None))
    assert [page["evidence"] for page in result["pages"]] == [evidence.for_page(n, 0, stand_ins.EVIDENCE) for n in (1, 2)]
    assert all("evidence" not in reading for page in result["pages"] for reading in page["engines"].values())


def test_a_page_no_engine_gave_evidence_for_has_none(tmp_path: Path):
    jobs, worker = service(tmp_path, engines(tmp_path, copisteria=stand_ins.fails), Clock(), retry_delays=())
    job_id = submit(jobs, tmp_path, 1)
    drain(worker)
    assert "evidence" not in jobs.fetch_result(jobs.find(job_id, None))["pages"][0]


def test_a_page_s_engines_read_it_in_parallel_by_default():
    assert Settings.from_environment(4).workers == 4
