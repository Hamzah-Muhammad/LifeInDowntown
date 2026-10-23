using LifeInDowntown.Game;
using LifeInDowntown.Vehicles;
using UnityEngine;

namespace LifeInDowntown.World
{
    // The Pantages garage's roll-up doors (docs/DESIGN.md 3O.3), for cars only: drive up to one and E fades out,
    // moves the car (with Tarik in it) to the other side and fades back in, the way the elevator does.
    public class GarageGate : Interactable
    {
        public string prompt = "Drive into P1 Parking";
        public Transform exit;

        public override string Prompt => prompt;
        public override bool Available => PlayerDriving.PlayerIsDriving && exit != null;

        public override void Interact()
        {
            var car = PlayerDriving.Instance.Car;
            if (car == null || Interactor.Instance == null) return;
            Interactor.Instance.Fade(() =>
            {
                car.Place(exit.position + Vector3.up * 0.05f, exit.rotation);
                GameState.Current.carInGarage = exit.position.y < -1f;
            });
        }
    }
}
