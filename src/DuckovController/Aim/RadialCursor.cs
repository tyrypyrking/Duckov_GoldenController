using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuckovController.Aim
{
    // Absolute-radial cursor: stick dir/mag → crosshair on circle around player; no flight lag.
    // IMPORTANT: writes AimMousePosition absolutely, not as a delta. SetAimInputUsingMouse scales
    // deltas by MouseSensitivity/10 — feeding a position error as delta makes the cursor crawl and
    // never settle. Set absolute here, then caller passes Vector2.zero to SetAimInputUsingMouse.
    internal static class RadialCursor
    {
        private static FieldInfo? _mainCamField;
        private static PropertyInfo? _aimMousePosProp;
        private static FieldInfo? _aimMousePosField;
        private static bool _resolved;

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            var im = typeof(InputManager);
            _mainCamField = im.GetField("mainCam", BindingFlags.Instance | BindingFlags.NonPublic);
            _aimMousePosProp = im.GetProperty("AimMousePosition",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (_aimMousePosProp == null || !_aimMousePosProp.CanWrite)
                _aimMousePosField = im.GetField("_aimMousePosCache", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        // Writes AimMousePosition = playerScreen + screenStick*radius (radius = radiusFactor * Screen.height).
        // Returns false if player screen pos is invalid.
        internal static bool WriteAbsolute(
            InputManager im, CharacterMainControl ch, Vector2 screenStick, float radiusFactor)
        {
            try
            {
                if (im == null || ch == null) return false;
                Resolve();
                var cam = _mainCamField?.GetValue(im) as Camera;
                if (cam == null) return false;

                var pc = cam.WorldToScreenPoint(ch.transform.position);
                if (pc.z <= 0f || float.IsNaN(pc.x) || float.IsNaN(pc.y)) return false;

                float radius = Mathf.Max(1f, radiusFactor * Screen.height);
                Vector2 desired = new Vector2(pc.x, pc.y) + screenStick * radius;
                WriteAim(im, desired);
                return true;
            }
            catch { return false; }
        }

        private static void WriteAim(InputManager im, Vector2 v)
        {
            if (_aimMousePosProp != null && _aimMousePosProp.CanWrite) { _aimMousePosProp.SetValue(im, v); return; }
            if (_aimMousePosField != null) _aimMousePosField.SetValue(im, v);
        }

        // Freeze/restore crosshair across inventory: AimMousePosition is slaved to OS cursor,
        // which GridFocusController parks off-slots.
        internal static void WriteAbsoluteRaw(InputManager im, Vector2 pos)
        {
            if (im == null) return;
            try { Resolve(); WriteAim(im, pos); } catch { }
        }

        // Reads live AimMousePosition. False if member unresolvable.
        internal static bool TryReadAim(InputManager im, out Vector2 pos)
        {
            pos = default;
            if (im == null) return false;
            try
            {
                Resolve();
                if (_aimMousePosProp != null && _aimMousePosProp.CanRead) { pos = (Vector2)_aimMousePosProp.GetValue(im); return true; }
                if (_aimMousePosField != null) { pos = (Vector2)_aimMousePosField.GetValue(im); return true; }
            }
            catch { }
            return false;
        }
    }
}
