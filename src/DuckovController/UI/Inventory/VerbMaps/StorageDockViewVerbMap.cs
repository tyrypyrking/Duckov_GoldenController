using System.Collections.Generic;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using Duckov.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DuckovController.UI.Inventory.VerbMaps
{
    // StorageDock ("Wasp Pickup Locker"): a paged list of StorageDockEntry
    // claim cards. Each Entry(Clone) hosts a Display (ItemMetaDisplay icon) and
    // a child Button (the claim/take button, listeners=0 → its handler is a
    // runtime onClick listener and/or an IPointerClickHandler). Controls:
    //   dpad/stick → move between item entries (Exit/Next/Prev excluded, see
    //                GridFocusController.IsStorageDockChrome)
    //   A          → claim/take the focused item (clicks the entry's child Button)
    //   LB / RB    → previous / next page (btnPrevPage / btnNextPage)
    //   B          → exit (router-global View.Close; base View.exitButton resolves)
    // No operation menu, no X action, and no hint panel (PromptsFor is empty).
    internal sealed class StorageDockViewVerbMap : DefaultViewVerbMap
    {
        public override string ViewTypeName => "StorageDock";

        public override bool TryA(GameObject? focus, InventoryVerbRouter router)
        {
            if (focus == null || router == null) return false;

            // Focus may be the Button itself or the entry root; find defensively.
            var btn = focus.GetComponent<Button>()
                      ?? focus.GetComponentInChildren<Button>(false);
            if (btn == null) return true;
            if (!btn.interactable || !btn.gameObject.activeInHierarchy) return true;

            // Use ExecuteEvents: claim Button has listeners=0, handler is IPointerClickHandler (onClick.Invoke would miss it).
            var ped = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
            };
            ExecuteEvents.Execute(btn.gameObject, ped, ExecuteEvents.pointerClickHandler);
            return true;
        }

        // LB / RB flip pages via the serialized btnPrevPage / btnNextPage Buttons.
        public override bool TryLB(GameObject? focus, InventoryVerbRouter router)
        {
            if (router == null) return true;
            var view = View.ActiveView;
            if (view != null) router.TryClickViewField(view, "btnPrevPage");
            return true;
        }

        public override bool TryRB(GameObject? focus, InventoryVerbRouter router)
        {
            if (router == null) return true;
            var view = View.ActiveView;
            if (view != null) router.TryClickViewField(view, "btnNextPage");
            return true;
        }

        // No operation menu / no X action on the pickup locker.
        public override bool TryX(GameObject? focus, InventoryVerbRouter router) => true;

        // Empty prompt set → suppresses the ViewHintPanel (its visibility gate is
        // CurrentPrompts.Count > 0). The B-Exit glyph renders independently.
        public override IReadOnlyList<PromptEntry> PromptsFor(GameObject? focus, InventoryVerbRouter router)
            => System.Array.Empty<PromptEntry>();

        // No on-screen button-glyph hints (entries are pooled clones, not named
        // serialized fields; a per-entry A glyph is out of scope).
        public override IReadOnlyList<(string FieldName, ButtonGlyph Glyph)> ButtonGlyphHints()
            => System.Array.Empty<(string, ButtonGlyph)>();
    }
}
