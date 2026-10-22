using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LifeInDowntown.EditorTools
{
    // The held / pickup pistol (docs/DESIGN.md 3M) is the duty pistol on Rocketbox Police_Male_01's belt:
    // one material slot of his skinned mesh. This cuts that slot's triangles out into a standalone mesh and
    // orients it from its own shape: the longest axis is the barrel, the slide is the side that runs the full
    // length (the grip hangs off one end), and the muzzle is the end away from the grip. Result: muzzle +Z,
    // slide +Y, origin at the centre. Assets/Characters/Weapons/Pistol.prefab.
    public static class PistolImport
    {
        const string Fbx = CharacterImport.Root + "/Police_Male_01/Police_Male_01.fbx";
        const string Dir = "Assets/Characters/Weapons";
        public const string PrefabPath = Dir + "/Pistol.prefab";

        public static void Extract()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            if (model == null) { Debug.Log("PISTOL_SKIP no officer model"); return; }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
            int sub = System.Array.FindIndex(smr.sharedMaterials, m => m != null && m.name.ToLowerInvariant().Contains("pistol"));
            if (sub < 0) { Object.DestroyImmediate(inst); Debug.LogWarning("PISTOL_WARN no pistol slot"); return; }

            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            var src = baked.vertices; var nrm = baked.normals; var uv = baked.uv;
            var tris = baked.GetTriangles(sub);
            var map = new Dictionary<int, int>();
            var pos = new List<Vector3>(); var nr = new List<Vector3>(); var uvs = new List<Vector2>();
            var outTris = new int[tris.Length];
            for (int i = 0; i < tris.Length; i++)
            {
                if (!map.TryGetValue(tris[i], out int k))
                {
                    k = pos.Count; map[tris[i]] = k;
                    pos.Add(src[tris[i]]); nr.Add(nrm.Length > 0 ? nrm[tris[i]] : Vector3.up); uvs.Add(uv.Length > 0 ? uv[tris[i]] : Vector2.zero);
                }
                outTris[i] = k;
            }
            Object.DestroyImmediate(inst);

            // principal axes
            Vector3 c = Vector3.zero;
            foreach (var p in pos) c += p;
            c /= pos.Count;
            var barrel = Principal(pos, c, Vector3.zero);
            var side = Principal(pos, c, barrel);
            // slide vs grip along the second axis: the slide band spans the whole barrel length
            float wMin = float.MaxValue, wMax = float.MinValue;
            foreach (var p in pos) { float w = Vector3.Dot(p - c, side); wMin = Mathf.Min(wMin, w); wMax = Mathf.Max(wMax, w); }
            float SpanAt(bool top, out float meanT)
            {
                float lo = float.MaxValue, hi = float.MinValue, sum = 0f; int n = 0;
                foreach (var p in pos)
                {
                    float w = Vector3.Dot(p - c, side), t = Vector3.Dot(p - c, barrel);
                    bool band = top ? w > wMax - (wMax - wMin) * 0.15f : w < wMin + (wMax - wMin) * 0.15f;
                    if (!band) continue;
                    lo = Mathf.Min(lo, t); hi = Mathf.Max(hi, t); sum += t; n++;
                }
                meanT = n > 0 ? sum / n : 0f;
                return hi - lo;
            }
            float spanTop = SpanAt(true, out float meanTop), spanBottom = SpanAt(false, out float meanBottom);
            Vector3 up = spanTop >= spanBottom ? side : -side;
            float gripT = spanTop >= spanBottom ? meanBottom : meanTop;
            Vector3 muzzle = gripT < 0f ? barrel : -barrel;     // the end away from the grip
            var toLocal = Quaternion.Inverse(Quaternion.LookRotation(muzzle, up));

            var b = new Bounds();
            for (int i = 0; i < pos.Count; i++)
            {
                pos[i] = toLocal * (pos[i] - c); nr[i] = toLocal * nr[i];
                if (i == 0) b = new Bounds(pos[i], Vector3.zero); else b.Encapsulate(pos[i]);
            }
            for (int i = 0; i < pos.Count; i++) pos[i] -= b.center;

            var mesh = new Mesh { name = "Pistol_m220" };
            mesh.SetVertices(pos); mesh.SetNormals(nr); mesh.SetUVs(0, uvs); mesh.SetTriangles(outTris, 0);
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            Directory.CreateDirectory(Dir);
            string meshPath = Dir + "/Pistol_m220.asset";
            if (File.Exists(meshPath)) AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);

            var go = new GameObject("Pistol");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(CharacterImport.Root + "/Police_Male_01/m220_pistol.mat");
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // held at the lens: no shadow on the chest
            PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            Debug.Log($"PISTOL_OK verts={pos.Count} tris={outTris.Length / 3} size(w,h,l)={mesh.bounds.size.x:F3},{mesh.bounds.size.y:F3},{mesh.bounds.size.z:F3} m " +
                      $"slideSpan={Mathf.Max(spanTop, spanBottom):F3} gripSpan={Mathf.Min(spanTop, spanBottom):F3}");
        }

        // dominant direction of the points around c, perpendicular to 'except' (power iteration on the covariance)
        static Vector3 Principal(List<Vector3> pts, Vector3 c, Vector3 except)
        {
            float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (var p0 in pts)
            {
                var p = p0 - c; p -= Vector3.Dot(p, except) * except;
                xx += p.x * p.x; xy += p.x * p.y; xz += p.x * p.z; yy += p.y * p.y; yz += p.y * p.z; zz += p.z * p.z;
            }
            var v = new Vector3(0.577f, 0.577f, 0.577f);
            if (except != Vector3.zero) v = Vector3.Cross(except, Mathf.Abs(except.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            for (int i = 0; i < 64; i++)
            {
                var n = new Vector3(xx * v.x + xy * v.y + xz * v.z, xy * v.x + yy * v.y + yz * v.z, xz * v.x + yz * v.y + zz * v.z);
                n -= Vector3.Dot(n, except) * except;
                if (n.sqrMagnitude < 1e-12f) break;
                v = n.normalized;
            }
            return v;
        }
    }
}
