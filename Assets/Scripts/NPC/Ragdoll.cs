using System.Collections.Generic;
using UnityEngine;

namespace LifeInDowntown.NPC
{
    // Turns a Rocketbox humanoid into a ragdoll at the moment it's hit (docs/DESIGN.md 3O.6): rigidbodies and
    // capsules on the main bones from the Animator's humanoid map, character joints to their parents, the
    // animator off, and a push from the bullet at the hit point.
    public static class Ragdoll
    {
        static readonly (HumanBodyBones bone, HumanBodyBones parent, float mass, float radius)[] Bones =
        {
            (HumanBodyBones.Hips, HumanBodyBones.LastBone, 12f, 0.13f),
            (HumanBodyBones.Spine, HumanBodyBones.Hips, 8f, 0.13f),
            (HumanBodyBones.Chest, HumanBodyBones.Spine, 8f, 0.14f),
            (HumanBodyBones.Head, HumanBodyBones.Chest, 5f, 0.1f),
            (HumanBodyBones.LeftUpperArm, HumanBodyBones.Chest, 2.5f, 0.05f),
            (HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftUpperArm, 1.8f, 0.045f),
            (HumanBodyBones.RightUpperArm, HumanBodyBones.Chest, 2.5f, 0.05f),
            (HumanBodyBones.RightLowerArm, HumanBodyBones.RightUpperArm, 1.8f, 0.045f),
            (HumanBodyBones.LeftUpperLeg, HumanBodyBones.Hips, 7f, 0.075f),
            (HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftUpperLeg, 4f, 0.06f),
            (HumanBodyBones.RightUpperLeg, HumanBodyBones.Hips, 7f, 0.075f),
            (HumanBodyBones.RightLowerLeg, HumanBodyBones.RightUpperLeg, 4f, 0.06f),
        };

        static readonly Dictionary<HumanBodyBones, HumanBodyBones> ChildOf = new Dictionary<HumanBodyBones, HumanBodyBones>
        {
            { HumanBodyBones.Hips, HumanBodyBones.Spine }, { HumanBodyBones.Spine, HumanBodyBones.Chest }, { HumanBodyBones.Chest, HumanBodyBones.Neck },
            { HumanBodyBones.Head, HumanBodyBones.LastBone },
            { HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm }, { HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand },
            { HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm }, { HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand },
            { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg }, { HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot },
            { HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg }, { HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot },
        };

        // returns the number of bodies made (0 = not a humanoid)
        public static int Go(GameObject who, Vector3 hitPoint, Vector3 push)
        {
            var anim = who.GetComponentInChildren<Animator>();
            if (anim == null || !anim.isHuman) return 0;
            var already = who.GetComponentsInChildren<Rigidbody>();
            if (!anim.enabled && already.Length > 0)   // knocked down already (a punch, DESIGN 3P.3): just push
            {
                Push(already, hitPoint, push);
                return already.Length;
            }
            var made = new Dictionary<HumanBodyBones, Rigidbody>();
            foreach (var (bone, parent, mass, radius) in Bones)
            {
                var t = anim.GetBoneTransform(bone);
                if (t == null) continue;
                var rb = t.gameObject.AddComponent<Rigidbody>();
                rb.mass = mass; rb.linearDamping = 0.1f; rb.angularDamping = 0.6f;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                var child = ChildOf.TryGetValue(bone, out var c) && c != HumanBodyBones.LastBone ? anim.GetBoneTransform(c) : null;
                var col = t.gameObject.AddComponent<CapsuleCollider>();
                if (child != null)
                {
                    Vector3 local = t.InverseTransformPoint(child.position);
                    col.direction = Axis(local);
                    col.center = local / 2f;
                    col.height = local.magnitude + radius;
                }
                else col.center = Vector3.zero;
                col.radius = radius;
                if (parent != HumanBodyBones.LastBone && made.TryGetValue(parent, out var prb))
                {
                    var j = t.gameObject.AddComponent<CharacterJoint>();
                    j.connectedBody = prb;
                    j.lowTwistLimit = new SoftJointLimit { limit = -30f }; j.highTwistLimit = new SoftJointLimit { limit = 30f };
                    j.swing1Limit = new SoftJointLimit { limit = 40f }; j.swing2Limit = new SoftJointLimit { limit = 30f };
                    j.enableProjection = true;
                }
                made[bone] = rb;
            }
            anim.enabled = false;
            foreach (var col in who.GetComponents<Collider>()) col.enabled = false;   // the walking capsule
            Push(new List<Rigidbody>(made.Values).ToArray(), hitPoint, push);
            return made.Count;
        }

        static void Push(Rigidbody[] bodies, Vector3 hitPoint, Vector3 push)
        {
            Rigidbody nearest = null; float best = float.MaxValue;
            foreach (var rb in bodies) { float d = (rb.position - hitPoint).sqrMagnitude; if (d < best) { best = d; nearest = rb; } }
            if (nearest != null) nearest.AddForceAtPosition(push, hitPoint, ForceMode.Impulse);
        }

        // back on their feet after a knockdown: the ragdoll parts come off, the body stands where the hips fell
        // (on the ground below them, facing the way it lay), and the animator takes over again
        public static void Recover(GameObject who)
        {
            var anim = who.GetComponentInChildren<Animator>();
            if (anim == null) return;
            var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            Vector3 at = hips != null ? hips.position : who.transform.position;
            float ground = who.transform.position.y;
            if (Physics.Raycast(at + Vector3.up * 0.5f, Vector3.down, out var hit, 3f, ~0, QueryTriggerInteraction.Ignore) && hit.collider.attachedRigidbody == null)
                ground = hit.point.y;
            foreach (var j in who.GetComponentsInChildren<CharacterJoint>()) Kill(j);
            foreach (var rb in who.GetComponentsInChildren<Rigidbody>())
            {
                var col = rb.GetComponent<CapsuleCollider>();
                if (col != null) Kill(col);
                Kill(rb);
            }
            Vector3 fwd = Vector3.ProjectOnPlane(who.transform.forward, Vector3.up);
            who.transform.SetPositionAndRotation(new Vector3(at.x, ground, at.z), fwd.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(fwd) : who.transform.rotation);
            foreach (var col in who.GetComponents<Collider>()) col.enabled = true;
            anim.enabled = true;
        }

        // immediate even in play: a deferred Destroy of a Rigidbody is refused while its joint still exists this frame
        static void Kill(Object o) => Object.DestroyImmediate(o);

        static int Axis(Vector3 v)
        {
            Vector3 a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            return a.x > a.y && a.x > a.z ? 0 : a.y > a.z ? 1 : 2;
        }
    }
}
