using System.Collections.Generic;
using DuckovController.UI.Common;
using DuckovController.UI.MiniMap;
using DuckovController.UI.Prompts;
using UnityEngine;

namespace DuckovController.UI.Inventory.VerbMaps
{
    // MiniMapView (in-game full map). Focus drives the marker toolbox palette
    // (D-pad + A). Left stick pans, right stick moves the cursor, LT/RT zoom,
    // X places/removes a marker, B exits, LB/RB keep the global tab cycle.
    // Analog work lives in MiniMapNavigator via TickView.
    internal sealed class MiniMapViewVerbMap : DefaultViewVerbMap
    {
        public override string ViewTypeName => "MiniMapView";

        private readonly MiniMapNavigator _nav = new MiniMapNavigator();

        // Self-register for static teardown access. Only one instance is ever
        // created (held in ViewVerbMapRegistry for the process lifetime).
        private static MiniMapViewVerbMap? _instance;
        public MiniMapViewVerbMap() { _instance = this; }

        private static readonly PromptEntry[] _prompts = new[]
        {
            new PromptEntry(ButtonGlyph.LStick, "Pan"),
            new PromptEntry(ButtonGlyph.RStick, "Cursor"),
            new PromptEntry(ButtonGlyph.LT, "Zoom -"),
            new PromptEntry(ButtonGlyph.RT, "Zoom +"),
            new PromptEntry(ButtonGlyph.A, "Select"),
            new PromptEntry(ButtonGlyph.X, "Mark toggle"),
        };

        public override void TickView(GameObject? focus, InventoryVerbRouter router)
            => _nav.Tick(router);

        // Consume A without Transfer: EventSystem Submit activates the focused button, triggering
        // MapMarkerSettingsPanel.Setup() which reshuffles the pooled palette — by A-release the GO is a mirrored slot.
        public override bool TryA(GameObject? focus, InventoryVerbRouter router) => true;

        // Consume LT/RT so the default pane-jump never fires (zoom is in TickView).
        public override bool TryLT(GameObject? focus, InventoryVerbRouter router) => true;
        public override bool TryRT(GameObject? focus, InventoryVerbRouter router) => true;

        // X: place a marker at the cursor's world position, or remove the nearest
        // marker if the cursor is within MarkerHitRadius of one.
        public override bool TryX(GameObject? focus, InventoryVerbRouter router)
        {
            _nav.PlaceOrRemoveAtCursor();
            return true;
        }

        // Destroy cursor GO (parented on game-side UI; outlives mod's GameObject). Static teardown via registry lifetime.
        internal static void OnModDeactivate()
        {
            _instance?._nav.DestroyOverlay();
        }

        public override IReadOnlyList<PromptEntry> PromptsFor(GameObject? focus, InventoryVerbRouter router)
            => _prompts;

        public override bool HorizontalPrompts => true;
    }
}
