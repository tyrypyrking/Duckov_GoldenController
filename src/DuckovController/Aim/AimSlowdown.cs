using System.Reflection;
using DuckovController.Config;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuckovController.Aim
{
    // Tier 2 aim assist (default off): if the cursor is within a screen-pixel
    // radius of an acquired target, attenuate input delta so the player gets
    // more dwell time for precision.
    internal static class AimSlowdown
    {
        private static FieldInfo? _aimMousePosCacheField;
        private static FieldInfo? _mainCamField;
        private static bool _reflectionResolved;

        private static void ResolveReflection()
        {
            if (_reflectionResolved) return;
            _reflectionResolved = true;
            var t = typeof(InputManager);
            _aimMousePosCacheField = t.GetField("_aimMousePosCache",
                BindingFlags.Instance | BindingFlags.NonPublic);
            _mainCamField = t.GetField("mainCam",
                BindingFlags.Instance | BindingFlags.NonPublic);
        }

        internal static void Apply(
            ref Vector2 delta,
            Transform? acquiredTarget,
            CharacterInputControl ctl,
            AimConfig cfg)
        {
            if (!cfg.SlowdownEnabled || acquiredTarget == null) return;
            ResolveReflection();
            var im = ctl.inputManager;
            if (im == null) return;
            if (_mainCamField == null) return;
            var cam = _mainCamField.GetValue(im) as Camera;
            if (cam == null) return;

            Vector2 cursor;
            if (_aimMousePosCacheField != null)
                cursor = (Vector2)_aimMousePosCacheField.GetValue(im);
            else if (Mouse.current != null)
                cursor = Mouse.current.position.ReadValue();
            else
                return;

            var targetScreen = (Vector2)cam.WorldToScreenPoint(acquiredTarget.position);
            var dist = (targetScreen - cursor).magnitude;
            if (dist <= cfg.SlowdownScreenRadiusPx)
                delta *= cfg.SlowdownFactor;
        }
    }
}
