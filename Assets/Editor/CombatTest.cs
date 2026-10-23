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
                Check(Resources.Load<AudioClip>("Audio/hurt_hit") != null && Resources.Load<AudioClip>("Audio/tinnitus") != null && Resources.Load<AudioClip>("Audio/heartbeat") != null,
                      "hit, tinnitus and heartbeat sounds exist");
                Object.DestroyImmediate(fb);
            }
            catch (System.Exception e) { Check(false, "exception: " + e); }
            Debug.Log(fails == 0 ? "COMBAT_RESULT all checks passed" : $"COMBAT_RESULT {fails} check(s) FAILED");
            if (Application.isBatchMode) EditorApplication.Exit(fails == 0 ? 0 : 1);
        }
    }
}
