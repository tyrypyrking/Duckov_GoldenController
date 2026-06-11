using System.Collections.Generic;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using Duckov.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DuckovController.UI.Inventory.VerbMaps
{
    // QuestView — Active/History tab cycling on LT/RT, sort cycle on Y, A
    // opens the focused quest (two-pane focus toggle is deferred; for now A
    // synthesizes a pointer click on the quest entry so the right-side
    // QuestViewDetails populates).
    //
    // Per-tab structure (from QuestView_*.txt dump):
    //   Content/Selection/Tabs/Btn_Active     ← In progress
    //   Content/Selection/Tabs/Btn_History    ← Completed
    //   Content/Selection/SortingBar/Btn_Sort ← QuestSortButton
    // Not sealed: QuestGiverViewVerbMap inherits the Btn_Sort (Y) logic and
    // the ClickByDescendantName helper.
    internal class QuestViewVerbMap : DefaultViewVerbMap
    {
        public override string ViewTypeName => "QuestView";

        private static readonly PromptEntry[] _prompts = new[]
        {
            new PromptEntry(ButtonGlyph.A, "Open"),
            new PromptEntry(ButtonGlyph.Y, "Sort"),
            new PromptEntry(ButtonGlyph.LT, "Active"),
            new PromptEntry(ButtonGlyph.RT, "History"),
        };

        public override bool TryY(GameObject? focus, InventoryVerbRouter router)
        {
            var view = View.ActiveView;
            if (view == null) return false;
            return router.TryClickViewField(view, "Btn_Sort")
                || ClickByDescendantName(view.gameObject, "Btn_Sort");
        }

        public override bool TryLT(GameObject? focus, InventoryVerbRouter router)
        {
            var view = View.ActiveView;
            if (view == null) return false;
            return ClickByDescendantName(view.gameObject, "Btn_Active");
        }

        public override bool TryRT(GameObject? focus, InventoryVerbRouter router)
        {
            var view = View.ActiveView;
            if (view == null) return false;
            return ClickByDescendantName(view.gameObject, "Btn_History");
        }

        public override IReadOnlyList<PromptEntry> PromptsFor(GameObject? focus, InventoryVerbRouter router) => _prompts;

        // Walk the View's hierarchy for a button-bearing GameObject whose
        // name matches. Used for tab buttons that aren't serialized fields.
        private static bool ClickByDescendantName(GameObject root, string targetName)
        {
            var t = root.transform;
            return Walk(t, targetName);
        }

        private static bool Walk(Transform t, string targetName)
        {
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (c.name == targetName && c.gameObject.activeInHierarchy)
                {
                    // Prefer ExecuteEvents: Btn_Sort has 0 persistent onClick listeners but a
                    // QuestSortButton IPointerClickHandler that does the work. pointerClickHandler
                    // covers both without double-firing.
                    var click = c.GetComponent<IPointerClickHandler>();
                    if (click != null)
                    {
                        var ped = new PointerEventData(EventSystem.current)
                        {
                            button = PointerEventData.InputButton.Left,
                        };
                        ExecuteEvents.Execute(c.gameObject, ped, ExecuteEvents.pointerClickHandler);
                        return true;
                    }
                    // Fallback for targets that are a plain Button with no
                    // IPointerClickHandler surface (rare).
                    var btn = c.GetComponent<UnityEngine.UI.Button>();
                    if (btn != null && btn.interactable)
                    {
                        btn.onClick.Invoke();
                        return true;
                    }
                }
                if (Walk(c, targetName)) return true;
            }
            return false;
        }
    }
}
