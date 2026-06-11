using System.Collections.Generic;
using DuckovController.UI.Inventory;
using DuckovController.UI.Prompts;
using UnityEngine;

namespace DuckovController.UI.Common
{
    internal interface IViewVerbMap
    {
        string ViewTypeName { get; }

        bool TryA(GameObject? focus, InventoryVerbRouter router);
        bool TryB(GameObject? focus, InventoryVerbRouter router);
        bool TryX(GameObject? focus, InventoryVerbRouter router);
        bool TryY(GameObject? focus, InventoryVerbRouter router);

        // Return true to consume; false falls through to router's default pane-jump.
        bool TryLT(GameObject? focus, InventoryVerbRouter router);
        bool TryRT(GameObject? focus, InventoryVerbRouter router);

        // Return true if handled. No default router behavior for shoulders (unlike LT/RT).
        bool TryLB(GameObject? focus, InventoryVerbRouter router);
        bool TryRB(GameObject? focus, InventoryVerbRouter router);

        // Called after _lastFocus updates. Views that auto-act on hover (e.g. NoteIndexView
        // synth-clicks to mirror the focused note) override this. Default no-op.
        void OnFocusChanged(GameObject? focus, InventoryVerbRouter router);

        // Per-frame analog hook (stick/trigger). Called each frame while this map is active. Default no-op.
        void TickView(GameObject? focus, InventoryVerbRouter router);

        // Router passed so prompts reflect live config (e.g. LootView X label depends on Rules.Enabled).
        IReadOnlyList<PromptEntry> PromptsFor(GameObject? focus, InventoryVerbRouter router);

        // True = horizontal strip layout for ViewHintPanel; false = vertical stack (default).
        bool HorizontalPrompts { get; }

        // (view field name, glyph) pairs — overlay glyph on named button when gamepad connected. Default empty.
        IReadOnlyList<(string FieldName, ButtonGlyph Glyph)> ButtonGlyphHints();
    }
}
