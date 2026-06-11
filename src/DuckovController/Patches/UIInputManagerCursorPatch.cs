using DuckovController.Bindings;
using HarmonyLib;
using UnityEngine;

namespace DuckovController.Patches
{
    // VirtualCursor polls UIInputManager.MouseDelta/WasClickedThisFrame — separate from SetButton/SetInputAxis.
    // PlayerInput stays KeyAndMouse on the Deck so gamepad bindings don't resolve. While a console is live,
    // override both getters with MiniGameInputGate values (right stick → delta, X → click).
    // Outside a console (CursorActive == false) originals run untouched.
    [HarmonyPatch(typeof(UIInputManager), "MouseDelta", MethodType.Getter)]
    internal static class UIInputManager_MouseDelta_Patch
    {
        private static bool Prefix(ref Vector2 __result)
        {
            if (!MiniGameInputGate.CursorActive) return true; // run original getter
            __result = MiniGameInputGate.CursorDelta;
            return false;                                     // skip original
        }
    }

    [HarmonyPatch(typeof(UIInputManager), "WasClickedThisFrame", MethodType.Getter)]
    internal static class UIInputManager_WasClickedThisFrame_Patch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!MiniGameInputGate.CursorActive) return true;
            // Consume the one-shot latch so each X press fires exactly one click,
            // regardless of how many times the getter is polled this frame.
            __result = MiniGameInputGate.ClickLatch;
            MiniGameInputGate.ClickLatch = false;
            return false;
        }
    }
}
