using System;
using LifeInDowntown.Game;
using UnityEngine;

namespace LifeInDowntown.Phone
{
    // Missions (docs/DESIGN.md 3D): decides when someone rings with a job, tracks the active objective,
    // completes it when the player reaches the real place, pays out, and sends texts.
    // State lives in GameState so it is saved; Tick() is public so tests can drive it in edit mode.
    public class MissionManager : MonoBehaviour
    {
        public static MissionManager Instance { get; private set; }

        public Transform player;
        public float retryAfterDeclineSeconds = 60f;

        public PhoneData Data { get; private set; }
        public event Action<Mission> IncomingCall;           // the phone rings
        public event Action<string, string> Notify;          // banner: title, body
        public event Action MessagesChanged;
        public event Action<Objective> MeetStarted;          // Tarik reached someone waiting in person (InPersonTalk)

        float quietSeconds;           // time since the last mission event (start, finish, decline)
        bool callPending;             // a mission call is ringing or in progress
        bool meeting;                 // a face-to-face talk is running; the objective waits for it
        float textClock;              // play time since the last between-jobs text

        GameState S => GameState.Current;
        public Mission Active => Data?.Mission(S.activeMission);
        public Objective ActiveObjective =>
            Active != null && S.objectiveIndex < Active.objectives.Length ? Active.objectives[S.objectiveIndex] : null;
        public Vector3? Target => ActiveObjective == null ? null : meetPoint ?? Data.PlacePosition(ActiveObjective.place);
        public Vector3? PlaceTarget => ActiveObjective != null ? Data.PlacePosition(ActiveObjective.place) : null;

        // where the person for this objective actually stands (Cast snaps them to the sidewalk); the meeting
        // radius and the waypoint use it instead of the map place
        Vector3? meetPoint;
        public void SetMeetPoint(Vector3? at) => meetPoint = at;

        void Awake()
        {
            Instance = this;
            Init();
        }

        public void Init()
        {
            if (Data == null) Data = PhoneData.Load();
        }

        void Update()
        {
            if (GameFlow.Instance != null && GameFlow.Instance.State != GameFlow.FlowState.Playing) return;
            if (player != null) Tick(Time.deltaTime, player.position);
        }

        public void ResetSession()
        {
            quietSeconds = 0f;
            callPending = false;
            meeting = false;
            textClock = 0f;
        }

        public void Tick(float dt, Vector3 playerPos)
        {
            Init();
            quietSeconds += dt;
            var obj = ActiveObjective;
            if (obj != null)
            {
                if (meeting) return;
                var t = Target;
                if (t.HasValue && Flat(playerPos - t.Value).magnitude <= obj.radius)
                {
                    if (!string.IsNullOrEmpty(obj.talk) && MeetStarted != null) { meeting = true; MeetStarted(obj); }
                    else Advance();
                }
                return;
            }
            if (callPending || S.activeMission != "") return;
            IdleTexts(dt);
            var next = NextTriggered();
            if (next != null)
            {
                callPending = true;
                IncomingCall?.Invoke(next);
            }
        }

        // the face-to-face talk finished: the meeting objective is done
        public void MeetEnded()
        {
            if (!meeting) return;
            meeting = false;
            if (ActiveObjective != null) Advance();
        }

        public bool Meeting => meeting;

        // between jobs, contacts with a text list send the next one every few minutes of play
        void IdleTexts(float dt)
        {
            textClock += dt;
            foreach (var c in Data.ContactOrder)
            {
                if (c.texts.Length == 0 || c.textMinutes <= 0f || textClock < c.textMinutes * 60f) continue;
                Text(c.id, c.texts[S.textsSent % c.texts.Length]);
                S.textsSent++;
                textClock = 0f;
                return;
            }
        }

        // first mission whose trigger conditions hold and whose wait has passed
        public Mission NextTriggered()
        {
            foreach (var m in Data.Missions)
            {
                if (S.HasFlag("done:" + m.id) || quietSeconds < m.trigger.afterSeconds) continue;
                if (Array.Exists(m.trigger.requires, f => !S.HasFlag(f))) continue;
                if (Array.Exists(m.trigger.blocked, f => S.HasFlag(f))) continue;
                return m;
            }
            return null;
        }

        // the phone answered or declined a mission call
        public void CallEnded(bool declined)
        {
            callPending = false;
            if (declined) quietSeconds = -retryAfterDeclineSeconds;   // ring again later
            else if (S.activeMission == "") quietSeconds = 0f;         // talked but no job started (e.g. said no)
        }

        // dialogue actions: start:<mission> flag:<f> unflag:<f> cash:+N rep:+N heat:+N loyalty:+N text:<contact>:<msg>
        //                   item:<id>:<count> (into the backpack)
        public void Apply(string action)
        {
            Init();
            int colon = action.IndexOf(':');
            if (colon < 0) return;
            string verb = action.Substring(0, colon), arg = action.Substring(colon + 1);
            switch (verb)
            {
                case "start": StartMission(arg); break;
                case "flag": S.SetFlag(arg); break;
                case "unflag": S.ClearFlag(arg); break;
                case "cash": S.cash += ParseInt(arg); break;
                case "rep": S.rep += ParseInt(arg); break;
                case "heat": S.heat += ParseInt(arg); break;
                case "loyalty": S.loyalty += ParseInt(arg); break;
                case "item":
                    var ip = arg.Split(':');
                    LifeInDowntown.Items.Inventory.Add(S, ip[0], ip.Length > 1 ? Mathf.Max(1, ParseInt(ip[1])) : 1);
                    break;
                case "text":
                    int c2 = arg.IndexOf(':');
                    if (c2 > 0) Text(arg.Substring(0, c2), arg.Substring(c2 + 1));
                    break;
                default: Debug.LogWarning($"PHONE unknown action '{action}'"); break;
            }
        }

        public void StartMission(string id)
        {
            var m = Data.Mission(id);
            if (m == null) { Debug.LogWarning($"PHONE unknown mission {id}"); return; }
            S.activeMission = id;
            S.objectiveIndex = 0;
            quietSeconds = 0f;
            Notify?.Invoke("NEW JOB", $"{m.title}: {DialogueRunner.Fill(m.objectives[0].text)}");
        }

        void Advance()
        {
            var m = Active;
            var obj = ActiveObjective;
            if (!string.IsNullOrEmpty(obj.arriveText)) Text(m.giver, obj.arriveText);
            S.objectiveIndex++;
            meetPoint = null;
            if (S.objectiveIndex >= m.objectives.Length) Complete(m);
            else Notify?.Invoke(m.title.ToUpperInvariant(), DialogueRunner.Fill(m.objectives[S.objectiveIndex].text));
        }

        void Complete(Mission m)
        {
            foreach (var r in m.rewards) Apply(r);
            S.SetFlag("done:" + m.id);
            S.activeMission = "";
            S.objectiveIndex = 0;
            quietSeconds = 0f;
            if (!string.IsNullOrEmpty(m.doneText)) Text(m.giver, m.doneText);
            Notify?.Invoke("JOB COMPLETE", $"{m.title}  ·  {RewardSummary(m)}");
        }

        public void Text(string from, string text)
        {
            S.messages.Add(new PhoneMessage { from = from, text = DialogueRunner.Fill(text), time = DateTime.Now.ToString("HH:mm") });
            MessagesChanged?.Invoke();
            Notify?.Invoke(Data.ContactName(from), DialogueRunner.Fill(text));
        }

        public static string RewardSummary(Mission m)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var r in m.rewards)
            {
                int c = r.IndexOf(':');
                if (c < 0 || r.StartsWith("flag") || r.StartsWith("unflag")) continue;
                string verb = r.Substring(0, c), val = r.Substring(c + 1);
                parts.Add(verb == "cash" ? (val.StartsWith("-") ? "-$" + val.Substring(1) : "$" + val.TrimStart('+')) : $"{verb.ToUpperInvariant()} {val}");
            }
            return string.Join("  ", parts);
        }

        static int ParseInt(string s) => int.TryParse(s.TrimStart('+'), out int v) ? v : 0;
        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
