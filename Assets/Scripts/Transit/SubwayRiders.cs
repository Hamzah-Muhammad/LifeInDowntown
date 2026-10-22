using UnityEngine;

namespace LifeInDowntown.Transit
{
    // The people on the trains and platforms (docs/DESIGN.md 3N.6): Rocketbox riders sitting, on their phones,
    // waiting. They only exist for Tarik while he is underground; on the street they're switched off, so the
    // street crowd keeps its frame budget.
    public class SubwayRiders : MonoBehaviour
    {
        public GameObject[] riders;
        float next;

        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;
            var cam = Camera.main;
            if (cam != null) Apply(cam.transform.position);
        }

        public void Apply(Vector3 cam)
        {
            bool on = cam.y < LightBudget.UndergroundBelow;
            foreach (var r in riders) if (r != null && r.activeSelf != on) r.SetActive(on);
        }
    }
}
