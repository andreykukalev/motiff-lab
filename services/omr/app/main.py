"""Motiff Lab sheet music recognition: a small HTTP service around homr, run on the player's own computer.

POST /recognise takes one PDF or photo of printed music and returns MusicXML. Each upload lives in
its own temporary folder for as long as the request, and the folder is deleted when it ends. Nothing
about the music is logged: the log has the request line and status only.
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

from app import merge, pages, recognise

MAX_UPLOAD_BYTES = 20 * 1024 * 1024
MAX_PAGES = int(os.environ.get("MAX_PAGES", "8"))

# The service is meant for one player's own computer. Narrow the origins to the app's address with
# ALLOWED_ORIGINS="https://…" (comma separated) when the machine is shared.
ALLOWED_ORIGINS = [origin.strip() for origin in os.environ.get("ALLOWED_ORIGINS", "*").split(",") if origin.strip()]

MUSICXML_TYPE = "application/vnd.recordare.musicxml+xml"


def create_app(recognise_page: Callable[[Path, int], bytes] = recognise.recognise_page) -> FastAPI:
    app = FastAPI(title="Motiff Lab sheet music recognition", docs_url=None, redoc_url=None, openapi_url=None)
    app.add_middleware(
        CORSMiddleware,
        allow_origins=ALLOWED_ORIGINS,
        allow_methods=["GET", "POST"],
        allow_headers=["*"],
        expose_headers=["X-Engine", "X-Pages"],
        max_age=600,
    )

    # homr uses every core for one page, so pages are read one at a time and a second upload waits its turn.
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
        return {"engine": "homr", "version": recognise.engine_version(), "maxPages": MAX_PAGES}

    @app.post("/recognise")
    async def recognise_upload(file: UploadFile) -> Response:
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

    return app


def _problem(status: int, message: str) -> JSONResponse:
    return JSONResponse(status_code=status, content={"message": message}, headers={"Cache-Control": "no-store"})


app = create_app()
