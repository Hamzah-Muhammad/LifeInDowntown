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

            // settle onto the ground
            for (int i = 0; i < 60; i++) Tick(player, rig, Vector2.zero, false, false);
            Check(player.Grounded, $"grounded after settling (y={player.transform.position.y:F2})");
            Check(player.transform.position.y > -0.1f && player.transform.position.y < 0.6f, "standing at street level, not fallen through");

            // jog 3 s, then measure speed over the last second
            float jog = MeasureSpeed(player, rig, new Vector2(0, 1), false, 180, out float bobMin, out float bobMax);
            Check(Mathf.Abs(jog - player.jogSpeed) < 0.4f, $"jog speed {jog:F2} m/s (target {player.jogSpeed})");
            float bobRange = bobMax - bobMin;
            Check(bobRange > 0.01f && bobRange < 0.04f, $"jog vertical bob {bobRange * 100f:F1} cm (design 1.5-3 cm after the 2026-10-22 shake cut)");

            float sprint = MeasureSpeed(player, rig, new Vector2(0, 1), true, 120, out _, out _);
            Check(Mathf.Abs(sprint - player.sprintSpeed) < 0.6f, $"sprint speed {sprint:F2} m/s (target {player.sprintSpeed})");

            // face the nearest building wall head-on and sprint into it for 3 s: the body may slide along
            // the wall (correct CharacterController behaviour) but must never cross its plane or overlap a building
            var wallHit = NearestWall(player.transform.position);
            if (wallHit.HasValue)
            {
                var hit = wallHit.Value;
                Vector3 into = -hit.normal; into.y = 0f;
                float targetYaw = Mathf.Atan2(into.x, into.z) * Mathf.Rad2Deg;
                float turn = Mathf.DeltaAngle(player.Yaw, targetYaw) / player.lookSensitivity;
                Tick(player, rig, Vector2.zero, false, false, new Vector2(turn, 0f));
                float minPlaneDist = float.MaxValue;
                bool overlapped = false;
                var cc = player.GetComponent<CharacterController>();
                for (int i = 0; i < 180; i++)
                {
                    Tick(player, rig, new Vector2(0, 1), true, false);
                    var p = player.transform.position;
                    minPlaneDist = Mathf.Min(minPlaneDist, Vector3.Dot(Flat(p) - Flat(hit.point), Flat(hit.normal).normalized));
                    var a = p + Vector3.up * (cc.radius + 0.1f);
                    var b = p + Vector3.up * (cc.height - cc.radius);
                    foreach (var c in Physics.OverlapCapsule(a, b, cc.radius * 0.8f))
                        if (c.name.StartsWith("Bldg_")) overlapped = true;
                }
                Check(minPlaneDist > cc.radius * 0.8f && !overlapped,
                      $"never passes through a building wall (closest {minPlaneDist:F2} m from the wall plane, radius {cc.radius}, overlap={overlapped})");
            }
            else Check(false, "found a building wall to test collision against");

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
                if (Physics.Raycast(from + Vector3.up * 1.2f, dir, out var hit, 40f) &&
                    hit.collider.name.StartsWith("Bldg_") && Mathf.Abs(hit.normal.y) < 0.2f &&
                    (!best.HasValue || hit.distance < best.Value.distance))
                    best = hit;
            }
            return best;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
