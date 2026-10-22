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

            // meshes live in one asset file so the scene file stays small
            AssetDatabase.CreateAsset(meshes[0], MeshAssetPath);
            for (int i = 1; i < meshes.Count; i++) AssetDatabase.AddObjectToAsset(meshes[i], MeshAssetPath);
            AssetDatabase.SaveAssets();

            PlaceMainCamera();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"CITY_OK buildings={built} roads={data.roads.Length} paths={data.paths.Length} " +
                      $"plazas={data.plazas.Length} sidewalkPieces={sidewalkPieces} meshes={meshes.Count}");
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
                mb.Polygon(ring, y1, true);
                if (y0 > 0.01f) mb.Polygon(ring, y0, false);   // underside of podium overhangs

                string label = string.IsNullOrEmpty(bd.name) ? bd.id.ToString() : bd.name;
                AddObject($"Bldg_{label}", group, mb.ToMesh($"Bldg_{bd.id}"), mats.Facade(bd.h), meshes, collider: true);
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

        static bool OnAnyRoad(Vector2 pt, List<(Vector2 a, Vector2 b, float halfW, long id)> segs, long ownId, float margin)
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
            foreach (var pz in d.plazas)
            {
                var ring = CityDataUtil.Points(pz.pts);
                if (ring.Count >= 3) mb.Polygon(ring, 0.08f, true);
            }
            foreach (var p in d.paths)
                Ribbon(mb, CityDataUtil.Points(p.pts), p.width, 0.09f);
            AddObject("PlazasAndPaths", parent, mb.ToMesh("PlazasAndPaths"), mats.Paving, meshes, collider: false);
        }

        // ------------------------------------------------------------- helpers
        static Vector3 V(Vector2 p, float y) => new Vector3(p.x, y, p.y);

        static void AddObject(string name, Transform parent, Mesh mesh, Material mat, List<Mesh> meshes, bool collider)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
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
        class Materials
        {
            public readonly Material Ground, Asphalt, Concrete, Paving, Glass, Brick, Storefront;

            public Materials()
            {
                Directory.CreateDirectory(MaterialDir);
                Ground = Get("Ground", new Color(0.22f, 0.22f, 0.21f), 0.2f, 0f);
                Asphalt = Get("Asphalt", new Color(0.09f, 0.09f, 0.10f), 0.35f, 0f);
                Concrete = Get("Sidewalk", new Color(0.55f, 0.54f, 0.52f), 0.25f, 0f);
                Paving = Get("Paving", new Color(0.45f, 0.42f, 0.40f), 0.3f, 0f);
                Glass = Get("FacadeGlass", new Color(0.20f, 0.27f, 0.33f), 0.9f, 0.6f);
                Brick = Get("FacadeBrick", new Color(0.45f, 0.25f, 0.18f), 0.2f, 0f);
                Storefront = Get("FacadeStorefront", new Color(0.62f, 0.58f, 0.50f), 0.3f, 0f);
            }

            public Material Facade(float height) => height > 60f ? Glass : height > 15f ? Brick : Storefront;

            static Material Get(string name, Color color, float smoothness, float metallic)
            {
                string path = $"{MaterialDir}/{name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null) return mat;   // keep hand-tuned or textured materials
                mat = new Material(Shader.Find("HDRP/Lit")) { name = name };
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Smoothness", smoothness);
                mat.SetFloat("_Metallic", metallic);
                UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(mat);
                AssetDatabase.CreateAsset(mat, path);
                return mat;
            }
        }
    }
}
