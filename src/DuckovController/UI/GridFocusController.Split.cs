using System.Reflection;
using Duckov.UI;
using Duckov.UI.Animations;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DuckovController.UI
{
    // SplitDialogue is the stack-split overlay reached from the item operation menu
    // (ItemOperationMenu.Split → SplitDialogue.SetupAndShow → Close). It floats over the
    // inventory/loot View but is NOT a View itself, so it never trips the verb-map machinery.
    // While shown it owns input (Update hands off here and returns): count slider via dpad/stick
    // (L/R = ±1, U/D = ±10, snapped to int), A = confirm, B = cancel — mirroring the SleepView
    // fixed-control treatment, just on an overlay instead of a View.
    internal sealed partial class GridFocusController : MonoBehaviour
    {
        private static FieldInfo? _splitFadeField;
        private static FieldInfo? _splitSliderField;
        private static FieldInfo? _splitConfirmField;
        private static MethodInfo? _itemHoverHideMethod;
        private static bool _splitReflected;

        // The same A-press that picks "Split" from the op-menu opens the overlay AND would be seen by
        // HandleSplitDialog as a confirm on the same/next frame (the op-menu uses the game's Submit;
        // SplitDialogue.IsShown flips synchronously in Show()). Debounce: A must be released once while
        // the overlay is up before it can confirm. Reset whenever the overlay isn't shown.
        private bool _splitWasOpen;
        private bool _splitAArmed;
        private GameObject? _splitConfirmGlyph; // A glyph attached to the dialogue's confirm button

        // Helper-panel prompts shown while the overlay is up. A lives as a glyph on the confirm button.
        private static readonly PromptEntry[] _splitPrompts =
        {
            new PromptEntry(ButtonGlyph.DPad, "Amount"),
            new PromptEntry(ButtonGlyph.B, "Cancel"),
        };

        private static void ResolveSplitFields()
        {
            if (_splitReflected) return;
            _splitReflected = true;
            var t = typeof(SplitDialogue);
            _splitFadeField    = AccessTools.Field(t, "fadeGroup");
            _splitSliderField  = AccessTools.Field(t, "slider");
            _splitConfirmField = AccessTools.Field(t, "confirmButton");
            _itemHoverHideMethod = AccessTools.Method(typeof(ItemHoveringUI), "Hide");
        }

        // Hide the item-info hover panel regardless of what the grid focus currently resolves to.
        // While the overlay is up the focus may still sit on the item behind it (or its now-closed
        // op-menu button), and the panel would otherwise render over the Split dialogue.
        private static void HideItemHoverPanel()
        {
            if (!ItemHoveringUI.Shown) return;
            ResolveSplitFields();
            _itemHoverHideMethod?.Invoke(ItemHoveringUI.Instance, null);
        }

        // True while the split overlay is faded in. Cheap: Instance is a singleton field read,
        // gated upstream to frames where a supported View is active.
        internal bool IsSplitDialogOpen()
        {
            var inst = SplitDialogue.Instance;
            if (inst == null) return false;
            ResolveSplitFields();
            var fg = _splitFadeField?.GetValue(inst) as FadeGroup;
            return fg != null && fg.IsShown;
        }

        // Owns input while the overlay is shown. Caller returns immediately after, so grid nav,
        // exit-glyph upkeep, and the verb router are all suppressed for the duration.
        private void HandleSplitDialog()
        {
            var pad = Gamepad.current;
            if (pad == null || Cfg == null) return;
            var inst = SplitDialogue.Instance;
            if (inst == null) return;

            // Stick contributes to the slider regardless of the StickAsDpad preference — the overlay
            // is modal, the stick has no competing job here.
            _stick.Sample(pad.leftStick.ReadValue(), true);

            // Keep the cursor hidden and drop any EventSystem selection so a stray Submit can't fire
            // on the inventory slot sitting behind the overlay.
            Cursor.visible = false;
            var es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null) es.SetSelectedGameObject(null);

            if (!_splitWasOpen)
            {
                _splitWasOpen = true;
                _splitAArmed = !pad.buttonSouth.isPressed; // armed only if A isn't the press that opened us
                _outlineOverlay?.Hide();                   // keep the golden outline hidden under the overlay
                ViewHintPanel.Override = _splitPrompts;    // swap the helper panel to Split controls
                // A-glyph on the dialogue's confirm button (mirrors on-button hints elsewhere).
                ResolveSplitFields();
                if (_splitConfirmGlyph == null && _splitConfirmField?.GetValue(inst) is Button cb)
                {
                    var img = CreateButtonGlyph(cb, ButtonGlyph.A);
                    if (img != null) _splitConfirmGlyph = img.gameObject;
                }
                Log.Debug_($"Split: overlay shown (aHeldAtOpen={pad.buttonSouth.isPressed} focus={(_focused != null ? _focused.name : "null")})");
            }
            // Every frame: keep the item-info panel suppressed (focus may resolve to the item behind us).
            HideItemHoverPanel();
            if (!pad.buttonSouth.isPressed) _splitAArmed = true; // A released → confirm now armed

            // B = cancel, A = confirm. A is debounced so the press that opened the overlay can't also confirm.
            if (pad.buttonEast.wasPressedThisFrame) { Log.Debug_("Split: B → cancel"); inst.Cancel(); return; }
            if (_splitAArmed && pad.buttonSouth.wasPressedThisFrame)
            {
                ResolveSplitFields();
                var btn = _splitConfirmField?.GetValue(inst) as Button;
                var sld = _splitSliderField?.GetValue(inst) as Slider;
                Log.Debug_($"Split: A → confirm (count={(sld != null ? Mathf.RoundToInt(sld.value) : -1)})");
                btn?.onClick.Invoke();
                return;
            }

            ResolveSplitFields();
            var slider = _splitSliderField?.GetValue(inst) as Slider;
            if (slider == null) return;

            const float fine = 1f;     // L/R: one item
            const float coarse = 10f;  // U/D: ten items
            float Edge()
            {
                if (DirEdge(pad, NavDir.Right)) return +fine;
                if (DirEdge(pad, NavDir.Left))  return -fine;
                if (DirEdge(pad, NavDir.Up))    return +coarse;
                if (DirEdge(pad, NavDir.Down))  return -coarse;
                return 0f;
            }
            float Held()
            {
                if (DirHeld(pad, NavDir.Right)) return +fine;
                if (DirHeld(pad, NavDir.Left))  return -fine;
                if (DirHeld(pad, NavDir.Up))    return +coarse;
                if (DirHeld(pad, NavDir.Down))  return -coarse;
                return 0f;
            }

            float edge = Edge();
            if (edge != 0f)
            {
                _sliderHoldStarted = Time.unscaledTime;
                _sliderLastRepeat  = Time.unscaledTime;
                AdjustSplitSlider(slider, edge);
                return;
            }

            float held = Held();
            if (held != 0f
                && Time.unscaledTime - _sliderHoldStarted >= Cfg.Ui.NavRepeatDelaySec
                && Time.unscaledTime - _sliderLastRepeat  >= Cfg.Ui.NavRepeatRateSec)
            {
                AdjustSplitSlider(slider, held);
                _sliderLastRepeat = Time.unscaledTime;
            }
        }

        // The split slider is a count (1..StackCount) but isn't flagged wholeNumbers, and its initial
        // value is (StackCount-1)/2f. Snap to int around the current value so steps land cleanly and
        // the count text (ToString("0")) / final RoundToInt agree with what the player sees.
        private static void AdjustSplitSlider(Slider slider, float delta)
        {
            float before = slider.value;
            float next = Mathf.Clamp(Mathf.Round(before) + delta, slider.minValue, slider.maxValue);
            if (!Mathf.Approximately(before, next))
                slider.value = next; // fires onValueChanged → RefreshCountText, as a mouse drag would
        }
    }
}
