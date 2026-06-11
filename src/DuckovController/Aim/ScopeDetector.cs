using DuckovController.Config;
using UnityEngine;

namespace DuckovController.Aim
{
    // AIM-4: classify a held gun as "scoped" (sniper/marksman/long-range) from its stats.
    // There is no weapon-type enum in the game; scopes are stat-modifying attachments, so we
    // threshold on ADSAimDistanceFactor (the scope-reach stat) OR BulletDistance (effective range).
    internal static class ScopeDetector
    {
        // Change-deduped calibration tracer state.
        private static int _lastLoggedGunId;
        private static bool _lastScoped;
        private static bool _hasLogged;

        internal static bool IsScoped(ItemAgent_Gun? gun, ScopeConfig cfg)
        {
            if (cfg == null || !cfg.Enabled || gun == null) return false;
            // Zoom is what actually defines a scope. On-device logs showed long-range ARs reach
            // bulletDist 28-30 m with NO zoom (adsFactor 1.00) — the old `|| bulletDist >= threshold`
            // branch wrongly routed them through ScopeAim, which suppresses recoil. The one real
            // scope held had adsFactor 2.71 (>= 1.5), so adsFactor alone separates them cleanly.
            // BulletDistanceThreshold is retained in config (used by the calibration log) but no
            // longer classifies on its own.
            return SafeAdsFactor(gun) >= cfg.AdsFactorThreshold;
        }

        // Call once per gameplay frame with the held gun. Logs only on gun/scoped-state change
        // (never per-frame spam), so the real ADSAimDistanceFactor/BulletDistance can be read off
        // the deck to calibrate the thresholds. Gated by DebugLog.
        internal static void LogIfChanged(ItemAgent_Gun? gun, ScopeConfig cfg, bool debugLog)
        {
            if (!debugLog || gun == null || cfg == null) return;
            int id = gun.GetInstanceID();
            bool scoped = IsScoped(gun, cfg);
            if (_hasLogged && id == _lastLoggedGunId && scoped == _lastScoped) return;
            _lastLoggedGunId = id;
            _lastScoped = scoped;
            _hasLogged = true;
            Log.Debug_($"[scope] gun adsFactor={SafeAdsFactor(gun):0.00} "
                       + $"bulletDist={SafeBulletDist(gun):0.0} scoped={scoped}");
        }

        private static float SafeAdsFactor(ItemAgent_Gun gun)
        {
            try { return gun.ADSAimDistanceFactor; } catch { return 0f; }
        }

        private static float SafeBulletDist(ItemAgent_Gun gun)
        {
            try { return gun.BulletDistance; } catch { return 0f; }
        }
    }
}
