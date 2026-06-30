using System.Collections.Generic;
using System.Reflection;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using Duckov.UI;
using UnityEngine;

namespace DuckovController.UI.Inventory.VerbMaps
{
    // NPC quest board. Inherits QuestViewVerbMap (Y→Btn_Sort). 3 tabs (Btn_Avaliable/Btn_Active/Btn_History)
    // cycled by RB/LB, not LT/RT. A = select focused QuestEntry. X = btn_Interact, which the game makes
    // context-sensitive: Accept on the Available tab, Complete (turn-in) on the Active tab once the
    // quest's tasks are finished. The X prompt label tracks that (Accept/Complete) and hides when the
    // button isn't actionable (no quest selected, or an Active quest whose tasks aren't done yet).
    //
    // The QuestCompletePanel reward modal that a turn-in opens is owned entirely by GridFocusController
    // (GridFocusController.QuestReward.cs) — it intercepts in Update and locks the board out, so this
    // map is never ticked while the modal is up. Hence no reward-claim handling lives here.
    internal sealed class QuestGiverViewVerbMap : QuestViewVerbMap
    {
        public override string ViewTypeName => "QuestGiverView";

        private const BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static readonly PromptEntry[] _promptsAccept = new[]
        {
            new PromptEntry(ButtonGlyph.A, "Select"),
            new PromptEntry(ButtonGlyph.X, "Accept"),
            new PromptEntry(ButtonGlyph.Y, "Sort"),
            // Tab switching (LB/RB) is hinted on the shared ViewTabs top bar.
        };

        private static readonly PromptEntry[] _promptsComplete = new[]
        {
            new PromptEntry(ButtonGlyph.A, "Select"),
            new PromptEntry(ButtonGlyph.X, "Complete"),
            new PromptEntry(ButtonGlyph.Y, "Sort"),
        };

        // No X row: no quest selected, or an Active quest whose tasks aren't finished (btn not interactable).
        private static readonly PromptEntry[] _promptsNoAction = new[]
        {
            new PromptEntry(ButtonGlyph.A, "Select"),
            new PromptEntry(ButtonGlyph.Y, "Sort"),
        };

        // Tab switching is RB/LB (3 tabs); don't claim the triggers.
        public override bool TryLT(GameObject? focus, InventoryVerbRouter router) => false;
        public override bool TryRT(GameObject? focus, InventoryVerbRouter router) => false;

        // X = accept / complete the currently selected quest (the game's btn_Interact does both,
        // branching on the active tab). No-ops harmlessly when the button isn't interactable.
        public override bool TryX(GameObject? focus, InventoryVerbRouter router)
        {
            var view = View.ActiveView;
            if (view == null) return false;
            if (!router.TryClickViewField(view, "btn_Interact")) return false;
            // Accept removes the entry from the list — force graph rebuild so focus re-picks a surviving entry.
            GridFocusController.Instance?.NotifyInventoryChanged();
            return true;
        }

        public override IReadOnlyList<PromptEntry> PromptsFor(GameObject? focus, InventoryVerbRouter router)
        {
            var view = View.ActiveView;
            return InteractKind(view) switch
            {
                1 => _promptsAccept,
                2 => _promptsComplete,
                _ => _promptsNoAction,
            };
        }

        // X glyph on the interact button (accept/complete), so the action reads on the button itself.
        private static readonly (string, ButtonGlyph)[] _hints = { ("btn_Interact", ButtonGlyph.X) };
        public override IReadOnlyList<(string FieldName, ButtonGlyph Glyph)> ButtonGlyphHints()
        {
            // Only advertise the glyph when the button is actually actionable.
            return InteractKind(View.ActiveView) == 0
                ? System.Array.Empty<(string, ButtonGlyph)>()
                : _hints;
        }

        // 0 = no actionable interact (button hidden / not interactable), 1 = Accept, 2 = Complete.
        // Reads the view's btnAcceptQuest/btnCompleteQuest flags (set by RefreshInteractButton) and the
        // live interactable state of btn_Interact (Complete is greyed until the quest's tasks finish).
        private static int InteractKind(View? view)
        {
            if (view == null) return 0;
            var t = view.GetType();
            var btn = t.GetField("btn_Interact", Flags)?.GetValue(view) as UnityEngine.UI.Button;
            if (btn == null || !btn.gameObject.activeInHierarchy || !btn.interactable) return 0;
            bool accept   = t.GetField("btnAcceptQuest", Flags)?.GetValue(view) as bool? ?? false;
            bool complete = t.GetField("btnCompleteQuest", Flags)?.GetValue(view) as bool? ?? false;
            if (accept) return 1;
            if (complete) return 2;
            return 0;
        }
    }
}
