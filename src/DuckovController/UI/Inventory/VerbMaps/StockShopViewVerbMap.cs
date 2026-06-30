using System.Collections.Generic;
using System.Reflection;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using Duckov.UI;
using ItemStatsSystem;
using UnityEngine;

namespace DuckovController.UI.Inventory.VerbMaps
{
    // StockShopView — shop interaction.
    //   A: toggle the Details panel for the item under the cursor (no buy).
    //   X: buy/sell in ONE press, without toggling Details. If Details is open it commits the
    //      panel's selection (native interactionButton); otherwise it deals the cursor item
    //      directly (so it never opens Details). 4 eggs = 4 X presses.
    //   Y: vanilla menu open/close (inherits default) + Details (hold).
    //   B: cancel carry → dismiss shop details → close (router-global B chain in Tick).
    internal sealed class StockShopViewVerbMap : DefaultViewVerbMap
    {
        public override string ViewTypeName => "StockShopView";
        public override bool HorizontalPrompts => true;

        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly PromptEntry[] _prompts = new[]
        {
            new PromptEntry(ButtonGlyph.A, "Details"),
            new PromptEntry(ButtonGlyph.X, "Buy/Sell"),
            new PromptEntry(ButtonGlyph.Y, "Menu"),
            // Tab switching (LB/RB) is hinted on the shared ViewTabs top bar.
        };

        // A = toggle Details for the cursor item.
        public override bool TryA(GameObject? focus, InventoryVerbRouter router)
        {
            if (focus == null || router == null) return false;

            // Player-side item: toggle ItemUIUtilities selection (drives the view's Details panel).
            var entry = focus.GetComponent<InventoryEntry>();
            if (entry != null && entry.Content != null)
            {
                var itemDisplay = focus.GetComponentInChildren<ItemDisplay>(false);
                if (itemDisplay != null)
                {
                    if (ReferenceEquals(ItemUIUtilities.SelectedItemDisplay, itemDisplay))
                        ItemUIUtilities.Select(null);
                    else
                        ItemUIUtilities.Select(itemDisplay);
                    return true;
                }
            }

            // Merchant-side entry: native pointer-click already toggles selection (and handles the
            // locked-item unlock-confirm flow), which is exactly the Details toggle we want for A.
            if (focus.GetComponent("StockShopItemEntry") != null)
            {
                PointerEventDispatcher.Click(focus);
                return true;
            }

            return base.TryA(focus, router);
        }

        // X = buy/sell in one press without toggling Details.
        public override bool TryX(GameObject? focus, InventoryVerbRouter router)
        {
            if (router == null) return false;
            var view = View.ActiveView;
            if (view == null) return false;

            // Details open → commit the panel's selection via the native interaction button
            // (buy → BuyTask incl. affordability + sfx; sell → Sell + sfx + close). Acts on the
            // selection, exactly as requested, and does not re-toggle Details.
            var selProp = view.GetType().GetProperty("Selection", Flags);
            var selection = selProp?.GetValue(view) as Object;
            if (selection != null)
                return router.TryClickViewField(view, "interactionButton");

            // No Details → deal the cursor item directly so nothing opens.
            if (focus == null) return false;

            // Player item under cursor → sell.
            var entry = focus.GetComponent<InventoryEntry>();
            if (entry != null && entry.Content != null)
                return Sell(view, entry.Content);

            // Merchant item under cursor → buy.
            var merchant = focus.GetComponent("StockShopItemEntry");
            if (merchant != null)
                return Buy(view, focus, merchant);

            return false;
        }

        // Direct sell of a specific item: StockShopView.Target.Sell(item) + sell sfx.
        private static bool Sell(View view, Item item)
        {
            var shop = view.GetType().GetProperty("Target", Flags)?.GetValue(view);
            if (shop == null || item == null) return false;
            var sell = shop.GetType().GetMethod("Sell", Flags, null, new[] { typeof(Item) }, null);
            if (sell == null) return false;
            try
            {
                sell.Invoke(shop, new object[] { item });
                GameRef.PostAudio("UI/sell");
                return true;
            }
            catch (System.Exception e) { Log.Debug_($"StockShop X-sell: {e.Message}"); return false; }
        }

        // Direct buy of the cursor merchant entry. Unlocked → the view's native BuyTask (affordability
        // gate + buy sfx). Locked → native pointer-click drives the unlock-confirm flow (selects, so
        // Details shows the unlock UI — only for locked items, never for a normal quick-buy).
        private static bool Buy(View view, GameObject focus, Component merchant)
        {
            bool unlocked = false;
            try { unlocked = (bool)(merchant.GetType().GetMethod("IsUnlocked", Flags)?.Invoke(merchant, null) ?? false); }
            catch { }

            if (!unlocked)
            {
                PointerEventDispatcher.Click(focus);
                return true;
            }

            int itemTypeID;
            try
            {
                var target = merchant.GetType().GetProperty("Target", Flags)?.GetValue(merchant);
                itemTypeID = (int)(target?.GetType().GetProperty("ItemTypeID", Flags)?.GetValue(target) ?? -1);
            }
            catch { itemTypeID = -1; }
            if (itemTypeID < 0) return false;

            // Prefer the view's private BuyTask(int) — same path native quick-buy uses (sfx + clickblocker
            // + affordability). Fall back to StockShop.Buy(int,int) + manual sfx if its name ever changes.
            try
            {
                var buyTask = view.GetType().GetMethod("BuyTask", Flags, null, new[] { typeof(int) }, null);
                if (buyTask != null)
                {
                    buyTask.Invoke(view, new object[] { itemTypeID });
                    return true;
                }

                var shop = view.GetType().GetProperty("Target", Flags)?.GetValue(view);
                var buy = shop?.GetType().GetMethod("Buy", Flags, null, new[] { typeof(int), typeof(int) }, null);
                if (buy != null)
                {
                    buy.Invoke(shop, new object[] { itemTypeID, 1 });
                    GameRef.PostAudio("UI/buy");
                    return true;
                }
            }
            catch (System.Exception e) { Log.Debug_($"StockShop X-buy: {e.Message}"); }
            return false;
        }

        public override IReadOnlyList<PromptEntry> PromptsFor(GameObject? focus, InventoryVerbRouter router)
        {
            return _prompts;
        }
    }
}
