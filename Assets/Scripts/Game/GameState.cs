using System;
using System.Collections.Generic;

namespace LifeInDowntown.Game
{
    // The four hidden stats, story flags, phone/mission progress, body stats and the backpack
    // (docs/DESIGN.md 1, 3D, 3M).
    // Carried in every save file.
    [Serializable]
    public class GameState : MinniGameFramework.Core.IPlayerState
    {
        // MinniGameFramework reads and writes these through IPlayerState (DESIGN 3S); the saved fields stay as they are
        float MinniGameFramework.Core.IPlayerState.health { get => health; set => health = value; }
        float MinniGameFramework.Core.IPlayerState.hunger { get => hunger; set => hunger = value; }
        float MinniGameFramework.Core.IPlayerState.thirst { get => thirst; set => thirst = value; }
        float MinniGameFramework.Core.IPlayerState.cash { get => cash; set => cash = value; }
        int MinniGameFramework.Core.IPlayerState.heat { get => heat; set => heat = value; }
        bool MinniGameFramework.Core.IPlayerState.drawn { get => drawn; set => drawn = value; }
        int MinniGameFramework.Core.IPlayerState.loaded { get => loaded; set => loaded = value; }
        string MinniGameFramework.Core.IPlayerState.equipped { get => equipped; set => equipped = value; }

        public string playerName = "Tarik";
        public int rep;
        public int heat;
        public int loyalty;
        public float cash = 60f;           // CAD
        public List<string> flags = new List<string>();

        // body and backpack (docs/DESIGN.md 3M): 0-100, full = 100
        public float health = 100f, hunger = 100f, thirst = 100f;
        public List<LifeInDowntown.Items.InventorySlot> inventory = new List<LifeInDowntown.Items.InventorySlot>();
        public string equipped = "";       // weapon item id in hand ("" = none)
        public bool drawn;                 // weapon out, or holstered
        public int loaded;                 // rounds in the equipped weapon's magazine

        // Tarik's car (docs/DESIGN.md 3O.3): in his bay, or where he left it
        public bool carInGarage = true, carSaved;
        public UnityEngine.Vector3 carPos;
        public float carYaw;

        public string activeMission = "";
        public int objectiveIndex;
        public List<PhoneMessage> messages = new List<PhoneMessage>();
        public List<string> callLog = new List<string>();
        public int textsSent;              // between-jobs texts sent so far (DESIGN 3O.1)

        public static GameState Current { get; set; } = new GameState();

        public static void NewGame()
        {
            Current = new GameState { cash = LifeInDowntown.Items.ItemCatalog.StartCash };
            foreach (var entry in LifeInDowntown.Items.ItemCatalog.StartKit)   // "id" or "id:count"
            {
                var kv = entry.Split(':');
                LifeInDowntown.Items.Inventory.Add(Current, kv[0], kv.Length > 1 && int.TryParse(kv[1], out int n) ? n : 1);
            }
            LifeInDowntown.Items.Inventory.Equip(Current, "pistol_9mm");   // on his hip, holstered, a mag loaded (DESIGN 3O.6)
        }

        public bool HasFlag(string f) => flags.Contains(f);
        public void SetFlag(string f) { if (!flags.Contains(f)) flags.Add(f); }
        public void ClearFlag(string f) => flags.Remove(f);
    }

    [Serializable]
    public class PhoneMessage
    {
        public string from;     // contact id
        public string text;
        public string time;     // display time, e.g. "23:41"
    }
}
