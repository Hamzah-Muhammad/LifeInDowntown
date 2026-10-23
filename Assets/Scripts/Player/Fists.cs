using LifeInDowntown.Game;
using LifeInDowntown.Items;
using LifeInDowntown.NPC;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LifeInDowntown.Player
{
    // Fist fights (docs/DESIGN.md 3P.3), on the eye camera beside the WeaponHolder. With the gun holstered:
    //   V           fists up / down (they come up by themselves when someone squares up within 2.5 m)
    //   left mouse  jab, then cross, alternating; HOLD it (0.25 s) for a heavy right hook that knocks people down
    //   right mouse block: fists to the face, 70% less damage
    // Tarik's own arms (TarikBody IK) go to two fist points in eye-camera space: a guard in front of the chest,
    // a straight punch out to ~0.5 m palm-down, a hook that winds out to the right and comes round. At the
    // punch's peak a 0.22 m sphere is cast 0.9 m ahead; a person in it takes the hit (Pedestrian.Punch), with a
    // code-made impact and a small view kick. Step(dt) is the whole update, so tests and renders drive it.
    [DefaultExecutionOrder(100)]   // after WeaponHolder, which lets go of the hands while holstered
    public class Fists : MonoBehaviour
    {
        public enum Move { None, Jab, Cross, Hook }

        public static Fists Instance { get; private set; }
        public bool Up { get; private set; }
        public bool Blocking { get; private set; }
        public Move Current { get; private set; }
        public float Weight { get; private set; }
        public Pedestrian LastHit { get; private set; }
        public int Hits { get; private set; }

        public const float Reach = 0.9f, Radius = 0.22f, HoldForHook = 0.25f;
        const float JabOut = 0.1f, JabBack = 0.16f, HookWind = 0.18f, HookStrike = 0.14f, HookBack = 0.3f;
        public static float JabDamage() => Random.Range(8f, 12f);
        public static float HookDamage() => Random.Range(18f, 24f);

        // eye-camera space; hand frame as TarikBody wants it: fingers along forward, palm to -X (right) / +X (left)
        static readonly Vector3 GuardR = new Vector3(0.13f, -0.2f, 0.33f), GuardL = new Vector3(-0.11f, -0.17f, 0.37f);
        static readonly Vector3 BlockR = new Vector3(0.07f, -0.06f, 0.25f), BlockL = new Vector3(-0.07f, -0.05f, 0.26f);
        static readonly Vector3 JabR = new Vector3(0.04f, -0.1f, 0.52f), JabL = new Vector3(-0.04f, -0.1f, 0.52f);
        // the hook stays short (elbow bent ~90, out at shoulder height): wound out right, round to the middle
        static readonly Vector3 HookWound = new Vector3(0.32f, -0.16f, 0.16f), HookMid = new Vector3(0.24f, -0.07f, 0.36f), HookEnd = new Vector3(-0.02f, -0.09f, 0.36f);
        static readonly Quaternion GuardRot = Quaternion.Euler(-25f, 0f, 0f);    // knuckles forward and up, palms in
        static readonly Quaternion BlockRot = Quaternion.Euler(-75f, 0f, 0f);    // forearms up in front of the face
        static readonly Quaternion PunchRotR = Quaternion.Euler(-5f, 0f, 90f), PunchRotL = Quaternion.Euler(-5f, 0f, -90f);   // palm down
        static readonly Quaternion HookRot = Quaternion.Euler(0f, -70f, 90f);     // knuckles turned in across the body
        static readonly Vector3 HintR = new Vector3(0.32f, -0.7f, 0.05f), HintL = new Vector3(-0.32f, -0.7f, 0.05f), HookHint = new Vector3(0.7f, -0.02f, 0.1f);

        Transform fistR, fistL, hintR, hintL;
        TarikBody body;
        BodycamRig rig;
        AudioSource audioSrc;
        AudioClip swing, jab, hook;
        float moveT, holdT = -1f;
        bool hitDone, leftNext = true;

        static GameState S => GameState.Current;

        void Awake() { Instance = this; }

        public void Init()
        {
            if (Instance == null) Instance = this;
            if (fistR != null) return;
            fistR = Point("FistR"); fistL = Point("FistL"); hintR = Point("FistHintR"); hintL = Point("FistHintL");
            body = WeaponHolder.PlayerBody(this);
            rig = GetComponent<BodycamRig>();
            audioSrc = gameObject.AddComponent<AudioSource>();
            audioSrc.playOnAwake = false; audioSrc.spatialBlend = 0f;
            swing = Resources.Load<AudioClip>("Audio/punch_swing");
            jab = Resources.Load<AudioClip>("Audio/punch_jab");
            hook = Resources.Load<AudioClip>("Audio/punch_hook");
        }

        Transform Point(string n) { var t = new GameObject(n).transform; t.SetParent(transform, false); return t; }

        void Update()
        {
            Init();
            var kb = Keyboard.current; var mouse = Mouse.current;
            bool free = WeaponHolder.InputFree() && !S.drawn;
            if (free && kb != null && kb.vKey.wasPressedThisFrame) SetUp(!Up);
            if (free && !Up && NpcMelee.AnyFighterNear(transform.position, 2.5f)) SetUp(true);
            if (free && Up && mouse != null)
            {
                SetBlock(mouse.rightButton.isPressed);
                if (mouse.leftButton.wasPressedThisFrame) holdT = 0f;
                if (holdT >= 0f)
                {
                    holdT += Time.deltaTime;
                    if (holdT >= HoldForHook) { Hook(); holdT = -1f; }
                    else if (mouse.leftButton.wasReleasedThisFrame) { Jab(); holdT = -1f; }
                }
            }
            else holdT = -1f;
            bool playing = GameFlow.Instance == null || GameFlow.Instance.State == GameFlow.FlowState.Playing;
            Step(playing ? Time.deltaTime : 0f);
        }

        public void SetUp(bool on)
        {
            Up = on && !S.drawn;
            if (!Up) Blocking = false;
        }

        public void SetBlock(bool on) => Blocking = on && Up && Current == Move.None;

        public bool Jab()
        {
            if (!Up || Blocking || Current != Move.None) return false;
            Current = leftNext ? Move.Jab : Move.Cross;
            leftNext = !leftNext;
            Begin();
            return true;
        }

        public bool Hook()
        {
            if (!Up || Blocking || Current != Move.None) return false;
            Current = Move.Hook;
            Begin();
            return true;
        }

        void Begin()
        {
            moveT = 0f; hitDone = false;
            if (swing != null && audioSrc != null && Application.isPlaying) audioSrc.PlayOneShot(swing, Current == Move.Hook ? 0.8f : 0.5f);
        }

        // a punch landed on Tarik (NpcMelee): the thud of it
        public void Struck(Vector3 from, float damage)
        {
            if (jab != null && audioSrc != null && Application.isPlaying) audioSrc.PlayOneShot(jab, Blocking ? 0.4f : 0.8f);
        }

        public void Step(float dt)
        {
            Init();
            if (S.drawn && Up) SetUp(false);
            Weight = Mathf.MoveTowards(Weight, Up ? 1f : 0f, dt / 0.15f);
            if (Current != Move.None)
            {
                moveT += dt;
                bool heavy = Current == Move.Hook;
                float peak = heavy ? HookWind + HookStrike : JabOut, end = heavy ? HookWind + HookStrike + HookBack : JabOut + JabBack;
                if (!hitDone && moveT >= peak) { hitDone = true; Strike(heavy); }
                if (moveT >= end) Current = Move.None;
            }
            Pose();
            Hands();
        }

        void Strike(bool heavy)
        {
            var dir = transform.forward;
            if (!Physics.SphereCast(transform.position, Radius, dir, out var hit, Reach, ~0, QueryTriggerInteraction.Ignore)) return;
            var ped = hit.collider.GetComponentInParent<Pedestrian>();
            if (ped == null || ped.Dead) return;
            ped.Punch(heavy ? HookDamage() : JabDamage(), hit.point, dir, heavy);
            LastHit = ped; Hits++;
            if (rig != null) rig.Kick(heavy ? 1.6f : 0.7f);
            var clip = heavy ? hook : jab;
            if (clip != null && audioSrc != null && Application.isPlaying) audioSrc.PlayOneShot(clip, heavy ? 1f : 0.8f);
        }

        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        void Pose()
        {
            Vector3 r = Blocking ? BlockR : GuardR, l = Blocking ? BlockL : GuardL;
            Quaternion rr = Blocking ? BlockRot : GuardRot, lr = rr;
            Vector3 hr = HintR;
            float t = moveT;
            switch (Current)
            {
                case Move.Jab:
                case Move.Cross:
                {
                    float k = t < JabOut ? Smooth(t / JabOut) : 1f - Smooth((t - JabOut) / JabBack);
                    if (Current == Move.Jab) { l = Vector3.Lerp(l, JabL, k); lr = Quaternion.Slerp(lr, PunchRotL, k); }
                    else { r = Vector3.Lerp(r, JabR, k); rr = Quaternion.Slerp(rr, PunchRotR, k); }
                    break;
                }
                case Move.Hook:
                {
                    if (t < HookWind) { float k = Smooth(t / HookWind); r = Vector3.Lerp(r, HookWound, k); rr = Quaternion.Slerp(rr, HookRot, k); hr = Vector3.Lerp(HintR, HookHint, k); }
                    else if (t < HookWind + HookStrike)
                    {
                        float k = Smooth((t - HookWind) / HookStrike), a = 1f - k;
                        r = a * a * HookWound + 2f * a * k * HookMid + k * k * HookEnd;   // round, not straight
                        rr = HookRot; hr = HookHint;
                    }
                    else { float k = Smooth((t - HookWind - HookStrike) / HookBack); r = Vector3.Lerp(HookEnd, r, k); rr = Quaternion.Slerp(HookRot, rr, k); hr = Vector3.Lerp(HookHint, HintR, k); }
                    break;
                }
            }
            fistR.localPosition = r; fistR.localRotation = rr;
            fistL.localPosition = l; fistL.localRotation = lr;
            hintR.localPosition = hr; hintL.localPosition = HintL;
        }

        void Hands()
        {
            if (body == null || Weight <= 0.001f) return;   // WeaponHolder has the hands (or they hang)
            body.rightTarget = fistR; body.leftTarget = fistL;
            body.rightHint = hintR; body.leftHint = hintL;
            body.rightWeight = body.leftWeight = Weight;
        }

        // editor renders: freeze a move at time t (Move.None = guard; block = true for the block)
        public void PoseForShot(Move move, float t, bool block = false)
        {
            Init();
            Up = true; Weight = 1f; Blocking = block; Current = move; moveT = t; hitDone = true;
            Pose(); Hands();
            if (body != null) { body.Solve(); body.Solve(); }
        }
    }
}
