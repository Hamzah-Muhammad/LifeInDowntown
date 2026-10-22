using UnityEditor;
using UnityEngine;

namespace LifeInDowntown.EditorTools
{
    // Read-only: prints where an NPC prefab's renderers actually are relative to its root
    // (bounds, local bounds, root bone), to debug culling/offset problems.
    public static class NpcDiagnostic
    {
        public static void Run()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterImport.Root + "/Prefabs/Female_Adult_09.prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.transform.position = new Vector3(10f, 0f, 20f);
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
                Debug.Log($"NPC_DIAG {smr.name}: bounds c={smr.bounds.center} s={smr.bounds.size} local c={smr.localBounds.center} s={smr.localBounds.size} " +
                          $"rootBone={(smr.rootBone ? smr.rootBone.name : "none")} rootBonePos={(smr.rootBone ? smr.rootBone.position : Vector3.zero)} " +
                          $"mats={string.Join(",", System.Array.ConvertAll(smr.sharedMaterials, m => m ? m.name : "null"))}");
            var anim = go.GetComponentInChildren<Animator>();
            // play each mode and measure the hips relative to the NPC root: should stay ~0.9 m up, <0.4 m sideways
            // Animator.Update does not evaluate outside play mode, so sample each clip with AnimationMode
            var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            var bindHips = hips.position;
            var ctrl = (UnityEditor.Animations.AnimatorController)anim.runtimeAnimatorController;
            AnimationMode.StartAnimationMode();
            foreach (var st in ctrl.layers[0].stateMachine.states)
            {
                var clip = (AnimationClip)st.state.motion;
                var track = new System.Text.StringBuilder();
                foreach (float f in new[] { 0f, 0.25f, 0.5f, 0.75f, 0.99f })
                {
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(anim.gameObject, clip, clip.length * f);
                    AnimationMode.EndSampling();
                    track.Append($" t{f:0.00}:z={(hips.position - anim.transform.position).z:F2}");
                }
                Debug.Log($"NPC_DIAG track state={st.state.name} len={clip.length:F2}s avgSpeed={clip.averageSpeed}{track}");
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(anim.gameObject, clip, clip.length * 0.5f);
                AnimationMode.EndSampling();
                // relative to the animated model: the sampler applies root motion to that transform,
                // which play mode never does with applyRootMotion off
                // minus the clip's own travel (the sampler applies it; play mode drops it with root motion off)
                var off = hips.position - anim.transform.position - clip.averageSpeed * (clip.length * 0.5f);
                bool posed = (hips.position - bindHips).magnitude > 0.005f || Quaternion.Angle(hips.rotation, Quaternion.identity) > 0.5f;
                bool ok = new Vector2(off.x, off.z).magnitude < 0.4f && off.y > 0.7f && off.y < 1.2f;
                Debug.Log($"NPC_DIAG {(ok ? "PASS" : "FAIL")} state={st.state.name} clip={clip.name} hips offset={off} sampled={posed}");
            }
            AnimationMode.StopAnimationMode();
            Debug.Log($"NPC_DIAG animator avatar={(anim.avatar ? anim.avatar.name + " human=" + anim.avatar.isHuman + " valid=" + anim.avatar.isValid : "none")} " +
                      $"model localPos={anim.transform.localPosition} localRot={anim.transform.localEulerAngles} scale={anim.transform.lossyScale}");
            Object.DestroyImmediate(go);
            EditorApplication.Exit(0);
        }
    }
}
