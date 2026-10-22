"""TTC Line 1 under Yonge (Queen - TMU - College) from OpenStreetMap -> game JSON (docs/DESIGN.md 3N.2).

    python Tools/osm/fetch_ttc.py            # converts the cache (fetches it first if missing)
    python Tools/osm/fetch_ttc.py --refetch  # pulls fresh data from Overpass, then converts

Cache:  Tools/osm/cache/raw_ttc_line1.json   (Overpass "out geom": ways carry their own node coordinates)
Output: Assets/City/Data/ttc_line1.json

Same projection as the city (build_city_json.py): x = east, z = north, metres from the Yonge & Dundas
node in downtown_yonge_dundas.json `origin`. Point lists are flat [x0, z0, x1, z1, ...] (JsonUtility).

What the game gets:
  alignment  the line's centreline (midway between the two tracks), resampled every SPACING m from south
             to north; distance along it is s = index * SPACING. Trains, tunnels and stations all hang off it.
  stations   centre s from the middle of the two OSM side platforms (the station node itself sits up to
             27 m off at College), plus the along-track heading there.
  entrances  every subway_entrance near the three stations, with the station-frame offsets u (along the
             line, north +) and v (across it, east +), a kind (street / elevator / path) and OSM level.
  tracks     the raw northbound and southbound OSM tracks (for the map overlay).

Map data (c) OpenStreetMap contributors, ODbL.
"""
import json
import math
import pathlib
import sys
import urllib.parse
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[2]
RAW = ROOT / "Tools" / "osm" / "cache" / "raw_ttc_line1.json"
CITY = ROOT / "Assets" / "City" / "Data" / "downtown_yonge_dundas.json"
OUT = ROOT / "Assets" / "City" / "Data" / "ttc_line1.json"

# Union to past Bloor along Yonge: wide enough for the tracks' full ways, King's entrances and Wellesley's platforms
SOUTH, WEST, NORTH, EAST = 43.6460, -79.3870, 43.6730, -79.3760
OVERPASS_URLS = [
    "https://overpass-api.de/api/interpreter",
    "https://overpass.kumi.systems/api/interpreter",
    "https://overpass.private.coffee/api/interpreter",
]
QUERY = f"""
[out:json][timeout:90];
(
  way["railway"="subway"]({SOUTH},{WEST},{NORTH},{EAST});
  way["railway"="platform"]({SOUTH},{WEST},{NORTH},{EAST});
  node["railway"="station"]["station"="subway"]({SOUTH},{WEST},{NORTH},{EAST});
  node["railway"="subway_entrance"]({SOUTH},{WEST},{NORTH},{EAST});
);
out geom;
"""

STATIONS = ["Queen", "TMU", "College"]   # built now (3N); others on the line come later
NB_WAY, SB_WAY = 7657604, 35981326       # the two Yonge main tracks (way direction = direction of travel)
SPACING = 2.0                             # alignment resample step (m)
KEEP_SOUTH, KEEP_NORTH = 520.0, 520.0     # alignment kept this far past Queen / College (tunnels, holding trains)
ENTRANCE_RADIUS = 120.0                   # entrance belongs to the nearest built station within this
PLATFORM_RADIUS = 60.0
TRACK_GAP = 4.1                           # centre to centre, measured 4.04 at TMU (3N.2)
PLATFORM_LENGTH = 152.0                   # TTC standard (OSM traces are approximate)


def fetch():
    data = urllib.parse.urlencode({"data": QUERY}).encode()
    for url in OVERPASS_URLS:
        req = urllib.request.Request(url, data=data, headers={
            "User-Agent": "LifeInDowntown/0.1 (github.com/Hamzah-Muhammad/LifeInDowntown)"})
        try:
            with urllib.request.urlopen(req, timeout=120) as resp:
                raw = json.load(resp)
            print(f"fetched from {url}")
            RAW.parent.mkdir(parents=True, exist_ok=True)
            RAW.write_text(json.dumps(raw), encoding="utf-8")
            return
        except (urllib.error.URLError, TimeoutError) as e:
            print(f"{url} failed: {e}")
    sys.exit("all Overpass mirrors failed; the cache was left as it was")


def flat(pts):
    return [round(v, 2) for p in pts for v in p]


def seg_nearest(pl, p):
    """(distance, nearest point, unit tangent) of polyline pl to point p."""
    best = (1e18, None, None)
    for a, b in zip(pl, pl[1:]):
        dx, dz = b[0] - a[0], b[1] - a[1]
        ll = dx * dx + dz * dz
        if ll == 0:
            continue
        t = max(0.0, min(1.0, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dz) / ll))
        q = (a[0] + t * dx, a[1] + t * dz)
        d = math.hypot(p[0] - q[0], p[1] - q[1])
        if d < best[0]:
            n = math.sqrt(ll)
            best = (d, q, (dx / n, dz / n))
    return best


def resample(pl, step):
    out, carry = [pl[0]], 0.0
    for a, b in zip(pl, pl[1:]):
        seg = math.hypot(b[0] - a[0], b[1] - a[1])
        d = step - carry
        while d <= seg:
            t = d / seg
            out.append((a[0] + t * (b[0] - a[0]), a[1] + t * (b[1] - a[1])))
            d += step
        carry = seg - (d - step)
    return out


def main():
    if "--refetch" in sys.argv or not RAW.exists():
        fetch()
    raw = json.loads(RAW.read_text(encoding="utf-8"))
    origin = json.loads(CITY.read_text(encoding="utf-8"))["origin"]
    lat0, lon0 = origin["lat"], origin["lon"]
    kx, kz = 111320.0 * math.cos(math.radians(lat0)), 110540.0

    def P(lat, lon):
        return ((lon - lon0) * kx, (lat - lat0) * kz)

    ways = {e["id"]: e for e in raw["elements"] if e["type"] == "way"}
    nodes = [e for e in raw["elements"] if e["type"] == "node"]
    nb = [P(n["lat"], n["lon"]) for n in ways[NB_WAY]["geometry"]]
    sb = [P(n["lat"], n["lon"]) for n in ways[SB_WAY]["geometry"]]

    # station centres: midpoint of the two side platforms closest to each station node
    st_nodes = {e["tags"]["name"]: P(e["lat"], e["lon"]) for e in nodes
                if e.get("tags", {}).get("railway") == "station" and e["tags"].get("name") in STATIONS}
    plats = []
    for w in ways.values():
        if w["tags"].get("railway") == "platform" and "geometry" in w:
            g = [P(n["lat"], n["lon"]) for n in w["geometry"]]
            plats.append((w["id"], (sum(q[0] for q in g) / len(g), sum(q[1] for q in g) / len(g)), g))

    # centreline: midpoints between NB samples and the nearest SB point, smoothed, kept around the 3 stations
    mids = []
    for p in resample(nb, SPACING):
        _, q, _ = seg_nearest(sb, p)
        mids.append(((p[0] + q[0]) / 2, (p[1] + q[1]) / 2))
    k = 4
    smooth = [(sum(m[0] for m in mids[max(0, i - k):i + k + 1]) / len(mids[max(0, i - k):i + k + 1]),
               sum(m[1] for m in mids[max(0, i - k):i + k + 1]) / len(mids[max(0, i - k):i + k + 1]))
              for i in range(len(mids))]
    line = resample(smooth, SPACING)

    def s_of(p):
        best = (1e18, 0.0)
        for i, (a, b) in enumerate(zip(line, line[1:])):
            dx, dz = b[0] - a[0], b[1] - a[1]
            ll = dx * dx + dz * dz
            t = max(0.0, min(1.0, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dz) / ll))
            d = math.hypot(p[0] - a[0] - t * dx, p[1] - a[1] - t * dz)
            if d < best[0]:
                best = (d, (i + t) * SPACING)
        return best[1]

    centres = {}
    for name, sp in st_nodes.items():
        near = sorted((math.hypot(c[0] - sp[0], c[1] - sp[1]), pid, c) for pid, c, _ in plats)
        near = [n for n in near if n[0] < PLATFORM_RADIUS][:2]
        if len(near) < 2:
            sys.exit(f"{name}: expected 2 side platforms within {PLATFORM_RADIUS} m, found {len(near)}")
        c = ((near[0][2][0] + near[1][2][0]) / 2, (near[0][2][1] + near[1][2][1]) / 2)
        centres[name] = (s_of(c), [n[1] for n in near])

    s_lo = min(v[0] for v in centres.values()) - KEEP_SOUTH
    s_hi = max(v[0] for v in centres.values()) + KEEP_NORTH
    i0, i1 = int(s_lo // SPACING), int(math.ceil(s_hi / SPACING))
    kept = line[i0:i1 + 1]
    shift = i0 * SPACING

    def frame(s):
        i = max(0, min(len(kept) - 2, int(s // SPACING)))
        t = s / SPACING - i
        a, b = kept[i], kept[i + 1]
        dx, dz = b[0] - a[0], b[1] - a[1]
        n = math.hypot(dx, dz)
        return (a[0] + t * dx, a[1] + t * dz), (dx / n, dz / n)

    # the station boxes are straight: within STRAIGHT m of each centre the line is the centre tangent exactly,
    # easing back to the traced curve over BLEND m (College bends 1.6 m off its tangent within 100 m)
    STRAIGHT, BLEND = 100.0, 80.0
    for name in centres:
        sc = centres[name][0] - shift
        (cx, cz), (fx, fz) = frame(sc)
        for i in range(len(kept)):
            ds = i * SPACING - sc
            if abs(ds) >= STRAIGHT + BLEND:
                continue
            w = 1.0 if abs(ds) <= STRAIGHT else 1.0 - (abs(ds) - STRAIGHT) / BLEND
            w = w * w * (3 - 2 * w)
            kept[i] = (kept[i][0] + (cx + fx * ds - kept[i][0]) * w, kept[i][1] + (cz + fz * ds - kept[i][1]) * w)

    stations = []
    for name in sorted(centres, key=lambda n: centres[n][0]):
        s = centres[name][0] - shift
        (cx, cz), (fx, fz) = frame(s)
        gap = math.hypot(*[a - b for a, b in zip(seg_nearest(nb, (cx, cz))[1], seg_nearest(sb, (cx, cz))[1])])
        stations.append({"name": name, "s": round(s, 2), "x": round(cx, 2), "z": round(cz, 2),
                         "dirX": round(fx, 4), "dirZ": round(fz, 4), "measuredGap": round(gap, 2),
                         "platformWays": centres[name][1]})

    entrances = []
    for e in nodes:
        t = e.get("tags", {})
        if t.get("railway") != "subway_entrance":
            continue
        p = P(e["lat"], e["lon"])
        st = min(stations, key=lambda s: math.hypot(s["x"] - p[0], s["z"] - p[1]))
        if math.hypot(st["x"] - p[0], st["z"] - p[1]) > ENTRANCE_RADIUS:
            continue
        name = t.get("name", "Entrance")
        below = t.get("level", "0").startswith("-")      # College Park Shops' door is below street level
        kind = "path" if "PATH" in name or below else "elevator" if "Elevator" in name else "street"
        ox, oz = p[0] - st["x"], p[1] - st["z"]
        entrances.append({"name": name, "osmId": e["id"], "station": st["name"], "kind": kind,
                          "level": t.get("level", "0"), "wheelchair": t.get("wheelchair") == "yes",
                          "x": round(p[0], 2), "z": round(p[1], 2),
                          "u": round(ox * st["dirX"] + oz * st["dirZ"], 2),
                          "v": round(ox * st["dirZ"] - oz * st["dirX"], 2)})
    entrances.sort(key=lambda e: (e["station"], e["kind"], e["name"]))

    out = {
        "source": "OpenStreetMap contributors (ODbL), Overpass pull " + raw.get("osm3s", {}).get("timestamp_osm_base", "?"),
        "origin": origin, "spacing": SPACING, "trackGap": TRACK_GAP, "platformLength": PLATFORM_LENGTH,
        "alignment": flat(kept), "stations": stations, "entrances": entrances,
        "nbTrack": flat(nb), "sbTrack": flat(sb),
    }
    OUT.write_text(json.dumps(out, indent=1), encoding="utf-8")
    for s in stations:
        n = sum(1 for e in entrances if e["station"] == s["name"])
        print(f"{s['name']:8s} s={s['s']:7.1f} at ({s['x']:7.1f}, {s['z']:7.1f}) gap {s['measuredGap']} m, {n} entrances")
    for a, b in zip(stations, stations[1:]):
        print(f"  {a['name']} -> {b['name']}: {b['s'] - a['s']:.0f} m along the line")
    print(f"alignment {len(kept)} points, {(len(kept) - 1) * SPACING:.0f} m; {len(entrances)} entrances -> {OUT}")


if __name__ == "__main__":
    main()
