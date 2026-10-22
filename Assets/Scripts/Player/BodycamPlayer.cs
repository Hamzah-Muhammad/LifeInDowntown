using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LifeInDowntown.Player
{
    // First-person body for the bodycam view (docs/DESIGN.md 3B.1). Owns movement and look angles;
    // BodycamRig turns those into the shaky chest-camera pose. All movement goes through Step() so the
    // editor simulation test can drive it with fake input.
    [RequireComponent(typeof(CharacterController))]
    public class BodycamPlayer : MonoBehaviour
    {
        [Header("Input (template InputSystem_Actions, map 'Player')")]
        public InputActionAsset actions;

        [Header("Speeds (m/s)")]
        public float walkSpeed = 1.4f;     // hold Left Alt
        public float jogSpeed = 3.2f;      // default
        public float sprintSpeed = 5.5f;   // Sprint action
        public float crouchSpeed = 1.0f;
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
        public event Action Footstep;
        public event Action<float> Landed;                    // impact speed (m/s)

        CharacterController cc;
        InputAction move, look, sprint, crouch, jump;
        Vector3 horizontal;
        float vertical;

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
            bool walk = Keyboard.current != null && Keyboard.current.leftAltKey.isPressed;
            Step(m, l, sprint?.IsPressed() ?? false, crouch?.IsPressed() ?? false,
                 jump?.WasPressedThisFrame() ?? false, walk, Time.deltaTime);
        }

        public void Step(Vector2 moveIn, Vector2 lookDelta, bool sprinting, bool crouching, bool jumping, bool walking, float dt)
        {
            Init();
            if (dt <= 0f) return;

            Yaw += lookDelta.x * lookSensitivity;
            Pitch = Mathf.Clamp(Pitch - lookDelta.y * lookSensitivity, -pitchLimit, pitchLimit);
            transform.rotation = Quaternion.Euler(0f, Yaw, 0f);

            float target = crouching ? crouchSpeed : sprinting && moveIn.y > 0.1f ? sprintSpeed : walking ? walkSpeed : jogSpeed;
            Vector3 wish = transform.TransformDirection(new Vector3(moveIn.x, 0f, moveIn.y));
            wish = Vector3.ClampMagnitude(wish, 1f) * target;
            horizontal = Vector3.MoveTowards(horizontal, wish, acceleration * dt);

            if (Grounded && vertical < 0f) vertical = -2f;        // keep the controller pressed to the ground
            if (Grounded && jumping && !crouching) vertical = jumpSpeed;
            vertical += gravity * dt;
            float fallSpeed = -vertical;

            // actual velocity from the position change (cc.velocity is always zero outside play mode)
            Vector3 startPos = transform.position;
            var flags = cc.Move((horizontal + Vector3.up * vertical) * dt);
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
            cc.height = h;
            cc.center = Vector3.up * (h * 0.5f);
        }
    }
}
