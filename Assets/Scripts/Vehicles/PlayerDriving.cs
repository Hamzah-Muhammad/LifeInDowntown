using LifeInDowntown.Game;
using LifeInDowntown.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LifeInDowntown.Vehicles
{
    // Getting in and out of Tarik's car and driving it (docs/DESIGN.md 3O.4). F beside the driver's door gets
    // in; F again gets out on the driver's side. Driving, the chest cam sits in the driver's seat (the dash
    // fills the bottom of the frame) and the mouse looks around inside the car; W/S throttle and brake (reverse
    // once stopped), A/D steer, Space handbrake. The player's body rides along, parented to the car, so the
    // map, missions and E prompts follow the car.
    public class PlayerDriving : MonoBehaviour
    {
        public static PlayerDriving Instance { get; private set; }
        public BodycamPlayer player;
        public BodycamRig rig;
        public float doorRange = 2.2f;

        CarController car;
        Transform seat;
        float lookYaw, lookPitch;

        public bool Driving => car != null;
        public static bool PlayerIsDriving => Instance != null && Instance.Driving;
        public CarController Car => car;

        void Awake() { Instance = this; }

        bool Playing => GameFlow.Instance == null || GameFlow.Instance.State == GameFlow.FlowState.Playing;

        void Update()
        {
            var kb = Keyboard.current;
            if (!Playing || kb == null || LifeInDowntown.Items.InventoryUI.IsOpen || LifeInDowntown.Map.MapController.IsOpen) return;
            if (!kb.fKey.wasPressedThisFrame) return;
            if (Driving) GetOut();
            else if (NearDoor(out var c)) GetIn(c);
        }

        public bool NearDoor(out CarController c)
        {
            c = CarController.PlayerCar;
            if (c == null || player == null) return false;
            var door = c.transform.Find("DriverDoor");
            return door != null && Vector3.Distance(door.position, player.transform.position) < doorRange
                   && Mathf.Abs(door.position.y - player.transform.position.y) < 1.5f;
        }

        public void GetIn(CarController c)
        {
            car = c;
            seat = c.transform.Find("DriverSeat");
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.enabled = false;
            player.transform.SetParent(c.transform, true);
            player.transform.localPosition = seat.localPosition - Vector3.up * 1.0f;
            player.transform.localRotation = Quaternion.identity;
            if (rig != null) rig.enabled = false;
            lookYaw = lookPitch = 0f;
            c.SetOccupied(true);
        }

        public void GetOut()
        {
            if (car == null) return;
            var door = car.transform.Find("DriverDoor");
            Vector3 at = door.position;
            if (Physics.Raycast(at + Vector3.up * 1.5f, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore)) at = hit.point;
            car.Step(0f, 0f, true);
            car.SetOccupied(false);
            player.transform.SetParent(null, true);
            player.transform.SetPositionAndRotation(at + Vector3.up * 0.05f, Quaternion.Euler(0f, car.transform.eulerAngles.y, 0f));
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = true;
            player.enabled = true;
            player.SetLook(car.transform.eulerAngles.y - 90f, 0f);   // facing away from the door
            if (rig != null) { rig.enabled = true; rig.Snap(); }
            car = null; seat = null;
        }

        // leave the car without moving the body (the caller is about to place it): loading, new game, collapsing
        public void ForceOut()
        {
            if (car == null) return;
            car.Step(0f, 0f, true);
            car.SetOccupied(false);
            player.transform.SetParent(null, true);
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = true;
            player.enabled = true;
            if (rig != null) rig.enabled = true;
            car = null; seat = null;
        }

        void FixedUpdate()
        {
            if (car == null) return;
            var kb = Keyboard.current;
            bool live = Playing && kb != null && !LifeInDowntown.Items.InventoryUI.IsOpen;
            float throttle = 0f, steer = 0f;
            if (live)
            {
                throttle = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
                steer = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            }
            car.Step(throttle, steer, !live || kb.spaceKey.isPressed);
        }

        void LateUpdate()
        {
            if (car == null || seat == null || rig == null) return;
            if (Playing && Mouse.current != null && player.lookEnabled)
            {
                var d = Mouse.current.delta.ReadValue() * player.lookSensitivity;
                lookYaw = Mathf.Clamp(lookYaw + d.x, -110f, 110f);
                lookPitch = Mathf.Clamp(lookPitch - d.y, -50f, 40f);
            }
            // road feel: a little of the body's pitch and roll, plus engine shake with speed
            float shake = Mathf.PerlinNoise(Time.time * 9f, 0.3f) - 0.5f;
            var t = rig.transform;
            t.position = seat.position + car.transform.up * (shake * 0.004f * Mathf.Clamp01(car.Kmh / 60f));
            t.rotation = car.transform.rotation * Quaternion.Euler(lookPitch + 2f, lookYaw, 0f);
        }
    }
}
