using System.IO;
using System.IO;
using LifeInDowntown.City;
using LifeInDowntown.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LifeInDowntown.EditorTools
{
    // Drives the bodycam player with simulated input in edit mode (no play mode needed in batch runs)
    // and checks: it stands on the ground, walks/jogs/sprints at the set speeds, stops at a building wall,
    // and the camera bob stays in the designed range. Prints SIM_PASS / SIM_FAIL lines; exits 1 on failure.
    // The scene is not saved.
    public static class PlayerSimTest
    {
        const string ScenePath = "Assets/Scenes/Downtown_YongeDundas.unity";
        const float Dt = 1f / 60f;

        public static void Run()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var player = Object.FindFirstObjectByType<BodycamPlayer>();
            var rig = Object.FindFirstObjectByType<BodycamRig>();
            int fails = 0;
            void Check(bool ok, string what) { Debug.Log((ok ? "SIM_PASS " : "SIM_FAIL ") + what); if (!ok) fails++; }

            Check(player != null && rig != null, "player and rig exist in the scene");
            if (player == null || rig == null) { EditorApplication.Exit(1); return; }
            player.Init();
            var data = JsonUtility.FromJson<CityData>(File.ReadAllText("Assets/City/Data/downtown_yonge_dundas.json"));

            // a new game starts in Tarik's condo (DESIGN 3H): stand on its floor, and the glass stops you
            if (CityGenerator.TryCondoFrame(data, out var condo))
            {
                for (int i = 0; i < 60; i++) Tick(player, rig, Vector2.zero, false, false);
                float y = player.transform.position.y;
                Check(player.Grounded && y > CityGenerator.CondoFloorY - 0.2f && y < CityGenerator.CondoFloorY + 0.6f,
                      $"spawn stands on the condo floor (y={y:F2}, floor {CityGenerator.CondoFloorY})");
                for (int i = 0; i < 240; i++) Tick(player, rig, new Vector2(0, 1), true, false);   // sprint at the window 4 s
                float behindGlass = Vector3.Dot(player.transform.position - condo.origin, condo.inward);
                Check(behindGlass > 0.15f && player.transform.position.y > CityGenerator.CondoFloorY - 0.2f,
                      $"the window glass stops you (still {behindGlass:F2} m inside, y={player.transform.position.y:F1})");
            }
            else Check(false, "condo frame found in Pantages Tower");
            var doors = System.Array.FindAll(Object.FindObjectsByType<LifeInDowntown.World.DoorLink>(FindObjectsSortMode.None),
                                             dl => !dl.name.StartsWith("SubwayElevator"));   // the subway's own pair: SubwayTest
            var condoDoor = System.Array.Find(doors, dl => dl.name == "CondoDoor_Inside");
            var liftUp = System.Array.Find(doors, dl => dl.name == "Lobby_Elevator_Up");
            Check(condoDoor != null && liftUp != null && liftUp.exit == condoDoor.transform && condoDoor.exit != null && condoDoor.exit.name == "Lobby_ElevatorLanding",
                  $"lobby elevator and condo door are linked (DESIGN 3O.2; {doors.Length} doors)");
            Check(System.Array.TrueForAll(doors, dl => dl.exit != null), "every door leads somewhere");

            // walk-in Rabba: from the sidewalk straight through the doorway into the store (DESIGN 3K)
            var shops = CityGenerator.ShopSites(data);
            Check(shops.FindAll(x => x.kind == "rabba").Count == 2 && shops.FindAll(x => x.kind == "lobby").Count == 1,
                  $"two Rabba stores and the Pantages lobby placed ({shops.Count} sites)");
            foreach (var shop in shops)
            {
                var outside = shop.W(-0.2f, -1.2f, 0.3f);
                var into = shop.W(-0.2f, 1f, 0f) - shop.W(-0.2f, 0f, 0f);
                var ccS = player.GetComponent<CharacterController>();
                ccS.enabled = false; player.transform.position = outside; ccS.enabled = true;
                player.SetLook(Mathf.Atan2(into.x, into.z) * Mathf.Rad2Deg, 0f);
                Physics.SyncTransforms();
                for (int i = 0; i < 300; i++) Tick(player, rig, new Vector2(0, 1), false, false);   // walk in for 5 s (walking is the default pace, DESIGN 3R)
                float depth = Vector3.Dot(player.transform.position - shop.W(0f, 0f, 0f), into.normalized);
                bool walkedIn = depth > 2f && depth < CityGenerator.ShopDepth && player.transform.position.y > -0.1f;
                string blocker = "";
                if (!walkedIn)
                    foreach (float hgt in new[] { 0.2f, 1.0f, 1.6f })
                        if (Physics.Raycast(player.transform.position + Vector3.up * hgt, into.normalized, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore))
                            blocker += $" [{hgt}m: {hit.collider.name} at {hit.distance:F2} m]";
                Check(walkedIn, $"{shop.name}: walked in through the door ({depth:F1} m inside, y={player.transform.position.y:F2}){blocker}");
            }

            // the rest runs on the street
            var street = PlayerSetup.StreetPoint(data, out float streetYaw);
            var ccTp = player.GetComponent<CharacterController>();
            ccTp.enabled = false; player.transform.position = street; ccTp.enabled = true;
            player.SetLook(streetYaw, 0f);
            Physics.SyncTransforms();

            // settle onto the ground
            for (int i = 0; i < 60; i++) Tick(player, rig, Vector2.zero, false, false);
            Check(player.Grounded, $"grounded after settling (y={player.transform.position.y:F2})");
            Check(player.transform.position.y > -0.1f && player.transform.position.y < 0.6f, "standing at street level, not fallen through");

            // walk by default (DESIGN 3R): 3 s, then measure speed over the last second
            float walk = MeasureSpeed(player, rig, new Vector2(0, 1), false, 180, out _, out _);
            Check(Mathf.Abs(walk - player.walkSpeed) < 0.3f && Mathf.Approximately(player.NoiseRadius, player.noiseWalk), $"walks by default: {walk:F2} m/s (target {player.walkSpeed}), heard at {player.NoiseRadius} m");

            // Shift: a run for the first 1.2 s held (measured 0.4-1.1 s in), then a sprint
            float runFrom = 0f, bobMin = float.MaxValue, bobMax = float.MinValue; Vector3 runStart = Vector3.zero;
            for (int i = 0; i < 66; i++)
            {
                Tick(player, rig, new Vector2(0, 1), true, false);
                if (i == 24) { runStart = player.transform.position; runFrom = i * Dt; }
                if (i > 24) { float by = rig.LastBobOffset.y; bobMin = Mathf.Min(bobMin, by); bobMax = Mathf.Max(bobMax, by); }
            }
            float run = Flat(player.transform.position - runStart).magnitude / (65 * Dt - runFrom);
            Check(Mathf.Abs(run - player.jogSpeed) < 0.5f && !player.Sprinting && Mathf.Approximately(player.NoiseRadius, player.noiseRun), $"Shift runs: {run:F2} m/s (target {player.jogSpeed}), heard at {player.NoiseRadius} m");
            float bobRange = bobMax - bobMin;
            Check(bobRange > 0.01f && bobRange < 0.04f, $"run vertical bob {bobRange * 100f:F1} cm (design 1.5-3 cm)");
            float sprint = MeasureSpeed(player, rig, new Vector2(0, 1), true, 120, out _, out _);
            Check(Mathf.Abs(sprint - player.sprintSpeed) < 0.6f && player.Sprinting, $"held on, it becomes a sprint: {sprint:F2} m/s (target {player.sprintSpeed})");

            // C: sneak (crouched, slow, quiet); Shift stands him back up
            player.SetSneak(true);
            float sneak = MeasureSpeed(player, rig, new Vector2(0, 1), false, 120, out _, out _);
            var ccCrouch = player.GetComponent<CharacterController>();
            Check(Mathf.Abs(sneak - player.crouchSpeed) < 0.25f && ccCrouch.height < player.crouchHeight + 0.05f && Mathf.Approximately(player.NoiseRadius, player.noiseSneak)
                  && rig.transform.position.y - player.transform.position.y < 1.15f,
                  $"sneaking: {sneak:F2} m/s, {ccCrouch.height:F2} m tall, eyes {rig.transform.position.y - player.transform.position.y:F2} m up, heard at {player.NoiseRadius} m");
            Tick(player, rig, new Vector2(0, 1), true, false);
            Check(!player.Sneaking, "Shift stands him up out of a sneak");
            for (int i = 0; i < 60; i++) Tick(player, rig, Vector2.zero, false, false);

            // face the nearest building wall head-on and sprint into it for 7 s (long enough to reach it): the body
            // may slide along the wall (correct CharacterController behaviour) but must never cross its plane or
            // overlap a building. Run from where the speed tests ended AND from a clean sidewalk spot.
            void WallTest(Vector3 from, string label)
            {
                var ccW = player.GetComponent<CharacterController>();
                ccW.enabled = false; player.transform.position = from; ccW.enabled = true;
                Physics.SyncTransforms();
                for (int i = 0; i < 30; i++) Tick(player, rig, Vector2.zero, false, false);
                var wallHit = NearestWall(player.transform.position);
                if (!wallHit.HasValue) { Check(false, $"{label}: found a building wall to test against"); return; }
                var hit = wallHit.Value;
                Vector3 into = -hit.normal; into.y = 0f;
                float targetYaw = Mathf.Atan2(into.x, into.z) * Mathf.Rad2Deg;
                Tick(player, rig, Vector2.zero, false, false, new Vector2(Mathf.DeltaAngle(player.Yaw, targetYaw) / player.lookSensitivity, 0f));
                // passing through = a step that crosses a building (a chest-high line from one frame's position to the
                // next hits a wall), or the capsule inside one. Sliding along a wall or round its end is neither; the
                // old "never past the wall's infinite plane" test failed on irregular buildings for exactly that.
                float closest = float.MaxValue;
                bool overlapped = false, crossed = false;
                Vector3 prev = player.transform.position;
                for (int i = 0; i < 420; i++)
                {
                    Tick(player, rig, new Vector2(0, 1), true, false);
                    var p = player.transform.position;
                    closest = Mathf.Min(closest, Flat(p - hit.point).magnitude);
                    if (Physics.Linecast(prev + Vector3.up * 1.2f, p + Vector3.up * 1.2f, out var step, ~0, QueryTriggerInteraction.Ignore) && step.collider.name.StartsWith("Bldg_")) crossed = true;
                    prev = p;
                    foreach (var c in Physics.OverlapCapsule(p + Vector3.up * (ccW.radius + 0.1f), p + Vector3.up * (ccW.height - ccW.radius), ccW.radius * 0.8f, ~0, QueryTriggerInteraction.Ignore))
                        if (c.name.StartsWith("Bldg_")) overlapped = true;
                }
                bool near = hit.distance < 8f;   // a near wall must actually be reached; a far one may be slid past on the way
                Check(!overlapped && !crossed && (!near || closest < 1.0f),
                      $"{label}: reaches the wall and never passes through (got within {closest:F2} m of it, wall={hit.collider.name} {hit.distance:F1} m away, crossed={crossed}, overlap={overlapped})");
            }
            WallTest(player.transform.position, "after the speed runs");
            WallTest(street, "from the street spawn");
            Debug.Log(fails == 0 ? "SIM_RESULT all checks passed" : $"SIM_RESULT {fails} check(s) failed");
            EditorApplication.Exit(fails == 0 ? 0 : 1);
        }

        static void Tick(BodycamPlayer p, BodycamRig r, Vector2 move, bool sprint, bool jump, Vector2 look = default)
        {
            p.Step(move, look, sprint, false, jump, false, Dt);
            Physics.SyncTransforms();
            r.Tick(Dt);
        }

        static float MeasureSpeed(BodycamPlayer p, BodycamRig r, Vector2 move, bool sprint, int frames, out float bobMin, out float bobMax)
        {
            bobMin = float.MaxValue; bobMax = float.MinValue;
            Vector3 mark = p.transform.position;
            for (int i = 0; i < frames; i++)
            {
                if (i == frames - 60) mark = p.transform.position;
                Tick(p, r, move, sprint, false);
                if (i >= frames - 60)
                {
                    bobMin = Mathf.Min(bobMin, r.LastBobOffset.y);
                    bobMax = Mathf.Max(bobMax, r.LastBobOffset.y);
                }
            }
            return Vector3.Distance(Flat(mark), Flat(p.transform.position));   // metres in the last 60 frames = m/s
        }

        // nearest vertical building wall within 40 m, by ray fan at chest height
        static RaycastHit? NearestWall(Vector3 from)
        {
            RaycastHit? best = null;
            for (int a = 0; a < 360; a += 5)
            {
                var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                // the first BUILDING wall along the ray that faces it: street props, signal poles and indoor
                // trigger boxes are skipped, and back faces don't count
                RaycastHit? first = null;
                foreach (var hit in Physics.RaycastAll(from + Vector3.up * 1.2f, dir, 40f, ~0, QueryTriggerInteraction.Ignore))
                    if (hit.collider.name.StartsWith("Bldg_") && Mathf.Abs(hit.normal.y) < 0.2f && Vector3.Dot(hit.normal, dir) < -0.3f &&
                        (!first.HasValue || hit.distance < first.Value.distance))
                        first = hit;
                if (first.HasValue && (!best.HasValue || first.Value.distance < best.Value.distance)) best = first;
            }
            return best;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
