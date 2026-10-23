using System;
using System.Collections.Generic;
using UnityEngine;

namespace LifeInDowntown.Items
{
    // Every item in the game and every shop's stock (docs/DESIGN.md 3M), from Resources/Items/items.json.
    [Serializable]
    public class ItemDef
    {
        public string id, name, category, desc;
        public float weight;          // kg per unit
        public int stack = 1;         // max units per slot
        public float price;           // CAD, tax in (0 = not sold)
        public int hunger, thirst, health;   // restored by eating / drinking / using one
        public string ammo;           // weapons: which item they fire
        public int magazine;          // weapons: rounds per magazine

        public bool IsKey => category == "key";
        public bool Usable => hunger != 0 || thirst != 0 || health != 0;
        public bool IsWeapon => category == "weapon";
    }

    [Serializable]
    public class ShopDef
    {
        public string id, name;
        public string[] stock;
        public int heat;              // HEAT per purchase (the corner dealers)
    }

    [Serializable]
    class CatalogFile
    {
        public ItemDef[] items;
        public ShopDef[] shops;
        public string[] startKit;
        public float startCash;
    }

    public static class ItemCatalog
    {
        static Dictionary<string, ItemDef> items;
        static Dictionary<string, ShopDef> shops;
        static CatalogFile file;

        static void Load()
        {
            if (items != null) return;
            var text = Resources.Load<TextAsset>("Items/items");
            file = text != null ? JsonUtility.FromJson<CatalogFile>(text.text) : new CatalogFile { items = new ItemDef[0], shops = new ShopDef[0], startKit = new string[0] };
            items = new Dictionary<string, ItemDef>();
            foreach (var i in file.items) items[i.id] = i;
            shops = new Dictionary<string, ShopDef>();
            foreach (var s in file.shops) shops[s.id] = s;
        }

        public static ItemDef Get(string id) { Load(); return id != null && items.TryGetValue(id, out var d) ? d : null; }
        public static ShopDef Shop(string id) { Load(); return id != null && shops.TryGetValue(id, out var s) ? s : null; }
        public static IEnumerable<ItemDef> All { get { Load(); return items.Values; } }
        public static string[] StartKit { get { Load(); return file.startKit; } }
        public static float StartCash { get { Load(); return file.startCash; } }

        public static string Money(float dollars) => $"${dollars:0.00}";
    }
}
