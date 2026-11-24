"""Runs Audiveris, unmodified, on one page image at a time: the third engine for scans (decision record 0035).

Audiveris 5.11 (AGPL-3.0) is a Java program, run as published in its Linux package (Dockerfile), with the Java runtime
that comes in it, in batch mode: `Audiveris -batch -export -output <folder> <page>`. It writes, in a folder of its own
next to the page, its book (`.omr`), its log, and the score as compressed MusicXML (`.mxl`), one file per movement
when it splits the page into movements; its settings, caches and global log go into that folder too. Only the
MusicXML is passed on, the movements joined in order; the whole folder is deleted with the upload. Its log is never read, since it names the title read. Audiveris has no OCR
languages here, so it reads no text: the page's words are neither read nor kept.
"""

from __future__ import annotations

import os
import re
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

from app import merge, recognise
from app.recognise import Reading, RecogniseError

ENGINE = "audiveris"

HOME = Path(os.environ.get("AUDIVERIS_HOME", "/opt/audiveris"))

# The package's launcher names its version among the Java options.
_VERSION = re.compile(r"-Djpackage\.app-version=(\S+)")
_MOVEMENT = re.compile(r"\.mvt(\d+)$")


def program() -> Path:
    return HOME / "bin" / "Audiveris"


def is_installed() -> bool:
    return program().is_file()


def engine_version() -> str:
    try:
        launcher = (HOME / "lib" / "app" / "Audiveris.cfg").read_text(encoding="utf-8")
    except OSError:
        return "not installed"
    match = _VERSION.search(launcher)
    return match.group(1) if match else "unknown"


def recognise_page(image: Path, page_number: int) -> Reading:
    out = image.parent / f"{image.stem}-audiveris"
    # Its settings, caches and logs go into the upload's folder too, so nothing of a page outlives it.
    own = {**os.environ, **{name: str(out / "home" / name.lower()) for name in ("XDG_CONFIG_HOME", "XDG_CACHE_HOME", "XDG_DATA_HOME")}}
    command = [str(program()), "-batch", "-export", "-output", str(out), str(image)]
    result = recognise.run(command, image.parent, page_number, own)
    movements = sorted(out.glob("*.mxl"), key=_movement_order)
    if result.returncode != 0 or not movements:
        # Audiveris writes no score for a page where it found no staves, and its messages may quote the page's text.
        raise RecogniseError(f"Page {page_number} could not be read.")
    try:
        scores = [unpack(movement) for movement in movements]
        return Reading(merge.merge(scores, new_page=False))
    except (zipfile.BadZipFile, KeyError, ET.ParseError, merge.MergeError) as error:
        raise RecogniseError(f"Page {page_number} could not be read.") from error


def unpack(mxl: Path) -> bytes:
    """The score in a compressed MusicXML file: the root file its container names."""
    with zipfile.ZipFile(mxl) as archive:
        container = ET.fromstring(archive.read("META-INF/container.xml"))
        root = next((element.get("full-path") for element in container.iter() if element.tag.endswith("rootfile")), None)
        return archive.read(root or "")  # KeyError without one


def _movement_order(path: Path) -> tuple[int, str]:
    match = _MOVEMENT.search(path.stem)
    return (int(match.group(1)) if match else 0, path.name)
