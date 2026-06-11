using DuckovController.Config;
using UnityEngine;

namespace DuckovController.Aim
{
    // AIM-1 v2 soft bias-ring lock. While a target is locked, the crosshair roams WITHIN a
    // per-tier ring around the lead-adjusted ideal point (stick = roam, eased toward ideal,
    // never a snap). Escape keys off the RAW stick (the player's clean intent, never touched
    // by recoil): a sustained push into the outer band breaks the lock — except toward the
    // counter-recoil side, which is suppressed so fighting the kick can't fling you off.
    internal static class BiasRing
    {
        internal static bool Enabled;
        private static BiasRingConfig? _cfg;

        private static float _dwell;          // seconds the stick has dwelt in the (non-suppressed) escape band
        private static float _reacquireTimer; // seconds left in the post-escape re-acquire suppression
        private static float _lastDiagLog;    // throttle for the geometry snapshot (DebugLog only)

        // True only when the tier supplies a ring; callers branch ring-vs-CursorBlend on this.
        internal static bool TierActive => Enabled && _cfg != null && _cfg.RingRadiusPx > 0f;

        // True while the post-escape window is open; AutoAim/AdsLock skip picking while true.
        internal static bool SuppressingReacquire => _reacquireTimer > 0f;

        internal static void Configure(BiasRingConfig cfg)
        {
            _cfg = cfg;
            Enabled = cfg != null && cfg.Enabled;
        }

        // Decay the re-acquire window. Called once per frame from AimDriverPatch (runs even with no lock).
        internal static void Tick(float dt)
        {
            if (_reacquireTimer > 0f)
            {
                _reacquireTimer -= dt;
                if (_reacquireTimer < 0f) _reacquireTimer = 0f;
            }
        }

        // Soft reset on a lock-drop / escape edge: clears the dwell but KEEPS the re-acquire window
        // (an escape drops the lock, and that window must survive the very drop it causes).
        internal static void Reset()
        {
            _dwell = 0f;
        }

        // Full reset, incl. the re-acquire window: view-open / scope-entry / weapon-switch / scene change.
        internal static void ResetHard()
        {
            _dwell = 0f;
            _reacquireTimer = 0f;
        }

        // Per-frame lock step. ideal = lead-adjusted target screen point; stick = post-deadzone
        // right stick (screen basis, mag 0..1); seedCursor = live cursor (SmoothDamp seed on the
        // acquire frame); playerScreen = the shooter's screen position. Returns false when the lock
        // just escaped (caller drops it); out cursor = the eased roam cursor otherwise.
        internal static bool Step(Vector2 ideal, Vector2 stick, Vector2 seedCursor,
                                  Vector2 playerScreen, bool allowEscape, AutoAimConfig blendCfg, out Vector2 cursor)
        {
            if (_cfg == null)
            {
                cursor = CursorBlend.Step(seedCursor, ideal, blendCfg);
                return true;
            }

            float R = Mathf.Min(_cfg.RingRadiusPx, _cfg.MaxRingRadiusPx) * (Screen.height / 1440f);
            Vector2 desired = RoamDesired(ideal, stick, R, _cfg.BiasStrength);
            cursor = CursorBlend.Step(seedCursor, desired, blendCfg);

            // Hip-fire: NO safety guard. The roam + cone-pin keep recoil-control on target, but leaving
            // is immediate via the caller's TargetSelector stick-escape — no dwell/suppression here.
            // The dwell-in-band + counter-recoil suppression below is the ADS-only safety guard.
            if (!allowEscape) { _dwell = 0f; return true; }

            // Geometric recoil axis: recoil always shoves the crosshair OUTWARD (away from the player,
            // along the aim), so "pulling back toward yourself" is always the recoil-control direction.
            // This is known every frame on every tier — unlike RecoilAssist.CurrentOffset, which the
            // counter-bleed cancels to ~0 within a frame (so the old offset/latched suppression went
            // dead exactly when the player was controlling recoil, and the push escaped). Independent
            // of whether any recoil is currently applied (Cheat too).
            Vector2 outward = ideal - playerScreen;   // = recoil push direction
            Vector2 kickDir = outward.sqrMagnitude > 1e-4f ? outward.normalized : Vector2.zero;

            // Escape: dwell in the outer band of the stick, suppressed toward the counter-recoil side.
            float mag = stick.magnitude;
            bool inBand = InEscapeBand(mag, _cfg.EscapeBandFrac);
            bool suppressed = inBand && IsCounterSuppressed(stick, kickDir, _cfg.CounterSuppressDot);

            // Geometry snapshot (DebugLog only, throttled): recoil-offset vs ring/roam scale, and the
            // stick-vs-counter dot so we can see exactly why a push does/doesn't get suppressed.
            if (Log.Verbose)
            {
                float recoilMag = RecoilAssist.CurrentOffset.magnitude;
                float roamMag = stick.magnitude * R * (1f - Mathf.Clamp01(_cfg.BiasStrength));
                float dotDbg = (mag > 1e-3f && kickDir != Vector2.zero)
                    ? Vector2.Dot(stick / mag, -kickDir) : 9f;
                float now = Time.unscaledTime;
                if ((recoilMag > 3f || mag > 0.30f) && now - _lastDiagLog > 0.50f)
                {
                    _lastDiagLog = now;
                    Log.Debug_($"[biasring] R={R:0.0} roam={roamMag:0.0} recoil={recoilMag:0.0} "
                        + $"stickMag={mag:0.00} dot={dotDbg:0.00} strength={_cfg.BiasStrength:0.00} "
                        + $"dwell={_dwell:0.00} inBand={inBand} supp={suppressed}");
                }
            }

            if (inBand && !suppressed) _dwell += Time.unscaledDeltaTime;
            else _dwell = 0f;

            if (_dwell >= _cfg.EscapeDwellSec)
            {
                if (Log.Verbose)
                {
                    float dot = (mag > 1e-3f && kickDir != Vector2.zero)
                        ? Vector2.Dot(stick / mag, -kickDir) : 9f;
                    Log.Debug_($"[biasring] ESCAPE stickMag={mag:0.00} stick=({stick.x:0.00},{stick.y:0.00}) "
                        + $"kickDir=({kickDir.x:0.00},{kickDir.y:0.00}) dot={dot:0.00} counterDot={_cfg.CounterSuppressDot:0.00} "
                        + $"recoil={RecoilAssist.CurrentOffset.magnitude:0.0} R={R:0.0} dwellNeeded={_cfg.EscapeDwellSec:0.00}");
                }
                _reacquireTimer = _cfg.ReacquireSuppressSec;
                _dwell = 0f;
                return false;
            }
            return true;
        }

        // --- Pure helpers (also exercised by the boot self-check) ---

        // Crosshair goal: ideal + attenuated stick roam. strength 1 => ideal (glued); 0 => ideal + stick*R.
        internal static Vector2 RoamDesired(Vector2 ideal, Vector2 stickScreen, float R, float strength)
            => ideal + stickScreen * (R * (1f - Mathf.Clamp01(strength)));

        // Stick deflection is in the escape band (outer EscapeBandFrac of full deflection).
        internal static bool InEscapeBand(float stickMag, float bandFrac)
            => stickMag >= 1f - Mathf.Clamp01(bandFrac);

        // True when the push points toward the counter-recoil side (opposite the kick) within the dot gate.
        internal static bool IsCounterSuppressed(Vector2 stick, Vector2 kick, float counterDot)
        {
            if (stick.sqrMagnitude < 1e-6f || kick.sqrMagnitude < 1e-4f) return false;
            return Vector2.Dot(stick.normalized, (-kick).normalized) > counterDot;
        }
    }
}
