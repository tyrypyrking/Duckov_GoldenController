using System.Collections.Generic;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using Duckov.UI;
using UnityEngine;

namespace DuckovController.UI.Inventory.VerbMaps
{
    // MapSelectionView (teleporter). Two phases in one view:
    //   Selection — a grid of MapSelectionEntry destination cards:
    //     dpad → move the focus (outline border), A → pick the focused card
    //            (opens the ConfirmIndicator overlay), B → exit the view.
    //   Confirm (ConfirmIndicator overlay shown):
    //     A → btnConfirm (travel), B → btnCancel (back to selection, NOT exit).
    // Phase is detected by the ConfirmIndicator child being active.
    internal sealed class MapSelectionViewVerbMap : DefaultViewVerbMap
    {
        public override string ViewTypeName => "MapSelectionView";

        private static readonly PromptEntry[] _selectPrompts = new[]
        {
            new PromptEntry(ButtonGlyph.DPad, "Select"),
            new PromptEntry(ButtonGlyph.A, "Choose"),
        };

        private static readonly PromptEntry[] _confirmPrompts = new[]
        {
            new PromptEntry(ButtonGlyph.A, "Confirm"),
            new PromptEntry(ButtonGlyph.B, "Cancel"),
        };

        public override bool TryA(GameObject? focus, InventoryVerbRouter router)
        {
            if (router == null) return false;
            var view = View.ActiveView;
            if (view != null && IsConfirmShown(view))
                return router.TryClickViewField(view, "btnConfirm");
            // Selection phase: click the focused destination card. MapSelectionEntry
            // is an IPointerClickHandler, which base.TryA → router.Transfer covers.
            return base.TryA(focus, router);
        }

        public override bool TryB(GameObject? focus, InventoryVerbRouter router)
        {
            if (router == null) return false;
            var view = View.ActiveView;
            if (view != null && IsConfirmShown(view))
                // Cancel the confirm overlay → back to selection (do NOT close the
                // view). Returning true tells the router to stop here.
                return router.TryClickViewField(view, "btnCancel");
            // Selection phase: let the router's generic View.Close handle B (exit).
            return false;
        }

        // No Smart-Take / operation menu on this screen.
        public override bool TryX(GameObject? focus, InventoryVerbRouter router) => true;
        public override bool TryY(GameObject? focus, InventoryVerbRouter router) => true;
        public override bool TryLT(GameObject? focus, InventoryVerbRouter router) => true;
        public override bool TryRT(GameObject? focus, InventoryVerbRouter router) => true;

        public override IReadOnlyList<PromptEntry> PromptsFor(GameObject? focus, InventoryVerbRouter router)
        {
            var view = View.ActiveView;
            return (view != null && IsConfirmShown(view)) ? _confirmPrompts : _selectPrompts;
        }

        // The ConfirmIndicator overlay (Btn_Confirm/Btn_Cancel) is a direct child
        // of the view root; it's inactive during selection and active while
        // confirming a destination.
        private static bool IsConfirmShown(View view)
        {
            var ci = view.transform.Find("ConfirmIndicator");
            return ci != null && ci.gameObject.activeInHierarchy;
        }
    }
}
