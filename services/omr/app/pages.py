"""Turns an uploaded PDF or photo into page images for homr.

Everything happens in memory and in the job's own temporary folder; nothing is written anywhere else.
"""

from __future__ import annotations

import io
from dataclasses import dataclass
from pathlib import Path

from PIL import Image, ImageOps, UnidentifiedImageError

# 300 dpi is what homr was tested at (docs/decisions/0011-sheet-music-recognition.md).
PDF_DPI = 300

# Larger photos are scaled down to this many pixels on the long side: homr resizes to a working size
# anyway, and smaller pages keep memory and time down.
MAX_LONG_SIDE = 4200


class PageError(ValueError):
    """The upload cannot be turned into pages. The message is plain words for the player."""


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


def write_pages(upload: Upload, folder: Path, max_pages: int) -> list[Path]:
    """Writes each page as a greyscale PNG into the folder, in order, and returns the paths."""
    if upload.kind == "image":
        return [_write(_open_image(upload.data), folder, 1)]

    import pypdfium2 as pdfium

    try:
        pdf = pdfium.PdfDocument(upload.data)
    except pdfium.PdfiumError as error:
        raise PageError("This PDF cannot be opened. It may be damaged or protected by a password.") from error
    try:
        if len(pdf) == 0:
            raise PageError("This PDF has no pages.")
        if len(pdf) > max_pages:
            raise PageError(f"This PDF has {len(pdf)} pages. Scanning reads up to {max_pages} pages at a time.")
        paths = []
        for index in range(len(pdf)):
            page = pdf[index]
            try:
                image = page.render(scale=PDF_DPI / 72, grayscale=True).to_pil()
            finally:
                page.close()
            paths.append(_write(image, folder, index + 1))
        return paths
    finally:
        pdf.close()


def _open_image(data: bytes) -> Image.Image:
    try:
        image = Image.open(io.BytesIO(data))
        image.load()
    except (UnidentifiedImageError, OSError) as error:
        raise PageError("This image cannot be read. It may be damaged.") from error
    # Phone photos are often stored sideways with an orientation tag; homr reads pixels as stored.
    return ImageOps.exif_transpose(image)


def _write(image: Image.Image, folder: Path, number: int) -> Path:
    image = image.convert("L")
    long_side = max(image.size)
    if long_side > MAX_LONG_SIDE:
        image.thumbnail((MAX_LONG_SIDE, MAX_LONG_SIDE), Image.Resampling.LANCZOS)
    path = folder / f"page-{number:02d}.png"
    # Saving through Pillow also drops the photo's metadata (location, camera), which homr does not need.
    image.save(path, format="PNG")
    return path
