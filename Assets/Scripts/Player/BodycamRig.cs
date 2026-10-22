using UnityEngine;

namespace LifeInDowntown.Player
{
    // The chest-mounted camera (docs/DESIGN.md 3B.1). Lags behind the look angles like a camera strapped
    // to a body, bobs and rolls with each footstep, jitters slightly, and kicks on landing.
    // Lives on the camera object; positions itself in world space every LateUpdate.
    public class BodycamRig : MonoBehaviour
    {
        public BodycamPlayer player;

        [Header("Mount")]
        public float mountHeight = 1.35f;     // chest, at full standing height
        public float mountForward = 0.15f;

        [Header("Lag")]
        public float yawStiffness = 9f;       // higher = tighter follow
        public float pitchStiffness = 11f;
        public float turnLean = 0.12f;        // roll degrees per degree of lag

        [Header("Footstep motion")]
        public float bobVertical = 0.04f;     // metres at jog speed
        public float bobLateral = 0.02f;
        public float rollSway = 1.5f;         // degrees
        public float sprintBobScale = 1.6f;

        [Header("Noise")]
        public float jitterDegrees = 0.12f;
        public float jitterFrequency = 7f;

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
            float scale = moving * Mathf.Lerp(1f, sprintBobScale, sprintAmt) * (player.Grounded ? 1f : 0.3f);
            float ph = player.StepPhase;
            var bob = new Vector3(Mathf.Sin(ph) * bobLateral, -Mathf.Abs(Mathf.Sin(ph)) * bobVertical, 0f) * scale;
            float roll = Mathf.Sin(ph) * rollSway * scale - lag * turnLean;

            // spring-damped landing kick
            kickVel += (-kickSpring * kick - kickDamping * kickVel) * dt;
            kick += kickVel * dt;

            noiseT += dt * jitterFrequency;
            float jp = (Mathf.PerlinNoise(noiseT, 0.3f) - 0.5f) * 2f * jitterDegrees * (1f + scale);
            float jy = (Mathf.PerlinNoise(0.7f, noiseT) - 0.5f) * 2f * jitterDegrees * (1f + scale);

            var bodyRot = Quaternion.Euler(0f, CamYaw, 0f);
            LastBobOffset = bob;
            transform.position = player.transform.position
                                 + Vector3.up * (mountHeight * player.HeightRatio)
                                 + bodyRot * (Vector3.forward * mountForward + bob);
            transform.rotation = Quaternion.Euler(CamPitch + kick + jp, CamYaw + jy, roll);
        }
    }
}
