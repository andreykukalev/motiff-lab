"""The engines a page can be read with: homr, always, and copisteria where it is installed (the Docker image has it).

An upload names its engine in the form field `engine`; without it, homr reads it, as before there was a choice.
Both engines read the same page images (pages.py turns and renders them first) and their MusicXML is joined the same
way (merge.py), so only the reading itself differs.
"""

from __future__ import annotations

from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path

from app import copisteria_reader, recognise

DEFAULT = recognise.ENGINE


@dataclass(frozen=True)
class Engine:
    name: str
    version: Callable[[], str]
    read: Callable[[Path, int], recognise.Reading]


def others() -> list[Engine]:
    """The engines besides homr that this server has installed."""
    if copisteria_reader.is_installed():
        return [Engine(copisteria_reader.ENGINE, copisteria_reader.engine_version, copisteria_reader.recognise_page)]
    return []
