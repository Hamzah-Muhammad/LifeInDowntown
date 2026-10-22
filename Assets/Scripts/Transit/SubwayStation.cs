using System;
using UnityEngine;

namespace LifeInDowntown.Transit
{
    // One Line 1 station (docs/DESIGN.md 3N.4). The station frame: u along the line (north +), v across it
    // (east +), origin at the platform centres. Routes are walkable waypoint lists from each street entrance
    // to a platform; SubwayTest walks them.
    public class SubwayStation : MonoBehaviour
    {
        public const float HalfPlatform = 76f, PlatformIn = 3.7f, PlatformOut = 7.2f;

        [Serializable]
        public class Route
        {
            public string name;
            public Vector3[] points;
        }

        public string stationName;
        public float s;                 // centre, along SubwayLine
        public Vector3 centre;          // world, on the centreline at street level (y = 0)
        public Vector3 along;           // u axis (northward)
        public Route[] routes;
        public string[] skippedEntrances;   // OSM street entrances inside buildings: not built (3N.4)

        public Vector3 Right => SubwayLine.Right(along);
        public Vector3 W(float u, float v, float y) => new Vector3(centre.x, y, centre.z) + along * u + Right * v;

        public Vector2 UV(Vector3 world)
        {
            var d = world - centre;
            return new Vector2(Vector3.Dot(d, along), Vector3.Dot(d, Right));
        }

        // standing on a platform: dir +1 = the northbound (east) one, -1 = southbound (west)
        public bool OnPlatform(Vector3 world, out int dir)
        {
            var uv = UV(world);
            dir = uv.y > 0f ? 1 : -1;
            float av = Mathf.Abs(uv.y);
            return Mathf.Abs(uv.x) < HalfPlatform && av > PlatformIn - 0.2f && av < PlatformOut + 0.2f &&
                   world.y > SubwayLine.PlatformFloor - 0.6f && world.y < SubwayLine.PlatformFloor + 2.5f;
        }

        // anywhere inside the station's underground spaces, roughly (used for HUD and the minimap)
        public bool Underground(Vector3 world) => world.y < -1.0f && UV(world).magnitude < 140f;
    }
}
