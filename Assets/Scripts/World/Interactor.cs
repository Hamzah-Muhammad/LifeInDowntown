using System.Collections;
using LifeInDowntown.Game;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LifeInDowntown.World
{
    // "Press E" interactions (docs/DESIGN.md 3H, 3M): shows the prompt for the nearest DoorLink, pickup or
    // shop counter in range. Pickups and counters act at once on E; a door fades to black, moves the player
    // to the door's exit, and fades back in (the elevator ride).
    public class Interactor : MonoBehaviour
    {
        public Transform player;
        public float fadeSeconds = 0.45f;

        public static Interactor Instance { get; private set; }
        static readonly System.Collections.Generic.List<DoorLink> NoDoors = new System.Collections.Generic.List<DoorLink>();
        void Awake() { Instance = this; }

        DoorLink near;
        Interactable nearThing;     // pickups, shop counters (docs/DESIGN.md 3M)
        float fade;          // 0 = clear, 1 = black
        bool busy;
        GUIStyle style;
        Texture2D black;

        void Update()
        {
            near = null; nearThing = null;
            bool playing = GameFlow.Instance == null || GameFlow.Instance.State == GameFlow.FlowState.Playing;
            if (!playing || busy || player == null || LifeInDowntown.Map.MapController.IsOpen || LifeInDowntown.Items.InventoryUI.IsOpen
                || LifeInDowntown.Phone.InPersonTalk.AnyTalking) return;
            float best = float.MaxValue;
            bool driving = LifeInDowntown.Vehicles.PlayerDriving.PlayerIsDriving;   // doors are for people, gates for cars
            foreach (var d in driving ? NoDoors : DoorLink.All)
            {
                float dist = Vector3.Distance(Flat(d.transform.position), Flat(player.position));
                if (dist < d.radius && Mathf.Abs(d.transform.position.y - player.position.y) < 2.5f && dist < best) { best = dist; near = d; }
            }
            foreach (var t in Interactable.All)
            {
                if (!t.Available) continue;
                float dist = Vector3.Distance(Flat(t.transform.position), Flat(player.position));
                if (dist < t.radius && Mathf.Abs(t.transform.position.y - player.position.y) < 2.5f && dist < best) { best = dist; nearThing = t; near = null; }
            }
            bool e = Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
            if (e && nearThing != null) nearThing.Interact();
            else if (e && near != null) StartCoroutine(Ride(near));
        }

        // fade to black, do something while it's black (move the car, ...), fade back in
        public void Fade(System.Action whileBlack) { if (!busy) StartCoroutine(FadeRoutine(whileBlack)); }

        IEnumerator FadeRoutine(System.Action whileBlack)
        {
            busy = true;
            for (float t = 0; t < fadeSeconds; t += Time.unscaledDeltaTime) { fade = t / fadeSeconds; yield return null; }
            fade = 1f;
            whileBlack?.Invoke();
            yield return new WaitForSecondsRealtime(0.35f);
            for (float t = 0; t < fadeSeconds; t += Time.unscaledDeltaTime) { fade = 1f - t / fadeSeconds; yield return null; }
            fade = 0f;
            busy = false;
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
            if ((near == null && nearThing == null) || busy) return;
            if (style == null) style = new GUIStyle { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            style.fontSize = Mathf.RoundToInt(26f * Screen.height / 1080f);
            var r = new Rect(0, Screen.height * 0.62f, Screen.width, 40f * Screen.height / 1080f);
            var sr = r; sr.x += 2; sr.y += 2;
            var shadow = new GUIStyle(style) { normal = { textColor = new Color(0, 0, 0, 0.8f) } };
            string text = $"[E]  {(nearThing != null ? nearThing.Prompt : near.prompt)}";
            GUI.Label(sr, text, shadow);
            GUI.Label(r, text, style);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
