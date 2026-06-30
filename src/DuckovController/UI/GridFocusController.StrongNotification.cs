using Duckov;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace DuckovController.UI
{
    // StrongNotification is the game's full-panel "new craft / blueprint registered" popup
    // (Duckov.StrongNotification — a singleton overlay, NOT a View). It is shown by
    // FormulasRegisterView.OnSubmitButtonClicked → StrongNotification.Push and dismissed by the
    // native UIInputManager.OnConfirm/OnCancel events. The mod no longer injects the gamepad
    // UI_Confirm/UI_Cancel bindings by default (Perf.ApplyGamepadBindings off — InputSystem cost),
    // so the pad can't dismiss it. The whole panel is an IPointerClickHandler (click-to-advance),
    // so we fire a synthetic pointer-click on A (and B) while it is showing — exactly what a mouse
    // click does (sets confirmed = true). Scoped to "while Showing" so there is no per-frame cost.
    internal sealed partial class GridFocusController : MonoBehaviour
    {
        // The A/X press that registered the formula (and triggered Push) could roll into a dismiss
        // before the first content even fades in — but StrongNotification only checks `confirmed`
        // inside the per-content wait loop (reset to false right before it), so an early press is
        // harmless. We still require A to be released once after the popup appears, mirroring the
        // Split overlay's debounce, so a held button can't auto-dismiss every queued content.
        private bool _strongNotifWasOpen;
        private bool _strongNotifAArmed;

        // True while the "new craft" popup is faded in. Cheap static read; gated upstream to frames
        // where a supported View is active (the popup floats over FormulasRegisterView).
        private static bool IsStrongNotificationOpen() => StrongNotification.Showing;

        // Owns input while the popup is shown. Caller returns immediately after, so grid nav,
        // the exit glyph, and the verb router are all suppressed for the duration — the press
        // dismisses the popup instead of acting on the View behind it.
        private void HandleStrongNotification()
        {
            var pad = Gamepad.current;
            var inst = StrongNotification.Instance;
            if (pad == null || inst == null) return;

            if (!_strongNotifWasOpen)
            {
                _strongNotifWasOpen = true;
                _strongNotifAArmed = !pad.buttonSouth.isPressed; // armed only if A isn't the press that opened us
                Log.Debug_($"StrongNotif: shown (aHeldAtOpen={pad.buttonSouth.isPressed})");
            }
            if (!pad.buttonSouth.isPressed) _strongNotifAArmed = true; // A released → confirm armed

            // A (armed) or B advances/dismisses the current content, like a mouse click would.
            bool dismiss =
                (_strongNotifAArmed && pad.buttonSouth.wasPressedThisFrame)
                || pad.buttonEast.wasPressedThisFrame;
            if (dismiss)
            {
                Log.Debug_("StrongNotif: A/B → dismiss");
                var ped = new PointerEventData(EventSystem.current);
                ExecuteEvents.Execute<IPointerClickHandler>(inst.gameObject, ped, ExecuteEvents.pointerClickHandler);
            }
        }
    }
}
