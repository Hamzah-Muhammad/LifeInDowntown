"""TTC subway signs, drawn in code (docs/DESIGN.md 3N.5): one atlas the station and train builders sample.

Writes  Assets/City/Signs/ttc_signs.png           albedo atlas
        Assets/City/Signs/ttc_tile_wall.png, ttc_tile_floor.png  repeating tile textures
        Assets/City/Signs/ttc_signs_emissive.png  glow atlas (screens, route signs, lit boxes; black elsewhere)
        Assets/City/Data/ttc_signs.json           {"names": [...], "rects": [u0, v0, u1, v1, ...]} in UV space

Lettering is Bahnschrift (Windows), our own render: the Toronto Subway typeface is not free to embed.
Wall colours are NEUTRAL PLACEHOLDERS until Hamzah's reference photos (3N.9); the murals are not drawn.
"""
import json
import pathlib

from PIL import Image, ImageDraw, ImageFont

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT_ALB = ROOT / "Assets" / "City" / "Signs" / "ttc_signs.png"
OUT_EM = ROOT / "Assets" / "City" / "Signs" / "ttc_signs_emissive.png"
OUT_JSON = ROOT / "Assets" / "City" / "Data" / "ttc_signs.json"
FONT = r"C:\Windows\Fonts\bahnschrift.ttf"
W, H = 2048, 2048
LINE1_YELLOW = (248, 195, 0)      # ~#F8C300, approximate (3N.5)
TTC_RED = (218, 37, 29)
STATIONS = ["Queen", "TMU", "College"]
STRIP = ["Union", "King", "Queen", "TMU", "College", "Wellesley", "Bloor-Yonge"]

alb = Image.new("RGB", (W, H), (0, 0, 0))
em = Image.new("RGB", (W, H), (0, 0, 0))
da, de = ImageDraw.Draw(alb), ImageDraw.Draw(em)
names, rects = [], []
cursor = [0, 0, 0]   # x, y, row height


def font(size):
    return ImageFont.truetype(FONT, size)


def cell(name, w, h):
    x, y, rh = cursor
    if x + w > W:
        x, y, rh = 0, y + rh + 4, 0
    cursor[:] = [x + w + 4, y, max(rh, h)]
    names.append(name)
    # UV origin bottom-left (Unity), image origin top-left
    rects.extend([round(x / W, 6), round(1 - (y + h) / H, 6), round((x + w) / W, 6), round(1 - y / H, 6)])
    return x, y


def text_c(d, box, s, size, fill, weight_px=0):
    f = font(size)
    l, t, r, b = d.textbbox((0, 0), s, font=f)
    x0, y0, x1, y1 = box
    d.text(((x0 + x1 - (r - l)) / 2 - l, (y0 + y1 - (b - t)) / 2 - t), s, font=f, fill=fill,
           stroke_width=weight_px, stroke_fill=fill)


def roundel(d, cx, cy, r, glow=None):
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=LINE1_YELLOW)
    text_c(d, (cx - r, cy - r, cx + r, cy + r), "1", int(r * 1.35), (0, 0, 0), 2)
    if glow:
        glow.ellipse([cx - r, cy - r, cx + r, cy + r], fill=(0, 0, 0))


# station name panels for the track walls: black lettering on a light tile panel (placeholder colour)
for st in STATIONS:
    x, y = cell(f"name_{st}", 1024, 192)
    da.rectangle([x, y, x + 1023, y + 191], fill=(226, 222, 210))
    text_c(da, (x, y, x + 1023, y + 191), st.upper() if st != "TMU" else "TMU", 132, (20, 20, 22), 1)

# Line 1 roundel
x, y = cell("roundel", 256, 256)
roundel(da, x + 128, y + 128, 122)

# direction signs over the stairs down (black band, white text, roundel)
for key, text in (("dir_nb", "Northbound  \u2013  Finch"), ("dir_sb", "Southbound  \u2013  Vaughan Metropolitan Centre via Union")):
    x, y = cell(key, 2040, 128)
    da.rectangle([x, y, x + 2039, y + 127], fill=(18, 18, 20))
    roundel(da, x + 70, y + 64, 50)
    f = font(64)
    da.text((x + 140, y + 30), text, font=f, fill=(245, 245, 245))
    de.rectangle([x, y, x + 2039, y + 127], fill=(28, 28, 28))
    de.text((x + 140, y + 30), text, font=f, fill=(230, 230, 230))

# street sign on a post: red TTC box, station name below
for st in STATIONS:
    x, y = cell(f"street_{st}", 256, 384)
    da.rectangle([x, y, x + 255, y + 383], fill=(24, 24, 26))
    da.rectangle([x + 8, y + 8, x + 247, y + 247], fill=TTC_RED)
    text_c(da, (x + 8, y + 8, x + 247, y + 247), "TTC", 110, (255, 255, 255), 2)
    text_c(da, (x, y + 256, x + 255, y + 383), st.upper(), 56 if len(st) < 6 else 46, (255, 255, 255), 1)
    de.rectangle([x + 8, y + 8, x + 247, y + 247], fill=(120, 18, 14))
    text_c(de, (x + 8, y + 8, x + 247, y + 247), "TTC", 110, (255, 255, 255), 2)

# small signs
SMALL = [
    ("dwa", "Designated\nWaiting Area", (20, 70, 160), (255, 255, 255)),
    ("path_closed", "PATH\nclosed", (60, 60, 64), (255, 255, 255)),
    ("exit", "Exit to\nstreet", (18, 18, 20), (255, 210, 60)),
    ("presto", "PRESTO", (40, 40, 44), (140, 200, 90)),
    ("washroom", "Washrooms\nclosed", (60, 60, 64), (230, 230, 230)),
    ("alarm", "Emergency\nalarm", (200, 30, 30), (255, 255, 255)),
    ("powercut", "Emergency\npower cut", (230, 200, 30), (20, 20, 20)),
    ("elevator", "Elevator", (18, 18, 20), (255, 255, 255)),
]
for key, text, bg, fg in SMALL:
    x, y = cell(key, 384, 192)
    da.rectangle([x, y, x + 383, y + 191], fill=bg)
    f = font(56)
    lines = text.split("\n")
    for i, ln in enumerate(lines):
        cy = y + 96 + (i - (len(lines) - 1) / 2) * 64
        text_c(da, (x, cy - 32, x + 383, cy + 32), ln, 56, fg)
    if key in ("dwa", "exit", "alarm"):
        de.rectangle([x, y, x + 383, y + 191], fill=tuple(int(c * 0.45) for c in bg))
        for i, ln in enumerate(lines):
            cy = y + 96 + (i - (len(lines) - 1) / 2) * 64
            text_c(de, (x, cy - 32, x + 383, cy + 32), ln, 56, fg)

# next-train screens and the train's route signs: amber LED text on black
for key, top, bottom in (("screen_nb", "1  FINCH", "Next train"), ("screen_sb", "1  VMC", "Next train")):
    x, y = cell(key, 512, 160)
    for d in (da, de):
        d.rectangle([x, y, x + 511, y + 159], fill=(6, 6, 8))
        d.text((x + 24, y + 14), bottom, font=font(40), fill=(255, 170, 40))
        d.text((x + 24, y + 66), top, font=font(70), fill=(255, 170, 40))
for key, text in (("route_finch", "1 FINCH"), ("route_vmc", "1 VMC")):
    x, y = cell(key, 512, 128)
    for d in (da, de):
        d.rectangle([x, y, x + 511, y + 127], fill=(6, 6, 8))
        text_c(d, (x, y, x + 511, y + 127), text, 82, (255, 170, 40))

# strip map over the train doors: the Yonge side of Line 1 around these stations
x, y = cell("stripmap", 2040, 160)
da.rectangle([x, y, x + 2039, y + 159], fill=(245, 245, 245))
da.line([x + 150, y + 60, x + 1890, y + 60], fill=LINE1_YELLOW, width=14)
for i, st in enumerate(STRIP):
    px = x + 150 + i * (1740 / (len(STRIP) - 1))
    da.ellipse([px - 16, y + 44, px + 16, y + 76], fill=(255, 255, 255), outline=(20, 20, 20), width=5)
    text_c(da, (px - 140, y + 92, px + 140, y + 150), st, 40, (20, 20, 20))
    de.ellipse([px - 9, y + 51, px + 9, y + 69], fill=(255, 150, 30) if st in STATIONS else (0, 0, 0))

# tile textures (their own files, so they repeat): 4 x 4 tiles of 15 cm with dark grout. Plain placeholder
# tones until the per-station colours are known (3N.9)
for key, tone in (("ttc_tile_wall", (214, 210, 198)), ("ttc_tile_floor", (128, 126, 122))):
    tile = Image.new("RGB", (256, 256), tone)
    td = ImageDraw.Draw(tile)
    for k in range(0, 256, 64):
        td.line([k, 0, k, 255], fill=tuple(c - 40 for c in tone), width=3)
        td.line([0, k, 255, k], fill=tuple(c - 40 for c in tone), width=3)
    tile.save(OUT_ALB.parent / f"{key}.png")

alb.save(OUT_ALB)
em.save(OUT_EM)
OUT_JSON.write_text(json.dumps({"names": names, "rects": rects}), encoding="utf-8")
print(f"{len(names)} signs -> {OUT_ALB.name}, {OUT_EM.name}, {OUT_JSON.name}; atlas rows used to y={cursor[1] + cursor[2]} of {H}")
