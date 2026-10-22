using System;
using LifeInDowntown.Game;
using LifeInDowntown.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace LifeInDowntown.Phone
{
    // The in-game phone (docs/DESIGN.md 3D): Up Arrow / P toggles it; Phone, Messages and Jobs apps;
    // outgoing and incoming calls with dialogue choices; mission waypoint, objective line and banners.
    [RequireComponent(typeof(UIDocument))]
    public class PhoneController : MonoBehaviour
    {
        public MissionManager missions;
        public BodycamPlayer player;
        public Camera cam;

        enum View { Home, Contacts, Messages, Jobs, Incoming, Call }

        VisualElement root, phone, banner, waypoint, objective, choicesBox;
        Label statusTime, homeClock, homeDate, homeStats, bannerTitle, bannerBody, waypointLabel,
              objectiveTitle, objectiveText, callName, callTimer, callLine, callHint, callAvatar, incomingName, incomingAvatar;
        ScrollView contactsList, messagesList, jobsList;
        VisualElement[] views;
        View view = View.Home;
        bool up;
        float bannerUntil, callStarted;

        DialogueRunner runner;
        Mission ringingMission;        // the mission whose call is ringing / being talked through
        string callerId;

        public bool IsUp => up;
        public bool InCall => runner != null && runner.Active;

        void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            phone = root.Q("phone");
            banner = root.Q("banner"); bannerTitle = root.Q<Label>("banner-title"); bannerBody = root.Q<Label>("banner-body");
            waypoint = root.Q("waypoint"); waypointLabel = root.Q<Label>("waypoint-label");
            objective = root.Q("objective"); objectiveTitle = root.Q<Label>("objective-title"); objectiveText = root.Q<Label>("objective-text");
            statusTime = root.Q<Label>("status-time");
            homeClock = root.Q<Label>("home-clock"); homeDate = root.Q<Label>("home-date"); homeStats = root.Q<Label>("home-stats");
            contactsList = root.Q<ScrollView>("contacts-list");
            messagesList = root.Q<ScrollView>("messages-list");
            jobsList = root.Q<ScrollView>("jobs-list");
            callName = root.Q<Label>("call-name"); callTimer = root.Q<Label>("call-timer"); callLine = root.Q<Label>("call-line");
            callHint = root.Q<Label>("call-hint"); callAvatar = root.Q<Label>("call-avatar");
            choicesBox = root.Q("call-choices");
            incomingName = root.Q<Label>("incoming-name"); incomingAvatar = root.Q<Label>("incoming-avatar");
            views = new[] { root.Q("view-home"), root.Q("view-contacts"), root.Q("view-messages"), root.Q("view-jobs"),
                            root.Q("view-incoming"), root.Q("view-call") };

            root.Q<Button>("app-phone").clicked += () => Open(View.Contacts);
            root.Q<Button>("app-messages").clicked += () => Open(View.Messages);
            root.Q<Button>("app-jobs").clicked += () => Open(View.Jobs);
            root.Q<Button>("home-bar").clicked += () => { if (!InCall && view != View.Incoming) Open(View.Home); };
            root.Q<Button>("btn-answer").clicked += Answer;
            root.Q<Button>("btn-decline").clicked += Decline;
            root.Q<Button>("btn-hangup").clicked += HangUp;

            runner = new DialogueRunner(a => missions.Apply(a));
            runner.Changed += ShowLine;
            runner.Ended += OnCallEnded;

            missions.IncomingCall += Ring;
            missions.Notify += Banner;
            missions.MessagesChanged += () => { LifeInDowntown.Game.AudioDirector.Instance?.MessagePing(); if (view == View.Messages) FillMessages(); };
            Open(View.Home);
        }

        // verification hooks for capture scripts: -startmission <id>, -phoneup
        System.Collections.IEnumerator Start()
        {
            yield return new WaitForSeconds(1.5f);
            string m = GameFlow.Arg("-startmission");
            if (m != null) missions.StartMission(m);
            if (GameFlow.HasArg("-phoneup")) SetUp(true);
        }

        void OnDisable()
        {
            if (missions == null) return;
            missions.IncomingCall -= Ring;
            missions.Notify -= Banner;
        }

        // ------------------------------------------------------------------ frame
        void Update()
        {
            bool playing = GameFlow.Instance == null || GameFlow.Instance.State == GameFlow.FlowState.Playing;
            bool mapOpen = LifeInDowntown.Map.MapController.IsOpen;
            root.style.display = playing && !mapOpen ? DisplayStyle.Flex : DisplayStyle.None;
            if (!playing || mapOpen) return;   // the full map owns the screen and input while open
            // the phone owns the cursor while playing (also right after resuming from pause)
            UnityEngine.Cursor.lockState = up ? CursorLockMode.None : CursorLockMode.Locked;
            UnityEngine.Cursor.visible = up;

            var kb = Keyboard.current;
            if (kb != null)
            {
                if ((kb.upArrowKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame) && !InCall && view != View.Incoming) SetUp(!up);
                if (view == View.Incoming && kb.enterKey.wasPressedThisFrame) Answer();
                if (view == View.Incoming && kb.backspaceKey.wasPressedThisFrame) Decline();
                if (InCall && runner.Node != null)
                {
                    if (runner.Node.choices.Length == 0 && kb.spaceKey.wasPressedThisFrame) runner.Continue();
                    for (int i = 0; i < Mathf.Min(3, runner.Node.choices.Length); i++)
                        if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) { runner.Choose(i); break; }
                }
            }

            string now = DateTime.Now.ToString("HH:mm");
            statusTime.text = now;
            homeClock.text = now;
            homeDate.text = DateTime.Now.ToString("dddd, MMMM d");
            var s = GameState.Current;
            homeStats.text = $"${s.cash}   REP {s.rep}   HEAT {s.heat}   LOYALTY {s.loyalty}";
            if (InCall) callTimer.text = TimeSpan.FromSeconds(Time.time - callStarted).ToString(@"mm\:ss");
            if (bannerUntil > 0f && Time.unscaledTime > bannerUntil) { banner.RemoveFromClassList("banner--on"); bannerUntil = 0f; }
            UpdateObjectiveHud();
        }

        void SetUp(bool on)
        {
            up = on;
            phone.EnableInClassList("phone--up", on);
            if (player != null) player.lookEnabled = !on;
            if (on && !InCall && view != View.Incoming) Open(View.Home);
        }

        void Open(View v)
        {
            view = v;
            for (int i = 0; i < views.Length; i++) views[i].EnableInClassList("view--on", i == (int)v);
            if (v == View.Contacts) FillContacts();
            if (v == View.Messages) FillMessages();
            if (v == View.Jobs) FillJobs();
        }

        // ------------------------------------------------------------------ lists
        void FillContacts()
        {
            contactsList.Clear();
            foreach (var c in missions.Data.ContactOrder)
            {
                var row = Row(c, c.name, "mobile", null);
                row.clicked += () => CallContact(c);
                contactsList.Add(row);
            }
        }

        void FillMessages()
        {
            messagesList.Clear();
            var msgs = GameState.Current.messages;
            if (msgs.Count == 0) messagesList.Add(Note("No messages yet."));
            for (int i = msgs.Count - 1; i >= 0; i--)
            {
                var m = msgs[i];
                missions.Data.Contacts.TryGetValue(m.from, out var c);
                messagesList.Add(Row(c, c != null ? c.name : m.from, m.text, m.time));
            }
        }

        void FillJobs()
        {
            jobsList.Clear();
            var active = missions.Active;
            if (active != null)
            {
                var row = Row(missions.Data.Contacts.TryGetValue(active.giver, out var g) ? g : null, active.title,
                              DialogueRunner.Fill(missions.ActiveObjective?.text), "ACTIVE");
                row.AddToClassList("job-active");
                jobsList.Add(row);
            }
            foreach (var m in missions.Data.Missions)
                if (GameState.Current.HasFlag("done:" + m.id))
                {
                    missions.Data.Contacts.TryGetValue(m.giver, out var g);
                    jobsList.Add(Row(g, m.title, "Done  ·  " + MissionManager.RewardSummary(m), null));
                }
            if (jobsList.childCount == 0) jobsList.Add(Note("No jobs yet. Keep your phone on."));
        }

        Button Row(Contact c, string title, string sub, string time)
        {
            var b = new Button(); b.AddToClassList("row");
            var av = new Label(c != null ? c.name.Substring(0, 1) : "?"); av.AddToClassList("row-avatar");
            av.style.backgroundColor = ContactColor(c);
            var text = new VisualElement(); text.AddToClassList("row-text");
            var t = new Label(title); t.AddToClassList("row-title");
            var s = new Label(sub); s.AddToClassList("row-sub");
            text.Add(t); text.Add(s);
            b.Add(av); b.Add(text);
            if (time != null) { var tm = new Label(time); tm.AddToClassList("row-time"); b.Add(tm); }
            return b;
        }

        static Label Note(string text) { var l = new Label(text); l.AddToClassList("row-sub"); return l; }

        static Color ContactColor(Contact c) =>
            c != null && ColorUtility.TryParseHtmlString(c.color, out var col) ? col : new Color(0.4f, 0.4f, 0.45f);

        // ------------------------------------------------------------------ calls
        void CallContact(Contact c)
        {
            ContactCall pick = null;
            foreach (var call in c.calls)
            {
                if (Array.Exists(call.requires, f => !GameState.Current.HasFlag(f))) continue;
                if (Array.Exists(call.blocked, f => GameState.Current.HasFlag(f))) continue;
                pick = call; break;
            }
            if (pick == null || !missions.Data.Dialogues.TryGetValue(pick.dialogue, out var dlg)) { Banner(c.name, "No answer."); return; }
            ringingMission = null;
            StartCall(c, dlg);
        }

        void Ring(Mission m)
        {
            ringingMission = m;
            missions.Data.Contacts.TryGetValue(m.giver, out var c);
            callerId = m.giver;
            incomingName.text = c != null ? c.name : m.giver;
            incomingAvatar.text = c != null ? c.name.Substring(0, 1) : "?";
            incomingAvatar.style.backgroundColor = ContactColor(c);
            if (!up) SetUp(true);
            Open(View.Incoming);
            LifeInDowntown.Game.AudioDirector.Instance?.PhoneRinging(true);
        }

        void Answer()
        {
            if (view != View.Incoming || ringingMission == null) return;
            missions.Data.Contacts.TryGetValue(ringingMission.giver, out var c);
            StartCall(c, missions.Data.Dialogues[ringingMission.call]);
        }

        void Decline()
        {
            if (view != View.Incoming) return;
            LifeInDowntown.Game.AudioDirector.Instance?.PhoneRinging(false);
            GameState.Current.callLog.Add($"missed:{callerId}");
            ringingMission = null;
            missions.CallEnded(declined: true);
            Open(View.Home);
            SetUp(false);
        }

        void StartCall(Contact c, Dialogue dlg)
        {
            LifeInDowntown.Game.AudioDirector.Instance?.PhoneRinging(false);
            callerId = c?.id;
            callName.text = c != null ? c.name : "Unknown";
            callAvatar.text = c != null ? c.name.Substring(0, 1) : "?";
            callAvatar.style.backgroundColor = ContactColor(c);
            callStarted = Time.time;
            GameState.Current.callLog.Add($"call:{callerId}");
            Open(View.Call);
            runner.Begin(dlg);
        }

        void HangUp() { if (InCall) runner.Stop(); }

        // New Game / Load: drop any call in progress and put the phone away
        public void ResetPhone()
        {
            LifeInDowntown.Game.AudioDirector.Instance?.PhoneRinging(false);
            ringingMission = null;
            if (InCall) runner.Stop();
            Open(View.Home);
            SetUp(false);
        }

        void ShowLine()
        {
            var n = runner.Node;
            callLine.text = $"<b>{n.speaker}:</b> {DialogueRunner.Fill(n.text)}";
            choicesBox.Clear();
            for (int i = 0; i < n.choices.Length; i++)
            {
                int idx = i;
                var b = new Button(() => runner.Choose(idx)) { text = $"{i + 1}.  {DialogueRunner.Fill(n.choices[i].text)}" };
                b.AddToClassList("choice");
                choicesBox.Add(b);
            }
            callHint.text = n.choices.Length > 0 ? "Press 1-3 or click a reply" : "Space to continue";
        }

        void OnCallEnded()
        {
            bool missionCall = ringingMission != null;
            ringingMission = null;
            if (missionCall) missions.CallEnded(declined: false);
            Open(View.Home);
            SetUp(false);
        }

        // ------------------------------------------------------------------ HUD
        void Banner(string title, string body)
        {
            bannerTitle.text = title.ToUpperInvariant();
            bannerBody.text = body;
            banner.AddToClassList("banner--on");
            bannerUntil = Time.unscaledTime + 4.5f;
        }

        void UpdateObjectiveHud()
        {
            var obj = missions.ActiveObjective;
            var target = missions.Target;
            objective.style.display = obj != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (obj == null || !target.HasValue || cam == null) { waypoint.style.display = DisplayStyle.None; return; }
            objectiveTitle.text = missions.Active.title.ToUpperInvariant();
            objectiveText.text = DialogueRunner.Fill(obj.text);

            Vector3 world = target.Value + Vector3.up * 2.5f;
            Vector3 sp = cam.WorldToScreenPoint(world);
            float dist = Vector3.Distance(new Vector3(cam.transform.position.x, 0, cam.transform.position.z), new Vector3(world.x, 0, world.z));
            if (sp.z < 0f) { sp.x = Screen.width - sp.x; sp.y = 0f; }               // behind: pin to the bottom edge
            sp.x = Mathf.Clamp(sp.x, 40f, Screen.width - 40f);
            sp.y = Mathf.Clamp(sp.y, 60f, Screen.height - 40f);
            var panelPos = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(sp.x, Screen.height - sp.y));
            waypoint.style.display = DisplayStyle.Flex;
            waypoint.style.left = panelPos.x - 30f;
            waypoint.style.top = panelPos.y - 14f;
            waypointLabel.text = dist < 1000f ? $"{dist:0} m" : $"{dist / 1000f:0.0} km";
        }
    }
}
