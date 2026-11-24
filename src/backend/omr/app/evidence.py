"""A page's evidence (decision record 0035, section 1): what music-core's merge checks the engines' readings against.

The server only finds and reports; it never compares a reading with the page, and nothing is merged here.

- Staves and bar lines, by image processing (no neural network). A staff is five long horizontal lines, evenly spaced.
  Staves joined by a vertical line at their left end are one system. A bar line on a staff is a thin vertical stroke
  from its top line to its bottom line, with no notehead at either end (a stem has one); a bar line of a system is a
  stroke at the same place on every one of its staves that is not a stem: it stops at the system's outer lines, and
  between two staves it either runs through the gap or stops at both. Each staff lists the system's bar lines with how
  far each runs up and down through that staff (through the gap to the next staff, or only across this one).
- The symbols copisteria's detector found (noteheads, rests, accidentals, dots, tuplets, clefs, key and time
  signatures), each with its class as a SMuFL glyph name, its place, and the system, bar and staff it is in: the
  nearest staff (at most 7 staff spaces away), and the bar its centre falls in.

Coordinates are pixels of the page image the engines read, origin top left, after the server turned it the right way
up (`image.rotated`; it never deskews). Bars are numbered from 0 within their system, systems from 0 top down, staves
from 1 top down within a system, as in a MusicXML part.
"""

from __future__ import annotations

import bisect
import re
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image

VERSION = 1

_INK = 160  # a grey level darker than this is ink
_REACH = 7.0  # staff spaces: a symbol farther from every staff belongs to none
_KEPT = 1  # decimals of the coordinates

_NOTE_VALUES = {"breve": "breve", "whole": "whole", "half": "half", "quarter": "quarter", "eighth": "eighth",
                "16th": "16th", "32nd": "32nd", "64th": "64th", "128th": "128th", "256": "256th", "512": "512th"}
_NOTEHEADS = {"breve": "noteheadDoubleWhole", "whole": "noteheadWhole", "half": "noteheadHalf", "x": "noteheadXBlack"}
_RESTS = {"breve": "restDoubleWhole", "whole": "restWhole", "half": "restHalf", "quarter": "restQuarter",
          "eighth": "rest8th", "16th": "rest16th", "32nd": "rest32nd", "64th": "rest64th", "128th": "rest128th"}
_ACCIDENTALS = {"sharp": "accidentalSharp", "flat": "accidentalFlat", "natural": "accidentalNatural",
                "dsharp": "accidentalDoubleSharp", "dflat": "accidentalDoubleFlat"}
_CLEF = re.compile(r"^clef_([GFC])([1-5])(?:_(8|15)([ab]))?$")
_KEY = re.compile(r"^keySig_(?:0|([1-7])([fs]))$")
_TIME = re.compile(r"^meterSig_(\d+)_(\d+)$")


def of_page(image: Path, detections: list[dict[str, Any]], detector: dict[str, str]) -> dict[str, Any]:
    """The evidence of the page image: its systems found by image processing, and the detector's symbols in them.
    The job adds the page's number and how it was turned (for_page)."""
    with Image.open(image) as page:
        dpi = page.info.get("dpi")
        ink = np.asarray(page.convert("L")) < _INK
    height, width = ink.shape
    systems = find_systems(ink)
    return {
        "detector": detector,
        "image": {"width": width, "height": height, "dpi": round(dpi[0]) if dpi else None},
        "systems": systems,
        "symbols": place_symbols(detections, systems),
    }


def for_page(number: int, rotated: int, found: dict[str, Any]) -> dict[str, Any]:
    """The evidence as a job's result gives it: the schema's version, the page, and the image as the engines read it."""
    return {"evidence": VERSION, "page": number, **found, "image": {**found["image"], "rotated": rotated, "deskewed": 0}}


# ---- Staves, systems and bar lines ---------------------------------------------------------------------------


def find_systems(ink: np.ndarray) -> list[dict[str, Any]]:
    staves, space = find_staves(ink)
    if not staves:
        return []
    upright = long_runs(ink.T, max(3, int(space))).T  # vertical strokes at least a staff space long
    marks = ink & ~long_runs(ink.T, max(3, int(1.5 * space))).T  # the ink but its strokes: heads, dots, beams
    systems = []
    for group in _group(staves, upright, space):
        left = max(staff["x0"] for staff in group)
        right = min(staff["x1"] for staff in group)
        lines = _bar_lines(ink, marks, upright, group, space)
        edges = [left, *lines]
        # Past the last bar line, only a bar whose line was missed is wider than a courtesy time signature.
        if not lines or lines[-1] < right - 4 * space:
            edges.append(right)
        systems.append({
            "box": [_round(min(s["x0"] for s in group)), _round(group[0]["lines"][0]),
                    _round(max(s["x1"] for s in group)), _round(group[-1]["lines"][4])],
            "staves": [
                {"staff": number, "lines": [_round(y) for y in staff["lines"]], "space": _round(staff["space"]),
                 "barLines": [{"x": _round(x), "y": [_round(y) for y in _extent(upright, x, staff["lines"][2])]}
                              for x in lines]}
                for number, staff in enumerate(group, 1)
            ],
            "bars": [{"x": [_round(a), _round(b)]} for a, b in zip(edges, edges[1:])],
        })
    return systems


def find_staves(ink: np.ndarray) -> tuple[list[dict[str, Any]], float]:
    """Staves top down, each its five line rows (centres), its staff space and its horizontal extent; and the page's
    staff space (the median), 0 without a staff."""
    width = ink.shape[1]
    lines = long_runs(ink, max(40, width // 30))
    profile = lines.sum(axis=1)
    if profile.max(initial=0) == 0:
        return [], 0.0
    rows = np.flatnonzero(profile > 0.25 * profile.max())
    groups = np.split(rows, np.flatnonzero(np.diff(rows) > 1) + 1)
    # A line's row is the group's longest: a beam along a line thickens its group, but is shorter than the line.
    centres = [float(group[profile[group] >= 0.9 * profile[group].max()].mean()) for group in groups]
    staves = []
    i = 0
    while i + 4 < len(centres):
        five = _five_lines(centres, i)
        if five is None:
            i += 1
            continue
        band = lines[max(0, int(five[0]) - 1):int(five[4]) + 2]
        columns = np.flatnonzero(band.any(axis=0))
        staves.append({"lines": five, "space": (five[4] - five[0]) / 4, "x0": int(columns[0]), "x1": int(columns[-1])})
        i = centres.index(five[4]) + 1
    space = float(np.median([staff["space"] for staff in staves])) if staves else 0.0
    return staves, space


def _five_lines(centres: list[float], first: int) -> list[float] | None:
    """Five evenly spaced rows from the first: long rows between them (a beam lying along a staff) are passed over."""
    for second in range(first + 1, min(first + 4, len(centres))):
        space = centres[second] - centres[first]
        if space <= 5:
            continue
        five = [centres[first]]
        for step in range(1, 5):
            target = centres[first] + step * space
            nearest = min(centres[first + 1:first + 16], key=lambda y: abs(y - target))
            if abs(nearest - target) > 0.2 * space:
                break
            five.append(nearest)
        else:
            return five
    return None


def long_runs(mask: np.ndarray, length: int) -> np.ndarray:
    """The pixels of each row that lie in a run of at least `length` set pixels along it (a morphological opening
    with a horizontal line `length` long); for columns, pass the transpose."""
    height, width = mask.shape
    padded = np.zeros((height, width + 2), dtype=np.int8)
    padded[:, 1:-1] = mask
    flat = padded.ravel()
    steps = np.diff(flat)
    starts = np.flatnonzero(steps == 1) + 1
    ends = np.flatnonzero(steps == -1) + 1
    long = ends - starts >= length
    marks = np.zeros(flat.size + 1, dtype=np.int32)
    np.add.at(marks, starts[long], 1)
    np.add.at(marks, ends[long], -1)
    return (np.cumsum(marks)[:flat.size] > 0).reshape(height, width + 2)[:, 1:-1]


def _group(staves: list[dict[str, Any]], upright: np.ndarray, space: float) -> list[list[dict[str, Any]]]:
    """Staves in systems: a staff joins the one above when a vertical line runs from that one's bottom line to its top
    line at their left end (the system's opening line)."""
    groups = [[staves[0]]]
    for upper, lower in zip(staves, staves[1:]):
        left = max(upper["x0"], lower["x0"])
        top, bottom = int(round(upper["lines"][4])), int(round(lower["lines"][0]))
        window = upright[top:bottom + 1, max(0, int(left - space)):int(left + space) + 1]
        joined = window.size > 0 and bottom > top and window.mean(axis=0).max() >= 0.9
        if joined:
            groups[-1].append(lower)
        else:
            groups.append([lower])
    return groups


def _strokes(ink: np.ndarray, staff: dict[str, Any], space: float) -> list[tuple[int, int]]:
    """Thin vertical strokes from the staff's top line to its bottom line: their first and last columns."""
    top, bottom = int(round(staff["lines"][0])), int(round(staff["lines"][4]))
    full = np.flatnonzero(ink[top:bottom + 1].sum(axis=0) >= (bottom - top + 1) * 0.95)
    if full.size == 0:
        return []
    runs = np.split(full, np.flatnonzero(np.diff(full) > 1) + 1)
    return [(int(run[0]), int(run[-1])) for run in runs if run[-1] - run[0] + 1 <= 0.6 * space]


def _merged(xs: list[float], space: float) -> list[float]:
    """Strokes closer than 1.2 staff spaces are one line (a double or a final bar line), at the last one's place."""
    merged: list[float] = []
    for x in xs:
        if merged and x - merged[-1] < 1.2 * space:
            merged[-1] = x
        else:
            merged.append(x)
    return merged


def _staff_bar_lines(ink: np.ndarray, marks: np.ndarray, staff: dict[str, Any], space: float) -> list[float]:
    """A staff's bar lines: strokes with no notehead beside either end, as a stem has, past its opening line. The
    heads are looked for in `marks`, the ink without its strokes, so the other line of a double bar line is none."""
    top, bottom = staff["lines"][0], staff["lines"][4]
    reach = int(1.3 * space)

    def head_beside(y: float, x0: int, x1: int) -> bool:
        rows = slice(max(0, int(y - 0.5 * space)), int(y + 0.5 * space))
        left, right = marks[rows, max(0, x0 - reach):max(0, x0 - 1)], marks[rows, x1 + 2:x1 + reach]
        return max(left.mean() if left.size else 0.0, right.mean() if right.size else 0.0) > 0.45

    xs = [(x0 + x1) / 2 for x0, x1 in _strokes(ink, staff, space)
          if (x0 + x1) / 2 > staff["x0"] + 1.5 * space and not head_beside(top, x0, x1) and not head_beside(bottom, x0, x1)]
    return _merged(xs, space)


def _extent(upright: np.ndarray, x: float, y: float) -> tuple[int, int]:
    """The rows a vertical stroke through (x, y) runs over, in the columns either side of x."""
    column = upright[:, max(0, int(x) - 1):int(x) + 2].max(axis=1)
    top = bottom = int(round(y))
    while top > 0 and column[top - 1]:
        top -= 1
    while bottom + 1 < len(column) and column[bottom + 1]:
        bottom += 1
    return top, bottom


def _bar_lines(ink: np.ndarray, marks: np.ndarray, upright: np.ndarray, group: list[dict[str, Any]], space: float) -> list[float]:
    """A system's bar lines: a staff's bar line found at the same place on every staff of the system (stems almost
    never line up) that does not run on past the system's outer lines, as a stem does to its head."""
    over = 0.6 * space
    left = max(staff["x0"] for staff in group)
    per_staff = [_staff_bar_lines(ink, marks, staff, space) for staff in group]

    def through(x: float) -> bool:
        extents = [_extent(upright, x, staff["lines"][2]) for staff in group]
        if extents[0][0] < group[0]["lines"][0] - over or extents[-1][1] > group[-1]["lines"][4] + over:
            return False
        for (upper, lower), (above, below) in zip(zip(group, group[1:]), zip(extents, extents[1:])):
            gap = upright[int(upper["lines"][4]):int(lower["lines"][0]), max(0, int(x) - 1):int(x) + 2].max(axis=1)
            crosses = above[1] > upper["lines"][4] + over, below[0] < lower["lines"][0] - over
            # Drawn through the gap, or stopping at both staves; broken by text in the gap, it crosses at both.
            if not (gap.size and gap.mean() > 0.8) and crosses[0] != crosses[1]:
                return False
        return True

    xs = [x for x in per_staff[0] if x > left + 1.5 * space
          and all(any(abs(x - other) <= 3 for other in strokes) for strokes in per_staff[1:]) and through(x)]
    return _merged(xs, space)


# ---- Symbols -------------------------------------------------------------------------------------------------


def place_symbols(detections: list[dict[str, Any]], systems: list[dict[str, Any]]) -> list[dict[str, Any]]:
    """The detector's symbols that name a SMuFL glyph (or a key or time signature), each in its system, bar and staff;
    a symbol near no staff is left out. In order of system, bar, staff and place."""
    staves = [(index, staff) for index, system in enumerate(systems) for staff in system["staves"]]
    placed = []
    for detection in detections:
        named = symbol_class(str(detection.get("cls", "")), detection.get("attrs") or {})
        box = detection.get("xyxy")
        if named is None or not staves or not _numbers(box, 4):
            continue
        x, y = (box[0] + box[2]) / 2, (box[1] + box[3]) / 2
        distance, index, staff = min(
            (max(staff["lines"][0] - y, y - staff["lines"][4], 0.0) / staff["space"], index, staff) for index, staff in staves
        )
        bars = systems[index]["bars"]
        if distance > _REACH or not bars:
            continue
        bar = max(0, bisect.bisect_right([bar["x"][0] for bar in bars], x) - 1)
        placed.append({
            "system": index, "bar": bar, "staff": staff["staff"], **named,
            "x": _round(x), "y": _round(y), "box": [_round(v) for v in box],
            "confidence": round(float(detection["conf"]), 3) if _numbers([detection.get("conf")], 1) else 0.0,
        })
    return sorted(placed, key=lambda s: (s["system"], s["bar"], s["staff"], s["x"], s["y"], s["class"]))


def symbol_class(name: str, attrs: dict[str, Any]) -> dict[str, Any] | None:
    """A detector class as the evidence names it: a SMuFL glyph name, with what else the class or its attributes say
    (a note's value, staff position, dots and whether it is a grace note; a clef's line; a key's fifths; a meter).
    None for the classes the evidence leaves out (beams, slurs, text, dynamics and the like)."""
    family, _, kind = name.partition("_")
    if family == "note" and (kind in _NOTE_VALUES or kind == "x"):
        named: dict[str, Any] = {"class": _NOTEHEADS.get(kind, "noteheadBlack")}
        if kind in _NOTE_VALUES:
            named["duration"] = _NOTE_VALUES[kind]
        position = _whole(attrs.get("staff_position"))
        if position is not None:
            named["position"] = position
        named["dots"] = _whole(attrs.get("dots")) or 0
        named["grace"] = attrs.get("grace", "<na>") not in ("<na>", "none")
        return named
    if family == "rest" and kind in _RESTS:
        return {"class": _RESTS[kind]}
    if name == "mRest_measure":
        return {"class": "restWhole", "measure": True}
    if family == "accid" and kind in _ACCIDENTALS:
        return {"class": _ACCIDENTALS[kind]}
    if name == "dots_1":
        return {"class": "augmentationDot"}
    if family == "tuplet" and kind.isdigit() and int(kind) <= 9:
        return {"class": f"tuplet{kind}"}
    if match := _CLEF.match(name):
        sign, line, octaves, way = match.groups()
        glyph = {"G": "gClef", "F": "fClef", "C": "cClef"}[sign]
        if octaves:
            glyph += {"8": "8v", "15": "15m"}[octaves] + ("a" if way == "a" else "b")
        return {"class": glyph, "line": int(line)}
    if match := _KEY.match(name):
        count, way = match.groups()
        return {"class": "keySignature", "fifths": 0 if count is None else int(count) * (1 if way == "s" else -1)}
    if match := _TIME.match(name):
        return {"class": "timeSignature", "beats": int(match.group(1)), "beatType": int(match.group(2))}
    return None


def _whole(value: object) -> int | None:
    if isinstance(value, float) and value.is_integer():
        return int(value)
    try:
        return int(str(value))
    except ValueError:
        return None


def _numbers(values: object, count: int) -> bool:
    """Whether the detector gave `count` numbers here (it should, but its file is read as it is)."""
    return (isinstance(values, list) and len(values) == count
            and all(isinstance(v, (int, float)) and not isinstance(v, bool) and np.isfinite(v) for v in values))


def _round(value: float) -> float:
    return round(float(value), _KEPT)
