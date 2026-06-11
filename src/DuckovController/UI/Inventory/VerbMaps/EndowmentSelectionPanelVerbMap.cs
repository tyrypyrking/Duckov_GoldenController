using System.Collections.Generic;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using Duckov.UI;
using UnityEngine;

namespace DuckovController.UI.Inventory.VerbMaps
{
    // EndowmentSelectionPanel ("choose talent"): a row of 5 EndowmentSelectionEntry
    // cards laid out like the difficulty picker, plus a Confirm button and an
    // Exit/cancel button. Controls:
    //   dpad/stick → move across the talent cards (Confirm/Exit excluded from nav)
    //   A          → select the focused card (click → SelectionIndicator + the
    //                description updates)
    //   X          → confirm (confirmButton)
    //   B          → exit (cancelButton)
    internal sealed class EndowmentSelectionPanelVerbMap : DefaultViewVerbMap
    {
        public override string ViewTypeName => "EndowmentSelectionPanel";

        private static readonly PromptEntry[] _prompts = new[]
        {
            new PromptEntry(ButtonGlyph.DPad, "Select"),
            new PromptEntry(ButtonGlyph.A, "Pick"),
            new PromptEntry(ButtonGlyph.X, "Confirm"),
        };

        // A = select the focused card. base.TryA (Transfer) clicks it
        // (EndowmentSelectionEntry is an IPointerClickHandler).

        public override bool TryX(GameObject? focus, InventoryVerbRouter router)
        {
            if (router == null) return true;
            var view = View.ActiveView;
            if (view != null) router.TryClickViewField(view, "confirmButton");
            return true;
        }

        public override bool TryB(GameObject? focus, InventoryVerbRouter router)
        {
            if (router == null) return false;
            var view = View.ActiveView;
            // base View.exitButton is null here — router's generic Close can't find it; click cancelButton directly.
            if (view != null && router.TryClickViewField(view, "cancelButton"))
                return true;
            return false; // fall through to the router's View.Close as a backstop
        }

        public override IReadOnlyList<PromptEntry> PromptsFor(GameObject? focus, InventoryVerbRouter router) => _prompts;

        // Show the X glyph on the Confirm button (mirrors the B/Esc exit hint).
        private static readonly (string, ButtonGlyph)[] _hints = { ("confirmButton", ButtonGlyph.X) };
        public override IReadOnlyList<(string FieldName, ButtonGlyph Glyph)> ButtonGlyphHints() => _hints;
    }
}
