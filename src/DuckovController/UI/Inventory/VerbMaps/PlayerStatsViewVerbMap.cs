using System.Collections.Generic;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using Duckov.UI;
using UnityEngine;

namespace DuckovController.UI.Inventory.VerbMaps
{
    // PlayerStatsView — read-only character-stats screen. It is a no-focus view (IsNoFocusView), so
    // there is no grid navigation and DefaultViewVerbMap returns no prompts (null focus → empty),
    // leaving the hint panel blank. The only action is to leave, so advertise a single B Exit row so
    // the panel still appears and the exit affordance is legible. B itself routes through the
    // router-global close (inherited TryB falls through), so no verb override is needed here.
    internal sealed class PlayerStatsViewVerbMap : DefaultViewVerbMap
    {
        public override string ViewTypeName => "PlayerStatsView";
        public override bool HorizontalPrompts => true;

        private static readonly PromptEntry[] _prompts = new[]
        {
            new PromptEntry(ButtonGlyph.B, "Exit"),
        };

        public override IReadOnlyList<PromptEntry> PromptsFor(GameObject? focus, InventoryVerbRouter router) => _prompts;
    }
}
