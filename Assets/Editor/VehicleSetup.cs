using System.Collections.Generic;
using LifeInDowntown.City;
using UnityEditor;
using UnityEngine;

namespace LifeInDowntown.EditorTools
{
    // Cars in the scene (docs/DESIGN.md 3O.4): builds Tarik's black E46 from CarBuilder and parks it in his
    // bay P1-17 under Pantages. Called by CityGenerator.Generate after the garage exists.
    public static class VehicleSetup
    {
        public const string TarikCar = "Car_E46_Tarik";

        public static void Setup()
        {
            var old = GameObject.Find("Vehicles");
            if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject("Vehicles").transform;
            var prefab = CarBuilder.BuildPrefab(CarBuilder.E46(), new Color(0.015f, 0.016f, 0.018f), TarikCar, drivable: true);
            var spot = GameObject.Find($"PantagesGarage/GarageSpot_{CityGenerator.TarikBay}");
            if (spot == null) { Debug.LogWarning("CARS_WARN no garage spot for Tarik's car"); return; }
            var car = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            car.name = "TarikCar";
            var pose = LifeInDowntown.Vehicles.CarPersistence.HomePose(spot.transform, out var rot);
            car.transform.SetPositionAndRotation(pose, rot);
            car.GetComponent<LifeInDowntown.Vehicles.CarController>().isPlayerCar = true;
            car.AddComponent<LifeInDowntown.Vehicles.CarPersistence>().homeSpot = spot.transform;

            // the roll-up doors are drive-through gates (fade), one each way
            var g = GameObject.Find("PantagesGarage");
            void Gate(string at, string to, string prompt)
            {
                var a = g.transform.Find(at); var b = g.transform.Find(to);
                if (a == null || b == null) return;
                var gate = a.gameObject.AddComponent<LifeInDowntown.World.GarageGate>();
                gate.prompt = prompt; gate.exit = b; gate.radius = 4f;
            }
            Gate("GarageGate_Street", "GarageGate_P1_Exit", "Drive into P1 Parking");
            Gate("GarageGate_P1", "GarageGate_Street_Exit", "Drive out onto Shuter St");
            int parked = ParkedCars(root, spot.transform);
            Debug.Log($"CARS_OK tarik at {car.transform.position} parked={parked}");
        }

        // ---------------------------------------------------------------- parked cars (DESIGN 3O.5)
        static readonly (string name, Color c, float w)[] Paints =
        {
            ("Grey", new Color(0.22f, 0.23f, 0.24f), 25), ("Black", new Color(0.02f, 0.02f, 0.025f), 20), ("White", new Color(0.82f, 0.82f, 0.8f), 20),
            ("Silver", new Color(0.55f, 0.56f, 0.58f), 15), ("Blue", new Color(0.05f, 0.12f, 0.32f), 8), ("Red", new Color(0.42f, 0.03f, 0.03f), 7),
            ("Green", new Color(0.05f, 0.14f, 0.1f), 5),
        };

        const int MaxStreet = 240;

        static int ParkedCars(Transform root, Transform tarikSpot)
        {
            var data = JsonUtility.FromJson<CityData>(System.IO.File.ReadAllText("Assets/City/Data/downtown_yonge_dundas.json"));
            var types = new (CarBuilder.Spec spec, float w)[] { (CarBuilder.Sedan(), 40), (CarBuilder.Hatch(), 20), (CarBuilder.Suv(), 28), (CarBuilder.Minivan(), 7), (CarBuilder.Van(), 5) };
            var prefabs = new GameObject[types.Length];
            for (int i = 0; i < types.Length; i++) prefabs[i] = CarBuilder.BuildPrefab(types[i].spec, Paints[0].c, "Car_" + types[i].spec.name, drivable: false);
            var paint = new Material[Paints.Length];
            for (int i = 0; i < Paints.Length; i++) paint[i] = CityGenerator.Materials.Plain("Car_Paint_" + Paints[i].name, Paints[i].c, 0.82f, 0.5f, Color.black, 0f);
            var rnd = new System.Random(905);
            int Pick(float[] w) { float x = (float)rnd.NextDouble() * System.Linq.Enumerable.Sum(w); for (int i = 0; i < w.Length; i++) { x -= w[i]; if (x <= 0) return i; } return w.Length - 1; }
            float[] tw = System.Array.ConvertAll(types, t => t.w), pw = System.Array.ConvertAll(Paints, x => x.w);

            // keep clear of: junctions, the garage street door, subway entrances, the lobby and Rabba fronts
            var avoid = new List<(Vector2 p, float r)>();
            foreach (var j in CityGenerator.FindJunctions(data)) avoid.Add((j.p, j.clear + 7f));
            var gate = GameObject.Find("PantagesGarage/GarageGate_Street");
            if (gate != null) avoid.Add((new Vector2(gate.transform.position.x, gate.transform.position.z), 12f));
            foreach (var site in CityGenerator.ShopSites(data)) { var w = site.W(0f, -2f, 0f); avoid.Add((new Vector2(w.x, w.z), 7f)); }
            var ttc = System.IO.File.Exists("Assets/City/Data/ttc_line1.json") ? JsonUtility.FromJson<TtcEntrances>(System.IO.File.ReadAllText("Assets/City/Data/ttc_line1.json")) : null;
            if (ttc != null) foreach (var e in ttc.entrances) avoid.Add((new Vector2(e.x, e.z), 8f));
            var footprints = new List<List<Vector2>>();
            foreach (var b in data.buildings) footprints.Add(CityDataUtil.Points(b.pts));

            var streets = new HashSet<string> { "residential", "tertiary", "unclassified", "secondary" };
            int placed = 0;
            var spots = new List<(Vector2 c, Vector2 face)>();
            var pr = new GameObject("ParkedCars").transform; pr.SetParent(root, false);
            foreach (var r in data.roads)
            {
                if (!streets.Contains(r.type) || (r.name != null && (r.name.Contains("Yonge") || r.name.Contains("Dundas")))) continue;
                var pts = CityDataUtil.Points(r.pts);
                foreach (int side in new[] { 1, -1 })
                {
                    float carry = (float)rnd.NextDouble() * 6f;
                    for (int i = 0; i + 1 < pts.Count; i++)
                    {
                        Vector2 a = pts[i], b = pts[i + 1];
                        float len = (b - a).magnitude;
                        if (len < 1f) continue;
                        Vector2 dir = (b - a) / len, right = new Vector2(dir.y, -dir.x);
                        for (float t = carry; t < len; t += 6.5f + (float)rnd.NextDouble() * 1.5f)
                        {
                            carry = 0f;
                            if (rnd.NextDouble() < 0.28) continue;                                  // gaps
                            Vector2 c = a + dir * t + right * side * (r.width / 2 - 1.15f);
                            if (avoid.Exists(x => (x.p - c).sqrMagnitude < x.r * x.r)) continue;
                            if (footprints.Exists(f => CityGenerator.PointInPolygon(c, f))) continue;
                            Vector2 face = side > 0 ? dir : -dir;                                    // drive on the right: kerb side faces traffic's way
                            spots.Add((c, face));
                        }
                    }
                }
            }
            // every valid kerb spot, then a seeded random MaxStreet of them (the bigger map has ~1,600)
            for (int i = spots.Count - 1; i > 0; i--) { int k = rnd.Next(i + 1); (spots[i], spots[k]) = (spots[k], spots[i]); }
            for (int i = 0; i < Mathf.Min(MaxStreet, spots.Count); i++)
            {
                var (c, face) = spots[i];
                var car = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[Pick(tw)], pr);
                car.transform.SetPositionAndRotation(new Vector3(c.x, 0.02f, c.y), Quaternion.LookRotation(new Vector3(face.x, 0f, face.y)));
                Paint(car, paint[Pick(pw)]);
                placed++;
            }
            Debug.Log($"CARS_SPOTS {spots.Count} kerb spots, {placed} used");
            StreetExtras(root, spots, rnd);
            // residents' cars in some P1 bays (not Tarik's)
            var garage = GameObject.Find("PantagesGarage");
            if (garage != null)
                for (int i = 0; i < 24; i++)
                {
                    var bay = garage.transform.Find($"GarageSpot_P1-{i + 1:00}");
                    if (bay == null || bay == tarikSpot || rnd.NextDouble() > 0.4) continue;
                    var car = (GameObject)PrefabUtility.InstantiatePrefab(prefabs[Pick(tw)], pr);
                    var pose = LifeInDowntown.Vehicles.CarPersistence.HomePose(bay, out var rot);
                    car.transform.SetPositionAndRotation(pose, rot);
                    Paint(car, paint[Pick(pw)]);
                    placed++;
                }
            return placed;
        }

        // detail on the parked-car streets (DESIGN 3O.7): Green P pay-and-display machines on the sidewalk beside
        // parked cars, Bike Share Toronto docks in the curb lane (where Toronto puts them), litter on the sidewalks
        static void StreetExtras(Transform root, List<(Vector2 c, Vector2 face)> spots, System.Random rnd)
        {
            var greenP = CityGenerator.Materials.Plain("Street_GreenP", new Color(0.04f, 0.28f, 0.14f), 0.5f, 0.3f, Color.black, 0f);
            var screen = CityGenerator.Materials.Plain("Street_GreenPScreen", Color.black, 0.6f, 0f, new Color(0.6f, 0.9f, 0.7f), 6f);
            var dock = CityGenerator.Materials.Plain("Street_BikeDock", new Color(0.18f, 0.19f, 0.2f), 0.5f, 0.7f, Color.black, 0f);
            var bikeGreen = CityGenerator.Materials.Plain("Street_BikeGreen", new Color(0.1f, 0.42f, 0.2f), 0.55f, 0.2f, Color.black, 0f);
            var tyre = CityGenerator.Materials.Plain("Car_Tyre", new Color(0.04f, 0.04f, 0.045f), 0.35f, 0f, Color.black, 0f);
            var paper = CityGenerator.Materials.Plain("Street_Litter", new Color(0.75f, 0.73f, 0.68f), 0.2f, 0f, Color.black, 0f);
            var cupRed = CityGenerator.Materials.Plain("Street_CupRed", new Color(0.6f, 0.08f, 0.06f), 0.4f, 0f, Color.black, 0f);   // the double-double cup
            var ex = new GameObject("StreetExtras").transform; ex.SetParent(root, false);
            GameObject Part(PrimitiveType t, Material m, Transform parent, Vector3 lp, Vector3 scale, Quaternion? r = null, bool col = false)
            {
                var g = GameObject.CreatePrimitive(t);
                if (!col) Object.DestroyImmediate(g.GetComponent<Collider>());
                g.transform.SetParent(parent, false);
                g.transform.localPosition = lp; g.transform.localScale = scale; g.transform.localRotation = r ?? Quaternion.identity;
                g.GetComponent<MeshRenderer>().sharedMaterial = m;
                return g;
            }
            Vector3 W(Vector2 c, float y) => new Vector3(c.x, y, c.y);
            int machines = 0, docks = 0, litter = 0;
            // pay machines: every so many parked cars, 1.6 m in from the curb line onto the sidewalk
            for (int i = 0; i < Mathf.Min(MaxStreet, spots.Count); i += 9)
            {
                var (c, face) = spots[i];
                Vector2 kerbOut = new Vector2(face.y, -face.x);   // the parked car's right = towards the kerb
                var m = new GameObject("GreenP_Machine").transform; m.SetParent(ex, false);
                m.SetPositionAndRotation(W(c + kerbOut * 2.4f, 0.15f), Quaternion.LookRotation(W(-kerbOut, 0f)));
                Part(PrimitiveType.Cube, greenP, m, new Vector3(0f, 0.75f, 0f), new Vector3(0.34f, 1.5f, 0.28f), null, true);
                Part(PrimitiveType.Cube, screen, m, new Vector3(0f, 1.2f, 0.145f), new Vector3(0.2f, 0.12f, 0.01f));
                Part(PrimitiveType.Cylinder, greenP, m, new Vector3(0f, 1.75f, 0f), new Vector3(0.32f, 0.02f, 0.32f), Quaternion.Euler(90f, 0f, 0f));
                machines++;
            }
            // Bike Share docks: three stations in the curb lane, a rack of 10 docks with some bikes in
            for (int i = MaxStreet; i < spots.Count && docks < 3; i += 37)
            {
                var (c, face) = spots[i];
                var st = new GameObject("BikeShare_Station").transform; st.SetParent(ex, false);
                st.SetPositionAndRotation(W(c, 0.02f), Quaternion.LookRotation(W(face, 0f)));
                Part(PrimitiveType.Cube, dock, st, new Vector3(0.45f, 0.08f, 0f), new Vector3(0.25f, 0.16f, 8.5f), null, true);
                Part(PrimitiveType.Cube, bikeGreen, st, new Vector3(0.45f, 1.0f, 4.45f), new Vector3(0.3f, 1.9f, 0.3f), null, true);   // the kiosk
                for (int k = 0; k < 10; k++)
                {
                    float z = -4f + k * 0.85f;
                    Part(PrimitiveType.Cube, dock, st, new Vector3(0.45f, 0.45f, z), new Vector3(0.12f, 0.75f, 0.14f));
                    if (rnd.NextDouble() < 0.6)   // a docked bike: two wheels, frame, basket
                    {
                        var b = new GameObject("Bike").transform; b.SetParent(st, false); b.localPosition = new Vector3(-0.4f, 0f, z); b.localRotation = Quaternion.Euler(0f, 90f, 0f);
                        foreach (float wz in new[] { -0.55f, 0.55f }) Part(PrimitiveType.Cylinder, tyre, b, new Vector3(0f, 0.33f, wz), new Vector3(0.66f, 0.02f, 0.66f), Quaternion.Euler(0f, 0f, 90f));
                        Part(PrimitiveType.Cube, bikeGreen, b, new Vector3(0f, 0.6f, 0f), new Vector3(0.05f, 0.05f, 1.0f), Quaternion.Euler(-15f, 0f, 0f));
                        Part(PrimitiveType.Cube, bikeGreen, b, new Vector3(0f, 0.75f, -0.3f), new Vector3(0.05f, 0.5f, 0.05f));
                        Part(PrimitiveType.Cube, dock, b, new Vector3(0f, 0.95f, 0.6f), new Vector3(0.32f, 0.2f, 0.28f));
                    }
                }
                docks++;
            }
            // litter: crumpled paper and the odd coffee cup on the sidewalks
            var crowd = Object.FindFirstObjectByType<LifeInDowntown.NPC.CrowdDirector>();
            if (crowd != null)
                for (int i = 0; i < crowd.points.Length; i += 23)
                {
                    var pt = crowd.points[i] + new Vector3((float)rnd.NextDouble() - 0.5f, 0.005f, (float)rnd.NextDouble() - 0.5f) * 1.6f;
                    bool cup = rnd.NextDouble() < 0.3;
                    var g = Part(cup ? PrimitiveType.Cylinder : PrimitiveType.Cube, cup ? cupRed : paper, ex, pt + Vector3.up * (cup ? 0.04f : 0.01f),
                                 cup ? new Vector3(0.08f, 0.055f, 0.08f) : new Vector3(0.14f, 0.015f, 0.1f),
                                 cup ? Quaternion.Euler(90f, (float)rnd.NextDouble() * 360f, 0f) : Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f));
                    g.transform.SetParent(ex, true);
                    litter++;
                }
            Debug.Log($"STREET_EXTRAS greenP={machines} bikeShare={docks} litter={litter}");
        }

        static void Paint(GameObject car, Material m)
        {
            var mr = car.transform.Find("Body").GetComponent<MeshRenderer>();
            var mats = mr.sharedMaterials; mats[0] = m; mr.sharedMaterials = mats;
        }

        [System.Serializable] class TtcEntrance { public string name; public float x, z; }
        [System.Serializable] class TtcEntrances { public TtcEntrance[] entrances = new TtcEntrance[0]; }
    }
}
