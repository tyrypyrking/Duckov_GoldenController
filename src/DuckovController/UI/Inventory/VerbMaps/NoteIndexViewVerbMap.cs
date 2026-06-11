using System.Collections.Generic;
using System.Reflection;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using UnityEngine;

namespace DuckovController.UI.Inventory.VerbMaps
{
    // NoteIndexView: OnFocusChanged synth-clicks NoteIndexView_Entry so D-pad nav loads the note (hover-only by default).
    // A is a no-op (note already shown); B falls through to router close.
    internal sealed class NoteIndexViewVerbMap : DefaultViewVerbMap
    {
        public override string ViewTypeName => "NoteIndexView";

        private static readonly PromptEntry[] _prompts = new[]
        {
            new PromptEntry(ButtonGlyph.DPad, "Browse"),
        };

        public override void OnFocusChanged(GameObject? focus, InventoryVerbRouter router)
        {
            if (focus == null) return;
            // Guard: whitelist gates focus, but double-check to avoid accidental click on sibling buttons.
            var entry = focus.GetComponent("NoteIndexView_Entry");
            if (entry == null) return;
            // Skip the synth-click when this entry's note is already displayed: clicking re-runs
            // SetDisplayTargetNote → SetNoteRead → onNoteStatusChanged → RefreshEntries, which reshuffles
            // the LIFO pool and drags our focused GO to the mirror slot. The reconcile re-pins us back
            // here every frame, so without this guard the click→reshuffle storms continuously.
            if (IsAlreadyDisplaying(entry)) return;
            PointerEventDispatcher.Click(focus);
        }

        // True when the focused entry's note == NoteIndexView.displayingNote (already in the inspector).
        private static bool IsAlreadyDisplaying(Component entry)
        {
            var keyProp = entry.GetType().GetProperty("key",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (keyProp == null) return false;
            string? entryKey;
            try { entryKey = keyProp.GetValue(entry) as string; }
            catch { return false; } // key getter dereferences note.key; treat un-Setup entry as "not displaying"
            if (string.IsNullOrEmpty(entryKey)) return false;

            var view = entry.GetComponentInParent(typeof(Duckov.UI.View)) as Component;
            if (view == null || view.GetType().Name != "NoteIndexView") return false;
            var f = DuckovController.ReflectionUtil.WalkField(view.GetType(), "displayingNote");
            return f?.GetValue(view) as string == entryKey;
        }

        public override bool TryA(GameObject? focus, InventoryVerbRouter router) => false;

        public override IReadOnlyList<PromptEntry> PromptsFor(GameObject? focus, InventoryVerbRouter router) => _prompts;
    }
}
