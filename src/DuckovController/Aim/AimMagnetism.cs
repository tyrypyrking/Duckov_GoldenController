using System.Reflection;
using DuckovController.Config;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuckovController.Aim
{
    // Tier 1 aim assist: biases mouseDelta toward the screen-pos of the stick-facing enemy.
    // Game's SetAimInputUsingMouse SphereCast handles final line-of-fire snap.
    internal static class AimMagnetism
    {
        private static FieldInfo? _aimMousePosCacheField;
        private static FieldInfo? _aimTargetFinderField;
        private static FieldInfo? _mainCamField;
        private static bool _reflectionResolved;

        private static void ResolveReflection()
        {
            if (_reflectionResolved) return;
            _reflectionResolved = true;
            var t = typeof(InputManager);
            _aimMousePosCacheField = t.GetField("_aimMousePosCache",
                BindingFlags.Instance | BindingFlags.NonPublic);
            _aimTargetFinderField = t.GetField("aimTargetFinder",
                BindingFlags.Instance | BindingFlags.Public);
            _mainCamField = t.GetField("mainCam",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (_aimMousePosCacheField == null) Log.Warn("AimMagnetism: _aimMousePosCache not found; falling back to Mouse.current position");
            if (_aimTargetFinderField == null) Log.Warn("AimMagnetism: aimTargetFinder not found; magnetism disabled");
            if (_mainCamField == null) Log.Warn("AimMagnetism: mainCam not found; magnetism disabled");
        }

        // Adds pixel-unit magnetism bias to `delta`. Returns acquired target (for downstream tiers) or null.
        internal static Transform? Bias(
            ref Vector2 delta,
            Vector2 rawStick,
            CharacterInputControl ctl,
            AimConfig cfg)
        {
            if (!cfg.MagnetismEnabled) return null;
            if (rawStick.magnitude < cfg.MagnetismMinStickMag) return null;

            ResolveReflection();
            if (_aimTargetFinderField == null || _mainCamField == null) return null;

            var im = ctl.inputManager;
            if (im == null) return null;

            var finder = _aimTargetFinderField.GetValue(im) as AimTargetFinder;
            if (finder == null) return null;

            var cam = _mainCamField.GetValue(im) as Camera;
            if (cam == null) return null;

            var character = im.ControllingCharacter;
            if (character == null) return null;

            // Override AimTargetFinder.searchRadius so our config controls reach, not the game default.
            try { finder.searchRadius = cfg.MagnetismSearchRadiusMeters; }
            catch { /* ignore — field may have been refactored away */ }

            var camRight = cam.transform.right; camRight.y = 0f; camRight.Normalize();
            var camFwd = cam.transform.forward; camFwd.y = 0f; camFwd.Normalize();
            var stickWorld = (camRight * rawStick.x + camFwd * rawStick.y).normalized;
            var lookahead = character.transform.position + stickWorld * cfg.MagnetismLookAheadMeters;

            CharacterMainControl? foundCharacter = null;
            var target = finder.Find(true, lookahead, ref foundCharacter);
            if (target == null) return null;
            // Perception gate: don't bias toward fogged / unperceived / render-cloaked (AIM-6) enemies.
            if (foundCharacter != null &&
                (foundCharacter.Hidden || CloakGate.IsCloakedFromPlayer(foundCharacter))) return null;

            var targetScreen = (Vector2)cam.WorldToScreenPoint(target.position);
            if (float.IsNaN(targetScreen.x) || float.IsNaN(targetScreen.y)) return null;

            Vector2 cursor;
            if (_aimMousePosCacheField != null)
                cursor = (Vector2)_aimMousePosCacheField.GetValue(im);
            else
                cursor = Mouse.current != null ? Mouse.current.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            // Bias toward target; player fights the magnet by moving stick away.
            var toTarget = targetScreen - cursor;
            var bias = toTarget * cfg.MagnetismStrength
                       * cfg.MagnetismRateHz * Time.deltaTime;

            var maxStep = toTarget.magnitude;
            if (bias.magnitude > maxStep)
                bias = toTarget;

            delta += bias;
            return target;
        }
    }
}
