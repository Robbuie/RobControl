"""Draws the application icon. Build-time only; nothing here ships.

Ported from NetControl's tools/icon.py. The family shares a mark as well as a
stylesheet: a dark rounded tile, light shapes on it, one stroke of the accent.

RobControl is a robot arm in front of a sheet: the arm is the controller, the
sheet behind it the backup of it. The tool flange and gripper are drawn in
amber - RobControl's own default accent (Theme.DefaultAccent) - so the family
reads as a set without two members sharing a colour. At 16px it is a light
L-shape with an amber tip, which is still that.

    pip install pillow
    python tools/icon.py

Writes `src/RobControl.App/Assets/RobControl.ico` and `assets/icon.png`.
"""

from __future__ import annotations

import os

from PIL import Image, ImageDraw

CANVAS = 1024
SCALE = CANVAS // 256

TILE_TOP = (32, 36, 44)
TILE_BOTTOM = (16, 18, 22)
CARD = (233, 237, 244)
CARD_DIM = (203, 210, 221)
ROW = (168, 176, 189)
SHEET = (70, 77, 90)
SHEET_ROW = (98, 106, 120)
ACCENT = (242, 165, 60)

ICO_SIZES = (16, 24, 32, 48, 64, 128, 256)


def _px(value: float) -> int:
    return int(round(value * SCALE))


def _limb(canvas: ImageDraw.ImageDraw, a: tuple[int, int], b: tuple[int, int], width: int, fill) -> None:
    canvas.line((a, b), fill=fill, width=width)
    r = width // 2
    for x, y in (a, b):
        canvas.ellipse((x - r, y - r, x + r, y + r), fill=fill)


def draw() -> Image.Image:
    image = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
    canvas = ImageDraw.Draw(image)

    gradient = Image.new("RGB", (1, CANVAS))
    for y in range(CANVAS):
        ratio = y / (CANVAS - 1)
        gradient.putpixel((0, y), tuple(
            int(round(top + (bottom - top) * ratio))
            for top, bottom in zip(TILE_TOP, TILE_BOTTOM)
        ))
    gradient = gradient.resize((CANVAS, CANVAS))
    mask = Image.new("L", (CANVAS, CANVAS), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, CANVAS - 1, CANVAS - 1), radius=_px(58), fill=255)
    image.paste(gradient, (0, 0), mask)

    # The backup: a sheet behind the arm, top right, with listing rows on it.
    sheet = (_px(120), _px(40), _px(218), _px(150))
    canvas.rounded_rectangle(sheet, radius=_px(12), fill=SHEET)
    row_h = _px(7)
    for i, frac in enumerate((1.0, 0.7, 0.85, 0.5)):
        y = sheet[1] + _px(16) + i * _px(18)
        x1 = sheet[0] + _px(14) + (sheet[2] - sheet[0] - _px(28)) * frac
        canvas.rounded_rectangle((sheet[0] + _px(14), y, x1, y + row_h), radius=row_h // 2, fill=SHEET_ROW)

    # The arm: a base on the floor, a lower link up, an upper link across, the flange in amber.
    base = (_px(40), _px(196), _px(120), _px(218))
    canvas.rounded_rectangle(base, radius=_px(8), fill=CARD)
    canvas.rounded_rectangle((_px(62), _px(176), _px(98), _px(200)), radius=_px(6), fill=CARD_DIM)

    shoulder = (_px(80), _px(176))
    elbow = (_px(96), _px(92))
    wrist = (_px(172), _px(120))
    _limb(canvas, shoulder, elbow, _px(24), CARD)
    _limb(canvas, elbow, wrist, _px(20), CARD_DIM)
    for x, y, r in ((*shoulder, _px(10)), (*elbow, _px(9))):
        canvas.ellipse((x - r, y - r, x + r, y + r), fill=ROW)

    # Flange and gripper - the one thing in colour.
    tip = (_px(196), _px(150))
    _limb(canvas, wrist, tip, _px(14), ACCENT)
    jaw = _px(9)
    canvas.rounded_rectangle((tip[0] - _px(18), tip[1] + _px(2), tip[0] - _px(18) + jaw, tip[1] + _px(28)), radius=_px(4), fill=ACCENT)
    canvas.rounded_rectangle((tip[0] + _px(6), tip[1] - _px(10), tip[0] + _px(6) + jaw, tip[1] + _px(18)), radius=_px(4), fill=ACCENT)
    return image


def main() -> int:
    here = os.path.dirname(os.path.abspath(__file__))
    root = os.path.dirname(here)
    master = draw()
    small = master.resize((256, 256), Image.LANCZOS)

    ico = os.path.join(root, "src", "RobControl.App", "Assets", "RobControl.ico")
    os.makedirs(os.path.dirname(ico), exist_ok=True)
    small.save(ico, format="ICO", sizes=[(s, s) for s in ICO_SIZES])

    png = os.path.join(root, "assets", "icon.png")
    os.makedirs(os.path.dirname(png), exist_ok=True)
    small.save(png, format="PNG")
    print(f"wrote {ico}\nwrote {png}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
