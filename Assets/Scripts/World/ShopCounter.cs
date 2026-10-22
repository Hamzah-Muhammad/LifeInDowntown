using System.Collections.Generic;
using LifeInDowntown.Game;
using LifeInDowntown.Items;
using UnityEngine;

namespace LifeInDowntown.World
{
    // A shop counter: E opens the shop's stock next to the backpack.
    public class ShopCounter : Interactable
    {
        public string shopId = "rabba";
        public override string Prompt => $"Shop at {ItemCatalog.Shop(shopId)?.name ?? "the counter"}";
        public override void Interact() => InventoryUI.Instance?.OpenShop(shopId);
    }
}
