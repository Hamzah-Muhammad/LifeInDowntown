using System;
using LifeInDowntown.Game;
using UnityEngine;

namespace LifeInDowntown.Items
{
    [Serializable]
    public class InventorySlot
    {
        public string id = "";
        public int count;
        public bool Empty => string.IsNullOrEmpty(id) || count <= 0;
    }

    // Tarik's backpack (docs/DESIGN.md 3M): 20 slots, stacks per item, carried weight. Over 25 kg he's
    // slow and can't sprint; nothing more goes in past 35 kg. All rules live here so the UI, the shop,
    // pickups and the test share them. Key items (phone, keys) can't be dropped, sold or used up.
    public static class Inventory
    {
        public const int Slots = 20;
        public const float Comfortable = 25f, MaxCarry = 35f;

        static void Fit(GameState s)
        {
            while (s.inventory.Count < Slots) s.inventory.Add(new InventorySlot());
            foreach (var slot in s.inventory) if (slot.Empty) { slot.id = ""; slot.count = 0; }
        }

        public static float Weight(GameState s)
        {
            float kg = 0f;
            foreach (var slot in s.inventory)
                if (!slot.Empty && ItemCatalog.Get(slot.id) is ItemDef d) kg += d.weight * slot.count;
            return kg;
        }

        public static int Count(GameState s, string id)
        {
            int n = 0;
            foreach (var slot in s.inventory) if (slot.id == id) n += slot.count;
            return n;
        }

        // how many of an item still fit, by stack room and by the carry limit
        public static int Room(GameState s, string id)
        {
            var d = ItemCatalog.Get(id);
            if (d == null) return 0;
            Fit(s);
            int bySlots = 0;
            foreach (var slot in s.inventory)
                bySlots += slot.Empty ? d.stack : slot.id == id ? Mathf.Max(0, d.stack - slot.count) : 0;
            int byWeight = d.weight <= 0f ? int.MaxValue : Mathf.FloorToInt((MaxCarry - Weight(s)) / d.weight + 1e-4f);
            return Mathf.Max(0, Mathf.Min(bySlots, byWeight));
        }

        // returns how many went in (tops up existing stacks first, then empty slots)
        public static int Add(GameState s, string id, int count)
        {
            var d = ItemCatalog.Get(id);
            if (d == null || count <= 0) return 0;
            int left = Mathf.Min(count, Room(s, id));
            int added = left;
            foreach (var slot in s.inventory)
            {
                if (left == 0) break;
                if (slot.id != id) continue;
                int put = Mathf.Min(left, d.stack - slot.count);
                slot.count += put; left -= put;
            }
            foreach (var slot in s.inventory)
            {
                if (left == 0) break;
                if (!slot.Empty) continue;
                int put = Mathf.Min(left, d.stack);
                slot.id = id; slot.count = put; left -= put;
            }
            return added;
        }

        public static int Remove(GameState s, string id, int count)
        {
            int left = count;
            for (int i = s.inventory.Count - 1; i >= 0 && left > 0; i--)
            {
                var slot = s.inventory[i];
                if (slot.id != id) continue;
                int take = Mathf.Min(left, slot.count);
                slot.count -= take; left -= take;
                if (slot.count <= 0) { slot.id = ""; slot.count = 0; }
            }
            if (Count(s, s.equipped) == 0) { s.equipped = ""; s.drawn = false; }
            return count - left;
        }

        public static bool CanDrop(string id) => ItemCatalog.Get(id) is ItemDef d && !d.IsKey;

        // eat / drink / take: one unit, stats capped at 100. Returns what happened, or null if it can't be used.
        public static string Use(GameState s, string id)
        {
            var d = ItemCatalog.Get(id);
            if (d == null || !d.Usable || Count(s, id) == 0) return null;
            s.hunger = Mathf.Clamp(s.hunger + d.hunger, 0f, 100f);
            s.thirst = Mathf.Clamp(s.thirst + d.thirst, 0f, 100f);
            s.health = Mathf.Clamp(s.health + d.health, 0f, 100f);
            Remove(s, id, 1);
            string verb = d.category == "drink" ? "Drank" : d.category == "food" ? "Ate" : "Used";
            return $"{verb} {d.name}";
        }

        public enum BuyResult { Ok, NotSold, NoCash, NoRoom }

        public static BuyResult Buy(GameState s, string id)
        {
            var d = ItemCatalog.Get(id);
            if (d == null || d.price <= 0f) return BuyResult.NotSold;
            if (s.cash + 1e-4f < d.price) return BuyResult.NoCash;
            if (Room(s, id) < 1) return BuyResult.NoRoom;
            Add(s, id, 1);
            s.cash = Mathf.Round((s.cash - d.price) * 100f) / 100f;   // keep cents exact
            return BuyResult.Ok;
        }

        // weapons: equip (holstered), then draw/holster; ammo loads from loose rounds
        public static bool Equip(GameState s, string id)
        {
            var d = ItemCatalog.Get(id);
            if (d == null || !d.IsWeapon || Count(s, id) == 0) return false;
            if (s.equipped == id) { s.equipped = ""; s.drawn = false; return true; }   // equip again = put it away
            s.equipped = id; s.drawn = false;
            Reload(s);
            return true;
        }

        public static int Reload(GameState s)
        {
            var d = ItemCatalog.Get(s.equipped);
            if (d == null || string.IsNullOrEmpty(d.ammo)) return 0;
            int want = d.magazine - s.loaded;
            int got = Remove(s, d.ammo, Mathf.Max(0, want));
            s.loaded += got;
            return got;
        }

        public static void EnsureKeyItems(GameState s)
        {
            Fit(s);
            foreach (var id in ItemCatalog.StartKit)
                if (ItemCatalog.Get(id) is ItemDef d && d.IsKey && Count(s, id) == 0) Add(s, id, 1);
        }
    }
}
