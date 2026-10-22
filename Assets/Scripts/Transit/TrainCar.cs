using UnityEngine;

namespace LifeInDowntown.Transit
{
    // One Toronto Rocket car (docs/DESIGN.md 3N.6). Local frame: z = direction of travel, x = right, y = up from
    // the rail top. Doors open on the right (+x), the platform side for both directions under Yonge. The leaves
    // slide along z; their closed positions are stored so the controller can animate them.
    public class TrainCar : MonoBehaviour
    {
        public const float Width = 3.137f, FloorY = 1.05f, CeilingY = 3.2f, InnerHalf = 1.42f;

        public float length = 22.86f;
        public float offsetFromHead;        // car centre's distance behind the train's front, metres
        public Transform[] rightLeaves;     // platform side
        public Vector3[] rightClosed;
        public float[] rightSlide;          // +1 / -1: which way each leaf slides along z when opening
        public float[] doorZ;               // door centres along the car

        public void SetDoors(float open01)
        {
            if (rightLeaves == null) return;
            for (int i = 0; i < rightLeaves.Length; i++)
                if (rightLeaves[i] != null)
                    rightLeaves[i].localPosition = rightClosed[i] + new Vector3(0.04f * Mathf.Clamp01(open01 * 4f), 0f, rightSlide[i] * 0.74f * open01);
        }

        // inside the car body (a standing passenger), in world space
        public bool Contains(Vector3 world)
        {
            var p = transform.InverseTransformPoint(world);
            return Mathf.Abs(p.x) < InnerHalf + 0.15f && p.y > FloorY - 0.4f && p.y < CeilingY + 0.2f && Mathf.Abs(p.z) < length * 0.5f + 0.2f;
        }
    }
}
