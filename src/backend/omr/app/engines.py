"""The engines a page can be read with: homr, always, and copisteria, Audiveris and Transcoda where they are installed
(the Docker image has them all, decision record 0035) and the server setting ENGINES does not switch them off.

An upload names its engine in the form field `engine`; without it, homr reads it, as before there was a choice.
Every engine reads the same page images (pages.py turns and renders them first) and their MusicXML is joined the same
way (merge.py), so only the reading itself differs. A scan job (jobs.py) reads its pages with every engine listed here
unless it names some; adding an engine is adding it to this list.

ENGINES (comma separated, such as `homr,copisteria,audiveris`) names the engines this server reads with, of those
installed; unset, it reads with all of them. homr cannot be switched off: `/recognise` reads with it by default.
Transcoda's weights are non-commercial (CC BY-NC 4.0): `ENGINES` without `transcoda` leaves it out.
"""

from __future__ import annotations

import os
from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from app import audiveris_reader, copisteria_reader, recognise, transcoda_reader

DEFAULT = recognise.ENGINE


@dataclass(frozen=True)
class Engine:
    name: str
    version: Callable[[], str]
    read: Callable[[Path, int], recognise.Reading]  # a job runs it in a process of its own, so it is picklable (unit_process.py)
    # About how long it takes to read a page here, until this server has measured it (jobs.py, the estimate).
    seconds: float = 60.0
    # The page's evidence from what the engine left beside the page after reading it, in a scan job (copisteria's).
    evidence: Callable[[Path], dict[str, Any] | None] | None = None


KNOWN = (recognise.ENGINE, copisteria_reader.ENGINE, audiveris_reader.ENGINE, transcoda_reader.ENGINE)


def others(chosen: str | None = None) -> list[Engine]:
    """The engines besides homr that this server has installed and reads with (chosen: ENGINES by default); a name it
    does not know stops the server at start (ValueError). The figures are about the seconds a page takes on 4 CPU cores
    with the four engines side by side, until this server has measured its own (jobs.py)."""
    names = _names(os.environ.get("ENGINES") if chosen is None else chosen)
    if unknown := sorted((names or set()) - set(KNOWN)):
        raise ValueError(f"ENGINES names {', '.join(unknown)}, which this server does not have. It has {', '.join(KNOWN)}.")
    installed = [
        (copisteria_reader.is_installed, Engine(
            copisteria_reader.ENGINE, copisteria_reader.engine_version, copisteria_reader.recognise_page, 90.0, copisteria_reader.page_evidence
        )),
        (audiveris_reader.is_installed, Engine(audiveris_reader.ENGINE, audiveris_reader.engine_version, audiveris_reader.recognise_page, 70.0)),
        (transcoda_reader.is_installed, Engine(transcoda_reader.ENGINE, transcoda_reader.engine_version, transcoda_reader.recognise_page, 140.0)),
    ]
    return [engine for is_installed, engine in installed if (names is None or engine.name in names) and is_installed()]


def _names(setting: str | None) -> set[str] | None:
    names = {name.strip().lower() for name in (setting or "").split(",") if name.strip()}
    return names or None
