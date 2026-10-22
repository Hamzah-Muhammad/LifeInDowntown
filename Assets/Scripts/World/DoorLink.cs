using System.Collections.Generic;
using UnityEngine;

namespace LifeInDowntown.World
{
    // A door that moves the player to its exit (docs/DESIGN.md 3H): the Pantages street entrance and the
    // condo's front door are linked pairs ("elevator"). Interactor finds the nearest one and handles E.
    public class DoorLink : MonoBehaviour
    {
        public string prompt = "Enter";
        public Transform exit;
        public float radius = 1.8f;

        public static readonly List<DoorLink> All = new List<DoorLink>();

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);
    }
}
