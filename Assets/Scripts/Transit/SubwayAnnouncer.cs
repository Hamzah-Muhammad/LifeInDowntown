using UnityEngine;

namespace LifeInDowntown.Transit
{
    // On-screen announcements and the next-train line (docs/DESIGN.md 3N.7): no voice, the real TTC wording as
    // subtitles at the bottom of the screen. Also draws the zone-out fade.
    public class SubwayAnnouncer : MonoBehaviour
    {
        public static SubwayAnnouncer Instance { get; private set; }

        public string Line { get; private set; } = "";
        public string Status { get; private set; } = "";   // "1 Finch - arriving" on a platform, "[Enter] Zone out" aboard
        int statusFrame = -10;
        public float Fade { get; set; }
        float until;
        GUIStyle style, shadow, small;

        void OnEnable() => Instance = this;

        public static void Say(string text, float seconds = 6f)
        {
            var a = Instance != null ? Instance : FindFirstObjectByType<SubwayAnnouncer>();
            if (a == null) return;
            Instance = a;
            a.Line = text;
            a.until = (Application.isPlaying ? Time.time : 0f) + seconds;
            Debug.Log($"SUBWAY_ANNOUNCE {text}");
        }

        // trains set this every frame it applies; it disappears a few frames after they stop
        public void SetStatus(string text) { Status = text; statusFrame = Time.frameCount; }

        void Update()
        {
            if (Time.frameCount - statusFrame > 3) Status = "";
            if (Line.Length > 0 && Time.time > until) Line = "";
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (Fade > 0f)
            {
                var c = GUI.color; GUI.color = new Color(0, 0, 0, Fade);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = c;
            }
            if (style == null)
            {
                style = new GUIStyle { alignment = TextAnchor.MiddleCenter, wordWrap = true, normal = { textColor = new Color(1f, 0.85f, 0.35f) } };
                shadow = new GUIStyle(style) { normal = { textColor = new Color(0, 0, 0, 0.85f) } };
                small = new GUIStyle(style) { normal = { textColor = new Color(0.92f, 0.92f, 0.92f) } };
            }
            float k = Screen.height / 1080f;
            style.fontSize = shadow.fontSize = Mathf.RoundToInt(30f * k);
            small.fontSize = Mathf.RoundToInt(22f * k);
            if (Line.Length > 0)
            {
                var r = new Rect(Screen.width * 0.15f, Screen.height * 0.80f, Screen.width * 0.7f, 80f * k);
                var sr = r; sr.x += 2; sr.y += 2;
                GUI.Label(sr, Line, shadow);
                GUI.Label(r, Line, style);
            }
            if (Status.Length > 0)
            {
                var r = new Rect(Screen.width * 0.15f, Screen.height * 0.89f, Screen.width * 0.7f, 40f * k);
                var sr = r; sr.x += 2; sr.y += 2;
                GUI.Label(sr, Status, shadow);
                GUI.Label(r, Status, small);
            }
        }
    }
}
