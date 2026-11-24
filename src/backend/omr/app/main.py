"""Motiff Lab sheet music recognition: a small HTTP service around homr, run on the player's own computer.

POST /recognise takes one PDF or photo of printed music and returns MusicXML, with a report on each page in
the X-Page-Report header: numbers only, never the music (see pages.page_report). The form field `engine` picks the
engine that reads it, homr by default or another installed one (engines.py); GET /health lists them. POST /transcribe takes an
audio or video recording and returns the notes heard in it as MIDI (Transkun, decision record 0014).
Each upload lives in its own temporary folder for as long as the request, and the folder is deleted when
it ends. Nothing about the music is logged: the log has the request line and status only.

POST /jobs reads the pages of an upload in the background, each page by every engine in parallel (decision record
0035): GET /jobs/{id} gives its progress, GET /jobs/{id}/result the readings and each page's evidence, DELETE /jobs/{id}
removes it (jobs.py).
"""

from __future__ import annotations

import json
import os
import re
import tempfile
import threading
from collections.abc import AsyncIterator, Awaitable, Callable
from contextlib import asynccontextmanager
from pathlib import Path

from typing import Annotated

from fastapi import FastAPI, Form, Request, UploadFile
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse, Response
from starlette.concurrency import run_in_threadpool

from app import auth, azure_store, engines, job_store, job_worker, jobs, merge, pages, recognise, transcribe

# A scanned PDF or photo; the app allows the same 50 MB (Andrei, 2026-10-30).
MAX_UPLOAD_BYTES = int(os.environ.get("MAX_UPLOAD_MB", "50")) * 1024 * 1024
MAX_PAGES = int(os.environ.get("MAX_PAGES", "8"))

# A recording may be a video, so it is allowed to be larger; only its first minutes are heard. Two and
# a half minutes take Transkun about as long on two cores, well inside the four minutes Azure Container
# Apps waits for an answer (0014).
MAX_RECORDING_BYTES = int(os.environ.get("MAX_RECORDING_MB", "200")) * 1024 * 1024
MAX_RECORDING_SECONDS = int(os.environ.get("MAX_RECORDING_SECONDS", "150"))

# The service is meant for one player's own computer. Narrow the origins to the app's address with
# ALLOWED_ORIGINS="https://…" (comma separated) when the machine is shared. The hosted server (0014)
# also allows the app's preview addresses through ALLOWED_ORIGIN_REGEX.
ALLOWED_ORIGINS = [origin.strip() for origin in os.environ.get("ALLOWED_ORIGINS", "*").split(",") if origin.strip()]
ALLOWED_ORIGIN_REGEX = os.environ.get("ALLOWED_ORIGIN_REGEX") or None

MUSICXML_TYPE = "application/vnd.recordare.musicxml+xml"
MIDI_TYPE = "audio/midi"

# Where the jobs wait and lie on the player's own computer (job_store.FolderStore); a restarted server goes on with them.
JOBS_FOLDER = Path(os.environ.get("JOBS_FOLDER") or Path(tempfile.gettempdir()) / "motiff-scan-jobs")


def jobs_store_from_environment() -> job_store.Store:
    """Hosted, with JOBS_STORAGE_ACCOUNT set, the jobs lie in that Azure Storage account's queue (JOBS_QUEUE) and blob
    container (JOBS_CONTAINER), signed in as the managed identity AZURE_CLIENT_ID names; otherwise in JOBS_FOLDER."""
    account = os.environ.get("JOBS_STORAGE_ACCOUNT")
    if not account:
        return job_store.FolderStore(JOBS_FOLDER)
    return azure_store.AzureStore.for_account(
        account,
        os.environ.get("JOBS_QUEUE") or "scan-units",
        os.environ.get("JOBS_CONTAINER") or "scan-jobs",
        os.environ.get("AZURE_CLIENT_ID") or None,
    )


def _verifier_from_environment() -> Callable[[str | None, str | None], str] | None:
    """Hosted (0014), a page is read only for a signed-in Pro player; on the player's own computer, for anyone."""
    authority, audience = os.environ.get("AUTH_AUTHORITY"), os.environ.get("AUTH_AUDIENCE")
    return auth.TokenVerifier(authority, audience).verify if authority and audience else None


def create_app(
    recognise_page: Callable[[Path, int], recognise.Reading] = recognise.recognise_page,
    transcribe_recording: Callable[[Path, int], transcribe.Transcription] = transcribe.transcribe,
    allowed_origins: list[str] = ALLOWED_ORIGINS,
    allowed_origin_regex: str | None = ALLOWED_ORIGIN_REGEX,
    verify: Callable[[str | None, str | None], str] | None = _verifier_from_environment(),
    other_engines: list[engines.Engine] | None = None,
    jobs_store: job_store.Store | None = None,
    job_settings: jobs.Settings | None = None,
) -> FastAPI:
    """other_engines: the engines besides homr (recognise_page), by default those installed (engines.others).
    jobs_store and job_settings: where scan jobs lie and how they are worked, by default as the environment says
    (jobs_store_from_environment)."""
    readers = {
        engine.name: engine
        for engine in [
            engines.Engine(recognise.ENGINE, recognise.engine_version, recognise_page, 75.0),
            *(engines.others() if other_engines is None else other_engines),
        ]
    }
    settings = job_settings or jobs.Settings.from_environment(len(readers))
    work = jobs.Jobs(jobs_store or jobs_store_from_environment(), readers, settings)
    runner = job_worker.Runner(work)

    @asynccontextmanager
    async def lifespan(_: FastAPI) -> AsyncIterator[None]:
        runner.start_threads()
        try:
            yield
        finally:
            runner.stop_threads()

    app = FastAPI(title="Motiff Lab sheet music recognition", docs_url=None, redoc_url=None, openapi_url=None, lifespan=lifespan)
    app.add_middleware(
        CORSMiddleware,
        allow_origins=allowed_origins,
        allow_origin_regex=allowed_origin_regex,
        allow_methods=["GET", "POST", "DELETE"],
        allow_headers=["*"],
        expose_headers=["X-Engine", "X-Pages", "X-Page-Report", "X-Seconds", "X-Cut", "Location"],
        max_age=600,
    )

    # The engines and Transkun use every core, so one upload is read at a time and a second waits its turn.
    busy = threading.Lock()

    @app.middleware("http")
    async def private_network(request: Request, call_next):  # type: ignore[no-untyped-def]
        # Chrome asks before a page served from the internet may call a server on this computer
        # (Private Network Access); the preflight carries this header, and the answer must allow it.
        response = await call_next(request)
        if request.headers.get("access-control-request-private-network") == "true":
            response.headers["Access-Control-Allow-Private-Network"] = "true"
        return response

    @app.get("/health")
    def health() -> dict[str, object]:
        # "engine" and "version" name the engine an upload without an `engine` field gets, for apps before the choice.
        return {
            "engine": engines.DEFAULT,
            "version": readers[engines.DEFAULT].version(),
            "engines": [_engine_entry(engine, work) for engine in readers.values()],
            "maxPages": MAX_PAGES,
            "signIn": verify is not None,
            "jobs": work.figures(),
            "transcribe": {
                "engine": transcribe.ENGINE,
                "version": transcribe.engine_version(),
                "maxMegabytes": MAX_RECORDING_BYTES // (1024 * 1024),
                "maxSeconds": MAX_RECORDING_SECONDS,
            },
        }

    async def signed_in(request: Request) -> tuple[str | None, Response | None]:
        """The signed-in player's id, or a refusal: 401 without a valid sign-in, 403 without Pro, on the hosted
        server. On the player's own computer nothing is checked and there is no player."""
        if verify is None:
            return None, None
        try:
            player = await run_in_threadpool(verify, request.headers.get("authorization"), request.headers.get(auth.PLAN_HEADER))
        except auth.AuthError as error:
            return None, _problem(401, str(error))
        except auth.NotProError as error:
            return None, _problem(403, str(error), code="pro")
        return player, None

    @app.post("/recognise")
    async def recognise_upload(request: Request, file: UploadFile, engine: Annotated[str | None, Form()] = None) -> Response:
        if (refusal := (await signed_in(request))[1]) is not None:
            return refusal

        reader = readers.get((engine or engines.DEFAULT).strip().lower())
        if reader is None:
            return _no_engine(engine or "", readers)

        upload = await _upload(file)
        if isinstance(upload, Response):
            return upload

        reports: list[dict[str, int | float]] = []

        def read(page: pages.Page, number: int) -> bytes:
            try:
                reading = reader.read(page.path, number)
            except recognise.RecogniseError as error:
                reports.append(pages.page_report(number, page.look, error.found, None))
                raise
            reports.append(pages.page_report(number, page.look, reading.found, reading.musicxml))
            return reading.musicxml

        def run() -> tuple[bytes, int]:
            with busy, tempfile.TemporaryDirectory(prefix="motiff-scan-") as folder:
                written = pages.write_pages(upload, Path(folder), MAX_PAGES)
                results = [read(page, number) for number, page in enumerate(written, 1)]
                return merge.merge(results), len(written)

        try:
            score, count = await run_in_threadpool(run)
        except (pages.PageError, merge.MergeError, recognise.RecogniseError) as error:
            # The pages read so far, and the one that could not be read, say why.
            return _problem(422, str(error), headers=_report_header(reports))

        return Response(
            content=score,
            media_type=MUSICXML_TYPE,
            headers={
                "X-Engine": f"{reader.name} {reader.version()}",
                "X-Pages": str(count),
                "Cache-Control": "no-store",
                **_report_header(reports),
            },
        )

    @app.post("/transcribe")
    async def transcribe_upload(request: Request, file: UploadFile) -> Response:
        if (refusal := (await signed_in(request))[1]) is not None:
            return refusal

        def run() -> transcribe.Transcription | JSONResponse:
            with busy, tempfile.TemporaryDirectory(prefix="motiff-transcribe-") as folder:
                # The upload keeps no name of the player's: ffmpeg tells the format from the content.
                source = Path(folder) / "upload"
                with source.open("wb") as target:
                    copied = _copy(file.file, target, MAX_RECORDING_BYTES + 1)
                if copied == 0:
                    return _problem(400, "The file is empty.")
                if copied > MAX_RECORDING_BYTES:
                    return _problem(413, f"The recording is larger than {MAX_RECORDING_BYTES // (1024 * 1024)} MB. Record a shorter passage, or only the sound.")
                return transcribe_recording(source, MAX_RECORDING_SECONDS)

        try:
            result = await run_in_threadpool(run)
        except transcribe.TranscribeError as error:
            return _problem(422, str(error))
        if isinstance(result, JSONResponse):
            return result

        return Response(
            content=result.midi,
            media_type=MIDI_TYPE,
            headers={
                "X-Engine": f"{transcribe.ENGINE} {transcribe.engine_version()}",
                "X-Seconds": f"{result.seconds:.1f}",
                "X-Cut": "true" if result.cut else "false",
                "Cache-Control": "no-store",
            },
        )

    _add_job_routes(app, work, readers, signed_in)

    return app


def _add_job_routes(
    app: FastAPI,
    work: jobs.Jobs,
    readers: dict[str, engines.Engine],
    signed_in: Callable[[Request], Awaitable[tuple[str | None, Response | None]]],
) -> None:
    """POST /jobs, GET /jobs/{id}, GET /jobs/{id}/result and DELETE /jobs/{id} (decision record 0035)."""

    @app.post("/jobs")
    async def create_job(
        request: Request,
        file: UploadFile,
        pages_: Annotated[str | None, Form(alias="pages")] = None,
        engines_: Annotated[str | None, Form(alias="engines")] = None,
    ) -> Response:
        player, refusal = await signed_in(request)
        if refusal is not None:
            return refusal
        chosen = list(dict.fromkeys(_names(engines_))) or list(readers)
        if unknown := [name for name in chosen if name not in readers]:
            return _no_engine(unknown[0], readers)
        try:
            numbers = sorted(set(int(number) for number in _names(pages_))) or None
        except ValueError:
            return _problem(400, "Pages are numbers from 1, separated by commas.", code="pages")
        upload = await _upload(file)
        if isinstance(upload, Response):
            return upload

        def submit() -> str:
            with tempfile.TemporaryDirectory(prefix="motiff-job-") as folder:
                written = pages.write_pages(upload, Path(folder), MAX_PAGES, numbers)
                return work.submit(list(zip(numbers or range(1, len(written) + 1), written)), chosen, player)

        try:
            job_id = await run_in_threadpool(submit)
        except pages.PageError as error:
            return _problem(422, str(error))
        status = f"/jobs/{job_id}"
        return JSONResponse(status_code=202, content={"id": job_id, "status": status}, headers={"Location": status, "Cache-Control": "no-store"})

    async def players_job(request: Request, job_id: str) -> jobs.Job | Response:
        player, refusal = await signed_in(request)
        if refusal is not None:
            return refusal
        job = await run_in_threadpool(work.find, job_id, player)
        # Another player's job is answered as one that does not exist, so an id tells nothing about anyone else's.
        return job if job is not None else _problem(404, "There is no such scan job. It may have been deleted, or made more than a day ago.", code="job")

    @app.get("/jobs/{job_id}")
    async def job_status(request: Request, job_id: str) -> Response:
        job = await players_job(request, job_id)
        if isinstance(job, Response):
            return job
        return JSONResponse(await run_in_threadpool(work.status, job), headers={"Cache-Control": "no-store"})

    @app.get("/jobs/{job_id}/result")
    async def job_result(request: Request, job_id: str) -> Response:
        job = await players_job(request, job_id)
        if isinstance(job, Response):
            return job
        try:
            result = await run_in_threadpool(work.fetch_result, job)
        except jobs.NotFinished:
            return _problem(409, "The pages are still being read.", code="running")
        return JSONResponse(result, headers={"Cache-Control": "no-store"})

    @app.delete("/jobs/{job_id}")
    async def delete_job(request: Request, job_id: str) -> Response:
        job = await players_job(request, job_id)
        if isinstance(job, Response):
            return job
        await run_in_threadpool(work.delete, job)
        return Response(status_code=204)


def _engine_entry(engine: engines.Engine, work: jobs.Jobs) -> dict[str, object]:
    seconds, measured = work.seconds_per_page(engine.name)
    return {"engine": engine.name, "version": engine.version(), "secondsPerPage": seconds, "pagesMeasured": measured}


async def _upload(file: UploadFile) -> pages.Upload | Response:
    """The uploaded PDF or photo, or why it is refused: empty, too large, or not a PDF, PNG or JPEG."""
    data = await file.read(MAX_UPLOAD_BYTES + 1)
    if len(data) == 0:
        return _problem(400, "The file is empty.")
    if len(data) > MAX_UPLOAD_BYTES:
        return _problem(413, f"The file is larger than {MAX_UPLOAD_BYTES // (1024 * 1024)} MB.")
    try:
        return pages.sniff(data)
    except pages.PageError as error:
        return _problem(415, str(error))


def _no_engine(name: str, readers: dict[str, engines.Engine]) -> JSONResponse:
    return _problem(400, f"This scan server has no engine called “{name[:40]}”. It has {', '.join(readers)}.", code="engine")


def _names(field: str | None) -> list[str]:
    """A form field's comma-separated values, trimmed and in lower case."""
    return [part.strip().lower() for part in re.split(r"[,\s]+", field or "") if part.strip()]


def _report_header(reports: list[dict[str, int | float]]) -> dict[str, str]:
    return {"X-Page-Report": json.dumps(reports, separators=(",", ":"))} if reports else {}


def _copy(source, target, limit: int) -> int:  # type: ignore[no-untyped-def]
    """Copies at most limit bytes and returns how many were copied, so an oversized upload stops early."""
    copied = 0
    while copied < limit and (chunk := source.read(min(1024 * 1024, limit - copied))):
        target.write(chunk)
        copied += len(chunk)
    return copied


def _problem(status: int, message: str, code: str | None = None, headers: dict[str, str] | None = None) -> JSONResponse:
    content = {"message": message} if code is None else {"message": message, "code": code}
    return JSONResponse(status_code=status, content=content, headers={"Cache-Control": "no-store", **(headers or {})})


app = create_app()
