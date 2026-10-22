using UnityEngine;

namespace LifeInDowntown.Game
{
    // Slow cinematic drift down the street behind the main menu. Runs on unscaled time.
    public class MenuCamera : MonoBehaviour
    {
        public Vector3 from, to;           // set by PlayerSetup along Yonge
        public float secondsPerPass = 70f;

        void LateUpdate()
        {
            float t = Mathf.SmoothStep(0f, 1f, Mathf.PingPong(Time.unscaledTime / secondsPerPass, 1f));
            Vector3 dir = (to - from).normalized;
            float s = Time.unscaledTime;
            var look = Quaternion.LookRotation(dir) *
                       Quaternion.Euler(-2f + Mathf.Sin(s * 0.17f) * 1.2f, Mathf.Sin(s * 0.11f) * 5f, Mathf.Sin(s * 0.07f) * 0.6f);
            transform.SetPositionAndRotation(Vector3.Lerp(from, to, t), look);
        }
    }
}
