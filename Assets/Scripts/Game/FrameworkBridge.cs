using LifeInDowntown.Items;
using MinniGameFramework.Core;
using UnityEngine;

namespace LifeInDowntown.Game
{
    // Plugs Life In Downtown into MinniGameFramework (docs/DESIGN.md 3S): the save state, the backpack as the
    // armory, the game flow, the phone banner, escalators, the crowd's sidewalks, indoor no-fire zones and pickups.
    // Installed before the first scene loads in a build, and when the editor loads (edit-mode tests and renders).
    public static class FrameworkBridge
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Install()
        {
            FrameworkHooks.State = () => GameState.Current;
            FrameworkHooks.Armory = new Backpack();
            FrameworkHooks.Playing = () => GameFlow.Instance == null || GameFlow.Instance.State == GameFlow.FlowState.Playing;
            FrameworkHooks.InputBlocked = () => InventoryUI.IsOpen || LifeInDowntown.Map.MapController.IsOpen
                                                || (LifeInDowntown.Phone.PhoneController.Instance != null && LifeInDowntown.Phone.PhoneController.Instance.IsUp);
            FrameworkHooks.Driving = () => LifeInDowntown.Vehicles.PlayerDriving.PlayerIsDriving;
            FrameworkHooks.Banner = (title, body) => LifeInDowntown.Phone.PhoneController.Instance?.ShowBanner(title, body);
            FrameworkHooks.WakeAtHome = () => GameFlow.Instance?.WakeAtHome();
            FrameworkHooks.Money = ItemCatalog.Money;
            FrameworkHooks.Overloaded = () => Inventory.Weight(GameState.Current) > Inventory.Comfortable;
            FrameworkHooks.GroundCarry = LifeInDowntown.Transit.Escalator.CarryAt;
            FrameworkHooks.SidewalkPoints = () =>
            {
                var crowd = LifeInDowntown.NPC.CrowdDirector.Instance != null ? LifeInDowntown.NPC.CrowdDirector.Instance : Object.FindFirstObjectByType<LifeInDowntown.NPC.CrowdDirector>();
                return crowd != null ? crowd.points : null;
            };
            FrameworkHooks.NoFireZone = LifeInDowntown.World.IndoorZone.IsIndoors;
            FrameworkHooks.MakeLoot = (go, item, count) =>
            {
                var pick = go.AddComponent<LifeInDowntown.World.WorldPickup>();
                pick.itemId = item; pick.count = count; pick.pickupId = "";
            };
        }

        // the backpack (DESIGN 3M) as the framework's armory
        class Backpack : IArmory
        {
            public int Count(string item) => string.IsNullOrEmpty(item) ? 0 : Inventory.Count(GameState.Current, item);
            public int Magazine(string weapon) => ItemCatalog.Get(weapon)?.magazine ?? 0;
            public string AmmoFor(string weapon) => ItemCatalog.Get(weapon)?.ammo ?? "";
            public int Reload() => Inventory.Reload(GameState.Current);
        }
    }
}
