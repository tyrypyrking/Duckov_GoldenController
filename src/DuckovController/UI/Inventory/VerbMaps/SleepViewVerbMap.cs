using System.Collections.Generic;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using Duckov.UI;
using UnityEngine;

namespace DuckovController.UI.Inventory.VerbMaps
{
    // SleepView — a fixed-control action screen (time slider + Sleep/Exit), not
    // an inventory grid. Control model (confirmed with the user):
    //   dpad  → adjust the time slider (handled by the focused-Slider branch in
    //           GridFocusController.Step; focus is pinned to the slider)
    //   A     → Sleep (clicks confirmButton, regardless of focus)
    //   B     → Exit (router-global View.Close fallback)
    //   X / Y → inert (no Smart-Take / operation menu on this screen)
    internal sealed class SleepViewVerbMap : DefaultViewVerbMap
    {
        public override string ViewTypeName => "SleepView";

        private static readonly PromptEntry[] _prompts = new[]
        {
            new PromptEntry(ButtonGlyph.DPad, "Time"),
            new PromptEntry(ButtonGlyph.A, "Sleep"),
        };

        // A always confirms regardless of focus (slider is focused for dpad) — click confirmButton directly.
        public override bool TryA(GameObject? focus, InventoryVerbRouter router)
        {
            if (router == null) return false;
            var view = View.ActiveView;
            if (view == null) return false;
            return router.TryClickViewField(view, "confirmButton");
        }

        // Inert on this screen — consume so the router doesn't run Smart-Take /
        // open an operation menu on the slider.
        public override bool TryX(GameObject? focus, InventoryVerbRouter router) => true;

        public override IReadOnlyList<PromptEntry> PromptsFor(GameObject? focus, InventoryVerbRouter router) => _prompts;
    }
}
