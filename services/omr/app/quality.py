"""Looks at a page image before homr reads it: how sharp it is, how dark its print is, and which way up it lies.

Phone photos of a book are often sideways or upside down in the pixels themselves, with no orientation tag
to say so, and homr reads pixels as stored. Staff lines tell which way a page lies. Cut the page into bands
across its height: a band that holds a staff has long runs of ink across most of its width, so the right
way round has many such bands and the page turned a quarter has few. Which end of the staves is the start
tells upright from upside down: each system opens with a brace (and often a bar number) just before its
staff lines begin, while the lines end on a bar line with nothing after it. When the signs are not clear
the page is left as it is.

Everything here uses Pillow only and is measured on a copy scaled to WORK_SIDE pixels on its long side, so
the numbers mean the same for a 12 megapixel photo and a 300 dpi scan. It never sees or returns the music.
"""

from __future__ import annotations

from dataclasses import dataclass

from PIL import Image, ImageChops, ImageFilter

WORK_SIDE = 2000

# On a page whose print is fainter than this the signs are too weak to turn it (grey levels, 0-255).
MIN_CONTRAST = 80
# Print counts as ink when it is at least 40 % as dark as the darkest print on the page.
INK_SHARE = 0.4
# A piece of line: RUN pixels side by side, at least LINE_FILL of 255 of them ink.
RUN = 12
LINE_FILL = 160
# The page is cut into BANDS bands. A column of a band belongs to a staff when LINE_ROWS of its rows hold
# a piece of line (several staff lines pass, where a stem or a bar line seen sideways is one or two), and
# the band holds a staff when such columns span STAFF_WIDTH of the page. Set on Andrei's 14 photos and on
# drawn pages (research/songbook-scan/measurement.md).
BANDS = 16
LINE_ROWS = 3
STAFF_WIDTH = 0.25
# A way round is taken when it has at least MIN_STAFF_BANDS such bands and half as many again as the
# page turned a quarter.
MIN_STAFF_BANDS = 6
CLEARER = 1.5
# Ink is looked for this share of the width before and after the staff lines; the start must
# outweigh the end (or the reverse) by BRACE.
REACH = 0.012
BRACE = 1.6


@dataclass(frozen=True)
class Look:
    sharpness: float  # how steep the edges of the print are against its contrast; about 1 is crisp
    contrast: int  # how much darker the darkest print is than the paper around it, in grey levels (0-255)
    rotation: int  # degrees the page was turned clockwise before reading: 0, 90, 180 or 270


@dataclass(frozen=True)
class _Staves:
    bands: int  # bands that hold a staff
    before: int  # ink just before the staff lines begin
    after: int  # ink just after they end


_TURNS = {
    90: Image.Transpose.ROTATE_270,  # Pillow turns counterclockwise
    180: Image.Transpose.ROTATE_180,
    270: Image.Transpose.ROTATE_90,
}


def examine(image: Image.Image) -> tuple[Image.Image, Look]:
    """Returns the page in greyscale, turned the right way up, and what was seen."""
    grey = image.convert("L")
    work = grey.copy()
    work.thumbnail((WORK_SIDE, WORK_SIDE), Image.Resampling.BOX)

    ink = _ink(work)
    contrast = _percentile(ink, 0.995)
    rotation = 0
    if contrast >= MIN_CONTRAST:
        rotation = _rotation(ink.point(lambda level: 255 if level >= contrast * INK_SHARE else 0))
    look = Look(sharpness=_sharpness(work, contrast), contrast=contrast, rotation=rotation)
    return (grey.transpose(_TURNS[rotation]) if rotation else grey), look


def _ink(grey: Image.Image) -> Image.Image:
    """How much darker each pixel is than the paper around it, so shadows and uneven light drop out."""
    width, height = grey.size
    paper = grey.reduce(16).filter(ImageFilter.MaxFilter(5))
    return ImageChops.subtract(paper.resize((width, height), Image.Resampling.BILINEAR), grey)


def _percentile(image: Image.Image, fraction: float) -> int:
    histogram = image.histogram()
    wanted = fraction * sum(histogram)
    seen = 0
    for level, count in enumerate(histogram):
        seen += count
        if seen >= wanted:
            return level
    return 255


def _sharpness(grey: Image.Image, contrast: int) -> float:
    """The steepest edges (Laplacian, top half percent) against the print's contrast."""
    if contrast <= 0:
        return 0.0
    edges = grey.filter(ImageFilter.Kernel((3, 3), (0, 1, 0, 1, -4, 1, 0, 1, 0), 1, 0))
    return round(_percentile(edges, 0.995) / contrast, 2)


def _rotation(inked: Image.Image) -> int:
    upright = _staves(inked)
    quarter = _staves(inked.transpose(Image.Transpose.ROTATE_90))
    if upright.bands >= max(MIN_STAFF_BANDS, quarter.bands * CLEARER):
        found, turn = upright, 0
    elif quarter.bands >= max(MIN_STAFF_BANDS, upright.bands * CLEARER):
        found, turn = quarter, 270
    else:
        return 0
    if found.before >= found.after * BRACE:
        return turn
    if found.after >= found.before * BRACE:
        return (turn + 180) % 360
    return 0


def _staves(inked: Image.Image) -> _Staves:
    """Bands down the page that hold a staff, and the ink just before and after their staff lines."""
    width, height = inked.size
    lines = inked.resize((width // RUN, height), Image.Resampling.BOX).point(lambda level: 255 if level >= LINE_FILL else 0)
    cells = lines.size[0]
    band = height // BANDS
    reach = max(2, int(cells * REACH)) * RUN
    bands = before = after = 0
    for index in range(BANDS):
        top, bottom = index * band, (index + 1) * band
        cover = lines.crop((0, top, cells, bottom)).resize((cells, 1), Image.Resampling.BOX).get_flattened_data()
        columns = [x for x, level in enumerate(cover) if level * band >= LINE_ROWS * 255]
        if len(columns) < cells * STAFF_WIDTH:
            continue
        start, end = columns[0] * RUN, (columns[-1] + 1) * RUN
        bands += 1
        before += _count(inked, (max(0, start - reach), top, max(0, start - 4), bottom))
        after += _count(inked, (min(width, end + 4), top, min(width, end + reach), bottom))
    return _Staves(bands, before, after)


def _count(inked: Image.Image, box: tuple[int, int, int, int]) -> int:
    if box[2] <= box[0]:
        return 0
    return inked.crop(box).histogram()[255]
