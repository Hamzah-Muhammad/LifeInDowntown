using System;
using MinniGameFramework.Combat;
using MinniGameFramework.Core;
using MinniGameFramework.NPC;
using UnityEngine.InputSystem;
using UnityEngine;

namespace MinniGameFramework.Player
{
    // First-person body (docs/DESIGN.md 3B.1, 3R). Owns movement and look angles; BodycamRig turns those into
    // the eye-camera pose. Movement for a stealth / crime game (3R):
    //   walk by default; Shift runs, and held over 1.2 s going forward it becomes a sprint
    //   C toggles sneaking: crouched, slow, quiet (Shift or jump stands him up)
    //   hold Alt + A / D to peek left / right round a corner (the head leans out, the feet stay; never into a wall)
    // Hostiles read NoiseRadius (how far his feet carry), Sneaking, and HeadPoint / ChestPoint (what of him shows).
    // All movement goes through Step() so the editor tests drive it with fake input.
    [RequireComponent(typeof(CharacterController))]
    public class BodycamPlayer : MonoBehaviour
    {
        [Header("Input (template InputSystem_Actions, map 'Player')")]
        public InputActionAsset actions;

        [Header("Speeds (m/s)")]
        public float walkSpeed = 1.6f;     // default
        public float jogSpeed = 4.0f;      // Shift: run
        public float sprintSpeed = 5.5f;   // Shift held 1.2 s going forward
        public float crouchSpeed = 1.0f;   // sneaking
        public float sprintAfter = 1.2f;

        [Header("Stealth (DESIGN 3R)")]
        public float leanDistance = 0.4f, leanRoll = 12f;
        public float noiseSneak = 1.5f, noiseWalk = 6f, noiseRun = 15f, noiseSprint = 18f;   // metres a hostile hears his feet
        public float speedScale = 1f;      // PlayerStats: an overloaded backpack slows Tarik down
        public bool canSprint = true;
        public bool moveEnabled = true;    // off while the inventory or a shop is open
        public float acceleration = 9f;    // m/s^2 towards the target speed
        public float jumpSpeed = 3.6f;
        public float gravity = -9.81f;

        [Header("Look")]
        public bool lookEnabled = true;         // off while the phone is up (mouse taps the phone instead)
        public float lookSensitivity = 0.08f;   // degrees per mouse count
        public float pitchLimit = 80f;

        [Header("Body")]
        public float standHeight = 1.75f;
        public float crouchHeight = 1.15f;

        public float Yaw { get; private set; }
        public float Pitch { get; private set; }
        public float StepPhase { get; private set; }          // radians; one footstep per pi
        public float HorizontalSpeed { get; private set; }
        public bool Grounded { get; private set; }
        public float HeightRatio => cc != null ? cc.height / standHeight : 1f;
        public bool Sneaking { get; private set; }
        public bool Sprinting { get; private set; }
        public float Lean { get; private set; }          // -1 left .. 1 right
        public float LeanOffset { get; private set; }    // metres the head is out to the side (stopped short of walls)
        public float NoiseRadius => !Grounded || HorizontalSpeed < 0.25f ? 0f : Sneaking ? noiseSneak
                                    : HorizontalSpeed <= walkSpeed + 0.4f ? noiseWalk : Sprinting ? noiseSprint : noiseRun;
        // what a watcher can see of him: the head goes where the eyes are (leaning out), the chest stays behind
        public Vector3 HeadPoint => transform.position + Vector3.up * (1.62f * HeightRatio) + transform.right * LeanOffset;
        public Vector3 ChestPoint => transform.position + Vector3.up * (1.3f * HeightRatio) + transform.right * (LeanOffset * 0.25f);
        public event Action Footstep;
        public event Action<float> Landed;                    // impact speed (m/s)

        CharacterController cc;
        InputAction move, look, sprint, crouch, jump;
        Vector3 horizontal;
        float vertical, runHeld;

        void Awake()
        {
            Init();
            if (actions != null)
            {
                var map = actions.FindActionMap("Player", true);
                move = map.FindAction("Move"); look = map.FindAction("Look");
                sprint = map.FindAction("Sprint"); crouch = map.FindAction("Crouch"); jump = map.FindAction("Jump");
                map.Enable();
            }
        }

        public void Init()
        {
            if (cc != null) return;
            cc = GetComponent<CharacterController>();
            Yaw = transform.eulerAngles.y;
        }

        // used by loading a save / starting a new game
        public void SetLook(float yaw, float pitch)
        {
            Yaw = yaw;
            Pitch = Mathf.Clamp(pitch, -pitchLimit, pitchLimit);
            transform.rotation = Quaternion.Euler(0f, Yaw, 0f);
        }

        void OnEnable()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void Update()
        {

            Vector2 m = move?.ReadValue<Vector2>() ?? Vector2.zero;
            Vector2 l = lookEnabled ? look?.ReadValue<Vector2>() ?? Vector2.zero : Vector2.zero;
            var kb = Keyboard.current;
            float lean = 0f;
            if (kb != null && (kb.leftAltKey.isPressed || kb.rightAltKey.isPressed))   // Alt + A / D peeks; the feet stay put
            {
                lean = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
                m.x = 0f;
            }
            if (moveEnabled && (crouch?.WasPressedThisFrame() ?? false)) ToggleSneak();
            Step(m, l, sprint?.IsPressed() ?? false, Sneaking, jump?.WasPressedThisFrame() ?? false, lean, Time.deltaTime);
        }

        public void ToggleSneak() => Sneaking = !Sneaking;
        public void SetSneak(bool on) => Sneaking = on;

        // the old signature (tests): `crouching` holds sneaking on while true; `walking` is the default now
        public void Step(Vector2 moveIn, Vector2 lookDelta, bool sprinting, bool crouching, bool jumping, bool walking, float dt)
        {
            if (crouching) Sneaking = true;
            Step(moveIn, lookDelta, sprinting, Sneaking, jumping, 0f, dt);
        }

        public void Step(Vector2 moveIn, Vector2 lookDelta, bool running, bool sneaking, bool jumping, float lean, float dt)
        {
            Init();
            if (dt <= 0f) return;

            Yaw += lookDelta.x * lookSensitivity;
            Pitch = Mathf.Clamp(Pitch - lookDelta.y * lookSensitivity, -pitchLimit, pitchLimit);
            transform.rotation = Quaternion.Euler(0f, Yaw, 0f);

            if (!moveEnabled) { moveIn = Vector2.zero; jumping = false; lean = 0f; }
            Sneaking = sneaking;
            if (Sneaking && (running || jumping)) Sneaking = false;   // standing up to run or jump
            runHeld = running && moveIn.y > 0.1f ? runHeld + dt : 0f;
            Sprinting = !Sneaking && canSprint && runHeld >= sprintAfter;
            bool crouching = Sneaking;
            float target = Sneaking ? crouchSpeed : Sprinting ? sprintSpeed : running && canSprint ? jogSpeed : walkSpeed;
            target *= speedScale;
            Peek(lean, dt);
            Vector3 wish = transform.TransformDirection(new Vector3(moveIn.x, 0f, moveIn.y));
            wish = Vector3.ClampMagnitude(wish, 1f) * target;
            horizontal = Vector3.MoveTowards(horizontal, wish, acceleration * dt);

            if (Grounded && vertical < 0f) vertical = -2f;        // keep the controller pressed to the ground
            if (Grounded && jumping && !crouching) vertical = jumpSpeed;
            vertical += gravity * dt;
            float fallSpeed = -vertical;

            // actual velocity from the position change (cc.velocity is always zero outside play mode)
            Vector3 startPos = transform.position;
            Vector3 carry = Grounded ? FrameworkHooks.GroundCarry(transform.position) : Vector3.zero;   // DESIGN 3N.4
            var flags = cc.Move((horizontal + Vector3.up * vertical + carry) * dt);
            Vector3 actual = (transform.position - startPos) / dt;
            bool groundedNow = (flags & CollisionFlags.Below) != 0;
            if ((flags & CollisionFlags.Sides) != 0)
                horizontal = Vector3.ProjectOnPlane(actual, Vector3.up);   // walls kill speed into them
            if (groundedNow && !Grounded && fallSpeed > 2.5f) Landed?.Invoke(fallSpeed);
            Grounded = groundedNow;

            HorizontalSpeed = new Vector3(actual.x, 0f, actual.z).magnitude;
            if (Grounded && HorizontalSpeed > 0.1f)
            {
                float stride = Mathf.Lerp(0.65f, 1.7f, HorizontalSpeed / sprintSpeed);   // metres per step
                float before = StepPhase;
                StepPhase += HorizontalSpeed * dt / stride * Mathf.PI;
                if (Mathf.Floor(before / Mathf.PI) != Mathf.Floor(StepPhase / Mathf.PI)) Footstep?.Invoke();
            }

            float h = Mathf.MoveTowards(cc.height, crouching ? crouchHeight : standHeight, 3f * dt);
            if (!crouching && h > cc.height && Physics.SphereCast(transform.position + Vector3.up * (cc.height - cc.radius), cc.radius * 0.9f, Vector3.up, out _, h - cc.height + 0.05f, ~0, QueryTriggerInteraction.Ignore))
                h = cc.height;   // no standing up into a low ceiling
            cc.height = h;
            cc.center = Vector3.up * (h * 0.5f);
        }

        // leaning: eases out to the side, stopping 12 cm short of anything solid there
        void Peek(float lean, float dt)
        {
            Lean = Mathf.MoveTowards(Lean, Mathf.Clamp(lean, -1f, 1f), dt * 5f);
            float want = Lean * leanDistance;
            if (Mathf.Abs(want) > 0.001f)
            {
                Vector3 head = transform.position + Vector3.up * (1.55f * HeightRatio);
                Vector3 side = transform.right * Mathf.Sign(want);
                if (Physics.SphereCast(head, 0.12f, side, out var hit, Mathf.Abs(want), ~0, QueryTriggerInteraction.Ignore) && !hit.transform.IsChildOf(transform))
                    want = Mathf.Sign(want) * Mathf.Max(0f, hit.distance - 0.02f);
            }
            LeanOffset = want;
        }
    }
}
