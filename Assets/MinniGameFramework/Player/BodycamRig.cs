using MinniGameFramework.Combat;
using MinniGameFramework.Core;
using MinniGameFramework.NPC;
using UnityEngine;

namespace MinniGameFramework.Player
{
    // The chest-mounted camera (docs/DESIGN.md 3B.1). Lags behind the look angles like a camera strapped
    // to a body, bobs and rolls with each footstep, jitters slightly, and kicks on landing.
    // Lives on the camera object; positions itself in world space every LateUpdate.
    public class BodycamRig : MonoBehaviour
    {
        public BodycamPlayer player;

        [Header("Mount")]
        public float mountHeight = 1.62f;     // eye level, at full standing height (first person; was a chest mount)
        public float mountForward = 0.10f;    // the eyes, ahead of the body's centre

        // 2026-10-22: Hamzah found the first values too shaky; bob/sway/jitter roughly halved
        [Header("Overall (Settings > Camera shake)")]
        [Range(0f, 1.5f)] public float shakeScale = 1f;

        [Header("Lag")]
        public float yawStiffness = 10f;      // higher = tighter follow
        public float pitchStiffness = 12f;
        public float turnLean = 0.07f;        // roll degrees per degree of lag

        [Header("Footstep motion")]
        public float bobVertical = 0.025f;    // metres at jog speed (was 0.04)
        public float bobLateral = 0.012f;     // was 0.02
        public float rollSway = 0.8f;         // degrees (was 1.5)
        public float sprintBobScale = 1.35f;  // was 1.6

        [Header("Noise")]
        public float jitterDegrees = 0.06f;   // was 0.12
        public float jitterFrequency = 6f;

        [Header("Landing")]
        public float landingKickPerMs = 1.2f; // degrees of pitch per m/s of impact
        public float kickSpring = 60f, kickDamping = 9f;

        public float CamYaw { get; private set; }
        public float CamPitch { get; private set; }
        public Vector3 LastBobOffset { get; private set; }

        float kick, kickVel, noiseT;
        bool initialised;

        void OnEnable()
        {
            if (player != null) player.Landed += OnLanded;
        }

        void OnDisable()
        {
            if (player != null) player.Landed -= OnLanded;
        }

        void OnLanded(float speed) => kickVel += speed * landingKickPerMs * 8f;

        // recoil: the view jumps up by about this many degrees and springs back (WeaponHolder)
        public void Kick(float degrees) => kickVel -= degrees * 8f;

        // jump straight to the player's look (after a teleport / load), no lag swing
        public void Snap()
        {
            initialised = false;
            kick = kickVel = 0f;
        }

        void LateUpdate() => Tick(Time.deltaTime);

        public void Tick(float dt)
        {
            if (player == null) return;
            if (!initialised) { CamYaw = player.Yaw; CamPitch = player.Pitch; initialised = true; }
            if (dt <= 0f) dt = 1f / 60f;

            CamYaw = Mathf.LerpAngle(CamYaw, player.Yaw, 1f - Mathf.Exp(-yawStiffness * dt));
            CamPitch = Mathf.Lerp(CamPitch, player.Pitch, 1f - Mathf.Exp(-pitchStiffness * dt));
            float lag = Mathf.DeltaAngle(CamYaw, player.Yaw);

            // footstep bob: dips on each step, sways side to side once per stride
            float moving = Mathf.Clamp01(player.HorizontalSpeed / player.jogSpeed);
            float sprintAmt = Mathf.Clamp01((player.HorizontalSpeed - player.jogSpeed) / (player.sprintSpeed - player.jogSpeed));
            float scale = moving * Mathf.Lerp(1f, sprintBobScale, sprintAmt) * (player.Grounded ? 1f : 0.3f) * shakeScale;
            float ph = player.StepPhase;
            var bob = new Vector3(Mathf.Sin(ph) * bobLateral, -Mathf.Abs(Mathf.Sin(ph)) * bobVertical, 0f) * scale;
            float roll = Mathf.Sin(ph) * rollSway * scale - lag * turnLean * shakeScale;

            // spring-damped landing kick
            kickVel += (-kickSpring * kick - kickDamping * kickVel) * dt;
            kick += kickVel * dt;

            noiseT += dt * jitterFrequency;
            float jAmt = jitterDegrees * shakeScale * (1f + scale);
            float jp = (Mathf.PerlinNoise(noiseT, 0.3f) - 0.5f) * 2f * jAmt;
            float jy = (Mathf.PerlinNoise(0.7f, noiseT) - 0.5f) * 2f * jAmt;

            var bodyRot = Quaternion.Euler(0f, CamYaw, 0f);
            LastBobOffset = bob;
            // peeking (DESIGN 3R): the eyes slide out to the side and dip a touch, the view tilts with the lean
            float leanOut = player.leanDistance > 0f ? player.LeanOffset / player.leanDistance : 0f;
            transform.position = player.transform.position
                                 + Vector3.up * (mountHeight * player.HeightRatio - Mathf.Abs(leanOut) * 0.05f)
                                 + bodyRot * (Vector3.forward * mountForward + Vector3.right * player.LeanOffset + bob);
            transform.rotation = Quaternion.Euler(CamPitch + kick * shakeScale + jp, CamYaw + jy, roll - leanOut * player.leanRoll);
        }
    }
}
