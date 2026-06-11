using UnityEngine;

namespace DuckovController.UI.Common
{
    // Ammo rarity tier on the 0-6 scale (White=0,Green=1,Blue=2,Purple=3,
    // Orange=4,LightRed=5,Red=6), ported from ItemLevelAndSearchSoundMod's
    // bullet branch. Works on raw (displayQuality, quality, value) so it serves
    // both Item instances (smart-loot) and ItemMetaData (HUD).
    internal static class AmmoRarity
    {
        // displayQuality: (int)Item.DisplayQuality  (None=0,White=1,...)
        // quality: Item.Quality   value: Item.Value (== ItemMetaData.priceEach)
        internal static int Level(int displayQuality, int quality, int value)
        {
            if (displayQuality != 0)
            {
                if (displayQuality == 5) return 5;        // LightRed (bullet special-case)
                return DisplayQualityToLevel(displayQuality, quality);
            }
            if (quality == 1) return 0;                    // White
            if (quality == 2) return 1;                    // Green
            int lvl = ValueToLevel((int)((value / 2f) * 30f));
            return lvl > 4 ? 4 : lvl;                      // value-derived ammo caps at Orange
        }

        private static int DisplayQualityToLevel(int dq, int quality) => dq switch
        {
            0 or 1 => 0, 2 => 1, 3 => 2, 4 => 3, 5 => 4,
            6 => quality == 6 ? 5 : 6,
            _ => 0,
        };

        private static int ValueToLevel(int v)
            => v >= 10000 ? 6 : v >= 5000 ? 5 : v >= 2500 ? 4
             : v >= 1200 ? 3 : v >= 600 ? 2 : v >= 200 ? 1 : 0;

        // Opaque rarity colours (RGB from ItemLevelAndSearchSoundMod; that mod
        // used low alpha for slot-highlight overlays, but HUD text must be solid).
        private static readonly Color[] _colors =
        {
            new Color32(0xFF, 0xFF, 0xFF, 0xFF), // 0 White
            new Color32(0x7C, 0xFF, 0x7C, 0xFF), // 1 Green
            new Color32(0x7C, 0xD5, 0xFF, 0xFF), // 2 Blue
            new Color32(0xD0, 0xAC, 0xFF, 0xFF), // 3 Purple
            new Color32(0xFF, 0xDC, 0x24, 0xFF), // 4 Orange
            new Color32(0xFF, 0x58, 0x58, 0xFF), // 5 LightRed
            new Color32(0xBB, 0x00, 0x00, 0xFF), // 6 Red
        };

        internal static Color ColorForLevel(int level)
            => _colors[Mathf.Clamp(level, 0, _colors.Length - 1)];
    }
}
