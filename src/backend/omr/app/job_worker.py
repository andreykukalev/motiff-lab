"""The workers that read a job's units, and the thread that supervises them (decision record 0035, section 3).

A worker takes one message from the queue, leases it, and reads that page with that engine in a process of its own
(unit_process.py), renewing the lease while it does. Then:

- read: the result, with the page's evidence where the engine gives it (copisteria), is written under the unit's name
  (a repeat overwrites it) and the engine's time is measured;
- failed or past the time limit: retried after 10, 20 and 40 s; after the last try the unit is not read by this
  engine, what was seen of the page is kept for its report, and the job goes on;
- a worker that dies leaves its lease to run out, and the message comes back to another; its try counts, so a page
  that kills its worker each time is given up too;
- an engine not reading `breaker_pages` pages of a job in a row is switched off for that job: its units still waiting
  are not read.

A message whose job is gone, whose unit is finished or whose token is not the unit's is dropped.
"""

from __future__ import annotations

import json
import logging
import tempfile
import threading
import time
from collections.abc import Callable
from dataclasses import asdict, replace
from pathlib import Path

from app import unit_process
from app.job_store import LeaseLost, Message
from app.jobs import FINAL, NOT_READ, READ, READING, RETRYING, WAITING, Job, Jobs, Unit, page_name, result_name
from app.recognise import RecogniseError

_log = logging.getLogger("motiff.jobs")


class Worker:
    def __init__(self, jobs: Jobs, timer: Callable[[], float] = time.monotonic) -> None:
        self._jobs = jobs
        self._timer = timer  # measures how long a reading took

    def step(self) -> bool:
        """Takes one message from the queue and works it; False when there was none."""
        settings = self._jobs.settings
        message = self._jobs.store.receive(settings.lease_seconds)
        if message is None:
            return False
        held = [message]

        def renew_lease() -> None:
            held[0] = self._jobs.store.renew(held[0], settings.lease_seconds)

        try:
            self._work(message, renew_lease)
        except LeaseLost:
            # The lease ran out while the unit was read, so the message went back to the queue: its next worker reads it.
            return True
        try:
            self._jobs.store.delete_message(held[0])
        except LeaseLost:
            pass
        return True

    def _work(self, message: Message, renew_lease: Callable[[], None]) -> None:
        ref = json.loads(message.body)
        job_id, page, name = ref["job"], ref["page"], ref["engine"]
        job = self._jobs.job(job_id)
        unit = self._jobs.unit(job_id, page, name) if job is not None else None
        if job is None or unit is None or unit.state in FINAL or unit.token != ref["token"]:
            return
        engine = self._jobs.readers.get(name)
        if engine is None:
            self._jobs.write_unit(job_id, page, name, replace(unit, state=NOT_READ, why="engine"))
            return
        if unit.tries >= self._jobs.settings.tries:
            # Its workers died on every try (the lease ran out each time).
            self._give_up(job, page, name, unit, RecogniseError(f"Page {page} could not be read."))
            return

        unit = replace(unit, state=READING, tries=unit.tries + 1, started=self._jobs.clock(), retry_at=None)
        self._jobs.write_unit(job_id, page, name, unit)
        image = self._jobs.store.read(page_name(job_id, page))
        began = self._timer()
        try:
            if image is None:
                raise RecogniseError(f"Page {page} could not be read.")
            with tempfile.TemporaryDirectory(prefix="motiff-unit-") as folder:
                path = Path(folder) / f"page-{page:02d}.png"
                path.write_bytes(image)
                settings = self._jobs.settings
                reading = unit_process.read_within(
                    engine.read, path, page, settings.unit_seconds, renew_lease, settings.lease_seconds / 3, settings.threads
                )
                found = engine.evidence(path) if engine.evidence else None
        except RecogniseError as error:
            self._failed(job, page, name, unit, error)
            return
        seconds = self._timer() - began
        if self._jobs.job(job_id) is None:
            return  # deleted while it was read
        self._write_result(job_id, page, name, {
            "musicxml": reading.musicxml.decode("utf-8", "replace"),
            "found": asdict(reading.found),
            "version": engine.version(),
            "evidence": found,
        })
        self._jobs.write_unit(job_id, page, name, replace(unit, state=READ))
        self._jobs.measure(name, seconds)

    def _failed(self, job: Job, page: int, engine: str, unit: Unit, error: RecogniseError) -> None:
        if self._jobs.job(job.id) is None:
            return
        delays = self._jobs.settings.retry_delays
        if unit.tries <= len(delays):
            delay = delays[unit.tries - 1]
            retrying = replace(unit, state=RETRYING, retry_at=self._jobs.clock() + delay)
            self._jobs.enqueue(job.id, page, engine, retrying, delay)
            return
        self._give_up(job, page, engine, unit, error)

    def _give_up(self, job: Job, page: int, engine: str, unit: Unit, error: RecogniseError) -> None:
        """The poison list: the unit is not read by this engine; what was seen is kept for the page's report."""
        self._write_result(job.id, page, engine, {"musicxml": None, "found": asdict(error.found), "message": str(error)})
        self._jobs.write_unit(job.id, page, engine, replace(unit, state=NOT_READ, why="failed"))
        self._break_if_failing(job, engine)

    def _break_if_failing(self, job: Job, engine: str) -> None:
        """The circuit breaker: an engine that failed on `breaker_pages` pages in a row is switched off for the job."""
        run = 0
        for page in job.pages:
            unit = self._jobs.unit(job.id, page, engine)
            run = run + 1 if unit.state == NOT_READ and unit.why == "failed" else 0
            if run >= self._jobs.settings.breaker_pages:
                break
        else:
            return
        for page in job.pages:
            unit = self._jobs.unit(job.id, page, engine)
            if unit.state in (WAITING, RETRYING):
                self._jobs.write_unit(job.id, page, engine, replace(unit, state=NOT_READ, why="breaker"))

    def _write_result(self, job_id: str, page: int, engine: str, result: dict[str, object]) -> None:
        self._jobs.store.write(result_name(job_id, page, engine), json.dumps(result).encode())


class Runner:
    """The worker threads and the supervisor's, started with the server and stopped with it."""

    def __init__(self, jobs: Jobs, worker: Worker | None = None) -> None:
        self._jobs = jobs
        self._worker = worker or Worker(jobs)
        self._stopping = threading.Event()
        self._threads: list[threading.Thread] = []

    def start_threads(self) -> None:
        self._stopping.clear()
        self._threads = [
            *(threading.Thread(target=self._work, name=f"scan-worker-{n}", daemon=True) for n in range(self._jobs.settings.workers)),
            threading.Thread(target=self._supervise, name="scan-supervisor", daemon=True),
        ]
        for thread in self._threads:
            thread.start()

    def stop_threads(self, wait: float = 5.0) -> None:
        self._stopping.set()
        for thread in self._threads:
            thread.join(wait)

    def _work(self) -> None:
        while not self._stopping.is_set():
            try:
                busy = self._worker.step()
            except Exception as error:  # noqa: BLE001 (a worker goes on; the message comes back when its lease runs out)
                _log.warning("A scan job worker met %s.", type(error).__name__)
                busy = False
            if not busy:
                self._stopping.wait(self._jobs.settings.idle_seconds)

    def _supervise(self) -> None:
        while not self._stopping.wait(self._jobs.settings.supervisor_seconds):
            try:
                self._jobs.supervise()
            except Exception as error:  # noqa: BLE001
                _log.warning("The scan job supervisor met %s.", type(error).__name__)
