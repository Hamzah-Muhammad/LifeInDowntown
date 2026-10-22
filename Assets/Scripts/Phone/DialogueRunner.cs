using System;
using LifeInDowntown.Game;

namespace LifeInDowntown.Phone
{
    // Steps through one Dialogue: shows a node, applies its actions, follows next/choices until the end.
    public class DialogueRunner
    {
        public Dialogue Current { get; private set; }
        public DialogueNode Node { get; private set; }
        public bool Active => Current != null;
        public event Action Changed;
        public event Action Ended;

        readonly Action<string> applyAction;

        public DialogueRunner(Action<string> applyAction) { this.applyAction = applyAction; }

        public void Begin(Dialogue d)
        {
            Current = d;
            Go(d.nodes.Length > 0 ? d.nodes[0].id : null);
        }

        // advance a node without choices
        public void Continue()
        {
            if (Node == null || Node.choices.Length > 0) return;
            Go(Node.next);
        }

        public void Choose(int index)
        {
            if (Node == null || index < 0 || index >= Node.choices.Length) return;
            var c = Node.choices[index];
            foreach (var a in c.actions) applyAction(a);
            Go(c.next);
        }

        public void Stop() => Go(null);

        public static string Fill(string text) =>
            (text ?? "").Replace("{name}", GameState.Current.playerName);

        void Go(string id)
        {
            DialogueNode next = null;
            if (Current != null && !string.IsNullOrEmpty(id))
                next = Array.Find(Current.nodes, n => n.id == id);
            if (next == null)
            {
                Current = null; Node = null;
                Ended?.Invoke();
                return;
            }
            Node = next;
            foreach (var a in Node.actions) applyAction(a);
            Changed?.Invoke();
        }
    }
}
