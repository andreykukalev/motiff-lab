import xml.etree.ElementTree as ET

import pytest

from app import transcoda_reader
from app.recognise import RecogniseError
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


def launcher(home, kern: str | None, status: int = 0) -> None:
    """A stand-in for Transcoda's Python: it notes its arguments and environment, and writes `kern` after --out."""
    (home / ".venv" / "bin").mkdir(parents=True)
    (home / "scripts").mkdir()
    (home / "scripts" / "infer.py").write_text("")
    program = home / ".venv" / "bin" / "python"
    write = f"printf '%s' '{kern}' > \"$5\"\n" if kern is not None else ""
    program.write_text(
        f'#!/bin/sh\necho "$PWD $* $HF_HOME $HF_HUB_OFFLINE $TRANSFORMERS_OFFLINE" > "{home}/called"\n{write}exit {status}\n'
    )
    program.chmod(0o755)


def test_transcoda_reads_the_page_offline_with_its_share_of_the_cores(tmp_path, monkeypatch):
    home = tmp_path / "transcoda"
    launcher(home, "4c\t4e\n=")
    monkeypatch.setattr(transcoda_reader, "HOME", home)
    monkeypatch.setattr(transcoda_reader, "musicxml", lambda kern: f"<read>{kern}</read>".encode())
    monkeypatch.setenv("OMP_NUM_THREADS", "2")
    image = tmp_path / "page-01.png"
    image.write_bytes(b"png")
    assert transcoda_reader.is_installed()
    assert transcoda_reader.recognise_page(image, 1).musicxml == b"<read>4c\t4e\n=</read>"
    kern = tmp_path / "page-01-transcoda.krn"
    assert (home / "called").read_text().split() == [
        str(home), "scripts/infer.py", "--image", str(image), "--out", str(kern), "--threads", "2", str(home / "hf"), "1", "1",
    ]


@pytest.mark.parametrize(("kern", "status"), [("4c\t4e", 1), (None, 0), (" \n", 0)])
def test_a_page_transcoda_could_not_read_is_not_read(tmp_path, monkeypatch, kern, status):
    home = tmp_path / "transcoda"
    launcher(home, kern, status)
    monkeypatch.setattr(transcoda_reader, "HOME", home)
    monkeypatch.setattr(transcoda_reader, "musicxml", lambda text: b"<read/>")
    image = tmp_path / "page-01.png"
    image.write_bytes(b"png")
    with pytest.raises(RecogniseError, match="Page 2 could not be read"):
        transcoda_reader.recognise_page(image, 2)


def test_kern_music21_cannot_read_is_a_page_not_read(tmp_path, monkeypatch):
    home = tmp_path / "transcoda"
    launcher(home, "4c")
    monkeypatch.setattr(transcoda_reader, "HOME", home)

    def cannot(text):
        raise ValueError("bad kern")

    monkeypatch.setattr(transcoda_reader, "musicxml", cannot)
    image = tmp_path / "page-01.png"
    image.write_bytes(b"png")
    with pytest.raises(RecogniseError, match="Page 1 could not be read"):
        transcoda_reader.recognise_page(image, 1)
