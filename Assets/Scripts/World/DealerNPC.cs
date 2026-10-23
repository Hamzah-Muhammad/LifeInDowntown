using LifeInDowntown.Game;
using LifeInDowntown.NPC;
using MinniGameFramework.NPC;
using UnityEngine;

namespace LifeInDowntown.World
{
    // A corner dealer (docs/DESIGN.md 3O): the lookout of each corner crew. E opens what he's holding next to the
    // backpack (the shop screen, the "corner" stock); buying from him raises HEAT. He won't deal with police in
    // sight, or with a gun out.
    public class DealerNPC : Interactable
    {
        public string nickname = "Dre";
        public float policeRange = 25f;

        public override string Prompt => $"Talk to {nickname}";

        public override void Interact()
        {
            var s = GameState.Current;
            string refuse = null;
            if (s.drawn) refuse = "Woah, woah. Put that away, fam.";
            else
                foreach (var p in Pedestrian.All)
                    if (p.role == Pedestrian.Role.Police && Vector3.Distance(p.transform.position, transform.position) < policeRange)
                    { refuse = "Not with 12 right there. Walk with me later."; break; }
            if (refuse != null) { LifeInDowntown.Phone.PhoneController.Instance?.ShowBanner(nickname.ToUpperInvariant(), refuse); return; }
            LifeInDowntown.Phone.PhoneController.Instance?.ShowBanner(nickname.ToUpperInvariant(), s.HasFlag("met_dealer:" + nickname) ? "Back again? Say less." : "You look new. What you need?");
            s.SetFlag("met_dealer:" + nickname);
            LifeInDowntown.Items.InventoryUI.Instance?.OpenShop("corner", nickname);
        }
    }
}
