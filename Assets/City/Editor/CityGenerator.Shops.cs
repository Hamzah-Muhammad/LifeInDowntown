using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace LifeInDowntown.City
{
    // Walk-in Rabba convenience stores (docs/DESIGN.md 3K). Each sits in a real ground-floor building on a
    // street-facing wall: the building's wall gets an opening (BuildBuildings skips it), a glass front with an
    // open doorway goes in, and the room behind is fitted inside the footprint. No loading screen.
    public static partial class CityGenerator
    {
        public const float ShopWidth = 8f, ShopDepth = 10f, ShopCeiling = 3.4f, ShopFloor = 0.15f;   // floor level with the sidewalk

        public struct ShopSite
        {
            public string name;
            public string kind;            // "rabba" or "lobby" (the Pantages lobby, CityGenerator.Lobby.cs)
            public long building;
            public Vector2 a, b;           // the wall edge (CCW footprint order)
            public float t;                // store centre along the edge (m from a)
            public Vector2 along, outward;
            public Vector3 W(float u, float v, float y)   // u along the wall, v into the store, y up
            {
                var c = a + along * t + along * u - outward * v;
                return new Vector3(c.x, y, c.y);
            }
        }

        static List<ShopSite> shopSitesCache;

        public static List<ShopSite> ShopSites(CityData d)
        {
            if (shopSitesCache != null) return shopSitesCache;
            var targets = new List<(string, Vector2)>();
            if (TryCondoFrame(d, out var condo))   // downstairs from Tarik, beside the Pantages lobby
            {
                var p = condo.outwardGround + condo.inward * 0.7f - condo.along * 7.2f;
                targets.Add(("Rabba (Pantages)", new Vector2(p.x, p.z)));
                var lp = condo.outwardGround + condo.inward * 0.7f + condo.along * LobbyShift;   // its own 8 m, clear of the Rabba
                targets.Add((LobbyName, new Vector2(lp.x, lp.z)));
            }
            // the second store: near the INS Market point if a deep enough building is there, else on Yonge
            // (the Dundas West buildings near it are too shallow for a 10 m store)
            var second = new List<Vector2>();
            foreach (var pl in LifeInDowntown.Phone.PhoneData.Load().Places.Values)
                if (pl.name == "INS Market") second.Add(new Vector2(pl.x, pl.z));
            second.Add(new Vector2(-12f, 60f));    // Yonge, west side, north of Dundas
            second.Add(new Vector2(28f, -60f));    // Yonge, east side, south of Dundas
            foreach (var p2 in second) targets.Add(("Rabba (Yonge & Dundas)", p2));

            var roadSegs = new List<(Vector2 a, Vector2 b)>();
            foreach (var r in d.roads) { var rp = CityDataUtil.Points(r.pts); for (int i = 0; i + 1 < rp.Count; i++) roadSegs.Add((rp[i], rp[i + 1])); }

            var sites = new List<ShopSite>();
            foreach (var (name, target) in targets)
            {
                if (sites.Exists(x => x.name == name)) continue;   // fallback spots only fill a missing store
                ShopSite? best = null; float bestD = 25f;
                foreach (var bd in d.buildings)
                {
                    if (bd.minH > 0.5f || bd.h < 6f) continue;
                    var ring = CityDataUtil.Points(bd.pts);
                    if (ring.Count < 3) continue;
                    if (Triangulator.SignedArea(ring) < 0f) ring.Reverse();
                    for (int i = 0; i < ring.Count; i++)
                    {
                        Vector2 a = ring[i], b = ring[(i + 1) % ring.Count], e = b - a;
                        float len = e.magnitude;
                        if (len < ShopWidth + 1.4f) continue;
                        var along = e / len; var outward = new Vector2(along.y, -along.x);
                        if (!NearAnyRoad((a + b) * 0.5f, roadSegs, 16f)) continue;
                        float t = Mathf.Clamp(Vector2.Dot(target - a, along), ShopWidth / 2 + 0.7f, len - ShopWidth / 2 - 0.7f);
                        float dist = (a + along * t - target).magnitude;
                        if (dist >= bestD) continue;
                        var s = new ShopSite { name = name, kind = name == LobbyName ? "lobby" : "rabba", building = bd.id, a = a, b = b, t = t, along = along, outward = outward };
                        bool fits = true;
                        foreach (var (u, v) in new[] { (-ShopWidth / 2, 0.3f), (ShopWidth / 2, 0.3f), (-ShopWidth / 2, ShopDepth), (ShopWidth / 2, ShopDepth) })
                        { var w = s.W(u, v, 0f); if (!PointInPolygon(new Vector2(w.x, w.z), ring)) { fits = false; break; } }
                        if (!fits || OtherBuildingInside(d, s)) continue;
                        best = s; bestD = dist;
                    }
                }
                if (best.HasValue) sites.Add(best.Value);
            }
            foreach (var (name, _) in targets)
                if (!sites.Exists(x => x.name == name)) Debug.LogWarning($"CITY_WARN no wall fits {name}");
            return shopSitesCache = sites;
        }

        // the store / lobby openings on this wall edge, sorted along it: (from, to) metres from a, and the room's
        // ceiling. Any building's wall lying on the front is cut, not only the host's: OSM often has overlapping
        // outlines/parts on one facade.
        static List<(float from, float to, float top)> ShopOpenings(List<ShopSite> sites, Vector2 a, Vector2 b)
        {
            var cuts = new List<(float, float, float)>();
            Vector2 e = b - a; float len = e.magnitude; if (len < 0.5f) return cuts;
            Vector2 dir = e / len, n = new Vector2(-dir.y, dir.x);
            foreach (var s in sites)
            {
                var o0 = s.W(-ShopWidth / 2, 0f, 0f); var o1 = s.W(ShopWidth / 2, 0f, 0f);
                Vector2 p0 = new Vector2(o0.x, o0.z), p1 = new Vector2(o1.x, o1.z);
                if (Mathf.Abs(Vector2.Dot(p0 - a, n)) > 0.35f || Mathf.Abs(Vector2.Dot(p1 - a, n)) > 0.35f) continue;   // not on this line
                float t0 = Vector2.Dot(p0 - a, dir), t1 = Vector2.Dot(p1 - a, dir);
                if (t0 > t1) (t0, t1) = (t1, t0);
                t0 = Mathf.Max(0f, t0); t1 = Mathf.Min(len, t1);
                if (t1 - t0 < 0.5f) continue;                                                                 // no overlap
                cuts.Add((t0, t1, s.kind == "lobby" ? LobbyCeiling : ShopCeiling));
            }
            cuts.Sort((x, y) => x.Item1.CompareTo(y.Item1));
            return cuts;
        }

        // another building's footprint reaching into the store room would put walls inside it
        static bool OtherBuildingInside(CityData d, ShopSite s)
        {
            var probes = new List<Vector2>();
            for (float u = -ShopWidth / 2 + 0.5f; u <= ShopWidth / 2 - 0.5f; u += 1.75f)
                for (float v = 0.6f; v <= ShopDepth - 0.4f; v += 1.6f) { var w = s.W(u, v, 0f); probes.Add(new Vector2(w.x, w.z)); }
            foreach (var bd in d.buildings)
            {
                if (bd.id == s.building || bd.minH > ShopCeiling) continue;
                var ring = CityDataUtil.Points(bd.pts);
                if (ring.Count < 3) continue;
                foreach (var pr in probes) if (PointInPolygon(pr, ring)) return true;
            }
            return false;
        }

        // --------------------------------------------------------------- interior
        static void BuildShops(CityData d, Transform parent, Materials mats, List<Mesh> meshes)
        {
            var sites = ShopSites(d);
            if (sites.Count == 0) return;
            var floorMat = Materials.Lit("RabbaFloor", "floor_tiles_06", 1.2f, Color.white);
            var paint = Materials.Plain("RabbaWall", new Color(0.92f, 0.92f, 0.9f), 0.25f, 0f, Color.black, 0f);
            var metal = mats.Pole;
            var glass = Materials.GlassMaterial("RabbaGlass", new Color(0.55f, 0.62f, 0.64f, 0.12f));
            var panel = Materials.Plain("RabbaCeilingPanel", Color.white, 0.2f, 0f, new Color(0.93f, 0.97f, 1f), 140f);
            var fridgeGlow = Materials.Plain("RabbaFridgeGlow", Color.white, 0.2f, 0f, new Color(0.85f, 0.93f, 1f), 70f);
            var shelfMat = Materials.Plain("RabbaShelf", new Color(0.78f, 0.78f, 0.76f), 0.4f, 0.6f, Color.black, 0f);
            var counterMat = Materials.Plain("RabbaCounter", new Color(0.55f, 0.08f, 0.08f), 0.45f, 0f, Color.black, 0f);   // Rabba red
            Color[] productColors =
            {
                new Color(0.85f, 0.15f, 0.1f), new Color(0.95f, 0.75f, 0.1f), new Color(0.1f, 0.35f, 0.8f), new Color(0.1f, 0.55f, 0.2f),
                new Color(0.92f, 0.92f, 0.9f), new Color(0.45f, 0.2f, 0.55f), new Color(0.95f, 0.45f, 0.1f), new Color(0.15f, 0.15f, 0.16f),
            };
            var productMats = new Material[productColors.Length];
            for (int i = 0; i < productColors.Length; i++) productMats[i] = Materials.Plain($"RabbaProduct{i}", productColors[i], 0.55f, 0f, Color.black, 0f);
            var sign = Materials.Plain("RabbaSign", Color.white, 0.3f, 0f, Color.white, 12f);   // 30 washed the red letters out
            sign.SetTexture("_BaseColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/City/Signs/rabba_sign.png"));
            sign.SetTexture("_EmissiveColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/City/Signs/rabba_sign_emissive.png"));
            HDMaterial.ValidateMaterial(sign);

            var clerkPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LifeInDowntown.EditorTools.CharacterImport.Root + "/Prefabs/Male_Adult_17.prefab");
            int idx = 0;
            foreach (var s in sites)
            {
                if (s.kind != "rabba") continue;   // the lobby builds itself after the condo (CityGenerator.Lobby.cs)
                var root = new GameObject($"Rabba_{idx++}").transform;
                root.SetParent(parent, false);
                float W = ShopWidth, D = ShopDepth, H = ShopCeiling, F = ShopFloor;
                var walls = new MeshBuilder(); var flr = new MeshBuilder(); var gl = new MeshBuilder(); var mt = new MeshBuilder();
                var pn = new MeshBuilder(); var fg = new MeshBuilder(); var sh = new MeshBuilder(); var ct = new MeshBuilder();
                var prod = new MeshBuilder(); var sg = new MeshBuilder();
                Vector3 al = new Vector3(s.along.x, 0, s.along.y), inw = new Vector3(-s.outward.x, 0, -s.outward.y);
                void B(MeshBuilder mb, float u0, float v0, float y0, float u1, float v1, float y1)
                {
                    var c = s.W((u0 + u1) / 2, (v0 + v1) / 2, (y0 + y1) / 2);
                    Box(mb, c, al * ((u1 - u0) / 2), Vector3.up * ((y1 - y0) / 2), inw * ((v1 - v0) / 2));
                }

                B(flr, -W / 2, 0.05f, 0f, W / 2, D, F);                          // tile floor, level with the sidewalk
                B(walls, -W / 2, 0f, H, W / 2, D, H + 0.12f);                    // ceiling
                B(walls, -W / 2 - 0.12f, 0f, 0f, -W / 2, D, H);                  // side walls
                B(walls, W / 2, 0f, 0f, W / 2 + 0.12f, D, H);
                B(walls, -W / 2, D, 0f, W / 2, D + 0.12f, H);                    // back wall
                // glass front with an open 2.2 m doorway (u -1.3 .. 0.9; 1.6 m was easy to clip), header and posts
                B(gl, -W / 2, 0.05f, F, -1.3f, 0.09f, 3.15f);
                B(gl, 0.9f, 0.05f, F, W / 2, 0.09f, 3.15f);
                B(mt, -W / 2, 0.02f, 3.15f, W / 2, 0.12f, H);
                foreach (float u in new[] { -W / 2, -1.3f, 0.9f, W / 2 }) B(mt, u - 0.05f, 0.02f, F, u + 0.05f, 0.12f, 3.15f);
                // ceiling light panels
                for (int k = 0; k < 3; k++) B(pn, -2.6f, 2.2f + k * 2.8f, H - 0.03f, 2.6f, 2.8f + k * 2.8f, H - 0.01f);
                // back wall drinks fridges: lit interiors, rows of bottles, glass doors
                B(fg, -W / 2 + 0.3f, D - 0.62f, F, W / 2 - 0.3f, D - 0.58f, 2.2f);
                B(mt, -W / 2 + 0.25f, D - 0.7f, 2.2f, W / 2 - 0.25f, D - 0.05f, 2.4f);
                var rnd = new System.Random(77 + idx);
                for (float y = F + 0.25f; y < 2.0f; y += 0.42f)
                    for (float u = -W / 2 + 0.4f; u < W / 2 - 0.4f; u += 0.11f)
                    {
                        int c = rnd.Next(productMats.Length);
                        prod.UseSubmesh(c);
                        B(prod, u, D - 0.56f, y, u + 0.07f, D - 0.48f, y + 0.26f);
                    }
                for (float u = -W / 2 + 0.3f; u < W / 2 - 0.3f - 0.01f; u += 0.92f) B(gl, u, D - 0.73f, F, u + 0.9f, D - 0.71f, 2.2f);
                // two double-sided aisles of shelves full of products
                foreach (float au in new[] { -2.2f, 0.7f })
                {
                    B(sh, au - 0.55f, 3.0f, F, au + 0.55f, 7.6f, F + 0.15f);
                    B(sh, au - 0.05f, 3.0f, F, au + 0.05f, 7.6f, F + 1.55f);
                    for (float y = F + 0.35f; y < F + 1.5f; y += 0.36f)
                    {
                        B(sh, au - 0.55f, 3.0f, y - 0.02f, au + 0.55f, 7.6f, y);
                        foreach (float side in new[] { -1f, 1f })
                            for (float v = 3.05f; v < 7.55f; v += rnd.Next(10, 22) / 100f)
                            {
                                float w = rnd.Next(6, 12) / 100f, h = rnd.Next(8, 22) / 100f;   // snack/can sized
                                prod.UseSubmesh(rnd.Next(productMats.Length));
                                float u0 = side < 0 ? au - 0.5f : au + 0.08f;
                                B(prod, u0 + 0.12f, v, y, u0 + 0.12f + rnd.Next(14, 26) / 100f, v + w, y + h);
                            }
                    }
                }
                // checkout counter right of the door, cigarette wall behind it
                B(ct, 1.7f, 1.1f, F, 3.8f, 1.8f, 1.05f);
                B(mt, 1.7f, 1.05f, 1.05f, 3.85f, 1.85f, 1.1f);
                B(sh, W / 2 - 0.35f, 0.6f, 1.0f, W / 2 - 0.02f, 2.8f, 2.4f);
                for (float y = 1.1f; y < 2.3f; y += 0.12f)
                    for (float v = 0.65f; v < 2.75f; v += 0.09f)
                    {
                        prod.UseSubmesh(rnd.Next(productMats.Length));
                        B(prod, W / 2 - 0.4f, v, y, W / 2 - 0.34f, v + 0.07f, y + 0.09f);
                    }
                // backlit sign above the storefront, outside
                var sc = s.W(0f, -0.08f, 3.95f);
                Vector3 sx = al * 3f, sy = Vector3.up * 0.4f;
                sg.Quad(sc - sx - sy, sc + sx - sy, sc + sx + sy, sc - sx + sy, -inw, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);

                AddObject("RabbaWalls", root, walls.ToMesh("RabbaWalls"), paint, meshes, collider: true);
                AddObject("RabbaFloor", root, flr.ToMesh("RabbaFloor"), floorMat, meshes, collider: true);
                AddObject("RabbaGlass", root, gl.ToMesh("RabbaGlass"), glass, meshes, collider: true);
                AddObject("RabbaFrames", root, mt.ToMesh("RabbaFrames"), metal, meshes, collider: true);
                AddObject("RabbaLightPanels", root, pn.ToMesh("RabbaLightPanels"), panel, meshes, collider: false);
                AddObject("RabbaFridgeGlow", root, fg.ToMesh("RabbaFridgeGlow"), fridgeGlow, meshes, collider: false);
                AddObject("RabbaShelves", root, sh.ToMesh("RabbaShelves"), shelfMat, meshes, collider: true);
                AddObject("RabbaCounter", root, ct.ToMesh("RabbaCounter"), counterMat, meshes, collider: true);
                AddObject("RabbaProducts", root, prod.ToMesh("RabbaProducts"), productMats, meshes, collider: false);
                AddObject("RabbaSign", root, sg.ToMesh("RabbaSign"), sign, meshes, collider: false);

                // bright fluorescent lighting
                foreach (float v in new[] { 2.5f, 6.5f })
                {
                    var lg = new GameObject("RabbaLight");
                    lg.transform.SetParent(root, false);
                    lg.transform.position = s.W(0f, v, H - 0.3f);
                    var l = lg.AddComponent<Light>(); lg.AddComponent<HDAdditionalLightData>();
                    l.type = LightType.Point; l.lightUnit = LightUnit.Lumen; l.intensity = ShopLumens;
                    l.useColorTemperature = true; l.colorTemperature = 4300f; l.color = Color.white; l.range = 12f;
                    l.shadows = v < 3f ? LightShadows.Soft : LightShadows.None;
                }
                // indoor zone (no rain), interior exposure, no outdoor fog
                var zone = new GameObject("RabbaIndoor");
                zone.transform.SetParent(root, false);
                zone.transform.SetPositionAndRotation(s.W(0f, D / 2, H / 2), Quaternion.LookRotation(inw));
                var iz = zone.AddComponent<LifeInDowntown.World.IndoorZone>();
                iz.size = new Vector3(W, H, D);
                var box = zone.AddComponent<BoxCollider>(); box.isTrigger = true; box.size = new Vector3(W + 0.3f, H + 0.4f, D + 0.3f);
                var vol = zone.AddComponent<Volume>(); vol.isGlobal = false; vol.priority = 40f; vol.blendDistance = 0.6f;
                vol.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ShopProfile());

                if (clerkPrefab != null)   // the clerk behind the counter, facing the door
                {
                    var clerk = (GameObject)PrefabUtility.InstantiatePrefab(clerkPrefab, root);
                    clerk.transform.SetPositionAndRotation(s.W(2.75f, 2.35f, F), Quaternion.LookRotation(-inw));
                    var ped = clerk.GetComponent<LifeInDowntown.NPC.Pedestrian>();
                    ped.mode = LifeInDowntown.NPC.Pedestrian.Mode.Idle; ped.role = LifeInDowntown.NPC.Pedestrian.Role.Clerk;
                }
                // E at the counter opens the shop (docs/DESIGN.md 3M); the customer side of the till
                var till = new GameObject("ShopCounter").transform;
                till.SetParent(root, false);
                till.position = s.W(2.75f, 1.3f, F);
                till.gameObject.AddComponent<LifeInDowntown.World.ShopCounter>().shopId = "rabba";
                Debug.Log($"SHOP_OK {s.name} building={s.building} door={s.W(-0.2f, 0f, 0f)}");
            }
        }

        public const float ShopLumens = 2600f;

        static string ShopProfile()
        {
            const string path = "Assets/City/Look/ShopInterior.asset";
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(path) != null) return path;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, path);
            var fog = p.Add<Fog>(true); fog.name = "Fog"; fog.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(fog, p);
            fog.enabled.Override(false);
            var exp = p.Add<Exposure>(true); exp.name = "Exposure"; exp.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(exp, p);
            exp.mode.Override(ExposureMode.Fixed);
            exp.fixedExposure.Override(5.5f);   // bright store interior
            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
            return path;
        }
    }
}
