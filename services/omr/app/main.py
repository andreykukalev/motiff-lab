"""Motiff Lab sheet music recognition: a small HTTP service around homr, run on the player's own computer.

POST /recognise takes one PDF or photo of printed music and returns MusicXML. POST /transcribe takes an
audio or video recording and returns the notes heard in it as MIDI (Transkun, decision record 0014).
Each upload lives in its own temporary folder for as long as the request, and the folder is deleted when
it ends. Nothing about the music is logged: the log has the request line and status only.
"""

from __future__ import annotations

import os
import tempfile
import threading
from collections.abc import Callable
from pathlib import Path

from fastapi import FastAPI, Request, UploadFile
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse, Response
from starlette.concurrency import run_in_threadpool

from app import auth, merge, pages, recognise, transcribe

MAX_UPLOAD_BYTES = 20 * 1024 * 1024
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


def _verifier_from_environment() -> Callable[[str | None], None] | None:
    """Hosted (0014), a page is read only for a signed-in player; on the player's own computer, for anyone."""
    authority, audience = os.environ.get("AUTH_AUTHORITY"), os.environ.get("AUTH_AUDIENCE")
    return auth.TokenVerifier(authority, audience).verify if authority and audience else None


def create_app(
    recognise_page: Callable[[Path, int], bytes] = recognise.recognise_page,
    transcribe_recording: Callable[[Path, int], transcribe.Transcription] = transcribe.transcribe,
    allowed_origins: list[str] = ALLOWED_ORIGINS,
    allowed_origin_regex: str | None = ALLOWED_ORIGIN_REGEX,
    verify: Callable[[str | None], None] | None = _verifier_from_environment(),
) -> FastAPI:
    app = FastAPI(title="Motiff Lab sheet music recognition", docs_url=None, redoc_url=None, openapi_url=None)
    app.add_middleware(
        CORSMiddleware,
        allow_origins=allowed_origins,
        allow_origin_regex=allowed_origin_regex,
        allow_methods=["GET", "POST"],
        allow_headers=["*"],
        expose_headers=["X-Engine", "X-Pages", "X-Seconds", "X-Cut"],
        max_age=600,
    )

    # homr and Transkun use every core, so one upload is read at a time and a second waits its turn.
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
        return {
            "engine": "homr",
            "version": recognise.engine_version(),
            "maxPages": MAX_PAGES,
            "signIn": verify is not None,
            "transcribe": {
                "engine": transcribe.ENGINE,
                "version": transcribe.engine_version(),
                "maxMegabytes": MAX_RECORDING_BYTES // (1024 * 1024),
                "maxSeconds": MAX_RECORDING_SECONDS,
            },
        }

    async def refused(request: Request) -> Response | None:
        """401 when the hosted server is asked without a valid sign-in; None when it may go on."""
        if verify is None:
            return None
        try:
            await run_in_threadpool(verify, request.headers.get("authorization"))
        except auth.AuthError as error:
            return _problem(401, str(error))
        return None

    @app.post("/recognise")
    async def recognise_upload(request: Request, file: UploadFile) -> Response:
        if (refusal := await refused(request)) is not None:
            return refusal

        data = await file.read(MAX_UPLOAD_BYTES + 1)
        if len(data) == 0:
            return _problem(400, "The file is empty.")
        if len(data) > MAX_UPLOAD_BYTES:
            return _problem(413, f"The file is larger than {MAX_UPLOAD_BYTES // (1024 * 1024)} MB.")

        try:
            upload = pages.sniff(data)
        except pages.PageError as error:
            return _problem(415, str(error))

        def run() -> tuple[bytes, int]:
            with busy, tempfile.TemporaryDirectory(prefix="motiff-scan-") as folder:
                paths = pages.write_pages(upload, Path(folder), MAX_PAGES)
                results = [recognise_page(path, number) for number, path in enumerate(paths, 1)]
                return merge.merge(results), len(paths)

        try:
            score, count = await run_in_threadpool(run)
        except (pages.PageError, merge.MergeError, recognise.RecogniseError) as error:
            return _problem(422, str(error))
        finally:
            del data

        return Response(
            content=score,
            media_type=MUSICXML_TYPE,
            headers={"X-Engine": f"homr {recognise.engine_version()}", "X-Pages": str(count), "Cache-Control": "no-store"},
        )

    @app.post("/transcribe")
    async def transcribe_upload(request: Request, file: UploadFile) -> Response:
        if (refusal := await refused(request)) is not None:
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

    return app


def _copy(source, target, limit: int) -> int:  # type: ignore[no-untyped-def]
    """Copies at most limit bytes and returns how many were copied, so an oversized upload stops early."""
    copied = 0
    while copied < limit and (chunk := source.read(min(1024 * 1024, limit - copied))):
        target.write(chunk)
        copied += len(chunk)
    return copied


def _problem(status: int, message: str) -> JSONResponse:
    return JSONResponse(status_code=status, content={"message": message}, headers={"Cache-Control": "no-store"})


app = create_app()
