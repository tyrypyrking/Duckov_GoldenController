using System.Collections.Generic;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using Duckov.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DuckovController.UI.Inventory.VerbMaps
{
    // ItemDecomposeView (dismantle bench): A selects the focused item (populates name/slider/result),
    // X decomposes the selected item (decomposeButton.onClick — focus-independent; the X glyph REPLACES
    // the native "F" prompt on the button). LB/RB toggle focus between the LEFT grids and the RIGHT count
    // slider (RB → slider, LB → grid restoring the last item). LT/RT adjust the count by ∓1/±1 from
    // anywhere (TickView; hold-repeat). D-pad navigates the grids (incl. backpack↔storage), or — when the
    // slider is focused — adjusts the count (trapped). B exits (View.exitButton). Y inert.
    internal sealed class ItemDecomposeViewVerbMap : DefaultViewVerbMap
    {
        public override string ViewTypeName => "ItemDecomposeView";

        // Last grid item focused before entering the slider, so LB restores it.
        private GameObject? _lastGridFocus;

        // LT/RT hold-repeat state (adjust count from anywhere).
        private float _trigHoldStarted;
        private float _trigLastRepeat = -10f;
        private int _lastTrigDir; // -1 LT, +1 RT, 0 none — re-arm edge on direction change

        // Grid focus: full prompt set (panel switch via RB, count via LT/RT).
        private static readonly PromptEntry[] _gridPrompts = new[]
        {
            new PromptEntry(ButtonGlyph.DPad, "Move"),
            new PromptEntry(ButtonGlyph.A, "Select"),
            new PromptEntry(ButtonGlyph.X, "Decompose"),
            new PromptEntry(ButtonGlyph.RB, "Amount"),
            new PromptEntry(ButtonGlyph.LT, ButtonGlyph.RT, "Amount"),
        };

        // Count-slider focus: D-pad changes the count, LB returns to the grid, X still commits.
        private static readonly PromptEntry[] _sliderPrompts = new[]
        {
            new PromptEntry(ButtonGlyph.DPad, "Amount"),
            new PromptEntry(ButtonGlyph.LT, ButtonGlyph.RT, "Amount"),
            new PromptEntry(ButtonGlyph.LB, "Back"),
            new PromptEntry(ButtonGlyph.X, "Decompose"),
        };

        public override bool TryA(GameObject? focus, InventoryVerbRouter router)
        {
            if (focus == null || router == null) return false;
            // Select only — never commit on A. Grids are read-only (Movable=False); Select raises
            // OnSelectionChanged → Setup populates name/slider/result and enables decomposeButton.
            var display = focus.GetComponentInChildren<ItemDisplay>(false);
            if (display != null) ItemUIUtilities.Select(display);
            return true;
        }

        public override bool TryX(GameObject? focus, InventoryVerbRouter router)
        {
            if (router == null) return true;
            var view = View.ActiveView;
            if (view == null) return true;

            // Focus-independent commit: X decomposes the selected item at the slider's count whether the
            // grid or the slider is focused. The button is active only when the formula is valid, so X is
            // a safe no-op on a non-decomposable selection.
            var btn = ResolveButton(view, "decomposeButton");
            if (btn != null && btn.interactable && btn.gameObject.activeInHierarchy)
                btn.onClick.Invoke();
            return true; // consume X regardless — no other X action on this screen
        }

        // No operation menu on the dismantle bench.
        public override bool TryY(GameObject? focus, InventoryVerbRouter router) => true;

        // LT/RT adjust the count (handled in TickView). Consume so the default pane-jump never fires.
        public override bool TryLT(GameObject? focus, InventoryVerbRouter router) => true;
        public override bool TryRT(GameObject? focus, InventoryVerbRouter router) => true;

        // RB: grid → slider. LB: slider → grid (restore the pre-entry item). Consume so they don't page.
        public override bool TryRB(GameObject? focus, InventoryVerbRouter router)
        {
            var gfc = GridFocusController.Instance;
            if (gfc == null) return true;
            var slider = ResolveSliderGo();
            if (slider == null) return true;
            // Already on the slider → no-op (RB only moves grid → slider).
            if (gfc.CurrentFocus != null && gfc.CurrentFocus.GetComponent<Slider>() != null) return true;
            _lastGridFocus = gfc.CurrentFocus; // remember so LB can come back
            gfc.SetFocusExternal(slider);
            return true;
        }

        public override bool TryLB(GameObject? focus, InventoryVerbRouter router)
        {
            var gfc = GridFocusController.Instance;
            if (gfc == null) return true;
            // Only acts from the slider — restore the remembered grid item.
            if (gfc.CurrentFocus == null || gfc.CurrentFocus.GetComponent<Slider>() == null) return true;
            var target = (_lastGridFocus != null && _lastGridFocus.activeInHierarchy)
                ? _lastGridFocus
                : null;
            if (target != null) gfc.SetFocusExternal(target);
            return true;
        }

        // Track the last grid (InventoryEntry) focus so LB can restore it after a slider visit.
        public override void OnFocusChanged(GameObject? focus, InventoryVerbRouter router)
        {
            if (focus == null) return;
            if (focus.GetComponent<Slider>() != null) return; // slider, not a grid item
            if (focus.GetComponent<InventoryEntry>() != null) _lastGridFocus = focus;
        }

        // LT/RT adjust the dismantle count by ∓1/±1 from anywhere (grid or slider), with hold-repeat at
        // the standard cadence. TryLT/RT are one-shot edges, so the live analog poll lives here.
        public override void TickView(GameObject? focus, InventoryVerbRouter router)
        {
            var pad = Gamepad.current;
            if (pad == null || router?.Cfg == null) return;
            var slider = ResolveSliderGo()?.GetComponent<Slider>();
            if (slider == null) return;

            const float pressed = 0.5f; // analog trigger threshold
            bool lt = pad.leftTrigger.ReadValue() > pressed;
            bool rt = pad.rightTrigger.ReadValue() > pressed;
            int dir = rt ? +1 : lt ? -1 : 0; // RT wins if both held
            if (dir == 0) { _lastTrigDir = 0; return; }

            float now = Time.unscaledTime;
            if (dir != _lastTrigDir)
            {
                // New press (edge) — step once, start the hold timer.
                _lastTrigDir = dir;
                _trigHoldStarted = now;
                _trigLastRepeat = now;
                AdjustCount(slider, dir);
                return;
            }
            // Held — repeat after the initial delay, then at the repeat rate.
            if (now - _trigHoldStarted >= router.Cfg.NavRepeatDelaySec
                && now - _trigLastRepeat >= router.Cfg.NavRepeatRateSec)
            {
                AdjustCount(slider, dir);
                _trigLastRepeat = now;
            }
        }

        // Integer round-and-clamp (the DecomposeSlider's underlying Slider isn't wholeNumbers); writing
        // slider.value fires onValueChanged → updates the count label + the live result preview.
        private static void AdjustCount(Slider slider, int delta)
        {
            float before = slider.value;
            float next = Mathf.Clamp(Mathf.Round(before) + delta, slider.minValue, slider.maxValue);
            if (!Mathf.Approximately(before, next)) slider.value = next;
        }

        public override IReadOnlyList<PromptEntry> PromptsFor(GameObject? focus, InventoryVerbRouter router)
        {
            // On the count slider GO → slider prompts; otherwise the grid prompts.
            if (focus != null && focus.GetComponent<Slider>() != null) return _sliderPrompts;
            return _gridPrompts;
        }

        private static readonly (string, ButtonGlyph)[] _hints =
        {
            ("decomposeButton", ButtonGlyph.X),
        };
        public override IReadOnlyList<(string FieldName, ButtonGlyph Glyph)> ButtonGlyphHints() => _hints;

        // The active …/CountSlider/Slider GO (the one with the UnityEngine.UI.Slider). Resolved from the
        // view's countSlider (DecomposeSlider) field, falling back to a descendant search.
        private static GameObject? ResolveSliderGo()
        {
            var view = View.ActiveView;
            if (view == null) return null;
            if (ResolveField(view, "countSlider") is Component cs)
            {
                var sl = cs.GetComponentInChildren<Slider>(false);
                if (sl != null) return sl.gameObject;
            }
            return null;
        }

        // reflection helpers (mirror RepairViewVerbMap)
        private static object? ResolveField(object target, string fieldName)
            => target == null ? null : ReflectionUtil.WalkField(target.GetType(), fieldName)?.GetValue(target);

        private static Button? ResolveButton(object target, string fieldName)
            => ResolveField(target, fieldName) as Button;
    }
}
