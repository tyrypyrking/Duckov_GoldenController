using UnityEngine;

namespace DuckovController.Aim
{
    internal static class ResponseCurve
    {
        // Power curve preserving direction. exponent=1 is linear,
        // exponent=2 gives finer center control with full range at the edge.
        internal static Vector2 Apply(Vector2 v, float exponent)
        {
            var mag = v.magnitude;
            if (mag <= 0f) return Vector2.zero;
            if (exponent == 1f) return v;
            // Clamp away from non-positive values so a typo'd config doesn't
            // silently disable the curve.
            if (exponent < 0.1f) exponent = 0.1f;
            var curved = Mathf.Pow(mag, exponent);
            return v * (curved / mag);
        }
    }
}
