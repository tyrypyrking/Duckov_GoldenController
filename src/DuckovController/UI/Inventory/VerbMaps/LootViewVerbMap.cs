using System.Collections.Generic;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using UnityEngine;

namespace DuckovController.UI.Inventory.VerbMaps
{
    // LootView — Take / Smart-Take filter / Menu / Pane jump.
    // Same verb behavior as Default; only prompts differ. The X label reflects
    // the Smart Loot master toggle: "Take All Filter" when the rule engine is
    // on, plain "Take All" when it's off (X then fires vanilla take-all).
    internal sealed class LootViewVerbMap : DefaultViewVerbMap
    {
        public override string ViewTypeName => "LootView";

        private static readonly PromptEntry[] _promptsFiltered = new[]
        {
            new PromptEntry(ButtonGlyph.A, "Take"),
            new PromptEntry(ButtonGlyph.X, "Take All Filter"),
            new PromptEntry(ButtonGlyph.Y, "Menu"),
            new PromptEntry(ButtonGlyph.Y, "Details (hold)"),
            new PromptEntry(ButtonGlyph.LT, ButtonGlyph.RT, "Pane"),
        };

        private static readonly PromptEntry[] _promptsPlain = new[]
        {
            new PromptEntry(ButtonGlyph.A, "Take"),
            new PromptEntry(ButtonGlyph.X, "Take All"),
            new PromptEntry(ButtonGlyph.Y, "Menu"),
            new PromptEntry(ButtonGlyph.Y, "Details (hold)"),
            new PromptEntry(ButtonGlyph.LT, ButtonGlyph.RT, "Pane"),
        };

        public override IReadOnlyList<PromptEntry> PromptsFor(GameObject? focus, InventoryVerbRouter router)
        {
            // Smart Loot on (default) → filtered take-all; off → vanilla take-all.
            bool filtered = router.Rules?.Enabled ?? true;
            return filtered ? _promptsFiltered : _promptsPlain;
        }
    }
}
