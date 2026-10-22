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
    //   the next train comes in quickly; you can board it, ride TMU -> College and get off;
    //   doors open only when the train stands at a platform, on the platform side; a hop takes <= 35 s;
    //   zoning out (Enter) puts you at the next station, still aboard; an unattended train runs its loop and
    //   short-turns at the end.
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
            Ride(player, line);

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
        public static bool Walk(BodycamPlayer p, Vector3[] pts, out string why, System.Action perStep = null, bool place = true)
        {
            why = "";
            var first = pts[1] - pts[0];
            if (place)
            {
                Place(p, pts[0], Mathf.Atan2(first.x, first.z) * Mathf.Rad2Deg);
                for (int i = 0; i < 30; i++) { perStep?.Invoke(); p.Step(Vector2.zero, Vector2.zero, false, false, false, false, Dt); }   // settle
            }
            int k = 1; float best = float.MaxValue, sinceBest = 0f, t = 0f;
            while (k < pts.Length && t < 240f)
            {
                var to = pts[k] - p.transform.position; to.y = 0f;
                float d = to.magnitude;
                if (d < 0.6f) { k++; best = float.MaxValue; sinceBest = 0f; continue; }
                perStep?.Invoke();
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

        // ------------------------------------------------------------------ riding (3N.7)
        static TrainController nb, sb;
        static int doorViolations, doorChecks;
        static string firstViolation = "";

        // both trains advance one step; doors may only be open at a standstill on a stop mark, on the platform side
        static void TickTrains()
        {
            foreach (var tr in new[] { nb, sb })
            {
                tr.Tick(Dt);
                if (tr.doorOpen <= 0f) continue;
                doorChecks++;
                bool atStop = tr.state == TrainController.State.Dwell && tr.speed == 0f && Mathf.Abs(tr.headS - tr.StopHead(tr.stopIndex)) < 0.05f;
                var st = tr.Stops[tr.stopIndex];
                var mid = tr.cars[2];
                var outside = mid.transform.TransformPoint(new Vector3(2.2f, TrainCar.FloorY + 0.1f, mid.doorZ[1]));   // just outside a right-side door
                bool platformSide = st.OnPlatform(outside, out int d) && d == tr.dir;
                if (!(atStop && platformSide))
                {
                    doorViolations++;
                    if (firstViolation.Length == 0) firstViolation = $"{tr.name} {tr.state} speed {tr.speed:F2} head {tr.headS:F1} stop {tr.StopHead(tr.stopIndex):F1} platformSide={platformSide}";
                }
            }
        }

        static void Stand(BodycamPlayer p, float seconds, System.Func<bool> until = null)
        {
            for (float t = 0; t < seconds; t += Dt)
            {
                TickTrains();
                p.Step(Vector2.zero, Vector2.zero, false, false, false, false, Dt);
                if (until != null && until()) return;
            }
        }

        static float Wait(BodycamPlayer p, float max, System.Func<bool> until)
        {
            float t = 0f;
            for (; t < max && !until(); t += Dt) { TickTrains(); p.Step(Vector2.zero, Vector2.zero, false, false, false, false, Dt); }
            return t;
        }

        static SubwayStation Station(SubwayLine line, string name) => System.Array.Find(line.stations, s => s.stationName == name);

        // walk from the platform through the nearest open door into the car
        static bool Board(BodycamPlayer p, TrainController tr, out string why)
        {
            TrainCar best = null; float bestZ = 0f, bestD = float.MaxValue;
            foreach (var c in tr.cars)
                foreach (float z in c.doorZ)
                {
                    float d = (c.transform.TransformPoint(new Vector3(2.3f, TrainCar.FloorY, z)) - p.transform.position).magnitude;
                    if (d < bestD) { bestD = d; best = c; bestZ = z; }
                }
            var pts = new[] { p.transform.position, best.transform.TransformPoint(new Vector3(2.4f, TrainCar.FloorY, bestZ)), best.transform.TransformPoint(new Vector3(0f, TrainCar.FloorY, bestZ)) };
            return Walk(p, pts, out why, TickTrains, place: false);
        }

        static bool Alight(BodycamPlayer p, TrainController tr, out string why)
        {
            TrainCar car = System.Array.Find(tr.cars, c => c.Contains(p.transform.position + Vector3.up * 0.9f));
            if (car == null) { why = "not in a car"; return false; }
            float z = car.doorZ[0]; float bestD = float.MaxValue;
            foreach (float dz in car.doorZ) { float d = Mathf.Abs(car.transform.InverseTransformPoint(p.transform.position).z - dz); if (d < bestD) { bestD = d; z = dz; } }
            var pts = new[] { p.transform.position, car.transform.TransformPoint(new Vector3(0f, TrainCar.FloorY, z)), car.transform.TransformPoint(new Vector3(3.6f, TrainCar.FloorY, z)) };
            return Walk(p, pts, out why, TickTrains, place: false);
        }

        static void Ride(BodycamPlayer player, SubwayLine line)
        {
            var trains = Object.FindObjectsByType<TrainController>(FindObjectsSortMode.None);
            nb = System.Array.Find(trains, t => t.dir > 0); sb = System.Array.Find(trains, t => t.dir < 0);
            Check(nb != null && sb != null && nb.cars.Length == 6 && sb.cars.Length == 6, "two 6-car trains (northbound, southbound)");
            if (nb == null || sb == null) return;
            nb.Init(); sb.Init();
            var tmu = Station(line, "TMU"); var college = Station(line, "College"); var queen = Station(line, "Queen");

            // 1. wait on TMU's northbound platform: the train is sent in
            Place(player, tmu.W(0f, 5.4f, SubwayLine.PlatformFloor + 0.1f), Mathf.Atan2(tmu.Right.x, tmu.Right.z) * Mathf.Rad2Deg + 180f);
            float waited = Wait(player, 60f, () => nb.state == TrainController.State.Dwell && nb.Stops[nb.stopIndex] == tmu && nb.doorOpen >= 1f);
            Check(waited < 30f, $"northbound train at TMU with doors open {waited:F1} s after Tarik reached the platform (target ~20 s, at most 30)");

            // 2. board, ride to College, get off
            bool boarded = Board(player, nb, out string why);
            Check(boarded && nb.PlayerAboard, $"boarded the northbound train at TMU through an open door ({why})");
            float tDepart = Wait(player, 20f, () => nb.state == TrainController.State.Running);
            Check(nb.state == TrainController.State.Running && nb.Stops[nb.stopIndex] == college, $"doors closed and the train left TMU for College after {tDepart:F1} s");
            float minY = 99f, maxY = -99f; bool stayedAboard = true;
            float ride = 0f;
            for (; ride < 60f && nb.state != TrainController.State.Dwell; ride += Dt)
            {
                TickTrains(); player.Step(Vector2.zero, Vector2.zero, false, false, false, false, Dt);
                float y = player.transform.position.y; minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                stayedAboard &= nb.PlayerAboard;
            }
            Check(nb.state == TrainController.State.Dwell && nb.Stops[nb.stopIndex] == college, $"the ride TMU -> College arrived ({ride:F1} s)");
            Check(stayedAboard && minY > SubwayLine.PlatformFloor - 0.25f && maxY < SubwayLine.PlatformFloor + 0.4f,
                  $"Tarik rode inside the car the whole way, standing on its floor (y {minY:F2}..{maxY:F2})");
            Check(nb.LastHopSeconds > 0f && nb.LastHopSeconds <= 35f, $"hop {nb.LastHop} took {nb.LastHopSeconds:F1} s (<= 35 s)");
            float uAtCollege = college.UV(player.transform.position).x;
            Check(Mathf.Abs(uAtCollege) < SubwayStation.HalfPlatform, $"the car stopped inside College's platform (u = {uAtCollege:F1} m)");
            Stand(player, 3f, () => nb.doorOpen >= 1f);
            bool off = Alight(player, nb, out why);
            Check(off && college.OnPlatform(player.transform.position, out int pd) && pd == 1, $"got off onto College's northbound platform ({why})");

            // 3. the end of the line: the train short-turns into the tunnel once everyone is off
            float leave = Wait(player, 40f, () => nb.state == TrainController.State.Leaving);
            Check(nb.state == TrainController.State.Leaving, $"after the short turn at College the empty train left into the tunnel ({leave:F1} s)");

            // 4. zone out: board southbound at TMU, press Enter after it leaves, wake up at Queen still aboard
            Place(player, tmu.W(10f, -5.4f, SubwayLine.PlatformFloor + 0.1f), Mathf.Atan2(-tmu.Right.x, -tmu.Right.z) * Mathf.Rad2Deg + 180f);
            waited = Wait(player, 60f, () => sb.state == TrainController.State.Dwell && sb.Stops[sb.stopIndex] == tmu && sb.doorOpen >= 1f);
            Check(waited < 30f, $"southbound train at TMU with doors open {waited:F1} s after Tarik reached the platform");
            boarded = Board(player, sb, out why);
            Wait(player, 20f, () => sb.state == TrainController.State.Running);
            Stand(player, 2f);
            bool could = sb.CanZoneOut;
            sb.ZoneOutNow();
            Stand(player, 0.2f);
            Check(boarded && could && sb.state == TrainController.State.Dwell && sb.Stops[sb.stopIndex] == queen && sb.PlayerAboard,
                  $"zoning out on the southbound train puts Tarik at Queen, still aboard ({sb.state} at {sb.Stops[sb.stopIndex].stationName}, aboard={sb.PlayerAboard})");

            // 5. nobody around: a full loop with its short turn (player sent up to the street)
            Place(player, new Vector3(0f, 0.5f, -60f), 0f);
            nb.Init();
            var seen = new List<string>();
            for (float t = 0; t < 240f; t += Dt)
            {
                TickTrains();
                string tag = nb.state == TrainController.State.Dwell ? "Dwell@" + nb.Stops[nb.stopIndex].stationName : nb.state.ToString();
                if (seen.Count == 0 || seen[seen.Count - 1] != tag) seen.Add(tag);
                if (seen.Count > 2 && tag == "Hidden" && seen.Contains("Leaving")) break;
            }
            string loop = string.Join(" > ", seen);
            Check(loop.Contains("Dwell@Queen > Running > Dwell@TMU > Running > Dwell@College > Leaving > Hidden"), $"unattended northbound loop: {loop}");

            Check(doorViolations == 0 && doorChecks > 0, $"doors opened only at a standstill on a platform, platform side ({doorChecks} open-door steps, {doorViolations} violations{(firstViolation.Length > 0 ? ": " + firstViolation : "")})");
        }
    }
}
