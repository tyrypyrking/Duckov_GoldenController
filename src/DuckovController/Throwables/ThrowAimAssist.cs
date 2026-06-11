using DuckovController.Aim;
using DuckovController.Config;
using UnityEngine;

namespace DuckovController.Throwables
{
    // LT lock for throws: finds the enemy nearest to the current aim point (cursor position),
    // then returns its ground position clamped to castRange, projected to screen.
    internal static class ThrowAimAssist
    {
        private const int MaxCandidates = 8;
        private static readonly ViewConeQuery.CandidateInfo[] _cand =
            new ViewConeQuery.CandidateInfo[MaxCandidates];

        internal static bool TryGetLockScreenPoint(
            CharacterInputControl ctl, ControllerConfig cfg, Camera? cam, float castRange,
            out Vector2 screen)
        {
            screen = default;
            if (ctl == null) return false;
            var im = ctl.inputManager;
            if (im == null) return false;
            var ch = im.ControllingCharacter;
            if (ch == null || cam == null || castRange <= 0f) return false;

            float halfAngle = ch.ViewAngle * 0.5f;
            float viewDist = Mathf.Min(ch.ViewDistance, castRange);
            float senseRange = Mathf.Min(ch.SenseRange, castRange);

            Vector3 axis = ViewDirectionDriver.HasDirection
                ? ViewDirectionDriver.CurrentDirection : ch.CurrentAimDirection;

            int count = ViewConeQuery.FindEnemiesInCone(
                im, ch.transform.position, axis, halfAngle, viewDist, senseRange,
                ch.Team, cfg.AutoAim.TargetThroughWalls, cam, AutoAim.OnScreenMarginFrac, _cand);

            var locked = TargetPicker.PickNearestToPoint(ch.GetCurrentAimPoint(), _cand, count);
            if (locked == null) return false;

            Vector3 body = Vector3.zero;
            bool found = false;
            for (int i = 0; i < count; i++)
                if (_cand[i].Transform == locked) { body = _cand[i].BodyCenter; found = true; break; }
            if (!found) return false;

            // Clamp the landing point to castRange (the throw can't reach past it anyway).
            // Preserve the enemy's y so the arc lands at their feet, not at ground level.
            Vector3 flat = body - ch.transform.position; float y = body.y; flat.y = 0f;
            if (flat.magnitude > castRange)
            {
                flat = flat.normalized * castRange;
                body = ch.transform.position + flat; body.y = y;
            }

            var sp = cam.WorldToScreenPoint(body);
            if (sp.z <= 0f || float.IsNaN(sp.x) || float.IsNaN(sp.y)) return false;
            screen = new Vector2(sp.x, sp.y);
            return true;
        }
    }
}
