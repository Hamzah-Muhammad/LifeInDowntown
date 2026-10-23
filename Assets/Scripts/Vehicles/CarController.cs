using UnityEngine;

namespace LifeInDowntown.Vehicles
{
    // A drivable car (docs/DESIGN.md 3O.4): Rigidbody on four WheelColliders, rear-wheel drive.
    // Step() takes the controls so the test can drive it in edit mode; PlayerDriving feeds it the keyboard.
    // Parked with nobody in it, the car brakes and goes kinematic so it sits still and costs nothing.
    [RequireComponent(typeof(Rigidbody))]
    public class CarController : MonoBehaviour
    {
        public static CarController PlayerCar { get; private set; }   // Tarik's own car
        public bool isPlayerCar;

        [Header("E46 330i-ish")]
        public float mass = 1450f;
        public float driveTorque = 1150f;     // Nm at each rear wheel at full throttle (gearing folded in)
        public float reverseTorque = 500f;
        public float brakeTorque = 2000f;     // ~0.8 g, about a real E46
        public float handbrakeTorque = 4000f;
        public float maxSteer = 33f;          // degrees at walking pace, less at speed
        public float topSpeed = 50f;          // m/s (180 km/h)

        public WheelCollider[] wheels;        // FL, FR, RL, RR
        public Transform[] visuals;           // their meshes
        public AudioSource engine;

        Rigidbody rb;
        bool occupied;
        float stoppedFor;          // time held at a standstill on the brake before reverse engages

        public float Speed => rb != null ? Vector3.Dot(rb.linearVelocity, transform.forward) : 0f;   // m/s, + forward
        public float Kmh => Mathf.Abs(Speed) * 3.6f;
        public Rigidbody Body => rb;

        void Awake() => Init();

        public void Init()
        {
            rb = GetComponent<Rigidbody>();
            rb.mass = mass;
            rb.centerOfMass = new Vector3(0f, 0.42f, 0.05f);   // low, slightly forward like an inline-6 up front
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            if (isPlayerCar) PlayerCar = this;
            Park(true);
        }

        public void SetOccupied(bool on)
        {
            occupied = on;
            Park(!on);
        }

        // parked: brakes on, kinematic so it neither drifts nor costs physics time
        void Park(bool parked)
        {
            if (rb == null) return;
            rb.isKinematic = parked;
            foreach (var w in wheels) if (w != null) { w.motorTorque = 0f; w.brakeTorque = parked ? handbrakeTorque : 0f; }
            if (engine != null) { if (parked) engine.Stop(); else if (!engine.isPlaying) engine.Play(); }
        }

        // throttle -1..1 (negative = brake, then reverse once stopped), steer -1..1
        public void Step(float throttle, float steer, bool handbrake, float dt = -1f)
        {
            if (dt < 0f) dt = Time.fixedDeltaTime;
            float v = Speed;
            float steerAngle = maxSteer * Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(Mathf.Abs(v) / 30f)) * steer;
            wheels[0].steerAngle = wheels[1].steerAngle = steerAngle;
            float drive = 0f, brake = 0f;
            if (throttle > 0.01f)
            {
                if (v < -0.5f) brake = brakeTorque * throttle;                       // rolling back: brake first
                else drive = v < topSpeed ? driveTorque * throttle * Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(v / topSpeed)) : 0f;
            }
            else if (throttle < -0.01f)
            {
                stoppedFor = v > 0.5f ? 0f : stoppedFor + dt;                       // rolling back counts as stopped
                if (v > 0.5f || stoppedFor < 0.35f) brake = brakeTorque * -throttle;   // brake, then a beat at a standstill
                else drive = -reverseTorque * -throttle;                             // still holding S: reverse
            }
            if (throttle >= -0.01f) stoppedFor = 0f;
            for (int i = 0; i < 4; i++)
            {
                wheels[i].motorTorque = i >= 2 ? drive : 0f;
                wheels[i].brakeTorque = brake + (handbrake && i >= 2 ? handbrakeTorque : 0f);
            }
            if (engine != null)
            {
                float rpm = Mathf.Lerp(0.55f, 1.9f, Mathf.Repeat(Mathf.Abs(v) / 14f, 1f) * 0.7f + Mathf.Clamp01(Mathf.Abs(v) / topSpeed) * 0.3f);
                engine.pitch = Mathf.Lerp(engine.pitch, Mathf.Max(0.55f, rpm + Mathf.Abs(throttle) * 0.15f), 0.1f);
                engine.volume = 0.35f + 0.35f * Mathf.Abs(throttle);
            }
        }

        void LateUpdate()
        {
            for (int i = 0; i < wheels.Length && i < visuals.Length; i++)
            {
                if (wheels[i] == null || visuals[i] == null) continue;
                wheels[i].GetWorldPose(out var p, out var q);
                visuals[i].SetPositionAndRotation(p, q);
            }
        }

        // the garage gate: put the car somewhere, stopped
        public void Place(Vector3 pos, Quaternion rot)
        {
            bool kin = rb.isKinematic;
            rb.isKinematic = true;
            transform.SetPositionAndRotation(pos, rot);
            rb.position = pos; rb.rotation = rot;
            if (!kin) { rb.isKinematic = false; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        }

        public bool Occupied => occupied;
    }
}
