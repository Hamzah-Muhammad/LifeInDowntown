using System.Collections.Generic;
using System.IO;
using LifeInDowntown.City;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace LifeInDowntown.EditorTools
{
    // Renders named camera poses of the city scene to Screenshots/<pose>.png (docs/DESIGN.md 6).
    // Run with -batchmode but WITHOUT -nographics so HDRP renders on the GPU.
    public static class ShotTool
    {
        const string ScenePath = "Assets/Scenes/Downtown_YongeDundas.unity";
        const string DataPath = "Assets/City/Data/downtown_yonge_dundas.json";
        const int Width = 1600, Height = 900, WarmupFrames = 24;

        [MenuItem("Life In Downtown/Render Screenshots")]
        public static void Run()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var data = JsonUtility.FromJson<CityData>(File.ReadAllText(DataPath));
            Directory.CreateDirectory("Screenshots");

            var poses = new List<(string name, Vector3 pos, Vector3 target)>
            {
                ("overhead", new Vector3(0, 420, -330), new Vector3(0, 0, -20)),
                ("aerial_northwest", new Vector3(-260, 160, -260), new Vector3(0, 30, 0)),
            };
            AddStreetPose(poses, data, "Yonge Street", "street_yonge_looking_north", southToNorth: true);
            AddStreetPose(poses, data, "Dundas Street East", "street_dundas_looking_west", southToNorth: false);

            var go = new GameObject("ShotCamera");
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 3000f;
            cam.fieldOfView = 60f;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;

            foreach (var (name, pos, target) in poses)
            {
                cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos));
                for (int i = 0; i < WarmupFrames; i++) Render(cam, rt);   // lets auto-exposure and sky settle
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                string file = Path.Combine("Screenshots", name + ".png");
                File.WriteAllBytes(file, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                Debug.Log($"SHOT_OK {file} pos={pos}");
            }
            Object.DestroyImmediate(go);
            rt.Release();
        }

        static void Render(Camera cam, RenderTexture rt)
        {
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else cam.Render();
        }

        // Eye-level pose 70 m back along the named street from the Yonge & Dundas origin, looking along it.
        static void AddStreetPose(List<(string, Vector3, Vector3)> poses, CityData data, string street, string name, bool southToNorth)
        {
            Vector2 best = Vector2.zero, other = Vector2.zero;
            float bestDist = float.MaxValue, roadWidth = 10f;
            foreach (var r in data.roads)
            {
                if (r.name != street) continue;
                var p = CityDataUtil.Points(r.pts);
                for (int i = 0; i + 1 < p.Count; i++)
                {
                    float d = p[i].magnitude;
                    if (d < bestDist) { bestDist = d; best = p[i]; other = p[i + 1]; roadWidth = r.width; }
                }
            }
            if (bestDist == float.MaxValue) { Debug.LogWarning($"SHOT_SKIP no road named {street}"); return; }
            Vector2 dir = (other - best).normalized;
            if (southToNorth && dir.y < 0) dir = -dir;      // point north
            if (!southToNorth && dir.x > 0) dir = -dir;     // point west
            // stand on the right-hand sidewalk, 70 m behind the origin
            Vector2 eye = -dir * 70f + new Vector2(dir.y, -dir.x) * (roadWidth * 0.5f + 1.5f);
            poses.Add((name, new Vector3(eye.x, 1.7f, eye.y), new Vector3(dir.x * 120f, 8f, dir.y * 120f)));
        }
    }
}
