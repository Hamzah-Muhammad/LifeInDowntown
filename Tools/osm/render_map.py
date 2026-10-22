"""Draw the GTA-style map image from the city JSON (docs/DESIGN.md 3F).

Output (loaded at runtime from Resources):
  Assets/Resources/City/map.png        top-down map, north up, PX_PER_M pixels per metre
  Assets/Resources/City/map_meta.json  world bounds of the image, so the game can place the player on it
  Assets/Resources/UI/map_arrow.png    player arrow
  Assets/Resources/UI/map_blip.png     round blip (tinted at runtime)
"""
import json
import math
import pathlib
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = pathlib.Path(__file__).resolve().parents[2]
CITY = ROOT / "Assets" / "City" / "Data" / "downtown_yonge_dundas.json"
PLACES = ROOT / "Assets" / "Resources" / "City" / "places.json"
OUT_MAP = ROOT / "Assets" / "Resources" / "City" / "map.png"
OUT_META = ROOT / "Assets" / "Resources" / "City" / "map_meta.json"
OUT_UI = ROOT / "Assets" / "Resources" / "UI"

PX_PER_M = 4.0
PAD_M = 60.0          # same padding as the ground plane in CityGenerator
FONT = r"C:\Windows\Fonts\bahnschrift.ttf"

BG = (24, 30, 38)
BLOCK = (36, 44, 54)          # pavement / ground
PLAZA = (52, 62, 70)
PARK = (40, 72, 52)
ROAD_EDGE = (20, 22, 26)
ROAD = (128, 134, 140)
ROAD_MAJOR = (176, 168, 150)
PATH = (70, 80, 88)
BUILDING = (58, 68, 82)
BUILDING_EDGE = (86, 98, 114)
TOWER = (72, 84, 102)
LABEL = (225, 228, 232)


def pts(flat):
    return [(flat[i], flat[i + 1]) for i in range(0, len(flat), 2)]


def main():
    d = json.loads(CITY.read_text(encoding="utf-8"))
    b = d["bounds"]
    xmin, xmax = b["xmin"] - PAD_M, b["xmax"] + PAD_M
    zmin, zmax = b["zmin"] - PAD_M, b["zmax"] + PAD_M
    w, h = int((xmax - xmin) * PX_PER_M), int((zmax - zmin) * PX_PER_M)

    def px(p):  # world (x, z) -> image (u, v), north up
        return ((p[0] - xmin) * PX_PER_M, (zmax - p[1]) * PX_PER_M)

    img = Image.new("RGB", (w, h), BG)
    dr = ImageDraw.Draw(img)
    dr.rectangle([px((b["xmin"] - PAD_M, b["zmax"] + PAD_M)), px((b["xmax"] + PAD_M, b["zmin"] - PAD_M))], fill=BLOCK)

    for pz in d["plazas"]:
        if pz.get("kind") == "pedestrian":
            dr.polygon([px(p) for p in pts(pz["pts"])], fill=PLAZA)
    # parks from named places with leisure=park (centroids only, drawn as soft discs)
    places = json.loads(PLACES.read_text(encoding="utf-8"))["places"]
    for pl in places:
        if pl["category"] in ("leisure=park", "place=square"):
            u, v = px((pl["x"], pl["z"]))
            r = 18 * PX_PER_M
            dr.ellipse([u - r, v - r, u + r, v + r], fill=PARK)

    for p in d["paths"]:
        line = [px(q) for q in pts(p["pts"])]
        if len(line) >= 2:
            dr.line(line, fill=PATH, width=max(2, int(p["width"] * PX_PER_M * 0.8)), joint="curve")

    roads = sorted(d["roads"], key=lambda r: r["width"])
    for r in roads:   # dark outline pass, then fill, so crossings read cleanly
        line = [px(q) for q in pts(r["pts"])]
        if len(line) >= 2:
            dr.line(line, fill=ROAD_EDGE, width=int(r["width"] * PX_PER_M) + 6, joint="curve")
    for r in roads:
        line = [px(q) for q in pts(r["pts"])]
        if len(line) >= 2:
            col = ROAD_MAJOR if r["type"] in ("primary", "secondary") else ROAD
            dr.line(line, fill=col, width=int(r["width"] * PX_PER_M), joint="curve")
    for pz in d["plazas"]:
        if pz.get("kind") == "road":
            dr.polygon([px(p) for p in pts(pz["pts"])], fill=ROAD_MAJOR)

    for bd in sorted(d["buildings"], key=lambda x: x["h"]):
        poly = [px(p) for p in pts(bd["pts"])]
        if len(poly) >= 3:
            dr.polygon(poly, fill=TOWER if bd["h"] > 60 else BUILDING, outline=BUILDING_EDGE)

    # street names: once per name, along its longest straight segment
    best = {}
    for r in d["roads"]:
        if not r["name"] or r["type"] not in ("primary", "secondary", "tertiary", "residential", "unclassified"):
            continue
        p = pts(r["pts"])
        for i in range(len(p) - 1):
            seg = math.dist(p[i], p[i + 1])
            if seg > best.get(r["name"], (0,))[0]:
                best[r["name"]] = (seg, p[i], p[i + 1])
    font = ImageFont.truetype(FONT, int(5.5 * PX_PER_M))
    for name, (seg, a, c) in best.items():
        if seg < 25:
            continue
        label = name.upper().replace(" STREET", " ST").replace(" AVENUE", " AVE")
        ua, va = px(a); uc, vc = px(c)
        ang = math.degrees(math.atan2(-(vc - va), uc - ua))
        if ang > 90: ang -= 180
        if ang < -90: ang += 180
        tw, th = font.getbbox(label)[2:]
        tile = Image.new("RGBA", (tw + 16, th + 16), (0, 0, 0, 0))
        td = ImageDraw.Draw(tile)
        td.text((8, 8), label, font=font, fill=LABEL + (255,), stroke_width=3, stroke_fill=(10, 12, 16, 255))
        tile = tile.rotate(ang, expand=True, resample=Image.BICUBIC)
        cu, cv = (ua + uc) / 2, (va + vc) / 2
        img.paste(tile, (int(cu - tile.width / 2), int(cv - tile.height / 2)), tile)

    # building names (one per name, at the footprint centre), smaller and softer than street names
    bfont = ImageFont.truetype(FONT, int(3.2 * PX_PER_M))
    seen = set()
    for bd in d["buildings"]:
        name = (bd.get("name") or "").replace(" homeless shelter", "").strip()
        if not name or name.isdigit() or name in seen or bd["part"]:
            continue
        seen.add(name)
        ring = pts(bd["pts"])
        cx = sum(q[0] for q in ring) / len(ring); cz = sum(q[1] for q in ring) / len(ring)
        u, v = px((cx, cz))
        dr.text((u, v), name, font=bfont, fill=(190, 200, 214), anchor="mm", stroke_width=2, stroke_fill=(14, 16, 20))

    img = img.filter(ImageFilter.SMOOTH_MORE)
    OUT_MAP.parent.mkdir(parents=True, exist_ok=True)
    img.save(OUT_MAP, optimize=True)
    OUT_META.write_text(json.dumps({"xmin": xmin, "xmax": xmax, "zmin": zmin, "zmax": zmax,
                                    "pxPerMetre": PX_PER_M, "width": w, "height": h}, indent=1), encoding="utf-8")

    OUT_UI.mkdir(parents=True, exist_ok=True)
    s = 128   # player arrow: white chevron pointing up, dark outline
    arrow = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    ad = ImageDraw.Draw(arrow)
    shape = [(s / 2, 10), (s - 22, s - 16), (s / 2, s - 40), (22, s - 16)]
    ad.polygon(shape, fill=(255, 255, 255, 255), outline=(0, 0, 0, 255))
    arrow.save(OUT_UI / "map_arrow.png")
    blip = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    bd_ = ImageDraw.Draw(blip)
    bd_.ellipse((6, 6, 58, 58), fill=(255, 255, 255, 255), outline=(0, 0, 0, 255), width=5)
    blip.save(OUT_UI / "map_blip.png")
    print(f"map {w}x{h} px at {PX_PER_M} px/m, {len(best)} street labels -> {OUT_MAP}")


if __name__ == "__main__":
    main()
