import json
from pathlib import Path

from fastapi.testclient import TestClient
from PIL import Image

from app import audiveris_reader, copisteria_reader, engines, transcoda_reader
from app.engines import Engine
from app.main import create_app
from app.recognise import Found, Reading
from tests.test_app import png
from tests.test_merge import page


def client(seen: list[str]) -> TestClient:
    def homr(path: Path, number: int) -> Reading:
        seen.append("homr")
        return Reading(page("1"))

    def copisteria(path: Path, number: int) -> Reading:
        seen.append("copisteria")
        return Reading(page("1", "2"), Found(staves=2, noteheads=40, bar_lines=4, space_px=12.5))

    return TestClient(create_app(homr, other_engines=[Engine("copisteria", lambda: "1.3.4", copisteria)]))


def test_health_lists_every_engine_and_names_the_default():
    body = client([]).get("/health").json()
    assert body["engine"] == "homr"
    assert [entry["engine"] for entry in body["engines"]] == ["homr", "copisteria"]
    assert body["engines"][1]["version"] == "1.3.4"


def test_without_an_engine_homr_reads_the_page():
    seen: list[str] = []
    response = client(seen).post("/recognise", files={"file": ("photo.png", png(), "image/png")})
    assert response.status_code == 200
    assert seen == ["homr"]
    assert response.headers["x-engine"].startswith("homr ")


def test_the_engine_field_picks_the_engine_and_the_answer_names_it():
    seen: list[str] = []
    response = client(seen).post("/recognise", files={"file": ("photo.png", png(), "image/png")}, data={"engine": "copisteria"})
    assert response.status_code == 200
    assert seen == ["copisteria"]
    assert response.headers["x-engine"] == "copisteria 1.3.4"
    assert json.loads(response.headers["x-page-report"])[0]["noteheads"] == 40


def test_an_engine_the_server_does_not_have_is_refused_before_reading():
    seen: list[str] = []
    response = client(seen).post("/recognise", files={"file": ("photo.png", png(), "image/png")}, data={"engine": "audiveris"})
    assert response.status_code == 400
    assert response.json()["code"] == "engine"
    assert "homr, copisteria" in response.json()["message"]
    assert seen == []


def test_a_server_without_copisteria_has_homr_only():
    body = TestClient(create_app(lambda path, number: Reading(page("1")), other_engines=[])).get("/health").json()
    assert [entry["engine"] for entry in body["engines"]] == ["homr"]


def test_copisteria_s_reading_gives_the_counts_and_nothing_else(tmp_path: Path):
    image = tmp_path / "page-01.png"
    Image.new("L", (2400, 3400), 255).save(image)
    # Two confident measure boxes 4 staff spaces of 25 px tall, one doubtful one that is not counted.
    measures = [{"cls": "measure", "conf": 0.9, "xyxy": [0, 0, 10, 100]}, {"cls": "measure", "conf": 0.9, "xyxy": [0, 0, 10, 100]},
                {"cls": "measure", "conf": 0.2, "xyxy": [0, 0, 10, 400]}]
    (tmp_path / "page-01.dets_v7_1792.json").write_text(json.dumps(measures))
    tokens = {
        "1": {"real": {"p": 1.0}, "cls": {"v": "note_eighth"}},
        "2": {"real": {"p": 0.9}, "cls": {"v": "note_half"}},
        "3": {"real": {"p": 0.1}, "cls": {"v": "note_half"}},
        "4": {"real": {"p": 1.0}, "cls": {"v": "barLine_single"}},
        "5": {"real": {"p": 1.0}, "cls": {"v": "clef_G2"}},
    }
    reading = tmp_path / "reading.json"
    reading.write_text(json.dumps({"tokens": tokens, "changes": [{"sym": 2, "text": "Andante"}], "staves": 2, "systems": 1}))
    assert copisteria_reader.read_found(reading, image) == Found(staves=2, noteheads=2, bar_lines=1, space_px=20.0)


def test_a_reading_that_is_missing_gives_no_counts(tmp_path: Path):
    image = tmp_path / "page-01.png"
    Image.new("L", (100, 100), 255).save(image)
    assert copisteria_reader.read_found(tmp_path / "none.json", image) == Found()


def test_the_engines_setting_switches_installed_engines_off(monkeypatch):
    for reader in (copisteria_reader, audiveris_reader, transcoda_reader):
        monkeypatch.setattr(reader, "is_installed", lambda: True)
    assert [engine.name for engine in engines.others("")] == ["copisteria", "audiveris", "transcoda"]
    assert [engine.name for engine in engines.others(" homr, Copisteria ,audiveris")] == ["copisteria", "audiveris"]
    monkeypatch.setenv("ENGINES", "homr,transcoda")
    assert [engine.name for engine in engines.others()] == ["transcoda"]
    monkeypatch.setattr(transcoda_reader, "is_installed", lambda: False)
    assert engines.others() == []
