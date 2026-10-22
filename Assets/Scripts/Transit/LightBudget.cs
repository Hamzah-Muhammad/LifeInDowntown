using System.Collections.Generic;
using UnityEngine;

namespace LifeInDowntown.Transit
{
    // HDRP caps how many lights reach one screen cell. The 602 streetlights (30 m range) reach well below the
    // pavement, so underground they crowded out the station lights (docs/DESIGN.md 3N.5). Below the street only
    // the city lights near the camera stay on; on the street only the station lights near the camera do.
    public class LightBudget : MonoBehaviour
    {
        public const float UndergroundBelow = -1.5f;
        public const float CityLightRadiusBelow = 30f;     // streetlights kept on near a stair top
        public const float SubwayLightRadiusAbove = 35f;   // station lights kept on near an opening

        static readonly List<Light> subway = new List<Light>(), city = new List<Light>();
        static Transform cachedRoot;
        float next;

        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.25f;
            var cam = Camera.main;
            if (cam != null) Apply(transform, cam.transform.position);
        }

        public static void Apply(Transform subwayRoot, Vector3 cam)
        {
            if (cachedRoot != subwayRoot || subway.Count == 0)
            {
                cachedRoot = subwayRoot;
                subway.Clear(); city.Clear();
                foreach (var l in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (l.type == LightType.Directional) continue;
                    (l.transform.IsChildOf(subwayRoot) ? subway : city).Add(l);
                }
            }
            bool below = cam.y < UndergroundBelow;
            foreach (var l in subway)
                if (l != null) l.enabled = below || Flat(l.transform.position - cam) < SubwayLightRadiusAbove;
            foreach (var l in city)
                if (l != null) l.enabled = !below || (l.transform.position - cam).magnitude < CityLightRadiusBelow;
        }

        static float Flat(Vector3 d) => new Vector2(d.x, d.z).magnitude;
    }
}
