using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuckovController.Patches
{
    // Controller input for Action_FishingV2 (CharacterAction, not a View). Owns the frame while fishing:
    //   A/X → TryCatchFishInput; Start → pause; anything else → cancel for immediate combat return.
    // Cancel: StopAction() is gated by private needStopAction (false mid-game); set it true via reflection
    // then call StopAction() — the action's own self-stop path, tears down cleanly from any state.
    internal static class FishingInputHandler
    {
        private const string FishingActionTypeName = "Action_FishingV2";
        private const float StickDeadzone = 0.18f;

        private static FieldInfo? _needStopActionField;
        private static bool _needStopActionResolved;

        // Tracks fishing-active across frames so we can refresh the native input
        // indicators on the enter/exit edge (see below).
        private static bool _wasFishing;

        // True when a fishing action is active and this handler consumed the
        // frame's input — the caller must then skip all other gameplay drives.
        // False when no fishing action is running.
        internal static bool TryHandle(Gamepad pad)
        {
            var main = CharacterMainControl.Main;
            var action = main?.CurrentAction;
            bool isFishing = action != null && action.Running
                && action.GetType().Name == FishingActionTypeName;

            // Nudge InputIndicators on state transition so the A-glyph swap in GameplayPromptGlyphInjector fires.
            // Action_FishingV2.OnStart refreshes before currentAction is set, so without this it shows the keyboard key.
            if (isFishing != _wasFishing)
            {
                _wasFishing = isFishing;
                try { InputIndicator.NotifyBindingChanged(); }
                catch (System.Exception e) { Log.Debug_($"FishingInputHandler.notify: {e.Message}"); }
            }

            if (!isFishing) return false;

            // Start stays as pause (non-destructive — pause freezes the ring).
            if (pad.startButton.wasPressedThisFrame)
            {
                PauseMenu.Toggle();
                return true;
            }

            // A or X → hook, via the vanilla TryCatchFishInput path.
            if (pad.buttonSouth.wasPressedThisFrame || pad.buttonWest.wasPressedThisFrame)
            {
                try { main!.TryCatchFishInput(); }
                catch (System.Exception e) { Log.Debug_($"FishingInputHandler.catch: {e.Message}"); }
                return true;
            }

            // Anything else → cancel immediately so combat is available.
            // action is non-null here (isFishing required it).
            if (AnyCancelInput(pad))
                Cancel(action!);

            // Fishing is active: consume the frame regardless so the normal
            // gameplay drives stay suppressed (no Dash/Reload/weapon-switch).
            return true;
        }

        private static bool AnyCancelInput(Gamepad pad)
        {
            if (pad.buttonEast.wasPressedThisFrame        // B
                || pad.buttonNorth.wasPressedThisFrame    // Y
                || pad.leftShoulder.wasPressedThisFrame
                || pad.rightShoulder.wasPressedThisFrame
                || pad.leftTrigger.wasPressedThisFrame
                || pad.rightTrigger.wasPressedThisFrame
                || pad.dpad.up.wasPressedThisFrame
                || pad.dpad.down.wasPressedThisFrame
                || pad.dpad.left.wasPressedThisFrame
                || pad.dpad.right.wasPressedThisFrame
                || pad.leftStickButton.wasPressedThisFrame
                || pad.rightStickButton.wasPressedThisFrame
                || pad.selectButton.wasPressedThisFrame)
                return true;
            // Stick past deadzone = cancel (stick is at rest during cast so no insta-cancel on entry).
            if (pad.leftStick.ReadValue().magnitude > StickDeadzone) return true;
            if (pad.rightStick.ReadValue().magnitude > StickDeadzone) return true;
            return false;
        }

        private static void Cancel(CharacterActionBase action)
        {
            try
            {
                if (!_needStopActionResolved)
                {
                    _needStopActionResolved = true;
                    _needStopActionField = action.GetType().GetField(
                        "needStopAction", BindingFlags.Instance | BindingFlags.NonPublic);
                }
                _needStopActionField?.SetValue(action, true);
                action.StopAction();
                Log.Debug_("FishingInputHandler: cancelled fishing (force-stop).");
            }
            catch (System.Exception e) { Log.Debug_($"FishingInputHandler.cancel: {e.Message}"); }
        }
    }
}
