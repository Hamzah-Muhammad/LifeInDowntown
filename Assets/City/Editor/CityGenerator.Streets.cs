using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace LifeInDowntown.City
{
    // Street detail (docs/DESIGN.md 3J): road paint (centre lines, lane lines, crosswalks, stop bars),
    // traffic signals at main-street intersections, and street furniture (bins, benches, hydrants, newspaper
    // boxes, Bike Share docks at their real OSM spots). Intersections are OSM nodes shared by 2+ roads.
    public static partial class CityGenerator
    {
        static readonly HashSet<string> MarkedRoads = new HashSet<string> { "primary", "secondary", "tertiary", "unclassified", "residential" };
        static readonly HashSet<string> SignalRoads = new HashSet<string> { "primary", "secondary", "tertiary" };

        // above the road-area polygons (0.07 m), below plazas (0.08 m): paint at RoadY sat under them, invisible
        const float PaintY = 0.075f;

        internal class Junction { public Vector2 p; public List<(Way road, Vector2 dir)> arms = new List<(Way, Vector2)>(); public float clear; }

        internal static List<Junction> FindJunctions(CityData d)
        {
            var byNode = new Dictionary<Vector2Int, Junction>();
            foreach (var r in d.roads)
            {
                if (!MarkedRoads.Contains(r.type)) continue;
                var p = CityDataUtil.Points(r.pts);
                for (int i = 0; i < p.Count; i++)
                {
                    var key = new Vector2Int(Mathf.RoundToInt(p[i].x * 2f), Mathf.RoundToInt(p[i].y * 2f));
                    if (!byNode.TryGetValue(key, out var j)) byNode[key] = j = new Junction { p = p[i] };
                    if (i > 0) j.arms.Add((r, (p[i - 1] - p[i]).normalized));
                    if (i + 1 < p.Count) j.arms.Add((r, (p[i + 1] - p[i]).normalized));
                }
            }
            var list = new List<Junction>();
            foreach (var j in byNode.Values)
            {
                var roads = new HashSet<long>();
                foreach (var a in j.arms) roads.Add(a.road.id);
                if (roads.Count < 2 || j.arms.Count < 3) continue;          // a real crossing or T, not a bend
                float maxHalf = 0f;
                foreach (var a in j.arms) maxHalf = Mathf.Max(maxHalf, a.road.width * 0.5f);
                j.clear = maxHalf + 1.5f;                                    // crosswalk distance from the centre
                list.Add(j);
            }
            return list;
        }

        static void BuildStreetDetail(CityData d, Transform parent, Materials mats, List<Mesh> meshes)
        {
            var root = new GameObject("StreetDetail").transform;
            root.SetParent(parent, false);
            var junctions = FindJunctions(d);
            var white = new MeshBuilder(); var yellow = new MeshBuilder();

            bool NearJunction(Vector2 pt, float extra)
            {
                foreach (var j in junctions) if ((pt - j.p).sqrMagnitude < (j.clear + extra) * (j.clear + extra)) return true;
                return false;
            }

            // lane paint, broken around junctions
            foreach (var r in d.roads)
            {
                if (!MarkedRoads.Contains(r.type)) continue;
                var p = CityDataUtil.Points(r.pts);
                float y = PaintY;
                for (int i = 0; i + 1 < p.Count; i++)
                {
                    Vector2 a = p[i], b = p[i + 1];
                    float len = (b - a).magnitude;
                    if (len < 1f) continue;
                    Vector2 dir = (b - a) / len, left = new Vector2(-dir.y, dir.x);
                    for (float t = 0f; t < len; t += 1.5f)
                    {
                        Vector2 m0 = a + dir * t, m1 = a + dir * Mathf.Min(len, t + 1.5f);
                        if (NearJunction((m0 + m1) * 0.5f, 3.5f)) continue;
                        if (!r.oneway && r.width >= 7.5f)                   // double yellow centre line
                            foreach (float o in new[] { -0.16f, 0.16f }) Strip(yellow, m0 + left * o, m1 + left * o, 0.11f, y);
                        if (r.width >= 12f && ((int)(t / 1.5f) % 6) < 2)    // 3 m dashes, 6 m gaps
                            foreach (float o in new[] { -r.width * 0.25f, r.width * 0.25f }) Strip(white, m0 + left * o, m1 + left * o, 0.12f, y);
                    }
                }
            }

            // crosswalks + stop bars on every arm of every junction
            int crossings = 0;
            foreach (var j in junctions)
                foreach (var (road, dir) in j.arms)
                {
                    float y = PaintY, hw = road.width * 0.5f;
                    Vector2 left = new Vector2(-dir.y, dir.x);
                    Vector2 c0 = j.p + dir * j.clear, c1 = c0 + dir * 3f;    // 3 m deep ladder crossing
                    for (float s = -hw + 0.5f; s < hw - 0.3f; s += 1.1f)
                        Strip(white, c0 + left * s, c1 + left * s, 0.5f, y);
                    Vector2 stop = c1 + dir * 1.2f;                          // stop bar on the approach half
                    Strip(white, stop + left * 0.2f, stop + left * hw, 0.4f, y, across: true);
                    crossings++;
                }
            var paintW = Materials.Plain("PaintWhite", new Color(0.82f, 0.82f, 0.8f), 0.7f, 0f, Color.black, 0f);
            var paintY = Materials.Plain("PaintYellow", new Color(0.85f, 0.65f, 0.12f), 0.7f, 0f, Color.black, 0f);
            AddObject("RoadPaintWhite", root, white.ToMesh("RoadPaintWhite"), paintW, meshes, collider: false);
            AddObject("RoadPaintYellow", root, yellow.ToMesh("RoadPaintYellow"), paintY, meshes, collider: false);

            int signals = BuildSignals(junctions, root, mats, meshes);
            int props = BuildProps(d, root, mats, meshes);
            Debug.Log($"STREETS_OK junctions={junctions.Count} crossings={crossings} signals={signals} props={props}");
        }

        // flat paint strip from a to b (width w); across=true: a..b is the strip's length across the lane
        static void Strip(MeshBuilder mb, Vector2 a, Vector2 b, float w, float y, bool across = false)
        {
            Vector2 d = (b - a); float len = d.magnitude; if (len < 0.01f) return;
            Vector2 n = new Vector2(-d.y, d.x) / len * (w * 0.5f);
            mb.Quad(V(a - n, y), V(b - n, y), V(b + n, y), V(a + n, y), Vector3.up, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
        }

        // ------------------------------------------------------------- signals
        static int BuildSignals(List<Junction> junctions, Transform parent, Materials mats, List<Mesh> meshes)
        {
            var redOn = Materials.Plain("SignalRedOn", Color.black, 0.8f, 0f, new Color(1f, 0.08f, 0.05f), 900f);
            var amberOn = Materials.Plain("SignalAmberOn", Color.black, 0.8f, 0f, new Color(1f, 0.55f, 0.05f), 900f);
            var greenOn = Materials.Plain("SignalGreenOn", Color.black, 0.8f, 0f, new Color(0.15f, 1f, 0.55f), 900f);
            var off = Materials.Plain("SignalLensOff", new Color(0.05f, 0.05f, 0.05f), 0.9f, 0f, Color.black, 0f);
            var housing = Materials.Plain("SignalHousing", new Color(0.06f, 0.06f, 0.05f), 0.4f, 0.3f, Color.black, 0f);
            int n = 0, ji = 0;
            foreach (var j in junctions)
            {
                bool main = j.arms.TrueForAll(a => SignalRoads.Contains(a.road.type)) && j.arms.Count >= 4;
                if (!main) continue;
                var go = new GameObject($"Signal_{ji++:00}");
                go.transform.SetParent(parent, false);
                var sig = go.AddComponent<LifeInDowntown.World.TrafficSignal>();
                sig.redOn = redOn; sig.amberOn = amberOn; sig.greenOn = greenOn; sig.lensOff = off;
                sig.offset = ji * 7.3f;
                var heads = new List<LifeInDowntown.World.TrafficSignal.Head>();
                var poles = new MeshBuilder();
                Vector2 axis0 = j.arms[0].dir;
                foreach (var (road, dir) in j.arms)
                {
                    // pole on the approaching driver's far-right corner, head facing back down the arm
                    Vector2 right = new Vector2(dir.y, -dir.x);
                    Vector2 at = j.p + dir * (j.clear + 3.6f) + right * (road.width * 0.5f + 0.9f);
                    var basePos = new Vector3(at.x, 0f, at.y);
                    var face = new Vector3(dir.x, 0f, dir.y);                 // towards the oncoming traffic
                    var side = new Vector3(right.x, 0f, right.y);
                    Box(poles, basePos + Vector3.up * 2.6f, side * 0.09f, Vector3.up * 2.6f, face * 0.09f);
                    Vector3 headC = basePos + Vector3.up * 4.6f - side * 1.6f;   // on a short mast arm over the lane
                    Box(poles, basePos + Vector3.up * 5.0f - side * 0.8f, side * 0.85f, Vector3.up * 0.05f, face * 0.05f);
                    Box(poles, headC, side * 0.2f, Vector3.up * 0.55f, face * 0.14f);
                    var head = new LifeInDowntown.World.TrafficSignal.Head { axis = Mathf.Abs(Vector2.Dot(dir, axis0)) > 0.7f ? 0 : 1 };
                    head.red = Lens(go.transform, headC + Vector3.up * 0.34f + face * 0.15f, face, off, meshes);
                    head.amber = Lens(go.transform, headC + face * 0.15f, face, off, meshes);
                    head.green = Lens(go.transform, headC - Vector3.up * 0.34f + face * 0.15f, face, off, meshes);
                    heads.Add(head);
                }
                AddObject("SignalPoles", go.transform, poles.ToMesh($"SignalPoles_{ji}"), housing, meshes, collider: true);
                sig.heads = heads.ToArray();
                n++;
            }
            return n;
        }

        static Renderer Lens(Transform parent, Vector3 c, Vector3 face, Material mat, List<Mesh> meshes)
        {
            var mb = new MeshBuilder();
            var side = Vector3.Cross(Vector3.up, face).normalized * 0.12f;
            mb.Quad(c - side - Vector3.up * 0.12f, c + side - Vector3.up * 0.12f, c + side + Vector3.up * 0.12f, c - side + Vector3.up * 0.12f,
                    face, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            var go = new GameObject("Lens");
            go.transform.SetParent(parent, false);
            var mesh = mb.ToMesh("Lens");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            meshes.Add(mesh);
            return r;
        }

        // --------------------------------------------------------------- props
        static int BuildProps(CityData d, Transform parent, Materials mats, List<Mesh> meshes)
        {
            var green = Materials.Plain("PropBinGreen", new Color(0.12f, 0.2f, 0.14f), 0.4f, 0.6f, Color.black, 0f);   // City of Toronto bins
            var red = Materials.Plain("PropHydrantRed", new Color(0.55f, 0.05f, 0.04f), 0.5f, 0.3f, Color.black, 0f);
            var wood = Materials.Plain("PropBenchWood", new Color(0.3f, 0.2f, 0.12f), 0.35f, 0f, Color.black, 0f);
            var metal = mats.Pole;
            var boxBlue = Materials.Plain("PropNewsBox", new Color(0.1f, 0.18f, 0.45f), 0.5f, 0.4f, Color.black, 0f);
            var screen = Materials.Plain("PropKioskScreen", Color.black, 0.9f, 0f, new Color(0.3f, 0.9f, 0.5f), 400f);
            var bins = new MeshBuilder(); var hyd = new MeshBuilder(); var bench = new MeshBuilder(); var frame = new MeshBuilder();
            var news = new MeshBuilder(); var kiosk = new MeshBuilder();

            var footprints = new List<List<Vector2>>();
            foreach (var b in d.buildings) if (b.minH < 0.5f) footprints.Add(CityDataUtil.Points(b.pts));
            var segs = new List<(Vector2 a, Vector2 b, float halfW, long id)>();
            foreach (var r in d.roads)
            {
                var p = CityDataUtil.Points(r.pts);
                for (int i = 0; i + 1 < p.Count; i++) segs.Add((p[i], p[i + 1], r.width * 0.5f, r.id));
            }
            var rnd = new System.Random(311);
            var used = new List<Vector2>();
            int n = 0;
            bool Free(Vector2 s, long ownRoad) =>
                s.magnitude < 230f && !OnAnyRoad(s, segs, ownRoad, 0.6f) && !footprints.Exists(f => PointInPolygon(s, f)) &&
                !used.Exists(u => (u - s).sqrMagnitude < 9f);

            foreach (var r in d.roads)
            {
                if (!SignalRoads.Contains(r.type) && r.type != "unclassified") continue;
                var p = CityDataUtil.Points(r.pts);
                for (int i = 0; i + 1 < p.Count; i++)
                {
                    Vector2 a = p[i], b = p[i + 1]; float len = (b - a).magnitude; if (len < 4f) continue;
                    Vector2 dir = (b - a) / len, left = new Vector2(-dir.y, dir.x);
                    for (float t = 6f; t < len - 2f; t += 18f + (float)rnd.NextDouble() * 22f)
                    {
                        int side = rnd.Next(2) == 0 ? 1 : -1;
                        Vector2 s = a + dir * t + left * ((r.width * 0.5f + 0.55f) * side);   // kerb edge of the sidewalk
                        if (!Free(s, r.id)) continue;
                        used.Add(s);
                        var c = new Vector3(s.x, SidewalkHeight, s.y);
                        var f = new Vector3(left.x, 0, left.y) * side;           // facing the sidewalk
                        var al = new Vector3(dir.x, 0, dir.y);
                        switch (rnd.Next(4))
                        {
                            case 0: Box(bins, c + Vector3.up * 0.5f, al * 0.3f, Vector3.up * 0.5f, f * 0.3f); break;   // litter + recycling bin
                            case 1:                                                                                // hydrant
                                Box(hyd, c + Vector3.up * 0.35f, al * 0.13f, Vector3.up * 0.35f, f * 0.13f);
                                Box(hyd, c + Vector3.up * 0.55f, al * 0.22f, Vector3.up * 0.05f, f * 0.06f); break;
                            case 2:                                                                                // bench facing the sidewalk
                                Box(bench, c + Vector3.up * 0.44f, al * 0.85f, Vector3.up * 0.03f, f * 0.22f);
                                Box(bench, c + Vector3.up * 0.75f - f * 0.22f, al * 0.85f, Vector3.up * 0.2f, f * 0.03f);
                                foreach (float k in new[] { -0.7f, 0.7f }) Box(frame, c + al * k + Vector3.up * 0.22f, al * 0.03f, Vector3.up * 0.22f, f * 0.22f);
                                break;
                            default:                                                                               // newspaper boxes
                                for (int k = 0; k < 3; k++) Box(news, c + al * (k * 0.5f - 0.5f) + Vector3.up * 0.55f, al * 0.22f, Vector3.up * 0.55f, f * 0.22f);
                                break;
                        }
                        n++;
                    }
                }
            }

            // Bike Share Toronto docks at their real OSM points
            foreach (var pl in LifeInDowntown.Phone.PhoneData.Load().Places.Values)
            {
                if (pl.category != "amenity=bicycle_rental") continue;
                var s = new Vector2(pl.x, pl.z);
                if (s.magnitude > 230f) continue;
                var c = new Vector3(s.x, SidewalkHeight, s.y);
                Box(kiosk, c + Vector3.up * 0.9f, Vector3.right * 0.3f, Vector3.up * 0.9f, Vector3.forward * 0.25f);
                Box(frame, c + Vector3.up * 1.3f + Vector3.forward * 0.26f, Vector3.right * 0.2f, Vector3.up * 0.15f, Vector3.forward * 0.01f);
                for (int k = 1; k <= 8; k++) Box(frame, c + Vector3.right * (k * 0.75f) + Vector3.up * 0.45f, Vector3.right * 0.05f, Vector3.up * 0.45f, Vector3.forward * 0.12f);
                var g = new MeshBuilder();
                Box(g, c + Vector3.up * 1.3f + Vector3.forward * 0.271f, Vector3.right * 0.18f, Vector3.up * 0.12f, Vector3.forward * 0.001f);
                AddObject("BikeShareScreen", parent, g.ToMesh("BikeShareScreen"), screen, meshes, collider: false);
                n++;
            }

            AddObject("PropBins", parent, bins.ToMesh("PropBins"), green, meshes, collider: true);
            AddObject("PropHydrants", parent, hyd.ToMesh("PropHydrants"), red, meshes, collider: true);
            AddObject("PropBenches", parent, bench.ToMesh("PropBenches"), wood, meshes, collider: true);
            AddObject("PropFrames", parent, frame.ToMesh("PropFrames"), metal, meshes, collider: true);
            AddObject("PropNewsBoxes", parent, news.ToMesh("PropNewsBoxes"), boxBlue, meshes, collider: true);
            AddObject("PropBikeKiosks", parent, kiosk.ToMesh("PropBikeKiosks"), metal, meshes, collider: true);
            return n;
        }
    }
}
