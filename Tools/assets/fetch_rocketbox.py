"""Download the approved Microsoft Rocketbox characters and animations (MIT) and convert textures.

Approved by Hamzah 2026-10-22: 10 adults + 8 animations, 831 MB raw (docs/DESIGN.md 3B.2).
Raw files: Tools/assets/cache/rocketbox/ (gitignored).
Project files: Assets/Characters/Rocketbox/<Avatar>/<Avatar>.fbx + textures converted from
uncompressed TGA (~12 MB each) to JPG (colour/specular/normal) or PNG (maps with alpha),
and Assets/Characters/Rocketbox/Animations/*.fbx.
"""
import json
import pathlib
import shutil
import urllib.request

from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parents[2]
CACHE = pathlib.Path(__file__).parent / "cache" / "rocketbox"
OUT = ROOT / "Assets" / "Characters" / "Rocketbox"
REPO = "microsoft/Microsoft-Rocketbox"
RAW = f"https://raw.githubusercontent.com/{REPO}/master/"
UA = {"User-Agent": "LifeInDowntown/0.1 (github.com/Hamzah-Muhammad/LifeInDowntown)"}

AVATARS = ["Female_Adult_04", "Female_Adult_06", "Female_Adult_07", "Female_Adult_09", "Female_Adult_12",
           "Male_Adult_04", "Male_Adult_10", "Male_Adult_12", "Male_Adult_17", "Male_Adult_18"]
# professions live under Assets/Avatars/Professions; approved 2026-10-22: the officer (90 MB raw) for police NPCs
# and his duty pistol, the held / pickup pistol model (docs/DESIGN.md 3M)
PROFESSIONS = ["Police_Male_01"]
ANIMATIONS = ["all_animations_max_motextr_xy/f_walk_neutral_01", "all_animations_max_motextr_xy/m_walk_neutral_01",
              "all_animations_max_motextr_xy/f_walk_slow_01", "all_animations_max_motextr_xy/m_walk_slow_01",
              "all_animations_max_motextr_static/f_idle_neutral_01", "all_animations_max_motextr_static/m_idle_neutral_01",
              "all_animations_max_motextr_static/m_cell_phone_talk_01", "all_animations_max_motextr_static/f_cell_phone_textmessage"]
# street crowd (docs/DESIGN.md 3L), approved 2026-10-22: 14 in-place clips, 60 MB
ANIMATIONS += [f"all_animations_max_motextr_static/{a}" for a in (
    "m_crouch_idle", "m_sit_chair_idle_neutral_01", "m_sit_chair_idle_look_around", "f_sit_chair_idle_neutral_01",
    "m_idle_drunk_01", "m_idle_cough_01", "m_idle_shake_arms_01", "m_idle_nervous_03", "m_idle_look_around_01",
    "m_gestic_talk_cool", "m_gestic_listen_nervous_01", "m_idle_waiting_01", "f_idle_waiting_01", "f_idle_look_around_01")]


def get(url, timeout=120):
    with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=timeout) as r:
        return r.read()


def download(path, size):
    dest = CACHE / path
    if dest.exists() and dest.stat().st_size == size:
        return 0
    dest.parent.mkdir(parents=True, exist_ok=True)
    tmp = dest.with_suffix(dest.suffix + ".part")
    with urllib.request.urlopen(urllib.request.Request(RAW + path, headers=UA), timeout=300) as r, open(tmp, "wb") as f:
        shutil.copyfileobj(r, f, 1 << 20)
    tmp.replace(dest)
    return size


def wanted(tree):
    files = []
    for e in tree:
        if e["type"] != "blob":
            continue
        p = e["path"]
        for av in AVATARS + PROFESSIONS:
            base = f"Assets/Avatars/{Group(av)}/{av}/"
            name = p.rsplit("/", 1)[-1].lower()
            if p == base + f"Export/{av}.fbx" or (p.startswith(base + "Textures/") and name.endswith(".tga") and "wrinkle" not in name):
                files.append((p, e["size"]))
        for a in ANIMATIONS:
            if p == f"Assets/Animations/{a}.max.fbx":
                files.append((p, e["size"]))
    return files


def Group(av):
    return "Professions" if av in PROFESSIONS else "Adults"


def convert_texture(src, dst_dir):
    img = Image.open(src)
    has_alpha = img.mode in ("RGBA", "LA") and img.getchannel("A").getextrema() != (255, 255)
    if has_alpha:
        out = dst_dir / (src.stem + ".png")
        img.save(out, optimize=True)
    else:
        out = dst_dir / (src.stem + ".jpg")
        img.convert("RGB").save(out, quality=92, subsampling=0)
    return out


def main():
    sha = json.loads(get(f"https://api.github.com/repos/{REPO}/branches/master"))["commit"]["sha"]
    tree = json.loads(get(f"https://api.github.com/repos/{REPO}/git/trees/{sha}?recursive=1"))["tree"]
    files = wanted(tree)
    total = sum(s for _, s in files)
    print(f"{len(files)} files, {total / 2**20:.1f} MB (repo commit {sha[:8]})")
    got = 0
    for i, (path, size) in enumerate(files, 1):
        got += download(path, size)
        if i % 10 == 0 or i == len(files):
            print(f"  {i}/{len(files)} files, {got / 2**20:.0f} MB downloaded this run")

    raw_mb = conv_mb = 0.0
    for av in AVATARS + PROFESSIONS:
        dst = OUT / av
        dst.mkdir(parents=True, exist_ok=True)
        shutil.copy2(CACHE / f"Assets/Avatars/{Group(av)}/{av}/Export/{av}.fbx", dst / f"{av}.fbx")
        for tga in sorted((CACHE / f"Assets/Avatars/{Group(av)}/{av}/Textures").glob("*.tga")):
            out = convert_texture(tga, dst)
            raw_mb += tga.stat().st_size / 2**20
            conv_mb += out.stat().st_size / 2**20
    anim_dir = OUT / "Animations"
    anim_dir.mkdir(parents=True, exist_ok=True)
    for a in ANIMATIONS:
        name = a.rsplit("/", 1)[-1]
        shutil.copy2(CACHE / f"Assets/Animations/{a}.max.fbx", anim_dir / f"{name}.fbx")
    lic = get(RAW + "LICENSE.md")
    (OUT / "LICENSE.md").write_bytes(lic)
    print(f"textures: {raw_mb:.0f} MB TGA -> {conv_mb:.0f} MB in the project")


if __name__ == "__main__":
    main()
