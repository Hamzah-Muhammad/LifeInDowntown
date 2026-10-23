using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace LifeInDowntown.City
{
    // Procedural cars (docs/DESIGN.md 3O.4, 3O.5). The body is lofted from slices along the car: each slice is a
    // rounded (superellipse) section whose half-width and height follow plan and side profiles; the cabin is a
    // second loft that narrows towards the roof (tumblehome), its faces sorted into glass, paint and black trim
    // (A/B/C pillars, window frames). Lights, grille, mirrors, wheels and a basic interior are added as parts.
    // Units: metres, car forward = +Z, origin on the ground at the centre, driver on the left (Canada).
    public static class CarBuilder
    {
        public const string Dir = "Assets/Vehicles";

        public class Spec
        {
            public string name;
            public float length = 4.47f, width = 1.74f, height = 1.415f, wheelbase = 2.725f, frontAxle = 0.8f;   // E46 sedan
            public float wheelR = 0.316f, tyreW = 0.205f, track = 1.48f, clearance = 0.17f;
            public float beltline = 0.92f;
            // side profile: top of the body (x from the front bumper, y) and the cabin's roof line
            public Vector2[] bodyTop = { new(0f, 0.6f), new(0.12f, 0.69f), new(0.5f, 0.74f), new(1.25f, 0.84f), new(3.65f, 0.98f), new(4.3f, 0.98f), new(4.47f, 0.62f) };
            public Vector2[] roof = { new(1.25f, 0.92f), new(1.95f, 1.415f), new(3.0f, 1.4f), new(3.65f, 0.98f) };
            public float cabinTopHalf = 0.64f;          // half-width at the roof
            public float bPillar = 2.44f;               // x of the B pillar
            public bool bmw = true;                     // twin round headlamps, kidney grille, L-shaped tails
            public bool interior = true;
        }

        public static Spec E46() => new Spec { name = "E46" };

        // street cars (DESIGN 3O.5): body types from typical real dimensions, no brand cues, no interior
        public static Spec Sedan() => new Spec { name = "Sedan", bmw = false, interior = false };
        public static Spec Hatch() => new Spec
        {
            name = "Hatch", bmw = false, interior = false, length = 4.15f, wheelbase = 2.6f, frontAxle = 0.85f, height = 1.46f, bPillar = 2.35f,
            bodyTop = new Vector2[] { new(0f, 0.6f), new(0.12f, 0.7f), new(0.5f, 0.76f), new(1.15f, 0.86f), new(3.9f, 0.96f), new(4.1f, 0.9f), new(4.15f, 0.6f) },
            roof = new Vector2[] { new(1.15f, 0.92f), new(1.85f, 1.46f), new(3.75f, 1.43f), new(4.05f, 0.97f) },
        };
        public static Spec Suv() => new Spec
        {
            name = "SUV", bmw = false, interior = false, length = 4.65f, width = 1.86f, height = 1.72f, wheelbase = 2.8f, frontAxle = 0.9f,
            wheelR = 0.36f, tyreW = 0.235f, clearance = 0.21f, track = 1.6f, beltline = 1.08f, cabinTopHalf = 0.74f, bPillar = 2.5f,
            bodyTop = new Vector2[] { new(0f, 0.75f), new(0.12f, 0.88f), new(0.6f, 0.95f), new(1.25f, 1.02f), new(4.3f, 1.1f), new(4.6f, 1.0f), new(4.65f, 0.75f) },
            roof = new Vector2[] { new(1.25f, 1.08f), new(1.95f, 1.72f), new(4.35f, 1.7f), new(4.6f, 1.12f) },
        };
        public static Spec Minivan() => new Spec
        {
            name = "Minivan", bmw = false, interior = false, length = 5.1f, width = 1.95f, height = 1.75f, wheelbase = 3.0f, frontAxle = 0.95f,
            wheelR = 0.35f, tyreW = 0.235f, track = 1.68f, beltline = 1.0f, cabinTopHalf = 0.8f, bPillar = 2.3f,
            bodyTop = new Vector2[] { new(0f, 0.7f), new(0.12f, 0.8f), new(0.6f, 0.9f), new(1.1f, 1.0f), new(4.85f, 1.05f), new(5.05f, 0.95f), new(5.1f, 0.7f) },
            roof = new Vector2[] { new(1.1f, 1.0f), new(1.9f, 1.75f), new(4.8f, 1.73f), new(5.05f, 1.1f) },
        };
        public static Spec Van() => new Spec
        {
            name = "Van", bmw = false, interior = false, length = 5.4f, width = 2.0f, height = 2.4f, wheelbase = 3.4f, frontAxle = 0.95f,
            wheelR = 0.37f, tyreW = 0.235f, clearance = 0.2f, track = 1.72f, beltline = 1.15f, cabinTopHalf = 0.85f, bPillar = 1.95f,
            bodyTop = new Vector2[] { new(0f, 0.8f), new(0.12f, 0.95f), new(0.5f, 1.05f), new(1.0f, 1.15f), new(2.1f, 1.15f), new(2.25f, 2.38f), new(5.3f, 2.38f), new(5.4f, 2.2f) },
            roof = new Vector2[] { new(1.0f, 1.15f), new(1.7f, 2.3f), new(2.15f, 2.36f), new(2.25f, 1.16f) },
        };

        // shared-vertex mesh: the lofts weld along their grids so RecalculateNormals smooths the body; parts
        // (boxes, discs) get their own vertices and come out flat. Winding is fixed from a facing hint.
        class CarMesh
        {
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<List<int>> sub = new List<List<int>>();
            int cur;
            public void UseSubmesh(int i) { while (sub.Count <= i) sub.Add(new List<int>()); cur = i; }
            public System.Func<Vector3, Vector3> Xform;   // places a part (the baked-in wheels of parked cars)
            public int V(Vector3 p) { v.Add(Xform != null ? Xform(p) : p); return v.Count - 1; }
            public Vector3 this[int i] => v[i];
            public System.Func<Vector3, Vector3> XformDir;
            public void T(int a, int b, int c, Vector3 facing)
            {
                UseSubmesh(cur);
                if (XformDir != null) facing = XformDir(facing);
                if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), facing) < 0f) (b, c) = (c, b);
                sub[cur].Add(a); sub[cur].Add(b); sub[cur].Add(c);
            }
            public void Q(int a, int b, int c, int d, Vector3 facing) { T(a, b, c, facing); T(a, c, d, facing); }
            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 facing) => T(V(a), V(b), V(c), facing);
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing) { int i = V(a), j = V(b), k = V(c), l = V(d); Q(i, j, k, l, facing); }
            public Mesh ToMesh(string name)
            {
                while (sub.Count < 11) sub.Add(new List<int>());
                var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.SetVertices(v);
                var uv = new List<Vector2>(v.Count);
                foreach (var p in v) uv.Add(new Vector2(p.z + p.x, p.y));
                m.SetUVs(0, uv);
                m.subMeshCount = sub.Count;
                for (int i = 0; i < sub.Count; i++) m.SetTriangles(sub[i], i);
                m.RecalculateNormals(); m.RecalculateBounds(); m.RecalculateTangents();
                return m;
            }
            public int Tris { get { int n = 0; foreach (var s in sub) n += s.Count / 3; return n; } }
        }

        public static GameObject BuildPrefab(Spec spec, Color paint, string prefabName, bool drivable)
        {
            Directory.CreateDirectory(Dir);
            var mats = Materials(paint, prefabName);
            if (!drivable)   // parked and switched off: lamps unlit
            {
                mats[4] = CityGenerator.Materials.Plain("Car_HeadlampOff", new Color(0.7f, 0.72f, 0.72f), 0.9f, 0f, Color.black, 0f);
                mats[5] = CityGenerator.Materials.Plain("Car_TaillampOff", new Color(0.32f, 0.03f, 0.03f), 0.85f, 0f, Color.black, 0f);
            }
            var body = new CarMesh();
            Body(body, spec);
            Cabin(body, spec);
            Details(body, spec);
            if (spec.interior) Interior(body, spec);
            if (!drivable)   // parked: wheels baked into the one body mesh (one renderer per car)
                for (int i = 0; i < 4; i++)
                {
                    bool front = i < 2, left = i % 2 == 0;
                    var c = new Vector3((left ? -1 : 1) * spec.track / 2, spec.wheelR, spec.length / 2 - spec.frontAxle - (front ? 0f : spec.wheelbase));
                    body.Xform = q => c + (left ? q : new Vector3(-q.x, q.y, -q.z));
                    body.XformDir = d => left ? d : new Vector3(-d.x, d.y, -d.z);
                    Wheel(body, spec);
                    body.Xform = null; body.XformDir = null;
                }
            int tris = body.Tris;
            var mesh = body.ToMesh($"{prefabName}_Body");
            Save(mesh, $"{Dir}/{prefabName}_Body.asset");

            var wheelMb = new CarMesh();
            Wheel(wheelMb, spec);
            var wheelMesh = wheelMb.ToMesh($"{prefabName}_Wheel");
            Save(wheelMesh, $"{Dir}/{prefabName}_Wheel.asset");

            var root = new GameObject(prefabName);
            var bodyGo = new GameObject("Body");
            bodyGo.transform.SetParent(root.transform, false);
            bodyGo.AddComponent<MeshFilter>().sharedMesh = mesh;
            bodyGo.AddComponent<MeshRenderer>().sharedMaterials = mats;
            float halfL = spec.length / 2;
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, (spec.clearance + spec.height) / 2 + 0.05f, 0f);
            col.size = new Vector3(spec.width, spec.height - spec.clearance - 0.1f, spec.length);
            string[] names = { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
            for (int i = 0; i < 4 && drivable; i++)
            {
                bool front = i < 2, left = i % 2 == 0;
                float z = front ? halfL - spec.frontAxle : halfL - spec.frontAxle - spec.wheelbase;
                var w = new GameObject(names[i]);
                w.transform.SetParent(root.transform, false);
                w.transform.localPosition = new Vector3((left ? -1 : 1) * spec.track / 2, spec.wheelR, z);
                var vis = new GameObject("Mesh");
                vis.transform.SetParent(w.transform, false);
                if (!left) vis.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // rim face outward
                vis.AddComponent<MeshFilter>().sharedMesh = wheelMesh;
                vis.AddComponent<MeshRenderer>().sharedMaterials = mats;
            }
            if (drivable)
            {
                // physics: Rigidbody on four WheelColliders (separate from the wheel meshes, which follow them)
                var rb = root.AddComponent<Rigidbody>();
                rb.mass = 1450f; rb.linearDamping = 0.02f; rb.angularDamping = 0.6f;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                var ctrl = root.AddComponent<LifeInDowntown.Vehicles.CarController>();
                ctrl.wheels = new WheelCollider[4]; ctrl.visuals = new Transform[4];
                for (int i = 0; i < 4; i++)
                {
                    var vis = root.transform.Find(names[i]);
                    var wc = new GameObject("Collider_" + names[i]);
                    wc.transform.SetParent(root.transform, false);
                    wc.transform.localPosition = vis.localPosition + Vector3.up * 0.09f;
                    var w = wc.AddComponent<WheelCollider>();
                    w.radius = spec.wheelR; w.mass = 20f; w.suspensionDistance = 0.18f; w.forceAppPointDistance = 0.1f;
                    w.suspensionSpring = new JointSpring { spring = 38000f, damper = 4200f, targetPosition = 0.5f };
                    var f = w.forwardFriction; f.stiffness = 1.7f; w.forwardFriction = f;
                    var sd = w.sidewaysFriction; sd.stiffness = 1.55f; w.sidewaysFriction = sd;
                    ctrl.wheels[i] = w; ctrl.visuals[i] = vis;
                }
                var src = root.AddComponent<AudioSource>();
                src.clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Resources/Audio/engine_i6.wav");
                src.loop = true; src.playOnAwake = false; src.spatialBlend = 0.6f; src.volume = 0.4f; src.minDistance = 4f; src.maxDistance = 60f;
                ctrl.engine = src;
                Anchor(root, "DriverSeat", new Vector3(-0.37f, 1.2f, halfL - 2.35f));           // his eyes, seated
                Anchor(root, "DriverDoor", new Vector3(-1.35f, 0.1f, halfL - 2.2f));            // step out here
                foreach (float x in new[] { -0.55f, 0.55f })
                {
                    var hl = Anchor(root, "Headlight", new Vector3(x, 0.66f, halfL + 0.05f));
                    var l = hl.gameObject.AddComponent<Light>(); hl.gameObject.AddComponent<HDAdditionalLightData>();
                    l.type = LightType.Spot; l.spotAngle = 70f; l.innerSpotAngle = 35f; l.range = 45f;
                    l.lightUnit = UnityEngine.Rendering.LightUnit.Lumen; l.intensity = 1800f;
                    l.useColorTemperature = true; l.colorTemperature = 4200f; l.color = Color.white;
                    l.shadows = LightShadows.None;
                    hl.localRotation = Quaternion.Euler(6f, 0f, 0f);
                }
            }
            string path = $"{Dir}/{prefabName}.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            Debug.Log($"CAR_OK {prefabName} tris={tris} size={spec.width:F2}x{spec.height:F2}x{spec.length:F2} m");
            return prefab;
        }

        static Transform Anchor(GameObject root, string name, Vector3 at)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root.transform, false);
            t.localPosition = at;
            return t;
        }

        // submeshes: 0 paint, 1 glass, 2 black trim, 3 chrome, 4 headlamp, 5 taillamp, 6 tyre, 7 rim, 8 interior, 9 gauges, 10 plate
        static Material[] Materials(Color paint, string name)
        {
            var m = new Material[11];
            m[0] = CityGenerator.Materials.Plain($"Car_{name}_Paint", paint, 0.86f, 0.55f, Color.black, 0f);
            m[1] = CityGenerator.Materials.GlassMaterial("Car_Glass", new Color(0.08f, 0.09f, 0.1f, 0.55f));
            m[2] = CityGenerator.Materials.Plain("Car_Trim", new Color(0.03f, 0.03f, 0.035f), 0.45f, 0f, Color.black, 0f);
            m[3] = CityGenerator.Materials.Plain("Car_Chrome", new Color(0.8f, 0.8f, 0.82f), 0.92f, 1f, Color.black, 0f);
            m[4] = CityGenerator.Materials.Plain("Car_Headlamp", new Color(0.9f, 0.9f, 0.88f), 0.9f, 0f, new Color(1f, 0.96f, 0.9f), 30f);
            m[5] = CityGenerator.Materials.Plain("Car_Taillamp", new Color(0.4f, 0.02f, 0.02f), 0.85f, 0f, new Color(1f, 0.05f, 0.03f), 10f);
            m[6] = CityGenerator.Materials.Plain("Car_Tyre", new Color(0.04f, 0.04f, 0.045f), 0.35f, 0f, Color.black, 0f);
            m[7] = CityGenerator.Materials.Plain("Car_Rim", new Color(0.62f, 0.63f, 0.65f), 0.8f, 1f, Color.black, 0f);
            m[8] = CityGenerator.Materials.Plain("Car_Interior", new Color(0.06f, 0.06f, 0.065f), 0.3f, 0f, Color.black, 0f);
            m[9] = CityGenerator.Materials.Plain("Car_Gauges", new Color(0.05f, 0.03f, 0.02f), 0.6f, 0f, new Color(1f, 0.45f, 0.1f), 6f);   // BMW orange
            m[10] = CityGenerator.Materials.Plain("Car_Plate", new Color(0.92f, 0.92f, 0.95f), 0.5f, 0f, Color.black, 0f);
            return m;
        }

        static void Save(Mesh mesh, string path)
        {
            if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
        }

        static float Profile(Vector2[] pts, float x)
        {
            if (x <= pts[0].x) return pts[0].y;
            for (int i = 0; i + 1 < pts.Length; i++)
                if (x <= pts[i + 1].x)
                {
                    float t = Mathf.InverseLerp(pts[i].x, pts[i + 1].x, x);
                    t = t * t * (3f - 2f * t);                       // smooth between the key points
                    return Mathf.Lerp(pts[i].y, pts[i + 1].y, t);
                }
            return pts[pts.Length - 1].y;
        }

        // plan half-width: rounded at both ends
        static float HalfWidth(Spec s, float x)
        {
            float e = Mathf.Min(x, s.length - x);
            return s.width / 2 * Mathf.Lerp(0.8f, 1f, Mathf.Sqrt(Mathf.Clamp01(e / 0.45f)));
        }

        static Vector3 P(Spec s, float x, float sideX, float y) => new Vector3(sideX, y, s.length / 2 - x);

        // superellipse ring point, angle a (0 = right, pi/2 = top)
        static Vector2 Ring(float a, float hw, float y0, float y1, float n)
        {
            float c = Mathf.Cos(a), si = Mathf.Sin(a);
            float px = Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2f / n), py = Mathf.Sign(si) * Mathf.Pow(Mathf.Abs(si), 2f / n);
            return new Vector2(hw * px, (y0 + y1) / 2 + (y1 - y0) / 2 * py);
        }

        static void Body(CarMesh mb, Spec s)
        {
            const int slices = 48, around = 28;
            mb.UseSubmesh(0);
            var idx = new int[slices + 1, around];
            var centre = new Vector3[slices + 1];
            for (int i = 0; i <= slices; i++)
            {
                float x = s.length * i / slices, top = Profile(s.bodyTop, x), hw = HalfWidth(s, x);
                centre[i] = P(s, x, 0f, (s.clearance + top) / 2);
                for (int k = 0; k < around; k++)
                {
                    var r = Ring(Mathf.PI * 2f * k / around, hw, s.clearance, top, 5f);
                    idx[i, k] = mb.V(P(s, x, r.x, r.y));
                }
            }
            for (int i = 0; i < slices; i++)
                for (int k = 0; k < around; k++)
                {
                    int k2 = (k + 1) % around;
                    var q = (mb[idx[i, k]] + mb[idx[i + 1, k2]]) / 2;
                    mb.Q(idx[i, k], idx[i + 1, k], idx[i + 1, k2], idx[i, k2], q - (centre[i] + centre[i + 1]) / 2);
                }
            foreach (int i in new[] { 0, slices })   // end caps (flat)
            {
                Vector3 face = i == 0 ? Vector3.forward : Vector3.back;
                int c = mb.V(centre[i]);
                var cap = new int[around];
                for (int k = 0; k < around; k++) cap[k] = mb.V(mb[idx[i, k]]);
                for (int k = 0; k < around; k++) mb.T(c, cap[k], cap[(k + 1) % around], face);
            }
        }

        static void Cabin(CarMesh mb, Spec s)
        {
            float x0 = s.roof[0].x, x1 = s.roof[s.roof.Length - 1].x;
            const int slices = 30, around = 14;   // half ring from right (0) over the top to left (pi)
            float hwB = s.width / 2 - 0.08f;
            var idx = new int[slices + 1, around + 1];
            var local = new Vector2[slices + 1, around + 1];      // (fraction across 0..1 from the right, height fraction)
            var centre = new Vector3[slices + 1];
            for (int i = 0; i <= slices; i++)
            {
                float x = Mathf.Lerp(x0, x1, (float)i / slices);
                float rTop = Profile(s.roof, x), h = Mathf.Max(0.01f, rTop - s.beltline);
                centre[i] = P(s, x, 0f, s.beltline);
                for (int k = 0; k <= around; k++)
                {
                    var r = Ring(Mathf.PI * k / around, hwB, s.beltline - h, rTop, 4f);
                    float f = Mathf.Clamp01((r.y - s.beltline) / h);
                    float hx = r.x * Mathf.Lerp(1f, s.cabinTopHalf / hwB, f);   // tumblehome
                    idx[i, k] = mb.V(P(s, x, hx, Mathf.Max(r.y, s.beltline)));
                    local[i, k] = new Vector2((float)k / around, f);
                }
            }
            float wsTop = s.roof[1].x, rwTop = s.roof[s.roof.Length - 2].x;
            for (int i = 0; i < slices; i++)
                for (int k = 0; k < around; k++)
                {
                    float x = Mathf.Lerp(x0, x1, (i + 0.5f) / slices);
                    float across = (local[i, k].x + local[i, k + 1].x) / 2;                     // 0 right .. 1 left
                    float up = (local[i, k].y + local[i + 1, k].y) / 2;
                    bool side = across < 0.22f || across > 0.78f;
                    int sub;
                    if (x < wsTop) sub = across < 0.12f || across > 0.88f ? 2 : 1;           // windshield, A pillars
                    else if (x > rwTop) sub = across < 0.2f || across > 0.8f ? 0 : 1;        // rear window, thick C pillars
                    else if (!side) sub = 0;                                                  // roof
                    else if (up > 0.9f || up < 0.06f) sub = 2;                                // window frame trim
                    else if (Mathf.Abs(x - s.bPillar) < 0.07f) sub = 2;                       // B pillar
                    else if (x > rwTop - 0.4f && up > 0.25f) sub = 0;                         // C pillar ahead of the kink
                    else sub = 1;
                    mb.UseSubmesh(sub);
                    var q = (mb[idx[i, k]] + mb[idx[i + 1, k + 1]]) / 2;
                    mb.Q(idx[i, k], idx[i + 1, k], idx[i + 1, k + 1], idx[i, k + 1], q - (centre[i] + centre[i + 1]) / 2);
                }
        }

        static void Box(CarMesh mb, int sub, Vector3 c, Vector3 half) =>
            OBox(mb, sub, c, new Vector3(half.x, 0, 0), new Vector3(0, half.y, 0), new Vector3(0, 0, half.z));

        // oriented box from half-axes, six flat faces
        static void OBox(CarMesh mb, int sub, Vector3 c, Vector3 x, Vector3 y, Vector3 z)
        {
            mb.UseSubmesh(sub);
            foreach (var (n, u, w) in new[] { (x, y, z), (-x, y, z), (y, x, z), (-y, x, z), (z, x, y), (-z, x, y) })
                mb.Quad(c + n + u + w, c + n - u + w, c + n - u - w, c + n + u - w, n);
        }

        static void Disc(CarMesh mb, int sub, Vector3 c, Vector3 normal, float r, int seg = 18)
        {
            mb.UseSubmesh(sub);
            Vector3 a = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized, b = Vector3.Cross(normal, a);
            for (int i = 0; i < seg; i++)
            {
                float t0 = Mathf.PI * 2 * i / seg, t1 = Mathf.PI * 2 * (i + 1) / seg;
                mb.Tri(c, c + (a * Mathf.Cos(t1) + b * Mathf.Sin(t1)) * r, c + (a * Mathf.Cos(t0) + b * Mathf.Sin(t0)) * r, normal);
            }
        }

        static void Details(CarMesh mb, Spec s)
        {
            float zF = s.length / 2, zR = -s.length / 2;
            if (s.bmw)
                foreach (float side in new[] { -1f, 1f })
                {
                    // twin round headlamps each side in a black housing, kidney grille with chrome surround
                    Box(mb, 2, new Vector3(side * 0.56f, 0.665f, zF - 0.03f), new Vector3(0.2f, 0.07f, 0.03f));
                    Disc(mb, 4, new Vector3(side * 0.47f, 0.665f, zF + 0.005f), Vector3.forward, 0.058f);
                    Disc(mb, 4, new Vector3(side * 0.64f, 0.665f, zF + 0.002f), Vector3.forward, 0.058f);
                    Box(mb, 3, new Vector3(side * 0.095f, 0.665f, zF - 0.01f), new Vector3(0.075f, 0.075f, 0.02f));
                    Box(mb, 2, new Vector3(side * 0.095f, 0.665f, zF + 0.004f), new Vector3(0.06f, 0.062f, 0.012f));
                    // L-shaped taillights wrapping the corner
                    Box(mb, 5, new Vector3(side * 0.6f, 0.86f, zR + 0.01f), new Vector3(0.22f, 0.06f, 0.02f));
                    Box(mb, 5, new Vector3(side * 0.76f, 0.82f, zR + 0.07f), new Vector3(0.05f, 0.08f, 0.08f));
                }
            else
                foreach (float side in new[] { -1f, 1f })
                {
                    Box(mb, 4, new Vector3(side * 0.58f, Profile(s.bodyTop, 0.1f) - 0.05f, zF - 0.01f), new Vector3(0.17f, 0.05f, 0.02f));
                    Box(mb, 5, new Vector3(side * 0.62f, Profile(s.bodyTop, s.length - 0.1f) - 0.12f, zR + 0.01f), new Vector3(0.14f, 0.07f, 0.02f));
                }
            // black bumper strips, plates, mirrors, door seams and handles, exhaust
            Box(mb, 2, new Vector3(0f, 0.33f, zF - 0.02f), new Vector3(s.width * 0.4f, 0.05f, 0.03f));
            Box(mb, 2, new Vector3(0f, 0.33f, zR + 0.02f), new Vector3(s.width * 0.4f, 0.05f, 0.03f));
            Box(mb, 10, new Vector3(0f, 0.45f, zF + 0.0f), new Vector3(0.155f, 0.07f, 0.01f));
            Box(mb, 10, new Vector3(0f, 0.62f, zR - 0.0f), new Vector3(0.155f, 0.07f, 0.01f));
            foreach (float side in new[] { -1f, 1f })
            {
                Box(mb, 0, new Vector3(side * (s.width / 2 + 0.04f), s.beltline + 0.06f, zF - s.roof[0].x - 0.25f), new Vector3(0.06f, 0.05f, 0.1f));
                foreach (float x in new[] { s.bPillar - 0.02f, s.bPillar + 0.8f })
                    Box(mb, 2, new Vector3(side * (s.width / 2 + 0.002f), (s.clearance + s.beltline) / 2 + 0.05f, zF - x), new Vector3(0.004f, (s.beltline - s.clearance) / 2 - 0.08f, 0.006f));
                foreach (float x in new[] { s.bPillar - 0.5f, s.bPillar + 0.45f })
                    Box(mb, 3, new Vector3(side * (s.width / 2 + 0.005f), s.beltline - 0.1f, zF - x), new Vector3(0.01f, 0.012f, 0.06f));
            }
            Box(mb, 3, new Vector3(0.45f, 0.28f, zR + 0.01f), new Vector3(0.04f, 0.03f, 0.06f));
        }

        static void Interior(CarMesh mb, Spec s)
        {
            float z(float x) => s.length / 2 - x;
            Box(mb, 8, new Vector3(0f, 0.84f, z(1.45f)), new Vector3(0.74f, 0.1f, 0.18f));       // dash
            Box(mb, 8, new Vector3(0f, 0.6f, z(2.15f)), new Vector3(0.1f, 0.12f, 0.45f));        // centre console
            foreach (float side in new[] { -0.37f, 0.37f })
            {
                Box(mb, 8, new Vector3(side, 0.52f, z(2.45f)), new Vector3(0.24f, 0.06f, 0.25f)); // seat base
                Box(mb, 8, new Vector3(side, 0.85f, z(2.72f)), new Vector3(0.24f, 0.3f, 0.06f));  // backrest
            }
            Box(mb, 8, new Vector3(0f, 0.85f, z(3.35f)), new Vector3(0.66f, 0.3f, 0.07f));       // rear bench
            foreach (float dx in new[] { -0.09f, 0.09f }) Disc(mb, 9, new Vector3(-0.37f + dx, 0.92f, z(1.56f)), Vector3.back, 0.06f);   // gauges
            // steering wheel: a ring of short boxes, tilted towards the driver
            Vector3 c = new Vector3(-0.37f, 0.86f, z(1.82f));
            Vector3 n = Quaternion.Euler(-24f, 0f, 0f) * Vector3.back;
            Vector3 a = Vector3.right, b = Vector3.Cross(n, a).normalized;
            for (int i = 0; i < 20; i++)
            {
                float t0 = Mathf.PI * 2 * i / 20, t1 = Mathf.PI * 2 * (i + 1) / 20;
                Vector3 p0 = c + (a * Mathf.Cos(t0) + b * Mathf.Sin(t0)) * 0.18f, p1 = c + (a * Mathf.Cos(t1) + b * Mathf.Sin(t1)) * 0.18f;
                OBox(mb, 2, (p0 + p1) / 2, (p1 - p0) / 2, n * 0.01f, Vector3.Cross(p1 - p0, n).normalized * 0.011f);
            }
            Box(mb, 2, new Vector3(0f, 1.25f, z(1.9f)), new Vector3(0.1f, 0.025f, 0.01f));      // rear-view mirror
        }

        // tyre (axis along X), rim face with 5 spokes on the outside (-X: left wheels; right wheels are turned round)
        static void Wheel(CarMesh mb, Spec s)
        {
            const int seg = 28;
            float r = s.wheelR, w = s.tyreW / 2, rr = r * 0.66f;
            mb.UseSubmesh(6);
            var outer = new int[seg]; var inner = new int[seg];
            for (int i = 0; i < seg; i++)
            {
                float t = Mathf.PI * 2 * i / seg;
                Vector3 d = new Vector3(0, Mathf.Cos(t), Mathf.Sin(t));
                outer[i] = mb.V(d * r + Vector3.right * w); inner[i] = mb.V(d * r - Vector3.right * w);
            }
            for (int i = 0; i < seg; i++)
            {
                int j = (i + 1) % seg;
                float tm = Mathf.PI * 2 * (i + 0.5f) / seg;
                mb.Q(outer[i], outer[j], inner[j], inner[i], new Vector3(0, Mathf.Cos(tm), Mathf.Sin(tm)));
                Vector3 d0 = new Vector3(0, Mathf.Cos(Mathf.PI * 2 * i / seg), Mathf.Sin(Mathf.PI * 2 * i / seg));
                Vector3 d1 = new Vector3(0, Mathf.Cos(Mathf.PI * 2 * j / seg), Mathf.Sin(Mathf.PI * 2 * j / seg));
                foreach (float sx in new[] { -1f, 1f })   // sidewalls
                {
                    Vector3 o = Vector3.right * w * sx;
                    mb.Quad(d0 * rr + o, d1 * rr + o, d1 * r + o, d0 * r + o, Vector3.right * sx);
                }
            }
            Disc(mb, 7, Vector3.left * (w - 0.03f), Vector3.left, rr, seg);
            Disc(mb, 2, Vector3.left * (w - 0.05f), Vector3.left, rr * 0.98f, seg);
            for (int k = 0; k < 5; k++)
            {
                float t = Mathf.PI * 2 * k / 5;
                Vector3 dir = new Vector3(0, Mathf.Cos(t), Mathf.Sin(t));
                OBox(mb, 7, Vector3.left * (w - 0.01f) + dir * rr * 0.5f, dir * rr * 0.48f, Vector3.right * 0.015f, Vector3.Cross(dir, Vector3.right) * 0.03f);
            }
            Disc(mb, 3, Vector3.left * (w + 0.005f), Vector3.left, 0.05f, 12);
        }
    }
}
