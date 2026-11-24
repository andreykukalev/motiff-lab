"""Runs Transcoda, unmodified, on one page image at a time: the fourth engine for scans (decision record 0035).

Transcoda 2.1 (Heinrich Heine University Düsseldorf; code AGPL-3.0, weights CC BY-NC 4.0) is run from its source at
the release's commit, in an environment of its own (Dockerfile), as its README runs one page:
`python scripts/infer.py --image <page> --out <page>.krn`, with its released weights and its grammar-constrained
decoding, offline. It writes the music as Humdrum **kern in its compact form (no spine header or terminator, a bare
`=` for a bar line across every spine, empty fields for null tokens). This module makes standard Humdrum of it,
reads that with music21 and converter21's Humdrum reader, and writes MusicXML: one part, its staves numbered top down,
as the other engines write a piano page. Only the MusicXML is passed on; the kern file is deleted with the upload.
"""

from __future__ import annotations

import os
import re
import xml.etree.ElementTree as ET
from pathlib import Path

from app import recognise
from app.recognise import Reading, RecogniseError

ENGINE = "transcoda"

HOME = Path(os.environ.get("TRANSCODA_HOME", "/opt/transcoda"))

_VERSION = re.compile(r'^version\s*=\s*"([^"]+)"', re.MULTILINE)
# What follows <staff> in a MusicXML note, so a staff number goes before them.
_AFTER_STAFF = ("beam", "notations", "lyric", "play", "listen")


def python() -> Path:
    return HOME / ".venv" / "bin" / "python"


def is_installed() -> bool:
    return python().is_file() and (HOME / "scripts" / "infer.py").is_file()


def engine_version() -> str:
    try:
        match = _VERSION.search((HOME / "pyproject.toml").read_text(encoding="utf-8"))
    except OSError:
        return "not installed"
    return match.group(1) if match else "unknown"


def recognise_page(image: Path, page_number: int) -> Reading:
    kern = image.parent / f"{image.stem}-transcoda.krn"
    # Half the cores: a job reads the other engines' units of the page beside it.
    threads = str(max(1, (os.cpu_count() or 2) // 2))
    # Its encoder and weights are in the image, so nothing is fetched (and nothing about the page is sent).
    offline = {**os.environ, "HF_HOME": str(HOME / "hf"), "HF_HUB_OFFLINE": "1", "TRANSFORMERS_OFFLINE": "1"}
    command = [str(python()), "scripts/infer.py", "--image", str(image), "--out", str(kern), "--threads", threads]
    result = recognise.run(command, HOME, page_number, offline)
    text = kern.read_text(encoding="utf-8") if kern.exists() else ""
    if result.returncode != 0 or not text.strip():
        # Transcoda's messages may quote what it read: none of them is passed on.
        raise RecogniseError(f"Page {page_number} could not be read.")
    try:
        return Reading(musicxml(text))
    except Exception as error:  # noqa: BLE001 (music21 raises many kinds on kern it cannot read)
        raise RecogniseError(f"Page {page_number} could not be read.") from error


def musicxml(compact: str) -> bytes:
    """Transcoda's compact kern as MusicXML with one part."""
    import converter21
    import music21
    from music21.musicxml.m21ToXml import GeneralObjectExporter

    converter21.register()
    score = music21.converter.parseData(standard_humdrum(compact), format="humdrum")
    return one_part(GeneralObjectExporter(score).parse())


def standard_humdrum(compact: str) -> str:
    """Compact kern as standard Humdrum: a `**kern` header and a `*-` terminator on every spine, a bar line on every
    spine, `.` for an empty data field and `*` for an empty interpretation field. The spines are counted again after
    each interpretation line, as `*^` splits a spine in two and a run of `*v` joins spines into one."""
    lines = [line for line in compact.splitlines() if line.strip()]
    if not lines:
        return ""
    spines = first = len(lines[0].split("\t"))
    out = []
    for line in lines:
        fields = line.split("\t")
        if line.startswith("=") and len(fields) == 1:
            fields = fields * spines
        fields += [""] * (spines - len(fields))
        interpretation = any(field.startswith("*") for field in fields)
        fields = [field or ("*" if interpretation else ".") for field in fields]
        out.append("\t".join(fields))
        if interpretation:
            spines = spines_after(fields)
    return "\n".join(["\t".join(["**kern"] * first), *out, "\t".join(["*-"] * spines)]) + "\n"


def spines_after(fields: list[str]) -> int:
    count, index = 0, 0
    while index < len(fields):
        if fields[index] == "*v":
            while index < len(fields) and fields[index] == "*v":
                index += 1
            count += 1
            continue
        count += 2 if fields[index] == "*^" else 1
        index += 1
    return count


def one_part(score: bytes) -> bytes:
    """A score of two parts, one per staff (upper first), as one part with staves 1 and 2: each bar's lower notes
    follow its upper ones after a backup, and the lower staff's clefs, numbered 2, stay where they were in its bar.
    Any other score is returned as it is."""
    root = ET.fromstring(score)
    parts = root.findall("part")
    if len(parts) != 2:
        return score
    upper, lower = parts
    for top, bottom in zip(upper.findall("measure"), lower.findall("measure")):
        _join_measure(top, bottom)
    _two_staves(upper)
    root.remove(lower)
    listed = root.findall("part-list/score-part")
    if len(listed) == 2:
        root.find("part-list").remove(listed[1])  # type: ignore[union-attr]
    return b'<?xml version="1.0" encoding="UTF-8"?>\n' + ET.tostring(root, encoding="utf-8")


def _join_measure(top: ET.Element, bottom: ET.Element) -> None:
    elapsed = 0
    for element in top:
        if element.tag == "note":
            _set_staff(element, "1")
            if element.find("chord") is None:
                elapsed += int(element.findtext("duration") or 0)
        elif element.tag == "backup":
            elapsed -= int(element.findtext("duration") or 0)
        elif element.tag == "forward":
            elapsed += int(element.findtext("duration") or 0)
        elif element.tag == "attributes":
            for clef in element.findall("clef"):
                clef.set("number", "1")
    if elapsed > 0:
        ET.SubElement(ET.SubElement(top, "backup"), "duration").text = str(elapsed)
    for element in list(bottom):
        if element.tag in ("note", "backup", "forward"):
            if element.tag == "note":
                _set_staff(element, "2")
            top.append(element)
        elif element.tag == "attributes" and (clefs := element.findall("clef")):
            for clef in clefs:
                clef.set("number", "2")
            ET.SubElement(top, "attributes").extend(clefs)


def _two_staves(part: ET.Element) -> None:
    """The part's first bar opens with attributes that say it has two staves."""
    first = part.find("measure")
    if first is None:
        return
    opening = first.find("attributes")
    if opening is None or list(first).index(opening) > min((i for i, e in enumerate(first) if e.tag == "note"), default=len(first)):
        opening = ET.Element("attributes")
        first.insert(next((i for i, e in enumerate(first) if e.tag != "print"), len(first)), opening)
    if opening.find("staves") is None:
        children = list(opening)
        staves = ET.Element("staves")
        staves.text = "2"
        opening.insert(next((i for i, c in enumerate(children) if c.tag in ("part-symbol", "instruments", "clef")), len(children)), staves)


def _set_staff(note: ET.Element, number: str) -> None:
    staff = note.find("staff")
    if staff is None:
        children = list(note)
        staff = ET.Element("staff")
        note.insert(next((i for i, child in enumerate(children) if child.tag in _AFTER_STAFF), len(children)), staff)
    staff.text = number
