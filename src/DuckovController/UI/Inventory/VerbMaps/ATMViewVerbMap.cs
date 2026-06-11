using System.Collections.Generic;
using System.Reflection;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using Duckov.UI;
using UnityEngine;
using UnityEngine.UI;

namespace DuckovController.UI.Inventory.VerbMaps
{
    // ATMView — the deposit/withdraw terminal. One View with three swappable
    // FadeGroup panes (inactive ones are GameObject-inactive):
    //   Select      — Btn_Select_Save / Btn_Select_Draw (pick deposit or withdraw)
    //   Save / Draw — a DigitInputPanel keypad (Key_0..9, Clear, Backspace) plus
    //                 Btn_Max, Btn_Confirm and a pane quit button.
    //
    // Every control is a plain Button, so the FocusGraph auto-discovers them and
    // dpad navigates spatially; A activates the focused one (base.TryA → onClick).
    // Pane-aware extras:
    //   B → keypad pane: back to Select (ATMPanel.ShowSelectPanel), NOT close.
    //       Select pane: fall through so the router closes the view.
    //   X → Backspace,  Y → Max (act on the live keypad regardless of focus).
    internal sealed class ATMViewVerbMap : DefaultViewVerbMap
    {
        public override string ViewTypeName => "ATMView";

        private static readonly PromptEntry[] _selectPrompts =
        {
            new PromptEntry(ButtonGlyph.DPad, "Select"),
            new PromptEntry(ButtonGlyph.A, "Choose"),
        };

        private static readonly PromptEntry[] _keypadPrompts =
        {
            new PromptEntry(ButtonGlyph.DPad, "Move"),
            new PromptEntry(ButtonGlyph.A, "Press"),
            new PromptEntry(ButtonGlyph.X, "Backspace"),
            new PromptEntry(ButtonGlyph.Y, "Max"),
        };

        // Native EventSystem Submit already fires onClick on A; default Transfer would double-fire (keypad "1"→"11"). Consume only.
        public override bool TryA(GameObject? focus, InventoryVerbRouter router) => true;

        public override bool TryB(GameObject? focus, InventoryVerbRouter router)
        {
            if (router == null) return false;
            var view = View.ActiveView;
            if (view != null && ActiveDigitPanel(view) != null)
            {
                // In a keypad pane → return to the select screen instead of closing.
                var atm = view.GetComponentInChildren<ATMPanel>();
                if (atm != null) { atm.ShowSelectPanel(); return true; }
            }
            // Select pane → let the router's generic View.Close handle B (exit).
            return false;
        }

        public override bool TryX(GameObject? focus, InventoryVerbRouter router)
        {
            ClickKeypadButton(ActiveDigitPanel(View.ActiveView), "backspaceButton");
            return true;   // consume (no-op on the select pane, which has no keypad)
        }

        public override bool TryY(GameObject? focus, InventoryVerbRouter router)
        {
            var panel = ActiveDigitPanel(View.ActiveView);
            if (panel != null) { try { panel.Max(); } catch { /* best-effort */ } }
            return true;
        }

        // No per-view tabs / shoulder actions.
        public override bool TryLT(GameObject? focus, InventoryVerbRouter router) => true;
        public override bool TryRT(GameObject? focus, InventoryVerbRouter router) => true;

        public override IReadOnlyList<PromptEntry> PromptsFor(GameObject? focus, InventoryVerbRouter router)
            => ActiveDigitPanel(View.ActiveView) != null ? _keypadPrompts : _selectPrompts;

        // The live keypad: only the active Save/Draw pane has an active
        // DigitInputPanel; the Select pane has none (returns null).
        private static DigitInputPanel? ActiveDigitPanel(View? view)
            => view == null ? null : view.GetComponentInChildren<DigitInputPanel>(false);

        // Fire a private serialized Button field's onClick on the keypad (matches
        // how the game's own keypad buttons are wired — runtime onClick listeners).
        private static void ClickKeypadButton(DigitInputPanel? panel, string fieldName)
        {
            if (panel == null) return;
            try
            {
                var f = typeof(DigitInputPanel).GetField(fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (f?.GetValue(panel) is Button b && b.interactable) b.onClick.Invoke();
            }
            catch { /* best-effort */ }
        }
    }
}
