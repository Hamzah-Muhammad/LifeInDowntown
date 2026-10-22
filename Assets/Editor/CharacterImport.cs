using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace LifeInDowntown.EditorTools
{
    // Turns the downloaded Rocketbox files (Tools/assets/fetch_rocketbox.py) into NPC prefabs:
    // humanoid rigs, looping in-place clips, one animator controller per gender, HDRP materials
    // for body / head / hair-and-lashes (alpha-clipped), and Assets/Characters/Rocketbox/Prefabs/<Avatar>.prefab.
    public static class CharacterImport
    {
        public const string Root = "Assets/Characters/Rocketbox";
        const string PrefabDir = Root + "/Prefabs";
        const string AnimDir = Root + "/Animations";

        // clip file per animator mode (Pedestrian.Mode order)
        static readonly string[] FemaleClips = { "f_walk_neutral_01", "f_walk_slow_01", "f_idle_neutral_01", "f_cell_phone_textmessage",
            "m_crouch_idle", "f_sit_chair_idle_neutral_01", "f_sit_chair_idle_neutral_01", "m_idle_drunk_01", "m_idle_cough_01",
            "m_idle_shake_arms_01", "m_idle_nervous_03", "f_idle_look_around_01", "m_gestic_talk_cool", "m_gestic_listen_nervous_01",
            "f_idle_waiting_01" };   // no female file for some street poses: the humanoid rig retargets the male one
        static readonly string[] MaleClips = { "m_walk_neutral_01", "m_walk_slow_01", "m_idle_neutral_01", "m_cell_phone_talk_01",
            "m_crouch_idle", "m_sit_chair_idle_neutral_01", "m_sit_chair_idle_look_around", "m_idle_drunk_01", "m_idle_cough_01",
            "m_idle_shake_arms_01", "m_idle_nervous_03", "m_idle_look_around_01", "m_gestic_talk_cool", "m_gestic_listen_nervous_01",
            "m_idle_waiting_01" };

        [MenuItem("Life In Downtown/Import Rocketbox Characters")]
        public static void ImportAll()
        {
            foreach (var f in Directory.GetFiles(AnimDir, "*.fbx")) SetupAnimationImporter(f.Replace('\\', '/'));
            var female = BuildController("Female", FemaleClips);
            var male = BuildController("Male", MaleClips);
            Directory.CreateDirectory(PrefabDir);
            int built = 0;
            foreach (var dir in Directory.GetDirectories(Root))
            {
                string av = Path.GetFileName(dir);
                if (!av.Contains("_Adult_") && !av.StartsWith("Police_")) continue;
                if (BuildPrefab(av, av.StartsWith("Female") ? female : male)) built++;
            }
            PistolImport.Extract();
            AssetDatabase.SaveAssets();
            Debug.Log($"NPC_IMPORT_OK prefabs={built}");
        }

        public static void ImportFromCommandLine() => ImportAll();

        static void SetupAnimationImporter(string path)
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            bool changed = imp.animationType != ModelImporterAnimationType.Human ||
                           imp.materialImportMode != ModelImporterMaterialImportMode.None;
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            var clips = imp.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.loopTime = true;
                // bake root motion into the pose, anchored to the body (centre of mass / feet), not to the
                // clip's original root: keeping the original root put bodies metres from their GameObject
                // XZ is NOT baked: the walk files carry their forward travel in the root (0.7 m by mid-cycle
                // when baked); with root motion off that travel is dropped and Pedestrian.cs moves the body
                c.lockRootRotation = c.lockRootHeightY = true;
                c.lockRootPositionXZ = false;
                c.keepOriginalOrientation = c.keepOriginalPositionY = c.keepOriginalPositionXZ = false;
                c.heightFromFeet = true;
            }
            imp.clipAnimations = clips;
            imp.SaveAndReimport();
            if (changed) Debug.Log($"NPC_IMPORT clip {Path.GetFileName(path)}: {clips.Length} clip(s)");
        }

        static AnimationClip Clip(string file)
        {
            return AssetDatabase.LoadAllAssetsAtPath($"{AnimDir}/{file}.fbx").OfType<AnimationClip>()
                                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        }

        static AnimatorController BuildController(string gender, string[] clipFiles)
        {
            string path = $"{Root}/NPC_{gender}.controller";
            if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            ctrl.AddParameter("Mode", AnimatorControllerParameterType.Int);
            var sm = ctrl.layers[0].stateMachine;
            string[] names = System.Enum.GetNames(typeof(LifeInDowntown.NPC.Pedestrian.Mode));
            for (int i = 0; i < names.Length; i++)
            {
                var clip = Clip(clipFiles[i]);
                if (clip == null) { Debug.LogWarning($"NPC_IMPORT_WARN missing clip {clipFiles[i]}"); continue; }
                var st = sm.AddState(names[i]);
                st.motion = clip;
                if (i == 0) sm.defaultState = st;
                var t = sm.AddAnyStateTransition(st);
                t.AddCondition(AnimatorConditionMode.Equals, i, "Mode");
                t.canTransitionToSelf = false;
                t.duration = 0.25f;
                t.hasExitTime = false;
            }
            return ctrl;
        }

        static bool BuildPrefab(string av, AnimatorController ctrl)
        {
            string fbx = $"{Root}/{av}/{av}.fbx";
            var imp = (ModelImporter)AssetImporter.GetAtPath(fbx);
            if (imp == null) { Debug.LogWarning($"NPC_IMPORT_WARN no fbx {fbx}"); return false; }
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.importAnimation = false;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;   // keeps the slot names
            imp.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            // Rocketbox is authored in centimetres: fix the scale once so people are ~1.6-1.9 m tall
            float h = Height(inst);
            if (h > 10f) { imp.globalScale = 0.01f; imp.SaveAndReimport(); Object.DestroyImmediate(inst); inst = (GameObject)PrefabUtility.InstantiatePrefab(model); h = Height(inst); }

            var mats = Materials(av);
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var slots = smr.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    string n = slots[i] != null ? slots[i].name.ToLowerInvariant() : "";
                    // professions carry extra texture sets (the officer's equipment belt, his pistol): match them by slot name
                    var extra = n.Contains("body") || n.Contains("head") || n.Contains("opacity") ? null : Tex(dir: $"{Root}/{av}", suffix: n + "_color", normal: false, warn: false);
                    slots[i] = extra != null ? Lit($"{Root}/{av}/{n}.mat", extra, Tex($"{Root}/{av}", n + "_normal", true, warn: false), 0.45f, false)
                             : n.Contains("opacity") ? mats.opacity : n.Contains("head") ? mats.head : mats.body;
                }
                smr.sharedMaterials = slots;
                smr.updateWhenOffscreen = false;
            }

            var animator = inst.GetComponent<Animator>() ?? inst.AddComponent<Animator>();
            animator.runtimeAnimatorController = ctrl;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullCompletely;

            var root = new GameObject($"NPC_{av}");
            inst.transform.SetParent(root.transform, false);
            var cap = root.AddComponent<CapsuleCollider>();
            cap.height = Mathf.Clamp(h, 1.5f, 2f); cap.radius = 0.25f; cap.center = Vector3.up * (cap.height * 0.5f);
            root.AddComponent<LifeInDowntown.NPC.Pedestrian>();

            string prefabPath = $"{PrefabDir}/{av}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"NPC_IMPORT prefab {av} height={h:F2} m");
            return true;
        }

        static float Height(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return 0f;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b.size.y;
        }

        struct AvatarMats { public Material body, head, opacity; }

        static AvatarMats Materials(string av)
        {
            string dir = $"{Root}/{av}";
            var m = new AvatarMats
            {
                body = Lit($"{dir}/{av}_body.mat", Tex(dir, "_body_color", false), Tex(dir, "_body_normal", true), 0.32f, false),
                head = Lit($"{dir}/{av}_head.mat", Tex(dir, "_head_color", false), Tex(dir, "_head_normal", true), 0.4f, false),
            };
            // some Rocketbox males ship no hair/lash texture; their opacity slot (if any) uses the head material
            var opacityTex = Tex(dir, "_opacity_color", false, warn: false);
            m.opacity = opacityTex != null ? Lit($"{dir}/{av}_opacity.mat", opacityTex, null, 0.35f, true) : m.head;
            return m;
        }

        static Texture2D Tex(string dir, string suffix, bool normal, bool warn = true)
        {
            foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (!Path.GetFileNameWithoutExtension(p).EndsWith(suffix)) continue;
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (ti.textureType != type || ti.maxTextureSize != 2048)
                {
                    ti.textureType = type;
                    ti.maxTextureSize = 2048;
                    ti.alphaIsTransparency = !normal && p.EndsWith(".png");
                    ti.SaveAndReimport();
                }
                return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }
            if (warn) Debug.LogWarning($"NPC_IMPORT_WARN missing texture {dir}/*{suffix}");
            return null;
        }

        static Material Lit(string path, Texture2D color, Texture2D normal, float smoothness, bool alphaClip)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("HDRP/Lit")); AssetDatabase.CreateAsset(mat, path); }
            mat.SetTexture("_BaseColorMap", color);
            mat.SetColor("_BaseColor", Color.white);
            if (normal != null) mat.SetTexture("_NormalMap", normal);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", 0f);
            if (alphaClip)
            {
                mat.SetFloat("_AlphaCutoffEnable", 1f);
                mat.SetFloat("_AlphaCutoff", 0.45f);
                mat.SetFloat("_DoubleSidedEnable", 1f);
            }
            UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(mat);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
