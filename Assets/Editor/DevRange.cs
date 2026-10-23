using System.IO;
using LifeInDowntown.Items;
using LifeInDowntown.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace LifeInDowntown.EditorTools
{
    // The development range (docs/DESIGN.md 3Q): a bare grey studio where models and poses are built and judged
    // before they go into the city. It holds the game's real player (PlayerSetup.BuildPlayer: Tarik's headless
    // body, the eye camera, the weapon holder), a shooting lane with targets at 5 / 10 / 20 m, the pistol on a
    // plinth, Tarik's E46 and a line-up of people under even light. Whatever looks right here is what the game
    // shows, because it is the same code.
    //   Build:  -executeMethod LifeInDowntown.EditorTools.DevRange.Build    (saves Assets/DevRange/DevRange.unity)
    //   Render: -executeMethod LifeInDowntown.EditorTools.DevRange.Render   (builds, then Screenshots/range_*.png;
    //           without -nographics; -shots a,b renders only those)
    public static class DevRange
    {
        public const string Dir = "Assets/DevRange", ScenePath = Dir + "/DevRange.unity";
        const int W = 1600, H = 900;
        static readonly Vector3 GunPad = new Vector3(-2.2f, 0f, 1.6f), CarPad = new Vector3(6.5f, 0f, 3.5f), Lineup = new Vector3(-6f, 0f, 3.5f);
        static readonly string[] People = { NpcSetup.TarikAvatar, NpcSetup.TyAvatar, "Female_Adult_04", "Male_Adult_04", "Police_Male_01" };

        public static void Build()
        {
            Directory.CreateDirectory(Dir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var grey = Mat("Grey", new Color(0.42f, 0.43f, 0.45f));
            var white = Mat("White", new Color(0.85f, 0.85f, 0.83f));
            var red = Mat("Red", new Color(0.7f, 0.08f, 0.06f));
            var dark = Mat("Dark", new Color(0.12f, 0.12f, 0.13f));

            // studio: floor, backdrop down the lane, key + fill light, fixed exposure, no fog
            Box("Floor", new Vector3(0f, -0.05f, 8f), new Vector3(30f, 0.1f, 34f), grey);
            Box("Backdrop", new Vector3(0f, 4f, 25f), new Vector3(30f, 8f, 0.2f), grey);
            Light(new Vector3(40f, -35f, 0f), 30000f, true);    // key: the one shadowed light, like the city's sun
            Light(new Vector3(25f, 150f, 0f), 12000f, false);   // fill from the front: a second shadowed light grazing the sleeves broke into acne
            var vol = new GameObject("RangeVolume").AddComponent<Volume>();
            vol.isGlobal = true; vol.priority = 100f;
            vol.sharedProfile = Profile();

            // the lane: a target board on a stand at 5, 10 and 20 m, rings on the face
            foreach (float z in new[] { 5f, 10f, 20f })
            {
                var t = new GameObject($"Target_{z:F0}m").transform;
                t.position = new Vector3(0f, 0f, z);
                Box("Stand", new Vector3(0f, 0.6f, z + 0.05f), new Vector3(0.06f, 1.2f, 0.06f), dark).transform.SetParent(t, true);
                Box("Board", new Vector3(0f, 1.45f, z), new Vector3(0.6f, 0.8f, 0.03f), white).transform.SetParent(t, true);
                for (int r = 0; r < 3; r++)
                {
                    var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Object.DestroyImmediate(ring.GetComponent<Collider>());
                    ring.name = $"Ring{r}";
                    float d = 0.45f - r * 0.15f;
                    ring.transform.SetPositionAndRotation(new Vector3(0f, 1.45f, z - 0.017f - r * 0.001f), Quaternion.Euler(90f, 0f, 0f));
                    ring.transform.localScale = new Vector3(d, 0.001f, d);
                    ring.GetComponent<Renderer>().sharedMaterial = r % 2 == 0 ? red : white;
                    ring.transform.SetParent(t, true);
                }
            }

            // the player, exactly as the game builds him, at the firing line facing down the lane
            PlayerSetup.BuildPlayer(Vector3.zero, 0f);

            // the pistol alone on a plinth, the E46, the line-up
            Box("GunPlinth", GunPad + Vector3.up * 0.5f, new Vector3(0.5f, 1f, 0.5f), dark);
            var gun = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PistolImport.PrefabPath));
            gun.name = "PistolOnPlinth";
            gun.transform.SetPositionAndRotation(GunPad + Vector3.up * 1.1f, Quaternion.Euler(0f, 90f, 0f));
            var carPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Vehicles/Car_E46_Tarik.prefab");
            if (carPrefab != null)
            {
                var car = (GameObject)PrefabUtility.InstantiatePrefab(carPrefab);
                car.transform.SetPositionAndRotation(CarPad, Quaternion.Euler(0f, -30f, 0f));
                foreach (var rb in car.GetComponentsInChildren<Rigidbody>()) rb.isKinematic = true;
            }
            int n = 0;
            foreach (var who in People)
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>($"{CharacterImport.Root}/Prefabs/{who}.prefab");
                if (p == null) continue;
                var go = (GameObject)PrefabUtility.InstantiatePrefab(p);
                go.name = $"Lineup_{who}";
                go.transform.SetPositionAndRotation(Lineup + Vector3.left * (n++ * 0.9f), Quaternion.Euler(0f, 180f, 0f));
                var ped = go.GetComponent<LifeInDowntown.NPC.Pedestrian>(); if (ped != null) Object.DestroyImmediate(ped);
            }

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            Debug.Log($"RANGE_OK {ScenePath} people={n}");
        }

        public static void Render()
        {
            Build();
            var only = ShotFilter();
            bool Want(string s) => only == null || System.Array.IndexOf(only, s) >= 0;
            Directory.CreateDirectory("Screenshots");
            var wh = Object.FindFirstObjectByType<WeaponHolder>();
            var eye = GameObject.Find("BodycamCamera").transform;
            var body = Object.FindFirstObjectByType<TarikBody>();
            AnimationMode.StartAnimationMode();
            foreach (var a in Object.FindObjectsByType<Animator>(FindObjectsSortMode.None)) Idle(a);
            // edit mode has no player loop: without this a skinned mesh keeps the pose of its first render, so the
            // arms drawn lag one pose behind the bones the IK just set
            foreach (var smr in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None)) { smr.forceMatrixRecalculationPerRender = true; smr.updateWhenOffscreen = true; }

            var camGo = new GameObject("RangeCam");
            var cam = camGo.AddComponent<Camera>(); camGo.AddComponent<HDAdditionalCameraData>();
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;

            void Eye(string name, float fov)
            {
                if (!Want(name)) return;
                camGo.transform.SetPositionAndRotation(eye.position, eye.rotation);
                cam.fieldOfView = fov; cam.nearClipPlane = eye.GetComponent<Camera>().nearClipPlane;
                Grab(cam, rt, name);
            }
            void Look(string name, Vector3 from, Vector3 at, float fov)
            {
                if (!Want(name)) return;
                camGo.transform.position = from; camGo.transform.LookAt(at);
                cam.fieldOfView = fov; cam.nearClipPlane = 0.02f;
                Grab(cam, rt, name);
            }

            wh.PoseForShot(false);
            Report("held", wh, body);
            Eye("range_eye_held", PlayerSetup.VerticalFov);
            wh.PoseForShot(true);
            Report("aim", wh, body);
            Eye("range_eye_aim", wh.aimFov);
            // the same view a moment after a 25-point hit: the red edge and the glitch (DESIGN 3P.2)
            var fb = eye.GetComponent<HurtFeedback>();
            if (fb != null)
            {
                var stats = new GameObject("RangeStats").AddComponent<PlayerStats>();
                fb.Init();   // starts listening (no OnEnable in edit mode)
                stats.TakeDamage(25f, Vector3.forward * 10f);
                fb.Step(0.05f);
                Eye("range_eye_hurt", wh.aimFov);
                fb.Step(3f);
                LifeInDowntown.Game.GameState.Current.health = 100f;
            }
            Vector3 hands = (body.HandPosition(true) + body.HandPosition(false)) / 2f;
            Look("range_side", new Vector3(2.2f, 1.5f, 0.5f), new Vector3(0f, 1.3f, 0.35f), 40f);
            Look("range_front34", new Vector3(-1.4f, 1.65f, 2.2f), new Vector3(0f, 1.35f, 0.3f), 40f);
            Look("range_hands_right", hands + new Vector3(0.45f, 0.03f, 0.05f), hands, 30f);
            Look("range_hands_left", hands + new Vector3(-0.45f, 0.03f, 0.05f), hands, 30f);
            Look("range_hands_under", hands + new Vector3(-0.15f, -0.35f, 0.25f), hands, 35f);
            wh.PoseReloadAt(0.95f);
            Eye("range_eye_reload", PlayerSetup.VerticalFov);
            wh.PoseReloadAt(1.3f);
            Eye("range_eye_rack", PlayerSetup.VerticalFov);
            wh.PoseForShot(true);

            var g = GameObject.Find("PistolOnPlinth").transform.position;
            for (int a = 0; a < 360; a += 90)
                Look($"range_pistol_{a}", g + Quaternion.Euler(15f, a, 0f) * (Vector3.back * 0.55f), g, 30f);
            Look("range_car_front34", CarPad + new Vector3(-3.5f, 1.6f, -5.5f), CarPad + Vector3.up * 0.7f, 45f);
            Look("range_car_rear34", CarPad + new Vector3(4.5f, 1.6f, 5f), CarPad + Vector3.up * 0.7f, 45f);
            Look("range_lineup", Lineup + new Vector3(-1.8f, 1.5f, -4.5f), Lineup + new Vector3(-1.8f, 1f, 0f), 40f);
            Look("range_lane", new Vector3(1.5f, 1.7f, -2.5f), new Vector3(0f, 1.2f, 8f), 55f);

            AnimationMode.StopAnimationMode();
            rt.Release();
        }

        // how far each hand bone ended from its grip point, and how much of the arm the reach uses
        static void Report(string pose, WeaponHolder wh, TarikBody body)
        {
            var gr = wh.transform.Find("Pistol(Clone)/GripR") ?? FindDeep(wh.transform, "GripR");
            var gl = FindDeep(wh.transform, "GripL");
            float offR = gr != null ? Vector3.Distance(body.HandPosition(true), gr.position) : -1f;
            float offL = gl != null ? Vector3.Distance(body.HandPosition(false), gl.position) : -1f;
            Debug.Log($"RANGE {pose}: right hand off grip {offR:F3} m, left {offL:F3} m; reach R {Vector3.Distance(body.Shoulder(true), body.HandPosition(true)):F2} / {body.ArmReach(true):F2} m, " +
                      $"L {Vector3.Distance(body.Shoulder(false), body.HandPosition(false)):F2} / {body.ArmReach(false):F2} m");
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = FindDeep(c, name); if (f != null) return f; }
            return null;
        }

        // edit mode: stand a humanoid on its idle clip (in play the animator does this every frame)
        static void Idle(Animator anim)
        {
            if (anim.runtimeAnimatorController is not UnityEditor.Animations.AnimatorController ctrl) return;
            var root = anim.transform.parent != null ? anim.transform.parent : anim.transform;
            var p = root.position; var r = root.rotation;
            foreach (var st in ctrl.layers[0].stateMachine.states)
                if (st.state.name == "Idle" && st.state.motion is AnimationClip clip)
                    AnimationMode.SampleAnimationClip(anim.gameObject, clip, 0.5f);
            root.SetPositionAndRotation(p, r);
            if (root != anim.transform) { anim.transform.localPosition = Vector3.zero; anim.transform.localRotation = Quaternion.identity; }
        }

        static string[] ShotFilter()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-shots");
            return i >= 0 && i + 1 < args.Length ? args[i + 1].Split(',') : null;
        }

        static Material Mat(string name, Color c)
        {
            string path = $"{Dir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("HDRP/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", 0.2f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static VolumeProfile Profile()
        {
            string path = $"{Dir}/RangeLook.asset";
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (prof != null) AssetDatabase.DeleteAsset(path);
            prof = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(prof, path);
            var exp = prof.Add<Exposure>(true); exp.mode.Override(ExposureMode.Fixed); exp.fixedExposure.Override(13.2f);
            var fog = prof.Add<Fog>(true); fog.enabled.Override(false);
            var contact = prof.Add<ContactShadows>(true); contact.enable.Override(false);
            var micro = prof.Add<MicroShadowing>(true); micro.enable.Override(false);
            foreach (var c in prof.components) AssetDatabase.AddObjectToAsset(c, prof);
            AssetDatabase.SaveAssets();
            return prof;
        }

        static GameObject Box(string name, Vector3 pos, Vector3 size, Material m)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
            b.name = name; b.transform.position = pos; b.transform.localScale = size;
            b.GetComponent<Renderer>().sharedMaterial = m;
            return b;
        }

        static void Light(Vector3 euler, float lux, bool shadows)
        {
            var go = new GameObject("RangeLight");
            go.transform.rotation = Quaternion.Euler(euler);
            var l = go.AddComponent<Light>(); go.AddComponent<HDAdditionalLightData>();
            l.type = LightType.Directional; l.lightUnit = LightUnit.Lux; l.intensity = lux; l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        }

        static void Grab(Camera cam, RenderTexture rt, string name)
        {
            for (int i = 0; i < 16; i++)
            {
                var req = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req); else cam.Render();
            }
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine("Screenshots", name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log($"RANGE_SHOT {name}");
        }
    }
}
