using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace LifeInDowntown.City
{
    // Tarik's 1-bedroom condo (docs/DESIGN.md 3H), built INSIDE the real Pantages Tower against its long
    // south wall on floor 38 (~120 m). The tower's facade only renders from outside, so the condo's
    // floor-to-ceiling windows look out over the generated city. Also the distant skyline and CN Tower.
    public static partial class CityGenerator
    {
        public const string CondoTower = "Pantages Tower";
        public const float CondoFloorY = 120f, CondoWidth = 10f, CondoDepth = 7.5f, CondoCeiling = 2.75f;
        const float WallT = 0.12f;

        // local frame: u along the window wall (-W/2..W/2), v inward from the glass (0..D), y up from the floor
        public struct CondoFrame
        {
            public Vector3 origin, along, inward, outwardGround;   // outwardGround: the street-side door point at y=0
            public Vector3 W(float u, float v, float y) => origin + along * u + inward * v + Vector3.up * y;
            public Quaternion Facing(Vector3 localDir) => Quaternion.LookRotation(along * localDir.x + inward * localDir.z);
        }

        public static bool TryCondoFrame(CityData d, out CondoFrame f)
        {
            f = default;
            Building tower = null;
            foreach (var b in d.buildings) if (b.name == CondoTower && !b.part) tower = b;
            if (tower == null) return false;
            var ring = CityDataUtil.Points(tower.pts);
            if (Triangulator.SignedArea(ring) < 0f) ring.Reverse();
            float best = 0f;
            for (int i = 0; i < ring.Count; i++)
            {
                Vector2 a = ring[i], c = ring[(i + 1) % ring.Count], e = c - a;
                var outward = new Vector2(e.y, -e.x).normalized;
                if (outward.y > -0.6f) continue;                          // the south-facing walls only
                if (e.magnitude <= best) continue;
                best = e.magnitude;
                var mid = (a + c) * 0.5f;
                var along2 = e.normalized;
                // viewer inside faces the glass (-inward): +u should be to their right
                var inward = new Vector3(-outward.x, 0f, -outward.y);
                var along = new Vector3(along2.x, 0f, along2.y);
                if (Vector3.Dot(Vector3.Cross(Vector3.up, -inward), along) < 0f) along = -along;
                f.inward = inward;
                f.along = along;
                f.origin = new Vector3(mid.x, CondoFloorY, mid.y) + inward * 0.35f;
                f.outwardGround = new Vector3(mid.x, 0f, mid.y) - inward * 0.7f;
            }
            if (best < CondoWidth + 1f) return false;
            // every room corner must sit inside the real footprint
            foreach (var (u, v) in new[] { (-CondoWidth / 2, 0f), (CondoWidth / 2, 0f), (-CondoWidth / 2, CondoDepth), (CondoWidth / 2, CondoDepth) })
            {
                var p = f.W(u, v, 0f);
                if (!PointInPolygon(new Vector2(p.x, p.z), ring)) { Debug.LogWarning($"CITY_WARN condo corner ({u},{v}) outside {CondoTower}"); return false; }
            }
            return true;
        }

        // ------------------------------------------------------------------ condo
        static void BuildCondo(CityData d, Transform parent, Materials mats, List<Mesh> meshes)
        {
            if (!TryCondoFrame(d, out var f)) { Debug.LogWarning("CITY_WARN no condo frame"); return; }
            var root = new GameObject("Condo").transform;
            root.SetParent(parent, false);
            var m = new CondoMats();

            var shell = new MeshBuilder(); var floor = new MeshBuilder(); var ceiling = new MeshBuilder();
            var glass = new MeshBuilder(); var metal = new MeshBuilder(); var cab = new MeshBuilder();
            var counter = new MeshBuilder(); var tile = new MeshBuilder(); var fabric = new MeshBuilder();
            var white = new MeshBuilder(); var black = new MeshBuilder(); var wood = new MeshBuilder();
            float W = CondoWidth, D = CondoDepth, H = CondoCeiling;

            void B(MeshBuilder mb, float u0, float v0, float y0, float u1, float v1, float y1)
            {
                var c = f.W((u0 + u1) * 0.5f, (v0 + v1) * 0.5f, (y0 + y1) * 0.5f);
                Box(mb, c, f.along * ((u1 - u0) * 0.5f), Vector3.up * ((y1 - y0) * 0.5f), f.inward * ((v1 - v0) * 0.5f));
            }
            // a wall from (u0,v0) to (u1,v1), axis-aligned in the local frame, with door gaps (from,to) along it
            void Wall(float u0, float v0, float u1, float v1, params (float a, float b)[] gaps)
            {
                bool alongU = Mathf.Abs(v1 - v0) < 0.01f;
                float s0 = alongU ? u0 : v0, s1 = alongU ? u1 : v1, t = alongU ? v0 : u0;
                var cuts = new List<float> { s0 };
                foreach (var g in gaps) { cuts.Add(g.a); cuts.Add(g.b); }
                cuts.Add(s1);
                for (int i = 0; i + 1 < cuts.Count; i += 2)
                    if (cuts[i + 1] - cuts[i] > 0.01f)
                        if (alongU) B(shell, cuts[i], t - WallT / 2, 0f, cuts[i + 1], t + WallT / 2, H);
                        else B(shell, t - WallT / 2, cuts[i], 0f, t + WallT / 2, cuts[i + 1], H);
                foreach (var g in gaps)   // lintel over each opening
                    if (alongU) B(shell, g.a, t - WallT / 2, 2.1f, g.b, t + WallT / 2, H);
                    else B(shell, t - WallT / 2, g.a, 2.1f, t + WallT / 2, g.b, H);
            }

            // slab, ceiling, outer walls (the glass wall is v = 0)
            B(floor, -W / 2, 0f, -0.3f, W / 2, D, 0f);
            B(ceiling, -W / 2, 0f, H, W / 2, D, H + 0.3f);
            Wall(-W / 2, D, W / 2, D, (-0.55f, 0.45f));            // back wall, front door gap
            Wall(-W / 2, 0f, -W / 2, D);                            // left
            Wall(W / 2, 0f, W / 2, D);                              // right
            // glass wall: sill, header, mullions, double-sided glass
            B(metal, -W / 2, -0.06f, 0f, W / 2, 0.06f, 0.12f);
            B(metal, -W / 2, -0.06f, H - 0.14f, W / 2, 0.06f, H);
            for (float u = -W / 2; u <= W / 2 + 0.01f; u += W / 8f) B(metal, u - 0.03f, -0.05f, 0f, u + 0.03f, 0.05f, H);
            var g0 = f.W(-W / 2, 0f, 0.12f); var g1 = f.W(W / 2, 0f, 0.12f); var g2 = f.W(W / 2, 0f, H - 0.14f); var g3 = f.W(-W / 2, 0f, H - 0.14f);
            glass.Quad(g0, g1, g2, g3, f.inward, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            glass.Quad(g0, g1, g2, g3, -f.inward, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);

            // bedroom (right, by the window) and bathroom (right, at the back)
            Wall(1.5f, 0f, 1.5f, 4.2f, (3.1f, 4.0f));               // bedroom door near the hallway
            Wall(1.5f, 4.2f, W / 2, 4.2f);
            Wall(2.5f, 4.2f, 2.5f, D, (4.45f, 5.3f));               // bathroom door
            B(tile, 2.5f, 4.2f, 0.001f, W / 2, D, 0.012f);          // bathroom floor tiles

            // kitchen along the back wall (left of the door) + island
            B(cab, -4.9f, D - 0.66f, 0f, -1.2f, D - 0.06f, 0.88f);
            B(counter, -4.92f, D - 0.68f, 0.88f, -1.18f, D - 0.06f, 0.92f);
            B(cab, -4.9f, D - 0.4f, 1.5f, -1.95f, D - 0.06f, 2.3f);
            B(metal, -4.95f, D - 0.75f, 0f, -4.92f, D - 0.06f, 2.1f);   // fridge side panel
            B(white, -4.9f, D - 0.72f, 0f, -4.1f, D - 0.06f, 2.05f);    // fridge (white = panel-ready)
            B(black, -3.5f, D - 0.6f, 0.921f, -2.9f, D - 0.12f, 0.926f); // induction cooktop
            B(cab, -4.2f, 4.6f, 0f, -1.8f, 5.45f, 0.9f);                 // island base
            B(counter, -4.35f, 4.5f, 0.9f, -1.65f, 5.55f, 0.94f);        // island top (overhang)

            // bed (queen) against the right wall, headboard on the wall
            B(wood, 2.95f, 1.15f, 0f, 4.94f, 2.85f, 0.32f);
            B(white, 3.0f, 1.2f, 0.32f, 4.9f, 2.8f, 0.56f);
            B(fabric, 2.98f, 1.18f, 0.56f, 4.35f, 2.82f, 0.63f);         // duvet, folded back below the pillows
            B(white, 4.4f, 1.3f, 0.56f, 4.85f, 1.95f, 0.7f);             // pillows
            B(white, 4.4f, 2.05f, 0.56f, 4.85f, 2.7f, 0.7f);
            B(wood, 4.9f, 1.05f, 0f, 4.98f, 2.95f, 1.15f);               // headboard

            // bathroom fixtures
            B(cab, 4.4f, 5.2f, 0f, 4.94f, 6.4f, 0.82f);                  // vanity
            B(counter, 4.35f, 5.15f, 0.82f, 4.94f, 6.45f, 0.86f);
            B(metal, 4.92f, 5.3f, 1.05f, 4.94f, 6.3f, 1.95f);            // mirror
            B(white, 2.75f, 6.9f, 0f, 3.2f, 7.4f, 0.42f);                // toilet
            B(white, 2.78f, 7.25f, 0.42f, 3.17f, 7.42f, 0.8f);           // cistern
            B(tile, 3.6f, D - 0.08f, 0f, W / 2 - 0.07f, D - 0.06f, 2.2f); // tiled shower back wall (thin panel)
            B(tile, W / 2 - 0.08f, 6.35f, 0f, W / 2 - 0.06f, D - 0.06f, 2.2f); // tiled shower side wall
            B(glass, 3.55f, 6.35f, 0f, 3.58f, 7.4f, 2.0f);               // shower screen

            // living: TV wall unit, rug, (furniture models go here once downloaded: section 3H)
            B(black, -4.94f, 2.2f, 1.0f, -4.9f, 3.65f, 1.82f);           // 65" TV
            if (!File.Exists(FurniturePrefab("modern_wooden_cabinet")))
                B(wood, -4.94f, 1.9f, 0f, -4.5f, 3.95f, 0.45f);          // low media cabinet (until the model is imported)
            B(fabric, -3.7f, 1.3f, 0.002f, -1.1f, 4.1f, 0.01f);          // rug
            // front door (closed panel, the interaction point is just inside it)
            B(wood, -0.55f, D - 0.04f, 0f, 0.45f, D + 0.02f, 2.08f);

            var add = new (string name, MeshBuilder mb, Material mat, bool col)[]
            {
                ("CondoShell", shell, m.Paint, true), ("CondoFloor", floor, m.Floor, true), ("CondoCeiling", ceiling, m.Ceiling, true),
                ("CondoGlass", glass, m.Glass, true), ("CondoMetal", metal, m.Metal, true), ("CondoCabinets", cab, m.Cabinet, true),
                ("CondoCounters", counter, m.Counter, true), ("CondoTile", tile, m.Tile, true), ("CondoFabric", fabric, m.Fabric, false),
                ("CondoWhite", white, m.White, true), ("CondoBlack", black, m.Black, true), ("CondoWood", wood, m.Wood, true),
            };
            foreach (var (name, mb, mat, col) in add)
                if (mb.VertexCount > 0) AddObject(name, root, mb.ToMesh(name), mat, meshes, col);

            int placed = PlaceFurniture(f, root);

            // warm interior lights (3000 K); the living light casts shadows
            void Lamp(string name, float u, float v, float lumens, bool shadows)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                go.transform.position = f.W(u, v, H - 0.25f);
                var l = go.AddComponent<Light>();
                go.AddComponent<HDAdditionalLightData>();
                l.type = LightType.Point; l.lightUnit = LightUnit.Lumen; l.intensity = lumens;
                l.useColorTemperature = true; l.colorTemperature = 3000f; l.color = Color.white; l.range = 9f;
                l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            }
            Lamp("LightLiving", -2.4f, 2.6f, CondoLumens, true);
            Lamp("LightKitchen", -3.0f, 5.9f, CondoLumens * 0.7f, false);
            Lamp("LightBedroom", 3.2f, 2.0f, CondoLumens * 0.55f, false);
            Lamp("LightBath", 3.7f, 5.8f, CondoLumens * 0.45f, false);
            Lamp("LightEntry", 0f, 6.6f, CondoLumens * 0.35f, false);

            // inside the condo: clearer air for the skyline and an interior exposure
            var volGo = new GameObject("CondoVolume");
            volGo.transform.SetParent(root, false);
            volGo.transform.SetPositionAndRotation(f.W(0f, D / 2, H / 2), Quaternion.LookRotation(f.inward));
            var box = volGo.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(W + 0.4f, H + 0.6f, D + 0.4f);
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = false; vol.priority = 40f; vol.blendDistance = 0.5f;
            vol.sharedProfile = CondoProfile();

            // doors: street entrance <-> condo entry (runtime: LifeInDowntown.World.DoorLink)
            var outside = new GameObject("CondoDoor_Street").transform;
            outside.SetParent(root, false);
            outside.SetPositionAndRotation(f.outwardGround, Quaternion.LookRotation(-f.inward));
            var inside = new GameObject("CondoDoor_Inside").transform;
            inside.SetParent(root, false);
            inside.SetPositionAndRotation(f.W(-0.05f, D - 0.7f, 0f), Quaternion.LookRotation(-f.inward));
            var streetDoor = outside.gameObject.AddComponent<LifeInDowntown.World.DoorLink>();
            streetDoor.prompt = "Take the elevator up to 3801";
            streetDoor.exit = inside;
            var condoDoor = inside.gameObject.AddComponent<LifeInDowntown.World.DoorLink>();
            condoDoor.prompt = "Take the elevator down to Shuter St";
            condoDoor.exit = outside;

            // a lit glass entrance on the street side so the door is findable
            var lobby = new MeshBuilder(); var lobbyGlow = new MeshBuilder();
            Vector3 dc = f.outwardGround + f.inward * 0.62f, side = f.along;
            Box(lobby, dc + Vector3.up * 1.2f, side * 1.3f, Vector3.up * 1.2f, f.inward * 0.05f);
            Box(lobbyGlow, dc + Vector3.up * 2.55f - f.inward * 0.05f, side * 1.4f, Vector3.up * 0.08f, f.inward * 0.06f);
            AddObject("LobbyDoors", root, lobby.ToMesh("LobbyDoors"), m.Glass, meshes, collider: false);
            AddObject("LobbyCanopyLight", root, lobbyGlow.ToMesh("LobbyCanopyLight"), mats.Lamp, meshes, collider: false);

            Debug.Log($"CONDO_OK tower={CondoTower} floorY={CondoFloorY} origin={f.origin} window faces {(-f.inward)} furniture={placed}");
        }

        // ------------------------------------------------------------- furniture
        static string FurniturePrefab(string id) => $"Assets/Condo/Prefabs/{id}.prefab";

        enum Align { Floor, Ceiling, Centre }

        // Poly Haven models face +Z. Each entry: model, (u, v) in the condo frame, the local direction its
        // front should face (x = along u, z = along v), how to set its height, and an extra height (m).
        static readonly (string id, float u, float v, Vector3 front, Align align, float y)[] Furniture =
        {
            ("modern_wooden_cabinet",    -4.68f, 2.93f, new Vector3(1, 0, 0),      Align.Floor,   0f),     // media unit under the TV
            ("sofa_03",                  -1.45f, 2.93f, new Vector3(-1, 0, 0),     Align.Floor,   0f),     // facing the TV
            ("modern_coffee_table_01",   -2.85f, 2.93f, new Vector3(1, 0, 0),      Align.Floor,   0f),
            ("potted_plant_04",          -2.85f, 3.25f, new Vector3(1, 0, 0),      Align.Floor,   0.39f),  // on the coffee table
            // facing the skyline. All seating faces +Z, measured by CondoFurnitureImport.ReportFacing
            // (highest backrest vertex at -Z); a top-down render misread once suggested otherwise
            ("mid_century_lounge_chair", -3.85f, 0.95f, new Vector3(-0.3f, 0, -1), Align.Floor,   0f),
            ("side_table_01",            -2.95f, 0.7f,  new Vector3(0, 0, 1),      Align.Floor,   0f),
            ("modern_arm_chair_01",       0.35f, 1.25f, new Vector3(-1, 0, 0.45f), Align.Floor,   0f),
            ("bar_chair_round_01",       -3.6f,  4.15f, new Vector3(0, 0, 1),      Align.Floor,   0f),     // island stools
            ("bar_chair_round_01",       -2.4f,  4.15f, new Vector3(0, 0, 1),      Align.Floor,   0f),
            ("modern_ceiling_lamp_01",   -2.85f, 2.93f, new Vector3(1, 0, 0),      Align.Ceiling, 0f),
            ("potted_plant_01",          -4.5f,  4.5f,  new Vector3(1, 0, 0),      Align.Floor,   0f),
            ("painted_wooden_nightstand", 4.62f, 3.25f, new Vector3(-1, 0, 0),     Align.Floor,   0f),
            ("hanging_picture_frame_01",  1.43f, 2.3f,  new Vector3(-1, 0, 0),     Align.Centre,  1.55f),  // living side of the bedroom wall
        };

        static int PlaceFurniture(CondoFrame f, Transform parent)
        {
            int n = 0;
            foreach (var it in Furniture)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FurniturePrefab(it.id));
                if (prefab == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                go.transform.rotation = f.Facing(it.front.normalized);
                go.transform.position = Vector3.zero;
                var b = LifeInDowntown.EditorTools.CondoFurnitureImport.Bounds(go);
                Vector3 target = f.W(it.u, it.v, 0f);
                float y = it.align == Align.Floor ? CondoFloorY + it.y - b.min.y
                        : it.align == Align.Ceiling ? CondoFloorY + CondoCeiling - b.max.y
                        : CondoFloorY + it.y - b.center.y;
                go.transform.position = new Vector3(target.x - b.center.x, y, target.z - b.center.z);
                n++;
            }
            return n;
        }

        public const float CondoLumens = 600f;   // 1400 blew the white walls out at EV 4.2
        public const float CondoExposureEV = 4.2f;

        static VolumeProfile CondoProfile()
        {
            const string path = "Assets/City/Look/CondoInterior.asset";
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(path) != null) AssetDatabase.DeleteAsset(path);
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, path);
            T Add<T>() where T : VolumeComponent { var c = p.Add<T>(true); c.name = typeof(T).Name; c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(c, p); return c; }
            // no fog from inside: even a thin 1600 m fog lit the distant skyline into flat pink slabs
            var fog = Add<Fog>();
            fog.enabled.Override(false);
            var exp = Add<Exposure>();
            exp.mode.Override(ExposureMode.Fixed);
            exp.fixedExposure.Override(CondoExposureEV);
            EditorUtility.SetDirty(p);
            return p;
        }

        // materials for the condo: Poly Haven sets if downloaded (section 3H), plain finishes otherwise
        class CondoMats
        {
            public readonly Material Paint, Ceiling, Floor, Glass, Metal, Cabinet, Counter, Tile, Fabric, White, Black, Wood;
            public CondoMats()
            {
                Paint = Materials.Plain("CondoPaint", new Color(0.86f, 0.85f, 0.82f), 0.15f, 0f, Color.black, 0f);
                Ceiling = Materials.Plain("CondoCeiling", new Color(0.92f, 0.92f, 0.9f), 0.1f, 0f, Color.black, 0f);
                Floor = Opt("CondoFloor", "laminate_floor_02", 2f, new Color(0.42f, 0.32f, 0.22f), 0.45f);
                Counter = Opt("CondoCounter", "marble_01", 1.5f, new Color(0.9f, 0.9f, 0.88f), 0.85f);
                Tile = Opt("CondoTile", "floor_tiles_06", 1.2f, new Color(0.7f, 0.71f, 0.72f), 0.6f);
                Cabinet = Opt("CondoCabinet", "black_oak_veneer", 1.5f, new Color(0.12f, 0.11f, 0.1f), 0.4f);
                Wood = Opt("CondoWood", "black_oak_veneer", 1.5f, new Color(0.18f, 0.14f, 0.11f), 0.35f);
                Fabric = Opt("CondoFabric", "fabric_pattern_07", 0.8f, new Color(0.24f, 0.27f, 0.32f), 0.1f);
                Metal = Materials.Plain("CondoMetal", new Color(0.08f, 0.08f, 0.09f), 0.75f, 1f, Color.black, 0f);
                White = Materials.Plain("CondoWhite", new Color(0.93f, 0.93f, 0.92f), 0.55f, 0f, Color.black, 0f);
                Black = Materials.Plain("CondoBlack", new Color(0.02f, 0.02f, 0.025f), 0.92f, 0f, Color.black, 0f);
                Glass = GlassMat();
            }

            static Material Opt(string name, string texSet, float tile, Color plain, float smooth) =>
                Directory.Exists($"Assets/City/Textures/{texSet}")
                    ? Materials.Lit(name, texSet, tile, Color.white)
                    : Materials.Plain(name, plain, smooth, 0f, Color.black, 0f);

            static Material GlassMat()
            {
                const string path = MaterialDir + "/CondoGlass.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) { mat = new Material(Shader.Find("HDRP/Lit")); AssetDatabase.CreateAsset(mat, path); }
                mat.SetFloat("_SurfaceType", 1f);
                mat.SetFloat("_BlendMode", 0f);
                mat.SetColor("_BaseColor", new Color(0.55f, 0.62f, 0.66f, 0.1f));
                mat.SetFloat("_Smoothness", 0.97f);
                mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_DoubleSidedEnable", 1f);
                HDMaterial.ValidateMaterial(mat);
                EditorUtility.SetDirty(mat);
                return mat;
            }
        }

        // --------------------------------------------------------------- skyline
        // Real CN Tower position relative to Yonge & Dundas (43.6426 N, 79.3871 W): ~0.5 km west, 1.5 km south.
        static readonly Vector2 CnTower = new Vector2(-499f, -1514f);

        // Beyond the mapped blocks: a seeded ring of distant lit towers (taller and denser to the south,
        // the Financial District) and the CN Tower, so the condo windows show a Toronto skyline.
        static void BuildSkyline(Transform parent, Materials mats, List<Mesh> meshes)
        {
            var root = new GameObject("Skyline").transform;
            root.SetParent(parent, false);
            // lit windows on the night-office facade (also lights up the near towers using it)
            var night = mats.FacadeNight;
            string em = "Assets/City/Textures/Facade009/Facade009_2K-JPG_Emission.jpg";
            var emTex = AssetDatabase.LoadAssetAtPath<Texture2D>(em);
            if (emTex != null)
            {
                night.SetTexture("_EmissiveColorMap", emTex);
                // HDRP tiles the emission map separately: without this it repeated every 1 m (metre UVs)
                // and every Facade009 tower glowed as a flat lavender slab
                night.SetTextureScale("_EmissiveColorMap", night.GetTextureScale("_BaseColorMap"));
                night.SetColor("_EmissiveColorLDR", Color.white);
                night.SetFloat("_EmissiveIntensity", WindowNits);
                night.SetColor("_EmissiveColor", Color.white * WindowNits);
                HDMaterial.ValidateMaterial(night);
                EditorUtility.SetDirty(night);
            }

            var towers = new MeshBuilder();
            var rnd = new System.Random(1793);   // Toronto (York) founding year
            int count = 0;
            for (int i = 0; i < 420; i++)
            {
                float ang = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float r = 700f + (float)(rnd.NextDouble() * rnd.NextDouble()) * 2600f;
                var pos = new Vector2(Mathf.Sin(ang), Mathf.Cos(ang)) * r;
                bool south = pos.y < -500f && pos.y > -1700f && pos.x > -1300f && pos.x < 900f;   // Financial District / waterfront
                if (!south && rnd.NextDouble() < 0.35) continue;
                float h = south ? 90f + (float)rnd.NextDouble() * 210f : 25f + (float)(rnd.NextDouble() * rnd.NextDouble()) * 140f;
                float wx = 18f + (float)rnd.NextDouble() * 22f, wz = 18f + (float)rnd.NextDouble() * 22f;
                float yaw = -17f * Mathf.Deg2Rad;   // Toronto's street grid is rotated ~17 degrees
                var ax = new Vector3(Mathf.Cos(yaw), 0, -Mathf.Sin(yaw)) * (wx * 0.5f);
                var az = new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw)) * (wz * 0.5f);
                var c = new Vector3(pos.x, h * 0.5f, pos.y);
                TowerBox(towers, c, ax, Vector3.up * (h * 0.5f), az);
                count++;
            }
            AddObject("SkylineTowers", root, towers.ToMesh("SkylineTowers"), night, meshes, collider: false);

            // CN Tower: tapered shaft, main pod at ~342 m, SkyPod at ~447 m, antenna to 553 m
            var cnConcrete = new MeshBuilder(); var cnLights = new MeshBuilder();
            var b0 = new Vector3(CnTower.x, 0f, CnTower.y);
            for (int s = 0; s < 12; s++)
            {
                float y0 = s * 28.5f, y1 = y0 + 28.5f, w = Mathf.Lerp(11f, 5.5f, s / 11f);
                Box(cnConcrete, b0 + Vector3.up * ((y0 + y1) * 0.5f), Vector3.right * w, Vector3.up * 14.25f, Vector3.forward * w);
            }
            Box(cnConcrete, b0 + Vector3.up * 345f, Vector3.right * 19f, Vector3.up * 10f, Vector3.forward * 19f);   // main pod
            Box(cnLights, b0 + Vector3.up * 339f, Vector3.right * 19.4f, Vector3.up * 1.6f, Vector3.forward * 19.4f); // pod light ring
            Box(cnConcrete, b0 + Vector3.up * 400f, Vector3.right * 4.5f, Vector3.up * 45f, Vector3.forward * 4.5f);
            Box(cnConcrete, b0 + Vector3.up * 447f, Vector3.right * 7f, Vector3.up * 4f, Vector3.forward * 7f);       // SkyPod
            Box(cnConcrete, b0 + Vector3.up * 502f, Vector3.right * 1.6f, Vector3.up * 51f, Vector3.forward * 1.6f);  // antenna
            // at night the real tower is floodlit by LEDs: plain grey concrete vanished against the sky
            var cnLit = Materials.Plain("CNTowerLit", new Color(0.5f, 0.5f, 0.52f), 0.3f, 0f, new Color(0.78f, 0.84f, 1f), 22f);
            var cnRing = Materials.Plain("CNTowerRing", Color.white, 0.3f, 0f, new Color(1f, 0.92f, 0.8f), 900f);
            var cnBeacon = Materials.Plain("CNTowerBeacon", Color.red, 0.3f, 0f, new Color(1f, 0.08f, 0.05f), 2500f);
            AddObject("CNTower", root, cnConcrete.ToMesh("CNTower"), cnLit, meshes, collider: false);
            AddObject("CNTowerLights", root, cnLights.ToMesh("CNTowerLights"), cnRing, meshes, collider: false);
            var beacon = new MeshBuilder();
            Box(beacon, b0 + Vector3.up * 556f, Vector3.right * 1.4f, Vector3.up * 1.4f, Vector3.forward * 1.4f);
            AddObject("CNTowerBeacon", root, beacon.ToMesh("CNTowerBeacon"), cnBeacon, meshes, collider: false);
            Debug.Log($"SKYLINE_OK towers={count} cnTower={b0}");
        }

        public const float WindowNits = 45f;

        // box with metre-based UVs on the sides (facade texture tiles correctly), flat roof
        static void TowerBox(MeshBuilder mb, Vector3 c, Vector3 x, Vector3 y, Vector3 z)
        {
            float wx = x.magnitude * 2f, wz = z.magnitude * 2f, h = y.magnitude * 2f, y0 = c.y - h / 2f;
            foreach (var (n, side, w) in new[] { (z, x, wx), (-z, -x, wx), (x, -z, wz), (-x, z, wz) })
            {
                Vector3 f = c + n - Vector3.up * (h / 2f);
                Vector3 a = f - side, b = f + side;
                mb.Quad(a, b, b + Vector3.up * h, a + Vector3.up * h, n,
                        new Vector2(0, y0), new Vector2(w, y0), new Vector2(w, y0 + h), new Vector2(0, y0 + h));
            }
            Vector3 t = c + y;
            mb.Quad(t - x - z, t + x - z, t + x + z, t - x + z, Vector3.up, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
        }
    }
}
