using System;
using LifeInDowntown.Game;
using UnityEngine;

namespace LifeInDowntown.World
{
    // Pantages Tower's concierge desk (docs/DESIGN.md 3O.2): greets Tarik once each time he comes through the
    // lobby (by time of day), and E at the desk gets a line: small talk, building notices, and where his car is.
    public class Concierge : Interactable
    {
        public float greetRange = 6f;
        public static Func<string> CarWhere;        // set by the garage: "Your car's down in P1-17." etc.

        static readonly string[] small =
        {
            "Quiet night. Well, quiet for Shuter Street.",
            "Package room's closed till seven, sorry.",
            "Elevator two's been slow all week. Management says they're on it.",
            "Someone's been buzzing 3801's intercom. I didn't let them up.",
            "Stay dry out there.",
        };
        int next;
        bool inRange;

        public override string Prompt => "Talk to the concierge";

        void Update()
        {
            var p = GameFlow.Instance != null && GameFlow.Instance.player != null ? GameFlow.Instance.player.transform : null;
            if (p == null) return;
            bool near = Vector3.Distance(p.position, transform.position) < greetRange && Mathf.Abs(p.position.y - transform.position.y) < 2.5f;
            if (near && !inRange) Say(Greeting());
            inRange = near;
        }

        public override void Interact()
        {
            string car = CarWhere?.Invoke();
            string line = car != null && next % 3 == 1 ? car : small[next % small.Length];
            next++;
            Say(line);
        }

        static string Greeting()
        {
            int h = DateTime.Now.Hour;
            string part = h < 5 ? "Late one, Tarik?" : h < 12 ? "Morning, Tarik." : h < 18 ? "Afternoon, Tarik." : "Evening, Tarik.";
            return part;
        }

        static void Say(string line) => LifeInDowntown.Phone.PhoneController.Instance?.ShowBanner("CONCIERGE", line);
    }
}
