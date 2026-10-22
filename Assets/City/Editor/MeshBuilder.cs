using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LifeInDowntown.City
{
    // Accumulates triangles with per-face normals. Every triangle is given the
    // direction it should face; the winding is fixed up to match, so callers
    // never reason about Unity's clockwise front-face rule.
    public class MeshBuilder
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int> tris = new List<int>();

        public int VertexCount => verts.Count;

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 facing, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0f)
            {
                (b, c) = (c, b);
                (ub, uc) = (uc, ub);
            }
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            var n = facing.normalized;
            normals.Add(n); normals.Add(n); normals.Add(n);
            uvs.Add(ua); uvs.Add(ub); uvs.Add(uc);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        // Quad a-b-c-d in order around its edge.
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing,
                         Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            Tri(a, b, c, facing, ua, ub, uc);
            Tri(a, c, d, facing, ua, uc, ud);
        }

        // Flat horizontal polygon at height y, facing up or down. UVs in metres (world XZ).
        public void Polygon(List<Vector2> ring, float y, bool up)
        {
            var facing = up ? Vector3.up : Vector3.down;
            foreach (var t in Triangulator.EarClip(ring))
            {
                Vector2 p0 = ring[t.x], p1 = ring[t.y], p2 = ring[t.z];
                Tri(new Vector3(p0.x, y, p0.y), new Vector3(p1.x, y, p1.y), new Vector3(p2.x, y, p2.y),
                    facing, p0, p1, p2);
            }
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            m.SetVertices(verts);
            m.SetNormals(normals);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }
    }

    public static class Triangulator
    {
        // Ear clipping for a simple polygon (no holes). Works for either winding.
        public static List<Vector3Int> EarClip(List<Vector2> pts)
        {
            var result = new List<Vector3Int>();
            int n = pts.Count;
            if (n < 3) return result;
            var idx = new List<int>(n);
            for (int i = 0; i < n; i++) idx.Add(i);
            bool ccw = SignedArea(pts) > 0f;

            int guard = n * n;
            while (idx.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int ia = idx[(i + idx.Count - 1) % idx.Count], ib = idx[i], ic = idx[(i + 1) % idx.Count];
                    Vector2 a = pts[ia], b = pts[ib], c = pts[ic];
                    float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                    if (ccw ? cross <= 1e-6f : cross >= -1e-6f) continue;   // reflex or degenerate
                    bool contains = false;
                    foreach (int j in idx)
                    {
                        if (j == ia || j == ib || j == ic) continue;
                        if (InTriangle(pts[j], a, b, c)) { contains = true; break; }
                    }
                    if (contains) continue;
                    result.Add(new Vector3Int(ia, ib, ic));
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;   // self-intersecting input: keep what we have
            }
            if (idx.Count == 3) result.Add(new Vector3Int(idx[0], idx[1], idx[2]));
            return result;
        }

        public static float SignedArea(List<Vector2> p)
        {
            float a = 0f;
            for (int i = 0; i < p.Count; i++)
            {
                var q = p[(i + 1) % p.Count];
                a += p[i].x * q.y - q.x * p[i].y;
            }
            return a * 0.5f;
        }

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Sign(p, a, b), d2 = Sign(p, b, c), d3 = Sign(p, c, a);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        static float Sign(Vector2 p1, Vector2 p2, Vector2 p3) =>
            (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }
}
