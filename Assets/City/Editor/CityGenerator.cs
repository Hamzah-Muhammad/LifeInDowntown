using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LifeInDowntown.City
{
    // Builds the Downtown_YongeDundas scene's City root from the OSM-derived JSON
    // (docs/DESIGN.md 3.2). Everything under City is regenerated; never hand-edit it.
    public static class CityGenerator
    {
        const string DataPath = "Assets/City/Data/downtown_yonge_dundas.json";
        const string ScenePath = "Assets/Scenes/Downtown_YongeDundas.unity";
        const string TemplateScenePath = "Assets/OutdoorsScene.unity";
        const string MeshAssetPath = "Assets/City/Generated/CityMeshes.asset";
        const string MaterialDir = "Assets/City/Materials";

        const float SidewalkWidth = 3.0f;
        const float SidewalkHeight = 0.15f;
        const float SidewalkPiece = 2.0f;

        [MenuItem("Life In Downtown/Generate City")]
        public static void Generate()
        {
            var data = JsonUtility.FromJson<CityData>(File.ReadAllText(DataPath));
            var scene = OpenOrCreateScene();

            var old = GameObject.Find("City");
            if (old != null) Object.DestroyImmediate(old);
            if (AssetDatabase.LoadAssetAtPath<Object>(MeshAssetPath) != null)
                AssetDatabase.DeleteAsset(MeshAssetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(MeshAssetPath));

            var mats = new Materials();
            var root = new GameObject("City");
            var meshes = new List<Mesh>();

            BuildGround(data, root.transform, mats, meshes);
            int built = BuildBuildings(data, root.transform, mats, meshes);
            BuildRoads(data, root.transform, mats, meshes, out int sidewalkPieces);
            BuildPaths(data, root.transform, mats, meshes);
            int lights = BuildStreetlights(data, root.transform, mats, meshes);
            int signs = BuildSigns(root.transform, mats, meshes);
            int puddles = RainLook.BuildPuddles(data, root.transform);
            RainLook.BuildRain();
            RainLook.EnableRayTracing();

            // meshes live in one asset file so the scene file stays small
            AssetDatabase.CreateAsset(meshes[0], MeshAssetPath);
            for (int i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], MeshAssetPath);
            AssetDatabase.SaveAssets();

            PlaceMainCamera();
            NightLook.Apply();
            LifeInDowntown.EditorTools.PlayerSetup.Setup(data);
            LifeInDowntown.EditorTools.NpcSetup.Setup(data);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"CITY_OK buildings={built} roads={data.roads.Length} paths={data.paths.Length} " +
                      $"plazas={data.plazas.Length} sidewalkPieces={sidewalkPieces} streetlights={lights} signs={signs} puddles={puddles} meshes={meshes.Count}");
        }

        public static void GenerateFromCommandLine() => Generate();

        static UnityEngine.SceneManagement.Scene OpenOrCreateScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                AssetDatabase.CopyAsset(TemplateScenePath, ScenePath);
            }
            return EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        // ---------------------------------------------------------------- ground
        static void BuildGround(CityData d, Transform parent, Materials mats, List<Mesh> meshes)
        {
            const float pad = 60f;
            var b = new MeshBuilder();
            var ring = new List<Vector2>
            {
                new Vector2(d.bounds.xmin - pad, d.bounds.zmin - pad), new Vector2(d.bounds.xmax + pad, d.bounds.zmin - pad),
                new Vector2(d.bounds.xmax + pad, d.bounds.zmax + pad), new Vector2(d.bounds.xmin - pad, d.bounds.zmax + pad),
            };
            b.Polygon(ring, 0f, true);
            AddObject("Ground", parent, b.ToMesh("Ground"), mats.Ground, meshes, collider: true);
        }

        // ------------------------------------------------------------- buildings
        static int BuildBuildings(CityData d, Transform parent, Materials mats, List<Mesh> meshes)
        {
            var group = new GameObject("Buildings").transform;
            group.SetParent(parent, false);
            int count = 0;
            foreach (var bd in d.buildings)
            {
                var ring = CityDataUtil.Points(bd.pts);
                if (ring.Count < 3 || bd.h - bd.minH < 0.3f) continue;
                if (Triangulator.SignedArea(ring) < 0f) ring.Reverse();   // CCW seen from above

                var mb = new MeshBuilder();
                float y0 = bd.minH, y1 = bd.h;
                for (int i = 0; i < ring.Count; i++)
                {
                    Vector2 p = ring[i], q = ring[(i + 1) % ring.Count];
                    Vector2 e = q - p;
                    float len = e.magnitude;
                    if (len < 0.01f) continue;
                    var outward = new Vector3(e.y, 0f, -e.x);   // CCW ring: right-hand side is outside
                    mb.Quad(new Vector3(p.x, y0, p.y), new Vector3(q.x, y0, q.y),
                            new Vector3(q.x, y1, q.y), new Vector3(p.x, y1, p.y), outward,
                            new Vector2(0, y0), new Vector2(len, y0), new Vector2(len, y1), new Vector2(0, y1));
                }
                mb.UseSubmesh(1);                               // roof and underside: roof material
                mb.Polygon(ring, y1, true);
                if (y0 > 0.01f) mb.Polygon(ring, y0, false);   // underside of podium overhangs

                string label = string.IsNullOrEmpty(bd.name) ? bd.id.ToString() : bd.name;
                AddObject($"Bldg_{label}", group, mb.ToMesh($"Bldg_{bd.id}"),
                          new[] { mats.Facade(bd.h, bd.id), mats.Roof }, meshes, collider: true);
                count++;
            }
            return count;
        }

        // ------------------------------------------------- roads and sidewalks
        static float RoadY(string type) => type switch
        {
            "primary" => 0.06f, "secondary" => 0.05f, "tertiary" => 0.04f, _ => 0.03f,
        };

        static void BuildRoads(CityData d, Transform parent, Materials mats, List<Mesh> meshes, out int pieces)
        {
            var roadMb = new MeshBuilder();
            foreach (var r in d.roads)
                Ribbon(roadMb, CityDataUtil.Points(r.pts), r.width, RoadY(r.type));

            // sidewalks: short raised slabs along both sides, dropped wherever they would sit on another road
            var segs = new List<(Vector2 a, Vector2 b, float halfW, long id)>();
            foreach (var r in d.roads)
            {
                var p = CityDataUtil.Points(r.pts);
                for (int i = 0; i + 1 < p.Count; i++) segs.Add((p[i], p[i + 1], r.width * 0.5f, r.id));
            }

            var swMb = new MeshBuilder();
            pieces = 0;
            foreach (var r in d.roads)
            {
                var p = CityDataUtil.Points(r.pts);
                float off = r.width * 0.5f + SidewalkWidth * 0.5f;
                for (int i = 0; i + 1 < p.Count; i++)
                {
                    Vector2 a = p[i], b = p[i + 1];
                    float len = (b - a).magnitude;
                    if (len < 0.5f) continue;
                    Vector2 dir = (b - a) / len, left = new Vector2(-dir.y, dir.x);
                    int n = Mathf.Max(1, Mathf.CeilToInt(len / SidewalkPiece));
                    for (int k = 0; k < n; k++)
                    {
                        Vector2 s = a + dir * (len * k / n), e = a + dir * (len * (k + 1) / n);
                        foreach (int side in new[] { 1, -1 })
                        {
                            Vector2 o = left * (off * side);
                            Vector2 centre = (s + e) * 0.5f + o;
                            if (OnAnyRoad(centre, segs, r.id, SidewalkWidth * 0.5f)) continue;
                            Slab(swMb, s + o, e + o, left, SidewalkWidth, SidewalkHeight);
                            pieces++;
                        }
                    }
                }
            }

            AddObject("Roads", parent, roadMb.ToMesh("Roads"), mats.Asphalt, meshes, collider: false);
            AddObject("Sidewalks", parent, swMb.ToMesh("Sidewalks"), mats.Concrete, meshes, collider: true);
        }

        internal static bool OnAnyRoad(Vector2 pt, List<(Vector2 a, Vector2 b, float halfW, long id)> segs, long ownId, float margin)
        {
            foreach (var s in segs)
            {
                if (s.id == ownId) continue;
                if (DistToSegment(pt, s.a, s.b) < s.halfW + margin) return true;
            }
            return false;
        }

        static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return (p - (a + ab * t)).magnitude;
        }

        // flat strip along a polyline, mitred at the joints
        static void Ribbon(MeshBuilder mb, List<Vector2> pts, float width, float y)
        {
            if (pts.Count < 2) return;
            float hw = width * 0.5f, dist = 0f;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                float len = (b - a).magnitude;
                if (len < 0.01f) continue;
                Vector2 la = JointLeft(pts, i) * hw, lb = JointLeft(pts, i + 1) * hw;
                mb.Quad(V(a - la, y), V(b - lb, y), V(b + lb, y), V(a + la, y), Vector3.up,
                        new Vector2(0, dist), new Vector2(0, dist + len), new Vector2(width, dist + len), new Vector2(width, dist));
                dist += len;
            }
        }

        static Vector2 JointLeft(List<Vector2> p, int i)
        {
            Vector2 dPrev = i > 0 ? (p[i] - p[i - 1]).normalized : Vector2.zero;
            Vector2 dNext = i + 1 < p.Count ? (p[i + 1] - p[i]).normalized : Vector2.zero;
            Vector2 d = (dPrev + dNext);
            if (d.sqrMagnitude < 1e-6f) d = dNext.sqrMagnitude > 0 ? dNext : dPrev;
            d.Normalize();
            Vector2 left = new Vector2(-d.y, d.x);
            // stretch the mitre so the strip keeps its width at the bend (capped for sharp turns)
            Vector2 segLeft = dNext.sqrMagnitude > 0 ? new Vector2(-dNext.y, dNext.x) : new Vector2(-dPrev.y, dPrev.x);
            float cos = Mathf.Max(0.5f, Vector2.Dot(left, segLeft));
            return left / cos;
        }

        // raised box strip from a to b, centred on the a-b line
        static void Slab(MeshBuilder mb, Vector2 a, Vector2 b, Vector2 left, float width, float height)
        {
            Vector2 h = left * (width * 0.5f);
            Vector2 a0 = a - h, a1 = a + h, b0 = b - h, b1 = b + h;
            float len = (b - a).magnitude;
            mb.Quad(V(a0, height), V(b0, height), V(b1, height), V(a1, height), Vector3.up,
                    a0, b0, b1, a1);
            var side = new Vector3(left.x, 0, left.y);
            mb.Quad(V(a1, 0), V(b1, 0), V(b1, height), V(a1, height), side,
                    new Vector2(0, 0), new Vector2(len, 0), new Vector2(len, height), new Vector2(0, height));
            mb.Quad(V(a0, 0), V(b0, 0), V(b0, height), V(a0, height), -side,
                    new Vector2(0, 0), new Vector2(len, 0), new Vector2(len, height), new Vector2(0, height));
        }

        // ------------------------------------------------------ paths and plazas
        static void BuildPaths(CityData d, Transform parent, Materials mats, List<Mesh> meshes)
        {
            var mb = new MeshBuilder();
            var roadAreas = new MeshBuilder();
            foreach (var pz in d.plazas)
            {
                var ring = CityDataUtil.Points(pz.pts);
                if (ring.Count < 3) continue;
                if (pz.kind == "road") roadAreas.Polygon(ring, 0.07f, true);
                else mb.Polygon(ring, 0.08f, true);
            }
            foreach (var p in d.paths)
                Ribbon(mb, CityDataUtil.Points(p.pts), p.width, 0.09f);
            AddObject("PlazasAndPaths", parent, mb.ToMesh("PlazasAndPaths"), mats.Paving, meshes, collider: false);
            AddObject("RoadAreas", parent, roadAreas.ToMesh("RoadAreas"), mats.Asphalt, meshes, collider: false);
        }

        // ----------------------------------------------------------------- signs
        const string SignsPath = "Assets/City/Data/signs.json";
        public const float SignNits = 300f;          // neon/backlit face luminance
        public const float SignLightLumens = 260f;   // coloured spill light per store sign

        // Store signs and building names (docs/DESIGN.md 3G): one quad per sign on its wall, all in one
        // mesh with the atlas material, plus a small coloured light in front of each neon store sign.
        static int BuildSigns(Transform parent, Materials mats, List<Mesh> meshes)
        {
            if (!File.Exists(SignsPath)) { Debug.Log("CITY_SKIP no signs.json (run Tools/assets/make_signs.py)"); return 0; }
            var list = JsonUtility.FromJson<SignList>(File.ReadAllText(SignsPath));
            var group = new GameObject("Signs").transform;
            group.SetParent(parent, false);
            var mb = new MeshBuilder();
            int n = 0;
            foreach (var s in list.signs)
            {
                var normal = new Vector3(s.nx, 0f, s.nz).normalized;
                var right = new Vector3(-s.nz, 0f, s.nx).normalized;     // reads left-to-right for a viewer facing the wall
                var c = new Vector3(s.x, s.y, s.z) + normal * 0.06f;
                Vector3 hw = right * (s.w * 0.5f), hh = Vector3.up * (s.h * 0.5f);
                mb.Quad(c - hw - hh, c + hw - hh, c + hw + hh, c - hw + hh, normal,
                        new Vector2(s.uv[0], s.uv[1]), new Vector2(s.uv[2], s.uv[1]),
                        new Vector2(s.uv[2], s.uv[3]), new Vector2(s.uv[0], s.uv[3]));
                if (s.style == "neon")
                {
                    var go = new GameObject($"SignLight_{n:000}");
                    go.transform.SetParent(group, false);
                    go.transform.position = c + normal * 0.9f - Vector3.up * 0.4f;
                    var light = go.AddComponent<Light>();
                    go.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();
                    light.type = LightType.Point;
                    light.lightUnit = UnityEngine.Rendering.LightUnit.Lumen;
                    light.intensity = SignLightLumens;
                    light.color = new Color(s.color[0], s.color[1], s.color[2]);
                    light.range = 6f;
                    light.shadows = LightShadows.None;
                }
                n++;
            }
            var obj = new GameObject("SignFaces");
            obj.transform.SetParent(group, false);
            var mesh = mb.ToMesh("Signs");
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = obj.AddComponent<MeshRenderer>();
            r.sharedMaterial = mats.Signs;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshes.Add(mesh);
            return n;
        }

        // ---------------------------------------------------------- streetlights
        const float LightSpacing = 32f, PoleHeight = 8f, ArmLength = 1.8f;
        static readonly HashSet<string> LitRoadTypes = new HashSet<string>
            { "primary", "secondary", "tertiary", "unclassified", "residential", "living_street" };

        // Staggered poles on both sides of every street, each with a warm downward spot light.
        // Poles never stand on another road, inside a building, or within 12 m of another pole.
        static int BuildStreetlights(CityData d, Transform parent, Materials mats, List<Mesh> meshes)
        {
            var group = new GameObject("Streetlights").transform;
            group.SetParent(parent, false);
            var segs = new List<(Vector2 a, Vector2 b, float halfW, long id)>();
            foreach (var r in d.roads)
            {
                var p = CityDataUtil.Points(r.pts);
                for (int i = 0; i + 1 < p.Count; i++) segs.Add((p[i], p[i + 1], r.width * 0.5f, r.id));
            }
            var footprints = new List<List<Vector2>>();
            foreach (var b in d.buildings) if (b.minH < 0.5f) footprints.Add(CityDataUtil.Points(b.pts));

            var poles = new MeshBuilder();
            var heads = new MeshBuilder();
            var placed = new List<Vector2>();
            foreach (var r in d.roads)
            {
                if (!LitRoadTypes.Contains(r.type)) continue;
                var p = CityDataUtil.Points(r.pts);
                float off = r.width * 0.5f + 0.6f, step = LightSpacing * 0.5f, untilNext = step * 0.5f;
                int k = 0;   // alternates the side: one pole every half-spacing, sides staggered
                for (int i = 0; i + 1 < p.Count; i++)
                {
                    Vector2 a = p[i], b = p[i + 1];
                    float len = (b - a).magnitude;
                    if (len < 0.5f) continue;
                    Vector2 dir = (b - a) / len, left = new Vector2(-dir.y, dir.x);
                    float t = untilNext;
                    for (; t < len; t += step)
                    {
                        int side = (k++ % 2 == 0) ? 1 : -1;
                        Vector2 basePt = a + dir * t + left * (off * side);
                        if (OnAnyRoad(basePt, segs, r.id, 0.4f)) continue;
                        if (footprints.Exists(f => PointInPolygon(basePt, f))) continue;
                        if (placed.Exists(q => (q - basePt).sqrMagnitude < 144f)) continue;
                        placed.Add(basePt);
                        Vector2 toRoad = -left * side;
                        AddPole(poles, heads, basePt, toRoad);
                        Vector2 headPt = basePt + toRoad * ArmLength;
                        AddLight(group, new Vector3(headPt.x, PoleHeight - 0.25f, headPt.y), placed.Count);
                    }
                    untilNext = t - len;
                }
            }
            AddObject("StreetlightPoles", group, poles.ToMesh("StreetlightPoles"), mats.Pole, meshes, collider: true);
            AddObject("StreetlightHeads", group, heads.ToMesh("StreetlightHeads"), mats.Lamp, meshes, collider: false);
            return placed.Count;
        }

        static void AddPole(MeshBuilder poles, MeshBuilder heads, Vector2 at, Vector2 toRoad)
        {
            var fwd = new Vector3(toRoad.x, 0, toRoad.y);
            var right = Vector3.Cross(Vector3.up, fwd);
            var basePos = new Vector3(at.x, 0, at.y);
            Box(poles, basePos + Vector3.up * (PoleHeight * 0.5f), right * 0.09f, Vector3.up * (PoleHeight * 0.5f), fwd * 0.09f);
            var armCentre = basePos + Vector3.up * (PoleHeight - 0.1f) + fwd * (ArmLength * 0.5f);
            Box(poles, armCentre, right * 0.05f, Vector3.up * 0.05f, fwd * (ArmLength * 0.5f));
            var headCentre = basePos + Vector3.up * (PoleHeight - 0.18f) + fwd * ArmLength;
            Box(heads, headCentre, right * 0.18f, Vector3.up * 0.06f, fwd * 0.32f);
        }

        static void AddLight(Transform parent, Vector3 pos, int index)
        {
            var go = new GameObject($"Lamp_{index:000}");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(90f, 0f, 0f));
            var light = go.AddComponent<Light>();
            go.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();
            light.type = LightType.Spot;
            light.lightUnit = UnityEngine.Rendering.LightUnit.Lumen;
            light.intensity = StreetlightLumens;
            light.useColorTemperature = true;
            light.colorTemperature = 3200f;
            light.color = Color.white;
            light.range = 30f;
            light.spotAngle = 125f;
            light.innerSpotAngle = 70f;
            light.shadows = LightShadows.None;
        }

        public const float StreetlightLumens = 7000f;
        const float LampNits = 4000f;

        // oriented box from centre and three half-extent axis vectors
        static void Box(MeshBuilder mb, Vector3 c, Vector3 x, Vector3 y, Vector3 z)
        {
            foreach (var (n, u, v) in new[] { (x, y, z), (-x, y, z), (y, z, x), (-y, z, x), (z, x, y), (-z, x, y) })
            {
                Vector3 f = c + n;
                mb.Quad(f - u - v, f + u - v, f + u + v, f - u + v, n,
                        Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            }
        }

        internal static bool PointInPolygon(Vector2 pt, List<Vector2> poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                if ((poly[i].y > pt.y) != (poly[j].y > pt.y) &&
                    pt.x < (poly[j].x - poly[i].x) * (pt.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            }
            return inside;
        }

        // ------------------------------------------------------------- helpers
        static Vector3 V(Vector2 p, float y) => new Vector3(p.x, y, p.y);

        static void AddObject(string name, Transform parent, Mesh mesh, Material mat, List<Mesh> meshes, bool collider) =>
            AddObject(name, parent, mesh, new[] { mat }, meshes, collider);

        static void AddObject(string name, Transform parent, Mesh mesh, Material[] mats, List<Mesh> meshes, bool collider)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                                                       StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI);
            meshes.Add(mesh);
        }

        static void PlaceMainCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;
            // street level on Yonge, south of Dundas, looking north
            cam.transform.SetPositionAndRotation(new Vector3(0f, 1.7f, -60f), Quaternion.Euler(0f, 0f, 0f));
        }

        // Placeholder HDRP Lit materials (grey-box pass). The CC0 texture pass replaces these.
        // HDRP Lit materials from the approved CC0 sets (docs/DESIGN.md 3A.1). Mesh UVs are in metres,
        // so each material's tiling is 1 / (real-world size of one texture tile).
        class Materials
        {
            const string TexDir = "Assets/City/Textures";
            public readonly Material Ground, Asphalt, Concrete, Paving, Roof, Pole, Lamp, Signs;
            readonly Material[] towers, midRise, lowRise;

            public Materials()
            {
                Directory.CreateDirectory(MaterialDir);
                // L3 wet streets: rain darkens albedo and lifts smoothness (smoothness remap min/max)
                Asphalt = Lit("Asphalt", "asphalt_02", 3f, new Color(0.6f, 0.6f, 0.62f), 0.62f, 0.95f);
                Concrete = Lit("Sidewalk", "brushed_concrete_03", 2f, new Color(0.62f, 0.62f, 0.63f), 0.45f, 0.85f);
                // PavingStones138 turned out to be mossy cobblestone; Yonge-Dundas Square is granite slabs.
                // Concrete stands in until a granite-slab set is approved.
                Paving = Lit("Paving", "brushed_concrete_03", 2f, new Color(0.55f, 0.55f, 0.57f), 0.5f, 0.88f);
                // downtown, everything off the road is pavement: the ground matches the sidewalk slabs
                Ground = Lit("Ground", "brushed_concrete_03", 2f, new Color(0.62f, 0.62f, 0.63f), 0.45f, 0.85f);
                Roof = Lit("Roof", "brushed_concrete_03", 2f, new Color(0.35f, 0.35f, 0.36f));
                var f009 = Lit("FacadeOfficeNight", "Facade009", 24f, Color.white);
                var f001 = Lit("FacadeGlassPanels", "Facade001", 14f, Color.white);
                var f005 = Lit("FacadeGlassGrid", "Facade005", 24f, Color.white);
                var f006 = Lit("FacadeOfficeBands", "Facade006", 30f, Color.white);
                var brick = Lit("FacadeBrickRed", "brick_wall_001", 3f, Color.white);
                var brown = Lit("FacadeBrickBuff", "brown_brick_02", 1.5f, Color.white);
                Pole = Plain("StreetlightPole", new Color(0.18f, 0.18f, 0.19f), 0.45f, 0.8f, Color.black, 0f);
                Lamp = Plain("StreetlightLamp", new Color(0.9f, 0.9f, 0.85f), 0.6f, 0f, new Color(1f, 0.78f, 0.52f), LampNits);
                Signs = SignMaterial();
                towers = new[] { f009, f009, f001, f005 };
                midRise = new[] { f006, f006, brick, f009 };
                lowRise = new[] { brick, brick, brown };
            }

            // stable per building: the same OSM id always gets the same facade
            public Material Facade(float height, long id)
            {
                var set = height > 60f ? towers : height > 18f ? midRise : lowRise;
                return set[(int)((id * 2654435761L & 0x7fffffff) % set.Length)];
            }

            static Material Lit(string name, string texSet, float tileMetres, Color tint,
                                float smoothMin = 0f, float smoothMax = 1f)
            {
                string path = $"{MaterialDir}/{name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(Shader.Find("HDRP/Lit")) { name = name };
                    AssetDatabase.CreateAsset(mat, path);
                }
                string dir = $"{TexDir}/{texSet}";
                mat.SetColor("_BaseColor", tint);
                mat.SetTexture("_BaseColorMap", LoadMap(dir, "_Color", TextureImporterType.Default, true));
                mat.SetTexture("_NormalMap", LoadMap(dir, "_NormalGL", TextureImporterType.NormalMap, false));
                mat.SetFloat("_NormalScale", 1f);
                mat.SetTexture("_MaskMap", LoadMap(dir, "_MaskMap", TextureImporterType.Default, false));
                mat.SetFloat("_MetallicRemapMin", 0f); mat.SetFloat("_MetallicRemapMax", 1f);
                mat.SetFloat("_SmoothnessRemapMin", smoothMin); mat.SetFloat("_SmoothnessRemapMax", smoothMax);
                mat.SetFloat("_AORemapMin", 0f); mat.SetFloat("_AORemapMax", 1f);
                float s = 1f / tileMetres;
                mat.SetTextureScale("_BaseColorMap", new Vector2(s, s));
                UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(mat);
                EditorUtility.SetDirty(mat);
                return mat;
            }

            // sign atlas from make_signs.py: albedo for the plates, emissive map for the glowing letters
            static Material SignMaterial()
            {
                string path = $"{MaterialDir}/Signs.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(Shader.Find("HDRP/Lit")) { name = "Signs" };
                    AssetDatabase.CreateAsset(mat, path);
                }
                Texture2D Atlas(string file)
                {
                    string p = $"Assets/City/Signs/{file}";
                    var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                    if (ti != null && ti.maxTextureSize < 8192) { ti.maxTextureSize = 8192; ti.SaveAndReimport(); }   // 4096 x 8192 atlas
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                }
                mat.SetTexture("_BaseColorMap", Atlas("signs_albedo.png"));
                mat.SetColor("_BaseColor", Color.white);
                mat.SetTexture("_EmissiveColorMap", Atlas("signs_emissive.png"));
                mat.SetColor("_EmissiveColorLDR", Color.white);
                mat.SetFloat("_EmissiveIntensity", SignNits);
                mat.SetColor("_EmissiveColor", Color.white * SignNits);
                mat.SetFloat("_Smoothness", 0.6f);
                mat.SetFloat("_Metallic", 0f);
                UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(mat);
                EditorUtility.SetDirty(mat);
                return mat;
            }

            // untextured Lit material, optionally emissive (luminance in nits)
            static Material Plain(string name, Color baseColor, float smoothness, float metallic, Color emissive, float nits)
            {
                string path = $"{MaterialDir}/{name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(Shader.Find("HDRP/Lit")) { name = name };
                    AssetDatabase.CreateAsset(mat, path);
                }
                mat.SetColor("_BaseColor", baseColor);
                mat.SetFloat("_Smoothness", smoothness);
                mat.SetFloat("_Metallic", metallic);
                mat.SetColor("_EmissiveColorLDR", emissive);
                mat.SetFloat("_EmissiveIntensity", nits);
                mat.SetColor("_EmissiveColor", emissive.linear * nits);
                UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(mat);
                EditorUtility.SetDirty(mat);
                return mat;
            }

            // finds <dir>/*<suffix>.(jpg|png) and fixes its import type / colour space
            static Texture2D LoadMap(string dir, string suffix, TextureImporterType type, bool srgb)
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    if (!Path.GetFileNameWithoutExtension(p).EndsWith(suffix)) continue;
                    var imp = (TextureImporter)AssetImporter.GetAtPath(p);
                    if (imp.textureType != type || imp.sRGBTexture != srgb || imp.maxTextureSize != 2048)
                    {
                        imp.textureType = type;
                        imp.sRGBTexture = srgb;
                        imp.maxTextureSize = 2048;
                        imp.SaveAndReimport();
                    }
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                }
                Debug.LogWarning($"CITY_WARN missing map {dir}/*{suffix}");
                return null;
            }
        }
    }
}
