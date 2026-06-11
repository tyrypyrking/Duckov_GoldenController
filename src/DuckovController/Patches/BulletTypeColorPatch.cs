using System.Reflection;
using DuckovController.UI.Common;
using HarmonyLib;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.UI;

namespace DuckovController.Patches
{
    // Colours the ammo-type HUD background by rarity so the tier is readable
    // at a glance. Text stays black (the game's default) so white-tier ammo
    // doesn't produce invisible white-on-white text. We skip the colour write
    // when unchanged to avoid redundant mesh dirtying.
    [HarmonyPatch(typeof(BulletTypeHUD), "Update")]
    internal static class BulletTypeColorPatch
    {
        private static FieldInfo? _bgField;
        private static FieldInfo? _idField;
        private static bool _resolved;

        [HarmonyPostfix]
        private static void Postfix(BulletTypeHUD __instance)
        {
            try
            {
                if (!DuckovController.Diagnostics.PerfFlags.BulletColor) return;
                if (!_resolved)
                {
                    _resolved = true;
                    const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic;
                    _bgField = typeof(BulletTypeHUD).GetField("background", f);
                    _idField = typeof(BulletTypeHUD).GetField("bulletTpyeID", f); // sic: game's spelling
                }
                if (_bgField == null || _idField == null) return;
                var bg = _bgField.GetValue(__instance) as Graphic;
                if (bg == null) return;
                int id = (int)(_idField.GetValue(__instance) ?? -1);
                if (id < 0) return; // not assigned — leave the game's default

                var meta = ItemAssetsCollection.GetMetaData(id);
                int level = AmmoRarity.Level((int)meta.displayQuality, meta.quality, meta.priceEach);
                var c = AmmoRarity.ColorForLevel(level);
                if (bg.color != c) bg.color = c;
            }
            catch { /* cosmetic; never disrupt the HUD */ }
        }
    }
}
