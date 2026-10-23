using LifeInDowntown.City;
using UnityEditor;
using UnityEngine;

namespace LifeInDowntown.EditorTools
{
    // Cars in the scene (docs/DESIGN.md 3O.4): builds Tarik's black E46 from CarBuilder and parks it in his
    // bay P1-17 under Pantages. Called by CityGenerator.Generate after the garage exists.
    public static class VehicleSetup
    {
        public const string TarikCar = "Car_E46_Tarik";

        public static void Setup()
        {
            var old = GameObject.Find("Vehicles");
            if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject("Vehicles").transform;
            var prefab = CarBuilder.BuildPrefab(CarBuilder.E46(), new Color(0.015f, 0.016f, 0.018f), TarikCar, drivable: true);
            var spot = GameObject.Find($"PantagesGarage/GarageSpot_{CityGenerator.TarikBay}");
            if (spot == null) { Debug.LogWarning("CARS_WARN no garage spot for Tarik's car"); return; }
            var car = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            car.name = "TarikCar";
            var pose = LifeInDowntown.Vehicles.CarPersistence.HomePose(spot.transform, out var rot);
            car.transform.SetPositionAndRotation(pose, rot);
            car.GetComponent<LifeInDowntown.Vehicles.CarController>().isPlayerCar = true;
            car.AddComponent<LifeInDowntown.Vehicles.CarPersistence>().homeSpot = spot.transform;

            // the roll-up doors are drive-through gates (fade), one each way
            var g = GameObject.Find("PantagesGarage");
            void Gate(string at, string to, string prompt)
            {
                var a = g.transform.Find(at); var b = g.transform.Find(to);
                if (a == null || b == null) return;
                var gate = a.gameObject.AddComponent<LifeInDowntown.World.GarageGate>();
                gate.prompt = prompt; gate.exit = b; gate.radius = 4f;
            }
            Gate("GarageGate_Street", "GarageGate_P1_Exit", "Drive into P1 Parking");
            Gate("GarageGate_P1", "GarageGate_Street_Exit", "Drive out onto Shuter St");
            Debug.Log($"CARS_OK tarik at {car.transform.position}");
        }
    }
}
