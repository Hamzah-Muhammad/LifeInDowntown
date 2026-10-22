using System.Collections.Generic;
using UnityEngine;

namespace LifeInDowntown.World
{
    // A box that counts as indoors (docs/DESIGN.md 3K): no rain inside, indoor ambience. Rabba stores use it;
    // the condo is covered by the height rule (above 60 m). Oriented by its transform; size is the full box.
    public class IndoorZone : MonoBehaviour
    {
        public Vector3 size = new Vector3(8f, 3.5f, 10f);
        public static readonly List<IndoorZone> All = new List<IndoorZone>();

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public bool Contains(Vector3 world)
        {
            var local = transform.InverseTransformPoint(world);
            return Mathf.Abs(local.x) <= size.x * 0.5f && Mathf.Abs(local.y) <= size.y * 0.5f && Mathf.Abs(local.z) <= size.z * 0.5f;
        }

        public const float CondoAbove = 60f;
        public const float UndergroundBelow = -1.5f;   // the subway (docs/DESIGN.md 3N)

        public static bool IsIndoors(Vector3 world)
        {
            if (world.y > CondoAbove || world.y < UndergroundBelow) return true;
            foreach (var z in All) if (z.Contains(world)) return true;
            return false;
        }
    }
}
