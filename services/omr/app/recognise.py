"""Runs homr, unmodified, on one page image at a time.

homr is called as its own command line program, as published on PyPI, so this service only hands it
files and reads back the MusicXML it writes next to each image.
"""

from __future__ import annotations

import subprocess
from importlib import metadata
from pathlib import Path

# A page takes 20 to 40 s on 4 CPU cores; a slow machine gets plenty of room before it is given up.
PAGE_TIMEOUT_SECONDS = 300


class RecogniseError(RuntimeError):
    """homr could not read a page. The message is plain words for the player."""


def engine_version() -> str:
    try:
        return metadata.version("homr")
    except metadata.PackageNotFoundError:
        return "not installed"


def recognise_page(image: Path, page_number: int) -> bytes:
    try:
        result = subprocess.run(
            ["homr", "--gpu", "no", str(image)],
            cwd=image.parent,
            capture_output=True,
            timeout=PAGE_TIMEOUT_SECONDS,
            check=False,
        )
    except subprocess.TimeoutExpired as error:
        raise RecogniseError(f"Reading page {page_number} took too long and was stopped.") from error

    output = image.with_suffix(".musicxml")
    if result.returncode != 0 or not output.exists():
        # homr's own messages name the step that failed, never the music, so the reason is safe to show.
        reason = _last_line(result.stderr)
        if "No noteheads found" in reason or "No staffs found" in reason:
            raise RecogniseError(f"No music staves were found on page {page_number}. Check that the page is sharp and fills the photo.")
        raise RecogniseError(f"Page {page_number} could not be read.")
    return output.read_bytes()


def _last_line(stderr: bytes) -> str:
    lines = [line for line in stderr.decode("utf-8", "replace").splitlines() if line.strip()]
    return lines[-1] if lines else ""
