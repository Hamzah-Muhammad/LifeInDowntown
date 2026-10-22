"""Download the approved condo set from Poly Haven (CC0), docs/DESIGN.md 3H. Approved 2026-10-22, 93 MB.

Models (FBX, 2K textures) -> Assets/Condo/Models/<id>/  (+ <slot>_mask_2k.png packed for HDRP)
Textures                 -> Assets/City/Textures/<id>/ (same naming as fetch_cc0.py; run pack_masks.py after)
"""
import json
import pathlib
import urllib.request

from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parents[2]
MODELS_DIR = ROOT / "Assets" / "Condo" / "Models"
TEX_DIR = ROOT / "Assets" / "City" / "Textures"
UA = {"User-Agent": "LifeInDowntown/0.1 (github.com/Hamzah-Muhammad/LifeInDowntown)"}

MODELS = ["sofa_03", "modern_arm_chair_01", "mid_century_lounge_chair", "modern_coffee_table_01", "side_table_01",
          "modern_wooden_cabinet", "bar_chair_round_01", "modern_ceiling_lamp_01", "potted_plant_01", "potted_plant_04",
          "hanging_picture_frame_01", "painted_wooden_nightstand"]
TEXTURES = ["laminate_floor_02", "marble_01", "floor_tiles_06", "black_oak_veneer", "fabric_pattern_07"]
TEX_MAPS = {"Diffuse": "Color", "nor_gl": "NormalGL", "Rough": "Roughness", "AO": "AO"}


def get_json(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=60) as r:
        return json.load(r)


def download(url, dest):
    if dest.exists():
        return 0
    dest.parent.mkdir(parents=True, exist_ok=True)
    tmp = dest.with_suffix(dest.suffix + ".part")
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=300) as r, open(tmp, "wb") as f:
        while chunk := r.read(1 << 20):
            f.write(chunk)
    tmp.replace(dest)
    return dest.stat().st_size


def pack_arm(folder):
    """Poly Haven ARM (R=AO, G=roughness, B=metal) -> HDRP mask (R=metal, G=AO, B=0, A=smoothness)."""
    for arm in folder.rglob("*_arm_2k.jpg"):
        r, g, b = Image.open(arm).convert("RGB").split()
        smooth = g.point(lambda v: 255 - v)
        zero = Image.new("L", r.size, 0)
        Image.merge("RGBA", (b, r, zero, smooth)).save(arm.with_name(arm.name.replace("_arm_2k.jpg", "_mask_2k.png")))


def main():
    total = 0
    for mid in MODELS:
        fbx = get_json(f"https://api.polyhaven.com/files/{mid}")["fbx"]["2k"]["fbx"]
        out = MODELS_DIR / mid
        total += download(fbx["url"], out / f"{mid}.fbx")
        for rel, info in fbx.get("include", {}).items():
            name = pathlib.PurePosixPath(rel).name          # flatten, ignore folders in the listing
            total += download(info["url"], out / "textures" / name)
        pack_arm(out)
        print(f"model {mid} ok")
    for tid in TEXTURES:
        files = get_json(f"https://api.polyhaven.com/files/{tid}")
        for key, suffix in TEX_MAPS.items():
            entry = files.get(key, {}).get("2k", {}).get("jpg")
            if entry is None and key == "Diffuse":                 # fabric sets name colour variants col_1, col_2...
                entry = files.get("col_1", {}).get("2k", {}).get("jpg")
            if entry:
                total += download(entry["url"], TEX_DIR / tid / f"{tid}_{suffix}.jpg")
        print(f"texture {tid} ok")
    print(f"downloaded {total / 2**20:.1f} MB this run")


if __name__ == "__main__":
    main()
