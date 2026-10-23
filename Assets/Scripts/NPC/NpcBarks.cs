using LifeInDowntown.Game;
using MinniGameFramework.NPC;
using UnityEngine;

namespace LifeInDowntown.NPC
{
    // Toronto street talk (docs/DESIGN.md 3O): now and then, someone Tarik walks right past says a line, shown
    // above their head for a few seconds (no voices: game sound is code-made). Lines depend on who they are on
    // the street. One line at a time, a few seconds apart, and the same person won't talk again for a while.
    public class NpcBarks : MonoBehaviour
    {
        public Transform player;
        public float range = 3.6f, chance = 0.45f, gap = 7f, perPerson = 45f;

        static readonly string[] walker =
        {
            "TTC's delayed again, I swear.", "Bro, it's mad wet out here.", "Tim's or nah?", "I'm so late, fam.",
            "Raps game tonight?", "Bare traffic on Yonge today.", "This rain is not it.", "Ahlie, I'm coming, I'm coming.",
            "Who's tryna get shawarma?", "Leafs gonna choke again, watch.", "Rent downtown is actually crazy.",
            "Oh, sorry! Sorry.", "Excuse me, sorry.", "My Presto's not tapping, man.", "Mans said ten minutes an hour ago.",
            "You see the Eaton Centre's open late?", "Dundas is TMU now? Since when?", "Styll, I'm not walking to Union in this.",
        };
        static readonly string[] corner = { "Wagwan.", "You good, fam?", "Keep it moving.", "Who you looking for?", "Ahlie.", "Mans is watching you, you know.", "Say less." };
        static readonly string[] homeless = { "Spare some change, bro?", "God bless, man.", "Anything helps.", "Cold one tonight, eh.", "You got a smoke?", "Stay dry, young man." };
        static readonly string[] police = { "Evening.", "Keep it moving, folks.", "Everything alright here?", "Watch yourself tonight." };
        static readonly string[] waiting = { "Come on, light.", "Is this where the 505 stops?", "Bro said five minutes twenty minutes ago.", "Why is this light so long." };
        static readonly string[] clerk = { "Hi, how are you?", "Let me know if you need anything.", "Cash or tap?" };

        Pedestrian speaker;
        string line;
        float until, next;

        void Update()
        {
            if (player == null) return;
            bool playing = GameFlow.Instance == null || GameFlow.Instance.State == GameFlow.FlowState.Playing;
            if (!playing || Time.time < next || LifeInDowntown.Vehicles.PlayerDriving.PlayerIsDriving) return;
            next = Time.time + 0.5f;
            if (Time.time < until + gap) return;
            foreach (var p in Pedestrian.All)
            {
                if (p.role == Pedestrian.Role.Story) continue;
                var d = p.transform.position - player.position;
                if (d.sqrMagnitude > range * range || Mathf.Abs(d.y) > 2f) continue;
                if (lastSpoke.TryGetValue(p, out float t) && Time.time - t < perPerson) continue;
                lastSpoke[p] = Time.time;
                if (Random.value > chance) continue;
                var pool = Pool(p.role);
                speaker = p; line = pool[Random.Range(0, pool.Length)]; until = Time.time + 3.5f;
                return;
            }
        }

        readonly System.Collections.Generic.Dictionary<Pedestrian, float> lastSpoke = new System.Collections.Generic.Dictionary<Pedestrian, float>();

        static string[] Pool(Pedestrian.Role r) => r switch
        {
            Pedestrian.Role.Corner => corner,
            Pedestrian.Role.Homeless => homeless,
            Pedestrian.Role.Police => police,
            Pedestrian.Role.Waiting => waiting,
            Pedestrian.Role.Clerk => clerk,
            _ => walker,
        };

        public static string[] LinesFor(Pedestrian.Role r) => Pool(r);   // for the test

        GUIStyle style, shadow;

        void OnGUI()
        {
            if (speaker == null || Time.time > until || Event.current.type != EventType.Repaint || Camera.main == null) return;
            var sp = Camera.main.WorldToScreenPoint(speaker.transform.position + Vector3.up * 2.05f);
            if (sp.z < 0f) return;
            float k = Screen.height / 1080f;
            if (style == null)
            {
                style = new GUIStyle { alignment = TextAnchor.MiddleCenter, wordWrap = true, normal = { textColor = new Color(0.95f, 0.95f, 0.92f) } };
                shadow = new GUIStyle(style) { normal = { textColor = new Color(0f, 0f, 0f, 0.85f) } };
            }
            style.fontSize = shadow.fontSize = Mathf.RoundToInt(22f * k);
            var r = new Rect(sp.x - 220f * k, Screen.height - sp.y - 30f * k, 440f * k, 60f * k);
            var rs = r; rs.x += 2f; rs.y += 2f;
            GUI.Label(rs, line, shadow);
            GUI.Label(r, line, style);
        }
    }
}
