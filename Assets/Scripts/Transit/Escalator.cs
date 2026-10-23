using System.Collections.Generic;
using MinniGameFramework.Player;
using UnityEngine;

namespace LifeInDowntown.Transit
{
    // A running escalator (docs/DESIGN.md 3N.4): anyone standing in its box is carried along it.
    // The box is in the transform's frame (z = up the slope's run); BodycamPlayer asks CarryAt every step.
    public class Escalator : MonoBehaviour
    {
        public Vector3 size = new Vector3(1.2f, 5.5f, 7f);
        public Vector3 velocity;        // world m/s along the steps (up or down)

        public static readonly List<Escalator> All = new List<Escalator>();
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public bool Contains(Vector3 world)
        {
            var p = transform.InverseTransformPoint(world);
            return Mathf.Abs(p.x) <= size.x * 0.5f && Mathf.Abs(p.y) <= size.y * 0.5f && Mathf.Abs(p.z) <= size.z * 0.5f;
        }

        public static Vector3 CarryAt(Vector3 world)
        {
            if (All.Count == 0) EnsureRegistered();
            foreach (var e in All) if (e.Contains(world)) return e.velocity;
            return Vector3.zero;
        }

        // OnEnable doesn't run for scene objects in edit mode (the batch tests), so look them up once
        static void EnsureRegistered()
        {
            if (Application.isPlaying) return;
            foreach (var e in FindObjectsByType<Escalator>(FindObjectsSortMode.None)) if (!All.Contains(e)) All.Add(e);
        }
    }
}
