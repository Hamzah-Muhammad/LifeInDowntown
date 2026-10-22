using UnityEngine;

namespace LifeInDowntown.Player
{
    // Makes the whole mix sound recorded on a chest-mounted camera mic (docs/DESIGN.md 3I): band-limited
    // (high-pass ~150 Hz, low-pass ~8 kHz), compressed, lightly saturated, plus wind buffet that grows with
    // the player's speed outdoors. Runs on the audio thread on the AudioListener's GameObject.
    public class BodyMicFilter : MonoBehaviour
    {
        [Range(0f, 1f)] public float strength = 1f;   // 0 = clean mix
        public float highPassHz = 150f, lowPassHz = 8000f;
        public float thresholdDb = -18f, ratio = 3.5f, makeupDb = 6f;
        public float drive = 1.6f;
        public BodycamPlayer player;

        // set on the main thread, read on the audio thread
        volatile float windAmount;

        Biquad[] hp, lp;
        float env;
        System.Random noise = new System.Random(7);
        float windLp;

        struct Biquad
        {
            float b0, b1, b2, a1, a2, z1, z2;
            public static Biquad Make(bool high, float hz, float sr)
            {
                float w = 2f * Mathf.PI * hz / sr, c = Mathf.Cos(w), s = Mathf.Sin(w), alpha = s / (2f * 0.707f);
                float a0 = 1f + alpha;
                var q = new Biquad();
                if (high) { q.b0 = (1 + c) / 2 / a0; q.b1 = -(1 + c) / a0; q.b2 = (1 + c) / 2 / a0; }
                else { q.b0 = (1 - c) / 2 / a0; q.b1 = (1 - c) / a0; q.b2 = (1 - c) / 2 / a0; }
                q.a1 = -2 * c / a0; q.a2 = (1 - alpha) / a0;
                return q;
            }
            public float Run(float x)
            {
                float y = b0 * x + z1;
                z1 = b1 * x - a1 * y + z2;
                z2 = b2 * x - a2 * y;
                return y;
            }
        }

        void Awake()
        {
            float sr = AudioSettings.outputSampleRate;
            hp = new[] { Biquad.Make(true, highPassHz, sr), Biquad.Make(true, highPassHz, sr) };
            lp = new[] { Biquad.Make(false, lowPassHz, sr), Biquad.Make(false, lowPassHz, sr) };
        }

        void Update()
        {
            bool indoors = LifeInDowntown.World.IndoorZone.IsIndoors(transform.position);
            float speed = player != null && player.enabled ? player.HorizontalSpeed : 0f;
            windAmount = indoors ? 0f : Mathf.Clamp01((speed - 1.5f) / 4f) * 0.06f;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (hp == null || strength <= 0f) return;
            float sr = AudioSettings.outputSampleRate;
            float attack = Mathf.Exp(-1f / (0.005f * sr)), release = Mathf.Exp(-1f / (0.12f * sr));
            float thr = Mathf.Pow(10f, thresholdDb / 20f), makeup = Mathf.Pow(10f, makeupDb / 20f);
            float wind = windAmount;
            for (int i = 0; i < data.Length; i += channels)
            {
                // wind buffet: low rumbling noise, the same in both channels (it's one mic)
                float n = (float)(noise.NextDouble() * 2 - 1);
                windLp += (n - windLp) * 0.02f;
                float peak = 0f;
                for (int c = 0; c < channels; c++)
                {
                    int k = Mathf.Min(c, 1);
                    float x = data[i + c] + windLp * wind * 8f;
                    x = lp[k].Run(hp[k].Run(x));
                    data[i + c] = Mathf.Lerp(data[i + c], x, strength);
                    peak = Mathf.Max(peak, Mathf.Abs(data[i + c]));
                }
                env = peak > env ? attack * env + (1 - attack) * peak : release * env + (1 - release) * peak;
                float gain = env > thr ? Mathf.Pow(env / thr, 1f / ratio - 1f) : 1f;
                for (int c = 0; c < channels; c++)
                {
                    float y = data[i + c] * Mathf.Lerp(1f, gain * makeup, strength);
                    y = (float)System.Math.Tanh(y * drive) / (float)System.Math.Tanh(drive);   // light saturation
                    data[i + c] = Mathf.Lerp(data[i + c], y, strength);
                }
            }
        }
    }
}
