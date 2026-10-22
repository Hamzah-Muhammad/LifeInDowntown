using UnityEngine;

namespace LifeInDowntown.World
{
    // One intersection's signals (docs/DESIGN.md 3J): approaches are split into two axes that alternate
    // green -> amber -> all-red. Each head has red/amber/green lamp renderers swapped between lit and dark
    // materials. Offset staggers neighbouring intersections.
    public class TrafficSignal : MonoBehaviour
    {
        [System.Serializable]
        public class Head { public int axis; public Renderer red, amber, green; }

        public Head[] heads = new Head[0];
        public Material redOn, amberOn, greenOn, lensOff;
        public float green = 22f, amber = 4f, allRed = 2f, offset;

        int lastPhase = -1;

        void Update()
        {
            float cycle = 2f * (green + amber + allRed);
            float t = Mathf.Repeat(Time.time + offset, cycle);
            int axis = t < cycle / 2f ? 0 : 1;
            float local = t % (cycle / 2f);
            int state = local < green ? 0 : local < green + amber ? 1 : 2;   // 0 green, 1 amber, 2 all red
            int phase = axis * 3 + state;
            if (phase == lastPhase) return;
            lastPhase = phase;
            foreach (var h in heads)
            {
                bool mine = h.axis == axis;
                Set(h.green, mine && state == 0 ? greenOn : lensOff);
                Set(h.amber, mine && state == 1 ? amberOn : lensOff);
                Set(h.red, !mine || state == 2 ? redOn : lensOff);
            }
        }

        static void Set(Renderer r, Material m) { if (r != null && r.sharedMaterial != m) r.sharedMaterial = m; }
    }
}
