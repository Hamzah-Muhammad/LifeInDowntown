using UnityEngine;

namespace LifeInDowntown.NPC
{
    // A street NPC (docs/DESIGN.md 3B.2): walks back and forth along a sidewalk path, or stands idle /
    // on the phone at one spot. The animator's "Mode" int picks the clip; the clips are in place,
    // so this script moves the body.
    public class Pedestrian : MonoBehaviour
    {
        public enum Mode { Walk = 0, WalkSlow = 1, Idle = 2, Phone = 3 }

        public Mode mode = Mode.Walk;
        public Vector3[] path;            // sidewalk points (world), walked forward then back
        public int startIndex;
        public float walkSpeed = 1.16f;   // measured: clip averageSpeed of the Rocketbox neutral walks (no foot sliding)
        public float slowSpeed = 0.85f;   // measured: slow walk
        public float turnSpeed = 220f;    // degrees per second

        Animator anim;
        int target, dir = 1;

        static readonly int ModeId = Animator.StringToHash("Mode");

        void Start()
        {
            anim = GetComponentInChildren<Animator>();
            if (anim != null)
            {
                anim.SetInteger(ModeId, (int)mode);
                anim.speed = Random.Range(0.92f, 1.08f);
                anim.Update(Random.Range(0f, 3f));      // desync the crowd's steps
            }
            target = Mathf.Clamp(startIndex + 1, 0, path != null ? path.Length - 1 : 0);
        }

        void Update()
        {
            if (mode == Mode.Idle || mode == Mode.Phone || path == null || path.Length < 2) return;
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
    }
}
