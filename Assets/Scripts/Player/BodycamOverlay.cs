using System;
using LifeInDowntown.Game;
using UnityEngine;

namespace LifeInDowntown.Player
{
    // Recording timestamp, top right (docs/DESIGN.md 3B.1). The top-left device ID and REC dot were
    // removed at Hamzah's request (2026-10-22). Hidden while a menu is open.
    public class BodycamOverlay : MonoBehaviour
    {
        public float referenceFontSize = 24f;   // at 1080p

        GUIStyle text, shadow;

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (GameFlow.Instance != null && GameFlow.Instance.State != GameFlow.FlowState.Playing) return;
            if (text == null)
            {
                var mono = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Lucida Console", "Courier New" }, 24);
                text = new GUIStyle { font = mono, alignment = TextAnchor.UpperRight, normal = { textColor = new Color(0.95f, 0.95f, 0.92f) } };
                shadow = new GUIStyle(text) { normal = { textColor = new Color(0f, 0f, 0f, 0.7f) } };
            }
            int size = Mathf.RoundToInt(referenceFontSize * Screen.height / 1080f);
            text.fontSize = shadow.fontSize = size;
            float pad = size * 0.9f;

            var offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
            string stamp = DateTime.Now.ToString("yyyy-MM-dd  HH:mm:ss") + "  " +
                           (offset < TimeSpan.Zero ? "-" : "+") + offset.ToString("hhmm");
            var r = new Rect(Screen.width - size * 22f - pad, pad, size * 22f, size * 1.4f);
            var sr = r; sr.x += 2; sr.y += 2;
            GUI.Label(sr, stamp, shadow);
            GUI.Label(r, stamp, text);
        }
    }
}
