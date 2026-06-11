using System;
using DuckovController.Config;
using UnityEngine;

namespace DuckovController.Aim
{
    // Scoring + hysteresis + stick-override target picker.
    internal static class TargetSelector
    {
        private static Transform? _currentLock;
        private static float _lockStartTime;

        internal static Transform? CurrentLock => _currentLock;

        internal static Transform? Pick(
            ArraySegment<ViewConeQuery.CandidateInfo> candidates,
            Vector2 restCursor,
            Vector2 screenSize,
            Vector2 rawStick,
            Vector3 stickWorldDir,
            Vector3 playerPos,
            float effectiveViewDistance,
            float effectiveHalfAngle,
            Camera mainCam,
            AutoAimConfig cfg,
            float overrideStickMagnitude = -1f)
        {
            // Drop a destroyed lock (Unity-null aware).
            if (_currentLock != null && !_currentLock) _currentLock = null;

            int count = candidates.Count;
            if (count == 0)
            {
                _currentLock = null;
                return null;
            }

            // Stick-override: break lock when stick pushed away from target — checked before scoring
            // so a deliberate flick wins even when scores would hold the lock.
            // AIM-1 M4: caller may pass a fire-scaled threshold so RS counter-recoil doesn't drop
            // the lock mid-burst; a deliberate bigger push still escapes.
            float stickEscapeThreshold = overrideStickMagnitude >= 0f
                ? overrideStickMagnitude : cfg.OverrideStickMagnitude;
            if (_currentLock != null && rawStick.magnitude >= stickEscapeThreshold)
            {
                var lockPos = _currentLock.position;
                var toLock = lockPos - playerPos; toLock.y = 0f;
                if (toLock.sqrMagnitude > 1e-6f)
                {
                    toLock.Normalize();
                    float ang = Vector3.Angle(stickWorldDir, toLock);
                    if (ang > cfg.OverrideAngleDegrees) _currentLock = null;
                }
            }

            int lockIdx = -1;
            float bestScore = float.NegativeInfinity;
            int bestIdx = -1;
            float secondBest = float.NegativeInfinity;

            float effHalf = Mathf.Max(1f, effectiveHalfAngle);
            float effDist = Mathf.Max(0.01f, effectiveViewDistance);
            float screenH = Mathf.Max(1f, screenSize.y);

            for (int i = 0; i < count; i++)
            {
                ref var c = ref AsRef(candidates.Array!, candidates.Offset + i);

                // Negative z = off-camera; use worst-case screen dist.
                var screenP = (Vector3)mainCam.WorldToScreenPoint(c.BodyCenter);
                float screenDistPx = (screenP.z > 0f)
                    ? ((Vector2)screenP - restCursor).magnitude
                    : screenH; // worst case, off-camera

                float screenDistN = Mathf.Clamp01(1f - screenDistPx / screenH);
                float worldDistN  = Mathf.Clamp01(1f - c.WorldDist / effDist);
                float centerN     = c.InCone
                    ? Mathf.Clamp01(1f - c.AngleFromAxisDeg / effHalf)
                    : 0f;
                float lowHpN      = Mathf.Clamp01(1f - c.HpFraction);

                float score = cfg.WeightScreenDist * screenDistN
                            + cfg.WeightWorldDist  * worldDistN
                            + cfg.WeightCenterness * centerN
                            + cfg.WeightLowHp      * lowHpN;

                if (_currentLock != null && c.Transform == _currentLock) lockIdx = i;

                if (score > bestScore)
                {
                    secondBest = bestScore;
                    bestScore = score;
                    bestIdx = i;
                }
                else if (score > secondBest)
                {
                    secondBest = score;
                }
            }

            if (lockIdx >= 0 && _currentLock != null)
            {
                float lockElapsedRealMs = (Time.realtimeSinceStartup - _lockStartTime) * 1000f;

                float lockScore = ScoreOf(lockIdx, candidates, restCursor, screenSize,
                                          mainCam, playerPos, effHalf, effDist, cfg);
                float topOther = bestIdx == lockIdx ? secondBest : bestScore;

                if (lockElapsedRealMs < cfg.MinLockTimeMs)
                    return _currentLock; // forced hold
                if (lockScore >= topOther)
                    return _currentLock;
                if (topOther < lockScore * (1f + cfg.SwitchMargin))
                    return _currentLock;
                // Otherwise: switch to bestIdx (handled below).
            }

            if (bestIdx < 0) { _currentLock = null; return null; }
            var newLock = candidates.Array![candidates.Offset + bestIdx].Transform;
            if (newLock != _currentLock)
            {
                _currentLock = newLock;
                _lockStartTime = Time.realtimeSinceStartup;
            }
            return _currentLock;
        }

        // Called on scene/disable transitions to prevent stale lock carrying across raids.
        internal static void ResetLock()
        {
            _currentLock = null;
            _lockStartTime = 0f;
        }

        private static ref T AsRef<T>(T[] array, int index) => ref array[index];

        // Recompute lock's score after global best is known (avoids two-pass scoring).
        private static float ScoreOf(
            int idx,
            ArraySegment<ViewConeQuery.CandidateInfo> candidates,
            Vector2 restCursor,
            Vector2 screenSize,
            Camera mainCam,
            Vector3 playerPos,
            float effHalf,
            float effDist,
            AutoAimConfig cfg)
        {
            ref var c = ref AsRef(candidates.Array!, candidates.Offset + idx);
            var screenP = (Vector3)mainCam.WorldToScreenPoint(c.BodyCenter);
            float screenH = Mathf.Max(1f, screenSize.y);
            float screenDistPx = (screenP.z > 0f)
                ? ((Vector2)screenP - restCursor).magnitude
                : screenH;
            float screenDistN = Mathf.Clamp01(1f - screenDistPx / screenH);
            float worldDistN  = Mathf.Clamp01(1f - c.WorldDist / Mathf.Max(0.01f, effDist));
            float centerN     = c.InCone
                ? Mathf.Clamp01(1f - c.AngleFromAxisDeg / Mathf.Max(1f, effHalf))
                : 0f;
            float lowHpN      = Mathf.Clamp01(1f - c.HpFraction);
            return cfg.WeightScreenDist * screenDistN
                 + cfg.WeightWorldDist  * worldDistN
                 + cfg.WeightCenterness * centerN
                 + cfg.WeightLowHp      * lowHpN;
        }
    }
}
