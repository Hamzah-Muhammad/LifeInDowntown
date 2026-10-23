using MinniGameFramework.Combat;
using MinniGameFramework.Core;
using MinniGameFramework.NPC;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering;
using UnityEngine;

namespace MinniGameFramework.Player
{
    // What getting hurt feels like (docs/DESIGN.md 3P.2), on the eye camera:
    //   a hit     - a red vignette pulse, a bodycam glitch (chromatic aberration + a burst of noise), the view
    //               knocked, a dull thud
    //   near miss - a tinnitus ring that fades over ~3 s
    //   low health (under 30) - a heartbeat that gets louder towards 0, and the red edge breathing with it
    // Listens to PlayerStats.Hurt / NearMissed. Step(dt) is the whole update, so tests and renders can drive it.
    public class HurtFeedback : MonoBehaviour
    {
        public float Pulse { get; private set; }      // 0-1, the red edge from the last hit
        public float Glitch { get; private set; }     // 0-1, the bodycam glitch
        public float Ring { get; private set; }       // 0-1, tinnitus loudness
        public float Heart { get; private set; }      // 0-1, heartbeat loudness (low health)

        Volume red, glitch;
        AudioSource sfx, ringSrc, heartSrc;
        BodycamRig rig;
        float beatT;

        static IPlayerState S => FrameworkHooks.S;

        bool listening;
        void OnEnable() => Listen(true);
        void OnDisable() => Listen(false);
        void OnDestroy() => Listen(false);
        void Listen(bool on)
        {
            if (on == listening) return;
            listening = on;
            if (on) { PlayerStats.Hurt += OnHurt; PlayerStats.NearMissed += OnNearMiss; }
            else { PlayerStats.Hurt -= OnHurt; PlayerStats.NearMissed -= OnNearMiss; }
        }

        public void Init()
        {
            Listen(true);   // edit mode (tests, renders) never runs OnEnable
            if (red != null) return;
            rig = GetComponent<BodycamRig>();
            var rp = ScriptableObject.CreateInstance<VolumeProfile>();
            var v = rp.Add<Vignette>(true);
            v.color.Override(new Color(0.55f, 0.02f, 0.02f)); v.intensity.Override(0.55f); v.smoothness.Override(0.75f);
            red = MakeVolume("HurtRed", rp, 45f);
            var gp = ScriptableObject.CreateInstance<VolumeProfile>();
            gp.Add<ChromaticAberration>(true).intensity.Override(1f);
            var grain = gp.Add<FilmGrain>(true); grain.type.Override(FilmGrainLookup.Medium4); grain.intensity.Override(1f); grain.response.Override(0.2f);
            glitch = MakeVolume("HurtGlitch", gp, 46f);
            sfx = Source(false, null);
            ringSrc = Source(false, Resources.Load<AudioClip>("Audio/tinnitus"));
            heartSrc = Source(true, Resources.Load<AudioClip>("Audio/heartbeat"));
        }

        Volume MakeVolume(string name, VolumeProfile p, float priority)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true; vol.priority = priority; vol.weight = 0f; vol.sharedProfile = p;
            return vol;
        }

        AudioSource Source(bool loop, AudioClip clip)
        {
            var a = gameObject.AddComponent<AudioSource>();
            a.playOnAwake = false; a.spatialBlend = 0f; a.loop = loop; a.clip = clip; a.volume = 0f;
            return a;
        }

        void Start() => Init();

        void OnHurt(float amount, Vector3 from)
        {
            if (this == null) { Listen(false); return; }   // destroyed with its scene in edit mode (no OnDestroy there)
            Init();
            float k = Mathf.Clamp01(amount / 25f);
            Pulse = Mathf.Max(Pulse, 0.55f + 0.45f * k);
            Glitch = Mathf.Max(Glitch, 0.5f + 0.5f * k);
            if (rig != null) rig.Kick(2f + 4f * k);
            var hit = Resources.Load<AudioClip>("Audio/hurt_hit");
            if (hit != null && sfx != null && Application.isPlaying) sfx.PlayOneShot(hit, 0.9f);
        }

        void OnNearMiss(Vector3 at)
        {
            if (this == null) { Listen(false); return; }
            Init();
            Ring = 1f;
            Glitch = Mathf.Max(Glitch, 0.25f);
            if (ringSrc != null && ringSrc.clip != null && Application.isPlaying) ringSrc.Play();
        }

        void Update()
        {
            bool playing = FrameworkHooks.Playing();
            Step(playing ? Time.deltaTime : 0f);
        }

        // one frame: decay the pulses, set the volumes and the sounds from them and from Tarik's health
        public void Step(float dt)
        {
            Init();
            Pulse = Mathf.MoveTowards(Pulse, 0f, dt / 0.7f);
            Glitch = Mathf.MoveTowards(Glitch, 0f, dt / 0.25f);
            Ring = Mathf.MoveTowards(Ring, 0f, dt / 3f);
            Heart = Mathf.InverseLerp(30f, 5f, S.health) * (S.health > 0f ? 1f : 0f);
            if (S.health < 30f) Heart = Mathf.Max(Heart, 0.25f);
            beatT += dt * 84f / 60f;
            float breathe = Heart * (0.35f + 0.2f * Mathf.Max(0f, Mathf.Sin(beatT * Mathf.PI * 2f)));
            red.weight = Mathf.Max(Pulse, breathe);
            glitch.weight = Glitch;
            if (ringSrc != null) ringSrc.volume = Ring * 0.8f;
            if (heartSrc != null && heartSrc.clip != null)
            {
                heartSrc.volume = Heart;
                if (Application.isPlaying)
                {
                    if (Heart > 0.01f && !heartSrc.isPlaying) heartSrc.Play();
                    else if (Heart <= 0.01f && heartSrc.isPlaying) heartSrc.Stop();
                }
            }
        }

        public float RedWeight => red != null ? red.weight : 0f;
        public float GlitchWeight => glitch != null ? glitch.weight : 0f;
    }
}
