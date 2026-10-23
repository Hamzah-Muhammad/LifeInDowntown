using System.Collections.Generic;
using LifeInDowntown.Game;
using LifeInDowntown.Items;
using LifeInDowntown.NPC;
using LifeInDowntown.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LifeInDowntown.EditorTools
{
    // Combat (docs/DESIGN.md 3P.6) in edit mode, in an empty scene: the damage model and the hurt feedback.
    // NPCs: a head hit drops them at once, two body shots do, police kills cost HEAT +10. Tarik: bullets
    // fall off with range, no healing for 8 s after a hit, the red edge / glitch / tinnitus / heartbeat
    // respond and fade, and at 0 he wakes at home poorer but still armed.
    // Prints COMBAT_PASS / COMBAT_FAIL; exits 1 on failure.
    public static class CombatTest
    {
        public static void Run()
        {
            int fails = 0;
            void Check(bool ok, string what) { Debug.Log((ok ? "COMBAT_PASS " : "COMBAT_FAIL ") + what); if (!ok) fails++; }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                GameState.NewGame();
                var s = GameState.Current;
                Pedestrian Person(string avatar, Vector3 at)
                {
                    var p = ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>($"{CharacterImport.Root}/Prefabs/{avatar}.prefab"))).GetComponent<Pedestrian>();
                    p.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, 180f, 0f));
                    Pedestrian.All.Remove(p); Pedestrian.All.Add(p);
                    return p;
                }

                // NPC damage
                var a = Person("Male_Adult_12", new Vector3(20f, 0f, 0f));
                Check(a.Damage(Pedestrian.BodyShot(), a.transform.position + Vector3.up * 1.72f, Vector3.forward, Vector3.zero) && a.Dead, "a head hit drops a person at once");
                for (int i = 0; i < 200; i++) { float x = Pedestrian.BodyShot(), y = Pedestrian.BodyShot(); if (x + y < 100f) { Check(false, $"two body shots can leave someone standing ({x:F1} + {y:F1})"); break; } }
                var b = Person("Male_Adult_12", new Vector3(-20f, 0f, 0f));
                bool first = b.Damage(Pedestrian.BodyShot(), b.transform.position + Vector3.up * 1.2f, Vector3.forward, Vector3.zero);
                Check(!first && !b.Dead && b.health is >= 45f and <= 50f && b.path != null, $"one body shot wounds and they run (health {b.health:F0})");
                Check(b.Damage(Pedestrian.BodyShot(), b.transform.position + Vector3.up * 1.2f, Vector3.forward, Vector3.zero) && b.Dead, "the second body shot drops them");

                // the gun on a police officer: wound +2 HEAT, kill +10 (plus +1 per shot near people)
                var cam = PlayerSetup.BuildPlayer(Vector3.zero, 0f);
                var gun = cam.GetComponent<WeaponHolder>();
                s.drawn = true;
                var cop = Person("Police_Male_01", new Vector3(0f, 0f, 6f));
                cop.role = Pedestrian.Role.Police;
                Physics.SyncTransforms();
                int heat0 = s.heat;
                var r1 = gun.Fire(new Vector3(0f, 1.2f, 0f), Vector3.forward);
                cop.transform.position = new Vector3(0f, 0f, 6f); Physics.SyncTransforms();
                var r2 = gun.Fire(new Vector3(0f, 1.2f, 0f), Vector3.forward);
                Check(r1 == WeaponHolder.Shot.Wounded && r2 == WeaponHolder.Shot.Killed && cop.Dead, $"two body shots on an officer ({r1}, {r2})");
                Check(s.heat == heat0 + 14, $"HEAT +1+2 for the wound, +1+10 for killing police ({heat0} -> {s.heat})");

                // Tarik's damage and regen
                Check(Mathf.Approximately(PlayerStats.BulletDamage(3f), 30f) && Mathf.Approximately(PlayerStats.BulletDamage(60f), 18f) && PlayerStats.BulletDamage(20f) is > 18f and < 30f,
                      "bullets do 30 close, 18 far, in between at 20 m");
                float punch = PlayerStats.PunchDamage();
                Check(punch is >= 6f and <= 10f, $"a punch does 6-10 ({punch:F1})");
                var statsGo = new GameObject("Stats");
                var stats = statsGo.AddComponent<PlayerStats>();
                var fb = cam.GetComponent<HurtFeedback>();
                fb.Step(0f);
                s.health = 100f; s.hunger = s.thirst = 100f;
                stats.TakeDamage(PlayerStats.BulletDamage(10f), Vector3.forward * 10f);
                float hurt = s.health;
                Check(hurt is > 70f and < 82.5f, $"a 10 m bullet takes ~28 ({hurt:F1} left)");
                fb.Step(1f / 60f);
                Check(fb.RedWeight > 0.5f && fb.GlitchWeight > 0.4f, $"the hit flashes red and glitches (red {fb.RedWeight:F2}, glitch {fb.GlitchWeight:F2})");
                fb.Step(1f);
                Check(fb.RedWeight < 0.01f && fb.GlitchWeight < 0.01f, "both fade within a second");
                stats.Tick(5f, false);
                Check(Mathf.Approximately(s.health, hurt), "no healing 5 s after a hit");
                stats.Tick(4f, false); stats.Tick(60f, false);
                Check(s.health > hurt + 2f, $"healing again after 8 s ({hurt:F1} -> {s.health:F1})");
                stats.NearMiss(Vector3.forward);
                fb.Step(1f / 60f);
                Check(fb.Ring > 0.95f, "a near miss rings");
                fb.Step(1.5f);
                Check(fb.Ring is > 0.3f and < 0.7f, $"the ring fades over ~3 s ({fb.Ring:F2} at 1.5 s)");
                s.health = 20f; fb.Step(0.1f);
                float heart = fb.Heart;
                s.health = 80f; fb.Step(0.1f);
                Check(heart > 0.25f && fb.Heart == 0f, $"a heartbeat under 30 health only ({heart:F2})");

                // going down: wake at home, 15% of the cash gone, still armed
                s.cash = 100f; s.health = 15f; s.drawn = true;
                stats.TakeDamage(40f, Vector3.forward);
                Check(stats.Collapses == 1 && Mathf.Approximately(s.health, 35f) && Mathf.Approximately(s.cash, 85f) && !s.drawn
                      && s.equipped == "pistol_9mm" && Inventory.Count(s, "pistol_9mm") == 1, $"at 0 he wakes at home, $15 lighter, pistol kept (cash {s.cash}, health {s.health})");
                // fists (DESIGN 3P.3)
                var fists = cam.GetComponent<Fists>();
                s.drawn = false; s.health = 100f;
                fists.SetUp(true); fists.Step(0.2f);
                var tb = WeaponHolder.PlayerBody(cam.transform);
                tb.Solve();
                Check(fists.Up && fists.Weight >= 1f && tb.rightTarget != null && tb.rightTarget.name == "FistR" && tb.leftTarget.name == "FistL", "fists up: his hands go to the guard");
                Check(tb.IndexBend(true) > 150f && tb.IndexBend(false) > 150f && tb.ThumbGap(true) < 0.03f && tb.ThumbGap(false) < 0.03f,
                      $"the hands close into fists (index bend {tb.IndexBend(true):F0} / {tb.IndexBend(false):F0}, thumb gap {tb.ThumbGap(true):F3} / {tb.ThumbGap(false):F3} m)");
                Pedestrian.Player = GameObject.Find("Player").transform;
                Vector3 inFront = new Vector3(0f, 0f, 0.85f);
                Pedestrian Victim(Pedestrian.Role role) { var p = Person("Male_Adult_12", inFront); p.role = role; Physics.SyncTransforms(); return p; }
                void Throw(bool heavy) { if (heavy) fists.Hook(); else fists.Jab(); for (int i = 0; i < 45; i++) fists.Step(1f / 60f); }
                void Melee(NpcMelee m, float seconds) { for (float t = 0f; t < seconds; t += 1f / 60f) m.Step(1f / 60f); }
                void Reset(Pedestrian p) { p.transform.position = inFront; Physics.SyncTransforms(); }

                var w = Victim(Pedestrian.Role.Walker);
                float h0 = w.health;
                Throw(false);
                var wm = w.GetComponent<NpcMelee>();
                Check(fists.LastHit == w && wm != null && h0 - w.health is >= 8f and <= 12f, $"a jab lands for 8-12 ({h0 - w.health:F1})");
                Melee(wm, 0.4f);
                Check((w.transform.position - inFront).magnitude > 0.15f && !wm.Fighting, $"they stagger back {(w.transform.position - inFront).magnitude:F2} m and stare, not fight, after one");
                Reset(w);
                Throw(false);
                Check(wm.Fighting && NpcMelee.AnyFighterNear(Vector3.zero, 2.5f), "the second punch: they fight back");
                fists.SetBlock(false);
                float hb = s.health;
                Melee(wm, 3f);
                float per = wm.SwingsLanded > 0 ? (hb - s.health) / wm.SwingsLanded : 0f;
                Check(wm.SwingsLanded >= 1 && per is >= 6f and <= 10f, $"they close in and land swings for 6-10 ({wm.SwingsLanded} landed, {per:F1} each)");
                int landed0 = wm.SwingsLanded; hb = s.health;
                fists.SetBlock(true);
                Melee(wm, 3f);
                per = wm.SwingsLanded > landed0 ? (hb - s.health) / (wm.SwingsLanded - landed0) : 99f;
                Check(per <= 3f, $"blocking takes 70% off ({per:F1} a swing)");
                fists.SetBlock(false);
                Object.DestroyImmediate(w.gameObject);

                var k = Victim(Pedestrian.Role.Walker);
                Throw(true);
                var km = k.GetComponent<NpcMelee>();
                var kAnim = k.GetComponentInChildren<Animator>();
                Check(km != null && km.Down && !kAnim.enabled && k.GetComponentsInChildren<Rigidbody>().Length >= 10, "a held hook knocks them down");
                Melee(km, NpcMelee.KnockdownSeconds + 0.2f);
                Check(!km.Down && kAnim.enabled && k.GetComponentsInChildren<Rigidbody>().Length == 0 && !k.Dead, "they get back up after 3 s");
                Object.DestroyImmediate(k.gameObject);

                var c = Victim(Pedestrian.Role.Corner);
                Throw(false);
                var cm = c.GetComponent<NpcMelee>();
                Check(cm.Fighting, "a corner crew member fights back at the first punch");
                c.health = 40f; Reset(c);
                Throw(false);
                Check(!cm.Fighting && c.path != null && !c.Dead, $"hurt below 35 they give up and run ({c.health:F0})");
                c.health = 5f; Reset(c);
                Throw(false);
                Check(c.Dead, "a beating can finish someone");
                s.drawn = true; fists.Step(0.2f);
                Check(!fists.Up && fists.Weight < 0.01f, "drawing the gun drops the fists");
                Check(Resources.Load<AudioClip>("Audio/punch_swing") != null && Resources.Load<AudioClip>("Audio/punch_jab") != null && Resources.Load<AudioClip>("Audio/punch_hook") != null,
                      "swing, jab and hook sounds exist");

                Check(Resources.Load<AudioClip>("Audio/hurt_hit") != null && Resources.Load<AudioClip>("Audio/tinnitus") != null && Resources.Load<AudioClip>("Audio/heartbeat") != null,
                      "hit, tinnitus and heartbeat sounds exist");
                Object.DestroyImmediate(fb);
                Hostiles(Check);
            }
            catch (System.Exception e) { Check(false, "exception: " + e); }
            Hostile.Rand = () => Random.value;
            Debug.Log(fails == 0 ? "COMBAT_RESULT all checks passed" : $"COMBAT_RESULT {fails} check(s) FAILED");
            if (Application.isBatchMode) EditorApplication.Exit(fails == 0 ? 0 : 1);
        }

        // enemies that shoot back (DESIGN 3P.4), stepped, on a flat lot with one low wall to hide behind
        static void Hostiles(System.Action<bool, string> Check)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Pedestrian.All.Clear(); Hostile.All.Clear();
            GameState.NewGame();
            var s = GameState.Current;
            GameObject Box(Vector3 at, Vector3 size) { var b = GameObject.CreatePrimitive(PrimitiveType.Cube); b.transform.position = at; b.transform.localScale = size; return b; }
            Box(new Vector3(0f, -0.05f, 0f), new Vector3(300f, 0.1f, 300f));
            Box(new Vector3(0f, 0.6f, 14f), new Vector3(4f, 1.2f, 0.5f));      // a car-high wall between them and Tarik
            var cam = PlayerSetup.BuildPlayer(Vector3.zero, 0f);
            var tarik = GameObject.Find("Player").transform;
            Pedestrian.Player = tarik;
            var stats = new GameObject("Stats").AddComponent<PlayerStats>();
            var spawner = new GameObject("Spawner").AddComponent<HostileSpawner>();
            spawner.prefabs = new[] { AssetDatabase.LoadAssetAtPath<GameObject>($"{CharacterImport.Root}/Prefabs/Male_Adult_12.prefab") };
            spawner.pistol = AssetDatabase.LoadAssetAtPath<GameObject>(PistolImport.PrefabPath);
            void Run(Hostile h, float seconds) { for (float t = 0f; t < seconds; t += 1f / 30f) { Physics.SyncTransforms(); h.Step(1f / 30f); } }

            var h1 = spawner.Spawn(new Vector3(0f, 0f, 20f), 1, 2f)[0];
            h1.transform.SetPositionAndRotation(new Vector3(1f, 0f, 20f), Quaternion.identity);   // back to Tarik
            var far = spawner.Spawn(new Vector3(90f, 0f, 0f), 1, 2f)[0];
            Run(h1, 1f);
            Check(h1.state == Hostile.State.Idle, "facing away 20 m off, they haven't clocked him");
            Hostile.HearShot(Vector3.zero);
            Check(h1.state == Hostile.State.Alert && far.state == Hostile.State.Idle, "a shot: the one 20 m away goes alert, the one 90 m away doesn't hear it");
            Hostile.Rand = () => 0.99f;   // every shot misses for now
            Run(h1, 1.2f);
            Check(h1.state == Hostile.State.Combat, "they turn, see him, and fight");
            Run(h1, 4f);
            var cover = h1.Cover;
            Check(cover.HasValue && !Hostile.Exposed(cover.Value, tarik) && (h1.transform.position - cover.Value).magnitude < 2f,
                  $"they find cover the wall hides from him and get to it ({(cover.HasValue ? (h1.transform.position - cover.Value).magnitude : -1f):F2} m off it)");
            Run(h1, 10f);
            Check(h1.ShotsFired >= 4 && h1.NearMisses >= 1, $"they peek and fire bursts ({h1.ShotsFired} shots, {h1.NearMisses} near misses, peeking {h1.Peeking})");
            Check(s.health == 100f, "missed shots don't hurt");
            Run(h1, 8f);
            Check(h1.Reloads >= 1, $"they reload after 12 ({h1.ShotsFired} shots, {h1.Reloads} reloads)");
            Hostile.Rand = () => 0f;      // every shot hits
            float before = s.health; int hits0 = h1.Hits;
            Run(h1, 4f);
            Check(h1.Hits > hits0 && s.health < before, $"hits hurt Tarik ({h1.Hits - hits0} hits, {before:F0} -> {s.health:F0})");
            s.health = 100f;

            var ped = h1.GetComponent<Pedestrian>();
            ped.health = 30f;
            Hostile.Rand = () => 0.1f;    // the coin says run
            ped.Damage(10f, ped.transform.position + Vector3.up, Vector3.forward, Vector3.zero);
            Run(h1, 0.2f);
            Check(h1.state == Hostile.State.Fled && h1.Down && ped.path != null, "under 25 health, a coin flip says run");

            var h3 = spawner.Spawn(new Vector3(-6f, 0f, 22f), 1, 2f)[0];
            var p3 = h3.GetComponent<Pedestrian>();
            p3.Damage(200f, p3.transform.position + Vector3.up * 1.7f, Vector3.forward, Vector3.zero);
            Run(h3, 0.1f);
            var drop = Object.FindObjectsByType<LifeInDowntown.World.WorldPickup>(FindObjectsSortMode.None);
            Check(h3.state == Hostile.State.Dead && System.Array.Exists(drop, d => d.itemId == "ammo_9mm" && d.count >= 6), "the dead drop their pistol as 9mm rounds");

            // a mission objective with "enemies": three on the street near the place, done when they're down
            var mm = new GameObject("Missions").AddComponent<LifeInDowntown.Phone.MissionManager>();
            mm.Init();
            var placeName = new List<string>(mm.Data.Places.Keys)[0];
            var place = mm.Data.PlacePosition(placeName).Value;
            Box(place + Vector3.down * 0.05f, new Vector3(60f, 0.1f, 60f));
            mm.Data.Missions.Add(new LifeInDowntown.Phone.Mission
            {
                id = "test_enemies", title = "Test", giver = "ty",
                objectives = new[] { new LifeInDowntown.Phone.Objective { text = "Deal with them", place = placeName, enemies = new[] { new LifeInDowntown.Phone.EnemyGroup { count = 3 } } } },
                rewards = new[] { "cash:+1" },
            });
            mm.StartMission("test_enemies");
            mm.Tick(0.1f, place + Vector3.forward * 200f);
            Check(mm.Spawned == null, "nobody spawns while Tarik is 200 m away");
            mm.Tick(0.1f, place + Vector3.forward * 40f);
            Check(mm.Spawned != null && mm.Spawned.Count == 3, $"within 90 m, three of them appear ({mm.Spawned?.Count ?? 0})");
            mm.Tick(0.1f, place + Vector3.forward * 40f);
            Check(s.activeMission == "test_enemies", "the objective waits while they're standing");
            foreach (var h in new List<Hostile>(mm.Spawned)) { var hp = h.GetComponent<Pedestrian>(); hp.Damage(200f, hp.transform.position + Vector3.up * 1.7f, Vector3.forward, Vector3.zero); h.Step(0.1f); }
            mm.Tick(0.1f, place + Vector3.forward * 40f);
            Check(s.HasFlag("done:test_enemies") && s.activeMission == "", "all down: the objective (and here the mission) completes");
            mm.Data.Missions.RemoveAll(m => m.id == "test_enemies");
            Check(mm.Data.Validate().Count == 0, "the real mission data still validates with the new enemies field");
        }
    }
}
