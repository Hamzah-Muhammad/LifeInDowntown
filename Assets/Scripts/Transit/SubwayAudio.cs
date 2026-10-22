using LifeInDowntown.Player;
using UnityEngine;

namespace LifeInDowntown.Transit
{
    // Subway sound (docs/DESIGN.md 3N.8), all code-generated clips (make_sounds.py subway_*):
    //   station hum while underground; a muffled interior bed while riding;
    //   each train carries 3D rumble, wheels and motor whine that follow its speed (the whine rises with it);
    //   wind pushed down the tunnel before an arriving train; chime, doors and brake hiss from the train's cues.
    public class SubwayAudio : MonoBehaviour
    {
        public float hum = 0.35f, interior = 0.5f, wind = 0.45f, trainBed = 0.9f, cues = 0.8f;

        AudioSource humSrc, interiorSrc, windSrc;
        TrainController[] trains;
        AudioSource[][] trainSrc;      // per train: rumble, wheels, motor (on the front car), cue source (middle car)
        AudioClip chime, doors, brake;
        Transform listener;
        BodycamPlayer player;

        static AudioClip Clip(string n) => Resources.Load<AudioClip>("Audio/" + n);

        AudioSource Bed(string clip)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = Clip(clip); s.loop = true; s.volume = 0f; s.playOnAwake = false; s.spatialBlend = 0f;
            if (s.clip != null) s.Play();
            return s;
        }

        static AudioSource On(GameObject go, string clip, bool loop, float min, float max)
        {
            var s = go.AddComponent<AudioSource>();
            s.clip = clip != null ? Clip(clip) : null; s.loop = loop; s.playOnAwake = false; s.volume = 0f;
            s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = min; s.maxDistance = max; s.dopplerLevel = 0.6f;
            if (loop && s.clip != null) { s.time = Random.Range(0f, s.clip.length); s.Play(); }
            return s;
        }

        void Start()
        {
            player = FindFirstObjectByType<BodycamPlayer>();
            var cam = Camera.main;
            listener = cam != null ? cam.transform : player != null ? player.transform : null;
            humSrc = Bed("subway_hum"); interiorSrc = Bed("subway_interior"); windSrc = Bed("subway_wind");
            chime = Clip("subway_chime"); doors = Clip("subway_doors"); brake = Clip("subway_brake");
            trains = GetComponentsInChildren<TrainController>();
            trainSrc = new AudioSource[trains.Length][];
            for (int i = 0; i < trains.Length; i++)
            {
                var tr = trains[i];
                var front = tr.cars[0].gameObject; var mid = tr.cars[tr.cars.Length / 2].gameObject;
                trainSrc[i] = new[] { On(front, "subway_rumble", true, 10f, 160f), On(front, "subway_wheels", true, 6f, 110f),
                                      On(front, "subway_motor", true, 6f, 90f), On(mid, null, false, 6f, 70f) };
                trainSrc[i][3].volume = 1f;
                var cueSrc = trainSrc[i][3];
                tr.Cue += c => OnCue(cueSrc, c);
            }
            Debug.Log($"SUBWAY_AUDIO_OK trains={trains.Length} beds={(humSrc.clip ? 1 : 0) + (interiorSrc.clip ? 1 : 0) + (windSrc.clip ? 1 : 0)}/3 cues={(chime ? 1 : 0) + (doors ? 1 : 0) + (brake ? 1 : 0)}/3");
        }

        void OnCue(AudioSource s, string cue)
        {
            switch (cue)
            {
                case "chime": if (chime) s.PlayOneShot(chime, cues); break;
                case "doors_open": case "doors_close": if (doors) s.PlayOneShot(doors, cues * 0.7f); break;
                case "arrive": if (brake) s.PlayOneShot(brake, cues * 0.6f); break;
            }
        }

        void Update()
        {
            if (listener == null) return;
            bool under = listener.position.y < LightBudget.UndergroundBelow;
            bool aboard = false; float aboardSpeed = 0f, windNear = 0f;
            for (int i = 0; i < trains.Length; i++)
            {
                var tr = trains[i];
                if (tr.PlayerAboard) { aboard = true; aboardSpeed = tr.speed; }
                float k = Mathf.Clamp01(tr.speed / tr.maxSpeed);
                float inside = tr.PlayerAboard ? 0.35f : 1f;          // heard through the car body
                var src = trainSrc[i];
                src[0].volume = trainBed * inside * Mathf.Lerp(0.15f, 1f, k) * (tr.state == TrainController.State.Hidden ? 0f : 1f);
                src[1].volume = trainBed * inside * 0.8f * k;
                src[2].volume = trainBed * inside * 0.45f * Mathf.Clamp01(k * 3f) * (1f - 0.5f * k);
                src[2].pitch = 0.55f + 0.9f * k;                       // the rising whine
                // wind ahead of a train coming into the player's station
                if (under && player != null && !tr.PlayerAboard && tr.state == TrainController.State.Running && tr.speed > 2f)
                {
                    var st = tr.Stops[tr.stopIndex];
                    float d = Mathf.Abs(tr.StopHead(tr.stopIndex) - tr.headS);
                    if (st.Underground(player.transform.position) && d < 200f) windNear = Mathf.Max(windNear, 1f - d / 200f);
                }
            }
            float dt = Time.unscaledDeltaTime;
            humSrc.volume = Mathf.MoveTowards(humSrc.volume, under ? hum * (aboard ? 0.3f : 1f) : 0f, dt);
            interiorSrc.volume = Mathf.MoveTowards(interiorSrc.volume, aboard ? interior * Mathf.Lerp(0.35f, 1f, aboardSpeed / 25f) : 0f, dt);
            windSrc.volume = Mathf.MoveTowards(windSrc.volume, wind * windNear, dt);
        }
    }
}
