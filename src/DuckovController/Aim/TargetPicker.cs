using UnityEngine;

namespace DuckovController.Aim
{
    // Target-pick strategies shared across aim subsystems.
    internal static class TargetPicker
    {
        // Angle-based pick: gun ADS (AdsLock). Fixed-radius cursor makes screen-distance picks wrong
        // (a near enemy loses even when aimed at), so we pick whoever the aim axis points most directly at.
        internal static Transform? PickByAimDirection(
            Vector3 playerPos, Vector3 aimDir, ViewConeQuery.CandidateInfo[] candidates, int count)
        {
            if (count <= 0) return null;
            Vector3 axis = aimDir; axis.y = 0f;
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.forward; else axis.Normalize();
            Transform? best = null;
            float bestAngle = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Vector3 to = candidates[i].BodyCenter - playerPos; to.y = 0f;
                if (to.sqrMagnitude < 1e-6f) continue;
                float ang = Vector3.Angle(axis, to.normalized);
                if (ang < bestAngle) { bestAngle = ang; best = candidates[i].Transform; }
            }
            return best;
        }

        // Nearest candidate to a world point (XZ plane). Used by throw aim-assist: the reticle pans
        // freely, so we lock whoever is closest to where the cursor is pointing.
        internal static Transform? PickNearestToPoint(
            Vector3 point, ViewConeQuery.CandidateInfo[] candidates, int count)
        {
            if (count <= 0) return null;
            point.y = 0f;
            Transform? best = null;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = candidates[i].BodyCenter; p.y = 0f;
                float d = (p - point).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; best = candidates[i].Transform; }
            }
            return best;
        }
    }
}
