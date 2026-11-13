import io
from pathlib import Path

from PIL import Image

from app import pages
from tests.test_quality import score_page

WIDTH, HEIGHT = 595, 842


def pdf_of(picture: Image.Image, drawn: bool) -> bytes:
    """A one-page PDF showing the picture over the whole page; `drawn` adds a line drawn by the program, as an
    engraving program's PDF has its staff lines and text as drawing, not as a picture."""
    import pypdfium2 as pdfium
    import pypdfium2.raw as pdfium_c

    document = pdfium.PdfDocument.new()
    page = document.new_page(WIDTH, HEIGHT)
    image = pdfium.PdfImage.new(document)
    image.set_bitmap(pdfium.PdfBitmap.from_pil(picture.convert("RGB")))
    image.set_matrix(pdfium.PdfMatrix().scale(WIDTH, HEIGHT))
    page.insert_obj(image)
    if drawn:
        line = pdfium_c.FPDFPageObj_CreateNewRect(40, 40, 100, 1)
        pdfium_c.FPDFPath_SetDrawMode(line, pdfium_c.FPDF_FILLMODE_ALTERNATE, False)
        pdfium_c.FPDFPage_InsertObject(page.raw, line)
    page.gen_content()
    buffer = io.BytesIO()
    document.save(buffer)
    return buffer.getvalue()


def test_a_scanned_pdf_page_lying_upside_down_is_turned_upright(tmp_path: Path):
    written = pages.write_pages(pages.sniff(pdf_of(score_page().rotate(180), drawn=False)), tmp_path, 8)
    assert written[0].look.rotation == 180


def test_a_pdf_page_drawn_by_a_program_is_read_as_it_is(tmp_path: Path):
    # Bug 15 (issue #479): the hosted server turned page 1 of a clean engraving upside down, because the ink
    # at the ends of its staff lines looked like a brace at the right; homr then read it as other music.
    # A page drawn by a program lies as its author made it, whatever the picture on it looks like.
    written = pages.write_pages(pages.sniff(pdf_of(score_page().rotate(180), drawn=True)), tmp_path, 8)
    assert written[0].look.rotation == 0
    assert written[0].look.contrast > 200


def test_only_text_and_lines_make_a_page_drawn():
    import pypdfium2 as pdfium

    scanned = pdfium.PdfDocument(pdf_of(score_page(), drawn=False))
    drawn = pdfium.PdfDocument(pdf_of(score_page(), drawn=True))
    assert not pages.drawn(scanned[0])
    assert pages.drawn(drawn[0])
