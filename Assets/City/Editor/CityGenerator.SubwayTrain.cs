using System.Collections.Generic;
using LifeInDowntown.Transit;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace LifeInDowntown.City
{
    // The Toronto Rocket, generated (docs/DESIGN.md 3N.6, 3N.10 task 5): 6 cars permanently coupled with open
    // gangways, end cars 23.19 m, middle cars 22.86 m, 3.137 m wide, 3.645 m tall; stainless sides with a black
    // window band; 4 doors per side per car (1.5 m). Inside: white walls, grey floor, longitudinal seats
    // (seat colour unverified: neutral placeholder), stainless poles, LED strips, a strip map over each door.
    // Two trains: northbound ("1 FINCH") and southbound ("1 VMC").
    public static partial class CityGenerator
    {
        static readonly float[] CarLengths = { 23.19f, 22.86f, 22.86f, 22.86f, 22.86f, 23.19f };
        const float CarHalfW = 1.5685f, Inner = 1.42f, SkinIn = 1.50f, Floor = 1.05f, Ceil = 3.2f, RoofEdge = 3.3f, RoofTop = 3.645f;
        const float WinLo = 1.9f, WinHi = 2.7f, DoorTop = 3.0f, DoorHalf = 0.75f;

        class TrainMats { public Material steel, glass, inside, floor, seat, led, head, tail, signs, rubber, dark; }

        static void BuildTrains(Transform root, SubwayMats sm, List<Mesh> meshes)
        {
            var tm = new TrainMats
            {
                steel = Materials.Plain("TrainStainless", new Color(0.72f, 0.73f, 0.74f), 0.55f, 0.95f, Color.black, 0f),
                glass = Materials.Plain("TrainWindow", new Color(0.02f, 0.025f, 0.03f), 0.92f, 0f, Color.black, 0f),
                inside = Materials.Plain("TrainInterior", new Color(0.88f, 0.88f, 0.86f), 0.35f, 0f, Color.black, 0f),
                floor = Materials.Plain("TrainFloor", new Color(0.36f, 0.36f, 0.37f), 0.3f, 0f, Color.black, 0f),
                seat = Materials.Plain("TrainSeat", new Color(0.16f, 0.2f, 0.3f), 0.3f, 0f, Color.black, 0f),   // placeholder (3N.9)
                led = sm.strip,
                head = Materials.Plain("TrainHeadlight", Color.white, 0.5f, 0f, new Color(1f, 0.97f, 0.9f), 3000f),
                tail = Materials.Plain("TrainTaillight", new Color(0.6f, 0.05f, 0.05f), 0.5f, 0f, new Color(1f, 0.1f, 0.08f), 600f),
                signs = sm.signs, rubber = sm.rubber, dark = sm.dark,
            };
            var mats = new[] { tm.steel, tm.glass, tm.inside, tm.floor, tm.seat, tm.led, tm.head, tm.tail, tm.signs, tm.rubber, tm.dark };
            var leafMesh = LeafMesh(meshes);
            var riders = new List<GameObject>();
            var cache = new Dictionary<string, Mesh>();
            foreach (int dir in new[] { 1, -1 })
            {
                var train = new GameObject(dir > 0 ? "Train_NB" : "Train_SB");
                train.transform.SetParent(root, false);
                var ctl = train.AddComponent<TrainController>();
                ctl.dir = dir;
                var cars = new List<TrainCar>();
                float offset = 0f;
                for (int k = 0; k < CarLengths.Length; k++)
                {
                    float L = CarLengths[k];
                    bool front = k == 0, back = k == CarLengths.Length - 1;
                    string key = $"{L}_{front}_{back}_{dir}";
                    if (!cache.TryGetValue(key, out var mesh))
                    {
                        mesh = CarMesh(L, front, back, dir > 0 ? "route_finch" : "route_vmc", sm);
                        mesh.name = $"TrainCar_{(front ? "front" : back ? "back" : "middle")}_{(dir > 0 ? "NB" : "SB")}";
                        cache[key] = mesh; meshes.Add(mesh);
                    }
                    var car = new GameObject($"Car_{k + 1}");
                    car.transform.SetParent(train.transform, false);
                    car.AddComponent<MeshFilter>().sharedMesh = mesh;
                    car.AddComponent<MeshRenderer>().sharedMaterials = mats;
                    var rb = car.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false; rb.interpolation = RigidbodyInterpolation.None;
                    var tc = car.AddComponent<TrainCar>();
                    tc.length = L; tc.offsetFromHead = offset + L / 2f;
                    offset += L;
                    CarColliders(car.transform, L, front, back);
                    // the platform-side door leaves (right, +x) are separate so they can slide
                    float[] dz = DoorZs(L);
                    tc.doorZ = dz;
                    var leaves = new List<Transform>(); var closed = new List<Vector3>(); var slide = new List<float>();
                    foreach (float z in dz)
                        foreach (int s in new[] { -1, 1 })
                        {
                            var leaf = new GameObject("DoorLeaf");
                            leaf.transform.SetParent(car.transform, false);
                            var pos = new Vector3(1.535f, (Floor + DoorTop) / 2f, z + s * DoorHalf / 2f);
                            leaf.transform.localPosition = pos;
                            leaf.AddComponent<MeshFilter>().sharedMesh = leafMesh;
                            leaf.AddComponent<MeshRenderer>().sharedMaterials = new[] { tm.steel, tm.glass };
                            var bc = leaf.AddComponent<BoxCollider>(); bc.size = new Vector3(0.06f, DoorTop - Floor, DoorHalf);
                            leaves.Add(leaf.transform); closed.Add(pos); slide.Add(s);
                        }
                    tc.rightLeaves = leaves.ToArray(); tc.rightClosed = closed.ToArray(); tc.rightSlide = slide.ToArray();
                    // two ceiling lights per car; headlights on the leading cab
                    foreach (float z in new[] { -L / 4f, L / 4f })
                    {
                        var lg = new GameObject("CarLight"); lg.transform.SetParent(car.transform, false);
                        lg.transform.localPosition = new Vector3(0f, Ceil - 0.25f, z);
                        var l = lg.AddComponent<Light>(); lg.AddComponent<HDAdditionalLightData>();
                        l.type = LightType.Point; l.lightUnit = UnityEngine.Rendering.LightUnit.Lumen; l.intensity = 900f; l.range = 6f;
                        l.useColorTemperature = true; l.colorTemperature = 5200f; l.shadows = LightShadows.None;
                    }
                    if (front)
                    {
                        var hl = new GameObject("Headlights"); hl.transform.SetParent(car.transform, false);
                        hl.transform.localPosition = new Vector3(0f, 1.4f, L / 2f + 0.4f);
                        var l = hl.AddComponent<Light>(); hl.AddComponent<HDAdditionalLightData>();
                        l.type = LightType.Spot; l.lightUnit = UnityEngine.Rendering.LightUnit.Lumen; l.intensity = 6000f; l.range = 70f;
                        l.spotAngle = 50f; l.innerSpotAngle = 25f; l.useColorTemperature = true; l.colorTemperature = 5600f; l.shadows = LightShadows.None;
                    }
                    cars.Add(tc);
                }
                ctl.cars = cars.ToArray();
                ctl.trainLength = offset;
                ctl.Init();      // parks it at its hold point in the tunnel
                AddTrainRiders(ctl, riders, dir > 0 ? 11 : 23);
            }
            AddPlatformRiders(root, riders);
            var rc = root.gameObject.AddComponent<SubwayRiders>();
            rc.riders = riders.ToArray();
            rc.Apply(Vector3.zero);     // saved switched off: the game starts on the street
            Debug.Log($"TRAIN_OK trains=2 cars={CarLengths.Length} length={SumLengths():F2} m width={2 * CarHalfW:F3} m height={RoofTop} m doors/side/car=4");
        }

        static List<GameObject> RiderPrefabs()
        {
            var list = new List<GameObject>();
            string dir = LifeInDowntown.EditorTools.CharacterImport.Root + "/Prefabs";
            if (!System.IO.Directory.Exists(dir)) return list;
            foreach (var f in System.IO.Directory.GetFiles(dir, "*.prefab"))
                if (!System.IO.Path.GetFileName(f).StartsWith("Police_"))
                    list.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(f.Replace('\\', '/')));
            return list;
        }

        static GameObject Rider(List<GameObject> prefabs, System.Random rnd, Transform parent, Vector3 localPos, Vector3 localFace, LifeInDowntown.NPC.Pedestrian.Mode mode)
        {
            var go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefabs[rnd.Next(prefabs.Count)], parent);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.LookRotation(localFace);
            var p = go.GetComponent<LifeInDowntown.NPC.Pedestrian>();
            if (p != null) { p.mode = mode; p.path = null; p.watchPlayer = false; }
            return go;
        }

        // a few people per train: sitting on the side seats, standing by the doors on their phones
        static void AddTrainRiders(TrainController ctl, List<GameObject> riders, int seed)
        {
            var prefabs = RiderPrefabs();
            if (prefabs.Count == 0) return;
            var rnd = new System.Random(seed);
            foreach (var car in ctl.cars)
            {
                int n = 1 + rnd.Next(2);
                for (int i = 0; i < n; i++)
                {
                    int s = rnd.NextDouble() < 0.5 ? -1 : 1;
                    int k = rnd.Next(3);         // the seat run between doors k and k+1
                    float z = (car.doorZ[k] + car.doorZ[k + 1]) * 0.5f + (float)(rnd.NextDouble() - 0.5) * 1.6f;
                    if (rnd.NextDouble() < 0.7)   // seated, facing across the car
                        riders.Add(Rider(prefabs, rnd, car.transform, new Vector3(s * (Inner - 0.5f), Floor, z), new Vector3(-s, 0, 0), LifeInDowntown.NPC.Pedestrian.Mode.Sit));
                    else                          // standing near a door, on the phone
                        riders.Add(Rider(prefabs, rnd, car.transform, new Vector3(s * 0.75f, Floor, car.doorZ[rnd.Next(4)] + 1.1f), new Vector3(0, 0, s), LifeInDowntown.NPC.Pedestrian.Mode.Phone));
                }
            }
        }

        // people waiting on every platform, back to the wall
        static void AddPlatformRiders(Transform root, List<GameObject> riders)
        {
            var prefabs = RiderPrefabs();
            if (prefabs.Count == 0) return;
            var rnd = new System.Random(416);
            foreach (var st in root.GetComponentsInChildren<SubwayStation>())
                foreach (int side in new[] { -1, 1 })
                    for (int i = 0; i < 3; i++)
                    {
                        float u = -55f + 50f * i + (float)rnd.NextDouble() * 12f;
                        var pos = st.W(u, side * 6.3f, PlatFloor);
                        var go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefabs[rnd.Next(prefabs.Count)], st.transform);
                        go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(st.Right * -side));
                        var p = go.GetComponent<LifeInDowntown.NPC.Pedestrian>();
                        if (p != null) { p.mode = rnd.NextDouble() < 0.5 ? LifeInDowntown.NPC.Pedestrian.Mode.Phone : LifeInDowntown.NPC.Pedestrian.Mode.Waiting; p.path = null; p.watchPlayer = false; }
                        riders.Add(go);
                    }
        }

        static float SumLengths() { float s = 0; foreach (var l in CarLengths) s += l; return s; }

        static float[] DoorZs(float L) => new[] { -L * 0.375f, -L * 0.125f, L * 0.125f, L * 0.375f };

        // car-local box with metre UVs (x right, y up, z forward)
        static void LBox(MeshBuilder mb, int sub, float x0, float x1, float y0, float y1, float z0, float z1)
        {
            mb.UseSubmesh(sub);
            var c = new Vector3((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2);
            Box(mb, c, Vector3.right * Mathf.Abs(x1 - x0) / 2, Vector3.up * Mathf.Abs(y1 - y0) / 2, Vector3.forward * Mathf.Abs(z1 - z0) / 2);
        }

        // submeshes: 0 steel, 1 glass, 2 interior, 3 floor, 4 seat, 5 LED, 6 headlight, 7 taillight, 8 signs, 9 rubber, 10 dark
        static Mesh CarMesh(float L, bool front, bool back, string routeKey, SubwayMats sm)
        {
            var mb = new MeshBuilder();
            float h = L / 2f;
            float[] dz = DoorZs(L);
            float zIn0 = back ? -h + 1.8f : -h, zIn1 = front ? h - 1.8f : h;      // passenger space (cabs at the ends)

            LBox(mb, 10, -1.3f, 1.3f, 0.3f, 0.95f, -h + 0.3f, h - 0.3f);           // underframe
            foreach (float bz in new[] { -h + 3.2f, h - 3.2f }) LBox(mb, 10, -1.15f, 1.15f, 0.08f, 0.75f, bz - 1.3f, bz + 1.3f);   // trucks
            LBox(mb, 3, -CarHalfW, CarHalfW, 0.95f, Floor, -h, h);                // floor

            // sides: z runs between the door openings (left side doors are part of the body, right side leaves move)
            var runs = new List<(float, float)>();
            float zPrev = -h;
            foreach (float d in dz) { runs.Add((zPrev, d - DoorHalf)); zPrev = d + DoorHalf; }
            runs.Add((zPrev, h));
            foreach (int s in new[] { -1, 1 })
            {
                foreach (var (z0, z1) in runs)
                {
                    // outer skin: stainless below and above the window band, black glass in it
                    LBox(mb, 0, s * SkinIn, s * CarHalfW, 0.75f, WinLo, z0, z1);
                    LBox(mb, 1, s * (SkinIn + 0.01f), s * (CarHalfW - 0.005f), WinLo, WinHi, z0, z1);
                    LBox(mb, 0, s * SkinIn, s * CarHalfW, WinHi, RoofEdge, z0, z1);
                    // inner panel: white, the same window band inside
                    LBox(mb, 2, s * Inner, s * SkinIn, Floor, WinLo, z0, z1);
                    LBox(mb, 1, s * Inner, s * SkinIn - s * 0.01f, WinLo, WinHi, z0, z1);
                    LBox(mb, 2, s * Inner, s * SkinIn, WinHi, Ceil, z0, z1);
                }
                foreach (float d in dz)
                {
                    LBox(mb, 0, s * SkinIn, s * CarHalfW, DoorTop, RoofEdge, d - DoorHalf, d + DoorHalf);   // door headers
                    LBox(mb, 2, s * Inner, s * SkinIn, DoorTop, Ceil, d - DoorHalf, d + DoorHalf);
                    if (s < 0)    // the far-side (left) doors stay shut: built into the body
                    {
                        LBox(mb, 0, -1.56f, -1.51f, Floor, DoorTop, d - DoorHalf, d + DoorHalf);
                        LBox(mb, 1, -1.565f, -1.505f, WinLo, WinHi - 0.1f, d - DoorHalf + 0.15f, d - 0.15f);
                        LBox(mb, 1, -1.565f, -1.505f, WinLo, WinHi - 0.1f, d + 0.15f, d + DoorHalf - 0.15f);
                    }
                    // strip map over each door, inside
                    mb.UseSubmesh(8);
                    SignQuadLocal(mb, sm.sign["stripmap"], new Vector3(s * (Inner - 0.01f), 2.86f, d), Vector3.right * -s, 1.4f, 0.11f);
                    // vertical poles beside each door
                    foreach (float pz in new[] { d - DoorHalf - 0.15f, d + DoorHalf + 0.15f })
                        LBox(mb, 0, s * 1.15f - 0.02f, s * 1.15f + 0.02f, Floor, Ceil, pz - 0.02f, pz + 0.02f);
                }
                // longitudinal seats in each run inside the passenger space, with a backrest on the wall
                foreach (var (z0r, z1r) in runs)
                {
                    float z0 = Mathf.Max(z0r + 0.25f, zIn0 + 0.1f), z1 = Mathf.Min(z1r - 0.25f, zIn1 - 0.1f);
                    if (z1 - z0 < 1.2f) continue;
                    LBox(mb, 10, s * (Inner - 0.42f), s * Inner, Floor, 1.45f, z0, z1);
                    LBox(mb, 4, s * (Inner - 0.46f), s * Inner, 1.45f, 1.53f, z0, z1);
                    LBox(mb, 4, s * (Inner - 0.08f), s * Inner, 1.53f, 2.0f, z0, z1);
                }
                // roof: rounded, six facets each side
                mb.UseSubmesh(0);
                for (int i = 0; i < 6; i++)
                {
                    float a0 = i / 6f * Mathf.PI / 2f, a1 = (i + 1) / 6f * Mathf.PI / 2f;
                    var p0 = new Vector3(s * CarHalfW * Mathf.Cos(a0), RoofEdge + (RoofTop - RoofEdge) * Mathf.Sin(a0), 0f);
                    var p1 = new Vector3(s * CarHalfW * Mathf.Cos(a1), RoofEdge + (RoofTop - RoofEdge) * Mathf.Sin(a1), 0f);
                    var n = Vector3.Cross(p1 - p0, Vector3.forward * s).normalized;
                    if (n.y < 0f) n = -n;
                    mb.Quad(p0 + Vector3.back * h, p1 + Vector3.back * h, p1 + Vector3.forward * h, p0 + Vector3.forward * h, n,
                            Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                }
            }
            LBox(mb, 2, -Inner, Inner, Ceil, Ceil + 0.05f, -h, h);                // ceiling
            foreach (float x in new[] { -0.55f, 0.55f }) LBox(mb, 5, x - 0.07f, x + 0.07f, Ceil - 0.03f, Ceil, zIn0 + 0.5f, zIn1 - 0.5f);   // LED strips
            foreach (float x in new[] { -0.75f, 0.75f }) LBox(mb, 0, x - 0.015f, x + 0.015f, 2.02f, 2.05f, zIn0 + 0.5f, zIn1 - 0.5f);    // grab bars

            // ends: open gangway (rubber frame) between cars, a cab with a windscreen and route sign at the train ends
            foreach (int e in new[] { -1, 1 })
            {
                bool cab = (e > 0 && front) || (e < 0 && back);
                float ze = e * h;
                if (!cab)
                {
                    LBox(mb, 2, -Inner, -0.9f, Floor, Ceil, ze - e * 0.12f, ze);
                    LBox(mb, 2, 0.9f, Inner, Floor, Ceil, ze - e * 0.12f, ze);
                    LBox(mb, 2, -0.9f, 0.9f, 3.05f, Ceil, ze - e * 0.12f, ze);
                    LBox(mb, 9, -1.45f, -0.9f, 0.95f, 3.3f, ze - 0.06f, ze + 0.06f);
                    LBox(mb, 9, 0.9f, 1.45f, 0.95f, 3.3f, ze - 0.06f, ze + 0.06f);
                    LBox(mb, 9, -0.9f, 0.9f, 3.05f, 3.3f, ze - 0.06f, ze + 0.06f);
                    continue;
                }
                float zc = ze - e * 1.8f;                                         // cab back wall (passenger side)
                LBox(mb, 2, -Inner, Inner, Floor, Ceil, zc - 0.05f, zc + 0.05f);
                LBox(mb, 0, -CarHalfW, CarHalfW, 0.75f, 1.75f, ze - e * 0.02f, ze + e * 0.25f);   // nose below the windscreen
                LBox(mb, 1, -1.35f, 1.35f, 1.75f, 2.85f, ze, ze + e * 0.18f);                 // windscreen
                LBox(mb, 0, -CarHalfW, CarHalfW, 2.85f, RoofEdge, ze - e * 0.02f, ze + e * 0.12f);
                LBox(mb, 0, -CarHalfW, -1.35f, 1.75f, 2.85f, ze - e * 0.02f, ze + e * 0.18f);
                LBox(mb, 0, 1.35f, CarHalfW, 1.75f, 2.85f, ze - e * 0.02f, ze + e * 0.18f);
                bool leading = e > 0;                                             // trains only ever run cab-first
                foreach (float x in new[] { -1.05f, 1.05f })
                    LBox(mb, leading ? 6 : 7, x - 0.17f, x + 0.17f, 1.2f, 1.36f, ze + e * 0.25f, ze + e * 0.27f);
                mb.UseSubmesh(8);
                SignQuadLocal(mb, sm.sign[routeKey], new Vector3(0f, 2.6f, ze + e * 0.19f), Vector3.forward * e, 0.9f, 0.22f);
            }
            return mb.ToMesh("TrainCar");
        }

        static void SignQuadLocal(MeshBuilder mb, Rect uv, Vector3 p, Vector3 n, float w, float h) => SignQuad(mb, uv, p, n, w, h);

        // one door leaf (0.75 m x 1.95 m): stainless with a window, local origin at its centre
        static Mesh LeafMesh(List<Mesh> meshes)
        {
            var mb = new MeshBuilder();
            float hh = (DoorTop - Floor) / 2f;
            LBox(mb, 0, -0.03f, 0.03f, -hh, hh, -DoorHalf / 2f, DoorHalf / 2f);
            float wy0 = WinLo - (Floor + hh) - 0.05f, wy1 = WinHi - (Floor + hh) - 0.1f;
            LBox(mb, 1, -0.035f, 0.035f, wy0, wy1, -DoorHalf / 2f + 0.1f, DoorHalf / 2f - 0.1f);
            var m = mb.ToMesh("TrainDoorLeaf");
            meshes.Add(m);
            return m;
        }

        // collision: floor, roof, walls between the doors (right side: the leaves close the openings), cab walls,
        // gangway end panels (the seats don't block: you can stand in front of them)
        static void CarColliders(Transform car, float L, bool front, bool back)
        {
            var go = new GameObject("Colliders");
            go.transform.SetParent(car, false);
            float h = L / 2f;
            void B(float x0, float x1, float y0, float y1, float z0, float z1)
            {
                var b = go.AddComponent<BoxCollider>();
                b.center = new Vector3((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2);
                b.size = new Vector3(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), Mathf.Abs(z1 - z0));
            }
            B(-CarHalfW, CarHalfW, 0.85f, Floor, -h, h);
            B(-CarHalfW, CarHalfW, Ceil, RoofTop, -h, h);
            B(-CarHalfW, -Inner, Floor, Ceil, -h, h);                         // left: closed all along
            float zPrev = -h;
            foreach (float d in DoorZs(L)) { B(Inner, CarHalfW, Floor, Ceil, zPrev, d - DoorHalf); zPrev = d + DoorHalf; }
            B(Inner, CarHalfW, Floor, Ceil, zPrev, h);
            foreach (int e in new[] { -1, 1 })
            {
                bool cab = (e > 0 && front) || (e < 0 && back);
                float ze = e * h;
                if (cab) B(-CarHalfW, CarHalfW, Floor, Ceil, ze - e * 1.85f, ze + e * 0.25f);
                else { B(-Inner, -0.9f, Floor, Ceil, ze - e * 0.12f, ze); B(0.9f, Inner, Floor, Ceil, ze - e * 0.12f, ze); }
            }
        }
    }
}
