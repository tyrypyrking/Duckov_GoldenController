using UnityEngine;

namespace DuckovController.Aim
{
    // Decouples FOW reveal cone from auto-aim cursor: looking direction follows stick, not locked target.
    // Updated by AimDriverPatch.Postfix each frame; idle stick holds last direction.
    // FogOfWarPatch reads CurrentDirection to overwrite mainVis.transform.rotation.
    // Bullets unaffected — they consume AimMousePosition / locked cursor.
    internal static class ViewDirectionDriver
    {
        private static Vector3 _viewDir = Vector3.forward; // horizontal, normalized
        private static bool _hasInitial;

        internal static Vector3 CurrentDirection => _viewDir;
        internal static bool HasDirection => _hasInitial;

        // minStickMag is a separate deadzone from aim so a slight wiggle doesn't recapture view direction.
        internal static void Update(Vector3 stickWorldDir, float stickMagnitude, float minStickMag)
        {
            if (stickMagnitude < minStickMag) return;
            if (stickWorldDir.sqrMagnitude < 1e-6f) return;
            _viewDir = stickWorldDir.normalized;
            _hasInitial = true;
        }

        // Seed on AutoAim activation so first lock doesn't snap cone from forward-default.
        internal static void SeedFromAimDirection(Vector3 aimDirection)
        {
            var flat = aimDirection;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-6f) return;
            _viewDir = flat.normalized;
            _hasInitial = true;
        }

        internal static void Reset()
        {
            _hasInitial = false;
            _viewDir = Vector3.forward;
        }
    }
}
