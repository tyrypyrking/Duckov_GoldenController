using System.Collections.Generic;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using Duckov.UI;
using UnityEngine;

namespace DuckovController.UI.Inventory.VerbMaps
{
    // BlackMarketView — buy / refresh / details.
    //   X: refresh via btn_refresh (replicates old TryViewSpecificXAction
    //       BlackMarket branch). A/B/Y inherit default behavior.
    //   Note: LT/RT/LB/RB tab swap stays in router.Tick — it's not a verb,
    //   it's pane navigation specific to this view.
    internal sealed class BlackMarketViewVerbMap : DefaultViewVerbMap
    {
        public override string ViewTypeName => "BlackMarketView";
        public override bool HorizontalPrompts => true;

        private static readonly PromptEntry[] _prompts = new[]
        {
            new PromptEntry(ButtonGlyph.A, "Buy"),
            new PromptEntry(ButtonGlyph.X, "Refresh"),
            new PromptEntry(ButtonGlyph.Y, "Menu"),
            // Triggers switch the demand/supply panes (router special-cases
            // BlackMarketView LT/RT → demand/supply); one combined pane row.
            new PromptEntry(ButtonGlyph.LT, ButtonGlyph.RT, "Pane"),
        };

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
