using UnityEngine;

namespace LifeInDowntown.NPC
{
    // A street NPC (docs/DESIGN.md 3B.2, 3L): walks back and forth along a sidewalk path, or holds one
    // street pose at a spot (waiting at the lights, loitering on a corner, sitting or crouched against a
    // wall). The animator's "Mode" int picks the clip; the clips are in place, so this script moves the body.
    public class Pedestrian : MonoBehaviour
    {
        // order = animator states = CharacterImport clip lists
        public enum Mode { Walk, WalkSlow, Idle, Phone, Crouch, Sit, SitLook, Drunk, Cough, Cold, Nervous, LookAround, TalkCool, Listen, Waiting }

        public Mode mode = Mode.Walk;
        public Vector3[] path;            // sidewalk points (world), walked forward then back
        public int startIndex;
        public float walkSpeed = 1.16f;   // measured: clip averageSpeed of the Rocketbox neutral walks (no foot sliding)
        public float slowSpeed = 0.85f;   // measured: slow walk
        public float turnSpeed = 220f;    // degrees per second
        public bool watchPlayer;          // corner loiterers turn to clock you when you get close
        public float watchRange = 5f;

        public static Transform Player;   // set by CrowdDirector

        Animator anim;
        int target, dir = 1;
        Quaternion home;

        static readonly int ModeId = Animator.StringToHash("Mode");

        public bool Walking => mode == Mode.Walk || mode == Mode.WalkSlow;

        void Start()
        {
            home = transform.rotation;
            Animate();
            if (path != null && target == 0) target = Mathf.Clamp(startIndex + dir, 0, path.Length - 1);
        }

        // re-enabling resets the animator's parameters (CrowdDirector switches far people off and on)
        void OnEnable() { if (anim != null) anim.SetInteger(ModeId, (int)mode); }

        // pooled walkers are re-used: new path, new place, new heading
        public void Begin(Vector3[] newPath, int start, int heading, Mode m)
        {
            path = newPath; startIndex = start; mode = m;
            dir = heading >= 0 ? 1 : -1;
            target = Mathf.Clamp(start + dir, 0, path.Length - 1);
            var to = path[target] - path[start]; to.y = 0f;
            transform.SetPositionAndRotation(path[start], to.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(to) : transform.rotation);
            Animate();
        }

        void Animate()
        {
            if (anim == null) anim = GetComponentInChildren<Animator>();
            if (anim == null) return;
            anim.SetInteger(ModeId, (int)mode);
            anim.speed = Random.Range(0.92f, 1.08f);
            anim.Update(Random.Range(0f, 3f));      // desync the crowd's steps and idles
        }

        void Update()
        {
            if (!Walking || path == null || path.Length < 2) { Watch(); return; }
            float speed = (mode == Mode.WalkSlow ? slowSpeed : walkSpeed) * (anim != null ? anim.speed : 1f);
            Vector3 to = path[target] - transform.position;
            to.y = 0f;
            if (to.magnitude < 0.3f)
            {
                if (target + dir < 0 || target + dir >= path.Length) dir = -dir;   // end of the block: turn around
                target += dir;
                return;
            }
            var want = Quaternion.LookRotation(to);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeed * Time.deltaTime);
            // only walk forward once roughly facing the way (no moonwalking round corners)
            if (Quaternion.Angle(transform.rotation, want) < 35f)
                transform.position += transform.forward * (speed * Time.deltaTime);
        }

        void Watch()
        {
            if (!watchPlayer || Player == null) return;
            Vector3 to = Player.position - transform.position; to.y = 0f;
            var want = to.magnitude < watchRange && to.sqrMagnitude > 0.01f ? Quaternion.LookRotation(to) : home;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, 90f * Time.deltaTime);
        }
    }
}
