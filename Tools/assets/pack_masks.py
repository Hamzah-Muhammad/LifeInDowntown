"""Pack HDRP Lit mask maps for every texture set in Assets/City/Textures/.

HDRP MaskMap channels: R = metallic, G = ambient occlusion, B = detail mask, A = smoothness.
Writes <set>/<set>_MaskMap.png next to the source maps. Missing inputs fall back to
metallic 0, AO 1, smoothness from roughness (1 - roughness).
"""
import pathlib
from PIL import Image, ImageChops

TEX_DIR = pathlib.Path(__file__).resolve().parents[2] / "Assets" / "City" / "Textures"


def find(folder, *suffixes):
    for s in suffixes:
        hits = [p for p in folder.glob("*.jpg") if p.stem.lower().endswith(s.lower())]
        if hits:
            return hits[0]
    return None


def main():
    for folder in sorted(p for p in TEX_DIR.iterdir() if p.is_dir()):
        rough_p = find(folder, "_Roughness")
        if rough_p is None:
            print(f"skip {folder.name}: no roughness map")
            continue
        rough = Image.open(rough_p).convert("L")
        size = rough.size
        metal_p, ao_p = find(folder, "_Metalness"), find(folder, "_AmbientOcclusion", "_AO")
        metal = Image.open(metal_p).convert("L").resize(size) if metal_p else Image.new("L", size, 0)
        ao = Image.open(ao_p).convert("L").resize(size) if ao_p else Image.new("L", size, 255)
        smooth = ImageChops.invert(rough)
        detail = Image.new("L", size, 0)
        out = folder / f"{folder.name}_MaskMap.png"
        Image.merge("RGBA", (metal, ao, detail, smooth)).save(out)
        print(f"{folder.name}: mask map {size} (metal={'yes' if metal_p else 'none'}, ao={'yes' if ao_p else 'none'})")


if __name__ == "__main__":
    main()
