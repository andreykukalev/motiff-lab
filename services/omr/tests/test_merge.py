import xml.etree.ElementTree as ET

import pytest

from app.merge import MergeError, merge


def page(*measures: str) -> bytes:
    body = "".join(f'<measure number="{number}"><note><rest/><duration>4</duration></note></measure>' for number in measures)
    return (
        '<?xml version="1.0" encoding="UTF-8"?>'
        '<score-partwise version="4.0"><part-list><score-part id="P1"><part-name>Piano</part-name></score-part></part-list>'
        f'<part id="P1">{body}</part></score-partwise>'
    ).encode()


def numbers(score: bytes) -> list[str]:
    return [measure.get("number") for measure in ET.fromstring(score).find("part").findall("measure")]


def test_one_page_is_returned_as_it_is():
    single = page("1", "2")
    assert merge([single]) == single


def test_pages_follow_each_other_and_numbers_run_on():
    score = merge([page("1", "2", "3"), page("1", "2"), page("1")])
    assert numbers(score) == ["1", "2", "3", "4", "5", "6"]
    assert len(ET.fromstring(score).findall("part-list/score-part")) == 1


def test_a_page_with_other_parts_is_refused():
    other = page("1").replace(b"</part></score-partwise>", b'</part><part id="P2"></part></score-partwise>')
    with pytest.raises(MergeError, match="Page 2"):
        merge([page("1"), other])


def test_damaged_music_is_refused():
    with pytest.raises(MergeError, match="damaged"):
        merge([page("1"), b"<score-partwise"])


def test_each_page_after_the_first_starts_with_a_new_page():
    score = ET.fromstring(merge([page("1", "2"), page("1", "2"), page("1")]))
    marks = [measure.get("number") for measure in score.find("part").findall("measure") if measure.find("print[@new-page='yes']") is not None]
    assert marks == ["3", "5"]
    assert score.find("part/measure[3]")[0].tag == "print"


def test_a_page_whose_first_measure_has_a_print_keeps_it():
    second = page("1").replace(b'<measure number="1">', b'<measure number="1"><print new-system="yes"/>')
    printed = ET.fromstring(merge([page("1"), second])).find("part/measure[2]/print")
    assert printed.get("new-page") == "yes" and printed.get("new-system") == "yes"
