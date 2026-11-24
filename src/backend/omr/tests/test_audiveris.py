import io
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

import pytest

from app import audiveris_reader
from app.recognise import RecogniseError
from tests.test_merge import page

CONTAINER = (b'<container><rootfiles><rootfile full-path="score.xml" media-type="application/vnd.recordare.musicxml+xml"/>'
             b'</rootfiles></container>')


def mxl(score: bytes) -> bytes:
    buffer = io.BytesIO()
    with zipfile.ZipFile(buffer, "w") as archive:
        archive.writestr("META-INF/container.xml", CONTAINER)
        archive.writestr("score.xml", score)
    return buffer.getvalue()


def installed(home: Path, movements: dict[str, bytes], status: int = 0) -> None:
    """A stand-in for Audiveris's launcher: it writes the given .mxl files into the folder after -output."""
    (home / "bin").mkdir(parents=True)
    (home / "lib" / "app").mkdir(parents=True)
    (home / "lib" / "app" / "Audiveris.cfg").write_text("[JavaOptions]\njava-options=-Djpackage.app-version=5.11.0\n")
    for name, data in movements.items():
        (home / name).write_bytes(data)
    names = " ".join(movements)
    program = home / "bin" / "Audiveris"
    program.write_text(
        f'#!/bin/sh\nout="$4"\nmkdir -p "$out"\necho "$XDG_CACHE_HOME" > "$out/cache"\n'
        f'for n in {names}; do cp "{home}/$n" "$out/$n"; done\nexit {status}\n'
    )
    program.chmod(0o755)


def test_audiveris_s_movements_are_joined_without_a_page_break(tmp_path: Path, monkeypatch):
    home = tmp_path / "audiveris"
    installed(home, {"page-01.mvt2.mxl": mxl(page("1")), "page-01.mvt10.mxl": mxl(page("1")), "page-01.mvt1.mxl": mxl(page("1", "2"))})
    monkeypatch.setattr(audiveris_reader, "HOME", home)
    image = tmp_path / "page-01.png"
    image.write_bytes(b"png")
    assert audiveris_reader.is_installed() and audiveris_reader.engine_version() == "5.11.0"
    score = ET.fromstring(audiveris_reader.recognise_page(image, 1).musicxml)
    assert [m.get("number") for m in score.find("part").findall("measure")] == ["1", "2", "3", "4"]
    assert score.find(".//print") is None
    # Its caches and logs go into the page's own folder, which is deleted with the upload.
    assert (tmp_path / "page-01-audiveris" / "cache").read_text().strip() == str(tmp_path / "page-01-audiveris" / "home" / "xdg_cache_home")


@pytest.mark.parametrize(("movements", "status"), [({}, 0), ({"page-01.mxl": b"not a zip"}, 0), ({"page-01.mxl": mxl(page("1"))}, 1)])
def test_a_page_audiveris_could_not_read_is_not_read(tmp_path: Path, monkeypatch, movements, status):
    home = tmp_path / "audiveris"
    installed(home, movements, status)
    monkeypatch.setattr(audiveris_reader, "HOME", home)
    image = tmp_path / "page-01.png"
    image.write_bytes(b"png")
    with pytest.raises(RecogniseError, match="Page 3 could not be read"):
        audiveris_reader.recognise_page(image, 3)


def test_without_the_package_audiveris_is_not_installed(tmp_path: Path, monkeypatch):
    monkeypatch.setattr(audiveris_reader, "HOME", tmp_path)
    assert not audiveris_reader.is_installed()
    assert audiveris_reader.engine_version() == "not installed"
