using System.Collections.Generic;
using LifeInDowntown.Game;
using LifeInDowntown.Items;
using LifeInDowntown.Player;
using LifeInDowntown.World;
using UnityEngine;

namespace LifeInDowntown.NPC
{
    // Someone who shoots back (docs/DESIGN.md 3P.4). Idle -> Alert -> Combat -> Fled / Dead.
    //   Alert:  heard a shot within 60 m, saw Tarik with a gun out (or close, within 20 m), or got hit
    //   sight:  40 m, a 120 degree view (all round once fighting), a clear line to Tarik's chest or head
    //   Combat: pick cover within 15 m, run to it, shoot from it or peek out to the side, 2-3 shot bursts with
    //           both hands on the pistol (a TarikBody of their own); reload after 12; under 25 health a coin flip
    //           says run. Accuracy falls off with range, with Tarik moving and with him half behind cover.
    //   Dead:   the pistol drops, picked up as 9mm rounds.
    // Cover is found, not placed: points on 1 m rings round them where the line from Tarik's chest is blocked by
    // something solid within 2.5 m (a parked car, a corner, a column). Step(dt) is the whole update (tests).
    public class Hostile : MonoBehaviour
    {
        public enum State { Idle, Alert, Combat, Fled, Dead }
        public static readonly List<Hostile> All = new List<Hostile>();
        public static System.Func<float> Rand = () => Random.value;   // tests pin the dice

        public State state = State.Idle;
        public float sight = 40f, fov = 120f, hearing = 60f, coverRange = 15f, runSpeed = 3.4f;
        public int magazine = 12;
        public GameObject pistolModel;

        public int Loaded { get; private set; }
        public int ShotsFired { get; private set; }
        public int Hits { get; private set; }
        public int NearMisses { get; private set; }
        public int Reloads { get; private set; }
        public Vector3? Cover { get; private set; }
        public bool Peeking { get; private set; }
        public bool Reloading => reloadT >= 0f;

        Pedestrian ped;
        Animator anim;
        TarikBody arms;
        Transform gun, muzzle;
        Light flash;
        LineRenderer tracer;
        AudioSource shotSrc;
        static AudioClip shotClip;
        float alertT, burstGap = 0.6f, shotGap, reloadT = -1f, coverT, flashUntil, tracerUntil, clock;
        int burstLeft;
        bool fleeRolled;
        Vector3 lastKnown, peekPoint;
        static readonly int ModeId = Animator.StringToHash("Mode");

        static GameState S => GameState.Current;
        static Transform Tarik() => Pedestrian.Player != null ? Pedestrian.Player : GameObject.Find("Player")?.transform;
        static Vector3 Chest(Transform t) => t.position + Vector3.up * 1.3f;
        static Vector3 Head(Transform t) => t.position + Vector3.up * 1.62f;
        Vector3 Eye => transform.position + Vector3.up * 1.55f;
        public bool Down => state == State.Dead || state == State.Fled;

        void OnDisable() => All.Remove(this);
        void Update() => Step(Time.deltaTime);

        public void Init()
        {
            if (ped != null) return;
            ped = GetComponent<Pedestrian>();
            anim = GetComponentInChildren<Animator>();
            if (!All.Contains(this)) All.Add(this);
            ped.role = Pedestrian.Role.Hostile;
            ped.path = null; ped.watchPlayer = false;
            Loaded = magazine;
            // the pistol in both hands (grip points as Tarik's own), a muzzle flash and a faint tracer
            gun = (pistolModel != null ? Instantiate(pistolModel) : WeaponHolder.StandIn()).transform;
            gun.name = "HostilePistol";
            gun.SetParent(transform, false);
            var b = WeaponHolder.Bounds(gun);
            muzzle = new GameObject("Muzzle").transform; muzzle.SetParent(gun, false);
            muzzle.localPosition = new Vector3(0f, b.max.y - 0.018f, b.max.z + 0.01f);
            arms = gameObject.AddComponent<TarikBody>();
            arms.anim = anim;
            arms.rightTarget = Point(gun, "GripR", WeaponHolder.GripRPos, WeaponHolder.GripRRot);
            arms.leftTarget = Point(gun, "GripL", WeaponHolder.GripLPos, WeaponHolder.GripLRot);
            arms.rightHint = Point(transform, "HintR", new Vector3(0.4f, 0.9f, 0.1f), Quaternion.identity);
            arms.leftHint = Point(transform, "HintL", new Vector3(-0.4f, 0.9f, 0.1f), Quaternion.identity);
            arms.rightWeight = arms.leftWeight = 1f;
            flash = muzzle.gameObject.AddComponent<Light>();
            flash.type = LightType.Point; flash.range = 7f; flash.color = new Color(1f, 0.78f, 0.45f); flash.intensity = 0f;
            var tg = new GameObject("Tracer");
            tg.transform.SetParent(transform, false);
            tracer = tg.AddComponent<LineRenderer>();
            tracer.positionCount = 2; tracer.startWidth = 0.012f; tracer.endWidth = 0.004f; tracer.enabled = false;
            var unlit = Shader.Find("HDRP/Unlit");
            if (unlit != null) { tracer.sharedMaterial = new Material(unlit); tracer.sharedMaterial.SetColor("_UnlitColor", new Color(1f, 0.85f, 0.55f, 0.6f)); }
            shotSrc = gameObject.AddComponent<AudioSource>();
            shotSrc.playOnAwake = false; shotSrc.spatialBlend = 1f; shotSrc.rolloffMode = AudioRolloffMode.Logarithmic;
            shotSrc.minDistance = 4f; shotSrc.maxDistance = 300f;
            if (shotClip == null) shotClip = Resources.Load<AudioClip>("Audio/gunshot_9mm");
            HoldGun(false, null);
        }

        static Transform Point(Transform parent, string n, Vector3 lp, Quaternion lr)
        {
            var t = new GameObject(n).transform;
            t.SetParent(parent, false); t.localPosition = lp; t.localRotation = lr;
            return t;
        }

        // ---------------------------------------------------------------- perception
        public static void HearShot(Vector3 at)
        {
            foreach (var h in new List<Hostile>(All))
                if (h != null && !h.Down && (h.transform.position - at).sqrMagnitude < h.hearing * h.hearing) h.Alarm(at);
        }

        public void Alarm(Vector3 at)
        {
            Init();
            if (Down) return;
            if (state == State.Idle) { state = State.Alert; alertT = 0f; }
            lastKnown = at;
        }

        public void TookHit(Vector3 from)
        {
            Alarm(from);
            if (state == State.Alert) state = State.Combat;   // no time to look around once shot
        }

        // a clear line from `from` to `to`: nothing solid in between except Tarik himself
        static bool Clear(Vector3 from, Vector3 to, Transform target)
        {
            if (!Physics.Linecast(from, to, out var hit, ~0, QueryTriggerInteraction.Ignore)) return true;
            return target != null && hit.transform.IsChildOf(target);
        }

        bool Sees(Transform t, out bool chest, out bool head)
        {
            chest = head = false;
            Vector3 to = t.position - transform.position; to.y = 0f;
            if (to.magnitude > sight) return false;
            if (state != State.Combat && Vector3.Angle(transform.forward, to) > fov * 0.5f) return false;
            Vector3 from = Eye + (to.sqrMagnitude > 1e-4f ? to.normalized : transform.forward) * 0.45f;   // out past their own capsule
            chest = Clear(from, Chest(t), t);
            head = Clear(from, Head(t), t);
            return chest || head;
        }

        // ---------------------------------------------------------------- the loop
        public void Step(float dt)
        {
            Init();
            clock += dt;
            if (flash != null) flash.intensity = clock < flashUntil ? 9000f : 0f;
            if (tracer != null) tracer.enabled = clock < tracerUntil;
            if (state == State.Dead) return;
            if (ped.Dead) { Die(); return; }
            if (state == State.Fled) return;
            var t = Tarik();
            if (t == null) return;
            bool sees = Sees(t, out bool chest, out bool head);
            if (sees) lastKnown = t.position;
            Vector3 flat = t.position - transform.position; flat.y = 0f;

            switch (state)
            {
                case State.Idle:
                    if (sees && (S.drawn || flat.magnitude < 20f)) Alarm(t.position);
                    SetMode(Pedestrian.Mode.TalkCool, 1f);
                    break;
                case State.Alert:
                    alertT += dt;
                    Face(lastKnown, dt);
                    if (sees && alertT >= 0.6f) state = State.Combat;   // a moment to clock him
                    else if (alertT > 1.5f) MoveTo(lastKnown, dt, 1.6f);
                    else SetMode(Pedestrian.Mode.LookAround, 1f);
                    break;
                case State.Combat:
                    Fight(dt, t, sees, chest, head);
                    break;
            }
            HoldGun(state == State.Combat && !Reloading, state == State.Combat ? t : null);
        }

        void Fight(float dt, Transform t, bool sees, bool chest, bool head)
        {
            if (ped.health < 25f && !fleeRolled)
            {
                fleeRolled = true;
                if (Rand() < 0.5f) { state = State.Fled; arms.rightWeight = arms.leftWeight = 0f; ped.Flee(t.position); return; }
            }
            if (reloadT >= 0f)
            {
                reloadT += dt;
                if (reloadT >= 2f) { reloadT = -1f; Loaded = magazine; Reloads++; }
            }

            coverT -= dt;
            if (Cover == null || coverT <= 0f || Exposed(Cover.Value, t)) { Cover = FindCover(t); coverT = 6f; Peeking = false; }
            Vector3 goal = Peeking ? peekPoint : Cover ?? transform.position;
            bool there = Flat(goal - transform.position).magnitude < 0.3f;
            if (!there) { MoveTo(goal, dt, runSpeed); return; }

            Face(t.position, dt);
            SetMode(Pedestrian.Mode.Idle, 1f);
            bool canShoot = Clear(Eye + Flat(t.position - transform.position).normalized * 0.45f, chest ? Chest(t) : Head(t), t) && (chest || head);
            if (!canShoot && !Peeking && Cover != null && !Reloading)
            {
                // step out to one side until there's a line to him, shoot, step back
                Vector3 side = Vector3.Cross(Vector3.up, Flat(t.position - Cover.Value).normalized);
                foreach (float s in new[] { 0.8f, -0.8f, 1.4f, -1.4f })
                {
                    Vector3 p = Cover.Value + side * s;
                    if (Physics.CheckCapsule(p + Vector3.up * 0.5f, p + Vector3.up * 1.5f, 0.28f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (Clear(p + Vector3.up * 1.55f, Chest(t), t) || Clear(p + Vector3.up * 1.55f, Head(t), t)) { peekPoint = p; Peeking = true; break; }
                }
                return;
            }
            if (!canShoot || Reloading) return;

            if (burstLeft == 0) { burstGap -= dt; if (burstGap <= 0f) { burstLeft = Rand() < 0.5f ? 2 : 3; shotGap = 0f; } }
            if (burstLeft > 0)
            {
                shotGap -= dt;
                if (shotGap <= 0f)
                {
                    Shoot(t, chest);
                    burstLeft--; shotGap = 0.18f;
                    if (Loaded <= 0) { burstLeft = 0; reloadT = 0f; Peeking = false; }
                    if (burstLeft == 0) { burstGap = 1.2f + Rand() * 0.8f; Peeking = false; }   // duck back between bursts
                }
            }
        }

        void Shoot(Transform t, bool chest)
        {
            Loaded--; ShotsFired++;
            flashUntil = clock + 0.05f;
            if (shotClip != null && shotSrc != null && Application.isPlaying) shotSrc.PlayOneShot(shotClip, 1f);
            WeaponHolder.PanicAt(transform.position);
            HearShot(transform.position);
            float dist = Flat(t.position - transform.position).magnitude;
            float p = 0.6f * Mathf.Lerp(1f, 0.2f, Mathf.InverseLerp(5f, 40f, dist));
            var bp = t.GetComponent<BodycamPlayer>();
            if (bp != null && bp.HorizontalSpeed > 1f) p *= 0.6f;     // a moving target
            if (!chest) p *= 0.4f;                                    // only his head shows over cover
            Vector3 aimAt = chest ? Chest(t) : Head(t);
            Vector3 from = muzzle != null ? muzzle.position : Eye;
            Vector3 end;
            if (Rand() < p)
            {
                Hits++;
                PlayerStats.Instance?.TakeDamage(PlayerStats.BulletDamage(dist), transform.position);
                end = aimAt;
            }
            else
            {
                Vector3 off = Random.onUnitSphere; off.y = Mathf.Abs(off.y) * 0.5f;
                Vector3 pass = aimAt + off.normalized * Random.Range(0.35f, 1.4f);
                if ((pass - aimAt).magnitude < 1.2f) { NearMisses++; PlayerStats.Instance?.NearMiss(pass); }
                end = from + (pass - from).normalized * 60f;
            }
            if (tracer != null) { tracer.SetPosition(0, from); tracer.SetPosition(1, end); tracerUntil = clock + 0.04f; }
        }

        // ---------------------------------------------------------------- cover
        public Vector3? FindCover(Transform t)
        {
            Vector3 tc = Chest(t);
            Vector3? best = null; float bestScore = float.MaxValue;
            for (float r = 1f; r <= coverRange; r += 1f)
                for (float a = 0f; a < 360f; a += 22.5f)
                {
                    Vector3 p = transform.position + Quaternion.Euler(0f, a, 0f) * Vector3.forward * r;
                    if (!Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out var g, 3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (Mathf.Abs(g.point.y - transform.position.y) > 0.6f || g.collider.attachedRigidbody != null || g.collider.GetComponentInParent<Pedestrian>() != null) continue;
                    p.y = g.point.y;
                    if (Flat(p - t.position).magnitude < 5f) continue;
                    if (Physics.CheckCapsule(p + Vector3.up * 0.5f, p + Vector3.up * 1.5f, 0.28f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (!Physics.Linecast(tc, p + Vector3.up * 1.0f, out var h, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (h.collider.GetComponentInParent<Pedestrian>() != null || h.transform.IsChildOf(t) || (h.point - (p + Vector3.up)).magnitude > 2.5f) continue;
                    float score = r + 0.3f * Mathf.Abs(Flat(p - t.position).magnitude - 12f);   // close by, not too near him or too far
                    if (score < bestScore) { bestScore = score; best = p; }
                }
            return best;
        }

        public static bool Exposed(Vector3 cover, Transform t) => !Physics.Linecast(Chest(t), cover + Vector3.up * 1.0f, out _, ~0, QueryTriggerInteraction.Ignore);

        // ---------------------------------------------------------------- body
        void MoveTo(Vector3 goal, float dt, float speed)
        {
            Vector3 d = Flat(goal - transform.position);
            if (d.magnitude < 0.05f) return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 540f * dt);
            transform.position += d.normalized * Mathf.Min(speed * dt, d.magnitude);
            SetMode(Pedestrian.Mode.Walk, speed / 1.16f * 0.55f);
        }

        void Face(Vector3 at, float dt)
        {
            Vector3 d = Flat(at - transform.position);
            if (d.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 360f * dt);
        }

        void SetMode(Pedestrian.Mode m, float speed)
        {
            if (anim == null || !anim.isActiveAndEnabled) return;
            anim.SetInteger(ModeId, (int)m); anim.speed = speed;
        }

        // raised: both hands out at the chest, aimed at him; otherwise low ready by the hip
        void HoldGun(bool raised, Transform t)
        {
            if (gun == null) return;
            if (raised && t != null)
            {
                gun.position = transform.TransformPoint(new Vector3(0.02f, 1.42f, 0.42f));
                gun.rotation = Quaternion.LookRotation(Chest(t) - gun.position, Vector3.up);
                arms.rightHint.localPosition = new Vector3(0.4f, 0.9f, 0.1f); arms.leftHint.localPosition = new Vector3(-0.4f, 0.9f, 0.1f);
            }
            else
            {
                gun.localPosition = new Vector3(0.1f, 1.0f, 0.32f);
                gun.localRotation = Quaternion.Euler(45f, -10f, 0f);
                arms.rightHint.localPosition = new Vector3(0.35f, 0.7f, -0.2f); arms.leftHint.localPosition = new Vector3(-0.35f, 0.7f, -0.2f);
            }
        }

        void Die()
        {
            state = State.Dead;
            All.Remove(this);
            if (arms != null) arms.rightWeight = arms.leftWeight = 0f;
            if (gun == null) return;
            // the pistol hits the ground beside them: E picks up what's left in it plus the spare mag
            gun.SetParent(null, true);
            gun.SetPositionAndRotation(transform.position + transform.right * 0.4f + Vector3.up * 0.03f, Quaternion.Euler(90f, Random.Range(0f, 360f), 0f));
            var box = gun.gameObject.AddComponent<BoxCollider>();
            box.size = new Vector3(0.04f, 0.15f, 0.2f);
            var pick = gun.gameObject.AddComponent<WorldPickup>();
            pick.itemId = "ammo_9mm"; pick.count = Mathf.Max(0, Loaded) + 6; pick.pickupId = "";
            gun = null;
        }

        // editor renders: squared up to `t`, pistol raised in both hands (or held low)
        public void PoseForShot(Transform t, bool raised = true)
        {
            Init();
            Vector3 d = Flat(t.position - transform.position);
            if (d.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(d);
            HoldGun(raised, t);
            arms.Solve(); arms.Solve();
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
