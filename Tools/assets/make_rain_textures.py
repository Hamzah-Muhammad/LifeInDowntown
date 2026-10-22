"""Generate the procedural textures for look pass 5c (rain).

Assets/City/Rain/RainStreak.png     soft vertical streak, white with alpha falloff (particles)
Assets/City/Rain/Puddle_Color.png   dark water colour, alpha = puddle shape (decal opacity)
Assets/City/Rain/Puddle_Mask.png    HDRP decal mask map: R metal 0, G AO 1, B opacity, A smoothness 1
"""
import math
import pathlib
import random
from PIL import Image, ImageFilter

OUT = pathlib.Path(__file__).resolve().parents[2] / "Assets" / "City" / "Rain"


def streak():
    w, h = 16, 256
    img = Image.new("RGBA", (w, h))
    px = img.load()
    for y in range(h):
        along = math.sin(math.pi * y / (h - 1)) ** 0.7          # fades at both ends
        for x in range(w):
            across = max(0.0, 1.0 - abs(x - (w - 1) / 2) / (w / 2)) ** 2
            px[x, y] = (255, 255, 255, int(255 * along * across))
    img.save(OUT / "RainStreak.png")


def puddle(size=512, seed=7):
    """Irregular blob: a few overlapping soft discs, thresholded with a soft edge."""
    rnd = random.Random(seed)
    field = Image.new("L", (size, size), 0)
    px = field.load()
    blobs = [(rnd.uniform(0.3, 0.7) * size, rnd.uniform(0.3, 0.7) * size, rnd.uniform(0.12, 0.28) * size)
             for _ in range(6)]
    for y in range(size):
        for x in range(size):
            v = sum(max(0.0, 1.0 - math.hypot(x - bx, y - by) / r) for bx, by, r in blobs)
            px[x, y] = min(255, int(255 * v))
    field = field.filter(ImageFilter.GaussianBlur(size / 64))
    alpha = field.point(lambda v: 0 if v < 70 else min(255, (v - 70) * 4))
    alpha = alpha.filter(ImageFilter.GaussianBlur(size / 128))
    color = Image.new("RGBA", (size, size), (14, 15, 18, 0))
    color.putalpha(alpha)
    color.save(OUT / "Puddle_Color.png")
    metal = Image.new("L", (size, size), 0)
    ao = Image.new("L", (size, size), 255)
    smooth = Image.new("L", (size, size), 255)
    Image.merge("RGBA", (metal, ao, alpha, smooth)).save(OUT / "Puddle_Mask.png")


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    streak()
    puddle()
    print(f"wrote {sorted(p.name for p in OUT.glob('*.png'))}")
