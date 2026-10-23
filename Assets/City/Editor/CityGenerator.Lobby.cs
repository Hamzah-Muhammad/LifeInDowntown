using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace LifeInDowntown.City
{
    // Pantages Tower's lobby on Shuter St (docs/DESIGN.md 3O.2): a walk-in room cut into the tower beside the
    // Pantages Rabba (same site picking and wall opening as the Rabbas, ShopSites kind "lobby").
    // Glass front and vestibule with a fob reader, stone floor, marble feature wall, a lounge, mailboxes,
    // the concierge desk with CCTV monitors and a concierge, and two elevators on the back wall:
    // left = up to 3801 (the condo door's pair), right = down to P1 (added by the garage).
    public static partial class CityGenerator
    {
        public const string LobbyName = "Pantages Lobby";
        public const float LobbyShift = 1.4f;       // lobby centre along the street wall from the old door point
        const float LobbyCeiling = 4.2f;

        public static bool TryLobbySite(CityData d, out ShopSite site)
        {
            foreach (var s in ShopSites(d)) if (s.kind == "lobby") { site = s; return true; }
            site = default; return false;
        }

        static void BuildLobby(CityData d, Transform parent, Materials mats, List<Mesh> meshes)
        {
            if (!TryLobbySite(d, out var s)) { Debug.LogWarning("CITY_WARN no lobby site"); return; }
            var root = new GameObject("PantagesLobby").transform;
            root.SetParent(parent, false);
            float W = ShopWidth, D = ShopDepth, H = LobbyCeiling, F = ShopFloor;
            Vector3 al = new Vector3(s.along.x, 0, s.along.y), inw = new Vector3(-s.outward.x, 0, -s.outward.y);

            var stone = Materials.Lit("LobbyStoneFloor", "marble_01", 1.6f, new Color(0.78f, 0.76f, 0.72f));
            var marble = Materials.Lit("LobbyFeatureMarble", "marble_01", 2.4f, new Color(0.42f, 0.4f, 0.38f));
            var paint = Materials.Plain("LobbyWall", new Color(0.66f, 0.63f, 0.58f), 0.25f, 0f, Color.black, 0f);
            var wood = Materials.Lit("LobbyWood", "black_oak_veneer", 1.2f, Color.white);
            var steel = Materials.Plain("LobbySteel", new Color(0.62f, 0.63f, 0.64f), 0.82f, 1f, Color.black, 0f);
            var desk = Materials.Plain("LobbyDeskStone", new Color(0.12f, 0.12f, 0.13f), 0.7f, 0f, Color.black, 0f);
            var glass = Materials.GlassMaterial("LobbyGlass", new Color(0.55f, 0.62f, 0.64f, 0.12f));
            var cove = Materials.Plain("LobbyCove", Color.white, 0.2f, 0f, new Color(1f, 0.8f, 0.58f), 22f);
            var screen = Materials.Plain("LobbyCctv", new Color(0.05f, 0.06f, 0.07f), 0.8f, 0f, new Color(0.5f, 0.55f, 0.58f), 5f);   // grey CCTV glow, not a blue slab
            var amber = Materials.Plain("LobbyFloorDisplay", Color.black, 0.5f, 0f, new Color(1f, 0.62f, 0.2f), 14f);
            var fob = Materials.Plain("LobbyFobLight", Color.black, 0.5f, 0f, new Color(0.3f, 1f, 0.4f), 10f);

            var walls = new MeshBuilder(); var flr = new MeshBuilder(); var gl = new MeshBuilder(); var mt = new MeshBuilder();
            var mb = new MeshBuilder(); var wd = new MeshBuilder(); var dk = new MeshBuilder(); var cv = new MeshBuilder();
            var sc = new MeshBuilder(); var am = new MeshBuilder(); var fb = new MeshBuilder(); var stl = new MeshBuilder();
            void B(MeshBuilder m, float u0, float v0, float y0, float u1, float v1, float y1)
            {
                var c = s.W((u0 + u1) / 2, (v0 + v1) / 2, (y0 + y1) / 2);
                Box(m, c, al * ((u1 - u0) / 2), Vector3.up * ((y1 - y0) / 2), inw * ((v1 - v0) / 2));
            }

            // shell
            B(flr, -W / 2, 0.05f, 0f, W / 2, D, F);
            B(walls, -W / 2, 0f, H, W / 2, D, H + 0.12f);
            B(walls, -W / 2 - 0.12f, 0f, 0f, -W / 2, D, H);
            B(walls, W / 2, 0f, 0f, W / 2 + 0.12f, D, H);
            B(mb, -W / 2, D, 0f, W / 2, D + 0.12f, H);                                  // marble feature wall behind the elevators
            // street front: glass with a 2 m doorway, then the vestibule's inner glass and its doorway
            B(gl, -W / 2, 0.05f, F, -1f, 0.09f, 3.3f); B(gl, 1f, 0.05f, F, W / 2, 0.09f, 3.3f);
            B(mt, -W / 2, 0.02f, 3.3f, W / 2, 0.12f, H);
            foreach (float u in new[] { -W / 2, -1f, 1f, W / 2 }) B(mt, u - 0.05f, 0.02f, F, u + 0.05f, 0.12f, 3.3f);
            B(gl, -W / 2, 2.38f, F, -0.9f, 2.42f, 2.9f); B(gl, 0.9f, 2.38f, F, W / 2, 2.42f, 2.9f);
            B(mt, -W / 2, 2.35f, 2.9f, W / 2, 2.45f, 3.1f);
            foreach (float u in new[] { -0.9f, 0.9f }) B(mt, u - 0.04f, 2.35f, F, u + 0.04f, 2.45f, 2.9f);
            B(mt, 1.15f, 2.2f, F, 1.25f, 2.3f, 1.25f); B(fb, 1.16f, 2.18f, 1.12f, 1.24f, 2.19f, 1.2f);   // fob reader post
            // LED cove round the ceiling, canopy glow outside
            B(cv, -W / 2 + 0.1f, 0.3f, H - 0.06f, W / 2 - 0.1f, 0.4f, H - 0.02f);
            B(cv, -W / 2 + 0.1f, D - 0.4f, H - 0.06f, W / 2 - 0.1f, D - 0.3f, H - 0.02f);
            B(cv, -W / 2 + 0.1f, 0.3f, H - 0.06f, -W / 2 + 0.2f, D - 0.3f, H - 0.02f);
            B(cv, W / 2 - 0.2f, 0.3f, H - 0.06f, W / 2 - 0.1f, D - 0.3f, H - 0.02f);
            B(cv, -1.6f, -0.9f, 3.55f, 1.6f, -0.1f, 3.6f);

            // concierge desk (right of the vestibule door), CCTV monitors on it
            B(dk, 1.4f, 4.6f, F, 3.6f, 5.0f, 1.1f);
            B(dk, 3.3f, 5.0f, F, 3.6f, 6.6f, 1.1f);
            B(mt, 1.35f, 4.55f, 1.1f, 3.65f, 5.05f, 1.14f);
            foreach (float u in new[] { 2.0f, 2.85f })
            {
                B(sc, u - 0.32f, 5.1f, 1.18f, u + 0.32f, 5.14f, 1.54f);
                B(dk, u - 0.05f, 5.14f, 1.1f, u + 0.05f, 5.22f, 1.2f);
            }
            // mailboxes on the left wall: 8 x 6 little doors
            B(mt, -W / 2 + 0.02f, 2.9f, 0.35f, -W / 2 + 0.2f, 5.3f, 2.1f);
            for (int r = 0; r < 6; r++)
                for (int c2 = 0; c2 < 8; c2++)
                {
                    float v0 = 2.95f + c2 * 0.29f, y0 = 0.4f + r * 0.28f;
                    B(stl, -W / 2 + 0.2f, v0, y0, -W / 2 + 0.22f, v0 + 0.26f, y0 + 0.25f);
                }
            // parcel room door (closed)
            B(wd, -W / 2 + 0.01f, 6.0f, F, -W / 2 + 0.06f, 7.1f, 2.3f);
            B(mt, -W / 2 + 0.06f, 6.95f, 1.0f, -W / 2 + 0.1f, 7.0f, 1.18f);
            // elevators on the marble wall: steel doors, frames, call buttons, floor displays
            foreach (float uc in new[] { -1.55f, 1.55f })
            {
                B(stl, uc - 0.65f, D - 0.04f, F, uc + 0.65f, D, 2.3f);
                B(mt, uc - 0.75f, D - 0.08f, F, uc - 0.65f, D, 2.4f); B(mt, uc + 0.65f, D - 0.08f, F, uc + 0.75f, D, 2.4f);
                B(mt, uc - 0.75f, D - 0.08f, 2.3f, uc + 0.75f, D, 2.4f);
                B(am, uc - 0.22f, D - 0.05f, 2.52f, uc + 0.22f, D - 0.04f, 2.68f);
            }
            B(mt, -0.12f, D - 0.06f, 1.0f, 0.12f, D, 1.4f);
            B(fb, -0.04f, D - 0.07f, 1.25f, 0.04f, D - 0.06f, 1.31f); B(am, -0.04f, D - 0.07f, 1.08f, 0.04f, D - 0.06f, 1.14f);

            AddObject("LobbyWalls", root, walls.ToMesh("LobbyWalls"), paint, meshes, collider: true);
            AddObject("LobbyFloor", root, flr.ToMesh("LobbyFloor"), stone, meshes, collider: true);
            AddObject("LobbyMarble", root, mb.ToMesh("LobbyMarble"), marble, meshes, collider: true);
            AddObject("LobbyGlass", root, gl.ToMesh("LobbyGlass"), glass, meshes, collider: true);
            AddObject("LobbyFrames", root, mt.ToMesh("LobbyFrames"), mats.Pole, meshes, collider: true);
            AddObject("LobbySteel", root, stl.ToMesh("LobbySteel"), steel, meshes, collider: true);
            AddObject("LobbyWood", root, wd.ToMesh("LobbyWoodDoor"), wood, meshes, collider: true);
            AddObject("LobbyDesk", root, dk.ToMesh("LobbyDesk"), desk, meshes, collider: true);
            AddObject("LobbyCove", root, cv.ToMesh("LobbyCove"), cove, meshes, collider: false);
            AddObject("LobbyCctv", root, sc.ToMesh("LobbyCctv"), screen, meshes, collider: false);
            AddObject("LobbyDisplays", root, am.ToMesh("LobbyDisplays"), amber, meshes, collider: false);
            AddObject("LobbyFob", root, fb.ToMesh("LobbyFob"), fob, meshes, collider: false);

            // lounge: the condo's furniture sets (sofa facing the room, chair, table, plants)
            Furnish(root, "sofa_03", s.W(-3.35f, 7.6f, F), al);
            Furnish(root, "modern_coffee_table_01", s.W(-2.2f, 7.6f, F), al);
            Furnish(root, "modern_arm_chair_01", s.W(-1.0f, 7.0f, F), -al);
            Furnish(root, "potted_plant_01", s.W(-3.5f, 9.4f, F), al);
            Furnish(root, "potted_plant_01", s.W(3.5f, 9.4f, F), al);

            // warm downlights
            foreach (var (u, v) in new[] { (-2f, 3.5f), (2f, 3.5f), (-2f, 7.5f), (2f, 7.5f) })
            {
                var lg = new GameObject("LobbyLight");
                lg.transform.SetParent(root, false);
                lg.transform.position = s.W(u, v, H - 0.3f);
                var l = lg.AddComponent<Light>(); lg.AddComponent<HDAdditionalLightData>();
                l.type = LightType.Point; l.lightUnit = LightUnit.Lumen; l.intensity = 1500f;
                l.useColorTemperature = true; l.colorTemperature = 3000f; l.color = Color.white; l.range = 10f;
                l.shadows = v < 4f ? LightShadows.Soft : LightShadows.None;
            }
            var zone = new GameObject("LobbyIndoor");
            zone.transform.SetParent(root, false);
            zone.transform.SetPositionAndRotation(s.W(0f, D / 2, H / 2), Quaternion.LookRotation(inw));
            zone.AddComponent<LifeInDowntown.World.IndoorZone>().size = new Vector3(W, H, D);
            var box = zone.AddComponent<BoxCollider>(); box.isTrigger = true; box.size = new Vector3(W + 0.3f, H + 0.4f, D + 0.3f);
            var vol = zone.AddComponent<Volume>(); vol.isGlobal = false; vol.priority = 40f; vol.blendDistance = 0.6f;
            vol.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ShopProfile());

            // the concierge behind the desk, facing the door, and the desk's "talk" spot
            var who = AssetDatabase.LoadAssetAtPath<GameObject>(LifeInDowntown.EditorTools.CharacterImport.Root + "/Prefabs/" + ConciergeAvatar + ".prefab");
            if (who != null)
            {
                var c = (GameObject)PrefabUtility.InstantiatePrefab(who, root);
                c.name = "Concierge";
                c.transform.SetPositionAndRotation(s.W(2.5f, 5.85f, F), Quaternion.LookRotation(-inw));
                var ped = c.GetComponent<LifeInDowntown.NPC.Pedestrian>();
                ped.mode = LifeInDowntown.NPC.Pedestrian.Mode.Idle; ped.role = LifeInDowntown.NPC.Pedestrian.Role.Clerk; ped.watchPlayer = true; ped.watchRange = 7f; ped.path = null;
            }
            var talk = new GameObject("ConciergeDesk").transform;
            talk.SetParent(root, false);
            talk.position = s.W(2.5f, 4.1f, F);
            talk.gameObject.AddComponent<LifeInDowntown.World.Concierge>();

            // elevators: up to 3801 pairs with the condo's front door
            var landing = new GameObject("Lobby_ElevatorLanding").transform;
            landing.SetParent(root, false);
            landing.SetPositionAndRotation(s.W(-1.55f, D - 1.2f, F), Quaternion.LookRotation(-inw));
            var up = new GameObject("Lobby_Elevator_Up").transform;
            up.SetParent(root, false);
            up.SetPositionAndRotation(s.W(-1.55f, D - 0.6f, F), Quaternion.LookRotation(-inw));
            var upDoor = up.gameObject.AddComponent<LifeInDowntown.World.DoorLink>();
            upDoor.prompt = "Take the elevator up to 3801";
            upDoor.radius = 1.3f;
            var condoDoor = GameObject.Find("CondoDoor_Inside");
            if (condoDoor != null)
            {
                upDoor.exit = condoDoor.transform;
                condoDoor.GetComponent<LifeInDowntown.World.DoorLink>().exit = landing;
            }
            Debug.Log($"LOBBY_OK building={s.building} door={s.W(0f, 0f, 0f)} desk={s.W(2.5f, 4.8f, 0f)}");
        }

        public const string ConciergeAvatar = "Female_Adult_06";

        static void Furnish(Transform parent, string id, Vector3 at, Vector3 front)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FurniturePrefab(id));
            if (prefab == null) return;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.rotation = Quaternion.LookRotation(front);   // the condo sets all face +Z (CondoFurnitureImport.ReportFacing)
            go.transform.position = Vector3.zero;
            var b = LifeInDowntown.EditorTools.CondoFurnitureImport.Bounds(go);
            go.transform.position = new Vector3(at.x - b.center.x, at.y - b.min.y, at.z - b.center.z);
        }
    }
}
