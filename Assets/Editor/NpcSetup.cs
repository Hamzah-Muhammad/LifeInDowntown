using System.Collections.Generic;
using System.IO;
using LifeInDowntown.City;
using LifeInDowntown.NPC;
using LifeInDowntown.Phone;
using MinniGameFramework.NPC;
using UnityEditor;
using UnityEngine;

namespace LifeInDowntown.EditorTools
{
    // The street crowd (docs/DESIGN.md 3B.2, 3L). Bakes the real sidewalk lines (road edges from the OSM
    // data, minus building footprints and crossings) for the whole map into a CrowdDirector, which keeps a
    // pool of walkers around the player at runtime. Places the people who stay put:
    //   crosswalks - people waiting at the lights, facing the crossing
    //   corners    - loiterers (the corner boys) in ones, twos and threes; one of them watches you
    //   walls      - homeless people outside real shops: on a crate, crouched, standing, or asleep in a bag
    // Seeded, so every regenerate gives the same street.
    public static class NpcSetup
    {
        public const string TyAvatar = "Male_Adult_10";
        public const string TarikAvatar = "Male_Adult_18";   // Tarik's own body (DESIGN 3P.1): never a stranger on the street
        const float SidewalkOffset = 1.6f, Spacing = 4f, SlabTop = 0.15f;
        const int CornerGroups = 10, CrosswalkCorners = 18, HomelessSpots = 16;
        static readonly HashSet<string> Streets = new HashSet<string> { "primary", "secondary", "tertiary", "unclassified", "residential" };

        class Ctx
        {
            public System.Random rnd;
            public List<GameObject> male = new List<GameObject>(), female = new List<GameObject>(), police = new List<GameObject>();
            public List<(Vector2 a, Vector2 b, float halfW, long id)> segs;
            public List<List<Vector2>> footprints;
            public List<Vector3> doors = new List<Vector3>();
            public List<Vector3> taken = new List<Vector3>();
            public List<Pedestrian> statics = new List<Pedestrian>();
            public Transform root;
            public Material cardboard, crate, bag, cup, pack;
        }

        public static void Setup(CityData data)
        {
            var old = GameObject.Find("NPCs");
            if (old != null) Object.DestroyImmediate(old);
            var c = new Ctx { rnd = new System.Random(4242) };
            string dir = CharacterImport.Root + "/Prefabs";
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*.prefab"))
                {
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(f.Replace('\\', '/'));
                    string file = Path.GetFileName(f);
                    if (file.StartsWith(TyAvatar) || file.StartsWith(TarikAvatar)) continue;   // Ty is one person: never in the street crowd (DESIGN 3O.1)
                    (file.StartsWith("Police_") ? c.police : file.StartsWith("Female") ? c.female : c.male).Add(go);
                }
            if (c.male.Count + c.female.Count == 0) { Debug.Log("NPC_SKIP no prefabs yet (run CharacterImport)"); return; }

            c.segs = new List<(Vector2, Vector2, float, long)>();
            foreach (var r in data.roads)
            {
                var p = CityDataUtil.Points(r.pts);
                for (int i = 0; i + 1 < p.Count; i++) c.segs.Add((p[i], p[i + 1], r.width * 0.5f, r.id));
            }
            c.footprints = new List<List<Vector2>>();
            foreach (var b in data.buildings) if (b.minH < 0.5f) c.footprints.Add(CityDataUtil.Points(b.pts));
            foreach (var d in Object.FindObjectsByType<LifeInDowntown.World.DoorLink>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (d.transform.position.y < 10f) c.doors.Add(d.transform.position);   // street doors: lobby, Rabbas
            c.cardboard = CityGenerator.Materials.Plain("Npc_Cardboard", new Color(0.34f, 0.26f, 0.17f), 0.12f, 0f, Color.black, 0f);
            c.crate = CityGenerator.Materials.Plain("Npc_MilkCrate", new Color(0.06f, 0.12f, 0.32f), 0.45f, 0f, Color.black, 0f);
            c.bag = CityGenerator.Materials.Plain("Npc_SleepingBag", new Color(0.16f, 0.2f, 0.15f), 0.3f, 0f, Color.black, 0f);
            var fabric = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/City/Textures/fabric_pattern_07/fabric_pattern_07_Color.jpg");
            if (fabric != null) { c.bag.SetTexture("_BaseColorMap", fabric); c.bag.SetTextureScale("_BaseColorMap", new Vector2(3f, 1.5f)); }
            c.cup = CityGenerator.Materials.Plain("Npc_PaperCup", new Color(0.8f, 0.77f, 0.7f), 0.2f, 0f, Color.black, 0f);
            c.pack = CityGenerator.Materials.Plain("Npc_Backpack", new Color(0.09f, 0.09f, 0.1f), 0.25f, 0f, Color.black, 0f);

            var lines = SidewalkLines(data, c, out var weights);
            c.root = new GameObject("NPCs").transform;
            var crowd = c.root.gameObject.AddComponent<CrowdDirector>();
            var all = new List<GameObject>(c.male); all.AddRange(c.female);
            crowd.walkerPrefabs = all.ToArray();
            crowd.policePrefabs = c.police.ToArray();
            var pts = new List<Vector3>(); var starts = new List<int>();
            foreach (var l in lines) { starts.Add(pts.Count); foreach (var p in l) pts.Add(p + Vector3.up * SlabTop); }
            crowd.points = pts.ToArray(); crowd.lineStart = starts.ToArray(); crowd.lineWeight = weights.ToArray();

            var junctions = CityGenerator.FindJunctions(data);
            junctions.Sort((a, b) => a.p.x != b.p.x ? a.p.x.CompareTo(b.p.x) : a.p.y.CompareTo(b.p.y));
            Shuffle(junctions, c.rnd);
            // each group's people get their street role (lines, dealer worries); corner lookouts are dealers (DESIGN 3O)
            void Roles(int from, Pedestrian.Role r) { for (int i = from; i < c.statics.Count; i++) c.statics[i].role = r; }
            int n0 = c.statics.Count;
            int police = PlacePolice(c, junctions); Roles(n0, Pedestrian.Role.Police); n0 = c.statics.Count;
            int groups = PlaceCorners(c, junctions); Roles(n0, Pedestrian.Role.Corner);
            string[] names = { "Dre", "Junior", "Smokes", "Lil T", "Bigs", "K", "Ricky", "Shottas", "Moe", "Tiny" };
            int dealers = 0;
            for (int i = n0; i < c.statics.Count; i++)
                if (c.statics[i].watchPlayer) c.statics[i].gameObject.AddComponent<LifeInDowntown.World.DealerNPC>().nickname = names[dealers++ % names.Length];
            n0 = c.statics.Count;
            int waiting = PlaceCrosswalks(c, junctions); Roles(n0, Pedestrian.Role.Waiting); n0 = c.statics.Count;
            int homeless = PlaceHomeless(c, lines); Roles(n0, Pedestrian.Role.Homeless);
            crowd.statics = c.statics.ToArray();
            // armed men for mission objectives (DESIGN 3P.4): the crowd's men, with Tarik's duty pistol
            var spawner = c.root.gameObject.AddComponent<HostileSpawner>();
            spawner.prefabs = c.male.ToArray();
            spawner.pistol = AssetDatabase.LoadAssetAtPath<GameObject>(PistolImport.PrefabPath);
            Debug.Log($"NPC_OK pool={crowd.walkers} cornerGroups={groups} crosswalkPeople={waiting} homelessSpots={homeless} " +
                      $"police={police} dealers={dealers} statics={c.statics.Count} sidewalkLines={lines.Count} points={pts.Count} characters={all.Count}");
        }

        // ---------- police: two officers posted at Yonge & Dundas, the corner TPS always has someone on ----------
        static int PlacePolice(Ctx c, List<CityGenerator.Junction> junctions)
        {
            if (c.police.Count == 0) return 0;
            CityGenerator.Junction centre = null;
            foreach (var j in junctions) if (centre == null || j.p.sqrMagnitude < centre.p.sqrMagnitude) centre = j;
            if (centre == null) return 0;
            var corners = Corners(c, centre);
            if (corners.Count == 0) return 0;
            var (spot, face) = corners[0];
            var side = new Vector3(-face.z, 0f, face.x);
            Person(c, c.police, spot - side * 0.6f, face, Pedestrian.Mode.LookAround, watch: true);
            Person(c, c.police, spot + side * 0.6f, (face - side).normalized, Pedestrian.Mode.Waiting, watch: false);
            c.taken.Add(spot);
            return 2;
        }

        // ---------- corners: the corner boys ----------
        static int PlaceCorners(Ctx c, List<CityGenerator.Junction> junctions)
        {
            int placed = 0;
            foreach (var j in junctions)
            {
                if (placed >= CornerGroups) break;
                if (Near(c.taken, Flat(j.p), 90f)) continue;                       // one crew per few blocks
                var corners = Corners(c, j);
                if (corners.Count == 0) continue;
                var (spot, face) = corners[c.rnd.Next(corners.Count)];
                double roll = c.rnd.NextDouble();
                var side = new Vector3(-face.z, 0f, face.x);
                if (roll < 0.4)
                    Person(c, c.male, spot, face, c.rnd.NextDouble() < 0.5 ? Pedestrian.Mode.Nervous : Pedestrian.Mode.LookAround, watch: true);
                else if (roll < 0.75)
                {
                    // two of them talking, side on to the street; the listener keeps an eye out
                    Person(c, c.male, spot - side * 0.55f, side, Pedestrian.Mode.TalkCool, watch: false);
                    Person(c, c.male, spot + side * 0.55f, -side, Pedestrian.Mode.Listen, watch: true);
                }
                else
                {
                    Person(c, c.male, spot, face, Pedestrian.Mode.LookAround, watch: true);
                    Person(c, c.male, spot - side * 0.9f - face * 0.6f, side, Pedestrian.Mode.TalkCool, watch: false);
                    Person(c, c.Any(), spot + side * 0.9f - face * 0.5f, -side, Pedestrian.Mode.Phone, watch: false);
                }
                c.taken.Add(spot);
                placed++;
            }
            return placed;
        }

        // ---------- crosswalks: people waiting at the lights ----------
        static int PlaceCrosswalks(Ctx c, List<CityGenerator.Junction> junctions)
        {
            int people = 0, used = 0;
            foreach (var j in junctions)
            {
                if (used >= CrosswalkCorners) break;
                var corners = Corners(c, j);
                if (corners.Count == 0) continue;
                var (spot, _) = corners[c.rnd.Next(corners.Count)];
                if (Near(c.taken, spot, 25f)) continue;
                Vector3 toCentre = Flat(j.p) - spot; toCentre.Normalize();
                int n = c.rnd.NextDouble() < 0.4 ? 2 : 1;
                for (int k = 0; k < n; k++)
                {
                    Vector3 off = new Vector3(-toCentre.z, 0f, toCentre.x) * (k == 0 ? 0f : 1.1f);
                    var mode = c.rnd.NextDouble() < 0.35 ? Pedestrian.Mode.Phone : Pedestrian.Mode.Waiting;
                    Person(c, c.Any(), spot + off, toCentre, mode, watch: false);
                    people++;
                }
                c.taken.Add(spot);
                used++;
            }
            return people;
        }

        // sidewalk points on the inside of each corner (between two arms), clear of roads, buildings and doors
        static List<(Vector3 spot, Vector3 face)> Corners(Ctx c, CityGenerator.Junction j)
        {
            var dirs = new List<Vector2>();
            foreach (var a in j.arms)
                if (!dirs.Exists(d => Vector2.Dot(d, a.dir) > 0.98f)) dirs.Add(a.dir);
            dirs.Sort((a, b) => Mathf.Atan2(a.y, a.x).CompareTo(Mathf.Atan2(b.y, b.x)));
            var list = new List<(Vector3, Vector3)>();
            for (int k = 0; k < dirs.Count; k++)
            {
                Vector2 a = dirs[k], b = dirs[(k + 1) % dirs.Count];
                float ang = Vector2.SignedAngle(a, b);
                if (ang < 0f) ang += 360f;
                if (ang < 50f || ang > 200f) continue;
                Vector2 m = (a + b).normalized;
                if (m.sqrMagnitude < 0.5f) continue;
                float dist = Mathf.Clamp((j.clear + 1.4f) / Mathf.Sin(ang * 0.5f * Mathf.Deg2Rad), 4f, 16f);
                Vector2 p = j.p + m * dist;
                if (!Clear(c, p, 0.6f)) continue;
                list.Add((Flat(p), new Vector3(-m.x, 0f, -m.y)));                // facing the intersection
            }
            return list;
        }

        // ---------- walls: homeless people outside real shops ----------
        static int PlaceHomeless(Ctx c, List<List<Vector3>> lines)
        {
            var places = new List<Place>(PhoneData.Load().Places.Values);
            places.Sort((a, b) => string.CompareOrdinal(a.name + a.category, b.name + b.category));
            Shuffle(places, c.rnd);
            int placed = 0;
            foreach (var pl in places)
            {
                if (placed >= HomelessSpots) break;
                if (!(pl.category.StartsWith("shop=") || pl.category.StartsWith("amenity="))) continue;
                Vector3 walk = NearestSidewalk(lines, new Vector3(pl.x, 0f, pl.z));
                if (!WallPoint(c, new Vector2(walk.x, walk.z), out Vector2 wall, out Vector2 outward)) continue;
                Vector2 spot2 = wall + outward * 0.5f;
                Vector3 spot = Flat(spot2), face = new Vector3(outward.x, 0f, outward.y);
                if (!Clear(c, spot2, 0.3f) || Near(c.taken, spot, 35f)) continue;
                Homeless(c, spot, face);
                c.taken.Add(spot);
                placed++;
            }
            return placed;
        }

        static void Homeless(Ctx c, Vector3 spot, Vector3 face)
        {
            var along = new Vector3(-face.z, 0f, face.x);
            var rot = Quaternion.LookRotation(face);
            double roll = c.rnd.NextDouble();
            var g = new GameObject("Homeless").transform;
            g.SetParent(c.root, false);
            g.SetPositionAndRotation(new Vector3(spot.x, SlabTop, spot.z), rot);
            if (roll < 0.3)
            {
                // on a milk crate against the wall, cardboard under the feet, a cup out
                bool woman = c.rnd.NextDouble() < 0.25 && c.female.Count > 0;
                var p = Person(c, woman ? c.female : c.male, spot + face * 0.25f, face,
                               c.rnd.NextDouble() < 0.5 ? Pedestrian.Mode.Sit : Pedestrian.Mode.SitLook, watch: false, worn: true);
                p.transform.SetParent(g, true);
                Prop(g, c.crate, PrimitiveType.Cube, spot - face * 0.02f + Vector3.up * (SlabTop + 0.21f), rot, new Vector3(0.36f, 0.42f, 0.36f));
                Prop(g, c.cardboard, PrimitiveType.Cube, spot + face * 0.55f + Vector3.up * (SlabTop + 0.005f), rot, new Vector3(0.9f, 0.01f, 0.7f));
                Prop(g, c.cup, PrimitiveType.Cylinder, spot + face * 0.95f + along * 0.2f + Vector3.up * (SlabTop + 0.055f), rot, new Vector3(0.09f, 0.055f, 0.09f));
                Prop(g, c.pack, PrimitiveType.Cube, spot + along * 0.55f + Vector3.up * (SlabTop + 0.2f), rot, new Vector3(0.32f, 0.4f, 0.22f));
            }
            else if (roll < 0.55)
            {
                var p = Person(c, c.male, spot + face * 0.2f, face, Pedestrian.Mode.Crouch, watch: false, worn: true);
                p.transform.SetParent(g, true);
                Prop(g, c.cardboard, PrimitiveType.Cube, spot + face * 0.3f + Vector3.up * (SlabTop + 0.005f), rot, new Vector3(1.0f, 0.01f, 0.8f));
                Prop(g, c.cup, PrimitiveType.Cylinder, spot + face * 0.85f + Vector3.up * (SlabTop + 0.055f), rot, new Vector3(0.09f, 0.055f, 0.09f));
            }
            else if (roll < 0.8)
            {
                // asleep in a doorway: a sleeping bag along the wall on flattened boxes, a bag for a pillow
                Prop(g, c.cardboard, PrimitiveType.Cube, spot + face * 0.1f + Vector3.up * (SlabTop + 0.005f), rot, new Vector3(2.1f, 0.01f, 0.9f));
                // a body under the bag: low and wide, shoulders and hips higher than the legs, knees drawn up a little
                var lying = Quaternion.LookRotation(face, Vector3.up) * Quaternion.Euler(0f, 0f, 90f);
                var mound = Prop(g, c.bag, PrimitiveType.Capsule, spot + face * 0.15f + Vector3.up * (SlabTop + 0.12f), lying, new Vector3(0.62f, 0.9f, 0.26f));
                mound.AddComponent<CapsuleCollider>();
                Prop(g, c.bag, PrimitiveType.Sphere, spot + face * 0.15f + along * 0.5f + Vector3.up * (SlabTop + 0.16f), rot, new Vector3(0.8f, 0.3f, 0.5f));     // shoulders
                Prop(g, c.bag, PrimitiveType.Sphere, spot + face * 0.15f - along * 0.1f + Vector3.up * (SlabTop + 0.15f), rot, new Vector3(0.75f, 0.28f, 0.52f));   // hips
                Prop(g, c.bag, PrimitiveType.Sphere, spot + face * 0.17f - along * 0.55f + Vector3.up * (SlabTop + 0.14f), rot, new Vector3(0.55f, 0.24f, 0.42f));  // knees
                Prop(g, c.pack, PrimitiveType.Cube, spot + face * 0.1f + along * 1.05f + Vector3.up * (SlabTop + 0.14f), rot, new Vector3(0.4f, 0.26f, 0.3f));
            }
            else
            {
                var mode = new[] { Pedestrian.Mode.Drunk, Pedestrian.Mode.Cough, Pedestrian.Mode.Cold }[c.rnd.Next(3)];
                var p = Person(c, c.male, spot + face * 0.3f, face, mode, watch: false, worn: true);
                p.transform.SetParent(g, true);
                Prop(g, c.pack, PrimitiveType.Cube, spot + along * 0.6f + Vector3.up * (SlabTop + 0.2f), rot, new Vector3(0.32f, 0.4f, 0.22f));
            }
        }

        // ---------- helpers ----------
        static Pedestrian Person(Ctx c, List<GameObject> set, Vector3 pos, Vector3 face, Pedestrian.Mode mode, bool watch, bool worn = false)
        {
            if (set.Count == 0) set = c.Any();
            var prefab = set[c.rnd.Next(set.Count)];
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, c.root);
            go.transform.SetPositionAndRotation(new Vector3(pos.x, SlabTop, pos.z), Quaternion.LookRotation(face));
            var p = go.GetComponent<Pedestrian>();
            p.mode = mode; p.watchPlayer = watch; p.path = null;
            p.walkSpeed = 1.16f; p.slowSpeed = 0.85f;
            if (worn) Wear(go, prefab.name);
            c.statics.Add(p);
            return p;
        }

        static List<GameObject> Any(this Ctx c) { var l = new List<GameObject>(c.male); l.AddRange(c.female); return l; }

        // faded, dirtier copy of the outfit (the face and hair stay as they are)
        static void Wear(GameObject go, string av)
        {
            string src = $"{CharacterImport.Root}/{av}/{av}_body.mat", dst = $"{CharacterImport.Root}/{av}/{av}_body_worn.mat";
            var body = AssetDatabase.LoadAssetAtPath<Material>(src);
            if (body == null) return;
            var worn = AssetDatabase.LoadAssetAtPath<Material>(dst);
            if (worn == null) { worn = new Material(body); AssetDatabase.CreateAsset(worn, dst); }
            worn.CopyPropertiesFromMaterial(body);
            worn.SetColor("_BaseColor", new Color(0.58f, 0.54f, 0.48f));
            worn.SetFloat("_Smoothness", 0.12f);
            UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(worn);
            EditorUtility.SetDirty(worn);
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var slots = smr.sharedMaterials;
                for (int i = 0; i < slots.Length; i++) if (slots[i] == body) slots[i] = worn;
                smr.sharedMaterials = slots;
            }
        }

        static GameObject Prop(Transform parent, Material m, PrimitiveType type, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = m.name.Replace("Npc_", "");
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }

        static bool Clear(Ctx c, Vector2 p, float roadMargin)
        {
            if (CityGenerator.OnAnyRoad(p, c.segs, -1, roadMargin)) return false;
            foreach (var f in c.footprints) if (CityGenerator.PointInPolygon(p, f)) return false;
            foreach (var d in c.doors) if ((new Vector2(d.x, d.z) - p).sqrMagnitude < 12f * 12f) return false;
            return true;
        }

        // the building wall nearest a sidewalk point (within a shopfront's depth), and the way out of it
        static bool WallPoint(Ctx c, Vector2 from, out Vector2 wall, out Vector2 outward)
        {
            wall = outward = Vector2.zero;
            float best = 7f;
            foreach (var f in c.footprints)
                for (int i = 0, k = f.Count - 1; i < f.Count; k = i++)
                {
                    Vector2 a = f[k], b = f[i], ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(from - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                    if (t < 0.1f || t > 0.9f) continue;                               // not in a building's corner
                    Vector2 q = a + ab * t;
                    float d = (from - q).magnitude;
                    if (d < best && d > 0.3f) { best = d; wall = q; outward = (from - q) / d; }
                }
            return best < 7f;
        }

        static bool Near(List<Vector3> list, Vector3 p, float r)
        {
            foreach (var q in list) if ((q - p).sqrMagnitude < r * r) return true;
            return false;
        }

        static Vector3 Flat(Vector2 p) => new Vector3(p.x, 0f, p.y);

        static void Shuffle<T>(List<T> list, System.Random rnd)
        {
            for (int i = list.Count - 1; i > 0; i--) { int k = rnd.Next(i + 1); (list[i], list[k]) = (list[k], list[i]); }
        }

        static Vector3 NearestSidewalk(List<List<Vector3>> lines, Vector3 to)
        {
            Vector3 best = to; float bd = float.MaxValue;
            foreach (var l in lines) foreach (var p in l) { float d = (p - to).sqrMagnitude; if (d < bd) { bd = d; best = p; } }
            return best;
        }

        // both sides of every street on the map, sampled every few metres, split wherever the sidewalk
        // would run into a building or across another road; busier streets weigh more
        static List<List<Vector3>> SidewalkLines(CityData d, Ctx c, out List<float> weights)
        {
            var lines = new List<List<Vector3>>();
            var ws = new List<float>();
            weights = ws;
            foreach (var r in d.roads)
            {
                if (!Streets.Contains(r.type)) continue;
                float w = r.name != null && r.name.Contains("Yonge") ? 3f : r.type == "primary" ? 2f : r.type == "secondary" ? 1.5f : 1f;
                var pts = CityDataUtil.Points(r.pts);
                foreach (int side in new[] { 1, -1 })
                {
                    var run = new List<Vector3>();
                    void Close() { if (run.Count >= 5) { lines.Add(run); ws.Add(w); } run = new List<Vector3>(); }
                    for (int i = 0; i + 1 < pts.Count; i++)
                    {
                        Vector2 a = pts[i], b = pts[i + 1];
                        float len = (b - a).magnitude;
                        if (len < 0.5f) continue;
                        Vector2 dir = (b - a) / len, left = new Vector2(-dir.y, dir.x);
                        for (float t = 0f; t < len; t += Spacing)
                        {
                            Vector2 s = a + dir * t + left * ((r.width * 0.5f + SidewalkOffset) * side);
                            bool ok = !CityGenerator.OnAnyRoad(s, c.segs, r.id, 0.3f)
                                      && !c.footprints.Exists(f => CityGenerator.PointInPolygon(s, f));
                            if (ok) run.Add(new Vector3(s.x, 0f, s.y));
                            else Close();
                        }
                    }
                    Close();
                }
            }
            return lines;
        }
    }
}
