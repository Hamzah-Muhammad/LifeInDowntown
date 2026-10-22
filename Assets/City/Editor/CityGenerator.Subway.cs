using System;
using System.Collections.Generic;
using System.IO;
using LifeInDowntown.Transit;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace LifeInDowntown.City
{
    // TTC Line 1 stations under Yonge (docs/DESIGN.md 3N.4-3N.5): Queen, TMU, College. The layout comes from
    // plan_ttc_stations.py (street stairs on real pavement, passages, PATH doors); this builds it. Everything is
    // in each station's frame: u along the line (north +), v across it (east +). Levels: street 0, concourse
    // -4.2 floor / -1.2 ceiling, platform -8.55 floor / -4.6 ceiling, track bed -9.8.
    public static partial class CityGenerator
    {
        const string TtcLinePath = "Assets/City/Data/ttc_line1.json";
        const string TtcLayoutPath = "Assets/City/Data/ttc_stations.json";
        const string TtcSignsPath = "Assets/City/Data/ttc_signs.json";

        public const float ConcFloor = -4.2f, ConcCeil = -1.2f, PlatFloor = -8.55f, PlatCeil = -4.6f, TrackBed = -9.8f;
        const float PlatIn = 3.7f, PlatOut = 7.2f, HalfPlat = 76f, StairBot = 69f, StairTop = 76f, FareU = 84f;
        const float Cell = 0.1f;
        const float StationLumens = 2200f;

        [Serializable] public class TtcLine { public float spacing, trackGap, platformLength; public float[] alignment, nbTrack, sbTrack; public TtcStation[] stations; public TtcEntrance[] entrances; }
        [Serializable] public class TtcStation { public string name; public float s, x, z, dirX, dirZ; }
        [Serializable] public class TtcEntrance { public string name, station, kind, level; public float x, z, u, v; }
        [Serializable] class TtcLayout { public TtcStationLayout[] stations; }
        [Serializable] class TtcStationLayout { public string name; public float[] corridors, holes; public TtcStair[] streetStairs, elevators; public TtcDoor[] pathDoors; public string[] skipped; }
        [Serializable] class TtcStair { public string name; public float[] rect, way; public int dirU, dirV, end; public float footU, footV, movedM; }
        [Serializable] class TtcDoor { public string name; public float u, v; public int nU, nV; }
        [Serializable] class TtcSigns { public string[] names; public float[] rects; }

        // station frame
        struct SF
        {
            public Vector3 c, f, r;
            public SF(float x, float z, float fx, float fz) { c = new Vector3(x, 0, z); f = new Vector3(fx, 0, fz); r = new Vector3(fz, 0, -fx); }
            public Vector3 W(float u, float v, float y) => c + f * u + r * v + Vector3.up * y;
            public Vector3 D(float du, float dv) => f * du + r * dv;
        }

        class SubwayMats
        {
            public Material wall, floor, ceiling, concrete, metal, dark, yellow, strip, signs, glass, rubber, orange, blue;
            public Dictionary<string, Rect> sign = new Dictionary<string, Rect>();
        }

        public static TtcLine LoadTtcLine() => JsonUtility.FromJson<TtcLine>(File.ReadAllText(TtcLinePath));

        static void BuildSubway(Transform parent, Materials mats, List<Mesh> meshes)
        {
            if (!File.Exists(TtcLinePath) || !File.Exists(TtcLayoutPath)) { Debug.LogWarning("CITY_WARN no subway data"); return; }
            var line = LoadTtcLine();
            var layout = JsonUtility.FromJson<TtcLayout>(File.ReadAllText(TtcLayoutPath));
            var sm = SubwayMaterials(mats);
            var root = new GameObject("Subway").transform;
            root.SetParent(parent, false);
            var lineComp = root.gameObject.AddComponent<SubwayLine>();
            root.gameObject.AddComponent<LightBudget>();
            lineComp.alignment = line.alignment;
            lineComp.spacing = line.spacing;
            lineComp.trackGap = line.trackGap;

            var holes = new List<Vector2[]>();
            var comps = new List<SubwayStation>();
            foreach (var st in line.stations)
            {
                var lay = Array.Find(layout.stations, l => l.name == st.name);
                if (lay == null) continue;
                comps.Add(BuildStation(root, st, lay, sm, meshes, holes));
            }
            lineComp.stations = comps.ToArray();
            CutGroundHoles(parent, holes);
            int nRoutes = 0; foreach (var c in comps) nRoutes += c.routes.Length;
            Debug.Log($"SUBWAY_OK stations={comps.Count} streetOpenings={holes.Count} routes={nRoutes}");
        }

        static SubwayMats SubwayMaterials(Materials mats)
        {
            var sm = new SubwayMats();
            Material Tex(string name, string tex, float tileM, Color tint, float smooth)
            {
                var m = Materials.Plain(name, tint, smooth, 0f, Color.black, 0f);
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/City/Signs/{tex}");
                m.SetTexture("_BaseColorMap", t);
                m.SetTextureScale("_BaseColorMap", new Vector2(1f / tileM, 1f / tileM));
                HDMaterial.ValidateMaterial(m);
                return m;
            }
            // wall and floor tiles: NEUTRAL placeholder tones until Hamzah's reference photos (3N.9)
            sm.wall = Tex("TtcWallTile", "ttc_tile_wall.png", 0.6f, Color.white, 0.55f);
            sm.floor = Tex("TtcFloorTile", "ttc_tile_floor.png", 0.6f, Color.white, 0.45f);
            sm.ceiling = Materials.Plain("TtcCeiling", new Color(0.72f, 0.72f, 0.7f), 0.2f, 0f, Color.black, 0f);
            sm.concrete = mats.Concrete;
            sm.metal = Materials.Plain("TtcSteel", new Color(0.62f, 0.63f, 0.64f), 0.6f, 0.9f, Color.black, 0f);
            sm.dark = Materials.Plain("TtcDark", new Color(0.08f, 0.08f, 0.09f), 0.35f, 0f, Color.black, 0f);
            sm.yellow = Materials.Plain("TtcTactile", new Color(0.95f, 0.75f, 0.05f), 0.35f, 0f, Color.black, 0f);
            sm.strip = Materials.Plain("TtcLightStrip", Color.white, 0.2f, 0f, new Color(0.93f, 0.97f, 1f), 400f);
            sm.glass = Materials.GlassMaterial("TtcGlass", new Color(0.6f, 0.68f, 0.7f, 0.15f));
            sm.rubber = Materials.Plain("TtcRubber", new Color(0.05f, 0.05f, 0.05f), 0.3f, 0f, Color.black, 0f);
            sm.orange = Materials.Plain("TtcRed", new Color(0.75f, 0.12f, 0.1f), 0.4f, 0f, Color.black, 0f);
            sm.blue = Materials.Plain("TtcBlueLight", new Color(0.1f, 0.2f, 0.9f), 0.3f, 0f, new Color(0.25f, 0.4f, 1f), 60f);
            sm.signs = Materials.Plain("TtcSigns", Color.white, 0.35f, 0f, Color.white, 30f);
            sm.signs.SetTexture("_BaseColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/City/Signs/ttc_signs.png"));
            sm.signs.SetTexture("_EmissiveColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/City/Signs/ttc_signs_emissive.png"));
            HDMaterial.ValidateMaterial(sm.signs);
            var atlas = JsonUtility.FromJson<TtcSigns>(File.ReadAllText(TtcSignsPath));
            for (int i = 0; i < atlas.names.Length; i++)
                sm.sign[atlas.names[i]] = Rect.MinMaxRect(atlas.rects[4 * i], atlas.rects[4 * i + 1], atlas.rects[4 * i + 2], atlas.rects[4 * i + 3]);
            return sm;
        }

        // ------------------------------------------------------------------ geometry helpers (station frame)
        // axis-aligned (in the station frame) box with metre UVs on every face; skip = faces to leave out
        static void Cuboid(MeshBuilder mb, SF sf, float u0, float u1, float v0, float v1, float y0, float y1)
        {
            if (u0 > u1) (u0, u1) = (u1, u0);
            if (v0 > v1) (v0, v1) = (v1, v0);
            if (y0 > y1) (y0, y1) = (y1, y0);
            FaceU(mb, sf, u1, v0, v1, y0, y1, 1); FaceU(mb, sf, u0, v0, v1, y0, y1, -1);
            FaceV(mb, sf, v1, u0, u1, y0, y1, 1); FaceV(mb, sf, v0, u0, u1, y0, y1, -1);
            FaceY(mb, sf, y1, u0, u1, v0, v1, 1); FaceY(mb, sf, y0, u0, u1, v0, v1, -1);
        }

        // plane u = const, facing +u (n = 1) or -u
        static void FaceU(MeshBuilder mb, SF sf, float u, float v0, float v1, float y0, float y1, int n) =>
            mb.Quad(sf.W(u, v0, y0), sf.W(u, v1, y0), sf.W(u, v1, y1), sf.W(u, v0, y1), sf.f * n,
                    new Vector2(v0, y0), new Vector2(v1, y0), new Vector2(v1, y1), new Vector2(v0, y1));

        static void FaceV(MeshBuilder mb, SF sf, float v, float u0, float u1, float y0, float y1, int n) =>
            mb.Quad(sf.W(u0, v, y0), sf.W(u1, v, y0), sf.W(u1, v, y1), sf.W(u0, v, y1), sf.r * n,
                    new Vector2(u0, y0), new Vector2(u1, y0), new Vector2(u1, y1), new Vector2(u0, y1));

        static void FaceY(MeshBuilder mb, SF sf, float y, float u0, float u1, float v0, float v1, int n) =>
            mb.Quad(sf.W(u0, v0, y), sf.W(u1, v0, y), sf.W(u1, v1, y), sf.W(u0, v1, y), Vector3.up * n,
                    new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1));

        // a sign quad from the atlas, centred at p, facing n (toward the reader), w x h metres
        static void SignQuad(MeshBuilder mb, Rect uv, Vector3 p, Vector3 n, float w, float h)
        {
            Vector3 x = Vector3.Cross(Vector3.up, -n).normalized * (w / 2), y = Vector3.up * (h / 2);
            mb.Quad(p - x - y, p + x - y, p + x + y, p - x + y, n,
                    new Vector2(uv.xMin, uv.yMin), new Vector2(uv.xMax, uv.yMin), new Vector2(uv.xMax, uv.yMax), new Vector2(uv.xMin, uv.yMax));
        }

        // flat collision-only surface (stairs walk as ramps)
        static void AddCollider(string name, Transform parent, Mesh mesh, List<Mesh> meshes)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            meshes.Add(mesh);
        }

        static void AddLight(Transform parent, string name, Vector3 at, float lumens, float range, float kelvin = 4300f)
        {
            var lg = new GameObject(name);
            lg.transform.SetParent(parent, false);
            lg.transform.position = at;
            var l = lg.AddComponent<Light>(); lg.AddComponent<HDAdditionalLightData>();
            l.type = LightType.Point; l.lightUnit = LightUnit.Lumen; l.intensity = lumens;
            l.useColorTemperature = true; l.colorTemperature = kelvin; l.color = Color.white; l.range = range;
            l.shadows = LightShadows.None;
        }

        // ------------------------------------------------------------------ one station
        static SubwayStation BuildStation(Transform parent, TtcStation st, TtcStationLayout lay, SubwayMats sm, List<Mesh> meshes, List<Vector2[]> holes)
        {
            var sf = new SF(st.x, st.z, st.dirX, st.dirZ);
            var root = new GameObject($"Station_{st.name}").transform;
            root.SetParent(parent, false);
            var comp = root.gameObject.AddComponent<SubwayStation>();
            comp.stationName = st.name; comp.s = st.s; comp.centre = sf.c; comp.along = sf.f;

            var wall = new MeshBuilder(); var floor = new MeshBuilder(); var ceil = new MeshBuilder(); var conc = new MeshBuilder();
            var metal = new MeshBuilder(); var dark = new MeshBuilder(); var tactile = new MeshBuilder(); var strip = new MeshBuilder();
            var signs = new MeshBuilder(); var glass = new MeshBuilder(); var ramps = new MeshBuilder(); var red = new MeshBuilder();
            var rubber = new MeshBuilder(); var stepsTile = new MeshBuilder(); var stepsMetal = new MeshBuilder(); var stepsConc = new MeshBuilder();

            // ---- platform level
            Cuboid(conc, sf, -HalfPlat, HalfPlat, -PlatIn, PlatIn, TrackBed - 0.3f, TrackBed);               // track bed (rails: track builder)
            foreach (int side in new[] { -1, 1 })
            {
                Cuboid(floor, sf, -HalfPlat, HalfPlat, side * PlatIn, side * PlatOut, TrackBed, PlatFloor);      // platform slab
                Cuboid(tactile, sf, -HalfPlat + 7f, HalfPlat - 7f, side * PlatIn, side * (PlatIn + 0.6f), PlatFloor, PlatFloor + 0.006f);
                Cuboid(wall, sf, -HalfPlat, HalfPlat, side * PlatOut, side * (PlatOut + 0.3f), TrackBed, PlatCeil);   // outer wall
                // the platform ceiling, except over the stair wells at each end
                FaceY(ceil, sf, PlatCeil, -StairBot, StairBot, side * PlatIn, side * PlatOut, -1);
                // fluorescent strips over the platform edge and along the wall
                foreach (float sv in new[] { PlatIn + 0.5f, PlatOut - 0.6f })
                    for (float u = -StairBot + 1f; u < StairBot - 1f; u += 4f)
                        Cuboid(strip, sf, u, u + 3.2f, side * (sv - 0.07f), side * (sv + 0.07f), PlatCeil - 0.06f, PlatCeil - 0.01f);
                for (float u = -StairBot + 6f; u <= StairBot - 6f; u += 12f)
                    AddLight(root, "PlatformLight", sf.W(u, side * 5.4f, PlatCeil - 0.35f), StationLumens, 9f);
                // station name panels and roundels along the wall, facing the platform
                for (float u = -60f; u <= 60f; u += 15f)
                {
                    var n = sf.r * -side;
                    SignQuad(signs, sm.sign[$"name_{st.name}"], sf.W(u, side * (PlatOut - 0.01f), PlatFloor + 2.2f), n, 3.2f, 0.6f);
                    SignQuad(signs, sm.sign["roundel"], sf.W(u + 7.5f, side * (PlatOut - 0.01f), PlatFloor + 2.2f), n, 0.55f, 0.55f);
                }
                // benches, bins, emergency stations
                for (float u = -52f; u <= 52f; u += 26f)
                {
                    Cuboid(metal, sf, u - 1.0f, u + 1.0f, side * (PlatOut - 0.55f), side * (PlatOut - 0.1f), PlatFloor + 0.42f, PlatFloor + 0.47f);
                    Cuboid(metal, sf, u - 0.9f, u - 0.8f, side * (PlatOut - 0.5f), side * (PlatOut - 0.15f), PlatFloor, PlatFloor + 0.42f);
                    Cuboid(metal, sf, u + 0.8f, u + 0.9f, side * (PlatOut - 0.5f), side * (PlatOut - 0.15f), PlatFloor, PlatFloor + 0.42f);
                    Cuboid(dark, sf, u + 3f, u + 3.5f, side * (PlatOut - 0.55f), side * (PlatOut - 0.1f), PlatFloor, PlatFloor + 0.9f);   // bin
                }
                foreach (float u in new[] { -44f, 44f })
                {
                    SignQuad(signs, sm.sign["alarm"], sf.W(u, side * (PlatOut - 0.01f), PlatFloor + 1.6f), sf.r * -side, 0.5f, 0.25f);
                    Cuboid(red, sf, u - 0.12f, u + 0.12f, side * (PlatOut - 0.12f), side * (PlatOut - 0.01f), PlatFloor + 1.1f, PlatFloor + 1.4f);
                    SignQuad(signs, sm.sign["powercut"], sf.W(u + 2.5f, side * (PlatOut - 0.01f), PlatFloor + 1.6f), sf.r * -side, 0.5f, 0.25f);
                }
                // Designated Waiting Area mid-platform: blue sign, intercom pillar, camera, bench, brighter light
                Cuboid(dark, sf, 4.4f, 4.7f, side * (PlatIn + 1.6f), side * (PlatIn + 1.9f), PlatFloor, PlatFloor + 1.7f);
                SignQuad(signs, sm.sign["dwa"], sf.W(4.55f, side * (PlatIn + 1.75f), PlatFloor + 2.1f), sf.f * -1, 1.0f, 0.5f);
                SignQuad(signs, sm.sign["dwa"], sf.W(4.55f, side * (PlatIn + 1.75f), PlatFloor + 2.1f), sf.f, 1.0f, 0.5f);
                Cuboid(dark, sf, 4.5f, 4.65f, side * (PlatOut - 0.3f), side * (PlatOut - 0.05f), PlatCeil - 0.4f, PlatCeil - 0.2f);   // camera
                AddLight(root, "DwaLight", sf.W(4.5f, side * 5.4f, PlatCeil - 0.3f), StationLumens * 0.8f, 6f, 5000f);
                // next-train screens hanging over the platform
                foreach (float u in new[] { -30f, 30f })
                {
                    Cuboid(dark, sf, u - 0.04f, u + 0.04f, side * 5.35f, side * 5.45f, PlatCeil - 0.5f, PlatCeil);
                    Cuboid(dark, sf, u - 0.06f, u + 0.06f, side * 4.6f, side * 6.2f, PlatCeil - 1.15f, PlatCeil - 0.5f);
                    string key = side > 0 ? "screen_nb" : "screen_sb";
                    SignQuad(signs, sm.sign[key], sf.W(u + 0.065f, side * 5.4f, PlatCeil - 0.82f), sf.f, 1.5f, 0.47f);
                    SignQuad(signs, sm.sign[key], sf.W(u - 0.065f, side * 5.4f, PlatCeil - 0.82f), -sf.f, 1.5f, 0.47f);
                }
            }
            // track ceiling across the whole station, and headers over the tunnel mouths
            FaceY(ceil, sf, PlatCeil, -HalfPlat, HalfPlat, -PlatIn, PlatIn, -1);
            foreach (int e in new[] { -1, 1 }) FaceU(wall, sf, e * HalfPlat, -PlatIn, PlatIn, TunnelTop, PlatCeil, -e);

            // ---- the stairs down at both ends of both platforms: 2.3 m stair + 1.2 m escalator (up)
            foreach (int e in new[] { -1, 1 })
                foreach (int side in new[] { -1, 1 })
                    BuildPlatformStair(root, sf, e, side, stepsTile, stepsMetal, metal, rubber, ramps);

            // ---- concourse level from the planned rectangles
            var grid = new ConcourseGrid(lay);
            grid.Build(sf, floor, ceil, wall, metal, glass, strip, root);

            // fare lines (gates open: free for now, 3N), PRESTO machines, signs
            foreach (int e in new[] { -1, 1 })
            {
                for (int k = 0; k < 12; k++)
                {
                    float v = -6.6f + 1.2f * k;
                    Cuboid(metal, sf, e * (FareU - 0.7f), e * (FareU + 0.7f), v - 0.11f, v + 0.11f, ConcFloor, ConcFloor + 1.0f);
                    Cuboid(dark, sf, e * (FareU + 0.25f), e * (FareU + 0.45f), v - 0.12f, v + 0.12f, ConcFloor + 1.0f, ConcFloor + 1.06f);   // card reader
                    if (k % 3 == 1) SignQuad(signs, sm.sign["presto"], sf.W(e * (FareU + 0.71f), v, ConcFloor + 0.75f), sf.f * e, 0.36f, 0.18f);
                }
                Cuboid(dark, sf, e * (FareU + 4.0f), e * (FareU + 4.6f), -PlatOut + 0.05f, -PlatOut + 0.9f, ConcFloor, ConcFloor + 1.8f);   // PRESTO machines
                Cuboid(dark, sf, e * (FareU + 5.2f), e * (FareU + 5.8f), -PlatOut + 0.05f, -PlatOut + 0.9f, ConcFloor, ConcFloor + 1.8f);
                SignQuad(signs, sm.sign["presto"], sf.W(e * (FareU + 4.3f), -PlatOut + 0.91f, ConcFloor + 1.5f), sf.r, 0.4f, 0.2f);
                SignQuad(signs, sm.sign["presto"], sf.W(e * (FareU + 5.5f), -PlatOut + 0.91f, ConcFloor + 1.5f), sf.r, 0.4f, 0.2f);
                // "Northbound - Finch" / "Southbound - VMC via Union" over each stair down, facing the fare line
                foreach (int side in new[] { -1, 1 })
                {
                    var c = sf.W(e * (StairTop + 1.4f), side * 5.45f, ConcCeil - 0.45f);
                    Cuboid(dark, sf, e * (StairTop + 1.36f), e * (StairTop + 1.44f), side * 3.7f, side * 7.2f, ConcCeil - 0.75f, ConcCeil - 0.15f);
                    SignQuad(signs, sm.sign[side > 0 ? "dir_nb" : "dir_sb"], c + sf.f * (0.05f * e), sf.f * e, 3.4f, 0.21f);
                }
                SignQuad(signs, sm.sign["exit"], sf.W(e * (FareU - 1.0f), 0f, ConcCeil - 0.4f), -sf.f * e, 0.9f, 0.45f);
                SignQuad(signs, sm.sign["washroom"], sf.W(e * (FareU - 8f), PlatOut - 0.01f, ConcFloor + 1.8f), -sf.r, 0.8f, 0.4f);
            }

            // PATH doors: shuttered (the PATH isn't built, 3N.9)
            if (lay.pathDoors != null)
                foreach (var d in lay.pathDoors)
                {
                    var n = sf.D(d.nU, d.nV);          // outward
                    var along = sf.D(d.nV, -d.nU);
                    var c = sf.W(d.u, d.v, ConcFloor) - n * 0.03f;
                    for (int k = 0; k < 13; k++)       // roll-up shutter slats
                    {
                        var p = c + Vector3.up * (0.1f + k * 0.2f);
                        var mb = k % 2 == 0 ? metal : dark;
                        mb.Quad(p - along * 1.2f, p + along * 1.2f, p + along * 1.2f + Vector3.up * 0.2f, p - along * 1.2f + Vector3.up * 0.2f, -n,
                                Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                    }
                    SignQuad(signs, sm.sign["path_closed"], c + Vector3.up * 2.8f - n * 0.01f, -n, 0.9f, 0.45f);
                }

            // ---- street stairs and the elevator
            var routes = new List<SubwayStation.Route>();
            foreach (var s in lay.streetStairs)
            {
                BuildStreetStair(root, sf, st.name, s, wall, stepsConc, metal, ramps, signs, sm, holes);
                AddRoutes(routes, sf, s, s.name, sf.W(s.footU - s.dirU * 7.15f, s.footV - s.dirV * 7.15f, 0.2f));
            }
            if (lay.elevators != null)
                foreach (var el in lay.elevators)
                {
                    var landing = BuildElevator(root, sf, el, glass, metal, dark, signs, sm);
                    AddRoutes(routes, sf, el, el.name + " (elevator)", landing);
                }
            comp.routes = routes.ToArray();
            comp.skippedEntrances = lay.skipped ?? new string[0];

            // ---- lights for the concourses and passages
            foreach (int e in new[] { -1, 1 })
                foreach (float u in new[] { 66f, 79f, 90f })
                    AddLight(root, "ConcourseLight", sf.W(e * u, 0f, ConcCeil - 0.3f), StationLumens, 10f);
            for (int i = 0; i + 3 < lay.corridors.Length; i += 4)
            {
                float u0 = lay.corridors[i], u1 = lay.corridors[i + 1], v0 = lay.corridors[i + 2], v1 = lay.corridors[i + 3];
                if (Mathf.Abs(v0) < 7.3f && Mathf.Abs(v1) < 7.3f && Mathf.Min(Mathf.Abs(u0), Mathf.Abs(u1)) > 60f) continue;   // the concourses themselves
                float len = Mathf.Max(u1 - u0, v1 - v0);
                int n = Mathf.Max(1, Mathf.RoundToInt(len / 14f));
                for (int k = 0; k < n; k++)
                {
                    float t = (k + 0.5f) / n;
                    bool alongU = u1 - u0 > v1 - v0;
                    var p = alongU ? sf.W(Mathf.Lerp(u0, u1, t), (v0 + v1) / 2, ConcCeil - 0.3f) : sf.W((u0 + u1) / 2, Mathf.Lerp(v0, v1, t), ConcCeil - 0.3f);
                    AddLight(root, "PassageLight", p, StationLumens * 0.6f, 8f);
                }
            }

            AddObject("StationWalls", root, wall.ToMesh($"{st.name}Walls"), sm.wall, meshes, collider: true);
            AddObject("StationFloor", root, floor.ToMesh($"{st.name}Floor"), sm.floor, meshes, collider: true);
            AddObject("StationCeiling", root, ceil.ToMesh($"{st.name}Ceiling"), sm.ceiling, meshes, collider: true);
            AddObject("StationConcrete", root, conc.ToMesh($"{st.name}Concrete"), sm.concrete, meshes, collider: true);
            AddObject("StationSteel", root, metal.ToMesh($"{st.name}Steel"), sm.metal, meshes, collider: true);
            AddObject("StationDark", root, dark.ToMesh($"{st.name}Dark"), sm.dark, meshes, collider: true);
            AddObject("StationRed", root, red.ToMesh($"{st.name}Red"), sm.orange, meshes, collider: false);
            AddObject("StationRubber", root, rubber.ToMesh($"{st.name}Rubber"), sm.rubber, meshes, collider: false);
            AddObject("StationTactile", root, tactile.ToMesh($"{st.name}Tactile"), sm.yellow, meshes, collider: false);
            AddObject("StationLightStrips", root, strip.ToMesh($"{st.name}Strips"), sm.strip, meshes, collider: false);
            AddObject("StationSigns", root, signs.ToMesh($"{st.name}Signs"), sm.signs, meshes, collider: false);
            AddObject("StationGlass", root, glass.ToMesh($"{st.name}Glass"), sm.glass, meshes, collider: true);
            AddCollider("StationRamps", root, ramps.ToMesh($"{st.name}Ramps"), meshes);
            AddObject("StationSteps", root, stepsTile.ToMesh($"{st.name}Steps"), sm.floor, meshes, collider: false);
            AddObject("StationStepsSteel", root, stepsMetal.ToMesh($"{st.name}StepsSteel"), sm.metal, meshes, collider: false);
            AddObject("StationStepsConcrete", root, stepsConc.ToMesh($"{st.name}StepsConcrete"), sm.concrete, meshes, collider: false);
            StationVolume(root, sf, lay);
            Debug.Log($"STATION_OK {st.name} at ({st.x:F0}, {st.z:F0}) streetStairs={lay.streetStairs.Length} elevators={lay.elevators?.Length ?? 0} pathDoors={lay.pathDoors?.Length ?? 0} routes={routes.Count}");
            return comp;
        }

        public const float TunnelTop = -5.4f;

        // route from a street entrance (or an elevator landing) to each platform: through the passage, the
        // fare line, and down the stair at that end
        static void AddRoutes(List<SubwayStation.Route> routes, SF sf, TtcStair s, string name, Vector3 start)
        {
            int e = s.end;
            foreach (int side in new[] { 1, -1 })
            {
                var pts = new List<Vector3> { start, sf.W(s.footU + s.dirU * 0.8f, s.footV + s.dirV * 0.8f, ConcFloor) };
                for (int i = 0; i + 1 < s.way.Length; i += 2) pts.Add(sf.W(s.way[i], s.way[i + 1], ConcFloor));
                float gateV = side * 4.8f;
                pts.Add(sf.W(e * (FareU + 1.6f), gateV, ConcFloor));
                pts.Add(sf.W(e * (FareU - 1.6f), gateV, ConcFloor));
                pts.Add(sf.W(e * (StairTop + 1.0f), side * 6.1f, ConcFloor));
                pts.Add(sf.W(e * (StairBot - 0.8f), side * 6.1f, PlatFloor));
                pts.Add(sf.W(e * 50f, side * 5.4f, PlatFloor));
                routes.Add(new SubwayStation.Route { name = $"{name} -> {(side > 0 ? "northbound" : "southbound")} platform", points = pts.ToArray() });
            }
        }

        // stair (outer 2.3 m) and up escalator (inner 1.2 m, by the platform edge) from the concourse at |u| 76
        // down to the platform at |u| 69
        static void BuildPlatformStair(Transform root, SF sf, int e, int side, MeshBuilder stepsTile, MeshBuilder stepsMetal, MeshBuilder metal, MeshBuilder rubber, MeshBuilder ramps)
        {
            const int risers = 25;
            float rise = (ConcFloor - PlatFloor) / risers, run = (StairTop - StairBot) / risers;
            float vEdge = side * PlatIn, vSplit = side * (PlatIn + 1.25f), vWall = side * PlatOut;
            for (int i = 0; i < risers; i++)
            {
                // step i: from u = 69 + i*run outward, top at platform + (i+1)*rise
                float ua = e * (StairBot + i * run), ub = e * (StairBot + (i + 1) * run);
                Cuboid(stepsTile, sf, ua, ub, vSplit + side * 0.05f, vWall, PlatFloor, PlatFloor + (i + 1) * rise);
                Cuboid(stepsMetal, sf, ua, ub, vEdge, vSplit - side * 0.05f, PlatFloor, PlatFloor + (i + 1) * rise);     // escalator steps
            }
            // balustrades (escalator / stair divider and the platform-edge side), rails at 0.95 m
            foreach (float v in new[] { vEdge, vSplit })
            {
                var a = sf.W(e * StairBot, v, PlatFloor + 0.95f); var b = sf.W(e * StairTop, v, ConcFloor + 0.95f);
                var a0 = sf.W(e * StairBot, v, PlatFloor); var b0 = sf.W(e * StairTop, v, ConcFloor);
                metal.Quad(a0, b0, b, a, sf.r * side, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                metal.Quad(a0, b0, b, a, -sf.r * side, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                var dv = sf.r * 0.04f;   // black rubber handrail along the top
                rubber.Quad(a - dv, b - dv, b + dv + Vector3.up * 0.04f, a + dv + Vector3.up * 0.04f, Vector3.up, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            }
            // walking surfaces: one ramp per lane
            foreach (var (v0, v1) in new[] { (vEdge, vSplit), (vSplit, vWall) })
            {
                var p0 = sf.W(e * (StairBot - 0.05f), v0, PlatFloor); var p1 = sf.W(e * (StairBot - 0.05f), v1, PlatFloor);
                var p2 = sf.W(e * (StairTop + 0.05f), v1, ConcFloor); var p3 = sf.W(e * (StairTop + 0.05f), v0, ConcFloor);
                ramps.Quad(p0, p1, p2, p3, Vector3.up, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            }
            AddLight(root, "StairWellLight", sf.W(e * (StairBot + 3.5f), side * 5.4f, ConcFloor - 0.5f), StationLumens * 0.7f, 8f);
            // the escalator carries you up at 0.5 m/s
            var esc = new GameObject("Escalator");
            esc.transform.SetParent(root, false);
            var bottom = sf.W(e * StairBot, (vEdge + vSplit) / 2, PlatFloor); var top = sf.W(e * StairTop, (vEdge + vSplit) / 2, ConcFloor);
            esc.transform.SetPositionAndRotation((bottom + top) / 2 + Vector3.up * 1.0f, Quaternion.LookRotation(sf.f * e, Vector3.up));
            var comp = esc.AddComponent<Escalator>();
            comp.size = new Vector3(1.25f, (ConcFloor - PlatFloor) + 2.2f, StairTop - StairBot);
            comp.velocity = (top - bottom).normalized * 0.5f;
        }

        // a sidewalk stair: 0.9 m landing at street level, 24 risers down to the concourse, walls rising 1.05 m
        // above the pavement on three sides, a red TTC sign on a post at the open end
        static void BuildStreetStair(Transform root, SF sf, string station, TtcStair s, MeshBuilder wall, MeshBuilder conc, MeshBuilder metal,
                                     MeshBuilder ramps, MeshBuilder signs, SubwayMats sm, List<Vector2[]> holes)
        {
            const int risers = 24; const float landing = 0.9f, len = 7.6f, wallT = 0.2f, rail = 1.05f;
            float du = s.dirU, dv = s.dirV;
            float tu = s.footU - du * len, tv = s.footV - dv * len;           // top end (street side)
            float cu = du != 0 ? 0f : 1f, cv = du != 0 ? 1f : 0f;               // across the stair
            float half = (du != 0 ? (s.rect[3] - s.rect[2]) : (s.rect[1] - s.rect[0])) / 2;
            float mu = (s.rect[0] + s.rect[1]) / 2, mv = (s.rect[2] + s.rect[3]) / 2;
            // P(a, c, y): a = metres from the top end toward the foot, c = metres across from the centre line
            Vector3 P(float a, float c, float y) => sf.W(tu + du * a + cu * c + (du != 0 ? 0 : mu - tu), tv + dv * a + cv * c + (dv != 0 ? 0 : mv - tv), y);
            float rise = -ConcFloor / risers, go = (len - landing) / risers, inner = half - wallT;
            void Block(MeshBuilder mb, float a0, float a1, float c0, float c1, float y0, float y1)
            {
                // a box in (a, c, y); convert corners through P
                var v = new[] { P(a0, c0, y0), P(a1, c0, y0), P(a1, c1, y0), P(a0, c1, y0), P(a0, c0, y1), P(a1, c0, y1), P(a1, c1, y1), P(a0, c1, y1) };
                var dirA = (P(1, 0, 0) - P(0, 0, 0)).normalized; var dirC = (P(0, 1, 0) - P(0, 0, 0)).normalized;
                mb.Quad(v[0], v[1], v[2], v[3], Vector3.down, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                mb.Quad(v[4], v[5], v[6], v[7], Vector3.up, new Vector2(a0, c0), new Vector2(a1, c0), new Vector2(a1, c1), new Vector2(a0, c1));
                mb.Quad(v[0], v[1], v[5], v[4], -dirC, new Vector2(a0, y0), new Vector2(a1, y0), new Vector2(a1, y1), new Vector2(a0, y1));
                mb.Quad(v[3], v[2], v[6], v[7], dirC, new Vector2(a0, y0), new Vector2(a1, y0), new Vector2(a1, y1), new Vector2(a0, y1));
                mb.Quad(v[0], v[3], v[7], v[4], -dirA, new Vector2(c0, y0), new Vector2(c1, y0), new Vector2(c1, y1), new Vector2(c0, y1));
                mb.Quad(v[1], v[2], v[6], v[5], dirA, new Vector2(c0, y0), new Vector2(c1, y0), new Vector2(c1, y1), new Vector2(c0, y1));
            }
            Block(conc, 0f, landing, -inner, inner, -0.6f, 0f);                                     // landing
            for (int i = 0; i < risers; i++)
                Block(conc, landing + i * go, landing + (i + 1) * go, -inner, inner, ConcFloor - 0.3f, -(i + 1) * rise);
            Block(wall, 0f, len, -half, -inner, ConcFloor - 0.3f, rail);                            // side walls
            Block(wall, 0f, len, inner, half, ConcFloor - 0.3f, rail);
            Block(wall, len - wallT, len, -inner, inner, ConcCeil, rail);                           // foot end: header + parapet
            Block(metal, 0f, len, -half - 0.02f, -half + 0.08f, rail, rail + 0.06f);              // cap rails
            Block(metal, 0f, len, half - 0.08f, half + 0.02f, rail, rail + 0.06f);
            Block(metal, len - wallT - 0.02f, len + 0.02f, -half, half, rail, rail + 0.06f);
            for (int k = 0; k < 2; k++)                                                          // handrails down the steps
            {
                float c = k == 0 ? -inner + 0.05f : inner - 0.05f;
                var a0 = P(landing, c, 0.9f); var a1 = P(len - wallT, c, ConcFloor + 0.9f);
                var dc = (P(0, 1, 0) - P(0, 0, 0)).normalized * 0.03f;
                metal.Quad(a0 - dc, a1 - dc, a1 + dc, a0 + dc, Vector3.up, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            }
            // walking surface: landing, then one ramp to the foot
            ramps.Quad(P(-0.05f, -inner, 0f), P(-0.05f, inner, 0f), P(landing, inner, 0f), P(landing, -inner, 0f), Vector3.up, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            ramps.Quad(P(landing, -inner, 0f), P(landing, inner, 0f), P(len + 0.05f, inner, ConcFloor), P(len + 0.05f, -inner, ConcFloor), Vector3.up, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            // lights down the stair (dimmer than the station: they're seen from the dark street)
            AddLight(root, "StairLight", P(3.2f, 0f, -0.2f), 70f, 6f);
            AddLight(root, "StairLight", P(6.4f, 0f, -2.4f), 140f, 6f);
            // red TTC sign on a post beside the open end
            var post = P(-0.35f, half + 0.25f, 0f);
            Cuboid(metal, new SF(post.x, post.z, sf.f.x, sf.f.z), -0.05f, 0.05f, -0.05f, 0.05f, 0f, 3.3f);
            var facing = (P(0, 0, 0) - P(1, 0, 0)).normalized;      // toward the street
            var signC = post + Vector3.up * 2.75f;
            SignQuad(signs, sm.sign[$"street_{station}"], signC + facing * 0.06f, facing, 0.5f, 0.75f);
            SignQuad(signs, sm.sign[$"street_{station}"], signC - facing * 0.06f, -facing, 0.5f, 0.75f);
            // the hole in the pavement: the whole opening, walls included
            holes.Add(new[] { Flat(sf.W(s.rect[0], s.rect[2], 0)), Flat(sf.W(s.rect[1], s.rect[2], 0)), Flat(sf.W(s.rect[1], s.rect[3], 0)), Flat(sf.W(s.rect[0], s.rect[3], 0)) });
        }

        static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

        // a glass kiosk on the street and its landing below; E rides it (the condo's DoorLink fade)
        static Vector3 BuildElevator(Transform root, SF sf, TtcStair el, MeshBuilder glass, MeshBuilder metal, MeshBuilder dark, MeshBuilder signs, SubwayMats sm)
        {
            float mu = (el.rect[0] + el.rect[1]) / 2, mv = (el.rect[2] + el.rect[3]) / 2, h = (el.rect[1] - el.rect[0]) / 2;
            Cuboid(glass, sf, mu - h, mu + h, mv - h, mv + h, 0.05f, 2.7f);
            Cuboid(metal, sf, mu - h - 0.05f, mu + h + 0.05f, mv - h - 0.05f, mv + h + 0.05f, 2.7f, 2.95f);
            foreach (var (a, b) in new[] { (-1, -1), (-1, 1), (1, -1), (1, 1) })
                Cuboid(metal, sf, mu + a * h - 0.06f, mu + a * h + 0.06f, mv + b * h - 0.06f, mv + b * h + 0.06f, 0f, 2.7f);
            Cuboid(dark, sf, mu - 0.6f, mu + 0.6f, mv - 0.6f, mv + 0.6f, 0.05f, 2.4f);        // the cab inside
            // the street door faces away from the passage; below, doors in the wall opposite the corridor
            var dir = sf.D(el.dirU, el.dirV);
            var streetDoor = sf.W(mu, mv, 0f) - dir * (h + 0.9f);
            SignQuad(signs, sm.sign["elevator"], sf.W(mu, mv, 2.82f) - dir * (h + 0.06f), -dir, 0.6f, 0.2f);
            var landing = sf.W(el.footU, el.footV, ConcFloor) + dir * 1.2f;
            var shaftDoor = sf.W(mu, mv, ConcFloor) - dir * (h - 0.02f);
            var across = Vector3.Cross(Vector3.up, dir).normalized * 0.6f;
            metal.Quad(shaftDoor - across, shaftDoor + across, shaftDoor + across + Vector3.up * 2.2f, shaftDoor - across + Vector3.up * 2.2f, dir,
                       Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            SignQuad(signs, sm.sign["elevator"], shaftDoor + Vector3.up * 2.45f + dir * 0.06f, dir, 0.6f, 0.2f);

            var up = new GameObject("SubwayElevator_Street").transform; up.SetParent(root, false);
            up.SetPositionAndRotation(streetDoor, Quaternion.LookRotation(-dir));
            var down = new GameObject("SubwayElevator_Concourse").transform; down.SetParent(root, false);
            down.SetPositionAndRotation(landing, Quaternion.LookRotation(dir));
            var a1 = up.gameObject.AddComponent<LifeInDowntown.World.DoorLink>(); a1.prompt = "Take the elevator down"; a1.exit = down; a1.radius = 2.2f;
            var a2 = down.gameObject.AddComponent<LifeInDowntown.World.DoorLink>(); a2.prompt = "Take the elevator up"; a2.exit = up; a2.radius = 2.2f;
            return landing + Vector3.up * 0.1f;
        }

        // no rain, no city fog, a brighter fixed exposure: one local volume over the station and its passages
        static void StationVolume(Transform root, SF sf, TtcStationLayout lay)
        {
            var go = new GameObject("StationVolume");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(sf.c, Quaternion.LookRotation(sf.f));
            void Box(float u0, float u1, float v0, float v1, float y0, float y1)
            {
                var b = go.AddComponent<BoxCollider>(); b.isTrigger = true;
                b.center = new Vector3((v0 + v1) / 2, (y0 + y1) / 2, (u0 + u1) / 2);   // local x = right (v), z = forward (u)
                b.size = new Vector3(Mathf.Abs(v1 - v0), Mathf.Abs(y1 - y0), Mathf.Abs(u1 - u0));
            }
            Box(-HalfPlat - 25f, HalfPlat + 25f, -PlatOut, PlatOut, TrackBed - 0.5f, ConcCeil);
            for (int i = 0; i + 3 < lay.corridors.Length; i += 4)
                Box(lay.corridors[i], lay.corridors[i + 1], lay.corridors[i + 2], lay.corridors[i + 3], ConcFloor - 0.2f, ConcCeil + 0.2f);
            foreach (var s in lay.streetStairs) Box(s.rect[0], s.rect[1], s.rect[2], s.rect[3], ConcFloor - 0.2f, -0.8f);
            var vol = go.AddComponent<Volume>(); vol.isGlobal = false; vol.priority = 40f; vol.blendDistance = 1.5f;
            vol.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(SubwayProfile());
        }

        public const float SubwayExposureEV = 6.0f;

        static string SubwayProfile()
        {
            const string path = "Assets/City/Look/SubwayInterior.asset";
            var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (p == null)
            {
                p = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(p, path);
                var f = p.Add<Fog>(true); f.name = "Fog"; f.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(f, p);
                var x = p.Add<Exposure>(true); x.name = "Exposure"; x.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(x, p);
            }
            p.TryGet<Fog>(out var fog); p.TryGet<Exposure>(out var exp);
            fog.enabled.Override(false);
            exp.mode.Override(ExposureMode.Fixed);
            exp.fixedExposure.Override(SubwayExposureEV);
            EditorUtility.SetDirty(fog); EditorUtility.SetDirty(exp); EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
            return path;
        }

        // ------------------------------------------------------------------ concourse level
        // The planned rectangles (concourses + passages) painted on a 10 cm grid in the station frame, so
        // overlapping passages simply merge. Cell types: 1 floor, 2 stair well (hole down to the platform),
        // 3 street stair (open: the stair builds its own walls).
        class ConcourseGrid
        {
            readonly float u0, v0; readonly int nu, nv; readonly byte[] c;

            public ConcourseGrid(TtcStationLayout lay)
            {
                float umin = 1e9f, umax = -1e9f, vmin = 1e9f, vmax = -1e9f;
                void Grow(float a0, float a1, float b0, float b1) { umin = Mathf.Min(umin, a0); umax = Mathf.Max(umax, a1); vmin = Mathf.Min(vmin, b0); vmax = Mathf.Max(vmax, b1); }
                for (int i = 0; i + 3 < lay.corridors.Length; i += 4) Grow(lay.corridors[i], lay.corridors[i + 1], lay.corridors[i + 2], lay.corridors[i + 3]);
                foreach (var s in lay.streetStairs) Grow(s.rect[0], s.rect[1], s.rect[2], s.rect[3]);
                u0 = umin - 1f; v0 = vmin - 1f;
                nu = Mathf.CeilToInt((umax - umin + 2f) / Cell); nv = Mathf.CeilToInt((vmax - vmin + 2f) / Cell);
                c = new byte[nu * nv];
                for (int i = 0; i + 3 < lay.corridors.Length; i += 4) Paint(lay.corridors[i], lay.corridors[i + 1], lay.corridors[i + 2], lay.corridors[i + 3], 1);
                if (lay.elevators != null) foreach (var s in lay.elevators) Paint(s.rect[0], s.rect[1], s.rect[2], s.rect[3], 1);
                for (int i = 0; i + 3 < lay.holes.Length; i += 4) Paint(lay.holes[i], lay.holes[i + 1], lay.holes[i + 2], lay.holes[i + 3], 2);
                foreach (var s in lay.streetStairs) Paint(s.rect[0], s.rect[1], s.rect[2], s.rect[3], 3);
            }

            void Paint(float a0, float a1, float b0, float b1, byte t)
            {
                int i0 = Mathf.RoundToInt((a0 - u0) / Cell), i1 = Mathf.RoundToInt((a1 - u0) / Cell);
                int j0 = Mathf.RoundToInt((b0 - v0) / Cell), j1 = Mathf.RoundToInt((b1 - v0) / Cell);
                for (int i = Mathf.Max(0, i0); i < Mathf.Min(nu, i1); i++)
                    for (int j = Mathf.Max(0, j0); j < Mathf.Min(nv, j1); j++) c[i * nv + j] = t;
            }

            byte At(int i, int j) => i < 0 || j < 0 || i >= nu || j >= nv ? (byte)0 : c[i * nv + j];
            float U(int i) => u0 + i * Cell;
            float V(int j) => v0 + j * Cell;

            // greedy rectangles over cells matching `want`
            List<(int, int, int, int)> Rects(Func<byte, bool> want)
            {
                var used = new bool[nu * nv];
                var list = new List<(int, int, int, int)>();
                for (int i = 0; i < nu; i++)
                    for (int j = 0; j < nv; j++)
                    {
                        if (used[i * nv + j] || !want(c[i * nv + j])) continue;
                        int j1 = j;
                        while (j1 + 1 < nv && !used[i * nv + j1 + 1] && want(c[i * nv + j1 + 1])) j1++;
                        int i1 = i;
                        while (i1 + 1 < nu)
                        {
                            bool ok = true;
                            for (int jj = j; jj <= j1 && ok; jj++) ok = !used[(i1 + 1) * nv + jj] && want(c[(i1 + 1) * nv + jj]);
                            if (!ok) break;
                            i1++;
                        }
                        for (int ii = i; ii <= i1; ii++) for (int jj = j; jj <= j1; jj++) used[ii * nv + jj] = true;
                        list.Add((i, i1 + 1, j, j1 + 1));
                    }
                return list;
            }

            public void Build(SF sf, MeshBuilder floor, MeshBuilder ceil, MeshBuilder wall, MeshBuilder metal, MeshBuilder glass, MeshBuilder strip, Transform root)
            {
                foreach (var (i0, i1, j0, j1) in Rects(t => t == 1))
                    FaceY(floor, sf, ConcFloor, U(i0), U(i1), V(j0), V(j1), 1);
                foreach (var (i0, i1, j0, j1) in Rects(t => t == 1 || t == 2))
                {
                    FaceY(ceil, sf, ConcCeil, U(i0), U(i1), V(j0), V(j1), -1);
                    // light strips down the middle of every ceiling rectangle wider than a passage edge
                    float um = (U(i0) + U(i1)) / 2, vm = (V(j0) + V(j1)) / 2;
                    if (U(i1) - U(i0) >= V(j1) - V(j0))
                        for (float u = U(i0) + 0.8f; u + 1.4f < U(i1); u += 3f) Cuboid(strip, sf, u, u + 1.2f, vm - 0.06f, vm + 0.06f, ConcCeil - 0.05f, ConcCeil - 0.01f);
                    else
                        for (float v = V(j0) + 0.8f; v + 1.4f < V(j1); v += 3f) Cuboid(strip, sf, um - 0.06f, um + 0.06f, v, v + 1.2f, ConcCeil - 0.05f, ConcCeil - 0.01f);
                }
                // walls where floor / stair well meets solid ground; railings where floor meets a stair well,
                // except along the stair-top edge (|u| = 76)
                for (int pass = 0; pass < 2; pass++)
                {
                    bool alongJ = pass == 0;      // edges between (i, j) and (i, j+1): planes v = const
                    int outer = alongJ ? nv + 1 : nu + 1, innerN = alongJ ? nu : nv;
                    for (int k = 0; k < outer; k++)
                    {
                        int runStart = -1; int runKind = 0;
                        for (int m = 0; m <= innerN; m++)
                        {
                            int kind = 0;
                            if (m < innerN)
                            {
                                byte a = alongJ ? At(m, k - 1) : At(k - 1, m), b = alongJ ? At(m, k) : At(k, m);
                                kind = EdgeKind(a, b, alongJ, k);
                            }
                            if (kind != runKind)
                            {
                                if (runKind != 0) EmitEdge(sf, wall, metal, glass, alongJ, k, runStart, m, runKind);
                                runStart = m; runKind = kind;
                            }
                        }
                    }
                }
            }

            // +1/-1: wall facing +/-;  +2/-2: railing (floor side +/-);  0 none
            int EdgeKind(byte a, byte b, bool alongJ, int k)
            {
                bool aOpen = a == 1 || a == 2, bOpen = b == 1 || b == 2;
                if (aOpen && b == 0) return -1;    // wall faces back toward a (negative side)
                if (bOpen && a == 0) return 1;
                if ((a == 1 && b == 2) || (a == 2 && b == 1))
                {
                    if (!alongJ)
                    {
                        float u = U(k);
                        if (Mathf.Abs(Mathf.Abs(u) - StairTop) < 0.15f) return 0;    // stair top: open
                    }
                    return a == 1 ? 2 : -2;     // the slab edge faces the well
                }
                return 0;
            }

            void EmitEdge(SF sf, MeshBuilder wall, MeshBuilder metal, MeshBuilder glass, bool alongJ, int k, int m0, int m1, int kind)
            {
                int n = kind > 0 ? 1 : -1;
                if (Mathf.Abs(kind) == 1)
                {
                    if (alongJ) FaceV(wall, sf, V(k), U(m0), U(m1), PlatCeil, ConcCeil, n);
                    else FaceU(wall, sf, U(k), V(m0), V(m1), PlatCeil, ConcCeil, n);
                    return;
                }
                // railing: glass panel, steel cap rail, and the slab edge below it facing into the well
                if (alongJ)
                {
                    Cuboid(glass, sf, U(m0), U(m1), V(k) - 0.01f, V(k) + 0.01f, ConcFloor, ConcFloor + 1.05f);
                    Cuboid(metal, sf, U(m0), U(m1), V(k) - 0.03f, V(k) + 0.03f, ConcFloor + 1.05f, ConcFloor + 1.1f);
                    FaceV(wall, sf, V(k), U(m0), U(m1), PlatCeil, ConcFloor, n);
                }
                else
                {
                    Cuboid(glass, sf, U(k) - 0.01f, U(k) + 0.01f, V(m0), V(m1), ConcFloor, ConcFloor + 1.05f);
                    Cuboid(metal, sf, U(k) - 0.03f, U(k) + 0.03f, V(m0), V(m1), ConcFloor + 1.05f, ConcFloor + 1.1f);
                    FaceU(wall, sf, U(k), V(m0), V(m1), PlatCeil, ConcFloor, n);
                }
            }
        }

        // ------------------------------------------------------------------ holes in the pavement
        // Cuts each street-stair opening out of every flat ground-level mesh (ground, roads, sidewalks, paths,
        // plazas, road paint), triangle by triangle: a triangle minus a convex quad = up to 4 convex pieces.
        static void CutGroundHoles(Transform city, List<Vector2[]> holes)
        {
            if (holes.Count == 0) return;
            var boxes = holes.ConvertAll(h => { var b = new Rect(h[0], Vector2.zero); foreach (var p in h) { b.xMin = Mathf.Min(b.xMin, p.x); b.xMax = Mathf.Max(b.xMax, p.x); b.yMin = Mathf.Min(b.yMin, p.y); b.yMax = Mathf.Max(b.yMax, p.y); } return b; });
            for (int i = 0; i < holes.Count; i++)   // counter-clockwise in x/z
            {
                var h = holes[i];
                float area = 0f; for (int k = 0; k < h.Length; k++) { var a = h[k]; var b = h[(k + 1) % h.Length]; area += a.x * b.y - b.x * a.y; }
                if (area < 0f) Array.Reverse(h);
            }
            int cutMeshes = 0, removed = 0;
            foreach (var mf in city.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.transform.IsChildOf(city.Find("Subway"))) continue;
                var r = mf.GetComponent<Renderer>();
                if (r == null || mf.sharedMesh == null) continue;
                var bb = r.bounds;
                // small props standing in an opening (bins, poles): remove them
                if (bb.size.x < 3f && bb.size.z < 3f && bb.min.y > -0.5f)
                {
                    var c = new Vector2(bb.center.x, bb.center.z);
                    bool inside = false;
                    for (int i = 0; i < holes.Count && !inside; i++) inside = boxes[i].Contains(c) && InConvex(c, holes[i]);
                    if (inside) { UnityEngine.Object.DestroyImmediate(mf.gameObject); removed++; continue; }
                }
                if (bb.max.y > 1.0f) continue;
                bool any = false;
                foreach (var b in boxes) if (b.Overlaps(Rect.MinMaxRect(bb.min.x, bb.min.z, bb.max.x, bb.max.z))) { any = true; break; }
                if (!any) continue;
                if (CutMesh(mf.sharedMesh, holes, boxes))
                {
                    cutMeshes++;
                    var mc = mf.GetComponent<MeshCollider>();
                    if (mc != null) { mc.sharedMesh = null; mc.sharedMesh = mf.sharedMesh; }
                }
            }
            Debug.Log($"SUBWAY_HOLES openings={holes.Count} meshesCut={cutMeshes} propsRemoved={removed}");
        }

        static bool InConvex(Vector2 p, Vector2[] h)
        {
            for (int k = 0; k < h.Length; k++)
            {
                var a = h[k]; var b = h[(k + 1) % h.Length];
                if ((b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x) < 0f) return false;
            }
            return true;
        }

        struct CV { public Vector3 p, n; public Vector2 uv; }

        static CV Lerp(CV a, CV b, float t) => new CV { p = Vector3.Lerp(a.p, b.p, t), n = Vector3.Lerp(a.n, b.n, t), uv = Vector2.Lerp(a.uv, b.uv, t) };

        // keep the part of poly on the left of a->b (keepLeft) or the right
        static List<CV> ClipHalf(List<CV> poly, Vector2 a, Vector2 b, bool keepLeft)
        {
            var outp = new List<CV>();
            float Side(CV v) { float s = (b.x - a.x) * (v.p.z - a.y) - (b.y - a.y) * (v.p.x - a.x); return keepLeft ? s : -s; }
            for (int i = 0; i < poly.Count; i++)
            {
                var cur = poly[i]; var nxt = poly[(i + 1) % poly.Count];
                float sc = Side(cur), sn = Side(nxt);
                if (sc >= 0f) outp.Add(cur);
                if ((sc >= 0f) != (sn >= 0f)) outp.Add(Lerp(cur, nxt, sc / (sc - sn)));
            }
            return outp;
        }

        static float AreaXZ(List<CV> p)
        {
            float a = 0f;
            for (int i = 0; i < p.Count; i++) { var u = p[i].p; var w = p[(i + 1) % p.Count].p; a += u.x * w.z - w.x * u.z; }
            return Mathf.Abs(a) * 0.5f;
        }

        static bool CutMesh(Mesh m, List<Vector2[]> holes, List<Rect> boxes)
        {
            var verts = m.vertices; var norms = m.normals; var uvs = m.uv;
            if (norms.Length != verts.Length || uvs.Length != verts.Length) return false;
            var nv = new List<Vector3>(verts); var nn = new List<Vector3>(norms); var nu = new List<Vector2>(uvs);
            bool changed = false;
            var subs = new List<int>[m.subMeshCount];
            for (int sIdx = 0; sIdx < m.subMeshCount; sIdx++)
            {
                var tris = m.GetTriangles(sIdx);
                var outT = new List<int>(tris.Length);
                for (int t = 0; t < tris.Length; t += 3)
                {
                    int ia = tris[t], ib = tris[t + 1], ic = tris[t + 2];
                    Vector3 A = verts[ia], B = verts[ib], C = verts[ic];
                    var tb = Rect.MinMaxRect(Mathf.Min(A.x, B.x, C.x), Mathf.Min(A.z, B.z, C.z), Mathf.Max(A.x, B.x, C.x), Mathf.Max(A.z, B.z, C.z));
                    bool hit = false;
                    for (int h = 0; h < boxes.Count && !hit; h++) hit = boxes[h].Overlaps(tb);
                    if (!hit) { outT.Add(ia); outT.Add(ib); outT.Add(ic); continue; }
                    changed = true;
                    var tri = new List<CV> { new CV { p = A, n = norms[ia], uv = uvs[ia] }, new CV { p = B, n = norms[ib], uv = uvs[ib] }, new CV { p = C, n = norms[ic], uv = uvs[ic] } };
                    // vertical faces (kerbs): keep or drop whole
                    if (AreaXZ(tri) < 1e-4f)
                    {
                        var cen = new Vector2((A.x + B.x + C.x) / 3f, (A.z + B.z + C.z) / 3f);
                        bool inside = false;
                        for (int h = 0; h < holes.Count && !inside; h++) inside = InConvex(cen, holes[h]);
                        if (!inside) { outT.Add(ia); outT.Add(ib); outT.Add(ic); }
                        continue;
                    }
                    bool flip = Vector3.Cross(B - A, C - A).y < 0f;   // remember the winding to keep facing
                    var pieces = new List<List<CV>> { tri };
                    for (int h = 0; h < holes.Count; h++)
                    {
                        if (!boxes[h].Overlaps(tb)) continue;
                        var next = new List<List<CV>>();
                        foreach (var piece in pieces)
                        {
                            var rem = piece;
                            var hole = holes[h];
                            for (int k = 0; k < hole.Length && rem.Count >= 3; k++)
                            {
                                var a = hole[k]; var b = hole[(k + 1) % hole.Length];
                                var outside = ClipHalf(rem, a, b, keepLeft: false);
                                if (outside.Count >= 3 && AreaXZ(outside) > 1e-5f) next.Add(outside);
                                rem = ClipHalf(rem, a, b, keepLeft: true);
                            }
                        }
                        pieces = next;
                    }
                    foreach (var piece in pieces)
                    {
                        int baseI = nv.Count;
                        foreach (var v in piece) { nv.Add(v.p); nn.Add(v.n.normalized); nu.Add(v.uv); }
                        for (int k = 1; k + 1 < piece.Count; k++)
                        {
                            int i0 = baseI, i1 = baseI + k, i2 = baseI + k + 1;
                            bool pieceFlip = Vector3.Cross(nv[i1] - nv[i0], nv[i2] - nv[i0]).y < 0f;
                            if (pieceFlip != flip) (i1, i2) = (i2, i1);
                            outT.Add(i0); outT.Add(i1); outT.Add(i2);
                        }
                    }
                }
                subs[sIdx] = outT;
            }
            if (!changed) return false;
            m.SetVertices(nv); m.SetNormals(nn); m.SetUVs(0, nu);
            for (int sIdx = 0; sIdx < subs.Length; sIdx++) m.SetTriangles(subs[sIdx], sIdx);
            m.RecalculateBounds(); m.RecalculateTangents();
            return true;
        }
    }
}
