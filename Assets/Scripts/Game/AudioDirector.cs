using System.Collections.Generic;
using LifeInDowntown.Player;
using MinniGameFramework.Player;
using UnityEngine;

namespace LifeInDowntown.Game
{
    // All game sound (docs/DESIGN.md 3I): outdoor/indoor ambience beds that crossfade on the elevator,
    // 3D streetcar bells and passing sirens around the player, footsteps by surface, and phone / elevator
    // cues. Clips load from Resources/Audio (Freesound CC0 files replace the synth ones by name).
    public class AudioDirector : MonoBehaviour
    {
        public static AudioDirector Instance { get; private set; }

        public BodycamPlayer player;
        public Transform listener;

        [Header("Mix (0-1)")]
        public float rainOutdoor = 0.55f, cityHum = 0.32f, rainIndoor = 0.12f, roomTone = 0.12f;
        public float footsteps = 0.5f, phone = 0.6f;
        public float indoorAbove = 60f;         // same rule as RainFollow: the condo is ~120 m up

        AudioSource rainOut, hum, rainIn, room, ring, oneShot;
        readonly Dictionary<string, AudioClip[]> steps = new Dictionary<string, AudioClip[]>();
        AudioClip bell, siren, ping, ding;
        float indoorMix, startFade, nextBell = 20f, nextSiren = 60f;
        int lastStep = -1;

        public bool Indoors => listener != null && LifeInDowntown.World.IndoorZone.IsIndoors(listener.position);
        // the subway counts as indoors for the rain, but it has its own beds (Transit.SubwayAudio) and concrete floors
        public bool Underground => listener != null && listener.position.y < LifeInDowntown.World.IndoorZone.UndergroundBelow;

        void Awake()
        {
            Instance = this;
            rainOut = Loop("rain_outdoor"); hum = Loop("city_hum"); rainIn = Loop("rain_indoor"); room = Loop("room_tone");
            ring = gameObject.AddComponent<AudioSource>(); ring.clip = Clip("phone_ring"); ring.loop = true; ring.playOnAwake = false;
            oneShot = gameObject.AddComponent<AudioSource>(); oneShot.playOnAwake = false;
            bell = Clip("streetcar_bell"); siren = Clip("siren_wail"); ping = Clip("phone_ping"); ding = Clip("elevator_ding");
            steps["concrete"] = Set("step_concrete"); steps["wood"] = Set("step_wood");
            int beds = (rainOut.clip ? 1 : 0) + (hum.clip ? 1 : 0) + (rainIn.clip ? 1 : 0) + (room.clip ? 1 : 0);
            int cues = (ring.clip ? 1 : 0) + (bell ? 1 : 0) + (siren ? 1 : 0) + (ping ? 1 : 0) + (ding ? 1 : 0);
            Debug.Log($"AUDIO_OK beds={beds}/4 cues={cues}/5 steps concrete={steps["concrete"].Length} wood={steps["wood"].Length}");
        }

        void OnEnable() { if (player != null) player.Footstep += OnFootstep; }
        void OnDisable() { if (player != null) player.Footstep -= OnFootstep; }

        static AudioClip Clip(string name) => Resources.Load<AudioClip>("Audio/" + name);

        static AudioClip[] Set(string prefix)
        {
            var list = new List<AudioClip>();
            for (int i = 1; i <= 8; i++) { var c = Clip($"{prefix}_{i}"); if (c != null) list.Add(c); }
            return list.ToArray();
        }

        AudioSource Loop(string name)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = Clip(name); s.loop = true; s.volume = 0f; s.playOnAwake = false;
            if (s.clip != null) { s.time = Random.Range(0f, s.clip.length); s.Play(); }
            return s;
        }

        void Update()
        {
            indoorMix = Mathf.MoveTowards(indoorMix, Indoors ? 1f : 0f, Time.unscaledDeltaTime);   // ~1 s crossfade
            bool paused = GameFlow.Instance != null && GameFlow.Instance.State == GameFlow.FlowState.Paused;
            // beds fade in over the first seconds so load hitches (audio thread starved) can't pop at full level
            startFade = Mathf.MoveTowards(startFade, 1f, Time.unscaledDeltaTime / 2.5f);
            float duck = (paused || LifeInDowntown.Map.MapController.IsOpen ? 0.45f : 1f) * startFade;
            rainOut.volume = rainOutdoor * (1f - indoorMix) * duck;
            hum.volume = cityHum * (1f - indoorMix * 0.85f) * duck;    // the city is still faintly audible upstairs
            float notSubway = Underground ? 0f : 1f;
            rainIn.volume = rainIndoor * indoorMix * duck * notSubway;
            room.volume = roomTone * indoorMix * duck * notSubway;

            if (paused || listener == null || Indoors) return;
            float t = Time.time;
            if (t > nextBell) { nextBell = t + Random.Range(25f, 60f); At(bell, Around(70f, 130f), 0.7f, 30f, 260f); }
            if (t > nextSiren) { nextSiren = t + Random.Range(70f, 150f); StartCoroutine(PassingSiren()); }
        }

        Vector3 Around(float min, float max)
        {
            float a = Random.Range(0f, Mathf.PI * 2f), r = Random.Range(min, max);
            return new Vector3(listener.position.x + Mathf.Sin(a) * r, 2f, listener.position.z + Mathf.Cos(a) * r);
        }

        AudioSource At(AudioClip clip, Vector3 pos, float vol, float minDist, float maxDist)
        {
            if (clip == null) return null;
            var go = new GameObject("Sfx_" + clip.name);
            go.transform.position = pos;
            var s = go.AddComponent<AudioSource>();
            s.clip = clip; s.volume = vol; s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = minDist; s.maxDistance = maxDist; s.dopplerLevel = 1f;
            s.Play();
            Destroy(go, clip.length + 0.2f);
            return s;
        }

        // a siren crosses the city ~250 m away
        System.Collections.IEnumerator PassingSiren()
        {
            if (siren == null) yield break;
            float a = Random.Range(0f, Mathf.PI * 2f);
            var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            var side = new Vector3(-dir.z, 0, dir.x) * 250f;
            var from = listener.position + side - dir * 250f; var to = listener.position + side + dir * 250f;
            var s = At(siren, from, 0.9f, 60f, 600f);
            for (float t = 0; s != null && t < siren.length; t += Time.deltaTime)
            {
                s.transform.position = Vector3.Lerp(from, to, t / siren.length);
                yield return null;
            }
        }

        void OnFootstep()
        {
            if (!steps.TryGetValue(Indoors && !Underground ? "wood" : "concrete", out var set) || set.Length == 0) return;
            int i = Random.Range(0, set.Length);
            if (i == lastStep && set.Length > 1) i = (i + 1) % set.Length;   // never the same sample twice in a row
            lastStep = i;
            float run = player != null ? Mathf.Clamp01(player.HorizontalSpeed / player.sprintSpeed) : 0.5f;
            oneShot.pitch = Random.Range(0.92f, 1.08f);
            oneShot.PlayOneShot(set[i], footsteps * Mathf.Lerp(0.6f, 1.1f, run) * Random.Range(0.85f, 1f));
        }

        public void PhoneRinging(bool on) { if (on && !ring.isPlaying) { ring.volume = phone; ring.Play(); } else if (!on) ring.Stop(); }
        public void MessagePing() { if (ping != null) oneShot.PlayOneShot(ping, phone); }
        public void ElevatorDing() { if (ding != null) oneShot.PlayOneShot(ding, 0.55f); }
    }
}
