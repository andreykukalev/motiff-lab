import xml.etree.ElementTree as ET

from app import transcoda_reader
from app.transcoda_reader import one_part, spines_after, standard_humdrum


def test_compact_kern_becomes_standard_humdrum():
    compact = "*clefF4\t*clefG2\n4C\t4e\n=\n\t4f\n*\t*^\n4D\t4g\t4b\n*\t*v\t*v\n="
    assert standard_humdrum(compact) == (
        "**kern\t**kern\n"
        "*clefF4\t*clefG2\n4C\t4e\n=\t=\n.\t4f\n*\t*^\n4D\t4g\t4b\n*\t*v\t*v\n=\t=\n"
        "*-\t*-\n"
    )


def test_a_split_adds_a_spine_and_a_run_of_joins_makes_one():
    assert spines_after(["*", "*^"]) == 3
    assert spines_after(["*v", "*v", "*"]) == 2
    assert spines_after(["*", "*v", "*v", "*v"]) == 2


def test_empty_kern_is_empty():
    assert standard_humdrum("\n \n") == ""


def note(step: str, duration: int, staff: str | None = None, chord: bool = False, beam: bool = False) -> str:
    return (f"<note>{'<chord/>' if chord else ''}<pitch><step>{step}</step><octave>4</octave></pitch>"
            f"<duration>{duration}</duration><voice>1</voice><type>quarter</type>"
            f"{f'<staff>{staff}</staff>' if staff else ''}{'<beam number=\"1\">begin</beam>' if beam else ''}</note>")


TWO_PARTS = (
    '<score-partwise version="4.0"><part-list><score-part id="P1"/><score-part id="P2"/></part-list>'
    '<part id="P1">'
    '<measure number="1"><attributes><divisions>1</divisions><clef><sign>G</sign><line>2</line></clef></attributes>'
    f'{note("E", 1, beam=True)}{note("G", 1, chord=True)}{note("F", 1)}</measure>'
    f'<measure number="2">{note("G", 2)}</measure></part>'
    '<part id="P2">'
    '<measure number="1"><attributes><divisions>1</divisions><clef><sign>F</sign><line>4</line></clef></attributes>'
    f'{note("C", 2)}</measure>'
    f'<measure number="2">{note("D", 1)}<attributes><clef><sign>G</sign><line>2</line></clef></attributes>{note("E", 1)}</measure>'
    '</part></score-partwise>'
).encode()


def test_two_parts_become_one_part_with_two_staves():
    root = ET.fromstring(one_part(TWO_PARTS))
    assert [p.get("id") for p in root.findall("part")] == ["P1"]
    assert [p.get("id") for p in root.findall("part-list/score-part")] == ["P1"]
    first, second = root.findall("part/measure")
    assert [child.tag for child in first] == ["attributes", "note", "note", "note", "backup", "attributes", "note"]
    assert first.find("attributes/staves").text == "2"
    assert [child.tag for child in first.find("attributes")] == ["divisions", "staves", "clef"]
    assert [clef.get("number") for clef in first.iter("clef")] == ["1", "2"]
    assert first.find("backup/duration").text == "2"  # the chord's second note takes no time
    assert [n.findtext("staff") for n in first.findall("note")] == ["1", "1", "1", "2"]
    # A staff number goes before a note's beam, as MusicXML orders them.
    assert [child.tag for child in first.find("note")][-2:] == ["staff", "beam"]
    # The lower staff's clef change stays where it was in its bar, after its first note.
    assert [child.tag for child in second] == ["note", "backup", "note", "attributes", "note"]
    assert second.find("attributes/clef").get("number") == "2"


def test_a_score_of_one_part_is_kept_as_it_is():
    single = b'<score-partwise version="4.0"><part-list><score-part id="P1"/></part-list><part id="P1"/></score-partwise>'
    assert one_part(single) == single


def test_transcoda_is_not_installed_without_its_environment(tmp_path, monkeypatch):
    monkeypatch.setattr(transcoda_reader, "HOME", tmp_path)
    assert not transcoda_reader.is_installed()
    assert transcoda_reader.engine_version() == "not installed"
    (tmp_path / "pyproject.toml").write_text('[project]\nname = "transcoda"\nversion = "2.1.0"\n')
    assert transcoda_reader.engine_version() == "2.1.0"
