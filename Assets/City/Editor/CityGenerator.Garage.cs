using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace LifeInDowntown.City
{
    // Pantages Tower's P1 parking (docs/DESIGN.md 3O.3), under the lobby, laid out in the lobby's street frame
    // (u along Shuter St, v in under the tower). Concrete slab, columns on a 7.8 m grid with yellow-black bands,
    // 24 painted bays P1-01..P1-24 in two rows either side of a 7 m aisle, fluorescent tubes, pipes, signs,
    // the elevator to the lobby at the west end and the exit lane to the roll-up door at the east end.
    // Tarik's bay P1-17 is a named spot for his car (GarageSpot_P1-17). Cars drive in and out through the
    // roll-up door on Shuter St with a fade (World/GarageGate, added on GarageGate_* by the car setup).
    public static partial class CityGenerator
    {
        public const float GarageFloorY = -3.6f, GarageClear = 2.6f;
        public const string TarikBay = "P1-17";
        const float BayW = 2.6f, BayL = 5.4f, Aisle = 7f, GarageU0 = -20f, GarageU1 = 20f, BaysU0 = -15.6f;
        const float StreetDoorU = -10.5f;   // the lobby's other side from the Pantages Rabba (that one sits at u ~ +8.6)

        public static Vector3 GarageW(ShopSite s, float u, float v, float y) => s.W(u, v, y);
        public static float BayCentreU(int index) => BaysU0 + BayW * ((index % 12) + 0.5f);   // index 0..23
        public static float BayCentreV(int index) => index < 12 ? 0.3f + BayL / 2 : 0.3f + BayL + Aisle + BayL / 2;

        static void BuildGarage(CityData d, Transform parent, Materials mats, List<Mesh> meshes)
        {
            if (!TryLobbySite(d, out var s)) { Debug.LogWarning("CITY_WARN no lobby site, so no garage"); return; }
            var root = new GameObject("PantagesGarage").transform;
            root.SetParent(parent, false);
            float Y = GarageFloorY, top = Y + GarageClear, D = 0.3f + BayL * 2 + Aisle + 0.3f;
            Vector3 al = new Vector3(s.along.x, 0, s.along.y), inw = new Vector3(-s.outward.x, 0, -s.outward.y);

            var concrete = Materials.Lit("GarageConcrete", "brushed_concrete_03", 2.5f, new Color(0.55f, 0.55f, 0.56f), 0.35f, 0.6f);
            var wallMat = Materials.Plain("GarageWall", new Color(0.62f, 0.62f, 0.6f), 0.2f, 0f, Color.black, 0f);
            var paint = Materials.Plain("GarageLinePaint", new Color(0.85f, 0.85f, 0.8f), 0.3f, 0f, Color.black, 0f);
            var yellow = Materials.Plain("GarageYellow", new Color(0.85f, 0.65f, 0.05f), 0.35f, 0f, Color.black, 0f);
            var black = Materials.Plain("GarageBlack", new Color(0.05f, 0.05f, 0.05f), 0.3f, 0f, Color.black, 0f);
            var tube = Materials.Plain("GarageTube", Color.white, 0.2f, 0f, new Color(0.9f, 0.96f, 1f), 60f);
            var pipe = Materials.Plain("GaragePipe", new Color(0.7f, 0.12f, 0.1f), 0.4f, 0.3f, Color.black, 0f);
            var duct = Materials.Plain("GarageDuct", new Color(0.55f, 0.56f, 0.57f), 0.5f, 0.8f, Color.black, 0f);
            var steel = Materials.Plain("LobbySteel", new Color(0.62f, 0.63f, 0.64f), 0.82f, 1f, Color.black, 0f);
            var roll = Materials.Plain("GarageRollUpDoor", new Color(0.42f, 0.43f, 0.44f), 0.45f, 0.7f, Color.black, 0f);
            var signs = Materials.Plain("GarageSigns", Color.white, 0.3f, 0f, Color.white, 10f);
            signs.SetTexture("_BaseColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/City/Signs/garage_signs.png"));
            signs.SetTexture("_EmissiveColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/City/Signs/garage_signs_emissive.png"));
            HDMaterial.ValidateMaterial(signs);

            var flr = new MeshBuilder(); var wl = new MeshBuilder(); var ln = new MeshBuilder(); var yl = new MeshBuilder();
            var bk = new MeshBuilder(); var tb = new MeshBuilder(); var pp = new MeshBuilder(); var dc = new MeshBuilder();
            var st = new MeshBuilder(); var rd = new MeshBuilder(); var sg = new MeshBuilder();
            void B(MeshBuilder m, float u0, float v0, float y0, float u1, float v1, float y1)
            {
                var c = s.W((u0 + u1) / 2, (v0 + v1) / 2, (y0 + y1) / 2);
                Box(m, c, al * ((u1 - u0) / 2), Vector3.up * ((y1 - y0) / 2), inw * ((v1 - v0) / 2));
            }
            // a sign plate: atlas cell k (4 x 8), facing 'face' (+inw or -inw / +al ...), centred at u,v,y
            void Sign(int k, float u, float v, float y, float w, Vector3 face)
            {
                var c = s.W(u, v, y);
                Vector3 right = Vector3.Cross(face, Vector3.up).normalized * (w / 2), up = Vector3.up * (w / 4);   // the reader's right
                float cu = (k % 4) / 4f, cv = 1f - (k / 4 + 1) / 8f;
                sg.Quad(c - right - up, c + right - up, c + right + up, c - right + up, face,
                        new Vector2(cu, cv), new Vector2(cu + 0.25f, cv), new Vector2(cu + 0.25f, cv + 0.125f), new Vector2(cu, cv + 0.125f));
            }

            // slab, ceiling, walls
            B(flr, GarageU0, 0f, Y - 0.3f, GarageU1, D, Y);
            B(wl, GarageU0, 0f, top, GarageU1, D, top + 0.35f);
            B(wl, GarageU0, -0.25f, Y, GarageU1, 0f, top);
            B(wl, GarageU0, D, Y, GarageU1, D + 0.25f, top);
            B(wl, GarageU0 - 0.25f, 0f, Y, GarageU0, D, top);
            // east end: the roll-up door at the end of the aisle, wall either side
            float a0 = 0.3f + BayL, a1 = a0 + Aisle;
            B(wl, GarageU1, 0f, Y, GarageU1 + 0.25f, a0 + 0.8f, top);
            B(wl, GarageU1, a1 - 0.8f, Y, GarageU1 + 0.25f, D, top);
            B(rd, GarageU1 + 0.05f, a0 + 0.8f, Y, GarageU1 + 0.15f, a1 - 0.8f, top);
            for (float y = Y + 0.15f; y < top; y += 0.15f) B(rd, GarageU1 - 0.01f, a0 + 0.8f, y, GarageU1 + 0.05f, a1 - 0.8f, y + 0.02f);   // corrugations

            // bays: lines, bay numbers on the wall plate in front of each, wheel stops
            for (int i = 0; i < 24; i++)
            {
                float uc = BayCentreU(i), vc = BayCentreV(i);
                bool rowA = i < 12;
                float v0 = rowA ? 0.3f : a1, v1 = rowA ? a0 : a1 + BayL;
                B(ln, uc - BayW / 2 - 0.05f, v0, Y, uc - BayW / 2 + 0.05f, v1, Y + 0.008f);
                if (i % 12 == 11) B(ln, uc + BayW / 2 - 0.05f, v0, Y, uc + BayW / 2 + 0.05f, v1, Y + 0.008f);
                float wall = rowA ? 0.05f : D - 0.05f;
                Sign(i, uc, wall, Y + 1.7f, 0.9f, rowA ? inw : -inw);
                float stop = rowA ? 0.9f : D - 0.9f;
                B(yl, uc - 0.8f, stop - 0.08f, Y, uc + 0.8f, stop + 0.08f, Y + 0.12f);
            }
            // aisle centre dashes and the exit arrow lane
            for (float u = GarageU0 + 5f; u < GarageU1 - 1f; u += 3f) B(yl, u, (a0 + a1) / 2 - 0.06f, Y, u + 1.5f, (a0 + a1) / 2 + 0.06f, Y + 0.008f);
            // columns on the bay lines, yellow-black bands at the foot
            for (float u = BaysU0; u <= -BaysU0 + 0.01f; u += BayW * 3)
                foreach (float v in new[] { a0, a1 })
                {
                    B(wl, u - 0.3f, v - 0.3f, Y, u + 0.3f, v + 0.3f, top);
                    for (int k = 0; k < 4; k++) B(k % 2 == 0 ? yl : bk, u - 0.31f, v - 0.31f, Y + k * 0.25f, u + 0.31f, v + 0.31f, Y + (k + 1) * 0.25f);
                }
            // fluorescent tubes over the aisle and bays, pipes and a duct under the slab
            for (float u = GarageU0 + 3f; u < GarageU1 - 2f; u += 4f)
                foreach (float v in new[] { 3f, (a0 + a1) / 2, a1 + 2.7f })
                {
                    B(tb, u, v - 0.06f, top - 0.12f, u + 1.5f, v + 0.06f, top - 0.06f);
                    B(dc, u - 0.02f, v - 0.08f, top - 0.06f, u + 1.52f, v + 0.08f, top - 0.02f);
                }
            B(pp, GarageU0, 1.2f, top - 0.35f, GarageU1, 1.32f, top - 0.23f);
            B(pp, GarageU0, D - 1.3f, top - 0.35f, GarageU1, D - 1.18f, top - 0.23f);
            B(dc, GarageU0, a0 + 1.5f, top - 0.55f, GarageU1, a0 + 2.3f, top - 0.2f);

            // west end: the elevator, its signs, the fire door
            float ev = (a0 + a1) / 2;
            B(st, GarageU0 + 0.0f, ev - 0.65f, Y, GarageU0 + 0.04f, ev + 0.65f, Y + 2.3f);
            B(dc, GarageU0, ev - 0.75f, Y + 2.3f, GarageU0 + 0.08f, ev + 0.75f, Y + 2.4f);
            Sign(27, GarageU0 + 0.06f, ev, Y + 2.15f + 0.3f, 0.8f, al);          // ELEVATOR
            Sign(25, GarageU0 + 0.06f, ev + 2.0f, Y + 1.7f, 0.9f, al);           // P1
            B(st, GarageU0, 1.5f, Y, GarageU0 + 0.05f, 2.5f, Y + 2.1f);           // fire door
            Sign(24, GarageU0 + 0.06f, 2.0f, Y + 2.3f, 0.6f, al);                 // EXIT
            Sign(30, GarageU1 - 0.06f, ev, top - 0.35f, 1.6f, -al);               // EXIT TO SHUTER ST over the door
            Sign(26, 0f, 0.06f, Y + 2.2f, 1.2f, inw);                             // RESIDENTS ONLY

            AddObject("GarageFloor", root, flr.ToMesh("GarageFloor"), concrete, meshes, collider: true);
            AddObject("GarageWalls", root, wl.ToMesh("GarageWalls"), wallMat, meshes, collider: true);
            AddObject("GarageLines", root, ln.ToMesh("GarageLines"), paint, meshes, collider: false);
            AddObject("GarageYellow", root, yl.ToMesh("GarageYellow"), yellow, meshes, collider: false);
            AddObject("GarageBlack", root, bk.ToMesh("GarageBlack"), black, meshes, collider: false);
            AddObject("GarageTubes", root, tb.ToMesh("GarageTubes"), tube, meshes, collider: false);
            AddObject("GaragePipes", root, pp.ToMesh("GaragePipes"), pipe, meshes, collider: false);
            AddObject("GarageDucts", root, dc.ToMesh("GarageDucts"), duct, meshes, collider: false);
            AddObject("GarageSteel", root, st.ToMesh("GarageSteel"), steel, meshes, collider: true);
            AddObject("GarageRollUp", root, rd.ToMesh("GarageRollUp"), roll, meshes, collider: true);
            AddObject("GarageSigns", root, sg.ToMesh("GarageSigns"), signs, meshes, collider: false);

            // street side: the roll-up door on Shuter St with its sign and light (visual; the gate is a fade)
            var outRd = new MeshBuilder(); var outSg = new MeshBuilder();
            var sgSave = sg; sg = outSg;
            B(outRd, StreetDoorU - 1.8f, -0.12f, ShopFloor, StreetDoorU + 1.8f, -0.04f, 2.9f);
            for (float y = ShopFloor + 0.15f; y < 2.9f; y += 0.15f) B(outRd, StreetDoorU - 1.8f, -0.14f, y, StreetDoorU + 1.8f, -0.12f, y + 0.02f);
            Sign(31, StreetDoorU, -0.15f, 3.25f, 2.2f, -inw);                     // PANTAGES PARKING
            sg = sgSave;
            AddObject("GarageStreetDoor", root, outRd.ToMesh("GarageStreetDoor"), roll, meshes, collider: false);
            AddObject("GarageStreetSign", root, outSg.ToMesh("GarageStreetSign"), signs, meshes, collider: false);

            // named points: Tarik's bay, the elevator pair with the lobby's right-hand elevator, the drive-in gates
            for (int i = 0; i < 24; i++)
            {
                var spot = new GameObject($"GarageSpot_P1-{i + 1:00}").transform;
                spot.SetParent(root, false);
                spot.SetPositionAndRotation(s.W(BayCentreU(i), BayCentreV(i), Y), Quaternion.LookRotation(i < 12 ? -inw : inw));   // nose to the wall
            }
            Point(root, "GarageGate_P1", s.W(GarageU1 - 3.5f, ev, Y), al);                          // drive up to the roll-up door
            Point(root, "GarageGate_P1_Exit", s.W(GarageU1 - 6f, ev, Y + 0.05f), -al);             // where you come in, facing into P1
            Point(root, "GarageGate_Street", s.W(StreetDoorU, -2.2f, 0f), inw);                    // pull up to the street door
            Point(root, "GarageGate_Street_Exit", s.W(StreetDoorU - 3f, -5.5f, 0.3f), -al);         // out on Shuter St, along the road

            var landing = Point(root, "Garage_ElevatorLanding", s.W(GarageU0 + 1.3f, ev, Y), al);
            var gLift = Point(root, "Garage_Elevator", s.W(GarageU0 + 0.6f, ev, Y), al).gameObject.AddComponent<LifeInDowntown.World.DoorLink>();
            gLift.prompt = "Take the elevator up to the lobby"; gLift.radius = 1.3f;
            var lobbyRoot = GameObject.Find("PantagesLobby");
            if (lobbyRoot != null)
            {
                var lLanding = Point(lobbyRoot.transform, "Lobby_ElevatorLanding2", s.W(1.55f, ShopDepth - 1.2f, ShopFloor), -inw);
                var down = Point(lobbyRoot.transform, "Lobby_Elevator_Down", s.W(1.55f, ShopDepth - 0.6f, ShopFloor), -inw).gameObject.AddComponent<LifeInDowntown.World.DoorLink>();
                down.prompt = "Take the elevator down to P1 Parking"; down.radius = 1.3f; down.exit = landing;
                gLift.exit = lLanding;
            }

            // lighting: cool tubes, no shadows but two; an indoor zone with a dim fixed exposure
            for (float u = GarageU0 + 4f; u < GarageU1; u += 8f)
                foreach (float v in new[] { 3f, a1 + 2.7f })
                {
                    var lg = new GameObject("GarageLight");
                    lg.transform.SetParent(root, false);
                    lg.transform.position = s.W(u, v, top - 0.3f);
                    var l = lg.AddComponent<Light>(); lg.AddComponent<HDAdditionalLightData>();
                    l.type = LightType.Point; l.lightUnit = LightUnit.Lumen; l.intensity = 1100f;
                    l.useColorTemperature = true; l.colorTemperature = 4200f; l.color = Color.white; l.range = 11f;
                    l.shadows = Mathf.Abs(u) < 5f ? LightShadows.Soft : LightShadows.None;
                }
            var zone = new GameObject("GarageIndoor");
            zone.transform.SetParent(root, false);
            zone.transform.SetPositionAndRotation(s.W(0f, D / 2, Y + GarageClear / 2), Quaternion.LookRotation(inw));
            zone.AddComponent<LifeInDowntown.World.IndoorZone>().size = new Vector3(GarageU1 - GarageU0, GarageClear + 0.6f, D);
            var box = zone.AddComponent<BoxCollider>(); box.isTrigger = true; box.size = new Vector3(GarageU1 - GarageU0, GarageClear + 0.6f, D);
            var vol = zone.AddComponent<Volume>(); vol.isGlobal = false; vol.priority = 40f; vol.blendDistance = 0.6f;
            vol.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(GarageProfile());
            Debug.Log($"GARAGE_OK under building={s.building} floorY={Y} bays=24 tarik={TarikBay} at {s.W(BayCentreU(16), BayCentreV(16), Y)}");
        }

        static Transform Point(Transform parent, string name, Vector3 at, Vector3 face)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.SetPositionAndRotation(at, Quaternion.LookRotation(face));
            return t;
        }

        static string GarageProfile()
        {
            const string path = "Assets/City/Look/GarageInterior.asset";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(path) != null) AssetDatabase.DeleteAsset(path);
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, path);
            var fog = p.Add<Fog>(true); fog.enabled.Override(false);
            var exp = p.Add<Exposure>(true); exp.mode.Override(ExposureMode.Fixed); exp.fixedExposure.Override(3.6f);   // dim concrete garage
            foreach (var c in p.components) { c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy; AssetDatabase.AddObjectToAsset(c, p); }
            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
            return path;
        }
    }
}
