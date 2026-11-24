"""Runs copisteria, unmodified, on one page image at a time: a second engine beside homr, for comparing them.

copisteria (KobiMusic, Apache-2.0, code and the copista-28m weights) is called as its own command line program,
`python -m copisteria.pipeline`, as published at its pinned commit (Dockerfile). It writes, in a folder of its own
next to the page, `<page>.musicxml`, `<page>.reading.json` (each symbol's reading with its probability, and the
readings the context changed) and a viewer page; beside the page it caches its detections. Only the MusicXML is
passed on; the reading is read for the page report's counts and never passed on. The detections give the page
report's staff space and, after a scan job's reading, the symbols of the page's evidence (page_evidence), their
classes and places only. The whole folder is deleted with the upload.
"""

from __future__ import annotations

import json
import sys
from importlib import metadata
from pathlib import Path
from typing import Any

from app import evidence, recognise
from app.recognise import Found, Reading, RecogniseError

ENGINE = "copisteria"

# The page report's spacePx is about one staff space in a working copy 1920 pixels wide, as homr's is.
_REPORT_WIDTH = 1920


def engine_version() -> str:
    try:
        return metadata.version("copisteria")
    except metadata.PackageNotFoundError:
        return "not installed"


def is_installed() -> bool:
    try:
        metadata.version("copisteria")
    except metadata.PackageNotFoundError:
        return False
    return True


def recognise_page(image: Path, page_number: int) -> Reading:
    out = image.parent / f"{image.stem}-copisteria"
    result = recognise.run([sys.executable, "-m", "copisteria.pipeline", str(image), "--out", str(out)], image.parent, page_number)

    found = read_found(out / f"{image.stem}.reading.json", image)
    output = out / f"{image.stem}.musicxml"
    if result.returncode != 0 or not output.exists():
        # copisteria's messages are Python's, which may quote the page's text: none of it is passed on.
        raise RecogniseError(f"Page {page_number} could not be read.", found)
    if found.staves == 0:
        raise RecogniseError(
            f"No music staves were found on page {page_number}. Check that the page is sharp and fills the photo.", found
        )
    return Reading(output.read_bytes(), found)


def page_evidence(image: Path) -> dict[str, Any] | None:
    """The page's evidence (evidence.py), from the page and the detections copisteria cached beside it when it read
    it; for a scan job only. None when it cannot be made: the reading is kept without it."""
    try:
        return evidence.of_page(image, detections(image), {"name": ENGINE, "version": engine_version()})
    except (ValueError, IndexError, TypeError, OSError):
        return None


def detections(image: Path) -> list[dict[str, Any]]:
    """The symbols copisteria's detector found on the page, from the file it caches beside it; none without one."""
    cached = sorted(image.parent.glob(f"{image.stem}.dets_*.json"))
    try:
        found = json.loads(cached[0].read_text(encoding="utf-8")) if cached else []
    except (OSError, ValueError):
        return []
    return found if isinstance(found, list) else []


def read_found(reading: Path, image: Path) -> Found:
    """The counts of the page report, from copisteria's reading and detections; 0 for what it did not write."""
    staves, noteheads, bar_lines = 0, 0, 0
    try:
        data = json.loads(reading.read_text(encoding="utf-8"))
        staves = int(data.get("staves", 0))
        for token in data.get("tokens", {}).values():
            name = str(token.get("cls", {}).get("v", ""))
            if token.get("real", {}).get("p", 1.0) < 0.5:
                continue
            if name.startswith("note_"):
                noteheads += 1
            elif name.startswith("barLine"):
                bar_lines += 1
    except (OSError, ValueError, AttributeError, TypeError):
        pass
    return Found(staves=staves, noteheads=noteheads, bar_lines=bar_lines, space_px=space_px(image))


def space_px(image: Path) -> float:
    """About one staff space, scaled to a page 1920 pixels wide: a measure box copisteria detects is four staff
    spaces tall, and the median of the confident ones is taken, as copisteria itself does (detect.measure_space)."""
    from PIL import Image

    try:
        with Image.open(image) as page:
            width = page.width
        heights = sorted(
            box["xyxy"][3] - box["xyxy"][1] for box in detections(image) if box.get("cls") == "measure" and box.get("conf", 0) >= 0.5
        )
    except (OSError, ValueError, KeyError, IndexError, TypeError):
        return 0.0
    if not heights or width <= 0:
        return 0.0
    return round(heights[len(heights) // 2] / 4 * _REPORT_WIDTH / width, 1)
