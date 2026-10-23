using LifeInDowntown.Game;
using UnityEngine;

namespace LifeInDowntown.Vehicles
{
    // Remembers where Tarik's car is (docs/DESIGN.md 3O.3): in his bay P1-17, or wherever he left it, kept in
    // GameState so it is saved. A new game or a loaded save puts the car back where that state says.
    [RequireComponent(typeof(CarController))]
    public class CarPersistence : MonoBehaviour
    {
        public Transform homeSpot;            // GarageSpot_P1-17 (the car's parked pose there)
        CarController car;
        GameState seen;
        float nextWrite;

        void Awake()
        {
            car = GetComponent<CarController>();
            LifeInDowntown.World.Concierge.CarWhere = () => GameState.Current.carInGarage
                ? "Your car's down in P1-17, safe and sound."
                : "Haven't seen your car come in tonight. Still out on the street?";
        }

        void Update()
        {
            var s = GameState.Current;
            if (s != seen) { seen = s; Apply(s); return; }      // new game / load
            if (Time.time < nextWrite) return;
            nextWrite = Time.time + 0.5f;
            s.carPos = transform.position; s.carYaw = transform.eulerAngles.y;
            s.carInGarage = transform.position.y < -1f;
            s.carSaved = true;
        }

        void Apply(GameState s)
        {
            if (s.carSaved && !s.carInGarage) car.Place(s.carPos, Quaternion.Euler(0f, s.carYaw, 0f));
            else if (homeSpot != null) car.Place(HomePose(homeSpot, out var rot), rot);
        }

        public static Vector3 HomePose(Transform spot, out Quaternion rot)
        {
            rot = spot.rotation;
            return spot.position + Vector3.up * 0.02f - spot.forward * 0.6f;   // nose 0.6 m short of the wheel stop
        }
    }
}
