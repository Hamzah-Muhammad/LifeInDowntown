"""Signs for the Pantages P1 garage (docs/DESIGN.md 3O.3): our own render, 4 x 8 cells of 256 x 128 px.

Cells 0-23: bay numbers P1-01 .. P1-24 (painted wall plates); then the garage signs below.
Writes Assets/City/Signs/garage_signs.png (albedo) and garage_signs_emissive.png (only EXIT glows).
"""
import pathlib
from PIL import Image, ImageDraw, ImageFont

OUT = pathlib.Path(__file__).resolve().parents[2] / "Assets" / "City" / "Signs"
BOLD = r"C:\Windows\Fonts\arialbd.ttf"
CW, CH, COLS, ROWS = 256, 128, 4, 8
EXTRA = [  # (text, background, ink, glows)
    ("EXIT", (20, 120, 40), (255, 255, 255), True),
    ("P1", (250, 200, 30), (20, 20, 20), False),
    ("RESIDENTS ONLY", (25, 60, 140), (255, 255, 255), False),
    ("ELEVATOR", (240, 240, 236), (20, 20, 20), False),
    ("VISITOR", (240, 240, 236), (20, 20, 20), False),
    ("NO PARKING", (200, 30, 30), (255, 255, 255), False),
    ("EXIT TO SHUTER ST", (20, 120, 40), (255, 255, 255), True),
    ("PANTAGES PARKING", (25, 25, 28), (250, 200, 30), False),
]

img = Image.new("RGB", (CW * COLS, CH * ROWS), (0, 0, 0))
glow = Image.new("RGB", img.size, (0, 0, 0))
d, g = ImageDraw.Draw(img), ImageDraw.Draw(glow)
cells = [(f"P1-{i:02d}", (238, 238, 232), (20, 20, 20), False) for i in range(1, 25)] + EXTRA
for k, (text, bg, ink, lit) in enumerate(cells):
    x0, y0 = (k % COLS) * CW, (k // COLS) * CH
    box = [x0 + 4, y0 + 4, x0 + CW - 5, y0 + CH - 5]
    d.rectangle(box, fill=bg)
    size = 64 if len(text) <= 6 else 30 if len(text) <= 14 else 24
    font = ImageFont.truetype(BOLD, size)
    d.text((x0 + CW / 2, y0 + CH / 2), text, font=font, fill=ink, anchor="mm")
    if lit:
        g.rectangle(box, fill=bg)
        g.text((x0 + CW / 2, y0 + CH / 2), text, font=font, fill=ink, anchor="mm")
OUT.mkdir(parents=True, exist_ok=True)
img.save(OUT / "garage_signs.png", optimize=True)
glow.save(OUT / "garage_signs_emissive.png", optimize=True)
print(f"garage signs: {len(cells)} cells")
