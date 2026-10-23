using LifeInDowntown.Game;
using LifeInDowntown.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LifeInDowntown.Phone
{
    // Face-to-face conversations (docs/DESIGN.md 3O.1): when Tarik reaches someone waiting for a meeting
    // objective, their dialogue plays as subtitles at the bottom of the screen, with the same 1-3 replies
    // and Space to continue as a phone call. Tarik stands still until it's over; then the objective completes.
    public class InPersonTalk : MonoBehaviour
    {
        public MissionManager missions;
        public BodycamPlayer player;

        DialogueRunner runner;
        GUIStyle line, speaker, choice, hint;
        Texture2D shade;

        public bool Talking => runner != null && runner.Active;
        public static bool AnyTalking { get; private set; }

        void OnEnable()
        {
            runner = new DialogueRunner(a => missions.Apply(a));
            runner.Ended += Ended;
            if (missions != null) missions.MeetStarted += Begin;
        }

        void OnDisable() { if (missions != null) missions.MeetStarted -= Begin; }

        void Begin(Objective obj)
        {
            if (!missions.Data.Dialogues.TryGetValue(obj.talk, out var d)) { missions.MeetEnded(); return; }
            if (player != null) player.moveEnabled = false;
            runner.Begin(d);
        }

        void Ended()
        {
            if (player != null) player.moveEnabled = true;
            missions.MeetEnded();
        }

        void Update()
        {
            AnyTalking = Talking;
            if (!Talking || Keyboard.current == null) return;
            bool playing = GameFlow.Instance == null || GameFlow.Instance.State == GameFlow.FlowState.Playing;
            if (!playing) return;
            var kb = Keyboard.current;
            var node = runner.Node;
            if (node == null) return;
            if (node.choices.Length == 0) { if (kb.spaceKey.wasPressedThisFrame) runner.Continue(); }   // not E: that's Interactor's
            else
                for (int i = 0; i < Mathf.Min(3, node.choices.Length); i++)
                    if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) { runner.Choose(i); break; }
        }

        void OnGUI()
        {
            if (!Talking || Event.current.type != EventType.Repaint || runner.Node == null) return;
            float k = Screen.height / 1080f;
            if (line == null)
            {
                line = new GUIStyle { wordWrap = true, alignment = TextAnchor.UpperCenter, normal = { textColor = new Color(0.93f, 0.93f, 0.9f) } };
                speaker = new GUIStyle(line) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.75f, 0.43f) } };
                choice = new GUIStyle(line) { alignment = TextAnchor.UpperLeft };
                hint = new GUIStyle(line) { normal = { textColor = new Color(0.93f, 0.93f, 0.9f, 0.5f) } };
                shade = new Texture2D(1, 1); shade.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.55f)); shade.Apply();
            }
            line.fontSize = choice.fontSize = Mathf.RoundToInt(28f * k);
            speaker.fontSize = Mathf.RoundToInt(24f * k);
            hint.fontSize = Mathf.RoundToInt(18f * k);
            var n = runner.Node;
            float w = Screen.width * 0.56f, x = (Screen.width - w) / 2f, y = Screen.height * 0.70f;
            float h = (110f + n.choices.Length * 40f) * k;
            GUI.DrawTexture(new Rect(x - 24f * k, y - 18f * k, w + 48f * k, h + 30f * k), shade);
            GUI.Label(new Rect(x, y, w, 30f * k), (n.speaker ?? "").ToUpperInvariant(), speaker);
            GUI.Label(new Rect(x, y + 34f * k, w, 70f * k), DialogueRunner.Fill(n.text), line);
            float cy = y + 108f * k;
            for (int i = 0; i < n.choices.Length && i < 3; i++)
            {
                GUI.Label(new Rect(x + 40f * k, cy, w - 40f * k, 36f * k), $"{i + 1}.  {DialogueRunner.Fill(n.choices[i].text)}", choice);
                cy += 40f * k;
            }
            if (n.choices.Length == 0) GUI.Label(new Rect(x, cy - 4f * k, w, 26f * k), "SPACE", hint);
        }
    }
}
