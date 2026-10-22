using UnityEngine;

namespace LifeInDowntown.Player
{
    // Keeps the rain mesh (RainLook, docs/DESIGN.md 3A L3) around the camera and scrolls it downward.
    // A second copy stacked one box-height above makes the scroll wrap seamlessly.
    public class RainFollow : MonoBehaviour
    {
        public Transform target;          // the bodycam camera
        public float boxHeight = 22f;     // RainLook.RainBoxHeight
        public float fallSpeed = 11f;     // m/s
        public float belowTarget = 3f;    // mesh bottom sits this far below the camera
        public float IndoorAbove = 60f;   // camera height (m) above which the rain is hidden

        Transform upper;

        void Start()
        {
            var copy = new GameObject("RainUpper");
            copy.transform.SetParent(transform, false);
            copy.transform.localPosition = Vector3.up * boxHeight;
            copy.AddComponent<MeshFilter>().sharedMesh = GetComponent<MeshFilter>().sharedMesh;
            var r = copy.AddComponent<MeshRenderer>();
            r.sharedMaterial = GetComponent<MeshRenderer>().sharedMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            upper = copy.transform;
        }

        void LateUpdate()
        {
            if (target == null) target = Camera.main != null ? Camera.main.transform : null;
            if (target == null) return;
            // no rain indoors: everything above street level is inside (Tarik's condo is at ~120 m)
            bool indoors = target.position.y > IndoorAbove;
            foreach (var r in GetComponentsInChildren<MeshRenderer>()) r.enabled = !indoors;
            float scroll = -Mathf.Repeat(Time.time * fallSpeed, boxHeight);
            var p = target.position;
            transform.position = new Vector3(p.x, p.y - belowTarget + scroll, p.z);
        }
    }
}
