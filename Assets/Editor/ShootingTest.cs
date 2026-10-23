using LifeInDowntown.Game;
using LifeInDowntown.Items;
using LifeInDowntown.NPC;
using MinniGameFramework.Combat;
using MinniGameFramework.NPC;
using UnityEditor.SceneManagement;
using UnityEditor;
using UnityEngine;

namespace LifeInDowntown.EditorTools
{
    // The pistol and shooting (docs/DESIGN.md 3O.6) in edit mode: firing at a wall leaves a hole and costs a
    // round; a body shot wounds a person, the second ragdolls them, both raise HEAT; bystanders run, police walk over; indoors is
    // refused; an empty mag clicks; reload refills. Prints SHOOT_PASS / SHOOT_FAIL; exits 1 on failure.
    public static class ShootingTest
    {
        public static void Run()
        {
            int fails = 0;
            void Check(bool ok, string what) { Debug.Log((ok ? "SHOOT_PASS " : "SHOOT_FAIL ") + what); if (!ok) fails++; }
            EditorSceneManager.OpenScene("Assets/Scenes/Downtown_YongeDundas.unity", OpenSceneMode.Single);
            try
            {
                var gun = Object.FindFirstObjectByType<WeaponHolder>();
                Check(gun != null && gun.gunModel != null, "the weapon holder has the real pistol model");
                if (gun == null) return;
                GameState.NewGame();
                var s = GameState.Current;
                s.drawn = true;
                Check(s.loaded == 15, $"armed from the start: {s.loaded} in the mag");

                // a real sidewalk line: along it is clear for the person test, across it is a wall for the hole test
                var crowd = Object.FindFirstObjectByType<CrowdDirector>();
                int li = 0;
                for (int k = 0; k < crowd.lineStart.Length; k++)
                {
                    int end = k + 1 < crowd.lineStart.Length ? crowd.lineStart[k + 1] : crowd.points.Length;
                    if (end - crowd.lineStart[k] >= 14) { li = crowd.lineStart[k]; break; }
                }
                Vector3 along = (crowd.points[li + 10] - crowd.points[li]); along.y = 0f; along.Normalize();
                Vector3 origin = crowd.points[li + 2] + Vector3.up * 1.25f;
                Physics.SyncTransforms();
                Vector3 toWall = Vector3.zero;
                foreach (var d in new[] { new Vector3(-along.z, 0, along.x), new Vector3(along.z, 0, -along.x) })
                    if (Physics.Raycast(origin + d * 0.35f, d, out var h, 25f, ~0, QueryTriggerInteraction.Ignore) && h.collider.GetComponentInParent<Pedestrian>() == null) { toWall = d; break; }
                Check(toWall != Vector3.zero, "found a wall beside the sidewalk to shoot at");
                foreach (var p in Object.FindObjectsByType<Pedestrian>(FindObjectsSortMode.None))
                    if ((p.transform.position - origin).sqrMagnitude < 50f * 50f) p.gameObject.SetActive(false);   // a quiet street for the wall shot
                int heat0 = s.heat;
                var r = gun.Fire(origin, toWall);
                Check(r == WeaponHolder.Shot.Hit && s.loaded == 14 && GameObject.Find("BulletHole") != null, $"a shot at a wall leaves a hole ({r}, {s.loaded} left)");
                Check(s.heat == heat0, "nobody around: no heat");

                // a person on the line of fire, and bystanders and an officer nearby
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterImport.Root + "/Prefabs/Male_Adult_12.prefab");
                var cop = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterImport.Root + "/Prefabs/Police_Male_01.prefab");
                Vector3 street = crowd.points[li + 2];
                var target = ((GameObject)PrefabUtility.InstantiatePrefab(prefab)).GetComponent<Pedestrian>();
                target.transform.position = street + along * 6f;
                var bystander = ((GameObject)PrefabUtility.InstantiatePrefab(prefab)).GetComponent<Pedestrian>();
                bystander.transform.position = street - along * 9f; bystander.mode = Pedestrian.Mode.Phone;
                var officer = ((GameObject)PrefabUtility.InstantiatePrefab(cop)).GetComponent<Pedestrian>();
                officer.transform.position = street - along * 20f; officer.role = Pedestrian.Role.Police; officer.mode = Pedestrian.Mode.Idle;
                foreach (var p in new[] { target, bystander, officer }) { Pedestrian.All.Remove(p); Pedestrian.All.Add(p); }
                Physics.SyncTransforms();
                heat0 = s.heat;
                r = gun.Fire(street + Vector3.up * 1.3f, along);   // a body shot: two drop anyone (DESIGN 3P.2)
                Check(r == WeaponHolder.Shot.Wounded && !target.Dead && target.health is >= 45f and <= 50f, $"one body shot wounds ({r}, health {target.health:F0})");
                target.transform.position = street + along * 6f;   // wounded people run: put him back on the line
                Physics.SyncTransforms();
                r = gun.Fire(street + Vector3.up * 1.3f, along);
                int bodies = target.GetComponentsInChildren<Rigidbody>().Length;
                Check(r == WeaponHolder.Shot.Killed && target.Dead && bodies >= 10 && !target.GetComponentInChildren<Animator>().enabled,
                      $"the second ragdolls them ({r}, {bodies} bodies)");
                Check(s.heat == heat0 + 7, $"HEAT +1 per shot near people, +2 the wound, +3 the kill ({heat0} -> {s.heat})");
                Check(bystander.path != null && bystander.path.Length == 2 && (bystander.path[1] - street).magnitude > 30f, "the bystander runs away from the shot");
                Check(officer.watchPlayer && officer.path != null, "the officer walks over and watches");

                // indoors, empty, reload
                var condoFloor = new Vector3(137.5f, 121.4f, -218.2f);
                r = gun.Fire(condoFloor, Vector3.forward);
                Check(r == WeaponHolder.Shot.NotHere && s.loaded == 12, $"no firing in the condo ({r})");
                s.loaded = 0;
                r = gun.Fire(origin, toWall);
                Check(r == WeaponHolder.Shot.Empty, "an empty mag just clicks");
                int got = Inventory.Reload(s);
                Check(got == 15 && s.loaded == 15 && Inventory.Count(s, "ammo_9mm") == 0, $"R reloads from the spare rounds ({got})");
                Check(Resources.Load<AudioClip>("Audio/gunshot_9mm") != null && Resources.Load<AudioClip>("Audio/gun_dry") != null, "gunshot and dry-fire sounds exist");
            }
            finally
            {
                Debug.Log(fails == 0 ? "SHOOT_RESULT all checks passed" : $"SHOOT_RESULT {fails} check(s) FAILED");
                if (Application.isBatchMode) EditorApplication.Exit(fails == 0 ? 0 : 1);
            }
        }
    }
}
