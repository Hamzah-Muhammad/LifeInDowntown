using System.Collections.Generic;
using LifeInDowntown.City;
using LifeInDowntown.Player;
using LifeInDowntown.Transit;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LifeInDowntown.EditorTools
{
    // The TTC subway (docs/DESIGN.md 3N.10 task 7), driven in edit mode like PlayerSimTest:
    //   every street entrance (and the Sankofa elevator) walks to both platforms of its station;
    // Prints SUBWAY_PASS / SUBWAY_FAIL lines; exits 1 on failure. The scene is not saved.
    public static class SubwayTest
    {
        const string ScenePath = "Assets/Scenes/Downtown_YongeDundas.unity";
        const float Dt = 1f / 60f;
        static int fails;

        static void Check(bool ok, string what) { Debug.Log((ok ? "SUBWAY_PASS " : "SUBWAY_FAIL ") + what); if (!ok) fails++; }

        public static void Run()
        {
            fails = 0;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var player = Object.FindFirstObjectByType<BodycamPlayer>();
            var line = Object.FindFirstObjectByType<SubwayLine>();
            Check(player != null && line != null, "player and the subway line exist");
            if (player == null || line == null) { EditorApplication.Exit(1); return; }
            player.Init();
            Check(line.stations != null && line.stations.Length == 3, $"three stations built ({line.stations?.Length ?? 0})");

            WalkRoutes(player, line);

            Debug.Log(fails == 0 ? "SUBWAY_RESULT all checks passed" : $"SUBWAY_RESULT {fails} check(s) failed");
            EditorApplication.Exit(fails == 0 ? 0 : 1);
        }

        static void Place(BodycamPlayer p, Vector3 at, float yaw)
        {
            var cc = p.GetComponent<CharacterController>();
            cc.enabled = false; p.transform.position = at; cc.enabled = true;
            p.SetLook(yaw, 0f);
            Physics.SyncTransforms();
        }

        // Walks the waypoints at a jog, steering at the next one; fails if it stops making progress
        public static bool Walk(BodycamPlayer p, Vector3[] pts, out string why)
        {
            why = "";
            var first = pts[1] - pts[0];
            Place(p, pts[0], Mathf.Atan2(first.x, first.z) * Mathf.Rad2Deg);
            for (int i = 0; i < 30; i++) p.Step(Vector2.zero, Vector2.zero, false, false, false, false, Dt);   // settle
            int k = 1; float best = float.MaxValue, sinceBest = 0f, t = 0f;
            while (k < pts.Length && t < 240f)
            {
                var to = pts[k] - p.transform.position; to.y = 0f;
                float d = to.magnitude;
                if (d < 0.6f) { k++; best = float.MaxValue; sinceBest = 0f; continue; }
                p.SetLook(Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 0f);
                p.Step(new Vector2(0, 1), Vector2.zero, false, false, false, false, Dt);
                t += Dt;
                if (d < best - 0.3f) { best = d; sinceBest = 0f; } else sinceBest += Dt;
                if (sinceBest > 6f)
                {
                    string blocker = "";
                    var dir = to.normalized;
                    foreach (float hgt in new[] { 0.3f, 1.0f, 1.6f })
                        if (Physics.Raycast(p.transform.position + Vector3.up * hgt, dir, out var hit, 2f, ~0, QueryTriggerInteraction.Ignore))
                            blocker += $" [{hgt}m: {hit.collider.name} at {hit.distance:F2} m]";
                    why = $"stuck before waypoint {k}/{pts.Length - 1} at {p.transform.position} (target {pts[k]}){blocker}";
                    return false;
                }
                if (p.transform.position.y < -12f) { why = $"fell to y={p.transform.position.y:F1} before waypoint {k}"; return false; }
            }
            if (k < pts.Length) { why = $"ran out of time at waypoint {k}"; return false; }
            why = $"{t:F0} s";
            return true;
        }

        static void WalkRoutes(BodycamPlayer player, SubwayLine line)
        {
            int n = 0, entrances = 0;
            var all = CityGenerator.LoadTtcLine();
            foreach (var e in all.entrances) if (e.kind == "street" || e.kind == "elevator") entrances++;
            int skipped = 0;
            foreach (var st in line.stations)
            {
                skipped += st.skippedEntrances.Length;
                foreach (var s in st.skippedEntrances) Debug.Log($"SUBWAY_INFO {st.stationName}: '{s}' is a door inside a building in OSM; not built");
            }
            Check(entrances == 10 && line.stations[0].routes.Length + line.stations[1].routes.Length + line.stations[2].routes.Length == 2 * (entrances - skipped),
                  $"every OSM street entrance and elevator is built or listed as skipped ({entrances} in OSM, {skipped} skipped)");
            foreach (var st in line.stations)
                foreach (var r in st.routes)
                {
                    n++;
                    bool ok = Walk(player, r.points, out string why);
                    var end = player.transform.position;
                    bool onPlatform = st.OnPlatform(end, out int dir);
                    int want = r.name.EndsWith("northbound platform") ? 1 : -1;
                    Check(ok && onPlatform && dir == want && Mathf.Abs(end.y - SubwayLine.PlatformFloor) < 0.35f,
                          $"{st.stationName}: {r.name} ({why}, ends y={end.y:F2}{(onPlatform ? "" : ", NOT on a platform")})");
                }
            Debug.Log($"SUBWAY_INFO {n} entrance-to-platform routes walked");
        }
    }
}
