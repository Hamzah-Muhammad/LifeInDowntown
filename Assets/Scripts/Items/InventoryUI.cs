using LifeInDowntown.Game;
using LifeInDowntown.Player;
using MinniGameFramework.Player;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using UnityEngine;

namespace LifeInDowntown.Items
{
    // The backpack screen (docs/DESIGN.md 3M): Tab opens it, Esc or Tab closes it. A 5 x 4 slot grid with
    // cash, carried weight and the body bars on top; the selected item's details and actions (eat, drink,
    // equip, drop) on the right. At a shop counter the store's stock opens beside it. Tarik stands still
    // while it's open; the world keeps going.
    [RequireComponent(typeof(UIDocument))]
    public class InventoryUI : MonoBehaviour
    {
        public static InventoryUI Instance { get; private set; }
        public static bool IsOpen { get; private set; }
        // Esc that closed the backpack must not also open the pause menu in the same frame
        public static bool ConsumedEscThisFrame => IsOpen || closedFrame == Time.frameCount;
        static int closedFrame = -1;

        public BodycamPlayer player;

        VisualElement root, grid, shopCol, shopList, detailCol, weightFill, healthFill, foodFill, waterFill;
        Label cash, weightValue, healthValue, foodValue, waterValue, detailName, detailMeta, detailDesc, detailEffects, toast, shopTitle;
        Button useBtn, equipBtn, drawBtn, dropBtn;
        readonly Button[] slots = new Button[Inventory.Slots];
        int selected = -1;
        string shopId;
        float toastUntil;

        static GameState S => GameState.Current;

        void OnEnable()
        {
            Instance = this;
            root = GetComponent<UIDocument>().rootVisualElement.Q("inv-root");
            root.Clear();

            shopCol = Col(root, "col-shop");
            shopTitle = Text(shopCol, "col-title", "SHOP");
            shopSub = Text(shopCol, "col-sub", "Prices include HST");
            shopList = new ScrollView { name = "shop-list" }; shopList.AddToClassList("shop-list"); shopCol.Add(shopList);

            var pack = Col(root, "col-backpack");
            var header = Box(pack, "header");
            var left = Box(header, null);
            Text(left, "col-title", "BACKPACK");
            cash = Text(left, "cash", "");
            var bars = Box(header, "bars");
            weightFill = Bar(bars, "CARRY", "fill-weight", out weightValue);
            healthFill = Bar(bars, "HEALTH", "fill-health", out healthValue);
            foodFill = Bar(bars, "FOOD", "fill-food", out foodValue);
            waterFill = Bar(bars, "WATER", "fill-water", out waterValue);
            grid = Box(pack, "grid");
            for (int i = 0; i < slots.Length; i++)
            {
                int idx = i;
                var b = new Button(() => Select(idx));
                b.AddToClassList("slot");
                grid.Add(b);
                slots[i] = b;
            }
            Text(pack, "hint", "TAB / ESC  close      click an item to select");

            detailCol = Col(root, "col-detail");
            detailName = Text(detailCol, "detail-name", "");
            detailMeta = Text(detailCol, "detail-meta", "");
            detailDesc = Text(detailCol, "detail-desc", "");
            detailEffects = Text(detailCol, "detail-effects", "");
            useBtn = Action(detailCol, "Use", UseSelected);
            equipBtn = Action(detailCol, "Equip", EquipSelected);
            drawBtn = Action(detailCol, "Draw  (Q)", () => { MinniGameFramework.Combat.WeaponHolder.Instance?.ToggleDraw(); Refresh(); });
            dropBtn = Action(detailCol, "Drop", DropSelected);
            toast = Text(detailCol, "toast", "");
            Show(false);
        }

        void Update()
        {
            bool playing = GameFlow.Instance == null || GameFlow.Instance.State == GameFlow.FlowState.Playing;
            if (!playing) { if (IsOpen) Close(); return; }
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.tabKey.wasPressedThisFrame && !LifeInDowntown.Map.MapController.IsOpen) { if (IsOpen) Close(); else Open(null); }
                else if (IsOpen && kb.escapeKey.wasPressedThisFrame) Close();
            }
            if (!IsOpen) return;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
            Header();
            if (toastUntil > 0f && Time.unscaledTime > toastUntil) { toast.text = ""; toastUntil = 0f; }
        }

        public void OpenShop(string id, string seller = null) { sellerName = seller; Open(id); }
        string sellerName;            // a person, not a store (the corner dealers)
        Label shopSub;

        public void Open(string shop)
        {
            shopId = shop;
            IsOpen = true;
            Show(true);
            if (player != null) { player.moveEnabled = false; player.lookEnabled = false; }
            if (selected < 0) selected = 0;
            Refresh();
        }

        public void Close()
        {
            IsOpen = false;
            closedFrame = Time.frameCount;
            shopId = null; sellerName = null;
            Show(false);
            bool phoneUp = LifeInDowntown.Phone.PhoneController.Instance != null && LifeInDowntown.Phone.PhoneController.Instance.IsUp;
            if (player != null) { player.moveEnabled = true; player.lookEnabled = !phoneUp; }
            UnityEngine.Cursor.lockState = phoneUp ? CursorLockMode.None : CursorLockMode.Locked;
            UnityEngine.Cursor.visible = phoneUp;
        }

        void Show(bool on)
        {
            root.EnableInClassList("inv-root--on", on);
            shopCol.style.display = on && shopId != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ------------------------------------------------------------------ drawing
        void Refresh()
        {
            Header();
            for (int i = 0; i < slots.Length; i++) DrawSlot(i);
            DrawDetail();
            if (shopId != null) DrawShop();
        }

        void Header()
        {
            float kg = Inventory.Weight(S);
            cash.text = ItemCatalog.Money(S.cash);
            weightValue.text = $"{kg:0.0} / {Inventory.Comfortable:0} kg";
            weightFill.style.width = Length.Percent(Mathf.Clamp01(kg / Inventory.Comfortable) * 100f);
            weightFill.EnableInClassList("fill-weight--heavy", kg > Inventory.Comfortable);
            Fill(healthFill, healthValue, S.health); Fill(foodFill, foodValue, S.hunger); Fill(waterFill, waterValue, S.thirst);
        }

        static void Fill(VisualElement fill, Label value, float v)
        {
            fill.style.width = Length.Percent(Mathf.Clamp(v, 0f, 100f));
            value.text = $"{v:0}";
        }

        void DrawSlot(int i)
        {
            var b = slots[i];
            b.Clear();
            var slot = i < S.inventory.Count ? S.inventory[i] : null;
            var d = slot != null && !slot.Empty ? ItemCatalog.Get(slot.id) : null;
            b.EnableInClassList("slot--empty", d == null);
            b.EnableInClassList("slot--selected", i == selected);
            if (d == null) return;
            var strip = new VisualElement(); strip.AddToClassList("slot-strip"); strip.AddToClassList("cat-" + d.category); b.Add(strip);
            Text(b, "slot-name", d.name);
            Text(b, "slot-cat", d.category.ToUpperInvariant());
            if (slot.count > 1) Text(b, "slot-count", $"x{slot.count}");
            if (S.equipped == d.id) Text(b, "slot-tag", S.drawn ? "IN HAND" : "HOLSTERED");
        }

        void DrawDetail()
        {
            var slot = selected >= 0 && selected < S.inventory.Count ? S.inventory[selected] : null;
            var d = slot != null && !slot.Empty ? ItemCatalog.Get(slot.id) : null;
            detailName.text = d != null ? d.name : "Empty slot";
            detailMeta.text = d != null ? $"{d.category.ToUpperInvariant()}   ·   {d.weight * slot.count:0.00} kg" + (d.price > 0f ? $"   ·   {ItemCatalog.Money(d.price)}" : "") : "";
            detailDesc.text = d != null ? d.desc : "";
            detailEffects.text = d == null ? "" : Effects(d) + (d.IsWeapon && S.equipped == d.id ? $"Loaded {S.loaded}/{d.magazine}   ·   spare {Inventory.Count(S, d.ammo)}" : "");
            useBtn.text = d != null && d.category == "drink" ? "Drink" : d != null && d.category == "food" ? "Eat" : "Use";
            useBtn.EnableInClassList("action--off", d == null || !d.Usable);
            equipBtn.text = d != null && S.equipped == d.id ? "Unequip" : "Equip";
            equipBtn.EnableInClassList("action--off", d == null || !d.IsWeapon);
            drawBtn.text = S.drawn ? "Holster  (Q)" : "Draw  (Q)";
            drawBtn.EnableInClassList("action--off", d == null || S.equipped != d.id);
            dropBtn.EnableInClassList("action--off", d == null || !Inventory.CanDrop(d.id) || shopId != null);
        }

        static string Effects(ItemDef d)
        {
            var e = new System.Collections.Generic.List<string>();
            if (d.hunger != 0) e.Add($"FOOD {(d.hunger > 0 ? "+" : "")}{d.hunger}");
            if (d.thirst != 0) e.Add($"WATER {(d.thirst > 0 ? "+" : "")}{d.thirst}");
            if (d.health != 0) e.Add($"HEALTH +{d.health}");
            return e.Count > 0 ? string.Join("   ", e) + "\n" : "";
        }

        void DrawShop()
        {
            var shop = ItemCatalog.Shop(shopId);
            shopTitle.text = sellerName != null ? sellerName.ToUpperInvariant() : shop != null ? shop.name.ToUpperInvariant() : "SHOP";
            shopSub.text = sellerName != null ? "Cash only. No receipts." : "Prices include HST";
            shopList.Clear();
            if (shop == null) return;
            foreach (var id in shop.stock)
            {
                var d = ItemCatalog.Get(id);
                if (d == null) continue;
                var row = Box(shopList.contentContainer, "shop-row");
                var strip = new VisualElement(); strip.AddToClassList("shop-strip"); strip.AddToClassList("cat-" + d.category); row.Add(strip);
                Text(row, "shop-name", d.name);
                Text(row, "shop-price", ItemCatalog.Money(d.price));
                var buy = new Button(() => Buy(id)) { text = "Buy" };
                buy.AddToClassList("shop-buy");
                buy.EnableInClassList("shop-buy--off", S.cash + 1e-4f < d.price || Inventory.Room(S, id) < 1);
                row.Add(buy);
            }
        }

        // ------------------------------------------------------------------ actions
        void Select(int i) { selected = i; Refresh(); }

        string SelectedId => selected >= 0 && selected < S.inventory.Count && !S.inventory[selected].Empty ? S.inventory[selected].id : null;

        void UseSelected()
        {
            var id = SelectedId;
            var said = id != null ? Inventory.Use(S, id) : null;
            if (said != null) Toast(said);
            Refresh();
        }

        void EquipSelected()
        {
            var id = SelectedId;
            if (id != null && Inventory.Equip(S, id)) Toast(S.equipped == id ? $"{ItemCatalog.Get(id).name} on your hip. Q to draw." : "Put away.");
            Refresh();
        }

        void DropSelected()
        {
            var id = SelectedId;
            if (id == null || !Inventory.CanDrop(id) || player == null) return;
            int n = S.inventory[selected].count;
            Inventory.Remove(S, id, n);
            var at = player.transform.position + player.transform.forward * 0.8f;
            LifeInDowntown.World.WorldPickup.Spawn(id, n, at);
            Toast($"Dropped {ItemCatalog.Get(id).name}{(n > 1 ? $" x{n}" : "")}");
            Refresh();
        }

        void Buy(string id)
        {
            var d = ItemCatalog.Get(id);
            switch (Inventory.Buy(S, id))
            {
                case Inventory.BuyResult.Ok:
                    Toast($"Bought {d.name} for {ItemCatalog.Money(d.price)}");
                    S.heat += ItemCatalog.Shop(shopId)?.heat ?? 0;
                    break;
                case Inventory.BuyResult.NoCash: Toast($"Not enough cash for the {d.name}."); break;
                case Inventory.BuyResult.NoRoom: Toast("No room in the backpack."); break;
            }
            Refresh();
        }

        void Toast(string text) { toast.text = text; toastUntil = Time.unscaledTime + 3f; }

        // ------------------------------------------------------------------ element helpers
        static VisualElement Col(VisualElement parent, string cls) { var v = Box(parent, "col"); v.AddToClassList(cls); return v; }

        static VisualElement Box(VisualElement parent, string cls)
        {
            var v = new VisualElement();
            if (cls != null) v.AddToClassList(cls);
            parent.Add(v);
            return v;
        }

        static Label Text(VisualElement parent, string cls, string text)
        {
            var l = new Label(text);
            l.AddToClassList(cls);
            l.pickingMode = PickingMode.Ignore;
            parent.Add(l);
            return l;
        }

        static Button Action(VisualElement parent, string text, System.Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("action");
            parent.Add(b);
            return b;
        }

        static VisualElement Bar(VisualElement parent, string label, string fillClass, out Label value)
        {
            var row = Box(parent, "bar-row");
            Text(row, "bar-label", label);
            var bar = Box(row, "bar");
            var fill = Box(bar, "bar-fill"); fill.AddToClassList(fillClass);
            value = Text(row, "bar-value", "");
            return fill;
        }
    }
}
