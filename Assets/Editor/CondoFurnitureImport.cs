using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace LifeInDowntown.EditorTools
{
    // Turns the Poly Haven FBX downloads (Tools/assets/fetch_condo.py) into HDRP prefabs
    // (docs/DESIGN.md 3H). Per material slot <slot>: <slot>_diff, _nor_gl, _rough, _metal, _opacity/_alpha.
    // HDRP Lit wants a packed mask (R metal, G AO, A smoothness) and alpha in the colour map, so both are
    // packed here (Unity reads the EXR maps Python can't). Output: Assets/Condo/Prefabs/<model>.prefab
    public static class CondoFurnitureImport
    {
        public const string ModelsDir = "Assets/Condo/Models";
        public const string PrefabDir = "Assets/Condo/Prefabs";
        const int MaskSize = 1024;

        [MenuItem("Life In Downtown/Import Condo Furniture")]
        public static void ImportAll()
        {
            Directory.CreateDirectory(PrefabDir);
            int n = 0;
            foreach (var dir in Directory.GetDirectories(ModelsDir))
            {
                string id = Path.GetFileName(dir);
                if (Build(id)) n++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"FURNITURE_IMPORT_OK prefabs={n}");
        }

        public static void ImportFromCommandLine() => ImportAll();

        // Read-only: for seating, the highest vertex is the top of the backrest, i.e. the BACK of the model.
        // Prints which local Z side that is, so the layout's facing can be set from data, not guesswork.
        public static void ReportFacing()
        {
            foreach (var id in new[] { "sofa_03", "mid_century_lounge_chair", "modern_arm_chair_01", "bar_chair_round_01" })
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{id}.prefab"));
                Vector3 top = Vector3.down * 1e9f;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                    foreach (var v in mf.sharedMesh.vertices)
                    {
                        var w = go.transform.InverseTransformPoint(mf.transform.TransformPoint(v));
                        if (w.y > top.y) top = w;
                    }
                Debug.Log($"FACING {id}: highest point at local z={top.z:F2} -> back is {(top.z < 0 ? "-Z, front +Z" : "+Z, front -Z")}");
                Object.DestroyImmediate(go);
            }
            EditorApplication.Exit(0);
        }

        static bool Build(string id)
        {
            string fbx = $"{ModelsDir}/{id}/{id}.fbx";
            var imp = AssetImporter.GetAtPath(fbx) as ModelImporter;
            if (imp == null) { Debug.LogWarning($"FURNITURE_WARN no fbx {fbx}"); return false; }
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;   // keeps slot names
            imp.importAnimation = false;
            imp.addCollider = false;
            imp.SaveAndReimport();

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
            var made = new Dictionary<string, Material>();
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                var slots = r.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    string slot = slots[i] != null ? slots[i].name : id;
                    if (!made.TryGetValue(slot, out var m)) made[slot] = m = SlotMaterial(id, slot);
                    slots[i] = m;
                }
                r.sharedMaterials = slots;
            }
            var root = new GameObject(id);
            inst.transform.SetParent(root.transform, false);
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())   // furniture blocks the player
                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
            PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{id}.prefab");
            var b = Bounds(root);
            Object.DestroyImmediate(root);
            Debug.Log($"FURNITURE_IMPORT {id}: {made.Count} material(s) [{string.Join(", ", made.Keys)}] size={b.size} centre={b.center}");
            return true;
        }

        public static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        // ------------------------------------------------------------ materials
        static Material SlotMaterial(string id, string slot)
        {
            string tex = $"{ModelsDir}/{id}/textures";
            string matPath = $"{ModelsDir}/{id}/{slot}_HDRP.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(Shader.Find("HDRP/Lit")); AssetDatabase.CreateAsset(mat, matPath); }

            string pre = Find(tex, slot) ?? Find(tex, id);   // some slots reuse the model's texture set
            var diff = Load(tex, pre, "_diff_2k");
            var alpha = Load(tex, pre, "_opacity_2k") ?? Load(tex, pre, "_alpha_2k");
            if (diff != null && alpha != null) diff = CombineAlpha(tex, pre, diff, alpha);
            mat.SetTexture("_BaseColorMap", diff);
            mat.SetColor("_BaseColor", Color.white);
            var nrm = Load(tex, pre, "_nor_gl_2k", normal: true);
            if (nrm != null) mat.SetTexture("_NormalMap", nrm);
            var mask = PackMask(tex, pre);
            if (mask != null)
            {
                mat.SetTexture("_MaskMap", mask);
                mat.SetFloat("_MetallicRemapMin", 0f); mat.SetFloat("_MetallicRemapMax", 1f);
                mat.SetFloat("_SmoothnessRemapMin", 0f); mat.SetFloat("_SmoothnessRemapMax", 1f);
                mat.SetFloat("_AORemapMin", 0f); mat.SetFloat("_AORemapMax", 1f);
            }
            else mat.SetFloat("_Smoothness", 0.35f);
            if (alpha != null)
            {
                mat.SetFloat("_AlphaCutoffEnable", 1f);
                mat.SetFloat("_AlphaCutoff", 0.5f);
                mat.SetFloat("_DoubleSidedEnable", 1f);
            }
            HDMaterial.ValidateMaterial(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // texture-name prefix for a slot: the longest "<prefix>_diff_2k" file the slot name starts with
        static string Find(string dir, string slot)
        {
            if (!Directory.Exists(dir)) return null;
            var prefixes = Directory.GetFiles(dir, "*_diff_2k.*").Select(f => Path.GetFileName(f)).Select(n => n.Substring(0, n.IndexOf("_diff_2k")));
            return prefixes.Where(p => slot.StartsWith(p) || p.StartsWith(slot)).OrderByDescending(p => p.Length).FirstOrDefault();
        }

        static Texture2D Load(string dir, string pre, string suffix, bool normal = false, bool readable = false, bool linear = false)
        {
            if (pre == null) return null;
            foreach (var ext in new[] { ".jpg", ".png", ".exr" })
            {
                string p = $"{dir}/{pre}{suffix}{ext}";
                var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                if (ti == null) continue;
                var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                bool srgb = !normal && !linear;
                if (ti.textureType != type || ti.isReadable != readable || ti.sRGBTexture != srgb)
                {
                    ti.textureType = type; ti.isReadable = readable; ti.sRGBTexture = srgb;
                    ti.SaveAndReimport();
                }
                return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }
            return null;
        }

        static Texture2D PackMask(string dir, string pre)
        {
            var rough = Load(dir, pre, "_rough_2k", readable: true, linear: true);
            if (rough == null) return null;
            var metal = Load(dir, pre, "_metal_2k", readable: true, linear: true);
            var outTex = new Texture2D(MaskSize, MaskSize, TextureFormat.RGBA32, false, true);
            var px = new Color32[MaskSize * MaskSize];
            for (int y = 0; y < MaskSize; y++)
                for (int x = 0; x < MaskSize; x++)
                {
                    float u = (x + 0.5f) / MaskSize, v = (y + 0.5f) / MaskSize;
                    float r = rough.GetPixelBilinear(u, v).r;
                    float m = metal != null ? metal.GetPixelBilinear(u, v).r : 0f;
                    px[y * MaskSize + x] = new Color32((byte)(Mathf.Clamp01(m) * 255), 255, 0, (byte)((1f - Mathf.Clamp01(r)) * 255));
                }
            outTex.SetPixels32(px);
            string path = $"{dir}/{pre}_mask_1k.png";
            File.WriteAllBytes(path, outTex.EncodeToPNG());
            Object.DestroyImmediate(outTex);
            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.sRGBTexture = false; ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Texture2D CombineAlpha(string dir, string pre, Texture2D diffTex, Texture2D alphaTex)
        {
            var d = Load(dir, pre, "_diff_2k", readable: true);
            var a = Load(dir, pre, AssetDatabase.GetAssetPath(alphaTex).Contains("_opacity_") ? "_opacity_2k" : "_alpha_2k", readable: true, linear: true);
            int w = d.width, h = d.height;
            var px = d.GetPixels32();
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x].a = (byte)(a.GetPixelBilinear((x + 0.5f) / w, (y + 0.5f) / h).r * 255);
            var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            outTex.SetPixels32(px);
            string path = $"{dir}/{pre}_diffalpha_2k.png";
            File.WriteAllBytes(path, outTex.EncodeToPNG());
            Object.DestroyImmediate(outTex);
            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.alphaIsTransparency = true; ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
