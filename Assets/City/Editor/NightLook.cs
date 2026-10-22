using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace LifeInDowntown.City
{
    // Look pass L2 + L8 (docs/DESIGN.md 3A): night HDRI sky, fixed night exposure, volumetric fog,
    // and the Cyberpunk-style post stack. Builds Assets/City/Look/NightRain.asset and points the
    // scene's global volume and static lighting sky at it. Called by CityGenerator.Generate.
    public static class NightLook
    {
        const string ProfilePath = "Assets/City/Look/NightRain.asset";
        const string SkyPath = "Assets/City/Sky/kloppenheim_07_puresky_4k.hdr";

        // tuned from ShotTool renders; see DESIGN.md 3A for the intent
        // pass 1 (EV 2.5, sky -1) left buildings as black silhouettes; pass 2 brightens both
        public const float ExposureEV = 1.5f;
        const float SkyExposureEV = 0.5f;
        const float FogMeanFreePath = 220f;

        public static void Apply()
        {
            var profile = BuildProfile();

            // the template's day volume is replaced by ours
            var templateVol = GameObject.Find("Sky and Fog Volume");
            if (templateVol != null) templateVol.SetActive(false);

            var volGo = GameObject.Find("NightVolume") ?? new GameObject("NightVolume");
            var vol = volGo.GetComponent<Volume>() ?? volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10f;
            vol.sharedProfile = profile;

            var staticSky = Object.FindFirstObjectByType<StaticLightingSky>();
            if (staticSky != null)
            {
                staticSky.profile = profile;
                staticSky.staticLightingSkyUniqueID = (int)SkyType.HDRI;
            }

            SetMoon();
        }

        static VolumeProfile BuildProfile()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath));
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath) != null)
                AssetDatabase.DeleteAsset(ProfilePath);
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, ProfilePath);

            var env = Add<VisualEnvironment>(p);
            env.skyType.Override((int)SkyType.HDRI);
            env.skyAmbientMode.Override(SkyAmbientMode.Dynamic);

            var sky = Add<HDRISky>(p);
            sky.hdriSky.Override(LoadCubemap(SkyPath));
            sky.exposure.Override(SkyExposureEV);
            sky.rotation.Override(0f);

            var exp = Add<Exposure>(p);
            exp.mode.Override(ExposureMode.Fixed);
            exp.fixedExposure.Override(ExposureEV);

            var fog = Add<Fog>(p);
            fog.enabled.Override(true);
            fog.meanFreePath.Override(FogMeanFreePath);
            fog.baseHeight.Override(0f);
            fog.maximumHeight.Override(90f);
            fog.enableVolumetricFog.Override(true);
            fog.albedo.Override(new Color(0.72f, 0.76f, 0.85f));
            fog.anisotropy.Override(0.6f);
            fog.depthExtent.Override(160f);

            var ao = Add<ScreenSpaceAmbientOcclusion>(p);
            ao.intensity.Override(1.2f);

            var bloom = Add<Bloom>(p);
            bloom.threshold.Override(0.8f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.75f);

            var tone = Add<Tonemapping>(p);
            tone.mode.Override(TonemappingMode.ACES);

            var grade = Add<ColorAdjustments>(p);
            grade.contrast.Override(18f);
            grade.saturation.Override(12f);

            // teal shadows, warm-magenta highlights
            var split = Add<SplitToning>(p);
            split.shadows.Override(new Color(0.30f, 0.42f, 0.62f));   // pass 1's teal read as green
            split.highlights.Override(new Color(0.85f, 0.40f, 0.60f));
            split.balance.Override(-10f);

            var grain = Add<FilmGrain>(p);
            grain.type.Override(FilmGrainLookup.Medium1);
            grain.intensity.Override(0.22f);

            var vig = Add<Vignette>(p);
            vig.intensity.Override(0.28f);
            vig.smoothness.Override(0.45f);

            var ca = Add<ChromaticAberration>(p);
            ca.intensity.Override(0.06f);

            EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
            return p;
        }

        static T Add<T>(VolumeProfile p) where T : VolumeComponent
        {
            var c = p.Add<T>(true);
            c.name = typeof(T).Name;
            c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            AssetDatabase.AddObjectToAsset(c, p);
            return c;
        }

        static Cubemap LoadCubemap(string path)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (imp.textureShape != TextureImporterShape.TextureCube)
            {
                imp.textureShape = TextureImporterShape.TextureCube;
                imp.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
                imp.maxTextureSize = 4096;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Cubemap>(path);
        }

        // the template's sun becomes a dim, cool moon
        static void SetMoon()
        {
            var sun = GameObject.Find("Sun") ?? GameObject.Find("Moon");
            if (sun == null) return;
            sun.name = "Moon";
            sun.transform.rotation = Quaternion.Euler(38f, 215f, 0f);
            var light = sun.GetComponent<Light>();
            light.lightUnit = LightUnit.Lux;
            light.intensity = 1.5f;
            light.useColorTemperature = true;
            light.colorTemperature = 8500f;
            light.color = Color.white;
        }
    }
}
