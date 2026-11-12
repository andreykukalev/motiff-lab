from random import Random

import pytest
from PIL import Image, ImageDraw, ImageFilter

from app import quality

SPACE = 12


def score_page(ink: int = 0, paper: int = 255) -> Image.Image:
    """A drawn piano page: six systems of two staves, each opening with a brace, clefs and a key signature,
    with bar lines and notes; enough of what a printed page has for the checks to go by."""
    page = Image.new("L", (1240, 1754), paper)
    draw = ImageDraw.Draw(page)
    left, right = 150, 1150
    random = Random(7)
    for system in range(6):
        top = 170 + system * 250
        staves = [top, top + 9 * SPACE]
        bottom = staves[1] + 4 * SPACE
        for staff in staves:
            for line in range(5):
                draw.line((left, staff + line * SPACE, right, staff + line * SPACE), fill=ink, width=2)
            draw.ellipse((left + 8, staff - SPACE, left + 34, staff + 5 * SPACE), outline=ink, width=5)
            draw.rectangle((left + 44, staff + SPACE, left + 52, staff + 3 * SPACE), fill=ink)
            for _ in range(14):
                x = random.randrange(left + 70, right - 30)
                y = staff + random.randrange(-2, 10) * SPACE // 2
                draw.ellipse((x, y - 5, x + 14, y + 5), fill=ink)
                draw.line((x + 13, y, x + 13, y - 3 * SPACE), fill=ink, width=2)
        # The brace, just before the staff lines, and the bar lines through both staves.
        middle = (top + bottom) // 2
        draw.arc((left - 24, top, left - 6, middle), 90, 270, fill=ink, width=6)
        draw.arc((left - 24, middle, left - 6, bottom), 90, 270, fill=ink, width=6)
        for x in [left] + [left + bar * (right - left) // 4 for bar in range(1, 5)]:
            draw.line((x, top, x, bottom), fill=ink, width=3)
    return page


def test_an_upright_page_is_left_as_it_is():
    page = score_page()
    turned, look = quality.examine(page)
    assert look.rotation == 0
    assert turned.size == page.size


@pytest.mark.parametrize(("stored", "turn"), [(Image.Transpose.ROTATE_90, 90), (Image.Transpose.ROTATE_180, 180), (Image.Transpose.ROTATE_270, 270)])
def test_a_page_stored_sideways_or_upside_down_is_turned_upright(stored, turn):
    upright = score_page()
    turned, look = quality.examine(upright.transpose(stored))
    assert look.rotation == turn
    assert turned.size == upright.size
    assert list(turned.get_flattened_data()) == list(upright.get_flattened_data())


def test_a_page_without_staves_is_left_as_it_is():
    page = Image.new("L", (1240, 1754), 255)
    ImageDraw.Draw(page).text((300, 400), "Contents", fill=0)
    _, look = quality.examine(page.transpose(Image.Transpose.ROTATE_90))
    assert look.rotation == 0


def test_a_faint_page_is_measured_but_never_turned():
    page = score_page(ink=170, paper=200).transpose(Image.Transpose.ROTATE_180)
    _, look = quality.examine(page)
    assert look.contrast < quality.MIN_CONTRAST
    assert look.rotation == 0


def test_contrast_is_how_much_darker_the_print_is_than_the_paper():
    _, crisp = quality.examine(score_page())
    _, grey = quality.examine(score_page(ink=120, paper=220))
    assert crisp.contrast > 200
    assert 80 < grey.contrast < 110


def test_a_blurred_page_is_less_sharp():
    _, crisp = quality.examine(score_page())
    _, blurred = quality.examine(score_page().filter(ImageFilter.GaussianBlur(3)))
    assert blurred.sharpness < crisp.sharpness / 2


def test_a_large_photo_is_measured_on_a_working_copy_and_kept_whole():
    page = score_page().resize((2480, 3508))
    turned, look = quality.examine(page.transpose(Image.Transpose.ROTATE_270))
    assert look.rotation == 270
    assert turned.size == (2480, 3508)
