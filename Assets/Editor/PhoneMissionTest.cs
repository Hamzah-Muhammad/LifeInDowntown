using System.Collections.Generic;
using LifeInDowntown.Game;
using LifeInDowntown.Phone;
using UnityEditor;
using UnityEngine;

namespace LifeInDowntown.EditorTools
{
    // Validates the phone data and plays the three starter missions through MissionManager + DialogueRunner
    // with scripted choices and teleports (edit mode). Prints PHONE_PASS / PHONE_FAIL; exits 1 on failure.
    public static class PhoneMissionTest
    {
        public static void Run()
        {
            int fails = 0;
            void Check(bool ok, string what) { Debug.Log((ok ? "PHONE_PASS " : "PHONE_FAIL ") + what); if (!ok) fails++; }

            var go = new GameObject("PhoneTest");
            var mm = go.AddComponent<MissionManager>();
            mm.Init();
            var problems = mm.Data.Validate();
            foreach (var p in problems) Debug.Log("PHONE_FAIL data: " + p);
            Check(problems.Count == 0, $"data valid: {mm.Data.ContactOrder.Count} contacts, {mm.Data.Dialogues.Count} dialogues, " +
                                       $"{mm.Data.Missions.Count} missions, {mm.Data.Places.Count} places");

            GameState.NewGame();
            var S = GameState.Current;
            Mission ringing = null;
            var banners = new List<string>();
            mm.IncomingCall += m => ringing = m;
            mm.Notify += (t, b) => banners.Add(t + ": " + b);
            var runner = new DialogueRunner(mm.Apply);
            var away = new Vector3(0f, 0f, 200f);

            void Wait(float seconds, Vector3 at) { for (float t = 0; t < seconds; t += 0.5f) mm.Tick(0.5f, at); }
            Vector3 P(string place) => mm.Data.PlacePosition(place).Value;

            // ---- Link Up: rings after 20 s, "Say less" starts it, two stops, reward
            Wait(19f, away);
            Check(ringing == null, "no call before 20 s");
            Wait(2f, away);
            Check(ringing?.id == "link_up", $"Ty rings with Link Up at ~20 s (got {ringing?.id})");
            runner.Begin(mm.Data.Dialogues[ringing.call]);
            Check(runner.Node.choices.Length == 3, "Ty's call offers 3 replies");
            runner.Choose(0);
            Check(S.activeMission == "link_up", "'Say less' starts Link Up");
            runner.Continue();
            Check(!runner.Active, "call ends after Ty's last line");
            mm.CallEnded(false); ringing = null;
            Wait(1f, P("Sankofa Square Entrance"));
            Check(S.objectiveIndex == 1 && S.messages.Count == 1, "arriving at Sankofa Square advances to Trinity Square and Ty texts");
            Wait(1f, P("Trinity Square"));
            Check(S.HasFlag("done:link_up") && S.activeMission == "" && Mathf.Approximately(S.cash, 80f) && S.rep == 1,
                  $"Link Up complete: cash {S.cash} (60+20), REP {S.rep}");

            // ---- Ma's Errand: rings 40 s later; declining delays the retry by 60 s
            Wait(41f, away);
            Check(ringing?.id == "ma_errand", $"Ma rings with her errand after 40 s (got {ringing?.id})");
            mm.CallEnded(true); ringing = null;
            Wait(80f, away);
            Check(ringing == null, "declined call does not ring again within 80 s");
            Wait(25f, away);
            Check(ringing?.id == "ma_errand", "Ma rings again after the decline wait");
            runner.Begin(mm.Data.Dialogues[ringing.call]);
            runner.Choose(1);   // "Just walking"
            runner.Continue();  // -> ask
            runner.Choose(1);   // "Can Baba not get it tomorrow?"
            runner.Continue();  // push -> yes (starts the errand)
            Check(S.activeMission == "ma_errand", "pushing back still ends with Ma's errand started");
            runner.Continue();
            mm.CallEnded(false); ringing = null;
            Wait(1f, P("Shoppers Drug Mart"));
            Check(S.HasFlag("done:ma_errand") && Mathf.Approximately(S.cash, 72f) && S.loyalty == 1, $"errand done: cash {S.cash} (-8), LOYALTY {S.loyalty}");

            // ---- The Package: refusing blocks it for good
            Wait(46f, away);
            Check(ringing?.id == "the_package", $"Ty rings with The Package (got {ringing?.id})");
            runner.Begin(mm.Data.Dialogues[ringing.call]);
            runner.Choose(2);   // "Nah, I'm good."
            runner.Continue();
            mm.CallEnded(false); ringing = null;
            Check(S.HasFlag("refused_package") && S.loyalty == 0 && S.activeMission == "", "refusing the package sets the flag and costs LOYALTY");
            Wait(120f, away);
            Check(ringing == null, "a refused job never rings again");

            // ---- Ty in person (DESIGN 3O.1): College Run waits for the face-to-face talk before it completes
            GameState.NewGame(); S = GameState.Current;
            S.SetFlag("done:link_up"); S.SetFlag("done:the_package"); S.SetFlag("done:ma_errand");
            mm.ResetSession();
            Objective met = null;
            mm.MeetStarted += o => met = o;
            Wait(239f, away);
            Check(ringing == null, "no Ty job before 4 min");
            Wait(2f, away);
            Check(ringing?.id == "college_run", $"Ty rings with College Run after 4 min (got {ringing?.id})");
            runner.Begin(mm.Data.Dialogues[ringing.call]);
            runner.Choose(0); runner.Continue();
            mm.CallEnded(false); ringing = null;
            Check(S.activeMission == "college_run" && mm.ActiveObjective.npc == "ty", "College Run: Ty waits in person at College Park");
            mm.SetMeetPoint(P("College Park Shops Entrance") + new Vector3(3f, 0f, 0f));   // Cast snaps him to the sidewalk
            Wait(1f, P("College Park Shops Entrance") + new Vector3(3f, 0f, 0f));
            Check(met != null && mm.Meeting && S.activeMission == "college_run", "reaching Ty starts the talk and the job waits for it");
            var talk = new DialogueRunner(mm.Apply);
            talk.Begin(mm.Data.Dialogues[met.talk]);
            talk.Choose(1); talk.Continue(); talk.Continue();
            mm.MeetEnded();
            Check(S.HasFlag("done:college_run") && Mathf.Approximately(S.cash, 100f) && !mm.Meeting, $"the talk ends the job: cash {S.cash} (60+40)");
            int textsBefore = S.messages.Count;
            Wait(301f, away);
            Check(S.messages.Count == textsBefore + 1 && S.messages[S.messages.Count - 1].from == "ty", $"between jobs Ty texts every 5 min ('{S.messages[S.messages.Count - 1].text}')");

            Check(banners.Exists(b => b.StartsWith("JOB COMPLETE")), $"completion banners shown ({banners.Count} banners total)");
            Object.DestroyImmediate(go);
            Debug.Log(fails == 0 ? "PHONE_RESULT all checks passed" : $"PHONE_RESULT {fails} check(s) failed");
            EditorApplication.Exit(fails == 0 ? 0 : 1);
        }
    }
}
