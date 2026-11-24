"""Turns an uploaded PDF or photo into page images for homr.

Everything happens in memory and in the job's own temporary folder; nothing is written anywhere else.
Each page is looked at first (quality.py) and measured for sharpness and contrast. A photo, or a PDF page
that is only a picture (a scan), is turned the right way up if it lies sideways or upside down. A PDF page
drawn by a program (its text and lines are in the PDF) lies as its author made it and is never turned: on
such a clean engraving the signs of which way up a page lies can mislead, and a page read upside down is
different music (issue #479 item 15).
"""

from __future__ import annotations

import io
from dataclasses import dataclass
from pathlib import Path

from PIL import Image, ImageOps, UnidentifiedImageError

from app import quality, recognise

# 300 dpi is what homr was tested at (docs/architecture/adr/0011-sheet-music-recognition.md).
PDF_DPI = 300

# Larger photos are scaled down to this many pixels on the long side: homr resizes to a working size
# anyway, and smaller pages keep memory and time down.
MAX_LONG_SIDE = 4200


class PageError(ValueError):
    """The upload cannot be turned into pages. The message is plain words for the player."""


@dataclass(frozen=True)
class Page:
    path: Path
    look: quality.Look


@dataclass(frozen=True)
class Upload:
    kind: str  # "pdf" or "image"
    data: bytes


def sniff(data: bytes) -> Upload:
    """Recognises a PDF, PNG or JPEG by its content, not its name."""
    if data.startswith(b"%PDF"):
        return Upload("pdf", data)
    if data.startswith(b"\x89PNG") or data.startswith(b"\xff\xd8\xff"):
        return Upload("image", data)
    raise PageError("Scanning reads PDF, PNG and JPEG files.")


def count_pages(upload: Upload) -> int:
    if upload.kind == "image":
        return 1
    import pypdfium2 as pdfium

    try:
        pdf = pdfium.PdfDocument(upload.data)
    except pdfium.PdfiumError as error:
        raise PageError("This PDF cannot be opened. It may be damaged or protected by a password.") from error
    try:
        return len(pdf)
    finally:
        pdf.close()


def write_pages(upload: Upload, folder: Path, max_pages: int, numbers: list[int] | None = None) -> list[Page]:
    """Writes each page, or those numbered (1 is the first, in order, no repeats), the right way up, as a greyscale
    PNG into the folder, in order."""
    if upload.kind == "image":
        if numbers not in (None, [1]):
            raise PageError("A photo has one page.")
        return [_write(_open_image(upload.data), folder, 1)]

    import pypdfium2 as pdfium

    try:
        pdf = pdfium.PdfDocument(upload.data)
    except pdfium.PdfiumError as error:
        raise PageError("This PDF cannot be opened. It may be damaged or protected by a password.") from error
    try:
        if len(pdf) == 0:
            raise PageError("This PDF has no pages.")
        if numbers is None and len(pdf) > max_pages:
            raise PageError(f"This PDF has {len(pdf)} pages. Scanning reads up to {max_pages} pages at a time.")
        if numbers is not None and len(numbers) > max_pages:
            raise PageError(f"{len(numbers)} pages were chosen. Scanning reads up to {max_pages} pages at a time.")
        if numbers is not None and not all(1 <= number <= len(pdf) for number in numbers):
            raise PageError(f"This PDF has {len(pdf)} pages, so only pages 1 to {len(pdf)} can be read.")
        written = []
        for index in (range(len(pdf)) if numbers is None else [number - 1 for number in numbers]):
            page = pdf[index]
            try:
                image = page.render(scale=PDF_DPI / 72, grayscale=True).to_pil()
                turn = not drawn(page)
            finally:
                page.close()
            written.append(_write(image, folder, index + 1, turn, PDF_DPI))
        return written
    finally:
        pdf.close()


def page_report(number: int, look: quality.Look, found: recognise.Found, musicxml: bytes | None) -> dict[str, int | float]:
    """One page's report: only counts and measurements, never the music. What was seen of the photo before the engine
    (sharpness, contrast, how far it was turned), what the engine found (staves, noteheads, bar lines, the height of a
    notehead in its working copy, about one staff space) and how many measures came out."""
    return {
        "page": number,
        "rotation": look.rotation,
        "sharpness": look.sharpness,
        "contrast": look.contrast,
        "staves": found.staves,
        "noteheads": found.noteheads,
        "barLines": found.bar_lines,
        "spacePx": found.space_px,
        "measures": recognise.count_measures(musicxml) if musicxml is not None else 0,
    }


def drawn(page) -> bool:
    """Whether a PDF page was drawn by a program: it holds text or lines, not only pictures."""
    import pypdfium2.raw as pdfium_c

    return any(each.type in (pdfium_c.FPDF_PAGEOBJ_TEXT, pdfium_c.FPDF_PAGEOBJ_PATH) for each in page.get_objects())


def _open_image(data: bytes) -> Image.Image:
    try:
        image = Image.open(io.BytesIO(data))
        image.load()
    except (UnidentifiedImageError, OSError) as error:
        raise PageError("This image cannot be read. It may be damaged.") from error
    # Phone photos are often stored sideways with an orientation tag; homr reads pixels as stored.
    return ImageOps.exif_transpose(image)


def _write(image: Image.Image, folder: Path, number: int, turn: bool = True, dpi: int | None = None) -> Page:
    image, look = quality.examine(image, turn)
    long_side = max(image.size)
    if long_side > MAX_LONG_SIDE:
        image.thumbnail((MAX_LONG_SIDE, MAX_LONG_SIDE), Image.Resampling.LANCZOS)
        dpi = round(dpi * max(image.size) / long_side) if dpi else None
    path = folder / f"page-{number:02d}.png"
    # Saving through Pillow also drops the photo's metadata (location, camera), which homr does not need. A PDF page
    # keeps the resolution it was rendered at, for the page's evidence (evidence.py); a photo has none to tell.
    image.save(path, format="PNG", **({"dpi": (dpi, dpi)} if dpi else {}))
    return Page(path, look)
