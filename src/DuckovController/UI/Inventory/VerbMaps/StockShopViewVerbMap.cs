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
    //   A: select item (DoShopAction)
    //   X: select-focused + commit via interactionButton (replicates old
    //       TryViewSpecificXAction StockShop branch).
    //   Y: vanilla menu open/close (inherits default).
    //   B: cancel carry → dismiss shop details → close (router still handles
    //       details dismissal + close in Tick's B chain; map only owns carry-
    //       cancel piece since the further B priority is router-global).
    internal sealed class StockShopViewVerbMap : DefaultViewVerbMap
    {
        public override string ViewTypeName => "StockShopView";
        public override bool HorizontalPrompts => true;

        private static readonly PromptEntry[] _prompts = new[]
        {
            new PromptEntry(ButtonGlyph.A, "Details"),
            new PromptEntry(ButtonGlyph.X, "Quick Sell/Buy"),
            new PromptEntry(ButtonGlyph.Y, "Menu"),
            new PromptEntry(ButtonGlyph.Y, "Details (hold)"),
            // Tab switching (LB/RB) is hinted on the shared ViewTabs top bar.
        };

        public override bool TryA(GameObject? focus, InventoryVerbRouter router)
        {
            if (focus == null || router == null) return false;
            var view = View.ActiveView;
            if (view == null) return base.TryA(focus, router);

            var entry = focus.GetComponent<InventoryEntry>();
            if (entry != null && entry.Content != null)
            {
                // Player-side entry in shop: select for sale instead of Fast-Pick.
                router.DoShopAction(focus, view);
                return true;
            }
            if (focus.GetComponent("StockShopItemEntry") != null)
            {
                // Merchant-side entry: pointer-click selects.
                router.DoShopAction(focus, view);
                return true;
            }
            return base.TryA(focus, router);
        }

        public override bool TryX(GameObject? focus, InventoryVerbRouter router)
        {
            if (router == null) return false;
            var view = View.ActiveView;
            if (view == null) return false;

            // Select focused first so X acts on the hovered item.
            if (focus != null)
            {
                var entry = focus.GetComponent<InventoryEntry>();
                if (entry != null && entry.Content != null)
                {
                    var itemDisplay = focus.GetComponentInChildren<ItemDisplay>(false);
                    if (itemDisplay != null) ItemUIUtilities.Select(itemDisplay);
                }
                else if (focus.GetComponent("StockShopItemEntry") != null)
                {
                    PointerEventDispatcher.Click(focus);
                }
            }
            // Commit through interactionButton.
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var f = view.GetType().GetField("interactionButton", flags);
            var btn = f?.GetValue(view) as UnityEngine.UI.Button;
            if (btn != null && btn.interactable)
            {
                btn.onClick.Invoke();
                return true;
            }
            return false;
        }

        public override IReadOnlyList<PromptEntry> PromptsFor(GameObject? focus, InventoryVerbRouter router)
        {
            return _prompts;
        }
    }
}
