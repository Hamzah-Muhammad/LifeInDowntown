using System.IO;
using LifeInDowntown.City;
using LifeInDowntown.Game;
using LifeInDowntown.Player;
using UnityEngine.UIElements;
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

            // main menu drift: from just behind the spawn, 60 m up Yonge, a little above head height
            var menuCam = camGo.AddComponent<MenuCamera>();
            var fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            menuCam.from = spawn + Vector3.up * 2.1f - fwd * 6f;
            menuCam.to = spawn + Vector3.up * 2.1f + fwd * 60f;
            menuCam.enabled = false;

            SetupMenu(player, rig, menuCam);
            Debug.Log($"PLAYER_OK spawn={spawn} yaw={yaw:F0}");
        }

        const string UxmlPath = "Assets/UI/Menu.uxml";
        const string PhoneUxmlPath = "Assets/UI/Phone.uxml";
        const string ThemePath = "Assets/UI/LIDTheme.tss";
        const string PanelPath = "Assets/UI/MenuPanelSettings.asset";

        // the Game object: UI Toolkit menu document + flow controller, plus an EventSystem for UI input
        static void SetupMenu(BodycamPlayer player, BodycamRig rig, MenuCamera menuCam)
        {
            foreach (var n in new[] { "Game", "EventSystem", "Phone", "Map" })
            {
                var old = GameObject.Find(n);
                if (old != null) Object.DestroyImmediate(old);
            }
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, PanelPath);
            }
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 1f;   // scale by height so the left-side menu fits any aspect
            EditorUtility.SetDirty(panel);

            var game = new GameObject("Game");
            var doc = game.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            doc.sortingOrder = 10;   // menus draw over the phone HUD
            var menu = game.AddComponent<MenuController>();
            var flow = game.AddComponent<GameFlow>();
            flow.player = player; flow.rig = rig; flow.menuCamera = menuCam; flow.menu = menu;
            menu.flow = flow;

            // phone + missions (docs/DESIGN.md 3D)
            var phoneGo = new GameObject("Phone");
            var phoneDoc = phoneGo.AddComponent<UIDocument>();
            phoneDoc.panelSettings = panel;
            phoneDoc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(PhoneUxmlPath);
            phoneDoc.sortingOrder = 0;
            var missions = phoneGo.AddComponent<LifeInDowntown.Phone.MissionManager>();
            missions.player = player.transform;
            var phone = phoneGo.AddComponent<LifeInDowntown.Phone.PhoneController>();
            phone.missions = missions;
            phone.player = player;
            phone.cam = rig.GetComponent<Camera>();

            // GTA-style minimap + full map (docs/DESIGN.md 3F)
            var mapTex = (TextureImporter)AssetImporter.GetAtPath("Assets/Resources/City/map.png");
            if (mapTex != null && (mapTex.maxTextureSize < 4096 || mapTex.mipmapEnabled))
            {
                mapTex.maxTextureSize = 4096;   // 2735 x 2690: keep full detail
                mapTex.mipmapEnabled = false;
                mapTex.textureCompression = TextureImporterCompression.CompressedHQ;
                mapTex.SaveAndReimport();
            }
            var mapGo = new GameObject("Map");
            var mapDoc = mapGo.AddComponent<UIDocument>();
            mapDoc.panelSettings = panel;
            mapDoc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/Map.uxml");
            mapDoc.sortingOrder = 5;   // over the phone HUD, under the menus
            var map = mapGo.AddComponent<LifeInDowntown.Map.MapController>();
            map.player = player; map.rig = rig; map.missions = missions;

            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
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
