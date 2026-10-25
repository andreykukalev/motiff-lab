import io
from pathlib import Path

from fastapi.testclient import TestClient
from PIL import Image

from app.main import MUSICXML_TYPE, create_app
from app.recognise import RecogniseError
from tests.test_merge import numbers, page


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


def client(read=lambda path, number: page("1", "2")) -> TestClient:
    return TestClient(create_app(read))


def test_health_names_the_engine():
    body = client().get("/health").json()
    assert body["engine"] == "homr"


def test_a_photo_comes_back_as_musicxml_and_its_folder_is_gone():
    seen: list[Path] = []

    def read(path: Path, number: int) -> bytes:
        assert path.exists() and path.suffix == ".png"
        seen.append(path)
        return page("1", "2")

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
    def read(path: Path, number: int) -> bytes:
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
