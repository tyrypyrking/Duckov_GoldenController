using System.Collections.Generic;
using System.Reflection;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using Duckov.UI;
using UnityEngine;

namespace DuckovController.UI.Inventory.VerbMaps
{
    // NPC quest board. Inherits QuestViewVerbMap (Y→Btn_Sort). 3 tabs (Btn_Avaliable/Btn_Active/Btn_History) cycled by RB/LB, not LT/RT.
    // X = btn_Interact (Accept/Complete). A = select focused QuestEntry.
    // QuestCompletePanel modal (questCompletePanel field, runtime onClick): A = Claim All, B = Skip.
    internal sealed class QuestGiverViewVerbMap : QuestViewVerbMap
    {
        public override string ViewTypeName => "QuestGiverView";

        private const BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static readonly PromptEntry[] _prompts = new[]
        {
            new PromptEntry(ButtonGlyph.A, "Select"),
            new PromptEntry(ButtonGlyph.X, "Accept"),
            new PromptEntry(ButtonGlyph.Y, "Sort"),
            // Tab switching (LB/RB) is hinted on the shared ViewTabs top bar.
        };

        private static readonly PromptEntry[] _completePrompts = new[]
        {
            new PromptEntry(ButtonGlyph.A, "Claim All"),
            new PromptEntry(ButtonGlyph.B, "Skip"),
        };

        // Tab switching is RB/LB (3 tabs); don't claim the triggers.
        public override bool TryLT(GameObject? focus, InventoryVerbRouter router) => false;
        public override bool TryRT(GameObject? focus, InventoryVerbRouter router) => false;

        // A = Claim All while the reward overlay is up; otherwise the inherited
        // "select focused quest entry" behaviour.
        public override bool TryA(GameObject? focus, InventoryVerbRouter router)
        {
            var panel = GetActiveCompletePanel(View.ActiveView);
            if (panel != null)
            {
                // Swallow press during fade-in: TakeAll() mid-fade closes the panel instantly.
                if (!IsPanelInteractable(panel)) return true;
                return ClickPanelButton(panel, "takeAllButton");
            }
            return base.TryA(focus, router);
        }

        // B = Skip the reward overlay; otherwise let the router-global B fall
        // through (carry-cancel → close view).
        public override bool TryB(GameObject? focus, InventoryVerbRouter router)
        {
            var panel = GetActiveCompletePanel(View.ActiveView);
            if (panel != null)
            {
                if (!IsPanelInteractable(panel)) return true;
                return ClickPanelButton(panel, "skipButton");
            }
            return base.TryB(focus, router);
        }

        // X = accept / complete the currently selected quest. Suppressed while
        // the reward overlay is up (the quest list behind it isn't actionable).
        public override bool TryX(GameObject? focus, InventoryVerbRouter router)
        {
            var view = View.ActiveView;
            if (view == null) return false;
            if (GetActiveCompletePanel(view) != null) return false;
            if (!router.TryClickViewField(view, "btn_Interact")) return false;
            // Accept removes the entry from the list — force graph rebuild so focus re-picks a surviving entry.
            GridFocusController.Instance?.NotifyInventoryChanged();
            return true;
        }

        public override IReadOnlyList<PromptEntry> PromptsFor(GameObject? focus, InventoryVerbRouter router)
        {
            if (GetActiveCompletePanel(View.ActiveView) != null)
                return _completePrompts;
            return _prompts;
        }

        // Returns the questCompletePanel MonoBehaviour only when it is actually
        // shown (its GameObject is active in the hierarchy); null otherwise.
        private static MonoBehaviour? GetActiveCompletePanel(View? view)
        {
            if (view == null) return null;
            var f = view.GetType().GetField("questCompletePanel", Flags);
            var panel = f?.GetValue(view) as MonoBehaviour;
            if (panel == null) return null;
            return panel.gameObject.activeInHierarchy ? panel : null;
        }

        // True once mainFadeGroup IsShown && !IsShowingInProgress — gates against the press that opened the panel.
        private static bool IsPanelInteractable(MonoBehaviour panel)
        {
            var f = panel.GetType().GetField("mainFadeGroup", Flags);
            var fg = f?.GetValue(panel) as Component;
            if (fg == null) return true; // no fade group → assume ready
            var t = fg.GetType();
            try
            {
                var isShown = (bool)(t.GetProperty("IsShown")?.GetValue(fg) ?? false);
                var inProg = (bool)(t.GetProperty("IsShowingInProgress")?.GetValue(fg) ?? false);
                return isShown && !inProg;
            }
            catch { return true; }
        }

        // Panel wires onClick at runtime (Awake) — use onClick.Invoke(), not ExecuteEvents (no IPointerClickHandler).
        private static bool ClickPanelButton(MonoBehaviour panel, string fieldName)
        {
            var f = panel.GetType().GetField(fieldName, Flags);
            var btn = f?.GetValue(panel) as UnityEngine.UI.Button;
            if (btn != null && btn.interactable)
            {
                btn.onClick.Invoke();
                return true;
            }
            return false;
        }
    }
}
