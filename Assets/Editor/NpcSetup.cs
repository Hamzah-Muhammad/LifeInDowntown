using System.Collections.Generic;
using System.IO;
using LifeInDowntown.City;
using LifeInDowntown.NPC;
using LifeInDowntown.Phone;
using UnityEditor;
using UnityEngine;

namespace LifeInDowntown.EditorTools
{
    // Places Rocketbox pedestrians (docs/DESIGN.md 3B.2) around Yonge & Dundas: walkers on real sidewalk
    // lines (road edges from the OSM data, minus building footprints and crossings) and people standing
    // idle or on their phones outside real shops. Seeded, so every regenerate gives the same crowd.
    public static class NpcSetup
    {
        const int Walkers = 24, Standers = 8;
        const float Radius = 170f, SidewalkOffset = 1.6f, Spacing = 4f;
        static readonly HashSet<string> Streets = new HashSet<string> { "primary", "secondary", "tertiary", "unclassified", "residential" };

        public static void Setup(CityData data)
        {
            var old = GameObject.Find("NPCs");
            if (old != null) Object.DestroyImmediate(old);
            var prefabs = new List<GameObject>();
            string dir = CharacterImport.Root + "/Prefabs";
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*.prefab")) prefabs.Add(AssetDatabase.LoadAssetAtPath<GameObject>(f.Replace('\\', '/')));
            if (prefabs.Count == 0) { Debug.Log("NPC_SKIP no prefabs yet (run CharacterImport)"); return; }

            var lines = SidewalkLines(data);
            var root = new GameObject("NPCs").transform;
            var rnd = new System.Random(4242);
            int walkers = 0, standers = 0;

            for (int n = 0; n < Walkers && lines.Count > 0; n++)
            {
                var line = lines[rnd.Next(lines.Count)];
                float lane = ((float)rnd.NextDouble() - 0.5f) * 1.2f;      // spread people across the sidewalk
                var path = new Vector3[line.Count];
                for (int i = 0; i < line.Count; i++) path[i] = line[i] + LaneOffset(line, i) * lane;
                int start = rnd.Next(line.Count - 1);
                var p = Spawn(prefabs[rnd.Next(prefabs.Count)], root, path[start], Look(path[start], path[start + 1]));
                p.path = path;
                p.startIndex = start;
                p.mode = rnd.NextDouble() < 0.2 ? Pedestrian.Mode.WalkSlow : Pedestrian.Mode.Walk;
                walkers++;
            }

            // standers: outside real shops and cafes near the intersection
            var places = new List<Place>();
            foreach (var pl in PhoneData.Load().Places.Values)
                if ((pl.category.StartsWith("shop=") || pl.category.StartsWith("amenity=cafe") || pl.category.StartsWith("amenity=fast_food"))
                    && new Vector2(pl.x, pl.z).magnitude < 120f) places.Add(pl);
            places.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            for (int n = 0; n < Standers && places.Count > 0; n++)
            {
                var pl = places[rnd.Next(places.Count)];
                Vector3 shop = new Vector3(pl.x, 0f, pl.z);
                Vector3 spot = NearestSidewalk(lines, shop);
                var p = Spawn(prefabs[rnd.Next(prefabs.Count)], root, spot, Look(spot, shop));   // facing the shopfront
                p.mode = n % 2 == 0 ? Pedestrian.Mode.Phone : Pedestrian.Mode.Idle;
                standers++;
            }
            Debug.Log($"NPC_OK walkers={walkers} standers={standers} sidewalkLines={lines.Count} characters={prefabs.Count}");
        }

        static Pedestrian Spawn(GameObject prefab, Transform parent, Vector3 pos, Quaternion rot)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetPositionAndRotation(pos + Vector3.up * 0.15f, rot);   // sidewalk slab top
            var p = go.GetComponent<Pedestrian>();
            p.walkSpeed = 1.16f;   // measured clip speeds (prefabs may carry older serialized values)
            p.slowSpeed = 0.85f;
            return p;
        }

        static Quaternion Look(Vector3 from, Vector3 to)
        {
            var d = to - from; d.y = 0f;
            return d.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(d) : Quaternion.identity;
        }

        static Vector3 LaneOffset(List<Vector3> line, int i)
        {
            var a = line[Mathf.Max(0, i - 1)]; var b = line[Mathf.Min(line.Count - 1, i + 1)];
            var d = (b - a); d.y = 0f; d.Normalize();
            return new Vector3(-d.z, 0f, d.x);
        }

        static Vector3 NearestSidewalk(List<List<Vector3>> lines, Vector3 to)
        {
            Vector3 best = to; float bd = float.MaxValue;
            foreach (var l in lines) foreach (var p in l) { float d = (p - to).sqrMagnitude; if (d < bd) { bd = d; best = p; } }
            return best;
        }

        // both sides of every street near the origin, sampled every few metres, split wherever the
        // sidewalk would run into a building or across another road
        static List<List<Vector3>> SidewalkLines(CityData d)
        {
            var segs = new List<(Vector2 a, Vector2 b, float halfW, long id)>();
            foreach (var r in d.roads)
            {
                var p = CityDataUtil.Points(r.pts);
                for (int i = 0; i + 1 < p.Count; i++) segs.Add((p[i], p[i + 1], r.width * 0.5f, r.id));
            }
            var footprints = new List<List<Vector2>>();
            foreach (var b in d.buildings) if (b.minH < 0.5f) footprints.Add(CityDataUtil.Points(b.pts));

            var lines = new List<List<Vector3>>();
            foreach (var r in d.roads)
            {
                if (!Streets.Contains(r.type)) continue;
                var pts = CityDataUtil.Points(r.pts);
                foreach (int side in new[] { 1, -1 })
                {
                    var run = new List<Vector3>();
                    for (int i = 0; i + 1 < pts.Count; i++)
                    {
                        Vector2 a = pts[i], b = pts[i + 1];
                        float len = (b - a).magnitude;
                        if (len < 0.5f) continue;
                        Vector2 dir = (b - a) / len, left = new Vector2(-dir.y, dir.x);
                        for (float t = 0f; t < len; t += Spacing)
                        {
                            Vector2 s = a + dir * t + left * ((r.width * 0.5f + SidewalkOffset) * side);
                            bool ok = s.magnitude < Radius && !CityGenerator.OnAnyRoad(s, segs, r.id, 0.3f)
                                      && !footprints.Exists(f => CityGenerator.PointInPolygon(s, f));
                            if (ok) run.Add(new Vector3(s.x, 0f, s.y));
                            else { if (run.Count >= 5) lines.Add(run); run = new List<Vector3>(); }
                        }
                    }
                    if (run.Count >= 5) lines.Add(run);
                }
            }
            return lines;
        }
    }
}
