using System.Collections.Generic;
using LifeInDowntown.Transit;
using UnityEngine;

namespace LifeInDowntown.City
{
    // TTC Line 1 tunnels and track (docs/DESIGN.md 3N.10 task 4). Everything follows the OSM centreline
    // (SubwayLine): a cut-and-cover double-track box between the stations, and through the whole length the
    // two tracks at TTC gauge (1,495 mm) on concrete ties, a third rail on the outside of each, cable trays
    // and blue emergency lights on the tunnel walls.
    public static partial class CityGenerator
    {
        const float TunnelHalf = 4.25f;         // inner half-width: 4.1 m between tracks + 3.14 m cars + clearance
        const float TtcGauge = 1.495f;
        const float StationHalfBox = 76f;       // HalfPlat: stations own |u| <= 76

        static void BuildTrack(Transform root, SubwayLine line, SubwayMats sm, List<Mesh> meshes)
        {
            var group = new GameObject("Track").transform;
            group.SetParent(root, false);
            float len = line.Length;
            var stationS = new List<float>();
            foreach (var st in line.stations) stationS.Add(st.s);
            stationS.Sort();

            // tunnel stretches: everything outside the station boxes
            var spans = new List<(float a, float b)>();
            float from = 0f;
            foreach (float s in stationS) { spans.Add((from, s - StationHalfBox)); from = s + StationHalfBox; }
            spans.Add((from, len));

            var walls = new MeshBuilder(); var floor = new MeshBuilder(); var ceil = new MeshBuilder();
            var rails = new MeshBuilder(); var ties = new MeshBuilder(); var third = new MeshBuilder();
            var trays = new MeshBuilder(); var blue = new MeshBuilder();
            float tunnelLen = 0f;

            Vector3 P(float s, float v, float y) { var c = line.Point(s); var r = SubwayLine.Right(line.Tangent(s)); return new Vector3(c.x, y, c.z) + r * v; }

            foreach (var (a, b) in spans)
            {
                if (b - a < 0.5f) continue;
                tunnelLen += b - a;
                int n = Mathf.CeilToInt((b - a) / line.spacing);
                for (int i = 0; i < n; i++)
                {
                    float s0 = a + (b - a) * i / n, s1 = a + (b - a) * (i + 1) / n;
                    var r0 = SubwayLine.Right(line.Tangent(s0)); var r1 = SubwayLine.Right(line.Tangent(s1));
                    // floor, ceiling, walls (inward), concrete box
                    floor.Quad(P(s0, -TunnelHalf, TrackBed), P(s0, TunnelHalf, TrackBed), P(s1, TunnelHalf, TrackBed), P(s1, -TunnelHalf, TrackBed), Vector3.up,
                               new Vector2(-TunnelHalf, s0), new Vector2(TunnelHalf, s0), new Vector2(TunnelHalf, s1), new Vector2(-TunnelHalf, s1));
                    ceil.Quad(P(s0, -TunnelHalf, TunnelTop), P(s0, TunnelHalf, TunnelTop), P(s1, TunnelHalf, TunnelTop), P(s1, -TunnelHalf, TunnelTop), Vector3.down,
                              new Vector2(-TunnelHalf, s0), new Vector2(TunnelHalf, s0), new Vector2(TunnelHalf, s1), new Vector2(-TunnelHalf, s1));
                    foreach (int side in new[] { -1, 1 })
                    {
                        float v = side * TunnelHalf;
                        walls.Quad(P(s0, v, TrackBed), P(s1, v, TrackBed), P(s1, v, TunnelTop), P(s0, v, TunnelTop), (r0 + r1) * -side,
                                   new Vector2(s0, TrackBed), new Vector2(s1, TrackBed), new Vector2(s1, TunnelTop), new Vector2(s0, TunnelTop));
                        // cable trays along the wall
                        foreach (float y in new[] { -6.0f, -6.35f })
                        {
                            float vi = side * (TunnelHalf - 0.25f);
                            trays.Quad(P(s0, v, y), P(s1, v, y), P(s1, vi, y), P(s0, vi, y), Vector3.up, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                            trays.Quad(P(s0, vi, y), P(s1, vi, y), P(s1, vi, y + 0.08f), P(s0, vi, y + 0.08f), (r0 + r1) * -side, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                        }
                    }
                }
                // blue emergency lights every 25 m, both walls
                for (float s = a + 10f; s < b - 5f; s += 25f)
                    foreach (int side in new[] { -1, 1 })
                    {
                        var c = P(s, side * (TunnelHalf - 0.06f), -7.6f);
                        var t = line.Tangent(s); var r = SubwayLine.Right(t);
                        Box(blue, c, t * 0.12f, Vector3.up * 0.08f, r * 0.05f);
                    }
            }
            // end walls at both ends of the line
            foreach (float s in new[] { 0.02f, len - 0.02f })
            {
                var t = line.Tangent(s) * (s < 1f ? 1f : -1f);
                walls.Quad(P(s, -TunnelHalf, TrackBed), P(s, TunnelHalf, TrackBed), P(s, TunnelHalf, TunnelTop), P(s, -TunnelHalf, TunnelTop), t,
                           Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            }
            // tunnel mouths at the station ends: the strip between the tunnel walls and the platform edges
            foreach (float sc in stationS)
                foreach (int e in new[] { -1, 1 })
                    foreach (int side in new[] { -1, 1 })
                    {
                        float s = sc + e * StationHalfBox;
                        var t = line.Tangent(s) * e;
                        float v0 = side * PlatIn, v1 = side * TunnelHalf;
                        walls.Quad(P(s, v0, TrackBed), P(s, v1, TrackBed), P(s, v1, TunnelTop), P(s, v0, TunnelTop), t,
                                   Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
                    }

            // track: rails at TTC gauge on ties, the third rail on the outside of each track, the whole length
            int nTies = 0;
            foreach (int dir in new[] { 1, -1 })
            {
                float tc = dir * line.trackGap * 0.5f;
                int n = Mathf.CeilToInt(len / line.spacing);
                for (int i = 0; i < n; i++)
                {
                    float s0 = len * i / n, s1 = len * (i + 1) / n;
                    foreach (float off in new[] { -TtcGauge / 2, TtcGauge / 2 })
                        RailRun(rails, P, s0, s1, tc + off, 0.035f, SubwayLine.RailTop - 0.16f, SubwayLine.RailTop);
                    // third rail with its cover board, on the side away from the other track
                    RailRun(third, P, s0, s1, tc + dir * 1.25f, 0.05f, SubwayLine.RailTop - 0.12f, SubwayLine.RailTop + 0.02f);
                }
                for (float s = 0.3f; s < len; s += 0.75f, nTies++)
                {
                    var c = P(s, tc, TrackBed + 0.08f); var t = line.Tangent(s); var r = SubwayLine.Right(t);
                    Box(ties, c, r * 1.25f, Vector3.up * 0.08f, t * 0.12f);
                }
            }

            AddObject("TunnelWalls", group, walls.ToMesh("TunnelWalls"), sm.concrete, meshes, collider: true);
            AddObject("TunnelFloor", group, floor.ToMesh("TunnelFloor"), sm.concrete, meshes, collider: true);
            AddObject("TunnelCeiling", group, ceil.ToMesh("TunnelCeiling"), sm.dark, meshes, collider: true);
            AddObject("Rails", group, rails.ToMesh("Rails"), sm.metal, meshes, collider: false);
            AddObject("ThirdRail", group, third.ToMesh("ThirdRail"), sm.orange, meshes, collider: false);
            AddObject("Ties", group, ties.ToMesh("Ties"), sm.concrete, meshes, collider: false);
            AddObject("CableTrays", group, trays.ToMesh("CableTrays"), sm.dark, meshes, collider: false);
            AddObject("TunnelLights", group, blue.ToMesh("TunnelLights"), sm.blue, meshes, collider: false);
            Debug.Log($"TRACK_OK line={len:F0} m tunnel={tunnelLen:F0} m ties={nTies}");
        }

        // a straight box section of rail between s0 and s1 at offset v (width 2*halfW, from y0 to y1)
        static void RailRun(MeshBuilder mb, System.Func<float, float, float, Vector3> P, float s0, float s1, float v, float halfW, float y0, float y1)
        {
            Vector3 a0 = P(s0, v - halfW, y1), b0 = P(s0, v + halfW, y1), a1 = P(s1, v - halfW, y1), b1 = P(s1, v + halfW, y1);
            mb.Quad(a0, b0, b1, a1, Vector3.up, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            var side = (b0 - a0).normalized;
            mb.Quad(P(s0, v - halfW, y0), P(s1, v - halfW, y0), a1, a0, -side, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            mb.Quad(P(s0, v + halfW, y0), P(s1, v + halfW, y0), b1, b0, side, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
        }
    }
}
