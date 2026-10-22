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

            Debug.Log($"SHOT_GFX {SystemInfo.graphicsDeviceType} rayTracing={SystemInfo.supportsRayTracing}");
            var rain = GameObject.Find("Rain");

            foreach (var (name, pos, target) in poses)
            {
                cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos));
                if (rain != null)
                {
                    // the rain box stands on the ground around the camera (useless high up, so aerials skip it)
                    rain.SetActive(pos.y < 30f);
                    rain.transform.position = new Vector3(pos.x, 0f, pos.z);
                }
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

        // Eye-level pose on the right-hand sidewalk of the named street, standing on a real point of the
        // street ~70 m from the Yonge & Dundas origin (south of it for north views, east of it for west views)
        // and looking back along the street through the intersection. Streets bend, so no straight-line guess.
        static void AddStreetPose(List<(string, Vector3, Vector3)> poses, CityData data, string street, string name, bool southToNorth)
        {
            Vector2 eyeOnRoad = Vector2.zero;
            float bestErr = float.MaxValue, roadWidth = 10f;
            foreach (var r in data.roads)
            {
                if (r.name != street) continue;
                foreach (var v in CityDataUtil.Points(r.pts))
                {
                    bool behind = southToNorth ? v.y < 0f : v.x > 0f;
                    float err = Mathf.Abs(v.magnitude - 70f);
                    if (behind && err < bestErr) { bestErr = err; eyeOnRoad = v; roadWidth = r.width; }
                }
            }
            if (bestErr == float.MaxValue) { Debug.LogWarning($"SHOT_SKIP no road named {street}"); return; }
            Vector2 dir = (-eyeOnRoad).normalized;   // towards the intersection
            // 2.5 m past the curb: streetlight poles stand 0.6 m past it
            Vector2 eye = eyeOnRoad + new Vector2(dir.y, -dir.x) * (roadWidth * 0.5f + 2.5f);
            Vector2 target = eye + dir * 150f;
            poses.Add((name, new Vector3(eye.x, 1.7f, eye.y), new Vector3(target.x, 8f, target.y)));
        }
    }
}
