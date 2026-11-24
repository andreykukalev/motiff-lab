"""Stand-ins for the engines in a scan job. A job reads each unit in a process of its own (unit_process.py), so these
are functions of a module, bound to a file with functools.partial, where each call writes a line for the test to read."""

import os
import subprocess
import time
from pathlib import Path

from app.recognise import Found, Reading, RecogniseError
from tests.test_merge import page


def calls(log: Path) -> list[str]:
    return log.read_text().split() if log.exists() else []


def _note(log: Path, what: object) -> None:
    with log.open("a") as file:
        file.write(f"{what}\n")


def reads(log: Path, image: Path, number: int) -> Reading:
    assert image.exists() and image.name == f"page-{number:02d}.png"
    _note(log, number)
    return Reading(page(str(number)), Found(staves=2, noteheads=10, bar_lines=3, space_px=12.0))


EVIDENCE = {"detector": {"name": "copisteria", "version": "1.3.4"}, "image": {"width": 2480, "height": 3508, "dpi": 300},
            "systems": [], "symbols": []}


def notes_threads(log: Path, image: Path, number: int) -> Reading:
    """Notes the limits its programs would inherit."""
    _note(log, f"{os.environ.get('OMP_NUM_THREADS')}|{os.environ.get('JAVA_TOOL_OPTIONS')}")
    return reads(log, image, number)


def fails(log: Path, image: Path, number: int) -> Reading:
    _note(log, number)
    raise RecogniseError(f"No music staves were found on page {number}.", Found(noteheads=4))


def reads_slowly(log: Path, seconds: float, image: Path, number: int) -> Reading:
    time.sleep(seconds)
    return reads(log, image, number)


def hangs(log: Path, image: Path, number: int) -> Reading:
    """Starts a program of its own, as homr is, and never answers."""
    program = subprocess.Popen(["sleep", "600"])
    _note(log, program.pid)
    program.wait()
    raise AssertionError("not reached")


def dies(log: Path, image: Path, number: int) -> Reading:
    _note(log, number)
    os._exit(1)
