from pathlib import Path

import pytest
from PIL import Image, ImageDraw

from app import evidence, pages

SPACE = 12
# Für Elise (Mutopia, public domain), the vector reader's fixture: page 1 has six piano systems of 6, 6, 5, 5, 5, 5 bars.
FUR_ELISE = Path(__file__).parents[4] / "tests/backend/Unit/MotiffLab.MusicCore.Tests/Import/Vector/Fixtures/fur-elise-a4.pdf"


def staff(draw: ImageDraw.ImageDraw, top: int, x0: int = 50, x1: int = 1150) -> None:
    for line in range(5):
        draw.rectangle([x0, top + line * SPACE, x1, top + line * SPACE + 1], fill=0)


def stroke(draw: ImageDraw.ImageDraw, x: int, top: int, bottom: int, width: int = 2) -> None:
    draw.rectangle([x, top, x + width - 1, bottom], fill=0)


def page(tmp_path: Path) -> Path:
    """A grand staff (staves at 100 and 220, joined at their left end) with three bars, a stem on each staff at the same
    place with a head at its end, and a beam along a line; then two staves of one staff each (not joined), the first
    with a bar line."""
    image = Image.new("L", (1200, 900), 255)
    draw = ImageDraw.Draw(image)
    staff(draw, 100)
    staff(draw, 220)
    stroke(draw, 50, 100, 269)  # the system's opening line
    for x in (400, 750):
        stroke(draw, x, 100, 269)
    stroke(draw, 1140, 100, 269)  # a final bar line: thin, then thick
    stroke(draw, 1146, 100, 269, width=5)
    for top in (88, 208):  # stems from above each staff down to a head on its bottom line
        stroke(draw, 600, top, 148 + top - 88)
        draw.ellipse([588, 142 + top - 88, 601, 153 + top - 88], fill=0)
    draw.rectangle([800, 123, 950, 129], fill=0)  # a beam lying along the middle line of the upper staff
    staff(draw, 500)
    stroke(draw, 500, 500, 549)
    staff(draw, 700)
    path = tmp_path / "page-01.png"
    image.save(path, dpi=(150, 150))
    return path


def test_staves_systems_and_bar_lines_are_found_on_the_page(tmp_path: Path):
    found = evidence.of_page(page(tmp_path), [], {"name": "copisteria", "version": "1.3.4"})
    assert found["image"] == {"width": 1200, "height": 900, "dpi": 150}
    grand, single, last = found["systems"]
    assert [s["staff"] for s in grand["staves"]] == [1, 2]
    assert grand["staves"][0]["lines"] == [100.5, 112.5, 124.5, 136.5, 148.5]  # the beam does not move the line
    assert grand["staves"][0]["space"] == SPACE
    # The stems line up on both staves but run past the system's outer lines; the double bar line is one.
    assert [bar["x"] for bar in grand["bars"]] == [[50.0, 400.5], [400.5, 750.5], [750.5, 1148.0]]
    assert grand["box"] == [50.0, 100.5, 1150.0, 268.5]
    # Each staff lists the system's bar lines and how far they run: through the gap to the other staff.
    assert grand["staves"][1]["barLines"][0] == {"x": 400.5, "y": [100.0, 269.0]}
    # Staves not joined at their left end are systems of their own.
    assert [len(s["staves"]) for s in (single, last)] == [1, 1]
    assert [bar["x"] for bar in single["bars"]] == [[50.0, 500.5], [500.5, 1150.0]]
    assert single["staves"][0]["barLines"] == [{"x": 500.5, "y": [500.0, 549.0]}]
    assert [bar["x"] for bar in last["bars"]] == [[50.0, 1150.0]]


def test_a_page_without_staves_has_no_systems_and_no_symbols(tmp_path: Path):
    path = tmp_path / "blank.png"
    Image.new("L", (300, 300), 255).save(path)
    found = evidence.of_page(path, [{"cls": "note_quarter", "conf": 0.9, "xyxy": [10, 10, 20, 20]}], {})
    assert found["systems"] == [] and found["symbols"] == [] and found["image"]["dpi"] is None


def test_symbols_are_placed_in_their_system_bar_and_staff(tmp_path: Path):
    detections = [
        {"cls": "note_eighth", "conf": 0.98, "xyxy": [500, 238, 514, 250],
         "attrs": {"staff_position": "-1", "dots": "1", "grace": "<na>", "stem_dir": "up"}},
        {"cls": "clef_G2", "conf": 0.97, "xyxy": [60, 90, 80, 160]},
        {"cls": "beam", "conf": 0.99, "xyxy": [800, 123, 950, 129]},  # not a symbol of the evidence
        {"cls": "note_quarter", "conf": 0.9, "xyxy": [300, 390, 310, 400]},  # more than 7 spaces from every staff
        {"cls": "rest_quarter", "conf": 0.8, "xyxy": [1180, 560, 1190, 580]},  # past the last bar line: the last bar
    ]
    found = evidence.of_page(page(tmp_path), detections, {"name": "copisteria", "version": "1.3.4"})
    assert found["symbols"] == [
        {"system": 0, "bar": 0, "staff": 1, "class": "gClef", "line": 2, "x": 70.0, "y": 125.0, "box": [60.0, 90.0, 80.0, 160.0],
         "confidence": 0.97},
        {"system": 0, "bar": 1, "staff": 2, "class": "noteheadBlack", "duration": "eighth", "position": -1, "dots": 1,
         "grace": False, "x": 507.0, "y": 244.0, "box": [500.0, 238.0, 514.0, 250.0], "confidence": 0.98},
        {"system": 1, "bar": 1, "staff": 1, "class": "restQuarter", "x": 1185.0, "y": 570.0,
         "box": [1180.0, 560.0, 1190.0, 580.0], "confidence": 0.8},
    ]


@pytest.mark.parametrize(("name", "attrs", "named"), [
    ("note_half", {"grace": "unacc", "dots": "<na>", "staff_position": "<na>"},
     {"class": "noteheadHalf", "duration": "half", "dots": 0, "grace": True}),
    ("note_whole", {}, {"class": "noteheadWhole", "duration": "whole", "dots": 0, "grace": False}),
    ("note_16th", {"staff_position": "11"}, {"class": "noteheadBlack", "duration": "16th", "position": 11, "dots": 0, "grace": False}),
    ("rest_eighth", {}, {"class": "rest8th"}),
    ("mRest_measure", {}, {"class": "restWhole", "measure": True}),
    ("accid_sharp", {}, {"class": "accidentalSharp"}),
    ("accid_1qs", {}, None),
    ("dots_1", {}, {"class": "augmentationDot"}),
    ("tuplet_3", {}, {"class": "tuplet3"}),
    ("tupletNum", {}, None),
    ("clef_F4", {}, {"class": "fClef", "line": 4}),
    ("clef_G2_8b", {}, {"class": "gClef8vb", "line": 2}),
    ("clef_C3", {}, {"class": "cClef", "line": 3}),
    ("keySig_3f", {}, {"class": "keySignature", "fifths": -3}),
    ("keySig_2s", {}, {"class": "keySignature", "fifths": 2}),
    ("keySig_0", {}, {"class": "keySignature", "fifths": 0}),
    ("meterSig_6_8", {}, {"class": "timeSignature", "beats": 6, "beatType": 8}),
    ("barLine_single", {}, None),
    ("dynam_p", {}, None),
])
def test_detector_classes_are_named_as_smufl_glyphs(name, attrs, named):
    assert evidence.symbol_class(name, attrs) == named


def test_the_job_s_evidence_names_the_page_and_how_it_was_turned():
    found = {"detector": {"name": "copisteria", "version": "1.3.4"}, "image": {"width": 10, "height": 20, "dpi": 300},
             "systems": [], "symbols": []}
    assert evidence.for_page(3, 90, found) == {
        "evidence": 1, "page": 3, "detector": {"name": "copisteria", "version": "1.3.4"},
        "image": {"width": 10, "height": 20, "dpi": 300, "rotated": 90, "deskewed": 0}, "systems": [], "symbols": [],
    }


@pytest.mark.skipif(not FUR_ELISE.exists(), reason="the repository's vector fixtures are not here")
def test_the_bars_of_a_printed_page_are_found(tmp_path: Path):
    written = pages.write_pages(pages.sniff(FUR_ELISE.read_bytes()), tmp_path, 8, [1])
    found = evidence.of_page(written[0].path, [], {})
    assert found["image"]["dpi"] == pages.PDF_DPI
    assert [len(system["staves"]) for system in found["systems"]] == [2] * 6
    assert [len(system["bars"]) for system in found["systems"]] == [6, 6, 5, 5, 5, 5]


def test_a_staff_at_the_top_edge_and_odd_detections_are_read_without_failing(tmp_path: Path):
    image = Image.new("L", (600, 300), 255)
    draw = ImageDraw.Draw(image)
    staff(draw, 0, 10, 590)  # its top line on the first row of the image
    path = tmp_path / "edge.png"
    image.save(path)
    detections = [
        {"cls": "note_quarter", "conf": None, "xyxy": [100, 20, 110, 30], "attrs": {"staff_position": 2.0, "dots": None}},
        {"cls": "note_quarter", "conf": 0.5, "xyxy": ["a", 20, 110, 30]},
        {"cls": "note_quarter", "conf": "high", "xyxy": [100, 20, 110]},
    ]
    found = evidence.of_page(path, detections, {})
    assert [staff["lines"][0] for system in found["systems"] for staff in system["staves"]] == [0.5]
    assert found["symbols"] == [{"system": 0, "bar": 0, "staff": 1, "class": "noteheadBlack", "duration": "quarter",
                                 "position": 2, "dots": 0, "grace": False, "x": 105.0, "y": 25.0,
                                 "box": [100.0, 20.0, 110.0, 30.0], "confidence": 0.0}]
