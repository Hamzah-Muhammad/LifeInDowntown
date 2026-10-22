"""Backlit storefront sign for the Rabba stores (docs/DESIGN.md 3K): white box sign, red RABBA, dark FINE FOODS.
Writes Assets/City/Signs/rabba_sign.png (albedo) and rabba_sign_emissive.png (the whole box glows, letters strongest).
Rabba is a real Toronto chain: fine for this personal project, a commercial release needs a parody name.
"""
import pathlib
from PIL import Image, ImageDraw, ImageFont

OUT = pathlib.Path(__file__).resolve().parents[2] / "Assets" / "City" / "Signs"
FONT = r"C:\Windows\Fonts\arialbd.ttf"
W, H = 2048, 272          # 6 m x 0.8 m

img = Image.new("RGB", (W, H), (244, 242, 236))
d = ImageDraw.Draw(img)
d.rectangle([0, 0, W - 1, H - 1], outline=(190, 30, 30), width=14)
d.text((W * 0.40, H * 0.52), "RABBA", font=ImageFont.truetype(FONT, 190), fill=(205, 25, 30), anchor="mm")
d.text((W * 0.78, H * 0.52), "FINE FOODS", font=ImageFont.truetype(FONT, 78), fill=(40, 40, 44), anchor="mm")
OUT.mkdir(parents=True, exist_ok=True)
img.save(OUT / "rabba_sign.png", optimize=True)
glow = img.point(lambda v: int(v * 0.75))
glow.save(OUT / "rabba_sign_emissive.png", optimize=True)
print("rabba sign written")
