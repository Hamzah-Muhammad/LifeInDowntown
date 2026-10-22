"""Download the approved CC0 asset list (docs/DESIGN.md 3A.1) into the Unity project.

Poly Haven maps go straight to Assets/City/Textures/<id>/; ambientCG zips are cached in
Tools/assets/cache/ (gitignored) and only their .jpg maps are extracted. HDRIs go to
Assets/City/Sky/. Re-running skips files that already exist.
"""
import json
import pathlib
import urllib.request
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[2]
TEX_DIR = ROOT / "Assets" / "City" / "Textures"
SKY_DIR = ROOT / "Assets" / "City" / "Sky"
CACHE = pathlib.Path(__file__).parent / "cache"
UA = {"User-Agent": "LifeInDowntown/0.1 (github.com/Hamzah-Muhammad/LifeInDowntown)"}

POLYHAVEN_TEXTURES = ["asphalt_02", "brushed_concrete_03", "brick_wall_001", "brown_brick_02"]
POLYHAVEN_MAPS = {"Diffuse": "Color", "nor_gl": "NormalGL", "Rough": "Roughness", "AO": "AO", "Displacement": "Displacement"}
POLYHAVEN_HDRIS = ["kloppenheim_07_puresky", "kloppenheim_02_puresky"]
AMBIENTCG = ["PavingStones138", "Facade001", "Facade005", "Facade006", "Facade009"]


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


def main():
    total = 0
    for tid in POLYHAVEN_TEXTURES:
        files = get_json(f"https://api.polyhaven.com/files/{tid}")
        for key, suffix in POLYHAVEN_MAPS.items():
            entry = files.get(key, {}).get("2k", {}).get("jpg")
            if entry:
                total += download(entry["url"], TEX_DIR / tid / f"{tid}_{suffix}.jpg")
        print(f"polyhaven texture {tid} ok")

    for hid in POLYHAVEN_HDRIS:
        files = get_json(f"https://api.polyhaven.com/files/{hid}")
        total += download(files["hdri"]["4k"]["hdr"]["url"], SKY_DIR / f"{hid}_4k.hdr")
        print(f"polyhaven hdri {hid} ok")

    for aid in AMBIENTCG:
        zpath = CACHE / f"{aid}_2K-JPG.zip"
        total += download(f"https://ambientcg.com/get?file={aid}_2K-JPG.zip", zpath)
        out = TEX_DIR / aid
        out.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(zpath) as z:
            for name in z.namelist():
                base = pathlib.PurePosixPath(name).name   # flatten; ignore any folder paths in the zip
                if not base.lower().endswith(".jpg"):
                    continue
                target = out / base
                if not target.exists():
                    target.write_bytes(z.read(name))
        print(f"ambientcg {aid} ok: {sorted(p.name for p in out.glob('*.jpg'))}")

    print(f"downloaded {total / (1 << 20):.1f} MB this run")


if __name__ == "__main__":
    main()
