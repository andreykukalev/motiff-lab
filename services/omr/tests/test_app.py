import io
import json
from pathlib import Path

from fastapi.testclient import TestClient
from PIL import Image

from app.main import MUSICXML_TYPE, create_app
from app.recognise import Found, Reading, RecogniseError, count_measures, read_log
from tests.test_merge import numbers, page
from tests.test_quality import score_page


def png(size=(400, 300)) -> bytes:
    buffer = io.BytesIO()
    Image.new("RGB", size, "white").save(buffer, format="PNG")
    return buffer.getvalue()


def pdf(pages: int) -> bytes:
    import pypdfium2 as pdfium

    document = pdfium.PdfDocument.new()
    for _ in range(pages):
        document.new_page(595, 842)
    buffer = io.BytesIO()
    document.save(buffer)
    return buffer.getvalue()


def client(read=lambda path, number: Reading(page("1", "2"))) -> TestClient:
    return TestClient(create_app(read))


def test_health_names_the_engine():
    body = client().get("/health").json()
    assert body["engine"] == "homr"


def test_a_photo_comes_back_as_musicxml_and_its_folder_is_gone():
    seen: list[Path] = []

    def read(path: Path, number: int) -> Reading:
        assert path.exists() and path.suffix == ".png"
        seen.append(path)
        return Reading(page("1", "2"))

    response = client(read).post("/recognise", files={"file": ("photo.jpg", png(), "image/png")})
    assert response.status_code == 200
    assert response.headers["content-type"].startswith(MUSICXML_TYPE)
    assert response.headers["x-pages"] == "1"
    assert numbers(response.content) == ["1", "2"]
    assert not seen[0].parent.exists()


def test_each_pdf_page_is_read_and_joined():
    response = client().post("/recognise", files={"file": ("score.pdf", pdf(3), "application/pdf")})
    assert response.status_code == 200
    assert response.headers["x-pages"] == "3"
    assert numbers(response.content) == ["1", "2", "3", "4", "5", "6"]


def test_too_many_pages_are_refused():
    response = client().post("/recognise", files={"file": ("score.pdf", pdf(9), "application/pdf")})
    assert response.status_code == 422
    assert "up to 8 pages" in response.json()["message"]


def test_other_files_are_refused_by_content():
    response = client().post("/recognise", files={"file": ("score.pdf", b"MThd\x00\x00\x00\x06", "application/pdf")})
    assert response.status_code == 415


def test_empty_files_are_refused():
    response = client().post("/recognise", files={"file": ("score.png", b"", "image/png")})
    assert response.status_code == 400


def test_a_page_the_engine_cannot_read_gives_its_reason():
    def read(path: Path, number: int) -> Reading:
        raise RecogniseError(f"No music staves were found on page {number}.")

    response = client(read).post("/recognise", files={"file": ("photo.png", png(), "image/png")})
    assert response.status_code == 422
    assert response.json()["message"] == "No music staves were found on page 1."


def test_the_app_may_call_from_the_internet():
    response = client().options(
        "/recognise",
        headers={
            "Origin": "https://motiff.example",
            "Access-Control-Request-Method": "POST",
            "Access-Control-Request-Private-Network": "true",
        },
    )
    assert response.status_code == 200
    assert response.headers["access-control-allow-private-network"] == "true"


def test_a_hosted_server_answers_only_the_app_and_its_previews():
    hosted = TestClient(
        create_app(
            lambda path, number: Reading(page("1", "2")),
            allowed_origins=["https://motiff.example"],
            allowed_origin_regex=r"https://motiff-[0-9]+\.example",
        )
    )

    def preflight(origin: str):
        return hosted.options("/recognise", headers={"Origin": origin, "Access-Control-Request-Method": "POST"})

    assert preflight("https://motiff.example").headers["access-control-allow-origin"] == "https://motiff.example"
    assert preflight("https://motiff-12.example").headers["access-control-allow-origin"] == "https://motiff-12.example"
    assert "access-control-allow-origin" not in preflight("https://elsewhere.example").headers


HOMR_LOG = b"""Processing page-01.png
Creating bounds for noteheads
Found 1001 staff line fragments
Found 174 noteheads
Average note head height: 13.04
Found 54 bar lines
Found 30 clefs
Found 10 staffs
Found 5 connected staffs (after merging grand staffs, multiple voices):  [1, 1, 1, 1, 1]
Found title: Andante
Finished parsing 1 staves
"""


def jpeg(image: Image.Image) -> bytes:
    buffer = io.BytesIO()
    image.save(buffer, format="JPEG")
    return buffer.getvalue()


def test_homr_s_log_gives_its_counts_and_nothing_else():
    assert read_log(HOMR_LOG) == Found(staves=10, noteheads=174, bar_lines=54, space_px=13.0)
    assert read_log(b"Found 3 staff line fragments\nNo staffs found") == Found()


def test_measures_are_counted_in_the_part_that_has_most():
    assert count_measures(page("1", "2", "3")) == 3
    assert count_measures(b"not music") == 0


def test_each_page_is_reported_in_numbers_only():
    response = client(lambda path, number: Reading(page("1", "2", "3"), read_log(HOMR_LOG))).post(
        "/recognise", files={"file": ("score.pdf", pdf(2), "application/pdf")}
    )
    report = json.loads(response.headers["x-page-report"])
    assert [entry["page"] for entry in report] == [1, 2]
    assert report[0] == {
        "page": 1,
        "rotation": 0,
        "sharpness": report[0]["sharpness"],
        "contrast": report[0]["contrast"],
        "staves": 10,
        "noteheads": 174,
        "barLines": 54,
        "spacePx": 13.0,
        "measures": 3,
    }
    assert all(isinstance(value, (int, float)) for entry in report for value in entry.values())
    assert "Andante" not in response.headers["x-page-report"]


def test_a_photo_stored_upside_down_is_read_the_right_way_up():
    upright = score_page()
    seen: list[Image.Image] = []

    def read(path: Path, number: int) -> Reading:
        seen.append(Image.open(path).copy())
        return Reading(page("1"))

    response = client(read).post("/recognise", files={"file": ("photo.jpg", jpeg(upright.rotate(180)), "image/jpeg")})
    report = json.loads(response.headers["x-page-report"])
    assert report[0]["rotation"] == 180
    assert report[0]["contrast"] > 200
    assert seen[0].size == upright.size


def test_a_page_that_cannot_be_read_still_says_what_was_seen():
    def read(path: Path, number: int) -> Reading:
        raise RecogniseError(f"No music staves were found on page {number}.", Found(noteheads=12))

    response = client(read).post("/recognise", files={"file": ("photo.png", png(), "image/png")})
    assert response.status_code == 422
    report = json.loads(response.headers["x-page-report"])
    assert report[0]["noteheads"] == 12
    assert report[0]["measures"] == 0


def test_the_app_may_read_the_page_report():
    simple = client().post("/recognise", files={"file": ("photo.png", png(), "image/png")}, headers={"Origin": "https://motiff.example"})
    assert "X-Page-Report" in simple.headers["access-control-expose-headers"]
