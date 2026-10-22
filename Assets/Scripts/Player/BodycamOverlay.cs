using System;
using UnityEngine;

namespace LifeInDowntown.Player
{
    // Recording overlay (docs/DESIGN.md 3B.1): timestamp, blinking REC dot, generic device ID.
    // No real camera or police brand names.
    public class BodycamOverlay : MonoBehaviour
    {
        public string deviceId = "CAM 0417";
        public float referenceFontSize = 24f;   // at 1080p

        GUIStyle text, shadow;
        Font mono;

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (text == null)
            {
                mono = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Lucida Console", "Courier New" }, 24);
                text = new GUIStyle { font = mono, normal = { textColor = new Color(0.95f, 0.95f, 0.92f) } };
                shadow = new GUIStyle(text) { normal = { textColor = new Color(0f, 0f, 0f, 0.7f) } };
            }
            int size = Mathf.RoundToInt(referenceFontSize * Screen.height / 1080f);
            text.fontSize = shadow.fontSize = size;
            float pad = size * 0.9f;

            var offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
            string stamp = DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss") + "  " +
                           (offset < TimeSpan.Zero ? "-" : "+") + offset.ToString("hhmm");
            Label(new Rect(Screen.width - size * 22f - pad, pad, size * 22f, size * 1.4f), stamp, TextAnchor.UpperRight);
            Label(new Rect(pad, pad, size * 12f, size * 1.4f), deviceId, TextAnchor.UpperLeft);

            bool on = (Time.unscaledTime % 1.2f) < 0.7f;
            if (on)
            {
                var red = new GUIStyle(text) { normal = { textColor = new Color(0.9f, 0.1f, 0.1f) }, fontSize = size, alignment = TextAnchor.UpperLeft };
                GUI.Label(new Rect(pad, pad + size * 1.5f, size * 2f, size * 1.4f), "●", red);
            }
            Label(new Rect(pad + size * 1.4f, pad + size * 1.5f, size * 6f, size * 1.4f), "REC", TextAnchor.UpperLeft);
        }

        void Label(Rect r, string s, TextAnchor anchor)
        {
            text.alignment = shadow.alignment = anchor;
            var sr = r; sr.x += 2; sr.y += 2;
            GUI.Label(sr, s, shadow);
            GUI.Label(r, s, text);
        }
    }
}
