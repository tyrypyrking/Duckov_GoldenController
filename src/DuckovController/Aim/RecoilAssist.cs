using DuckovController.Config;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuckovController.Aim
{
    // Mod-owned recoil sim. The game applies recoil as a screen-space cursor offset that our
    // absolute-radial cursor overwrites every frame; so we own a persistent offset here,
    // scaled per tier, and add it onto the cursor AFTER the radial/lock base is written.
    // Right-stick motion toward the recover direction bleeds it down asymmetrically.
    internal static class RecoilAssist
    {
        internal static bool Enabled;          // mirrors cfg.Recoil.Enabled
        private static RecoilConfig? _cfg;
        private static Vector2 _offset;        // accumulated recoil offset (screen px)
        private static float _lastShotTime = -999f;

        internal static Vector2 CurrentOffset => _offset;
        // True when a live recoil offset is being applied to the cursor this frame. Lets the
        // driver gate its zero-delta path on actual recoil, not just the Enabled flag.
        internal static bool HasOffset => _offset.sqrMagnitude >= 0.01f;

        internal static void Configure(RecoilConfig cfg)
        {
            _cfg = cfg;
            Enabled = cfg != null && cfg.Enabled;
        }

        internal static void Reset()
        {
            _offset = Vector2.zero;
            _lastShotTime = -999f;
        }

        // Per-shot impulse. cursorPx & playerScreenPx define the recoil basis (outward = away
        // from the player, side = perpendicular), mirroring the game's ProcessMousePosViaRecoil.
        internal static void OnShot(float recoilV, float recoilH, Vector2 cursorPx, Vector2 playerScreenPx)
        {
            if (_cfg == null || !Enabled) return;
            Vector2 outward = cursorPx - playerScreenPx;
            outward = outward.sqrMagnitude < 1e-4f ? Vector2.up : outward.normalized;
            Vector2 side = -Vector2.Perpendicular(outward);
            float scale = _cfg.KickScale * (Screen.height / 1440f);
            _offset += (outward * recoilV + side * recoilH) * scale;
            ClampOffset();
            _lastShotTime = Time.unscaledTime;
        }

        // Pure integrate step: decay after the hold + asymmetric counter-steer. Side-effect
        // free (used by the self-check and by Apply).
        internal static Vector2 Integrate(
            Vector2 offset, Vector2 intendedMotionPx, float dt, float timeSinceShot, RecoilConfig cfg)
        {
            if (cfg == null) return offset;
            if (timeSinceShot >= cfg.RecoverHoldSec && offset.sqrMagnitude > 0f)
                offset = Vector2.MoveTowards(offset, Vector2.zero, cfg.RecoverRate * dt);

            if (offset.sqrMagnitude > 1e-4f && intendedMotionPx.sqrMagnitude > 1e-6f)
            {
                Vector2 recoverDir = -offset.normalized;
                float c = Vector2.Dot(recoverDir, intendedMotionPx);
                float bleed = c > 0f ? c * cfg.CounterGainToward : (-c) * cfg.CounterGainAway;
                if (bleed > 0f) offset = Vector2.MoveTowards(offset, Vector2.zero, bleed);
            }
            return offset;
        }

        // Per-frame, from AimDriverPatch AFTER the radial/lock base is written and BEFORE the
        // final SetAimInputUsingMouse. Adds the offset onto the live cursor.
        internal static void Apply(InputManager im, Vector2 intendedMotionPx)
        {
            if (_cfg == null || !Enabled || im == null) return;
            _offset = Integrate(_offset, intendedMotionPx, Time.unscaledDeltaTime,
                                Time.unscaledTime - _lastShotTime, _cfg);
            if (_offset.sqrMagnitude < 0.01f) { _offset = Vector2.zero; return; }
            if (!RadialCursor.TryReadAim(im, out var basePos)) return;
            Vector2 final = basePos + _offset;
            RadialCursor.WriteAbsoluteRaw(im, final);
            if (Mouse.current != null) Mouse.current.WarpCursorPosition(final);
        }

        private static void ClampOffset()
        {
            float max = _cfg!.MaxOffsetPx;
            if (max > 0f && _offset.sqrMagnitude > max * max)
                _offset = _offset.normalized * max;
        }
    }
}
