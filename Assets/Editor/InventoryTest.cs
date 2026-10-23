using System.IO;
using LifeInDowntown.Game;
using LifeInDowntown.Items;
using LifeInDowntown.World;
using MinniGameFramework.Combat;
using MinniGameFramework.Player;
using UnityEditor.SceneManagement;
using UnityEditor;
using UnityEngine;

namespace LifeInDowntown.EditorTools
{
    // Backpack, shop, body stats and saves (docs/DESIGN.md 3M), plus the scene wiring: shop counters at
    // both Rabbas and the condo pickups. Saves go to a temp folder. Prints INV_PASS / INV_FAIL; exits 1 on failure.
    public static class InventoryTest
    {
        public static void Run()
        {
            int fails = 0;
            void Check(bool ok, string what) { Debug.Log((ok ? "INV_PASS " : "INV_FAIL ") + what); if (!ok) fails++; }
            string tmp = Path.Combine(Path.GetTempPath(), "LifeInDowntownInv_" + System.Guid.NewGuid().ToString("N"));
            SaveSystem.Folder = Path.Combine(tmp, "saves");

            try
            {
                // new game: start kit and cash
                GameState.NewGame();
                var s = GameState.Current;
                Check(Inventory.Count(s, "phone") == 1 && Inventory.Count(s, "condo_keys") == 1 && Inventory.Count(s, "lighter") == 1,
                      "new game: phone, keys and lighter in the backpack");
                Check(s.equipped == "pistol_9mm" && !s.drawn && s.loaded == 15 && Inventory.Count(s, "ammo_9mm") == 15,
                      $"new game: the 9mm on his hip, {s.loaded} loaded, {Inventory.Count(s, "ammo_9mm")} spare (DESIGN 3O.6)");
                Check(Mathf.Approximately(s.cash, 60f) && s.inventory.Count == Inventory.Slots, $"new game: $60.00 and {Inventory.Slots} slots (cash {s.cash})");
                Check(!Inventory.CanDrop("phone") && Inventory.CanDrop("lighter"), "key items can't be dropped");

                // stacking: 7 chips with a stack of 5 = two slots
                int used0 = Used(s);
                Check(Inventory.Add(s, "ketchup_chips", 7) == 7 && Used(s) == used0 + 2 && Inventory.Count(s, "ketchup_chips") == 7,
                      $"7 chips stack into 2 slots ({Used(s) - used0} new slots)");
                Check(Inventory.Remove(s, "ketchup_chips", 7) == 7 && Used(s) == used0, "removing them frees both slots");

                // weight: water is 0.52 kg; 35 kg cap
                float before = Inventory.Weight(s);
                int room = Inventory.Room(s, "water");
                int expectByWeight = Mathf.FloorToInt((Inventory.MaxCarry - before) / 0.52f + 1e-4f);
                Check(room == Mathf.Min(expectByWeight, (Inventory.Slots - Used(s)) * 4), $"room for water by slots and weight: {room}");
                int spare0 = Inventory.Count(s, "ammo_9mm");
                Inventory.Add(s, "ammo_9mm", 50); Inventory.Add(s, "ammo_9mm", 50);
                Check(Inventory.Count(s, "ammo_9mm") == spare0 + 100, $"100 more rounds stack up ({spare0} + 100)");
                Inventory.Remove(s, "ammo_9mm", 100);

                // buying
                float cash0 = s.cash;
                Check(Inventory.Buy(s, "beef_patty") == Inventory.BuyResult.Ok && Mathf.Approximately(s.cash, cash0 - 3.99f) && Inventory.Count(s, "beef_patty") == 1,
                      $"buy a patty: {ItemCatalog.Money(cash0)} -> {ItemCatalog.Money(s.cash)}");
                Check(Inventory.Buy(s, "pistol_9mm") == Inventory.BuyResult.NotSold, "a Rabba doesn't sell pistols");
                s.cash = 1.50f;
                Check(Inventory.Buy(s, "water") == Inventory.BuyResult.NoCash && Inventory.Count(s, "water") == 0, "can't buy water with $1.50");
                s.cash = 500f;
                int bought = 0;
                while (bought < 200 && Inventory.Buy(s, "water") == Inventory.BuyResult.Ok) bought++;
                Check(bought > 0 && Inventory.Buy(s, "water") == Inventory.BuyResult.NoRoom && Inventory.Weight(s) <= Inventory.MaxCarry + 1e-3f,
                      $"buying stops when the backpack is full, by slots or weight ({Inventory.Weight(s):0.0} kg, {Inventory.Count(s, "water")} waters)");
                Inventory.Remove(s, "water", 100);

                // eating and drinking
                s.hunger = 50f; s.thirst = 95f;
                string said = Inventory.Use(s, "beef_patty");
                Check(said == "Ate Beef Patty" && Mathf.Approximately(s.hunger, 80f) && Inventory.Count(s, "beef_patty") == 0, $"eat a patty: hunger 50 -> {s.hunger}");
                Inventory.Add(s, "water", 1);
                Inventory.Use(s, "water");
                Check(Mathf.Approximately(s.thirst, 100f), "drinking caps thirst at 100");
                Check(Inventory.Use(s, "lighter") == null, "a lighter can't be eaten");

                // the body clock (PlayerStats.Tick): 60 min walking = half of 120 min hunger
                var statsGo = new GameObject("StatsTest");
                var stats = statsGo.AddComponent<PlayerStats>();
                s.hunger = 100f; s.thirst = 100f; s.health = 80f;
                for (int i = 0; i < 3600; i++) stats.Tick(1f, false);
                Check(Mathf.Abs(s.hunger - 50f) < 0.5f && Mathf.Abs(s.thirst - 33.3f) < 0.5f, $"an hour: hunger {s.hunger:0.0} (50), thirst {s.thirst:0.0} (33.3)");
                // starving: -6 health a minute from 20 hits zero after ~3.3 min -> collapse, wake at home on 35 health, 15% of cash gone
                s.hunger = 0f; s.thirst = 60f; s.health = 20f; s.cash = 100f;
                for (int i = 0; i < 120; i++) stats.Tick(1f, false);
                Check(Mathf.Abs(s.health - 8f) < 0.2f, $"starving drains 6 health a minute ({s.health:0.0} after 2 min, from 20)");
                for (int i = 0; i < 120; i++) stats.Tick(1f, false);
                Check(Mathf.Approximately(s.health, 35f) && Mathf.Approximately(s.cash, 85f) && s.hunger >= 29f,
                      $"collapse at zero: wake on {s.health:0} health, cash $100 -> {ItemCatalog.Money(s.cash)}, hunger {s.hunger:0} (woke on 30, then the clock kept going)");
                Object.DestroyImmediate(statsGo);

                // weapons: equip loads the magazine from loose rounds (start from a clean slate: a new game starts armed)
                s.health = 100f;
                Inventory.Remove(s, "pistol_9mm", 9); Inventory.Remove(s, "ammo_9mm", 999); s.equipped = ""; s.loaded = 0;
                Inventory.Add(s, "pistol_9mm", 1); Inventory.Add(s, "ammo_9mm", 20);
                Check(Inventory.Equip(s, "pistol_9mm") && s.equipped == "pistol_9mm" && s.loaded == 15 && Inventory.Count(s, "ammo_9mm") == 5,
                      $"equip pistol: {s.loaded} loaded, {Inventory.Count(s, "ammo_9mm")} spare");
                Inventory.Remove(s, "pistol_9mm", 1);
                Check(s.equipped == "" && !s.drawn, "dropping the pistol unequips it");

                // save / load round trip
                Inventory.Add(s, "sandwich", 2);
                s.hunger = 42f;
                SaveSystem.Write(0, new SaveData { state = s });
                var back = SaveSystem.Read(0).state;
                Check(Inventory.Count(back, "sandwich") == 2 && Mathf.Approximately(back.hunger, 42f) && Mathf.Approximately(back.cash, s.cash),
                      "backpack, stats and cash survive a save");
                var oldSave = new GameState();   // a save from before the backpack
                Inventory.EnsureKeyItems(oldSave);
                Check(Inventory.Count(oldSave, "phone") == 1 && Inventory.Count(oldSave, "condo_keys") == 1, "old saves get the phone and keys back");

                // a corner dealer: his own stock, and buying from him is HEAT (DESIGN 3O)
                s.cash = 100f; int heat0 = s.heat;
                var corner = ItemCatalog.Shop("corner");
                Check(corner != null && corner.heat == 1 && System.Array.IndexOf(corner.stock, "loud") >= 0, "the corner stock exists and costs heat");
                Check(Inventory.Buy(s, "loud") == Inventory.BuyResult.Ok && Mathf.Approximately(s.cash, 85f), $"a gram of loud is $15 (cash {s.cash})");
                Check(LifeInDowntown.NPC.NpcBarks.LinesFor(MinniGameFramework.NPC.Pedestrian.Role.Walker).Length >= 10
                      && LifeInDowntown.NPC.NpcBarks.LinesFor(MinniGameFramework.NPC.Pedestrian.Role.Homeless).Length > 0, "Toronto street lines for walkers and the homeless");

                // scene wiring
                EditorSceneManager.OpenScene("Assets/Scenes/Downtown_YongeDundas.unity", OpenSceneMode.Single);
                var counters = Object.FindObjectsByType<ShopCounter>(FindObjectsSortMode.None);
                Check(counters.Length == 2, $"a shop counter in each Rabba ({counters.Length})");
                var pickups = Object.FindObjectsByType<WorldPickup>(FindObjectsSortMode.None);
                bool upstairs = System.Array.TrueForAll(pickups, p => p.transform.position.y > 119f && p.transform.position.y < 122f);
                Check(pickups.Length == 4 && upstairs, $"4 condo pickups on the furniture, in the condo ({pickups.Length})");
                var dealers = Object.FindObjectsByType<LifeInDowntown.World.DealerNPC>(FindObjectsSortMode.None);
                Check(dealers.Length >= 8 && System.Array.TrueForAll(dealers, dl => dl.GetComponent<MinniGameFramework.NPC.Pedestrian>().role == MinniGameFramework.NPC.Pedestrian.Role.Corner),
                      $"a dealer on each corner crew ({dealers.Length})");
                Check(Object.FindFirstObjectByType<LifeInDowntown.NPC.NpcBarks>() != null, "street talk is in the scene");
                Check(Object.FindFirstObjectByType<InventoryUI>() != null && Object.FindFirstObjectByType<PlayerStats>() != null
                      && Object.FindFirstObjectByType<WeaponHolder>() != null, "backpack screen, body stats and weapon holder are in the scene");
            }
            finally
            {
                SaveSystem.Folder = null;
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            }

            Debug.Log(fails == 0 ? "INV_RESULT all checks passed" : $"INV_RESULT {fails} check(s) FAILED");
            if (Application.isBatchMode) EditorApplication.Exit(fails == 0 ? 0 : 1);
        }

        static int Used(GameState s) { int n = 0; foreach (var slot in s.inventory) if (!slot.Empty) n++; return n; }
    }
}
