"""Facade detail for the night city (docs/DESIGN.md 3J). Our own generated textures, CC0.

1. Lit-window emission maps for the ambientCG office facades: each glass pane is found as its own region
   (mullions separate them), and ~22% are lit (warm lamps, cool office light, the odd blue TV glow).
   -> Assets/City/Textures/<Facade>/<Facade>_2K-JPG_Emission.jpg
2. Brick facades WITH windows: the brick textures were blank walls. Real-size openings (1.2 x 1.6 m,
   stone lintel and sill, dark frame) on a 3.5 m floor grid, 8 windows x 4 floors per tile.
   -> Assets/City/Textures/brick_windows_red|buff/  (Color, NormalGL, MaskMap, Emission)
3. Ground-floor storefronts: lit display glass, mullions, a door, dark sign band, stone bulkhead.
   -> Assets/City/Textures/storefront/
Tile sizes in metres are written to facade_tiles.json for the generator.
"""
import json
import pathlib
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy import ndimage

ROOT = pathlib.Path(__file__).resolve().parents[2]
TEX = ROOT / "Assets" / "City" / "Textures"
rng = np.random.default_rng(1834)   # Toronto incorporated

LIT_FRACTION = 0.22
WARM, COOL, TV = np.array([255, 196, 128]), np.array([215, 228, 255]), np.array([110, 140, 255])

# real-world tile size (m) for each texture: (width, height)
TILES = {
    "Facade001": (24.0, 34.0),        # 16 x 10 panes: 1.5 m wide, 3.4 m floors
    "Facade005": (48.0, 54.0),        # 32 x 32 panes, two pane rows per floor
    "Facade006": (25.0, 29.0),        # 14 x 8 ribbon windows on 3.6 m floors
    "Facade009": (24.0, 24.0),
    "brick_windows_red": (24.0, 14.0),
    "brick_windows_buff": (24.0, 14.0),
    "storefront": (12.0, 4.5),
}


def lit_colour():
    r = rng.random()
    base = WARM if r < 0.6 else COOL if r < 0.9 else TV
    return base * rng.uniform(0.55, 1.0)


def emission_from_panes(name, glass_mask):
    """glass_mask: bool array of glass pixels; mullions/spandrels False"""
    mask = ndimage.binary_erosion(glass_mask, iterations=2)
    labels, n = ndimage.label(mask)
    h, w = mask.shape
    out = np.zeros((h, w, 3), np.float32)
    objs = ndimage.find_objects(labels)
    for i, sl in enumerate(objs, 1):
        if sl is None or rng.random() > LIT_FRACTION:
            continue
        region = labels[sl] == i
        ys = np.linspace(1.0, 0.75, region.shape[0])[:, None]        # a little darker at the sill
        col = lit_colour()
        blinds = rng.random() < 0.3                                  # some windows have blinds down
        shade = ys * (0.6 + 0.4 * (np.arange(region.shape[0])[:, None] % 6 < 4)) if blinds else ys
        shade = np.broadcast_to(shade, region.shape)
        out[sl][region] = (col[None, None, :] * shade[..., None])[region]
    img = Image.fromarray(np.clip(out, 0, 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.2))
    path = TEX / name / f"{name}_2K-JPG_Emission.jpg"
    img.save(path, quality=92)
    print(f"{name}: {n} panes, {int(n * LIT_FRACTION)} lit (approx) -> {path.name}")


def grid_mask(size, cols, rows, inset=0.12):
    """pane rectangles of a regular curtain-wall grid (the photos' reflections fool brightness detection)"""
    m = np.zeros((size, size), bool)
    cw, ch = size / cols, size / rows
    for r in range(rows):
        for c in range(cols):
            x0, x1 = int(c * cw + cw * inset), int((c + 1) * cw - cw * inset)
            y0, y1 = int(r * ch + ch * inset), int((r + 1) * ch - ch * inset)
            m[y0:y1, x0:x1] = True
    return m


def office_facades():
    emission_from_panes("Facade001", grid_mask(2048, 16, 10))      # 16 x 10 panes
    emission_from_panes("Facade005", grid_mask(2048, 32, 32))      # 32 x 32 panes
    lum = np.asarray(Image.open(TEX / "Facade006" / "Facade006_2K-JPG_Color.jpg").convert("L"), np.float32)
    emission_from_panes("Facade006", lum < 175)                      # glass is darker than the white bands


def brick_with_windows(src, out_name):
    px_per_m = 170
    tw, th = TILES[out_name]
    W, H = int(tw * px_per_m), int(th * px_per_m)
    brick = Image.open(TEX / src / f"{src}_Color.jpg").convert("RGB")
    bn = Image.open(TEX / src / f"{src}_NormalGL.jpg").convert("RGB")
    brick_m = 3.0 if src == "brick_wall_001" else 1.0                 # Poly Haven tile size
    bs = int(brick_m * px_per_m)
    brick, bn = brick.resize((bs, bs)), bn.resize((bs, bs))
    col = Image.new("RGB", (W, H)); nrm = Image.new("RGB", (W, H))
    for y in range(0, H, bs):
        for x in range(0, W, bs):
            col.paste(brick, (x, y)); nrm.paste(bn, (x, y))
    smooth = Image.new("L", (W, H), 60)
    emis = np.zeros((H, W, 3), np.float32)
    dc, dn, ds = ImageDraw.Draw(col), ImageDraw.Draw(nrm), ImageDraw.Draw(smooth)
    floors, per_floor = int(th / 3.5), 8
    bay = tw / per_floor
    for f in range(floors):
        for k in range(per_floor):
            x0 = (k * bay + (bay - 1.2) / 2) * px_per_m
            y_top = H - (f * 3.5 + 0.9 + 1.6) * px_per_m                # image y grows downward
            x1, y1 = x0 + 1.2 * px_per_m, y_top + 1.6 * px_per_m
            dc.rectangle([x0 - 14, y_top - 22, x1 + 14, y_top - 2], fill=(150, 146, 138))   # stone lintel
            dc.rectangle([x0 - 10, y1 + 2, x1 + 10, y1 + 16], fill=(140, 136, 128))         # sill
            dc.rectangle([x0, y_top, x1, y1], fill=(34, 36, 40))                            # frame
            dc.rectangle([x0 + 10, y_top + 10, x1 - 10, y1 - 10], fill=(26, 32, 40))        # glass
            dc.line([((x0 + x1) / 2, y_top + 10), ((x0 + x1) / 2, y1 - 10)], fill=(34, 36, 40), width=8)
            dn.rectangle([x0, y_top, x1, y1], fill=(128, 128, 255))                         # flat glass
            ds.rectangle([x0 + 10, y_top + 10, x1 - 10, y1 - 10], fill=235)
            if rng.random() < LIT_FRACTION + 0.08:                                         # homes stay up later
                c = lit_colour()
                emis[int(y_top + 10):int(y1 - 10), int(x0 + 10):int(x1 - 10)] = c
    out = TEX / out_name
    out.mkdir(parents=True, exist_ok=True)
    col.save(out / f"{out_name}_Color.jpg", quality=92)
    nrm.save(out / f"{out_name}_NormalGL.jpg", quality=92)
    z = Image.new("L", (W, H), 0); ao = Image.new("L", (W, H), 255)
    Image.merge("RGBA", (z, ao, z, smooth)).save(out / f"{out_name}_MaskMap.png", optimize=True)
    Image.fromarray(emis.astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.5)).save(out / f"{out_name}_Emission.jpg", quality=92)
    print(f"{out_name}: {floors} floors x {per_floor} windows, {W}x{H}")


def storefront():
    px_per_m = 170
    tw, th = TILES["storefront"]
    W, H = int(tw * px_per_m), int(th * px_per_m)
    col = Image.new("RGB", (W, H), (30, 30, 34)); dc = ImageDraw.Draw(col)
    smooth = Image.new("L", (W, H), 90); ds = ImageDraw.Draw(smooth)
    emis = Image.new("RGB", (W, H), (0, 0, 0)); de = ImageDraw.Draw(emis)
    band_top = H - int(3.7 * px_per_m)                       # sign band above 3.7 m
    dc.rectangle([0, 0, W, band_top], fill=(18, 18, 20))
    dc.rectangle([0, H - int(0.4 * px_per_m), W, H], fill=(120, 118, 112))     # stone bulkhead
    bays = 4
    for k in range(bays):
        bx0, bx1 = int(k * W / bays) + 12, int((k + 1) * W / bays) - 12
        gy0, gy1 = band_top + 14, H - int(0.4 * px_per_m) - 6
        door = k == 1
        dc.rectangle([bx0, gy0, bx1, gy1], fill=(22, 26, 30))
        ds.rectangle([bx0, gy0, bx1, gy1], fill=240)
        # interior glow: warm, brighter at the top (ceiling lights), shelves as darker bands
        tint = lit_colour() if rng.random() < 0.85 else np.array([0, 0, 0])
        for y in range(gy0, gy1):
            f = 0.55 + 0.45 * (1 - (y - gy0) / (gy1 - gy0))
            shelf = 0.7 if ((y - gy0) // 40) % 3 == 2 else 1.0
            de.line([(bx0, y), (bx1, y)], fill=tuple(int(v * f * shelf) for v in tint))
        if door:
            dx0 = bx0 + (bx1 - bx0) // 3
            dc.rectangle([dx0, gy0 + 40, dx0 + int(1.0 * px_per_m), gy1], outline=(60, 60, 64), width=10)
        for mx in np.linspace(bx0, bx1, 4)[1:-1]:
            dc.line([(mx, gy0), (mx, gy1)], fill=(40, 40, 44), width=10)
            de.line([(mx, gy0), (mx, gy1)], fill=(0, 0, 0), width=10)
        dc.rectangle([bx0 - 12, gy0, bx0, gy1], fill=(45, 45, 50)); dc.rectangle([bx1, gy0, bx1 + 12, gy1], fill=(45, 45, 50))
    out = TEX / "storefront"
    out.mkdir(parents=True, exist_ok=True)
    col.save(out / "storefront_Color.jpg", quality=92)
    Image.new("RGB", (W, H), (128, 128, 255)).save(out / "storefront_NormalGL.jpg", quality=92)
    z = Image.new("L", (W, H), 0); ao = Image.new("L", (W, H), 255)
    Image.merge("RGBA", (z, ao, z, smooth)).save(out / "storefront_MaskMap.png", optimize=True)
    emis.filter(ImageFilter.GaussianBlur(2)).save(out / "storefront_Emission.jpg", quality=92)
    print(f"storefront: {bays} bays, {W}x{H}")


if __name__ == "__main__":
    office_facades()
    brick_with_windows("brick_wall_001", "brick_windows_red")
    brick_with_windows("brown_brick_02", "brick_windows_buff")
    storefront()
    (ROOT / "Assets" / "City" / "Data" / "facade_tiles.json").write_text(
        json.dumps({"tiles": [{"name": k, "w": v[0], "h": v[1]} for k, v in TILES.items()]}, indent=1), encoding="utf-8")
