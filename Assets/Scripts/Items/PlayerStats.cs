using LifeInDowntown.Game;
using LifeInDowntown.Player;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace LifeInDowntown.Items
{
    // Body stats (docs/DESIGN.md 3M), in real time while playing: hunger empties in ~2 h, thirst in ~1.5 h,
    // faster while sprinting. Empty hunger or thirst drains health; both above half heals slowly. Low stats
    // wash the colour out and close the vignette in. At zero health Tarik collapses and wakes up at home,
    // a little poorer. The backpack's weight slows him down past the comfortable limit.
    public class PlayerStats : MonoBehaviour
    {
        public BodycamPlayer player;
        public float hungerMinutes = 120f, thirstMinutes = 90f;     // full to empty, walking
        public float starveDrain = 6f, healPerMinute = 3f;           // health points per minute
        public float regenDelay = 8f;                                // no healing this long after a hit (DESIGN 3P.2)

        public static PlayerStats Instance { get; private set; }
        public float SinceHit { get; private set; } = 999f;
        public int Collapses { get; private set; }
        // HurtFeedback listens: a hit (damage, where it came from) and a bullet cracking past
        public static event System.Action<float, Vector3> Hurt;
        public static event System.Action<Vector3> NearMissed;

        // damage by kind (DESIGN 3P.2): bullets fall off with range, punches are light
        public static float BulletDamage(float distance) => Mathf.Lerp(30f, 18f, Mathf.InverseLerp(5f, 40f, distance));
        public static float PunchDamage() => Random.Range(6f, 10f);

        Volume volume;
        ColorAdjustments colour;
        Vignette vignette;
        bool warnedHunger, warnedThirst, warnedHealth;

        static GameState S => GameState.Current;

        void Awake() { Instance = this; }

        // Tarik is hit: health down, the regen clock restarts; at zero he goes down and wakes at home
        public void TakeDamage(float amount, Vector3 from)
        {
            if (amount <= 0f || S.health <= 0f) return;
            S.health = Mathf.Max(0f, S.health - amount);
            SinceHit = 0f;
            Hurt?.Invoke(amount, from);
            if (S.health <= 0f) Collapse("YOU WENT DOWN");
        }

        public void NearMiss(Vector3 at) => NearMissed?.Invoke(at);

        void Start()
        {
            // a runtime profile layered over the bodycam look; its weight is how rough Tarik feels
            var go = new GameObject("StatsVolume");
            go.transform.SetParent(transform, false);
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true; volume.priority = 30f; volume.weight = 0f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            colour = profile.Add<ColorAdjustments>(true);
            colour.saturation.Override(-70f);
            colour.postExposure.Override(-0.4f);
            vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.6f);
            vignette.smoothness.Override(0.6f);
            volume.sharedProfile = profile;
        }

        void Update()
        {
            bool playing = GameFlow.Instance == null || GameFlow.Instance.State == GameFlow.FlowState.Playing;
            if (player != null)
            {
                bool heavy = Inventory.Weight(S) > Inventory.Comfortable;
                player.speedScale = heavy ? 0.65f : 1f;
                player.canSprint = !heavy;
            }
            if (!playing) return;
            Tick(Time.deltaTime, player != null && player.HorizontalSpeed > player.jogSpeed + 0.3f);
            float rough = Mathf.Max(Mathf.InverseLerp(25f, 0f, Mathf.Min(S.hunger, S.thirst)), Mathf.InverseLerp(40f, 5f, S.health));
            if (volume != null) volume.weight = Mathf.MoveTowards(volume.weight, rough, Time.deltaTime * 0.2f);
        }

        // one step of the body clock (the test drives this directly)
        public void Tick(float dt, bool sprinting)
        {
            float minutes = dt / 60f, effort = sprinting ? 2.5f : 1f;
            SinceHit += dt;
            S.hunger = Mathf.Max(0f, S.hunger - 100f / hungerMinutes * minutes * effort);
            S.thirst = Mathf.Max(0f, S.thirst - 100f / thirstMinutes * minutes * effort);
            if (S.hunger <= 0f || S.thirst <= 0f) S.health -= starveDrain * minutes;
            else if (S.hunger > 50f && S.thirst > 50f && SinceHit >= regenDelay) S.health = Mathf.Min(100f, S.health + healPerMinute * minutes);

            Warn(ref warnedHunger, S.hunger < 20f, S.hunger > 30f, "HUNGRY", "Tarik needs to eat something.");
            Warn(ref warnedThirst, S.thirst < 20f, S.thirst > 30f, "THIRSTY", "Grab a drink.");
            Warn(ref warnedHealth, S.health < 30f, S.health > 40f, "HURTING", "Health is low. Eat, drink, or use a first aid kit.");
            if (S.health <= 0f) Collapse("YOU COLLAPSED");
        }

        static void Warn(ref bool warned, bool low, bool recovered, string title, string body)
        {
            if (low && !warned) { warned = true; LifeInDowntown.Phone.PhoneController.Instance?.ShowBanner(title, body); }
            else if (recovered) warned = false;
        }

        // weapons are kept (holstered); 15% of the cash is gone
        void Collapse(string title)
        {
            float lost = Mathf.Round(S.cash * 0.15f * 100f) / 100f;
            S.cash -= lost;
            S.health = 35f; S.hunger = Mathf.Max(S.hunger, 30f); S.thirst = Mathf.Max(S.thirst, 30f);
            S.drawn = false;
            SinceHit = 999f;
            Collapses++;
            GameFlow.Instance?.WakeAtHome();
            LifeInDowntown.Phone.PhoneController.Instance?.ShowBanner(title, $"Woke up at home. {ItemCatalog.Money(lost)} missing from your wallet.");
            if (volume != null) volume.weight = 0.6f;
        }
    }
}
