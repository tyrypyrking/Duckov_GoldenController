using System.Collections.Generic;
using System.Reflection;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using Duckov.UI;
using UnityEngine;

namespace DuckovController.UI.Inventory.VerbMaps
{
    // BlackMarketView — buy / refresh / details.
    //   A: deal (buy on supply pane / sell on demand pane) via the focused entry's dealButton.
    //   X: refresh via btn_refresh (replicates old TryViewSpecificXAction BlackMarket branch).
    //   B/Y inherit default behavior.
    //   Note: LT/RT/LB/RB tab swap stays in router.Tick — it's not a verb,
    //   it's pane navigation specific to this view.
    internal sealed class BlackMarketViewVerbMap : DefaultViewVerbMap
    {
        public override string ViewTypeName => "BlackMarketView";
        public override bool HorizontalPrompts => true;

        private static readonly PromptEntry[] _prompts = new[]
        {
            new PromptEntry(ButtonGlyph.A, "Buy/Sell"),
            new PromptEntry(ButtonGlyph.X, "Refresh"),
            new PromptEntry(ButtonGlyph.Y, "Menu"),
            // Triggers switch the demand/supply panes (router special-cases
            // BlackMarketView LT/RT → demand/supply); one combined pane row.
            new PromptEntry(ButtonGlyph.LT, ButtonGlyph.RT, "Pane"),
        };

        // The focus node in BlackMarket is the entry's inner Button (FocusGraph whitelists the Button,
        // not the SupplyPanel_Entry/DemandPanel_Entry itself). DefaultViewVerbMap.TryA → router.Transfer
        // can't act on these (no InventoryEntry / IPointerClickHandler) → silent no-op despite the "Buy"
        // prompt. Fire the deal directly: walk up to the entry and invoke its private `dealButton`
        // (→ OnDealButtonClicked → onDealButtonClicked → BlackMarket buy/sell). One press = one deal.
        public override bool TryA(GameObject? focus, InventoryVerbRouter router)
        {
            if (focus == null) return false;

            // Find the SupplyPanel_Entry/DemandPanel_Entry on the focus or any ancestor (focus is the
            // entry's inner Button). Match by type name to avoid a hard reference to the game's UI types.
            Component? entry = null;
            var t = focus.transform;
            while (t != null && entry == null)
            {
                foreach (var c in t.GetComponents<Component>())
                {
                    if (c == null) continue;
                    var n = c.GetType().Name;
                    if (n == "SupplyPanel_Entry" || n == "DemandPanel_Entry") { entry = c; break; }
                }
                t = t.parent;
            }

            if (entry != null)
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                var f = entry.GetType().GetField("dealButton", flags);
                var btn = f?.GetValue(entry) as UnityEngine.UI.Button;
                if (btn != null && btn.interactable)
                {
                    btn.onClick.Invoke();
                    return true;
                }
                return false; // entry found but not dealable (can't afford / locked) — consume, no fallthrough.
            }

            // Focus is itself a button (e.g. the dealButton GO directly) — click it.
            if (focus.GetComponent<UnityEngine.UI.Button>() is { interactable: true } b)
            {
                b.onClick.Invoke();
                return true;
            }
            return false;
        }

        public override bool TryX(GameObject? focus, InventoryVerbRouter router)
        {
            if (router == null) return false;
            var view = View.ActiveView;
            if (view == null) return false;
            return router.TryClickViewField(view, "btn_refresh");
        }

        public override IReadOnlyList<PromptEntry> PromptsFor(GameObject? focus, InventoryVerbRouter router)
        {
            return _prompts;
        }
    }
}
