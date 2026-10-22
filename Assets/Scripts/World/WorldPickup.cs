using System.Collections.Generic;
using LifeInDowntown.Game;
using LifeInDowntown.Items;
using UnityEngine;

namespace LifeInDowntown.World
{
    // An item lying in the world. Placed ones have an id so they stay taken across saves.
    public class WorldPickup : Interactable
    {
        public string itemId;
        public int count = 1;
        public string pickupId;        // "" for things the player dropped

        string Flag => "picked:" + pickupId;

        void Start()
        {
            if (!string.IsNullOrEmpty(pickupId) && GameState.Current.HasFlag(Flag)) gameObject.SetActive(false);
        }

        public override string Prompt
        {
            get
            {
                var d = ItemCatalog.Get(itemId);
                string n = d != null ? d.name : itemId;
                return count > 1 ? $"Pick up {n} x{count}" : $"Pick up {n}";
            }
        }

        public override void Interact()
        {
            var s = GameState.Current;
            int got = Inventory.Add(s, itemId, count);
            var d = ItemCatalog.Get(itemId);
            if (got == 0) { LifeInDowntown.Phone.PhoneController.Instance?.ShowBanner("BACKPACK FULL", $"No room for the {d?.name ?? itemId}."); return; }
            count -= got;
            LifeInDowntown.Phone.PhoneController.Instance?.ShowBanner("PICKED UP", $"{d?.name ?? itemId}{(got > 1 ? $" x{got}" : "")}");
            if (count > 0) return;
            if (!string.IsNullOrEmpty(pickupId)) s.SetFlag(Flag);
            if (string.IsNullOrEmpty(pickupId)) Destroy(gameObject); else gameObject.SetActive(false);
        }

        // something Tarik drops: a small bag on the ground in front of him
        public static WorldPickup Spawn(string itemId, int count, Vector3 at)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Dropped_" + itemId;
            Destroy(go.GetComponent<Collider>());
            go.transform.position = at + Vector3.up * 0.09f;
            go.transform.localScale = new Vector3(0.28f, 0.18f, 0.2f);
            var r = go.GetComponent<Renderer>();
            var shader = Shader.Find("HDRP/Lit");
            if (shader != null) { r.material = new Material(shader); r.material.SetColor("_BaseColor", new Color(0.85f, 0.83f, 0.78f)); }   // plastic bag
            var p = go.AddComponent<WorldPickup>();
            p.itemId = itemId; p.count = count; p.pickupId = "";
            return p;
        }
    }
}
