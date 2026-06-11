using DuckovController.Config;
using UnityEngine;

namespace DuckovController.Aim
{
    // Two-cursor smoothing: rest cursor read fresh from AimMousePosition each frame (post-stick/recoil).
    internal static class CursorBlend
    {
        private static Vector2 _aimPos;
        private static Vector2 _velocity;
        private static bool _wasLocked;
        private static bool _initialized;

        internal static Vector2 AimPos => _aimPos;

        // Seed _aimPos so we don't snap from origin on first frame.
        internal static void SeedIfNeeded(Vector2 restPos)
        {
            if (!_initialized)
            {
                _aimPos = restPos;
                _velocity = Vector2.zero;
                _initialized = true;
            }
        }

        // Called on scene change / activation transition.
        internal static void Reset()
        {
            _initialized = false;
            _wasLocked = false;
            _velocity = Vector2.zero;
        }

        internal static Vector2 Step(Vector2 restPos, Vector2? targetScreen, AutoAimConfig cfg)
        {
            SeedIfNeeded(restPos);

            bool nowLocked = targetScreen.HasValue;
            if (_wasLocked != nowLocked)
            {
                _velocity = Vector2.zero; // zero on state change to avoid overshoot
                _wasLocked = nowLocked;
            }

            Vector2 goal = targetScreen ?? restPos;

            float snapTau = Mathf.Lerp(0.18f, 0.005f, Mathf.Clamp01(cfg.SnapStrength));
            float returnTau = Mathf.Lerp(0.25f, 0.05f, Mathf.Clamp01(cfg.ReturnSpeed));
            float tau = nowLocked ? snapTau : returnTau;

            _aimPos = Vector2.SmoothDamp(_aimPos, goal, ref _velocity, tau);
            return _aimPos;
        }
    }
}
