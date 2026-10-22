"""Lay out the three Line 1 stations (docs/DESIGN.md 3N.4) in each station's own frame.

Reads   Assets/City/Data/ttc_line1.json            (fetch_ttc.py: stations, entrances)
        Assets/City/Data/downtown_yonge_dundas.json (buildings and roads, to keep stair openings on the sidewalk)
Writes  Assets/City/Data/ttc_stations.json         (read by CityGenerator.Subway)
        Screenshots/ttc_plan_<station>.png          (debug plan, gitignored)

Station frame: u = metres along the line (north +), v = metres across it (east +), origin = station centre.
Levels (3N.4): street 0, concourse floor -4.2 / ceiling -1.2, platform floor -8.55 / ceiling -4.6.

Fixed parts, the same at every station:
  platforms   2 side platforms, |v| 3.7..7.2, u -76..76 (152 m)
  concourses  one over each end of the platforms, u 62..96 (mirrored south), |v| <= 7.2
  fare line   across each concourse at |u| = 84: paid side (stairs down) inward, unpaid side outward
  stairs down at the platform ends, |u| 69..76 (top at 76), one per platform per end: 2.3 m stair + 1.2 m escalator
Planned parts:
  street stairs  one per OSM street entrance: a 3 m x 7.6 m opening placed on open pavement (not a road,
                 not a building) as close to the OSM node as fits, descending 4.2 m to concourse level
  passages       3 m corridors at concourse level, from each stair's foot to the nearer concourse's unpaid side
  elevator       the Sankofa Square Elevator: a street kiosk and a landing below it, joined the same way
  PATH doors     shuttered doors on the unpaid-side walls nearest each PATH entrance (the PATH is not built)
"""
import json
import math
import pathlib

ROOT = pathlib.Path(__file__).resolve().parents[2]
LINE = ROOT / "Assets" / "City" / "Data" / "ttc_line1.json"
CITY = ROOT / "Assets" / "City" / "Data" / "downtown_yonge_dundas.json"
OUT = ROOT / "Assets" / "City" / "Data" / "ttc_stations.json"
SHOTS = ROOT / "Screenshots"

HALF_PLATFORM = 76.0
PLAT_IN, PLAT_OUT = 3.7, 7.2
CONC_IN, CONC_OUT, FARE_U = 62.0, 96.0, 84.0
STAIR_TOP, STAIR_BOT = 76.0, 69.0
STREET_W, STREET_L = 2.6, 7.6            # opening: 2.2 m clear between 0.2 m walls; 0.9 m landing + 6.7 m of steps
ELEV_W = 2.6
CORRIDOR_W = 3.0
SEARCH_R = (12.0, 30.0)                  # how far an opening may move from its OSM node (second try: wider)
CLEAR_ROAD, CLEAR_BLDG = 0.15, 0.25        # extra clearance from the kerb line and from building walls


def rect(u0, u1, v0, v1):
    return [min(u0, u1), max(u0, u1), min(v0, v1), max(v0, v1)]


def overlap(a, b, pad=0.0):
    return a[0] < b[1] + pad and b[0] < a[1] + pad and a[2] < b[3] + pad and b[2] < a[3] + pad


def pip(x, z, poly):
    ins = False
    j = len(poly) - 1
    for i in range(len(poly)):
        (x1, z1), (x2, z2) = poly[i], poly[j]
        if (z1 > z) != (z2 > z) and x < x1 + (z - z1) * (x2 - x1) / (z2 - z1):
            ins = not ins
        j = i
    return ins


def seg_dist(px, pz, a, b):
    dx, dz = b[0] - a[0], b[1] - a[1]
    ll = dx * dx + dz * dz
    t = 0.0 if ll == 0 else max(0.0, min(1.0, ((px - a[0]) * dx + (pz - a[1]) * dz) / ll))
    return math.hypot(px - a[0] - t * dx, pz - a[1] - t * dz)


def main():
    line = json.loads(LINE.read_text(encoding="utf-8"))
    city = json.loads(CITY.read_text(encoding="utf-8"))
    pts = lambda f: list(zip(f[0::2], f[1::2]))
    # outlines, plus building parts that come down to the street (their walls are solid too)
    buildings = [pts(b["pts"]) for b in city["buildings"] if not b["part"] or (b.get("minH") or 0) < 2.5]
    roads = [(r["width"] / 2, pts(r["pts"])) for r in city["roads"]]

    out = {"stations": []}
    for st in line["stations"]:
        cx, cz, fx, fz = st["x"], st["z"], st["dirX"], st["dirZ"]
        to_world = lambda u, v: (cx + fx * u + fz * v, cz + fz * u - fx * v)
        near_b = [b for b in buildings if min(math.hypot(x - cx, z - cz) for x, z in b) < 220]
        near_r = [(h, p) for h, p in roads if min(math.hypot(x - cx, z - cz) for x, z in p) < 260]

        def street_ok(r):
            """every 0.5 m sample of the rect (station frame) lies on open pavement"""
            u = r[0]
            while u <= r[1] + 1e-6:
                v = r[2]
                while v <= r[3] + 1e-6:
                    x, z = to_world(u, v)
                    for b in near_b:
                        if pip(x, z, b) or min(seg_dist(x, z, b[i], b[(i + 1) % len(b)]) for i in range(len(b))) < CLEAR_BLDG:
                            return False
                    for h, p in near_r:
                        if any(seg_dist(x, z, p[i], p[i + 1]) < h + CLEAR_ROAD for i in range(len(p) - 1)):
                            return False
                    v += 0.5
                u += 0.5
            return True

        holes, conc = [], []
        for e in (1, -1):
            conc.append(rect(e * CONC_IN, e * CONC_OUT, -PLAT_OUT, PLAT_OUT))
            for side in (1, -1):
                holes.append(rect(e * STAIR_BOT, e * STAIR_TOP, side * PLAT_IN, side * PLAT_OUT))
        corridors = list(conc)
        columns = []          # street stair / kiosk footprints: nothing else may pass under them
        stairs, elevators, skipped = [], [], []

        def route(bu, bv, du, dv, end):
            """L-shaped corridor rects from a foot point to the unpaid side of concourse `end`. The corridor
            must leave the foot in the stair's own direction (du, dv), never fold back under the stair."""
            tu = end * (FARE_U + CONC_OUT) / 2
            tv = max(-PLAT_OUT + CORRIDOR_W / 2, min(PLAT_OUT - CORRIDOR_W / 2, bv))
            h = CORRIDOR_W / 2
            sgn = lambda x: 0 if abs(x) < 1e-3 else (1 if x > 0 else -1)
            options = []
            for order in ("uv", "vu"):
                if order == "uv":
                    legs = [(rect(bu, tu, bv - h, bv + h), (sgn(tu - bu), 0)), (rect(tu - h, tu + h, bv, tv), (0, sgn(tv - bv)))]
                else:
                    legs = [(rect(bu - h, bu + h, bv, tv), (0, sgn(tv - bv))), (rect(bu, tu, tv - h, tv + h), (sgn(tu - bu), 0))]
                legs = [l for l in legs if l[1] != (0, 0)]
                if not legs or legs[0][1] != (du, dv):
                    continue
                corner = (tu, bv) if order == "uv" else (bu, tv)
                options.append((abs(tu - bu) + abs(tv - bv), [l[0] for l in legs], [corner, (tu, tv)]))
            return options

        def place(e, kind, radius):
            u0, v0 = e["u"], e["v"]
            end = 1 if u0 >= 0 else -1
            best = None
            steps = [i * 0.5 for i in range(-int(radius * 2), int(radius * 2) + 1)]
            offsets = sorted(((ou, ov) for ou in steps for ov in steps if math.hypot(ou, ov) <= radius), key=lambda o: math.hypot(*o))
            for ou, ov in offsets:
                cost0 = math.hypot(ou, ov) * 3.0
                if best is not None and cost0 >= best[0]:
                    break
                ou_, ov_ = u0 + ou, v0 + ov
                street_checked = None
                for du, dv in [(1, 0), (-1, 0), (0, 1), (0, -1)]:
                    if kind == "street":
                        # opening centred on (ou_, ov_), long side along (du, dv); the foot is the far end
                        hl, hw = STREET_L / 2, STREET_W / 2
                        col = rect(ou_ - hl, ou_ + hl, ov_ - hw, ov_ + hw) if du else rect(ou_ - hw, ou_ + hw, ov_ - hl, ov_ + hl)
                        bu, bv = ou_ + du * hl, ov_ + dv * hl
                    else:
                        hw = ELEV_W / 2
                        col = rect(ou_ - hw, ou_ + hw, ov_ - hw, ov_ + hw)
                        bu, bv = ou_ + du * hw, ov_ + dv * hw
                    if any(overlap(col, c, 0.6) for c in columns) or any(overlap(col, hh, 0.3) for hh in holes):
                        continue
                    if any(overlap(col, c, -0.05) for c in corridors):
                        continue
                    # the street side of the opening (where you walk in) must not be inside a building
                    if kind == "street":
                        tu_, tv_ = ou_ - du * (STREET_L / 2 + 0.8), ov_ - dv * (STREET_L / 2 + 0.8)
                        x_, z_ = to_world(tu_, tv_)
                        if any(pip(x_, z_, b) for b in near_b):
                            continue
                    for length, legs, way in route(bu, bv, du, dv, end):
                        cost = cost0 + length * 0.15
                        if best is not None and cost >= best[0]:
                            continue
                        if any(overlap(l, c, 0.3) for l in legs for c in columns):
                            continue
                        if any(overlap(l, hh, 0.3) for l in legs for hh in holes):
                            continue
                        key = tuple(col)
                        if street_checked is None or street_checked[0] != key:
                            street_checked = (key, street_ok(col))
                        if not street_checked[1]:
                            break
                        best = (cost, col, (du, dv), (bu, bv), legs, way)
            return best

        for e in [x for x in line["entrances"] if x["station"] == st["name"] and x["kind"] in ("street", "elevator")]:
            got = place(e, e["kind"], SEARCH_R[0]) or place(e, e["kind"], SEARCH_R[1])
            if got is None:
                # e.g. "Entrance by 2 Queen Street East" is a door inside the tower: no open pavement near it
                print(f"  {st['name']}: NO PLACE for {e['name']} (inside a building; not built)")
                skipped.append(e["name"])
                continue
            cost, col, (du, dv), (bu, bv), legs, way = got
            columns.append(col)
            corridors.extend(legs)
            moved = math.hypot((col[0] + col[1]) / 2 - e["u"], (col[2] + col[3]) / 2 - e["v"])
            rec = {"name": e["name"], "osmId": e["osmId"], "rect": [round(x, 2) for x in col],
                   "dirU": du, "dirV": dv, "footU": round(bu, 2), "footV": round(bv, 2), "movedM": round(moved, 1),
                   "end": 1 if e["u"] >= 0 else -1, "way": [round(c, 2) for p in way for c in p]}
            (stairs if e["kind"] == "street" else elevators).append(rec)
            print(f"  {st['name']:8s} {e['kind']:8s} {e['name'][:40]:40s} moved {moved:4.1f} m, corridor {sum((l[1]-l[0])+(l[3]-l[2]) for l in legs) - 2 * CORRIDOR_W * len(legs) / 2:5.1f} m")

        # PATH doors: on the unpaid-side walls (end wall and the two side walls), nearest each PATH entrance,
        # where no corridor joins
        doors = []
        for e in [x for x in line["entrances"] if x["station"] == st["name"] and x["kind"] == "path"]:
            end = 1 if e["u"] >= 0 else -1
            cands = []
            for k in range(0, 49):
                t = k * 0.5
                cands.append((end * CONC_OUT, -PLAT_OUT + 1.0 + t * (2 * PLAT_OUT - 2.0) / 24, end, 0))      # end wall
            for side in (1, -1):
                for k in range(0, 21):
                    u = end * (FARE_U + 1.2 + k * (CONC_OUT - FARE_U - 2.4) / 20)
                    cands.append((u, side * PLAT_OUT, 0, side))
            best = None
            for u, v, nu, nv in cands:
                probe = rect(u + nu * 0.3 - 0.9 * abs(nv), u + nu * 0.3 + 0.9 * abs(nv), v + nv * 0.3 - 0.9 * abs(nu), v + nv * 0.3 + 0.9 * abs(nu))
                if any(overlap(probe, c, 0.4) for c in corridors[2:]) or any(math.hypot(u - d["u"], v - d["v"]) < 2.6 for d in doors):
                    continue
                dd = math.hypot(u - e["u"], v - e["v"])
                if best is None or dd < best[0]:
                    best = (dd, u, v, nu, nv)
            if best:
                doors.append({"name": e["name"], "u": round(best[1], 2), "v": round(best[2], 2), "nU": best[3], "nV": best[4]})

        out["stations"].append({
            "name": st["name"], "corridors": [round(x, 2) for r in corridors for x in r], "holes": [x for r in holes for x in r],
            "streetStairs": stairs, "elevators": elevators, "pathDoors": doors, "skipped": skipped,
        })
        draw(st, corridors, holes, columns, stairs, elevators, doors)

    OUT.write_text(json.dumps(out, indent=1), encoding="utf-8")
    print(f"wrote {OUT}")


def draw(st, corridors, holes, columns, stairs, elevators, doors):
    try:
        from PIL import Image, ImageDraw
    except ImportError:
        return
    s, w, h = 4, 140, 260
    img = Image.new("RGB", (w * s, h * s), (30, 30, 34))
    d = ImageDraw.Draw(img)
    P = lambda u, v: ((v + w / 2) * s, (h / 2 - u) * s)
    R = lambda r, **k: d.rectangle([P(max(r[0], r[1]), min(r[2], r[3])), P(min(r[0], r[1]), max(r[2], r[3]))], **k)
    R([-HALF_PLATFORM, HALF_PLATFORM, -PLAT_OUT, PLAT_OUT], fill=(60, 60, 70))
    for side in (1, -1):
        R([-HALF_PLATFORM, HALF_PLATFORM, side * PLAT_IN, side * PLAT_OUT], fill=(110, 110, 120))
    for r in corridors:
        R(r, outline=(220, 200, 90))
    for r in holes:
        R(r, fill=(200, 80, 80))
    for r in columns:
        R(r, fill=(90, 160, 230))
    for e in (1, -1):
        d.line([P(e * FARE_U, -PLAT_OUT), P(e * FARE_U, PLAT_OUT)], fill=(240, 240, 240), width=2)
    for x in stairs + elevators:
        d.ellipse([P(x["footU"], x["footV"])[0] - 4, P(x["footU"], x["footV"])[1] - 4,
                   P(x["footU"], x["footV"])[0] + 4, P(x["footU"], x["footV"])[1] + 4], fill=(255, 255, 255))
        d.text(P((x["rect"][0] + x["rect"][1]) / 2, x["rect"][3] + 1), x["name"][:28], fill=(230, 230, 230))
    for x in doors:
        d.rectangle([P(x["u"], x["v"])[0] - 4, P(x["u"], x["v"])[1] - 4, P(x["u"], x["v"])[0] + 4, P(x["u"], x["v"])[1] + 4], fill=(80, 220, 120))
    d.text((8, 8), f"{st['name']}  (north up, east right; blue = street openings, red = stairs down, green = PATH doors)", fill=(255, 255, 255))
    SHOTS.mkdir(exist_ok=True)
    img.save(SHOTS / f"ttc_plan_{st['name'].lower()}.png")


if __name__ == "__main__":
    main()
