using System.Collections.Generic;
using DuckovController.UI;
using DuckovController.UI.Builder;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using UnityEngine;

namespace DuckovController.UI.Inventory.VerbMaps
{
    // BuilderView (base building). Browse: D-pad navigates the catalog, A (native
    // EventSystem Submit) begins placing, RS targets a building, X recycles it,
    // LS pans (native), LT/RT zoom, B exits. Placing: RS moves the ghost, A places,
    // Y rotates, B cancels. Analog work + verbs live in BuilderNavigator.
    internal sealed class BuilderViewVerbMap : DefaultViewVerbMap
    {
        public override string ViewTypeName => "BuilderView";

        private readonly BuilderNavigator _nav = new BuilderNavigator();

        // Self-register for static teardown access. Re-newed on each mod activation
        // (RegisterAllViewMaps); the latest instance wins.
        private static BuilderViewVerbMap? _instance;
        private static bool _subscribedViewChange;
        public BuilderViewVerbMap()
        {
            _instance = this;
            // Tear down the reticle on EVERY BuilderView exit — controller B, mouse,
            // Esc, scene change — not just the controller path. TickView (and thus the
            // in-Resolve teardown) stops running once the view closes, so the gold ring
            // used to linger on the HUD (PT-5). One process-lifetime subscription
            // (statics persist across mod enable/disable; guard prevents duplicates).
            if (!_subscribedViewChange)
            {
                _subscribedViewChange = true;
                Duckov.UI.View.OnActiveViewChanged += OnActiveViewChanged;
            }
        }

        // Fires whenever the active View changes. Any exit from BuilderView lands here
        // with ActiveView no longer the builder → destroy the reticle. Idempotent and
        // cheap (BuilderCursor.Destroy null-checks; EnsureCreated re-creates on reopen),
        // so firing on unrelated view changes is harmless.
        private static void OnActiveViewChanged()
        {
            var v = Duckov.UI.View.ActiveView;
            if (v == null || v.GetType().Name != "BuilderView")
                _instance?._nav.DestroyOverlay();
        }

        // Called from ModBehaviour.OnBeforeDeactivate to destroy the reticle GO
        // (it is parented on game-side UI and outlives the mod's GameObject).
        internal static void OnModDeactivate() { _instance?._nav.DestroyOverlay(); }

        private static readonly PromptEntry[] _browsePrompts = new[]
        {
            new PromptEntry(ButtonGlyph.LStick, "Pan"),
            new PromptEntry(ButtonGlyph.RStick, "Cursor"),
            new PromptEntry(ButtonGlyph.A, "Select"),
            new PromptEntry(ButtonGlyph.X, "Recycle"),
            new PromptEntry(ButtonGlyph.LT, "Zoom -"),
            new PromptEntry(ButtonGlyph.RT, "Zoom +"),
        };

        private static readonly PromptEntry[] _placingPrompts = new[]
        {
            new PromptEntry(ButtonGlyph.RStick, "Move"),
            new PromptEntry(ButtonGlyph.A, "Place"),
            new PromptEntry(ButtonGlyph.Y, "Rotate"),
            new PromptEntry(ButtonGlyph.B, "Cancel"),
            new PromptEntry(ButtonGlyph.LStick, "Pan"),
        };

        public override void TickView(GameObject? focus, InventoryVerbRouter router)
        {
            _nav.Tick(router);
            // Suspend catalog focus while placing so the native EventSystem Submit
            // can't re-trigger BeginPlacing when A confirms placement.
            GridFocusController.Instance?.SetExternalFocusSuspended(_nav.IsPlacing);
        }

        public override bool TryA(GameObject? focus, InventoryVerbRouter router)
        {
            // Placing: confirm handled in Tick (fresh press-edge avoids same-tap begin+confirm race).
            // Browse: native Submit begins placing. Consume so Transfer never runs.
            return true;
        }

        public override bool TryB(GameObject? focus, InventoryVerbRouter router)
        {
            if (_nav.IsPlacing) { _nav.CancelPlacing(); return true; }
            return false; // Browse: fall through to the default B-exit.
        }

        public override bool TryX(GameObject? focus, InventoryVerbRouter router)
        {
            if (!_nav.IsPlacing) _nav.RecycleHovered();
            return true; // handled (no-op while placing)
        }

        public override bool TryY(GameObject? focus, InventoryVerbRouter router)
        {
            if (_nav.IsPlacing) { _nav.Rotate(); return true; }
            return false;
        }

        // Consume LT/RT so the default pane-jump never fires (zoom is in TickView).
        public override bool TryLT(GameObject? focus, InventoryVerbRouter router) => true;
        public override bool TryRT(GameObject? focus, InventoryVerbRouter router) => true;

        public override IReadOnlyList<PromptEntry> PromptsFor(GameObject? focus, InventoryVerbRouter router)
            => _nav.IsPlacing ? _placingPrompts : _browsePrompts;

        public override bool HorizontalPrompts => true;
    }
}
