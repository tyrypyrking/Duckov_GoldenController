using DuckovController.Bindings;
using Duckov.MiniMaps.UI;
using Duckov.UI;
using HarmonyLib;

namespace DuckovController.Patches
{
    // Suppress InventoryView/MiniMapView/PauseMenu Show() while in a console. InputAction disabling doesn't
    // stick (gamepad press auto-switches scheme and re-enables maps); blocking Show() is scheme-proof.
    // B/East exits via GamingConsole.OnUICancel → StopInteract, not these menus.

    [HarmonyPatch(typeof(InventoryView), "Show")]
    internal static class InventoryView_Show_Block
    {
        private static bool Prefix() => !MiniGameInputGate.InConsole;
    }

    [HarmonyPatch(typeof(MiniMapView), "Show")]
    internal static class MiniMapView_Show_Block
    {
        private static bool Prefix() => !MiniGameInputGate.InConsole;
    }

    [HarmonyPatch(typeof(PauseMenu), "Show")]
    internal static class PauseMenu_Show_Block
    {
        private static bool Prefix() => !MiniGameInputGate.InConsole;
    }
}
