using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace LifeInDowntown.City
{
    // Look pass L3 + L4 (docs/DESIGN.md 3A): ray tracing switched on in every HDRP quality asset,
    // puddle decals on roads and sidewalks, and a camera-following rain particle system.
    // Wet material values live in CityGenerator.Materials; RT reflections in NightLook's profile.
    public static class RainLook
    {
        const string RainDir = "Assets/City/Rain";
        const int PuddleCount = 180;

        public static void EnableRayTracing()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:HDRenderPipelineAsset"))
            {
                var asset = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                var so = new SerializedObject(asset);
                Set(so, "m_RenderPipelineSettings.supportRayTracing", true);
                Set(so, "m_RenderPipelineSettings.supportSSR", true);
                var mode = so.FindProperty("m_RenderPipelineSettings.supportedRayTracingMode");
                if (mode != null) mode.intValue = 3;   // performance + quality
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
        }

        static void Set(SerializedObject so, string path, bool value)
        {
            var p = so.FindProperty(path);
            if (p != null) p.boolValue = value;
            else Debug.LogWarning($"CITY_WARN HDRP setting not found: {path}");
        }

        // ------------------------------------------------------------------ puddles
        public static int BuildPuddles(CityData d, Transform parent)
        {
            var mat = PuddleMaterial();
            var group = new GameObject("Puddles").transform;
            group.SetParent(parent, false);
            var rnd = new System.Random(5150);
            var segs = new List<(Vector2 a, Vector2 b, float w)>();
            float total = 0f;
            foreach (var r in d.roads)
            {
                var p = CityDataUtil.Points(r.pts);
                for (int i = 0; i + 1 < p.Count; i++)
                {
                    segs.Add((p[i], p[i + 1], r.width));
                    total += (p[i + 1] - p[i]).magnitude;
                }
            }
            for (int n = 0; n < PuddleCount; n++)
            {
                // pick a point along the network weighted by length, then a spot across road + sidewalk
                float pick = (float)rnd.NextDouble() * total;
                var s = segs[0];
                foreach (var seg in segs)
                {
                    float len = (seg.b - seg.a).magnitude;
                    if (pick <= len) { s = seg; break; }
                    pick -= len;
                }
                Vector2 dir = (s.b - s.a).normalized, left = new Vector2(-dir.y, dir.x);
                float t = Mathf.Clamp01(pick / Mathf.Max(0.01f, (s.b - s.a).magnitude));
                float across = ((float)rnd.NextDouble() * 2f - 1f) * (s.w * 0.5f + 2.5f);
                Vector2 at = Vector2.Lerp(s.a, s.b, t) + left * across;
                float size = 1.5f + (float)rnd.NextDouble() * 4f;

                var go = new GameObject($"Puddle_{n:000}");
                go.transform.SetParent(group, false);
                go.transform.SetPositionAndRotation(new Vector3(at.x, 0.5f, at.y),
                    Quaternion.Euler(90f, (float)rnd.NextDouble() * 360f, 0f));
                var dp = go.AddComponent<DecalProjector>();
                dp.material = mat;
                dp.size = new Vector3(size, size * (0.5f + (float)rnd.NextDouble() * 0.6f), 1.2f);
                dp.pivot = new Vector3(0f, 0f, 0.6f);
                dp.fadeFactor = 1f;
            }
            return PuddleCount;
        }

        static Material PuddleMaterial()
        {
            string path = $"{RainDir}/Puddle.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("HDRP/Decal")) { name = "Puddle" };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_BaseColorMap", Tex($"{RainDir}/Puddle_Color.png", false));
            mat.SetTexture("_MaskMap", Tex($"{RainDir}/Puddle_Mask.png", true));
            mat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.85f));
            mat.SetFloat("_AffectAlbedo", 1f);
            mat.SetFloat("_AffectNormal", 1f);      // no normal map = flat water surface
            mat.SetFloat("_AffectSmoothness", 1f);
            mat.SetFloat("_AffectMetal", 0f);
            mat.SetFloat("_AffectAO", 0f);
            mat.SetFloat("_NormalBlendSrc", 0f);     // normal opacity from base colour alpha
            mat.SetFloat("_MaskBlendSrc", 1f);       // mask opacity from mask map blue
            mat.SetFloat("_DecalMaskMapBlueScale", 1f);
            mat.SetFloat("_SmoothnessRemapMin", 0.9f);
            mat.SetFloat("_SmoothnessRemapMax", 1f);
            HDMaterial.ValidateMaterial(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // --------------------------------------------------------------------- rain
        // A box of motion-blurred streaks centred on the camera. A particle system was tried first:
        // it simulated (14,400 particles) but never drew in edit-mode batch renders, so rain is a mesh.
        // In play it will follow the camera and scroll downward (runtime script, task 6); ShotTool
        // moves it to each shot.
        const int StreakCount = 9000;
        public const float RainBoxSize = 36f, RainBoxHeight = 22f;

        public static GameObject BuildRain()
        {
            var old = GameObject.Find("Rain");
            if (old != null) Object.DestroyImmediate(old);
            var go = new GameObject("Rain");
            go.transform.position = new Vector3(0f, 0f, -40f);

            var rnd = new System.Random(77);
            var mb = new MeshBuilder();
            var wind = new Vector3(-0.08f, -1f, 0.03f).normalized;   // slight lean
            for (int i = 0; i < StreakCount; i++)
            {
                float R() => (float)rnd.NextDouble();
                var c = new Vector3((R() - 0.5f) * RainBoxSize, R() * RainBoxHeight, (R() - 0.5f) * RainBoxSize);
                if (c.x * c.x + c.z * c.z < 2.5f * 2.5f) continue;   // keep streaks off the lens
                float len = 0.5f + R() * 0.5f, width = 0.012f + R() * 0.008f;
                float yaw = R() * Mathf.PI;
                var side = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)) * width;
                var half = wind * (len * 0.5f);
                // two crossed quads so streaks read from any direction
                foreach (var s in new[] { side, Vector3.Cross(wind, side).normalized * width })
                {
                    var f = Vector3.Cross(s, wind);
                    mb.Quad(c - s + half, c + s + half, c + s - half, c - s - half, f,
                            new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
                    mb.Quad(c - s + half, c + s + half, c + s - half, c - s - half, -f,
                            new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
                }
            }
            var mesh = mb.ToMesh("RainStreaks");
            string meshPath = $"{RainDir}/RainStreaks.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(meshPath) != null) AssetDatabase.DeleteAsset(meshPath);
            AssetDatabase.CreateAsset(mesh, meshPath);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = RainMaterial();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        static Material RainMaterial()
        {
            string path = $"{RainDir}/RainStreak.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("HDRP/Unlit")) { name = "RainStreak" };
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_UnlitColorMap", Tex($"{RainDir}/RainStreak.png", true));
            var tint = new Color(0.7f, 0.75f, 0.85f) * RainBrightness;
            mat.SetColor("_UnlitColor", new Color(tint.r, tint.g, tint.b, 0.18f));
            mat.SetFloat("_SurfaceType", 1f);   // transparent
            mat.SetFloat("_BlendMode", 0f);     // alpha
            mat.SetFloat("_AlphaCutoffEnable", 0f);
            mat.SetFloat("_ZWrite", 0f);
            HDMaterial.ValidateMaterial(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // HDRP Unlit colour is luminance: 1 rendered nothing visible through the particle path,
        // 30 on the mesh glowed like sparks; 1.2 with low alpha reads as rain at EV 1.5
        public const float RainBrightness = 1.2f;

        static Texture2D Tex(string path, bool linear)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (imp != null && (imp.sRGBTexture == linear || !imp.alphaIsTransparency))
            {
                imp.sRGBTexture = !linear;
                imp.alphaIsTransparency = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
