"""Fetch raw OpenStreetMap data for the milestone-1 area from the Overpass API.

Writes Tools/osm/cache/raw_yonge_dundas.json (gitignored). Run once; the
committed city JSON is built from this cache by build_city_json.py.

Map data (c) OpenStreetMap contributors, ODbL.
"""
import json
import pathlib
import urllib.parse
import urllib.request

# lat/lon bounding box from docs/DESIGN.md section 3.1
SOUTH, WEST, NORTH, EAST = 43.6535, -79.3845, 43.6585, -79.3775
# public Overpass instances, tried in order (the main one often returns 502/504 when busy)
OVERPASS_URLS = [
    "https://overpass-api.de/api/interpreter",
    "https://overpass.kumi.systems/api/interpreter",
    "https://overpass.private.coffee/api/interpreter",
]
OUT = pathlib.Path(__file__).parent / "cache" / "raw_yonge_dundas.json"

QUERY = f"""
[out:json][timeout:90];
(
  way["building"]({SOUTH},{WEST},{NORTH},{EAST});
  way["building:part"]({SOUTH},{WEST},{NORTH},{EAST});
  relation["building"]["type"="multipolygon"]({SOUTH},{WEST},{NORTH},{EAST});
  way["highway"]({SOUTH},{WEST},{NORTH},{EAST});
  way["leisure"]({SOUTH},{WEST},{NORTH},{EAST});
  way["landuse"]({SOUTH},{WEST},{NORTH},{EAST});
  way["area:highway"]({SOUTH},{WEST},{NORTH},{EAST});
);
(._;>;);
out body;
"""


def fetch_from_osm_api():
    """Fallback: the main OSM API's /map call (fine for an area this small).

    Returns everything in the box; build_city_json.py filters by tag, so the
    output is converted to the same element shape Overpass returns.
    """
    url = f"https://api.openstreetmap.org/api/0.6/map.json?bbox={WEST},{SOUTH},{EAST},{NORTH}"
    req = urllib.request.Request(
        url, headers={"User-Agent": "LifeInDowntown/0.1 (github.com/Hamzah-Muhammad/LifeInDowntown)"})
    with urllib.request.urlopen(req, timeout=180) as resp:
        raw = json.load(resp)
    print(f"fetched from {url}")
    return {"elements": raw["elements"]}


def main():
    data = urllib.parse.urlencode({"data": QUERY}).encode()
    raw = None
    for url in OVERPASS_URLS:
        req = urllib.request.Request(
            url, data=data,
            headers={"User-Agent": "LifeInDowntown/0.1 (github.com/Hamzah-Muhammad/LifeInDowntown)"})
        try:
            with urllib.request.urlopen(req, timeout=120) as resp:
                raw = json.load(resp)
            print(f"fetched from {url}")
            break
        except (urllib.error.URLError, TimeoutError) as e:
            print(f"{url} failed: {e}")
    if raw is None:
        raw = fetch_from_osm_api()
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(raw), encoding="utf-8")
    kinds = {}
    for el in raw["elements"]:
        kinds[el["type"]] = kinds.get(el["type"], 0) + 1
    print(f"wrote {OUT} : {kinds}")


if __name__ == "__main__":
    main()
