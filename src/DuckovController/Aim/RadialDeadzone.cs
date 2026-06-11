using UnityEngine;

namespace DuckovController.Aim
{
    internal static class RadialDeadzone
    {
        // Standard radial deadzone: magnitudes below `inner` snap to zero,
        // magnitudes above `outer` clamp to 1, in-between is rescaled linearly.
        internal static Vector2 Apply(Vector2 raw, float inner, float outer)
        {
            // Guard against degenerate config (inner >= outer) producing NaN.
            if (outer <= inner) outer = inner + 0.01f;
            var mag = raw.magnitude;
            if (mag <= inner) return Vector2.zero;
            if (mag >= outer) return raw / mag;
            var rescaled = (mag - inner) / (outer - inner);
            return raw * (rescaled / mag);
        }
    }
}
