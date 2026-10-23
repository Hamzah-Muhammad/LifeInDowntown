using UnityEngine;

namespace LifeInDowntown.Player
{
    // Tarik's own body (docs/DESIGN.md 3P.1): a Rocketbox humanoid at the player's feet, head and neck hidden
    // from the chest cam, legs on the walk/idle clips. The arms are posed after everything else each frame by
    // a two-bone IK solve (upper arm + forearm to a hand target, bending towards an elbow hint), the hands
    // turned to a grip frame worked out from their own finger bones, and the fingers curled. Targets come
    // from the weapon (grip points on the pistol) or the fists. Pure transform maths, so it poses the same in
    // the editor (renders, tests) as in play.
    [DefaultExecutionOrder(200)]   // after the rig has placed the chest cam and the weapon its gun
    public class TarikBody : MonoBehaviour
    {
        public BodycamPlayer player;
        public Animator anim;

        public Transform rightTarget, leftTarget;     // where the hands go (grip points), null = arm hangs
        public Transform rightHint, leftHint;         // which way the elbows bend
        [Range(0f, 1f)] public float rightWeight, leftWeight;
        [Range(0f, 1f)] public float rightCurl = 1f, leftCurl = 1f;   // closed round a 3 cm grip
        public bool rightPalmIn = true;               // palm faces -X of the grip frame (a pistol grip)

        Arm right, left;
        static readonly int ModeId = Animator.StringToHash("Mode");

        class Arm
        {
            public Transform upper, fore, hand;
            public Transform[][] fingers;
            public Transform[] thumb;
            public Quaternion[] thumbRest;
            public Quaternion handOffset;             // hand bone rotation in its own grip frame
            public Quaternion upperRest, foreRest, handRest;
            public Quaternion[][] fingerRest;
            public bool isRight;
        }

        public void Init()
        {
            if (anim == null) anim = GetComponentInChildren<Animator>();
            if (anim == null || !anim.isHuman || right != null) return;
            // the head is left alone: it sits above and behind the chest cam. Shrinking bones to hide it doesn't
            // work on Rocketbox rigs: the clavicles hang off the neck (arms collapse) and the jacket collar is
            // partly weighted to the head (it stretches into sheets across the lens)
            right = MakeArm(true); left = MakeArm(false);
        }

        Arm MakeArm(bool isRight)
        {
            HumanBodyBones U(string s) => (HumanBodyBones)System.Enum.Parse(typeof(HumanBodyBones), (isRight ? "Right" : "Left") + s);
            var a = new Arm
            {
                isRight = isRight,
                upper = anim.GetBoneTransform(U("UpperArm")), fore = anim.GetBoneTransform(U("LowerArm")), hand = anim.GetBoneTransform(U("Hand")),
            };
            string[] names = { "Index", "Middle", "Ring", "Little" };
            a.fingers = new Transform[names.Length][];
            a.fingerRest = new Quaternion[names.Length][];
            for (int f = 0; f < names.Length; f++)
            {
                a.fingers[f] = new[] { anim.GetBoneTransform(U(names[f] + "Proximal")), anim.GetBoneTransform(U(names[f] + "Intermediate")), anim.GetBoneTransform(U(names[f] + "Distal")) };
                a.fingerRest[f] = System.Array.ConvertAll(a.fingers[f], t => t != null ? t.localRotation : Quaternion.identity);
            }
            a.thumb = new[] { anim.GetBoneTransform(U("ThumbProximal")), anim.GetBoneTransform(U("ThumbIntermediate")), anim.GetBoneTransform(U("ThumbDistal")) };
            a.thumbRest = System.Array.ConvertAll(a.thumb, t => t != null ? t.localRotation : Quaternion.identity);
            a.upperRest = a.upper.localRotation; a.foreRest = a.fore.localRotation; a.handRest = a.hand.localRotation;
            a.handOffset = Quaternion.Inverse(HandFrame(a)) * a.hand.rotation;
            return a;
        }

        // the hand's own frame: forward = wrist to knuckles, up = back of the hand
        static Quaternion HandFrame(Arm a)
        {
            var mid = a.fingers[1][0] != null ? a.fingers[1][0].position : a.hand.position + a.hand.forward * 0.08f;
            var ix = a.fingers[0][0] != null ? a.fingers[0][0].position : mid;
            var lt = a.fingers[3][0] != null ? a.fingers[3][0].position : mid;
            Vector3 fwd = (mid - a.hand.position).normalized;
            Vector3 across = (ix - lt).normalized;                                    // towards the thumb
            Vector3 palm = a.isRight ? Vector3.Cross(fwd, across) : Vector3.Cross(across, fwd);
            return Quaternion.LookRotation(fwd, -palm);
        }

        void LateUpdate()
        {
            if (player != null && anim != null && anim.isActiveAndEnabled)
                anim.SetInteger(ModeId, player.HorizontalSpeed > 0.3f ? 0 : 2);   // walk / idle under the arms
            Stance();
            Solve();
        }

        // sneaking and peeking (DESIGN 3R). The street crowd's crouch clip is a deep forward kneel, which put his
        // hunched shoulder in front of the lowered camera, so instead the body sinks with the crouched capsule
        // (chest stays under the eyes; the legs are out of view) and the spine bends into a lean.
        // TODO: a real crouch-walk clip (Mixamo, on Hamzah's OK) replaces the sink.
        public void Stance()
        {
            if (player == null) return;
            float sink = (1f - player.HeightRatio) * player.standHeight;
            transform.localPosition = new Vector3(0f, -sink, 0f);
            if (anim == null || Mathf.Abs(player.LeanOffset) < 0.005f) return;
            var spine = anim.GetBoneTransform(HumanBodyBones.Spine);
            var chest = anim.GetBoneTransform(HumanBodyBones.Chest);
            float roll = -(player.LeanOffset / Mathf.Max(0.01f, player.leanDistance)) * player.leanRoll;
            Vector3 axis = player.transform.forward;
            if (spine != null) spine.rotation = Quaternion.AngleAxis(roll * 0.6f, axis) * spine.rotation;
            if (chest != null) chest.rotation = Quaternion.AngleAxis(roll * 0.6f, axis) * chest.rotation;
        }

        public void Solve()
        {
            Init();
            if (right == null) return;
            Pose(right, rightTarget, rightHint, rightWeight, rightCurl);
            Pose(left, leftTarget, leftHint, leftWeight, leftCurl);
        }

        void Pose(Arm a, Transform target, Transform hint, float w, float curl)
        {
            if (target == null || w <= 0.001f) return;
            Vector3 goal = Vector3.Lerp(a.hand.position, target.position, w);
            TwoBone(a, goal, hint != null ? hint.position : a.fore.position + Vector3.down * 0.3f);
            // the hand: fingers along the target's forward, palm facing the target's -right (right hand) / +right (left)
            Vector3 fwd = target.forward, palm = a.isRight ? -target.right : target.right;
            var want = Quaternion.LookRotation(fwd, -palm) * a.handOffset;
            a.hand.rotation = Quaternion.Slerp(a.hand.rotation, want, w);
            Curl(a, curl * w);
        }

        // analytic two-bone IK: elbow from the law of cosines, bent towards the hint
        static void TwoBone(Arm a, Vector3 t, Vector3 hint)
        {
            Vector3 s = a.upper.position;
            float l1 = Vector3.Distance(s, a.fore.position), l2 = Vector3.Distance(a.fore.position, a.hand.position);
            Vector3 d = t - s;
            float dist = Mathf.Clamp(d.magnitude, 0.05f, l1 + l2 - 0.001f);
            Vector3 dir = d.normalized;
            Vector3 bend = Vector3.ProjectOnPlane(hint - s, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(Vector3.down, dir);
            bend.Normalize();
            float cosA = Mathf.Clamp((l1 * l1 + dist * dist - l2 * l2) / (2f * l1 * dist), -1f, 1f);
            Vector3 elbow = s + dir * (cosA * l1) + bend * (Mathf.Sqrt(1f - cosA * cosA) * l1);
            a.upper.rotation = Quaternion.FromToRotation(a.fore.position - s, elbow - s) * a.upper.rotation;
            a.fore.rotation = Quaternion.FromToRotation(a.hand.position - a.fore.position, s + dir * dist - a.fore.position) * a.fore.rotation;
        }

        // fingers bend towards the palm (sign found from the bones, so it works for either hand's rig axes)
        static void Curl(Arm a, float amount)
        {
            var frame = HandFrame(a);
            Vector3 palm = frame * Vector3.down, across = frame * Vector3.right;
            for (int f = 0; f < a.fingers.Length; f++)
            {
                float sign = 1f;
                for (int k = 0; k < a.fingers[f].Length; k++)
                {
                    var b = a.fingers[f][k];
                    if (b == null) continue;
                    b.localRotation = a.fingerRest[f][k];
                    // the way to the palm is judged once, at the knuckle: past ~90 the next joint already points
                    // into the palm and the same test flips, bending it backwards (a claw instead of a fist)
                    if (k == 0)
                    {
                        var child = a.fingers[f][1];
                        Vector3 dir = child != null ? (child.position - b.position).normalized : frame * Vector3.forward;
                        sign = Vector3.Dot(Quaternion.AngleAxis(10f, across) * dir, palm) > Vector3.Dot(dir, palm) ? 1f : -1f;
                    }
                    float angle = (k == 0 ? 85f : k == 1 ? 95f : 40f) * amount * (f == 0 ? 0.85f : 1f);   // a real wrap: ~90 at the knuckle and the middle joint
                    b.rotation = Quaternion.AngleAxis(angle * sign, across) * b.rotation;
                }
            }
            // the thumb folds in across the palm, round the hand's long axis, towards the middle of the curled fingers
            Vector3 fold = a.hand.position + frame * new Vector3(0f, -0.03f, 0.07f);
            for (int k = 0; k < a.thumb.Length; k++)
            {
                var b = a.thumb[k];
                if (b == null) continue;
                b.localRotation = a.thumbRest[k];
                var tip = k + 1 < a.thumb.Length && a.thumb[k + 1] != null ? a.thumb[k + 1] : b.childCount > 0 ? b.GetChild(0) : null;
                if (tip == null) continue;
                // try the whole fold both ways and keep the one that brings the tip in (mirrored rigs differ)
                Vector3 axis = frame * Vector3.forward;
                float angle = (k == 0 ? 45f : k == 1 ? 45f : 35f) * amount;
                var start = b.rotation;
                b.rotation = Quaternion.AngleAxis(angle, axis) * start;
                float dPlus = (tip.position - fold).sqrMagnitude;
                b.rotation = Quaternion.AngleAxis(-angle, axis) * start;
                if (dPlus < (tip.position - fold).sqrMagnitude) b.rotation = Quaternion.AngleAxis(angle, axis) * start;
            }
        }

        // where a hand would sit, for tests: hand bone world position
        public Vector3 HandPosition(bool rightHand) { Init(); return (rightHand ? right : left)?.hand.position ?? Vector3.zero; }
        public float ArmReach(bool rightHand)
        {
            Init();
            var a = rightHand ? right : left;
            return a == null ? 0f : Vector3.Distance(a.upper.position, a.fore.position) + Vector3.Distance(a.fore.position, a.hand.position);
        }
        // how closed a hand is, for tests and the range: the angle between the back of the hand (wrist to
        // knuckle) and the index fingertip's last bone. ~0 = flat, 150+ = a fist
        public float IndexBend(bool rightHand)
        {
            Init();
            var a = rightHand ? right : left;
            if (a == null || a.fingers[0][0] == null || a.fingers[0][2] == null) return 0f;
            var tip = a.fingers[0][2];
            Vector3 tipDir = tip.childCount > 0 ? tip.GetChild(0).position - tip.position : tip.position - a.fingers[0][1].position;
            return Vector3.Angle(a.fingers[0][0].position - a.hand.position, tipDir);
        }

        // the thumb tip's distance to the curled fingers (m), for the range and tests: a folded thumb is ~0.03-0.05
        public float ThumbGap(bool rightHand)
        {
            Init();
            var a = rightHand ? right : left;
            if (a == null || a.thumb[2] == null) return -1f;
            var tip = a.thumb[2].childCount > 0 ? a.thumb[2].GetChild(0) : a.thumb[2];
            var mid = a.fingers[1][1] != null ? a.fingers[1][1].position : a.hand.position;
            return Vector3.Distance(tip.position, mid);
        }

        public string ThumbBones(bool rightHand)
        {
            Init();
            var a = rightHand ? right : left;
            return a == null ? "-" : string.Join(",", System.Array.ConvertAll(a.thumb, t => t != null ? t.name : "null"));
        }

        public Vector3 Shoulder(bool rightHand) { Init(); return (rightHand ? right : left)?.upper.position ?? Vector3.zero; }
    }
}
