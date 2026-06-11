using System.Collections.Generic;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using Duckov.UI;
using UnityEngine;
using UnityEngine.UI;

namespace DuckovController.UI.Inventory.VerbMaps
{
    // PerkTreeView ("choose talent"): a spatial grid of PerkEntry nodes plus a
    // NodeInspector (PerkDetails) that shows the focused perk and its action
    // button. Controls:
    //   dpad/stick → move between perks (PerkGrid pane, FocusGraph spatial nav)
    //   A          → select the focused perk (click it → inspector updates)
    //   X          → confirm: click the inspector's active action button
    //                (ActivateButton / BeginButton, whichever the perk state
    //                 currently shows)
    //   B          → exit (router-global View.Close)
    internal sealed class PerkTreeViewVerbMap : DefaultViewVerbMap
    {
        public override string ViewTypeName => "PerkTreeView";

        private static readonly PromptEntry[] _prompts = new[]
        {
            new PromptEntry(ButtonGlyph.DPad, "Move"),
            new PromptEntry(ButtonGlyph.A, "Select"),
            new PromptEntry(ButtonGlyph.X, "Confirm"),
        };

        // A = select the focused perk. Default Transfer clicks the focused
        // PerkEntry (an IPointerClickHandler), which drives the inspector.
        // (base.TryA handles entry + non-entry click fallbacks.)

        public override bool TryX(GameObject? focus, InventoryVerbRouter router)
        {
            var view = View.ActiveView;
            var confirm = view != null ? FindActivePerkActionButton(view) : null;
            if (confirm != null)
                DuckovController.UI.PointerEventDispatcher.Click(confirm);
            return true; // consume X on this view regardless (no Smart-Take)
        }

        public override IReadOnlyList<PromptEntry> PromptsFor(GameObject? focus, InventoryVerbRouter router) => _prompts;

        // Inspector at Content/NodeInspector. Mutually-exclusive action buttons (ActivateButton/BeginButton/…);
        // prefer known names, else first active interactable button (excluding scrollbars).
        private static GameObject? FindActivePerkActionButton(View view)
        {
            var inspector = view.transform.Find("Content/NodeInspector");
            if (inspector == null) return null;
            GameObject? fallback = null;
            foreach (var b in inspector.GetComponentsInChildren<Button>(includeInactive: false))
            {
                if (b == null || !b.interactable || !b.gameObject.activeInHierarchy) continue;
                if (b.GetComponent<Scrollbar>() != null) continue;
                var n = b.gameObject.name;
                if (n == "ActivateButton" || n == "BeginButton") return b.gameObject;
                fallback ??= b.gameObject;
            }
            return fallback;
        }
    }
}
