using System;
using System.Collections.Generic;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using Duckov.UI;
using UnityEngine;

namespace DuckovController.UI.Inventory.VerbMaps
{
    // Default verb map — covers the generic inventory case (CharacterView,
    // EquipmentView, plain inventory windows). Used as the registry's
    // fallback via SetDefault, so ViewTypeName is "*" (unused as a key).
    //
    // Replicates the previous default branches of the router's switch chains:
    //   A → Transfer (Fast-Pick)
    //   B → cancel carry if carrying
    //   X → TrySmartTake
    //   Y → OpenOperationMenu / CloseOperationMenu toggle
    internal class DefaultViewVerbMap : IViewVerbMap
    {
        public virtual string ViewTypeName => "*";

        private static readonly PromptEntry[] _prompts = new[]
        {
            new PromptEntry(ButtonGlyph.A, "Transfer"),
            new PromptEntry(ButtonGlyph.Y, "Menu"),
        };

        // INV-1 — same as _prompts plus a "hold Y → Details" row, used when the
        // active view embeds a Details panel (ActiveViewHasDetailsPanel).
        private static readonly PromptEntry[] _promptsWithDetails = new[]
        {
            new PromptEntry(ButtonGlyph.A, "Transfer"),
            new PromptEntry(ButtonGlyph.Y, "Menu"),
            new PromptEntry(ButtonGlyph.Y, "Details (hold)"),
        };

        private static readonly PromptEntry[] _empty = Array.Empty<PromptEntry>();

        public virtual bool TryA(GameObject? focus, InventoryVerbRouter router)
        {
            if (focus == null || router == null) return false;
            // Do NOT pre-gate on InventoryEntry — Transfer handles the non-entry fallback chain too
            // (Button.onClick, StockShopItemEntry, IPointerClickHandler); gating dropped it and broke A on action buttons.
            router.Transfer(focus);
            return true;
        }

        public virtual bool TryB(GameObject? focus, InventoryVerbRouter router)
        {
            if (router == null) return false;
            if (router.Carry.Current == InventoryCarryState.Phase.Carrying)
            {
                router.Carry.Cancel();
                return true;
            }
            return false;
        }

        public virtual bool TryX(GameObject? focus, InventoryVerbRouter router)
        {
            if (router == null) return false;
            router.TrySmartTake(focus);
            // Silent no-op on unsupported views; consume so router doesn't fall back.
            return true;
        }

        public virtual bool TryY(GameObject? focus, InventoryVerbRouter router)
        {
            if (router == null) return false;
            if (router.IsOperationMenuOpen())
            {
                router.CloseOperationMenu();
                return true;
            }
            if (focus == null) return false;
            router.OpenOperationMenu(focus);
            return true;
        }

        // Default LT/RT: no per-view tab. Falls through to router pane jump.
        public virtual bool TryLT(GameObject? focus, InventoryVerbRouter router) => false;
        public virtual bool TryRT(GameObject? focus, InventoryVerbRouter router) => false;

        // Default LB/RB: unused (no shoulder action). CraftView overrides for
        // section tabs.
        public virtual bool TryLB(GameObject? focus, InventoryVerbRouter router) => false;
        public virtual bool TryRB(GameObject? focus, InventoryVerbRouter router) => false;

        // Default focus-change: no auto-act.
        public virtual void OnFocusChanged(GameObject? focus, InventoryVerbRouter router) { }

        // Default: no per-frame work.
        public virtual void TickView(GameObject? focus, InventoryVerbRouter router) { }

        public virtual IReadOnlyList<PromptEntry> PromptsFor(GameObject? focus, InventoryVerbRouter router)
        {
            if (focus == null) return _empty;
            if (focus.GetComponent<InventoryEntry>() == null) return _empty;
            // Advertise hold-Y Details only where it actually does something — a view
            // that embeds an ItemDetailsDisplay panel (INV-1). Probe is cached.
            if (router != null && router.ActiveViewHasDetailsPanel())
                return _promptsWithDetails;
            return _prompts;
        }

        // Vertical prompt stack by default; trader / craft maps override → true.
        public virtual bool HorizontalPrompts => false;

        // No on-screen button-glyph hints by default; views that want them
        // (e.g. X on a Confirm button) override this.
        public virtual IReadOnlyList<(string FieldName, ButtonGlyph Glyph)> ButtonGlyphHints()
            => System.Array.Empty<(string, ButtonGlyph)>();
    }
}
