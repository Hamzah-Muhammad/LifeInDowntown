using UnityEngine;

namespace LifeInDowntown.Transit
{
    // TTC Line 1 under Yonge (docs/DESIGN.md 3N): the centreline from OpenStreetMap (fetch_ttc.py), resampled
    // every `spacing` m from south to north. Distance along it is s. The two tracks run trackGap/2 either side;
    // northbound on the east (right-hand running), southbound on the west. Filled in by CityGenerator.Subway.
    public class SubwayLine : MonoBehaviour
    {
        public const float RailTop = -9.6f;            // 3N.4
        public const float PlatformFloor = -8.55f;
        public const float ConcourseFloor = -4.2f;

        public float[] alignment;                      // flat x0, z0, x1, z1, ...
        public float spacing = 2f;
        public float trackGap = 4.1f;
        public SubwayStation[] stations;               // south to north

        static SubwayLine instance;
        public static SubwayLine Instance => instance != null ? instance : instance = FindFirstObjectByType<SubwayLine>();

        public float Length => (alignment.Length / 2 - 1) * spacing;

        // centreline at rail-top height
        public Vector3 Point(float s)
        {
            int n = alignment.Length / 2;
            float f = Mathf.Clamp(s / spacing, 0f, n - 1.001f);
            int i = (int)f;
            float t = f - i;
            return new Vector3(Mathf.Lerp(alignment[2 * i], alignment[2 * i + 2], t), RailTop,
                               Mathf.Lerp(alignment[2 * i + 1], alignment[2 * i + 3], t));
        }

        // northward unit tangent (averaged over 4 m so the cars don't twitch at sample joins)
        public Vector3 Tangent(float s)
        {
            var d = Point(s + 2f) - Point(s - 2f);
            d.y = 0f;
            return d.normalized;
        }

        public static Vector3 Right(Vector3 north) => new Vector3(north.z, 0f, -north.x);

        // track centre at rail top; dir +1 = northbound track (east), -1 = southbound (west)
        public Vector3 TrackPoint(float s, int dir) => Point(s) + Right(Tangent(s)) * (trackGap * 0.5f * dir);

        // facing the direction of travel on that track
        public Quaternion TrackRotation(float s, int dir) => Quaternion.LookRotation(Tangent(s) * dir, Vector3.up);

        public SubwayStation StationAt(Vector3 world, out int platformDir)
        {
            platformDir = 0;
            if (stations == null) return null;
            foreach (var st in stations)
                if (st != null && st.OnPlatform(world, out platformDir)) return st;
            return null;
        }
    }
}
