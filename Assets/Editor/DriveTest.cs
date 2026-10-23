using LifeInDowntown.City;
using LifeInDowntown.Game;
using LifeInDowntown.Vehicles;
using LifeInDowntown.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LifeInDowntown.EditorTools
{
    // Tarik's car and the garage (docs/DESIGN.md 3O.3, 3O.4), in edit mode with physics stepped by hand:
    // the car's parts, the gates, accelerating / braking / reversing / steering down the P1 aisle, getting
    // in and out, and the saved position coming back. Prints DRIVE_PASS / DRIVE_FAIL; exits 1 on failure.
    public static class DriveTest
    {
        public static void Run()
        {
            int fails = 0;
            void Check(bool ok, string what) { Debug.Log((ok ? "DRIVE_PASS " : "DRIVE_FAIL ") + what); if (!ok) fails++; }
            EditorSceneManager.OpenScene("Assets/Scenes/Downtown_YongeDundas.unity", OpenSceneMode.Single);
            var mode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            try
            {
                var carGo = GameObject.Find("Vehicles/TarikCar");
                Check(carGo != null, "Tarik's car is in the scene");
                if (carGo == null) return;
                var car = carGo.GetComponent<CarController>();
                Check(car != null && car.wheels.Length == 4 && System.Array.TrueForAll(car.wheels, w => w != null) && car.engine != null && car.engine.clip != null,
                      "four wheel colliders and an engine sound");
                Check(carGo.transform.Find("DriverSeat") != null && carGo.transform.Find("DriverDoor") != null, "driver's seat and door points");
                var spot = GameObject.Find($"PantagesGarage/GarageSpot_{CityGenerator.TarikBay}");
                Check(spot != null && Vector3.Distance(carGo.transform.position, spot.transform.position) < 1.5f, $"parked in {CityGenerator.TarikBay}");
                var gates = Object.FindObjectsByType<GarageGate>(FindObjectsSortMode.None);
                Check(gates.Length == 2 && System.Array.Exists(gates, g => g.exit.position.y < -3f) && System.Array.Exists(gates, g => g.exit.position.y > -0.5f),
                      $"two drive-through gates, street <-> P1 ({gates.Length})");

                // drive down the aisle from the roll-up door towards the elevator
                car.Init();
                var start = GameObject.Find("PantagesGarage/GarageGate_P1_Exit").transform;
                car.Place(start.position + Vector3.up * 0.1f, start.rotation);
                car.SetOccupied(true);
                void Sim(float seconds, float throttle, float steer, bool hand)
                {
                    for (float t = 0; t < seconds; t += 0.02f) { car.Step(throttle, steer, hand, 0.02f); Physics.Simulate(0.02f); }
                }
                Sim(1.0f, 0f, 0f, false);                        // settle on the suspension
                float y0 = carGo.transform.position.y;
                Check(Mathf.Abs(y0 - CityGenerator.GarageFloorY) < 0.25f, $"sits on the P1 floor (y={y0:F2})");
                Vector3 p0 = carGo.transform.position;
                Sim(3f, 1f, 0f, false);
                float v = car.Speed;
                Check(v > 5f && v < 20f, $"full throttle 3 s: {v * 3.6f:F0} km/h, {Vector3.Distance(p0, carGo.transform.position):F1} m");
                float stopAt = -1f;
                for (float t = 0; t < 3f && stopAt < 0f; t += 0.02f) { car.Step(-1f, 0f, false, 0.02f); Physics.Simulate(0.02f); if (Mathf.Abs(car.Speed) < 0.5f) stopAt = t; }
                Check(stopAt > 0f && stopAt < 3f, $"brakes stop it from {v * 3.6f:F0} km/h in {stopAt:F1} s");
                Sim(1.5f, -1f, 0f, false);
                Check(car.Speed < -0.4f, $"holding S once stopped reverses ({car.Speed * 3.6f:F1} km/h)");
                Sim(2f, -1f, 0f, false); Sim(1f, 0f, 0f, true);
                float yaw0 = carGo.transform.eulerAngles.y;
                Sim(2.5f, 0.45f, 1f, false);
                float turned = Mathf.Abs(Mathf.DeltaAngle(yaw0, carGo.transform.eulerAngles.y));
                Check(turned > 15f, $"steers: turned {turned:F0} degrees");
                Check(Vector3.Angle(carGo.transform.up, Vector3.up) < 10f, "still on its wheels");

                // getting in and out
                var drive = Object.FindFirstObjectByType<PlayerDriving>();
                var player = drive != null ? drive.player : null;
                Check(drive != null && player != null && drive.rig != null, "PlayerDriving is wired to the player and the chest cam");
                if (drive != null && player != null)
                {
                    Sim(1.5f, -1f, 0f, true);
                    car.SetOccupied(false);
                    var door = carGo.transform.Find("DriverDoor");
                    var cc = player.GetComponent<CharacterController>();
                    cc.enabled = false; player.transform.position = door.position + carGo.transform.right * -0.3f; cc.enabled = true;
                    Check(drive.NearDoor(out var near) && near == car, "standing at the driver's door, F would get in");
                    drive.GetIn(car);
                    Check(drive.Driving && player.transform.parent == carGo.transform && !player.enabled && !drive.rig.enabled, "in the car: body rides along, chest cam in the seat");
                    drive.GetOut();
                    Check(!drive.Driving && player.transform.parent == null && player.enabled && cc.enabled
                          && Vector3.Distance(player.transform.position, door.position) < 1.5f, "out on the driver's side");
                }

                // the garage remembers where the car was left
                GameState.NewGame();
                var s = GameState.Current;
                s.carSaved = true; s.carInGarage = false; s.carPos = new Vector3(100f, 0.05f, -225f); s.carYaw = 73f;
                var keep = carGo.GetComponent<CarPersistence>();
                Check(keep != null && keep.homeSpot != null, "car persistence knows the home bay");
                typeof(CarPersistence).GetMethod("Apply", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(keep, new object[] { s });
                Check(Vector3.Distance(carGo.transform.position, s.carPos) < 0.01f, "a save with the car out on the street puts it there");
                s.carInGarage = true;
                typeof(CarPersistence).GetMethod("Apply", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(keep, new object[] { s });
                Check(Vector3.Distance(carGo.transform.position, spot.transform.position) < 1.5f, "a save with the car in the garage puts it back in P1-17");
            }
            finally
            {
                Physics.simulationMode = mode;
                Debug.Log(fails == 0 ? "DRIVE_RESULT all checks passed" : $"DRIVE_RESULT {fails} check(s) FAILED");
                if (Application.isBatchMode) EditorApplication.Exit(fails == 0 ? 0 : 1);
            }
        }
    }
}
