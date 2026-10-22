"""Convert the raw OSM cache into the committed city JSON the Unity generator reads.

Input:  Tools/osm/cache/raw_yonge_dundas.json   (from fetch_osm.py)
Output: Assets/City/Data/downtown_yonge_dundas.json

Coordinates: metres, local equirectangular projection around the Yonge & Dundas
intersection (docs/DESIGN.md 3.1). Output x = east, z = north (Unity axes).

Map data (c) OpenStreetMap contributors, ODbL.
"""
import json
import math
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parents[2]
RAW = ROOT / "Tools" / "osm" / "cache" / "raw_yonge_dundas.json"
OUT = ROOT / "Assets" / "City" / "Data" / "downtown_yonge_dundas.json"
PLACES_OUT = ROOT / "Assets" / "Resources" / "City" / "places.json"   # loaded at runtime

SOUTH, WEST, NORTH, EAST = 43.6505, -79.3845, 43.6625, -79.3775
CLIP_MARGIN_M = 40.0          # keep road geometry this far past the box edge
LEVEL_HEIGHT_M = 3.5
DEFAULT_HEIGHT_M = 12.0

# road width (m) by highway type; null = not a drivable road we build
ROAD_WIDTHS = {
    "motorway": 16, "trunk": 16, "primary": 14, "secondary": 12, "tertiary": 10,
    "unclassified": 8, "residential": 8, "living_street": 6, "service": 5,
    "primary_link": 8, "secondary_link": 7, "tertiary_link": 7,
}
PATH_WIDTHS = {"pedestrian": 6, "footway": 2.5, "path": 2, "cycleway": 2, "steps": 2}


def flat(pts):
    """[(x, z), ...] -> [x0, z0, x1, z1, ...] (Unity's JsonUtility cannot read nested arrays)."""
    return [round(v, 2) for p in pts for v in p]


def unflat(vals):
    return [(vals[i], vals[i + 1]) for i in range(0, len(vals), 2)]


def parse_m(value):
    """'45', '45 m', '45.5m', "150'" -> metres, or None."""
    if not value:
        return None
    m = re.match(r"^\s*([0-9]+(?:\.[0-9]+)?)\s*(m|ft|')?\s*$", value)
    if not m:
        return None
    v = float(m.group(1))
    return v * 0.3048 if m.group(2) in ("ft", "'") else v


def parse_num(value):
    try:
        return float(value)
    except (TypeError, ValueError):
        return None


def signed_area(pts):
    return 0.5 * sum(pts[i][0] * pts[(i + 1) % len(pts)][1] - pts[(i + 1) % len(pts)][0] * pts[i][1]
                     for i in range(len(pts)))


def centroid(pts):
    return (sum(p[0] for p in pts) / len(pts), sum(p[1] for p in pts) / len(pts))


def point_in_poly(pt, poly):
    x, z = pt
    inside = False
    j = len(poly) - 1
    for i in range(len(poly)):
        xi, zi = poly[i]
        xj, zj = poly[j]
        if (zi > z) != (zj > z) and x < (xj - xi) * (z - zi) / (zj - zi) + xi:
            inside = not inside
        j = i
    return inside


def main():
    raw = json.loads(RAW.read_text(encoding="utf-8"))
    nodes = {e["id"]: (e["lat"], e["lon"]) for e in raw["elements"] if e["type"] == "node"}
    ways = {e["id"]: e for e in raw["elements"] if e["type"] == "way"}
    relations = [e for e in raw["elements"] if e["type"] == "relation"]

    # origin = node shared by Yonge Street and a Dundas Street way
    yonge, dundas = set(), set()
    for w in ways.values():
        name = w.get("tags", {}).get("name", "")
        if "highway" not in w.get("tags", {}):
            continue
        if name == "Yonge Street":
            yonge.update(w["nodes"])
        elif name.startswith("Dundas Street"):
            dundas.update(w["nodes"])
    shared = sorted(yonge & dundas)
    if not shared:
        raise SystemExit("could not find the Yonge & Dundas intersection node")
    lat0, lon0 = nodes[shared[0]]
    kx = 111320.0 * math.cos(math.radians(lat0))
    kz = 110540.0

    def project(nid):
        lat, lon = nodes[nid]
        return ((lon - lon0) * kx, (lat - lat0) * kz)

    xmin, xmax = (WEST - lon0) * kx, (EAST - lon0) * kx
    zmin, zmax = (SOUTH - lat0) * kz, (NORTH - lat0) * kz

    def in_box(p, margin=0.0):
        return xmin - margin <= p[0] <= xmax + margin and zmin - margin <= p[1] <= zmax + margin

    def ring(node_ids):
        pts = [project(n) for n in node_ids if n in nodes]
        if len(pts) > 1 and pts[0] == pts[-1]:
            pts = pts[:-1]
        if len(pts) < 3:
            return None
        if signed_area(pts) < 0:      # counter-clockwise when viewed from above (+y)
            pts.reverse()
        return pts

    # ---- buildings -------------------------------------------------------
    height_src = {"height": 0, "levels": 0, "default": 0}

    def building_record(eid, tags, pts, is_part):
        h = parse_m(tags.get("height"))
        if h is not None:
            height_src["height"] += 1
        else:
            lv = parse_num(tags.get("building:levels"))
            if lv:
                h = lv * LEVEL_HEIGHT_M
                height_src["levels"] += 1
            else:
                h = DEFAULT_HEIGHT_M
                height_src["default"] += 1
        min_h = parse_m(tags.get("min_height"))
        if min_h is None:
            ml = parse_num(tags.get("building:min_level"))
            min_h = ml * LEVEL_HEIGHT_M if ml else 0.0
        if tags.get("building") == "roof" or tags.get("building:part") == "roof":
            min_h = max(min_h, h - 1.0)  # canopies: thin slab at the top
        return {
            "id": eid, "part": is_part,
            "kind": tags.get("building:part") or tags.get("building", "yes"),
            "name": tags.get("name", ""),
            "h": round(h, 2), "minH": round(min_h, 2),
            "pts": flat(pts),
        }

    outlines, parts = [], []
    for w in ways.values():
        tags = w.get("tags", {})
        is_part = "building:part" in tags
        if not is_part and "building" not in tags:
            continue
        if w["nodes"][0] != w["nodes"][-1]:
            continue
        pts = ring(w["nodes"])
        if not pts or not in_box(centroid(pts)):
            continue
        (parts if is_part else outlines).append(building_record(w["id"], tags, pts, is_part))

    skipped_rel = 0
    for r in relations:
        tags = r.get("tags", {})
        if "building" not in tags:
            continue
        outers = [m["ref"] for m in r["members"] if m["type"] == "way" and m.get("role") == "outer"]
        for ref in outers:
            w = ways.get(ref)
            if not w or w["nodes"][0] != w["nodes"][-1]:
                skipped_rel += 1
                continue
            pts = ring(w["nodes"])
            if pts and in_box(centroid(pts)):
                outlines.append(building_record(r["id"], tags, pts, False))

    # an outline that contains building:parts is drawn by its parts instead (OSM simple 3D buildings)
    part_centres = [centroid(unflat(p["pts"])) for p in parts]
    kept_outlines = [b for b in outlines
                     if not any(point_in_poly(c, unflat(b["pts"])) for c in part_centres)]
    buildings = kept_outlines + parts

    # ---- roads and paths ------------------------------------------------
    roads, paths = [], []
    for w in ways.values():
        tags = w.get("tags", {})
        hw = tags.get("highway")
        if hw == "construction" and tags.get("construction") in ROAD_WIDTHS:
            hw = tags["construction"]   # Queen St at Yonge (Ontario Line works) is still a street surface
        if hw is None or tags.get("area") == "yes":
            continue
        if tags.get("tunnel") in ("yes", "building_passage") or tags.get("layer", "0").startswith("-"):
            continue
        width = parse_m(tags.get("width"))
        lanes = parse_num(tags.get("lanes"))
        if hw in ROAD_WIDTHS:
            target, w_m = roads, width or (lanes * 3.3 if lanes else ROAD_WIDTHS[hw])
        elif hw in PATH_WIDTHS:
            if tags.get("footway") == "sidewalk":
                continue      # sidewalks are generated along roads instead
            target, w_m = paths, width or PATH_WIDTHS[hw]
        else:
            continue
        pts = [project(n) for n in w["nodes"] if n in nodes]
        # split the polyline into runs that stay inside the clipped box
        run = []
        for p in pts + [None]:
            if p is not None and in_box(p, CLIP_MARGIN_M):
                run.append(p)
                continue
            if len(run) >= 2:
                target.append({
                    "id": w["id"], "type": hw, "name": tags.get("name", ""),
                    "width": round(w_m, 2), "oneway": tags.get("oneway") == "yes",
                    "pts": flat(run),
                })
            run = []

    # ---- plazas (pedestrian areas, squares) -----------------------------
    plazas = []
    for w in ways.values():
        tags = w.get("tags", {})
        is_plaza = (tags.get("highway") == "pedestrian" and tags.get("area") == "yes") \
            or "area:highway" in tags or tags.get("place") == "square"
        if not is_plaza or w["nodes"][0] != w["nodes"][-1]:
            continue
        pts = ring(w["nodes"])
        if pts and in_box(centroid(pts)):
            # area:highway=primary/secondary/... is a road surface polygon, not walkable paving
            ah = tags.get("area:highway", "")
            kind = "road" if ah in ROAD_WIDTHS else "pedestrian"
            plazas.append({"id": w["id"], "name": tags.get("name", ""), "kind": kind,
                           "pts": flat(pts)})

    # ---- named places (mission targets, docs/DESIGN.md 3D) -----------------
    place_keys = ("amenity", "shop", "leisure", "place", "tourism", "railway", "public_transport")
    places, seen_names = [], set()
    for el in raw["elements"]:
        tags = el.get("tags", {})
        name = tags.get("name")
        if not name or name in seen_names or "highway" in tags:
            continue
        cat = next((f"{k}={tags[k]}" for k in place_keys if k in tags), None)
        if cat is None:
            continue
        if el["type"] == "node":
            pt = project(el["id"])
        elif el["type"] == "way" and len(el.get("nodes", [])) >= 3:
            pts = ring(el["nodes"])
            if not pts:
                continue
            pt = centroid(pts)
        else:
            continue
        if not in_box(pt):
            continue
        seen_names.add(name)
        places.append({"name": name, "category": cat, "x": round(pt[0], 2), "z": round(pt[1], 2)})
    PLACES_OUT.parent.mkdir(parents=True, exist_ok=True)
    PLACES_OUT.write_text(json.dumps({"source": "OpenStreetMap contributors, ODbL", "places": places}, indent=1),
                          encoding="utf-8")
    print(f"places {len(places)} -> {PLACES_OUT}")

    out = {
        "source": "OpenStreetMap contributors, ODbL",
        "origin": {"lat": lat0, "lon": lon0, "node": shared[0]},
        "bounds": {"xmin": round(xmin, 2), "xmax": round(xmax, 2), "zmin": round(zmin, 2), "zmax": round(zmax, 2)},
        "buildings": buildings, "roads": roads, "paths": paths, "plazas": plazas,
    }
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(out, separators=(",", ":")), encoding="utf-8")

    tallest = sorted(buildings, key=lambda b: -b["h"])[:5]
    print(f"origin node {shared[0]} at {lat0:.6f},{lon0:.6f}; box x {xmin:.0f}..{xmax:.0f} z {zmin:.0f}..{zmax:.0f} m")
    print(f"buildings {len(buildings)} (outlines {len(kept_outlines)}/{len(outlines)} kept, parts {len(parts)}); "
          f"height from tag {height_src['height']}, levels {height_src['levels']}, default {height_src['default']}; "
          f"multipolygon outers skipped {skipped_rel}")
    print(f"roads {len(roads)}, paths {len(paths)}, plazas {len(plazas)}")
    print("tallest: " + "; ".join(f"{b['name'] or b['id']} {b['h']}m" for b in tallest))
    print(f"wrote {OUT} ({OUT.stat().st_size / 1024:.0f} KB)")


if __name__ == "__main__":
    main()
