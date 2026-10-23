using System;
using MinniGameFramework.Combat;
using MinniGameFramework.Core;
using MinniGameFramework.NPC;
using UnityEngine;

namespace MinniGameFramework.Player
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
            if (!FrameworkHooks.Playing()) return;
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

            // stealth state, bottom left (DESIGN 3R): sneaking, peeking
            if (player == null) player = FindFirstObjectByType<BodycamPlayer>();
            if (player == null) return;
            string state = player.Sneaking ? "SNEAKING" : "";
            if (Mathf.Abs(player.Lean) > 0.3f) state += (state.Length > 0 ? "  ·  " : "") + (player.Lean < 0f ? "< PEEK" : "PEEK >");
            if (state.Length == 0) return;
            var sr2 = new Rect(pad + 2, Screen.height - pad - size * 1.4f + 2, size * 16f, size * 1.4f);
            var r2 = new Rect(pad, Screen.height - pad - size * 1.4f, size * 16f, size * 1.4f);
            var left = new GUIStyle(text) { alignment = TextAnchor.LowerLeft }; var leftShadow = new GUIStyle(shadow) { alignment = TextAnchor.LowerLeft };
            GUI.Label(sr2, state, leftShadow);
            GUI.Label(r2, state, left);
        }

        BodycamPlayer player;
    }
}
