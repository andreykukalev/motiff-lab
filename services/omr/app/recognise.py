"""Runs homr, unmodified, on one page image at a time.

homr is called as its own command line program, as published on PyPI, so this service only hands it
files and reads back the MusicXML it writes next to each image, and the counts it writes to its log
(stderr): how many staves, noteheads and bar lines it found and how tall a notehead is. The log is read
for those counts only and never passed on, since it also holds the title homr read.
"""

from __future__ import annotations

import re
import subprocess
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from importlib import metadata
from pathlib import Path

# A page takes 20 to 40 s on 4 CPU cores; a slow machine gets plenty of room before it is given up.
PAGE_TIMEOUT_SECONDS = 300

# homr 0.7.0's log lines (homr/main.py, detect_staffs_in_image), one count each.
_COUNTS = {
    "staves": re.compile(r"^Found (\d+) staffs$"),
    "noteheads": re.compile(r"^Found (\d+) noteheads$"),
    "bar_lines": re.compile(r"^Found (\d+) bar lines$"),
    "space_px": re.compile(r"^Average note head height: ([0-9]+(?:\.[0-9]+)?)$"),
}


@dataclass(frozen=True)
class Found:
    """What homr found on a page, from its log; a count homr did not reach is 0.

    A notehead is about one staff space tall. Its height is measured in homr's own working copy of the
    page, which homr crops to the paper and scales to 1920 pixels wide."""

    staves: int = 0
    noteheads: int = 0
    bar_lines: int = 0
    space_px: float = 0.0


@dataclass(frozen=True)
class Reading:
    musicxml: bytes
    found: Found = field(default_factory=Found)


class RecogniseError(RuntimeError):
    """homr could not read a page. The message is plain words for the player."""

    def __init__(self, message: str, found: Found | None = None) -> None:
        super().__init__(message)
        self.found = found or Found()


def engine_version() -> str:
    try:
        return metadata.version("homr")
    except metadata.PackageNotFoundError:
        return "not installed"


def recognise_page(image: Path, page_number: int) -> Reading:
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

    found = read_log(result.stderr)
    output = image.with_suffix(".musicxml")
    if result.returncode != 0 or not output.exists():
        # homr's own messages name the step that failed, never the music, so the reason is safe to show.
        reason = _last_line(result.stderr)
        if "No noteheads found" in reason or "No staffs found" in reason:
            raise RecogniseError(
                f"No music staves were found on page {page_number}. Check that the page is sharp and fills the photo.", found
            )
        raise RecogniseError(f"Page {page_number} could not be read.", found)
    return Reading(output.read_bytes(), found)


def read_log(stderr: bytes) -> Found:
    counts: dict[str, float] = {}
    for line in stderr.decode("utf-8", "replace").splitlines():
        for name, pattern in _COUNTS.items():
            if name not in counts and (match := pattern.match(line.strip())):
                counts[name] = float(match.group(1))
    return Found(
        staves=int(counts.get("staves", 0)),
        noteheads=int(counts.get("noteheads", 0)),
        bar_lines=int(counts.get("bar_lines", 0)),
        space_px=round(counts.get("space_px", 0.0), 1),
    )


def count_measures(musicxml: bytes) -> int:
    """Measures on the page: the most that any part has; 0 when the MusicXML cannot be read."""
    try:
        root = ET.fromstring(musicxml)
    except ET.ParseError:
        return 0
    return max((len(part.findall("measure")) for part in root.findall("part")), default=0)


def _last_line(stderr: bytes) -> str:
    lines = [line for line in stderr.decode("utf-8", "replace").splitlines() if line.strip()]
    return lines[-1] if lines else ""
