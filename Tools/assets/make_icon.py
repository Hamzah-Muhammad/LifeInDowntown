"""Make the game icon: a square crop of the rainy Dundas screenshot with the title.

Input:  Screenshots/street_dundas_looking_west.png (from ShotTool)
Output: Assets/Branding/GameIcon.png (1024x1024)
"""
import pathlib
from PIL import Image, ImageDraw, ImageEnhance, ImageFont

ROOT = pathlib.Path(__file__).resolve().parents[2]
SRC = ROOT / "Screenshots" / "street_dundas_looking_west.png"
OUT = ROOT / "Assets" / "Branding" / "GameIcon.png"
FONTS = [r"C:\Windows\Fonts\bahnschrift.ttf", r"C:\Windows\Fonts\arialbd.ttf"]


def font(size):
    for f in FONTS:
        if pathlib.Path(f).exists():
            return ImageFont.truetype(f, size)
    return ImageFont.load_default()


def main():
    img = Image.open(SRC).convert("RGB")
    w, h = img.size
    side = h
    left = (w - side) // 2 - 80          # keep the street and lamps centred
    img = img.crop((left, 0, left + side, side)).resize((1024, 1024), Image.LANCZOS)
    img = ImageEnhance.Contrast(img).enhance(1.15)

    # dark band at the bottom for the title
    band = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
    d = ImageDraw.Draw(band)
    for y in range(600, 1024):
        d.line([(0, y), (1024, y)], fill=(0, 0, 0, int(230 * (y - 600) / 424)))
    img = Image.alpha_composite(img.convert("RGBA"), band)

    d = ImageDraw.Draw(img)
    d.text((512, 820), "LIFE IN", font=font(120), fill=(235, 235, 230), anchor="mm")
    d.text((512, 935), "DOWNTOWN", font=font(150), fill=(255, 190, 110), anchor="mm")
    d.ellipse((70, 70, 120, 120), fill=(220, 25, 25))           # REC dot
    d.text((140, 95), "REC", font=font(56), fill=(235, 235, 230), anchor="lm")

    OUT.parent.mkdir(parents=True, exist_ok=True)
    img.convert("RGB").save(OUT)
    print(f"wrote {OUT}")


if __name__ == "__main__":
    main()
