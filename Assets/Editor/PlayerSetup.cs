using System.IO;
using LifeInDowntown.City;
using LifeInDowntown.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace LifeInDowntown.EditorTools
{
    // Puts the bodycam player (docs/DESIGN.md 3B.1) into the city scene: body, chest camera, overlay,
    // rain follow, and the Bodycam volume profile layered over NightRain. Idempotent; called by
    // CityGenerator.Generate after the city is built.
    public static class PlayerSetup
    {
        const string ProfilePath = "Assets/City/Look/Bodycam.asset";
        const string ActionsPath = "Assets/InputSystem_Actions.inputactions";
        public const float VerticalFov = 68f;   // ~100 degrees horizontal at 16:9

        public static void Setup(CityData data)
        {
            foreach (var n in new[] { "Player", "BodycamCamera" })
            {
                var old = GameObject.Find(n);
                if (old != null) Object.DestroyImmediate(old);
            }
            var template = GameObject.Find("Main Camera");
            if (template != null) template.SetActive(false);

            Vector3 spawn = SpawnPoint(data, out float yaw);
            var body = new GameObject("Player");
            body.transform.SetPositionAndRotation(spawn, Quaternion.Euler(0f, yaw, 0f));
            var cc = body.AddComponent<CharacterController>();
            cc.height = 1.75f; cc.radius = 0.3f; cc.center = Vector3.up * 0.875f;
            cc.stepOffset = 0.35f; cc.slopeLimit = 50f; cc.skinWidth = 0.04f;
            var player = body.AddComponent<BodycamPlayer>();
            player.actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);

            var camGo = new GameObject("BodycamCamera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = VerticalFov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 3000f;
            camGo.AddComponent<HDAdditionalCameraData>();
            camGo.AddComponent<AudioListener>();
            var rig = camGo.AddComponent<BodycamRig>();
            rig.player = player;
            rig.Tick(1f / 60f);   // place it at the mount now so the scene opens at the right pose
            camGo.AddComponent<BodycamOverlay>();

            var rain = GameObject.Find("Rain");
            if (rain != null)
            {
                var follow = rain.GetComponent<RainFollow>() ?? rain.AddComponent<RainFollow>();
                follow.target = camGo.transform;
                follow.boxHeight = RainLook.RainBoxHeight;
            }

            var volGo = new GameObject("BodycamVolume");
            volGo.transform.SetParent(camGo.transform, false);
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 20f;
            vol.sharedProfile = BuildProfile();
            Debug.Log($"PLAYER_OK spawn={spawn} yaw={yaw:F0}");
        }

        // right-hand sidewalk of Yonge, ~40 m south of Dundas, facing north
        static Vector3 SpawnPoint(CityData data, out float yaw)
        {
            Vector2 best = new Vector2(0f, -40f);
            float bestErr = float.MaxValue, width = 13f;
            foreach (var r in data.roads)
            {
                if (r.name != "Yonge Street") continue;
                foreach (var v in CityDataUtil.Points(r.pts))
                {
                    float err = Mathf.Abs(v.magnitude - 40f);
                    if (v.y < 0f && err < bestErr) { bestErr = err; best = v; width = r.width; }
                }
            }
            Vector2 dir = (-best).normalized;
            Vector2 pos = best + new Vector2(dir.y, -dir.x) * (width * 0.5f + 2.5f);
            yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;
            return new Vector3(pos.x, 0.3f, pos.y);
        }

        static VolumeProfile BuildProfile()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath));
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath) != null) AssetDatabase.DeleteAsset(ProfilePath);
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, ProfilePath);

            var lens = Add<LensDistortion>(p);
            lens.intensity.Override(BarrelIntensity);
            lens.scale.Override(1.06f);          // crop in a little so the bent edges stay filled

            var mb = Add<MotionBlur>(p);
            mb.intensity.Override(0.6f);

            var vig = Add<Vignette>(p);
            vig.intensity.Override(0.42f);
            vig.smoothness.Override(0.5f);

            var ca = Add<ChromaticAberration>(p);
            ca.intensity.Override(0.18f);

            var grain = Add<FilmGrain>(p);
            grain.type.Override(FilmGrainLookup.Medium3);
            grain.intensity.Override(0.4f);

            var bloom = Add<Bloom>(p);
            bloom.intensity.Override(0.55f);
            bloom.scatter.Override(0.8f);
            bloom.threshold.Override(0.6f);

            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
            return p;
        }

        // HDRP: positive intensity = barrel (edges bow outward), the look of a wide action-cam lens
        public const float BarrelIntensity = 0.32f;

        static T Add<T>(VolumeProfile p) where T : VolumeComponent
        {
            var c = p.Add<T>(true);
            c.name = typeof(T).Name;
            c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(c, p);
            return c;
        }
    }
}
