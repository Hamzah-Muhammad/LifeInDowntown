using System;
using System.Collections.Generic;
using System.IO;
using LifeInDowntown.Player;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace LifeInDowntown.Game
{
    // Player settings (docs/DESIGN.md 3C), persisted to <persistentDataPath>/settings.json and applied live.
    [Serializable]
    public class SettingsData
    {
        public int quality = 0;                 // index into QualitySettings.names (High Fidelity, Balanced, Performant)
        public int width, height;               // 0 = current screen resolution
        public FullScreenMode displayMode = FullScreenMode.FullScreenWindow;
        public bool vsync = true;
        public bool rayTracing = true;
        public bool motionBlur = true;
        public float horizontalFov = 100f;      // degrees
        public float mouseSensitivity = 1f;     // multiplier
        public float cameraShake = 1f;          // 0..1.5
        public float masterVolume = 0.8f;
    }

    public static class GameSettings
    {
        // the whole mix plays at 5% of its old level (Hamzah 2026-10-22: "wayyy to loud"); the slider keeps its range
        public const float MixScale = 0.05f;
        public const float BaseLookSensitivity = 0.08f;
        static string pathOverride;   // tests point this at a temp file
        public static string FilePath
        {
            get => pathOverride ?? Path.Combine(Application.persistentDataPath, "settings.json");
            set => pathOverride = value;
        }
        public static SettingsData Current { get; private set; } = new SettingsData();

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath)) Current = JsonUtility.FromJson<SettingsData>(File.ReadAllText(FilePath)) ?? new SettingsData();
            }
            catch (Exception e) { Debug.LogWarning($"Settings unreadable, using defaults: {e.Message}"); Current = new SettingsData(); }
        }

        public static void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonUtility.ToJson(Current, true));
        }

        public static List<Resolution> Resolutions()
        {
            var list = new List<Resolution>();
            foreach (var r in Screen.resolutions)
                if (!list.Exists(x => x.width == r.width && x.height == r.height)) list.Add(r);
            list.Sort((a, b) => (b.width * b.height).CompareTo(a.width * a.height));
            return list;
        }

        // pushes Current into the engine and the scene's player/camera
        public static void Apply()
        {
            var s = Current;
            if (s.quality >= 0 && s.quality < QualitySettings.names.Length) QualitySettings.SetQualityLevel(s.quality, true);
            QualitySettings.vSyncCount = s.vsync ? 1 : 0;
            int w = s.width > 0 ? s.width : Screen.currentResolution.width;
            int h = s.height > 0 ? s.height : Screen.currentResolution.height;
            if (!Application.isEditor) Screen.SetResolution(w, h, s.displayMode);
            AudioListener.volume = s.masterVolume * MixScale;

            var player = UnityEngine.Object.FindFirstObjectByType<BodycamPlayer>(FindObjectsInactive.Include);
            if (player != null) player.lookSensitivity = BaseLookSensitivity * s.mouseSensitivity;
            var rig = UnityEngine.Object.FindFirstObjectByType<BodycamRig>(FindObjectsInactive.Include);
            if (rig != null)
            {
                rig.shakeScale = s.cameraShake;
                var cam = rig.GetComponent<Camera>();
                if (cam != null)
                {
                    float aspect = cam.aspect > 0f ? cam.aspect : 16f / 9f;
                    cam.fieldOfView = 2f * Mathf.Atan(Mathf.Tan(s.horizontalFov * 0.5f * Mathf.Deg2Rad) / aspect) * Mathf.Rad2Deg;
                    SetRayTracing(cam, s.rayTracing);
                }
                var vol = rig.GetComponentInChildren<Volume>(true);
                if (vol != null && vol.profile.TryGet<MotionBlur>(out var mb)) mb.active = s.motionBlur;
            }
        }

        static void SetRayTracing(Camera cam, bool on)
        {
            var hd = cam.GetComponent<HDAdditionalCameraData>();
            if (hd == null) return;
            hd.customRenderingSettings = true;
            hd.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.RayTracing] = true;
            hd.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.RayTracing, on);
        }
    }
}
