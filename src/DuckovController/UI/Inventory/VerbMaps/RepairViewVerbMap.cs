using System.Collections.Generic;
using System.Reflection;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using Duckov.UI;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.UI;

namespace DuckovController.UI.Inventory.VerbMaps
{
    // ItemRepairView: A selects item / second A repairs (RepairButton), X repairs all, LT/RT pane jump.
    // Transfer click = select (OnItemDisplayPointerClicked → ItemUIUtilities.Select); first A delegates to base.
    internal sealed class RepairViewVerbMap : DefaultViewVerbMap
    {
        public override string ViewTypeName => "ItemRepairView";

        private static readonly PromptEntry[] _prompts = new[]
        {
            new PromptEntry(ButtonGlyph.DPad, "Move"),
            new PromptEntry(ButtonGlyph.A, "Select / Repair"),
            new PromptEntry(ButtonGlyph.X, "Repair All"),
            new PromptEntry(ButtonGlyph.LT, ButtonGlyph.RT, "Pane"),
        };

        public override bool TryA(GameObject? focus, InventoryVerbRouter router)
        {
            if (focus == null || router == null) return false;
            var view = View.ActiveView;
            if (view == null) return true;

            var display = focus.GetComponentInChildren<ItemDisplay>(false);
            Item? focusedItem = display != null ? display.Target : null;
            bool isSelected = focusedItem != null && ReferenceEquals(focusedItem, ItemUIUtilities.SelectedItem);
            var repairBtn = ResolveButton(view, "repairButton");

            // Second press on the already-selected item commits the single repair.
            if (isSelected && repairBtn != null && repairBtn.interactable && repairBtn.gameObject.activeInHierarchy)
            {
                repairBtn.onClick.Invoke();
                return true;
            }

            // First press: select directly — Transfer is no-op (Movable=False). Select raises OnSelectionChanged → enables RepairButton.
            if (display != null) ItemUIUtilities.Select(display);
            return true;
        }

        public override bool TryX(GameObject? focus, InventoryVerbRouter router)
        {
            if (router == null) return true;
            var view = View.ActiveView;
            if (view == null) return true;

            // repairAllPanel wraps ItemRepair_RepairAllPanel; button is one level down.
            var panel = ResolveField(view, "repairAllPanel") as Component;
            var btn = panel != null ? ResolveButton(panel, "button") : null;
            if (btn != null && btn.interactable && btn.gameObject.activeInHierarchy)
                btn.onClick.Invoke();
            return true; // consume X regardless — no other X action on this screen
        }

        // No operation menu on the repair bench.
        public override bool TryY(GameObject? focus, InventoryVerbRouter router) => true;

        public override IReadOnlyList<PromptEntry> PromptsFor(GameObject? focus, InventoryVerbRouter router) => _prompts;

        private static readonly (string, ButtonGlyph)[] _hints =
        {
            ("repairButton",   ButtonGlyph.A),
            ("repairAllPanel", ButtonGlyph.X),
        };
        public override IReadOnlyList<(string FieldName, ButtonGlyph Glyph)> ButtonGlyphHints() => _hints;

        // reflection helpers
        private static object? ResolveField(object target, string fieldName)
            => target == null ? null : ReflectionUtil.WalkField(target.GetType(), fieldName)?.GetValue(target);

        private static Button? ResolveButton(object target, string fieldName)
            => ResolveField(target, fieldName) as Button;
    }
}
