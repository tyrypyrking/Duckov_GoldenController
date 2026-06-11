using System.Collections.Generic;

namespace DuckovController.Aim
{
    // AIM-6: suppress auto-aim on render-cloaked enemies (the J-Lab "Test Object",
    // Cname_LabTestObjective) until the player's thermal/night-vision reveals them.
    //
    // The game has NO per-enemy "cloaked" flag: cloaked characters are drawn only by the
    // "ThermalCharacter" URP render feature, which NightVisionVisual.Refresh() turns on iff the
    // active vision type has thermalOn — the same condition as CharacterMainControl.ThermalOn.
    // Reveal is therefore global, player-side, and instant. So an enemy is hidden-from-player iff
    // (its characterPreset.nameKey is a known cloak) AND (the player's thermal is off).
    internal static class CloakGate
    {
        // Diagnostic kill-switch. Mirrored from AutoAimConfig.RespectCloak in AutoAimTiers.Apply
        // (runs on config load + hot-reload). Default on.
        internal static bool Enabled = true;

        // Known render-cloaked character name keys. Currently only the J-Lab "Test Object".
        // Extend here if more cloaked enemies are found (the tracer below makes a miss obvious).
        private static readonly HashSet<string> CloakedNameKeys =
            new HashSet<string> { "Cname_LabTestObjective" };

        // Per-enemy last-logged suppression state → change-deduped diagnostics (never per-frame spam).
        // Only ever holds entries for cloaked-identity enemies (tiny).
        private static readonly Dictionary<int, bool> _lastLogged = new Dictionary<int, bool>();

        // True = mc is a known cloaked enemy the player currently cannot see → auto-aim must skip it.
        internal static bool IsCloakedFromPlayer(CharacterMainControl? mc)
        {
            if (!Enabled || mc == null) return false;

            var preset = mc.characterPreset;
            if (preset == null || string.IsNullOrEmpty(preset.nameKey)) return false; // normal enemy
            if (!CloakedNameKeys.Contains(preset.nameKey)) return false;               // normal enemy

            // Known cloaked enemy: revealed iff the player's thermal is active. If we can't read the
            // player (death / scene transition), fail safe = suppress (never lock an unprovable cloak).
            var player = CharacterMainControl.Main;
            bool suppressed = player == null || !player.ThermalOn;

            int id = mc.GetInstanceID();
            if (!_lastLogged.TryGetValue(id, out var prev) || prev != suppressed)
            {
                _lastLogged[id] = suppressed;
                Log.Debug_($"[cloak] {(suppressed ? "suppress" : "release")} {preset.nameKey} "
                           + $"(thermal{(suppressed ? "Off" : "On")})");
            }
            return suppressed;
        }
    }
}
