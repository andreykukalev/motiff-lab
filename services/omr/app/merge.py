"""Joins the MusicXML homr writes for each page into one score.

homr writes one partwise score per page, each with its own part list and attributes. The pages of a
piano piece have the same parts, so the measures of page 2 follow those of page 1 in each part, and
measure numbers run on. Each page's first measure keeps its attributes (divisions, key, time,
clefs), which MusicXML allows mid-part, so durations stay right even when pages use different
divisions.
"""

from __future__ import annotations

import xml.etree.ElementTree as ET


class MergeError(ValueError):
    """The pages cannot be joined. The message is plain words for the player."""


def merge(pages: list[bytes]) -> bytes:
    if not pages:
        raise MergeError("No page was read.")

    scores = [_parse(page, number) for number, page in enumerate(pages, 1)]
    first = scores[0]
    parts = first.findall("part")
    if not parts:
        raise MergeError("Page 1 has no music.")

    number = _last_number(parts[0])
    for page_number, score in enumerate(scores[1:], 2):
        page_parts = score.findall("part")
        if len(page_parts) != len(parts):
            raise MergeError(
                f"Page {page_number} has {len(page_parts)} parts where page 1 has {len(parts)}; "
                "scan the pages one at a time instead."
            )
        added = 0
        for part, page_part in zip(parts, page_parts):
            measures = page_part.findall("measure")
            for offset, measure in enumerate(measures, 1):
                measure.set("number", str(number + offset))
                part.append(measure)
            added = max(added, len(measures))
        number += added

    if len(scores) == 1:
        return pages[0]
    return b'<?xml version="1.0" encoding="UTF-8" standalone="no"?>\n' + ET.tostring(first, encoding="utf-8")


def _parse(page: bytes, number: int) -> ET.Element:
    try:
        root = ET.fromstring(page)
    except ET.ParseError as error:
        raise MergeError(f"The music read from page {number} is damaged.") from error
    if root.tag != "score-partwise":
        raise MergeError(f"The music read from page {number} is not a partwise MusicXML score.")
    return root


def _last_number(part: ET.Element) -> int:
    measures = part.findall("measure")
    for measure in reversed(measures):
        try:
            return int(measure.get("number", ""))
        except ValueError:
            continue
    return len(measures)
