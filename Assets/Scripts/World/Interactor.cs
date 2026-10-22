using System.Collections;
using LifeInDowntown.Game;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LifeInDowntown.World
{
    // "Press E" interactions (docs/DESIGN.md 3H): shows the prompt for the nearest DoorLink in range and,
    // on E, fades to black, moves the player to the door's exit, and fades back in (the elevator ride).
    public class Interactor : MonoBehaviour
    {
        public Transform player;
        public float fadeSeconds = 0.45f;

        DoorLink near;
        float fade;          // 0 = clear, 1 = black
        bool busy;
        GUIStyle style;
        Texture2D black;

        void Update()
        {
            near = null;
            bool playing = GameFlow.Instance == null || GameFlow.Instance.State == GameFlow.FlowState.Playing;
            if (!playing || busy || player == null || LifeInDowntown.Map.MapController.IsOpen) return;
            float best = float.MaxValue;
            foreach (var d in DoorLink.All)
            {
                float dist = Vector3.Distance(Flat(d.transform.position), Flat(player.position));
                if (dist < d.radius && Mathf.Abs(d.transform.position.y - player.position.y) < 2.5f && dist < best) { best = dist; near = d; }
            }
            if (near != null && Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                StartCoroutine(Ride(near));
        }

        IEnumerator Ride(DoorLink door)
        {
            busy = true;
            for (float t = 0; t < fadeSeconds; t += Time.unscaledDeltaTime) { fade = t / fadeSeconds; yield return null; }
            fade = 1f;
            if (door.exit != null) GameFlow.Instance.Teleport(door.exit.position, door.exit.eulerAngles.y);
            AudioDirector.Instance?.ElevatorDing();
            yield return new WaitForSecondsRealtime(0.35f);
            for (float t = 0; t < fadeSeconds; t += Time.unscaledDeltaTime) { fade = 1f - t / fadeSeconds; yield return null; }
            fade = 0f;
            busy = false;
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            if (fade > 0f)
            {
                if (black == null) { black = new Texture2D(1, 1); black.SetPixel(0, 0, Color.black); black.Apply(); }
                var c = GUI.color; GUI.color = new Color(0, 0, 0, fade);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), black);
                GUI.color = c;
            }
            if (near == null || busy) return;
            if (style == null) style = new GUIStyle { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            style.fontSize = Mathf.RoundToInt(26f * Screen.height / 1080f);
            var r = new Rect(0, Screen.height * 0.62f, Screen.width, 40f * Screen.height / 1080f);
            var sr = r; sr.x += 2; sr.y += 2;
            var shadow = new GUIStyle(style) { normal = { textColor = new Color(0, 0, 0, 0.8f) } };
            string text = $"[E]  {near.prompt}";
            GUI.Label(sr, text, shadow);
            GUI.Label(r, text, style);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
