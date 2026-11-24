"""Scan jobs (decision record 0035): a file's pages read in the background, each page by each engine.

A job is made from the pages of one upload (pages.py renders them) and the engines that read them. Its unit of work is
one page read by one engine: a job of five pages and two engines is ten units, each a message in the queue that a
worker takes (job_worker.py). Everything lies in the store (job_store.py) under the job's id:

- `jobs/<id>/job.json`: who made it (a hash of the player's id, never the id), when, its pages and engines, and what
  was seen of each page before reading (its look);
- `jobs/<id>/pages/<page>.png`: the page as the engines read it. The upload itself is not kept;
- `jobs/<id>/units/<page>-<engine>.json`: the unit's state, written by the worker that holds its message;
- `jobs/<id>/results/<page>-<engine>.json`: its reading, or what was seen when it could not be read. The name is the
  unit's, so a repeat overwrites it.

The progress and the estimate weigh each unit by its engine's seconds per page, measured on this server
(`engines/<engine>.json`). The supervisor deletes a job once its result was fetched and a little time has passed, or a
day after it was made at the latest; it gives a unit whose message was lost a new one, and closes a job that can no
longer finish. Nothing about the music is logged.
"""

from __future__ import annotations

import hashlib
import json
import math
import os
import re
import secrets
import time
from collections.abc import Callable, Iterator
from dataclasses import asdict, dataclass, field, replace
from typing import Any

from app import engines, pages, quality, recognise
from app.job_store import Store

WAITING, READING, READ, RETRYING, NOT_READ = "waiting", "reading", "read", "retrying", "not_read"
FINAL = (READ, NOT_READ)

# A job's id: 24 characters from 18 random bytes, so it cannot be guessed.
_ID = re.compile(r"^[A-Za-z0-9_-]{24}$")


@dataclass(frozen=True)
class Settings:
    workers: int = 1  # the engines use every core, so one unit is read at a time unless JOB_WORKERS says otherwise
    unit_seconds: float = 300.0  # a unit's time limit, as the engines' own (recognise.PAGE_TIMEOUT_SECONDS)
    lease_seconds: float = 60.0  # renewed every third of it while the unit is read
    retry_delays: tuple[float, ...] = (10.0, 20.0, 40.0)  # a unit is tried once and retried after each of these
    breaker_pages: int = 3  # an engine not reading this many pages in a row is switched off for the job
    keep_seconds: float = 24 * 3600.0  # a job is deleted at the latest this long after it was made
    fetched_seconds: float = 600.0  # and this long after its result was first fetched, if the app did not delete it
    deadline_seconds: float = 3 * 3600.0  # a job not finished by then is closed, so the player still gets what was read
    supervisor_seconds: float = 60.0
    idle_seconds: float = 1.0  # how long a worker waits when the queue is empty

    @property
    def tries(self) -> int:
        return len(self.retry_delays) + 1

    @classmethod
    def from_environment(cls) -> Settings:
        return cls(workers=max(1, int(os.environ.get("JOB_WORKERS", "1"))))


@dataclass(frozen=True)
class Unit:
    state: str = WAITING
    tries: int = 0  # how many times a worker has started reading it
    token: str = ""  # names its one live message; a message with another token is stale and dropped
    started: float | None = None
    retry_at: float | None = None
    why: str | None = None  # for not_read: "failed", "breaker" (engine switched off), "closed" or "engine" (gone)


@dataclass(frozen=True)
class Job:
    id: str
    owner: str | None
    created: float
    pages: tuple[int, ...]
    engines: tuple[str, ...]
    looks: dict[str, dict[str, Any]] = field(default_factory=dict)  # by page number, quality.Look's fields
    fetched: float | None = None


class NotFinished(Exception):
    """The job is still waiting or being read, so it has no result yet."""


def owner_of(player: str | None) -> str | None:
    """What a job keeps of the signed-in player who made it: a hash of the id, enough to tell the player again."""
    return None if player is None else hashlib.sha256(player.encode()).hexdigest()


def job_name(job_id: str) -> str:
    return f"jobs/{job_id}/job.json"


def page_name(job_id: str, page: int) -> str:
    return f"jobs/{job_id}/pages/{page:03d}.png"


def unit_name(job_id: str, page: int, engine: str) -> str:
    return f"jobs/{job_id}/units/{page:03d}-{engine}.json"


def result_name(job_id: str, page: int, engine: str) -> str:
    return f"jobs/{job_id}/results/{page:03d}-{engine}.json"


def message_body(job_id: str, page: int, engine: str, token: str) -> str:
    return json.dumps({"job": job_id, "page": page, "engine": engine, "token": token})


class Jobs:
    def __init__(
        self, store: Store, readers: dict[str, engines.Engine], settings: Settings, clock: Callable[[], float] = time.time
    ) -> None:
        self.store = store
        self.readers = readers
        self.settings = settings
        self.clock = clock

    # ---- Records ---------------------------------------------------------------------------------------------

    def job(self, job_id: str) -> Job | None:
        data = self.store.read(job_name(job_id)) if _ID.match(job_id) else None
        if data is None:
            return None
        kept = json.loads(data)
        return Job(**{**kept, "pages": tuple(kept["pages"]), "engines": tuple(kept["engines"])})

    def unit(self, job_id: str, page: int, engine: str) -> Unit:
        data = self.store.read(unit_name(job_id, page, engine))
        return Unit(**json.loads(data)) if data is not None else Unit()

    def write_unit(self, job_id: str, page: int, engine: str, unit: Unit) -> None:
        self.store.write(unit_name(job_id, page, engine), json.dumps(asdict(unit)).encode())

    def units(self, job: Job) -> Iterator[tuple[int, str, Unit]]:
        for page in job.pages:
            for engine in job.engines:
                yield page, engine, self.unit(job.id, page, engine)

    def enqueue(self, job_id: str, page: int, engine: str, unit: Unit, delay: float = 0.0) -> None:
        """Writes the unit with a new token and sends its message; an older message of the unit becomes stale."""
        token = secrets.token_hex(8)
        self.write_unit(job_id, page, engine, replace(unit, token=token))
        self.store.send(message_body(job_id, page, engine, token), delay)

    def seconds_per_page(self, engine: str) -> tuple[float, int]:
        """The engine's measured seconds per page on this server and how many pages it was measured on; until one
        page has been read, the engine's own figure."""
        data = self.store.read(f"engines/{engine}.json")
        if data is None:
            reader = self.readers.get(engine)
            return (reader.seconds if reader else 60.0), 0
        kept = json.loads(data)
        return kept["seconds"], kept["pages"]

    def measure(self, engine: str, seconds: float) -> None:
        """Adds one page's reading time to the engine's average: of every page at first, of about the last 20 after."""
        average, count = self.seconds_per_page(engine)
        count += 1
        average = seconds if count == 1 else average + (seconds - average) / min(count, 20)
        self.store.write(f"engines/{engine}.json", json.dumps({"seconds": round(average, 2), "pages": count}).encode())

    # ---- What the routes ask ---------------------------------------------------------------------------------

    def submit(self, written: list[tuple[int, pages.Page]], chosen: list[str], player: str | None) -> str:
        """Makes a job of the numbered pages read by the chosen engines, puts its units in the queue, and returns its id."""
        job_id = secrets.token_urlsafe(18)
        numbers = tuple(number for number, _ in written)
        looks = {str(number): asdict(page.look) for number, page in written}
        job = Job(job_id, owner_of(player), self.clock(), numbers, tuple(chosen), looks)
        # The job's record first: a folder without one is left over from a deleted job, and the supervisor removes it.
        self.store.write(job_name(job_id), json.dumps(asdict(job)).encode())
        for number, page in written:
            self.store.write(page_name(job_id, number), page.path.read_bytes())
        for number in numbers:
            for engine in chosen:
                self.enqueue(job_id, number, engine, Unit())
        return job_id

    def find(self, job_id: str, player: str | None) -> Job | None:
        """The player's job; None when there is none, it is another player's, or its time is up."""
        job = self.job(job_id)
        if job is None or job.owner != owner_of(player) or self._expired(job, self.clock()):
            return None
        return job

    def status(self, job: Job) -> dict[str, Any]:
        now = self.clock()
        units = list(self.units(job))
        seconds = {engine: self.seconds_per_page(engine)[0] for engine in job.engines}
        state = _job_state([unit for _, _, unit in units])
        total = sum(seconds[engine] for _, engine, _ in units) or 1.0
        left = sum(_seconds_left(unit, seconds[engine], now) for _, engine, unit in units)
        final = state in ("done", "failed")
        return {
            "id": job.id,
            "state": state,
            "pages": [
                {"page": page, "engines": {engine: unit.state for p, engine, unit in units if p == page}} for page in job.pages
            ],
            "progress": 1.0 if final else round(max(0.0, min(1.0, 1 - left / total)), 3),
            "etaSeconds": 0 if final else math.ceil(left / self.settings.workers),
        }

    def fetch_result(self, job: Job) -> dict[str, Any]:
        """Each page's reading by each engine, with its report; marks the job fetched, so it is deleted soon after."""
        units = list(self.units(job))
        if any(unit.state not in FINAL for _, _, unit in units):
            raise NotFinished()
        if job.fetched is None:
            self.store.write(job_name(job.id), json.dumps(asdict(replace(job, fetched=self.clock()))).encode())
        return {
            "id": job.id,
            "pages": [
                {"page": page, "engines": {engine: self._reading(job, page, engine, unit) for p, engine, unit in units if p == page}}
                for page in job.pages
            ],
        }

    def delete(self, job: Job) -> None:
        """Deletes the job's files and results. Its messages still in the queue are dropped when a worker takes them."""
        self.store.delete(f"jobs/{job.id}/")

    def figures(self) -> dict[str, Any]:
        return {"queue": self.store.queue_length(), "workers": self.settings.workers}

    def _reading(self, job: Job, page: int, engine: str, unit: Unit) -> dict[str, Any]:
        data = self.store.read(result_name(job.id, page, engine))
        kept = json.loads(data) if data is not None else {}
        musicxml = kept.get("musicxml")
        found = kept.get("found") or {}
        look = quality.Look(**job.looks[str(page)])
        report = pages.page_report(page, look, recognise.Found(**found), musicxml.encode() if musicxml else None)
        reading: dict[str, Any] = {"state": unit.state, "report": report}
        if kept.get("version"):
            reading["version"] = kept["version"]
        if musicxml is not None:
            reading["musicxml"] = musicxml
        else:
            reading["message"] = kept.get("message") or f"Page {page} could not be read."
        return reading

    # ---- The supervisor --------------------------------------------------------------------------------------

    def supervise(self) -> None:
        """Deletes the jobs whose time is up and folders left from deleted ones; gives each unit that lost its message
        a new one; closes the units whose engine this server no longer has, and the jobs past their deadline."""
        now = self.clock()
        # Read first: with no message in the queue, a unit not yet read or given up belongs to none.
        lost = self.store.queue_length() == 0
        for job_id in sorted({name.split("/")[1] for name in self.store.names("jobs/")}):
            job = self.job(job_id)
            # A job whose time is up before the next pass goes now, so none is kept past its time.
            if job is None or self._expired(job, now + self.settings.supervisor_seconds):
                self.store.delete(f"jobs/{job_id}/")
                continue
            late = now - job.created > self.settings.deadline_seconds
            for page, engine, unit in self.units(job):
                if unit.state in FINAL:
                    continue
                if late or engine not in self.readers:
                    self.write_unit(job_id, page, engine, replace(unit, state=NOT_READ, why="closed" if late else "engine"))
                elif lost:
                    self.enqueue(job_id, page, engine, replace(unit, state=WAITING if unit.state == READING else unit.state))

    def _expired(self, job: Job, now: float) -> bool:
        return now - job.created >= self.settings.keep_seconds or (
            job.fetched is not None and now - job.fetched >= self.settings.fetched_seconds
        )


def _job_state(units: list[Unit]) -> str:
    if all(unit.state in FINAL for unit in units):
        return "done" if any(unit.state == READ for unit in units) else "failed"
    if all(unit.state == WAITING and unit.tries == 0 for unit in units):
        return "queued"
    return "running"


def _seconds_left(unit: Unit, seconds: float, now: float) -> float:
    """About how long the unit still takes: its engine's seconds per page, less what it has been read for already, plus
    the wait before a retry."""
    if unit.state in FINAL:
        return 0.0
    if unit.state == READING and unit.started is not None:
        return max(seconds * 0.05, seconds - (now - unit.started))
    if unit.state == RETRYING and unit.retry_at is not None:
        return seconds + max(0.0, unit.retry_at - now)
    return seconds
