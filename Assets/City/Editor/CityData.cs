using System;
using System.Collections.Generic;
using UnityEngine;

namespace LifeInDowntown.City
{
    // Mirrors the JSON written by Tools/osm/build_city_json.py.
    // Point lists are flat [x0, z0, x1, z1, ...] in metres (x = east, z = north).
    [Serializable]
    public class CityData
    {
        public string source;
        public Bounds2 bounds;
        public Building[] buildings;
        public Way[] roads;
        public Way[] paths;
        public Plaza[] plazas;
    }

    [Serializable]
    public class Bounds2
    {
        public float xmin, xmax, zmin, zmax;
    }

    [Serializable]
    public class Building
    {
        public long id;
        public bool part;
        public string kind;
        public string name;
        public float h;
        public float minH;
        public float[] pts;
    }

    [Serializable]
    public class Way
    {
        public long id;
        public string type;
        public string name;
        public float width;
        public bool oneway;
        public float[] pts;
    }

    [Serializable]
    public class Plaza
    {
        public long id;
        public string name;
        public string kind;   // "road" (area:highway road surface) or "pedestrian"
        public float[] pts;
    }

    // Tools/assets/make_signs.py output (docs/DESIGN.md 3G)
    [Serializable]
    public class Sign
    {
        public string text, kind, style;        // style: neon (store) / plate (building name) / crown (tower top)
        public float x, z, nx, nz, y, h, w;     // centre on the wall, outward normal, height, size (m)
        public float[] uv;                      // u0, v0, u1, v1 in the atlas
        public float[] color;                   // neon colour (light tint)
    }

    [Serializable]
    public class SignList { public Sign[] signs; }

    public static class CityDataUtil
    {
        public static List<Vector2> Points(float[] flat)
        {
            var list = new List<Vector2>(flat.Length / 2);
            for (int i = 0; i + 1 < flat.Length; i += 2)
                list.Add(new Vector2(flat[i], flat[i + 1]));
            return list;
        }
    }
}
