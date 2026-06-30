using System.Collections.Generic;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using Duckov.UI;
using UnityEngine;

namespace DuckovController.UI.Inventory.VerbMaps
{
    // FormulasRegisterView (Duckov.UI) — the blueprint/formula register terminal. Same shape as the
    // key-register view: A fast-picks a blueprint into the slot, X = Register (submitButton →
    // OnSubmitButtonClicked → CraftingManager.UnlockFormula, which then shows the StrongNotification
    // "new craft" popup that GridFocusController.StrongNotification.cs dismisses on A/B). Without this
    // map the view fell through to DefaultViewVerbMap and showed no hint row unless focus sat on an
    // inventory entry, so the register affordance was invisible on a gamepad.
    internal sealed class FormulasRegisterViewVerbMap : DefaultViewVerbMap
    {
        public override string ViewTypeName => "FormulasRegisterView";
        public override bool HorizontalPrompts => true;

        private static readonly PromptEntry[] _prompts = new[]
        {
            new PromptEntry(ButtonGlyph.A, "Transfer"),
            new PromptEntry(ButtonGlyph.X, "Register"),
            new PromptEntry(ButtonGlyph.Y, "Menu"),
        };

        public override bool TryX(GameObject? focus, InventoryVerbRouter router)
        {
            if (router == null) return false;
            var view = View.ActiveView;
            if (view == null) return base.TryX(focus, router);
            router.TryClickViewField(view, "submitButton"); // no-op when nothing is slotted
            return true;
        }

        public override IReadOnlyList<PromptEntry> PromptsFor(GameObject? focus, InventoryVerbRouter router) => _prompts;

        private static readonly (string, ButtonGlyph)[] _hints = { ("submitButton", ButtonGlyph.X) };
        public override IReadOnlyList<(string FieldName, ButtonGlyph Glyph)> ButtonGlyphHints() => _hints;
    }
}
