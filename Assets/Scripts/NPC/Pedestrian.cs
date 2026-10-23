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

        // who they are on the street: picks their lines (NpcBarks) and who dealers worry about (DESIGN 3O)
        public enum Role { Walker, Corner, Homeless, Police, Waiting, Clerk, Story }

        public Mode mode = Mode.Walk;
        public Role role = Role.Walker;
        public static readonly System.Collections.Generic.List<Pedestrian> All = new System.Collections.Generic.List<Pedestrian>();
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
        public bool Dead { get; private set; }
        public float health = 100f;
        public const float HeadHeight = 1.5f;   // a hit this far above their feet is the head
        float hurry = 1f;            // fleeing: the walk clip and pace sped up

        // a hit (DESIGN 3P.2): the head drops them at once, anything else takes `amount`. A wounded person runs
        // from where it came. True if this one killed them.
        public bool Damage(float amount, Vector3 hitPoint, Vector3 push, Vector3 from)
        {
            if (Dead) return false;
            if (hitPoint.y - transform.position.y > HeadHeight) amount = health;
            health -= amount;
            if (health <= 0f) { Kill(hitPoint, push); return true; }
            Flee(from);
            return false;
        }

        public static float BodyShot() => Random.Range(50f, 55f);   // two of these always drop anyone (45-55 could leave them on 6)

        // shot (DESIGN 3O.6): ragdoll, stop thinking
        public void Kill(Vector3 hitPoint, Vector3 push)
        {
            if (Dead) return;
            Dead = true;
            LifeInDowntown.NPC.Ragdoll.Go(gameObject, hitPoint, push);
            enabled = false;
        }

        // a gunshot nearby: run away from it along a straight line (sitting / crouched people stay and cower)
        public void Flee(Vector3 from)
        {
            if (Dead || mode == Mode.Sit || mode == Mode.SitLook || mode == Mode.Crouch) return;
            var away = transform.position - from; away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = transform.forward;
            away.Normalize();
            path = new[] { transform.position, transform.position + away * 40f };
            startIndex = 0; target = 1; dir = 1;
            watchPlayer = false;
            mode = Mode.Walk; hurry = 2.6f;
            if (anim != null) { anim.SetInteger(ModeId, (int)mode); anim.speed = 1.9f; }
        }

        // police: walk up to a few metres from Tarik and watch him (no police combat yet)
        public void Approach(Vector3 to, float stopAt = 7f)
        {
            if (Dead) return;
            var d = to - transform.position; d.y = 0f;
            if (d.magnitude <= stopAt) { mode = Mode.LookAround; watchPlayer = true; watchRange = 40f; if (anim != null) anim.SetInteger(ModeId, (int)mode); return; }
            path = new[] { transform.position, to - d.normalized * stopAt };
            startIndex = 0; target = 1; dir = 1;
            mode = Mode.Walk; hurry = 1.3f; watchPlayer = true; watchRange = 40f;
            if (anim != null) { anim.SetInteger(ModeId, (int)mode); anim.speed = 1.15f; }
        }

        void Start()
        {
            home = transform.rotation;
            Animate();
            if (path != null && target == 0) target = Mathf.Clamp(startIndex + dir, 0, path.Length - 1);
        }

        // re-enabling resets the animator's parameters (CrowdDirector switches far people off and on)
        void OnEnable() { All.Add(this); if (anim != null) anim.SetInteger(ModeId, (int)mode); }
        void OnDisable() => All.Remove(this);

        // pooled walkers are re-used: new path, new place, new heading
        public void Begin(Vector3[] newPath, int start, int heading, Mode m)
        {
            path = newPath; startIndex = start; mode = m; hurry = 1f;
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
            float speed = (mode == Mode.WalkSlow ? slowSpeed : walkSpeed) * (hurry > 1f ? hurry : anim != null ? anim.speed : 1f);
            Vector3 to = path[target] - transform.position;
            to.y = 0f;
            if (to.magnitude < 0.3f)
            {
                if (hurry > 1f && target == path.Length - 1)   // ran (or walked up) as far as they meant to: stop and look
                {
                    hurry = 1f; mode = watchPlayer ? Mode.LookAround : Mode.Nervous; path = null;
                    if (anim != null) { anim.speed = 1f; anim.SetInteger(ModeId, (int)mode); }
                    return;
                }
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
