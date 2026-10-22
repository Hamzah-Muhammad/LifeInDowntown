"""Store signs and building names from the real OSM places (docs/DESIGN.md 3G).

Reads   Assets/City/Data/downtown_yonge_dundas.json  (buildings, roads)
        Assets/Resources/City/places.json            (named shops, cafes, restaurants...)
Writes  Assets/City/Data/signs.json                  placements + atlas UV rects (read by CityGenerator)
        Assets/City/Signs/signs_albedo.png           sign faces (dark plate + text)
        Assets/City/Signs/signs_emissive.png         what glows (neon text / backlit letters)

Each store sign goes on the building wall nearest its OSM point, facing outward, above the shopfront.
Named buildings get a plate over the street side; towers also get a lit crown sign near the top.
"""
import json
import math
import pathlib
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = pathlib.Path(__file__).resolve().parents[2]
CITY = ROOT / "Assets" / "City" / "Data" / "downtown_yonge_dundas.json"
PLACES = ROOT / "Assets" / "Resources" / "City" / "places.json"
OUT_JSON = ROOT / "Assets" / "City" / "Data" / "signs.json"
OUT_DIR = ROOT / "Assets" / "City" / "Signs"

RADIUS = 200.0            # metres from Yonge & Dundas
MAX_WALL_DIST = 14.0      # a shop's OSM point must be this close to a wall
CELL_W, CELL_H = 1024, 128
ATLAS, ATLAS_H = 4096, 8192
COLS, ROWS = ATLAS // CELL_W, ATLAS_H // CELL_H   # 4 x 64 = 256 signs
FONT_BOLD = r"C:\Windows\Fonts\bahnschrift.ttf"

SIGN_KINDS = ("amenity=fast_food", "amenity=restaurant", "amenity=cafe", "amenity=bank", "amenity=ice_cream",
              "amenity=bar", "amenity=pub", "amenity=nightclub", "amenity=theatre", "amenity=cinema",
              "amenity=pharmacy", "leisure=amusement_arcade")

# neon colour per kind (R, G, B)
NEON = {
    "food": (255, 120, 40), "cafe": (120, 230, 255), "bar": (255, 60, 170), "shop": (90, 200, 255),
    "pharmacy": (255, 70, 70), "bank": (120, 255, 170), "fun": (200, 90, 255), "theatre": (255, 210, 90),
}
SKIP_NAME_PARTS = (" homeless shelter",)


def kind_of(cat):
    if cat in ("amenity=fast_food", "amenity=restaurant", "amenity=ice_cream"): return "food"
    if cat == "amenity=cafe": return "cafe"
    if cat in ("amenity=bar", "amenity=pub", "amenity=nightclub"): return "bar"
    if cat == "amenity=pharmacy": return "pharmacy"
    if cat == "amenity=bank": return "bank"
    if cat in ("leisure=amusement_arcade", "amenity=cinema"): return "fun"
    if cat == "amenity=theatre": return "theatre"
    return "shop"


def pts(flat):
    return [(flat[i], flat[i + 1]) for i in range(0, len(flat), 2)]


def ccw(ring):
    a = sum(ring[i][0] * ring[(i + 1) % len(ring)][1] - ring[(i + 1) % len(ring)][0] * ring[i][1] for i in range(len(ring)))
    return ring if a > 0 else list(reversed(ring))


def edges(ring):
    for i in range(len(ring)):
        a, b = ring[i], ring[(i + 1) % len(ring)]
        L = math.dist(a, b)
        if L > 0.5:
            yield a, b, L, ((b[1] - a[1]) / L, -(b[0] - a[0]) / L)   # outward normal of a CCW ring


def closest_on(a, b, p):
    ab = (b[0] - a[0], b[1] - a[1])
    t = max(0.0, min(1.0, ((p[0] - a[0]) * ab[0] + (p[1] - a[1]) * ab[1]) / (ab[0] ** 2 + ab[1] ** 2)))
    return t, (a[0] + ab[0] * t, a[1] + ab[1] * t)


def clean(name):
    for s in SKIP_NAME_PARTS:
        name = name.replace(s, "")
    return name.strip()


def main():
    city = json.loads(CITY.read_text(encoding="utf-8"))
    places = json.loads(PLACES.read_text(encoding="utf-8"))["places"]
    blds = [(b, ccw(pts(b["pts"]))) for b in city["buildings"] if b["minH"] < 0.5 and len(b["pts"]) >= 6]
    road_pts = [q for r in city["roads"] for q in pts(r["pts"])]

    fonts = {}
    signs, used = [], []          # used: (x, z) of placed signs, to keep them apart

    def font_at(size):
        if size not in fonts:
            fonts[size] = ImageFont.truetype(FONT_BOLD, size)
        return fonts[size]

    def text_px(text, size=84):
        return font_at(size).getbbox(text)[2] + 60

    def fit_size(text):
        """84 px glyphs, shrunk so long names fit the 1024 px cell instead of being clipped"""
        w = text_px(text, 84)
        return 84 if w <= CELL_W else max(36, int(84 * (CELL_W - 60) / (w - 60)))

    def add(text, kind, x, z, nx, nz, y, height, style):
        if len(signs) >= COLS * ROWS:
            return False
        if any(math.dist((x, z), u) < 2.2 for u in used):
            return False
        size = fit_size(text)
        tw = min(CELL_W, text_px(text, size))
        signs.append({"text": text, "kind": kind, "style": style, "x": round(x, 2), "z": round(z, 2),
                      "nx": round(nx, 4), "nz": round(nz, 4), "y": y, "h": height,
                      "w": round(height * tw / CELL_H, 2), "cellW": tw, "size": size})
        used.append((x, z))
        return True

    # ---- named buildings: plate on the side facing the nearest road; towers also get a crown sign.
    # A building split into several OSM parts carries its name on each part: one sign per name.
    named = set()
    for b, ring in blds:
        name = clean(b["name"] or "")
        if not name or name.isdigit() or name in named:
            continue
        cx = sum(q[0] for q in ring) / len(ring); cz = sum(q[1] for q in ring) / len(ring)
        if math.hypot(cx, cz) > RADIUS + 40:
            continue
        def street_score(e):
            a, c, L, n = e
            mid = ((a[0] + c[0]) / 2 + n[0] * 3, (a[1] + c[1]) / 2 + n[1] * 3)
            return min(math.dist(mid, r) for r in road_pts) - L * 0.05
        a, c, L, n = min(edges(ring), key=street_score)
        mx, mz = (a[0] + c[0]) / 2, (a[1] + c[1]) / 2
        text = name.upper()
        named.add(name)
        if b["h"] > 40 and add(text, "crown", mx, mz, n[0], n[1], round(b["h"] - 3.0, 2), 2.0 if L > 20 else 1.4, "crown"):
            used.pop()                                  # the crown is high up; don't block the street plate
        add(text, "plate", mx + n[1] * 0.01, mz, n[0], n[1], min(5.2, b["h"] - 1.0), 0.5, "plate")

    # ---- store signs, nearest first
    shops = [p for p in places if (p["category"] in SIGN_KINDS or p["category"].startswith("shop="))
             and math.hypot(p["x"], p["z"]) < RADIUS]
    shops.sort(key=lambda p: math.hypot(p["x"], p["z"]))
    for p in shops:
        best = None
        for b, ring in blds:
            for a, c, L, n in edges(ring):
                t, q = closest_on(a, c, (p["x"], p["z"]))
                d = math.dist(q, (p["x"], p["z"]))
                if d < MAX_WALL_DIST and (best is None or d < best[0]):
                    best = (d, a, c, L, n, t, b)
        if best is None:
            continue
        _, a, c, L, n, t, b = best
        text = clean(p["name"]).upper()
        h = 0.85                                       # store sign height (m); 0.62 read too small in street shots
        w = h * min(CELL_W, text_px(text, fit_size(text))) / CELL_H
        if L < w + 0.4:
            continue                                   # wall too short for this sign
        half = (w * 0.5 + 0.2) / L
        t = max(half, min(1 - half, t))                # keep the sign on the wall
        x, z = a[0] + (c[0] - a[0]) * t, a[1] + (c[1] - a[1]) * t
        add(text, kind_of(p["category"]), x, z, n[0], n[1], min(3.6, b["h"] - 0.6), h, "neon")

    # ---- atlas
    albedo = Image.new("RGB", (ATLAS, ATLAS_H), (12, 12, 14))
    emissive = Image.new("RGB", (ATLAS, ATLAS_H), (0, 0, 0))
    for i, s in enumerate(signs):
        col, row = i % COLS, i // COLS
        ox, oy = col * CELL_W, row * CELL_H
        tw = s["cellW"]
        style = s["style"]
        neon = NEON.get(s["kind"], (255, 255, 255))
        plate = (14, 14, 18) if style != "plate" else (38, 40, 44)
        text_col = neon if style == "neon" else (238, 238, 232)
        a = Image.new("RGB", (tw, CELL_H), plate)
        e = Image.new("RGB", (tw, CELL_H), (0, 0, 0))
        glyphs = Image.new("L", (tw, CELL_H), 0)
        ImageDraw.Draw(glyphs).text((tw // 2, CELL_H // 2), s["text"], font=font_at(s["size"]), fill=255, anchor="mm")
        glow = glyphs.filter(ImageFilter.GaussianBlur(10))
        if style == "neon":
            a.paste(Image.new("RGB", a.size, tuple(int(v * 0.35) for v in neon)), (0, 0), glow)   # coloured halo on the plate
            a.paste(Image.new("RGB", a.size, text_col), (0, 0), glyphs)
            e.paste(Image.new("RGB", e.size, tuple(int(v * 0.5) for v in neon)), (0, 0), glow)
            e.paste(Image.new("RGB", e.size, text_col), (0, 0), glyphs)
        else:   # plates and crowns: backlit white letters
            a.paste(Image.new("RGB", a.size, text_col), (0, 0), glyphs)
            e.paste(Image.new("RGB", e.size, (200, 200, 190) if style == "crown" else (90, 90, 85)), (0, 0), glyphs)
        albedo.paste(a, (ox, oy))
        emissive.paste(e, (ox, oy))
        # UV rect in Unity convention (v up from the bottom)
        s["uv"] = [round(ox / ATLAS, 6), round(1 - (oy + CELL_H) / ATLAS_H, 6), round((ox + tw) / ATLAS, 6), round(1 - oy / ATLAS_H, 6)]
        s["color"] = [round(v / 255, 3) for v in neon] if style == "neon" else [1, 0.95, 0.85]
        del s["cellW"], s["size"]

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    albedo.save(OUT_DIR / "signs_albedo.png", optimize=True)
    emissive.save(OUT_DIR / "signs_emissive.png", optimize=True)
    OUT_JSON.write_text(json.dumps({"source": "OpenStreetMap contributors, ODbL", "signs": signs}, indent=1), encoding="utf-8")
    kinds = {}
    for s in signs:
        kinds[s["style"]] = kinds.get(s["style"], 0) + 1
    print(f"signs {len(signs)} ({kinds}) from {len(shops)} nearby shops; atlas {COLS}x{ROWS}")


if __name__ == "__main__":
    main()
