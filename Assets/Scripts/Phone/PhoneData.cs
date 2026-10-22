using System;
using System.Collections.Generic;
using UnityEngine;

namespace LifeInDowntown.Phone
{
    // Data for contacts, dialogue, missions and places (docs/DESIGN.md 3D), loaded from Resources/.
    [Serializable] public class ContactCall { public string dialogue; public string[] requires = new string[0]; public string[] blocked = new string[0]; }
    [Serializable] public class Contact
    {
        public string id; public string name; public string color; public ContactCall[] calls = new ContactCall[0];
        public string[] texts = new string[0];   // sent between jobs, in order, looping (DESIGN 3O.1)
        public float textMinutes;                // minutes of play between those texts (0 = never)
    }
    [Serializable] public class ContactList { public Contact[] contacts = new Contact[0]; }

    [Serializable] public class DialogueChoice { public string text; public string next; public string[] actions = new string[0]; }
    [Serializable] public class DialogueNode
    {
        public string id; public string speaker; public string text; public string next;
        public string[] actions = new string[0];
        public DialogueChoice[] choices = new DialogueChoice[0];
    }
    [Serializable] public class Dialogue { public string id; public DialogueNode[] nodes = new DialogueNode[0]; }
    [Serializable] public class DialogueList { public Dialogue[] dialogues = new Dialogue[0]; }

    [Serializable] public class MissionTrigger { public float afterSeconds; public string[] requires = new string[0]; public string[] blocked = new string[0]; }
    [Serializable] public class Objective
    {
        public string text; public string place; public float radius = 10f; public string arriveText;
        public string npc;    // someone waiting there in person, e.g. "ty" (DESIGN 3O.1)
        public string talk;   // dialogue played face to face on arrival; the objective completes when it ends
    }
    [Serializable] public class Mission
    {
        public string id; public string title; public string giver;
        public MissionTrigger trigger = new MissionTrigger();
        public string call;
        public Objective[] objectives = new Objective[0];
        public string[] rewards = new string[0];
        public string doneText;
    }
    [Serializable] public class MissionList { public Mission[] missions = new Mission[0]; }

    [Serializable] public class Place { public string name; public string category; public float x; public float z; }
    [Serializable] public class PlaceList { public Place[] places = new Place[0]; }

    public class PhoneData
    {
        public readonly Dictionary<string, Contact> Contacts = new Dictionary<string, Contact>();
        public readonly List<Contact> ContactOrder = new List<Contact>();
        public readonly Dictionary<string, Dialogue> Dialogues = new Dictionary<string, Dialogue>();
        public readonly List<Mission> Missions = new List<Mission>();
        public readonly Dictionary<string, Place> Places = new Dictionary<string, Place>();

        public static PhoneData Load()
        {
            var d = new PhoneData();
            foreach (var c in Read<ContactList>("Phone/contacts").contacts) { d.Contacts[c.id] = c; d.ContactOrder.Add(c); }
            foreach (var x in Read<DialogueList>("Phone/dialogues").dialogues) d.Dialogues[x.id] = x;
            d.Missions.AddRange(Read<MissionList>("Phone/missions").missions);
            foreach (var p in Read<PlaceList>("City/places").places) d.Places[p.name] = p;
            return d;
        }

        static T Read<T>(string resource) where T : new()
        {
            var ta = Resources.Load<TextAsset>(resource);
            if (ta == null) { Debug.LogError($"PHONE missing Resources/{resource}.json"); return new T(); }
            return JsonUtility.FromJson<T>(ta.text);
        }

        public Mission Mission(string id) => string.IsNullOrEmpty(id) ? null : Missions.Find(m => m.id == id);

        public string ContactName(string id) => id != null && Contacts.TryGetValue(id, out var c) ? c.name : id;

        public Vector3? PlacePosition(string name) =>
            name != null && Places.TryGetValue(name, out var p) ? new Vector3(p.x, 0f, p.z) : (Vector3?)null;

        // every dialogue, mission and place reference resolves; returns problems (empty = valid)
        public List<string> Validate()
        {
            var problems = new List<string>();
            foreach (var c in ContactOrder)
                foreach (var call in c.calls)
                    if (!Dialogues.ContainsKey(call.dialogue)) problems.Add($"contact {c.id}: missing dialogue {call.dialogue}");
            foreach (var m in Missions)
            {
                if (!Dialogues.ContainsKey(m.call)) problems.Add($"mission {m.id}: missing call dialogue {m.call}");
                if (!Contacts.ContainsKey(m.giver)) problems.Add($"mission {m.id}: unknown giver {m.giver}");
                foreach (var o in m.objectives)
                {
                    if (!Places.ContainsKey(o.place)) problems.Add($"mission {m.id}: unknown place '{o.place}'");
                    if (!string.IsNullOrEmpty(o.talk) && !Dialogues.ContainsKey(o.talk)) problems.Add($"mission {m.id}: missing talk dialogue {o.talk}");
                    if (!string.IsNullOrEmpty(o.talk) && string.IsNullOrEmpty(o.npc)) problems.Add($"mission {m.id}: talk '{o.talk}' with nobody there (npc)");
                }
            }
            foreach (var dlg in Dialogues.Values)
            {
                var ids = new HashSet<string>();
                foreach (var n in dlg.nodes) ids.Add(n.id);
                foreach (var n in dlg.nodes)
                {
                    if (!string.IsNullOrEmpty(n.next) && !ids.Contains(n.next)) problems.Add($"dialogue {dlg.id}/{n.id}: next '{n.next}' missing");
                    foreach (var ch in n.choices)
                        if (!string.IsNullOrEmpty(ch.next) && !ids.Contains(ch.next)) problems.Add($"dialogue {dlg.id}/{n.id}: choice next '{ch.next}' missing");
                    foreach (var a in n.actions) CheckAction(a, dlg.id, problems);
                    foreach (var ch in n.choices) foreach (var a in ch.actions) CheckAction(a, dlg.id, problems);
                }
            }
            return problems;
        }

        void CheckAction(string a, string where, List<string> problems)
        {
            if (a.StartsWith("start:") && Mission(a.Substring(6)) == null) problems.Add($"dialogue {where}: starts unknown mission {a}");
        }
    }
}
