using DuckovController.UI.CostDisplay;
using DuckovController.UI.Settings;
using HarmonyLib;

namespace DuckovController.Patches
{
    // Detailed Cost List (global): CostDisplay.Setup is the single shared renderer for
    // every item-requirement list in the game. One postfix here covers craft, perk,
    // dismantle, black market, buildings, crops, map selection, death lottery, and the
    // interact bubble. Gated by config; any failure falls back to the vanilla layout.
    [HarmonyPatch(typeof(CostDisplay), "Setup")]
    internal static class CostDisplayDetailPatch
    {
        [HarmonyPostfix]
        private static void Postfix(CostDisplay __instance)
        {
            try
            {
                if (__instance == null) return;
                if (SettingsBridge.Cfg?.CostDisplay?.Enabled == true)
                {
                    // PT-6: the in-raid build/fix world billboard (CostTakerHUD_Entry, e.g. a
                    // ConstructionSite prompt) reuses CostDisplay. Keep our readable vertical
                    // layout there too, but WITHOUT the 1.75x enlargement — native is too small
                    // to read and 1.75x was comically big. Menu cost lists keep the enlargement.
                    float scale = DetailedCostListStyler.IsInRaidBuildFixHost(__instance)
                        ? DetailedCostListStyler.InRaidBuildFixScale
                        : DetailedCostListStyler.DefaultScale;
                    DetailedCostListStyler.Apply(__instance, scale);
                }
                else
                    DetailedCostListStyler.Restore(__instance);
            }
            // Gated, not silent: a swallowed NRE here hid a layout bug for an entire session.
            catch (System.Exception e) { Log.Debug_($"CostDisplay detail styler failed: {e.Message}"); }
        }
    }
}
