using System;
using System.Reflection;
using DuckovController.Config;
using Duckov.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuckovController.Aim
{
    // Auto-aim orchestrator. Called from AimDriverPatch.Postfix after SetAimInputUsingMouse(delta).
    internal static class AutoAim
    {
        private static FieldInfo? _mainCamField;          // InputManager.mainCam (NonPublic)
        private static PropertyInfo? _aimMousePosProp;    // InputManager.AimMousePosition
        private static FieldInfo? _aimMousePosBackingField; // fallback: _aimMousePosCache
        private static bool _reflectionResolved;
        private static bool _reflectionFailed;

        // Shared on-screen margin (frac of W/H) for free-aim and ADS lock; see ViewConeQuery.
        internal const float OnScreenMarginFrac = 0.05f;

        // Buffers — single-threaded main loop, safe as plain static.
        private const int MaxCandidates = 8;
        private static readonly ViewConeQuery.CandidateInfo[] _candidates =
            new ViewConeQuery.CandidateInfo[MaxCandidates];

        // Two-cursor model: rest cursor restored before SetAimInputUsingMouse so game's delta+recoil
        // layers on top of the true rest, not last frame's lock pos.
        private static Vector2 _trackedRestCursor;
        private static bool _hasTrackedRest;
        // Must be false when unarmed: rest-cursor restore would pin the cursor to a stale pos and block stick-delta accumulation.
        private static bool _isLocked;
        // AIM-1: lets AimDriverPatch know a lock pins the base cursor (so intendedMotion = stick intent, not base-delta).
        internal static bool IsLocked => _isLocked;
        // AIM-1: set once from AutoAimTiers.Apply so the lead layers share the live RecoilConfig.
        internal static RecoilConfig? RecoilCfg;

        // Field names match FogOfWarManager.cs so cone scaling mirrors player visibility.
        private static FieldInfo? _nightViewAngleField;
        private static FieldInfo? _nightSenseRangeField;
        private static FieldInfo? _nightViewDistanceField;

        private static void ResolveReflection()
        {
            if (_reflectionResolved) return;
            _reflectionResolved = true;
            try
            {
                var im = typeof(InputManager);
                _mainCamField = im.GetField("mainCam",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                _aimMousePosProp = im.GetProperty("AimMousePosition",
                    BindingFlags.Instance | BindingFlags.Public);
                if (_aimMousePosProp == null || !_aimMousePosProp.CanWrite)
                {
                    _aimMousePosBackingField = im.GetField("_aimMousePosCache",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    if (_aimMousePosBackingField == null)
                        _aimMousePosBackingField = im.GetField("aimMousePosition",
                            BindingFlags.Instance | BindingFlags.NonPublic);
                    if (_aimMousePosBackingField == null)
                        _aimMousePosBackingField = im.GetField("AimMousePosition",
                            BindingFlags.Instance | BindingFlags.Public);
                }
                if (_mainCamField == null)
                {
                    Log.Warn("AutoAim: InputManager.mainCam not found via reflection; disabling.");
                    _reflectionFailed = true;
                }
                if (_aimMousePosProp == null && _aimMousePosBackingField == null)
                {
                    Log.Warn("AutoAim: cannot find a writable AimMousePosition channel; disabling.");
                    _reflectionFailed = true;
                }
                var tod = typeof(TimeOfDayController);
                _nightViewAngleField = tod.GetField("NightViewAngleFactor",
                    BindingFlags.Static | BindingFlags.Public);
                _nightSenseRangeField = tod.GetField("NightSenseRangeFactor",
                    BindingFlags.Static | BindingFlags.Public);
                _nightViewDistanceField = tod.GetField("NightViewDistanceFactor",
                    BindingFlags.Static | BindingFlags.Public);
                // Absence falls back to 1.0 (daytime).
            }
            catch (Exception e)
            {
                Log.Warn($"AutoAim reflection threw: {e.Message}; disabling.");
                _reflectionFailed = true;
            }
        }

        private static int _logSuppressFrame = -1;
        private static int _suppressedExceptions;

        internal static void OnViewChanged()
        {
            // Clears lock + cursor state. ViewDirection NOT reset — last look direction persists
            // through inventory open/close so closing doesn't whip the cone home.
            TargetSelector.ResetLock();
            CursorBlend.Reset();
            _hasTrackedRest = false;
            _isLocked = false;
            AdsLock.Reset();
            MeleeAimAssist.Reset();
            ScopeAim.Reset();
            // AIM-1: defense-in-depth — clear any recoil offset on view changes too (the driver
            // also resets on view-open; this covers the OnViewChanged-from-Run path).
            RecoilAssist.Reset();
            BiasRing.ResetHard();
        }

        // Lightweight lock reset for weapon-switch contexts (melee↔gun): clears target + cursor
        // state without touching ViewDirectionDriver (so the look direction doesn't snap).
        internal static void ClearLock()
        {
            TargetSelector.ResetLock();
            CursorBlend.Reset();
            _hasTrackedRest = false;
            _isLocked = false;
            // AIM-1 M3: flush any live recoil offset so a weapon-switch doesn't carry stale kick.
            RecoilAssist.Reset();
            BiasRing.ResetHard();
        }

        // Called BEFORE SetAimInputUsingMouse so delta+recoil advance from the true rest cursor,
        // not last frame's lock pos. Without this, releasing a lock leaves cursor stuck at lock site.
        internal static void WriteRestCursorIfTracked(InputManager im, AutoAimConfig cfg)
        {
            try
            {
                if (cfg == null || !cfg.Enabled) return;
                if (!_hasTrackedRest || !_isLocked) return;
                if (im == null) return;
                ResolveReflection();
                if (_reflectionFailed) return;
                WriteAimMousePos(im, _trackedRestCursor);
            }
            catch { } // Run's rate-limited catch surfaces persistent failures.
        }

        // rawStick: post-deadzone/curve right-stick. stickWorldDir: cam-horizontal-plane mapped (caller).
        internal static void Run(
            CharacterInputControl ctl,
            AutoAimConfig cfg,
            Vector2 rawStick,
            Vector3 stickWorldDir,
            Vector2 stickDeadzoned)
        {
            try
            {
                if (cfg == null) return;
                if (!cfg.Enabled && !BiasRing.TierActive)
                {
                    // Off/Light/Standard tier: leave AimMousePosition as-is; reset so tier-bump starts clean.
                    if (CursorBlend.AimPos != Vector2.zero) CursorBlend.Reset();
                    TargetSelector.ResetLock();
                    _hasTrackedRest = false;
                    _isLocked = false;
                    return;
                }
                if (!Application.isFocused) return;
                if (Gamepad.current == null || Mouse.current == null) return;
                if (View.ActiveView != null) { OnViewChanged(); return; }
                if (!InputManager.InputActived) return;
                if (ctl == null || ctl.inputManager == null) return;
                var im = ctl.inputManager;
                var ch = im.ControllingCharacter;
                if (ch == null) { _isLocked = false; return; }
                if (ch.CurrentHoldItemAgent == null) { _isLocked = false; return; }

                // AIM-1 v2: during the post-escape re-acquire window, take no lock so the player
                // free-aims away (and can re-capture a different in-FOV enemy once it expires).
                if (BiasRing.SuppressingReacquire)
                {
                    if (_isLocked) { CursorBlend.Reset(); _isLocked = false; }
                    TargetSelector.ResetLock();
                    return;
                }

                // Idle-skip: stick at rest + no lock = skip OverlapSphere scan (dominant cost while walking/looting).
                // Stick flick re-acquires next frame; held lock keeps running for decay/hysteresis.
                if (rawStick.sqrMagnitude < 1e-6f && !_isLocked)
                {
                    if (CursorBlend.AimPos != Vector2.zero) CursorBlend.Reset();
                    return;
                }

                ResolveReflection();
                if (_reflectionFailed) return;

                var cam = _mainCamField!.GetValue(im) as Camera;
                if (cam == null) return;

                // Seed view direction on first activation so cone doesn't snap from world-forward.
                if (!ViewDirectionDriver.HasDirection)
                    ViewDirectionDriver.SeedFromAimDirection(ch.CurrentAimDirection);

                // 1. Read rest cursor: (prev rest) + (stick delta) + (recoil) = new rest this frame.
                Vector2 restPos = ReadAimMousePos(im);
                _trackedRestCursor = restPos;
                _hasTrackedRest = true;

                float halfAngle = ch.ViewAngle * 0.5f;
                float viewDist = ch.ViewDistance;
                float senseRange = ch.SenseRange;
                if (cfg.RespectNightVisionTimeOfDay)
                {
                    float t = Mathf.Clamp01(ch.NightVisionAbility + (ch.FlashLight ? 0.3f : 0f));
                    float aFactor = ReadFloatField(_nightViewAngleField, 1f);
                    float sFactor = ReadFloatField(_nightSenseRangeField, 1f);
                    float dFactor = ReadFloatField(_nightViewDistanceField, 1f);
                    halfAngle *= Mathf.Lerp(aFactor, 1f, t);
                    senseRange *= Mathf.Lerp(sFactor, 1f, t);
                    viewDist *= Mathf.Lerp(dFactor, 1f, t);
                }

                // Distance cap: prevents locking onto distant decorative entities the game considers visible.
                if (cfg.MaxTargetDistanceMeters > 0f)
                {
                    viewDist = Mathf.Min(viewDist, cfg.MaxTargetDistanceMeters);
                    senseRange = Mathf.Min(senseRange, cfg.MaxTargetDistanceMeters);
                }

                // Cone axis: decouple=on scans where player looks (ViewDirectionDriver), not the lock center.
                // Without this, stick rotation only switches targets when scores are near-tied.
                Vector3 coneAxis = (cfg.DecoupleViewFromAim
                                     && ViewDirectionDriver.HasDirection)
                    ? ViewDirectionDriver.CurrentDirection
                    : ch.CurrentAimDirection;

                int count = ViewConeQuery.FindEnemiesInCone(
                    im,
                    ch.transform.position,
                    coneAxis,
                    halfAngle,
                    viewDist,
                    senseRange,
                    ch.Team,
                    cfg.TargetThroughWalls,
                    cam,
                    OnScreenMarginFrac,
                    _candidates);

                var segment = new ArraySegment<ViewConeQuery.CandidateInfo>(_candidates, 0, count);
                var screenSize = new Vector2(Screen.width, Screen.height);
                // AIM-1 M4: while firing, raise the escape bar so RS counter-recoil doesn't drop
                // the hip-fire lock mid-burst (mirrors ScopeAim's FireEscapeMultiplier logic).
                // AIM-1 v2: when the bias ring owns escape, disable TargetSelector's stick-magnitude
                // escape (unreachable threshold) so the two mechanisms don't compete. Otherwise keep
                // the original fire-scaled threshold (Off tier / ring master off).
                // Hip-fire has NO bias-ring safety guard, so TargetSelector owns escape: a hard stick
                // push away from the target drops the lock immediately. You "recoil-control your way
                // back" as long as you don't aim far off-target. No fire multiplier — escape stays snappy.
                float escapeMag = cfg.OverrideStickMagnitude;

                var lockT = TargetSelector.Pick(
                    segment, restPos, screenSize, rawStick, stickWorldDir,
                    ch.transform.position, viewDist, halfAngle, cam, cfg, escapeMag);

                Vector2? targetScreen = null;
                Vector3 lockedBody = Vector3.zero;
                if (lockT != null && lockT)
                {
                    for (int i = 0; i < count; i++)
                    {
                        if (_candidates[i].Transform == lockT)
                        {
                            lockedBody = _candidates[i].BodyCenter;
                            var leadWorld = TargetLead.Compute(
                                _candidates[i].BodyCenter,
                                _candidates[i].MainControl != null ? _candidates[i].MainControl.Velocity : Vector3.zero,
                                ch.transform.position,
                                TargetLead.EffectiveBulletSpeed(ch.GetGun(), ch),
                                AutoAim.RecoilCfg);
                            var sp = cam.WorldToScreenPoint(leadWorld);
                            // Reject behind-camera/origin projection (dead target) to avoid cursor corner-fling.
                            float marginX = Screen.width * 0.25f;
                            float marginY = Screen.height * 0.25f;
                            if (sp.z > 0f && !float.IsNaN(sp.x) && !float.IsNaN(sp.y)
                                && sp.x >= -marginX && sp.x <= Screen.width + marginX
                                && sp.y >= -marginY && sp.y <= Screen.height + marginY)
                                targetScreen = new Vector2(sp.x, sp.y);
                            break;
                        }
                    }
                }

                if (targetScreen.HasValue)
                {
                    // Keep the view cone pinned on the locked target (the driver no longer swings it
                    // from the stick while locked), so a strafing target stays in the cone and the
                    // lock survives recoil-control input instead of dropping on a count=0 cone miss.
                    Vector3 toLock = lockedBody - ch.transform.position; toLock.y = 0f;
                    if (toLock.sqrMagnitude > 1e-4f)
                        ViewDirectionDriver.SeedFromAimDirection(toLock.normalized);
                    Vector2 aimPos;
                    if (BiasRing.TierActive)
                    {
                        var psp = cam.WorldToScreenPoint(ch.transform.position);
                        Vector2 playerScreen = new Vector2(psp.x, psp.y);
                        if (!BiasRing.Step(targetScreen.Value, stickDeadzoned, restPos, playerScreen, false, cfg, out aimPos))
                        {
                            // Ring escaped: drop the lock; the suppress window (set inside Step) now
                            // gates re-acquire so the crosshair free-aims away.
                            TargetSelector.ResetLock();
                            CursorBlend.Reset();
                            BiasRing.Reset();
                            _isLocked = false;
                            return;
                        }
                    }
                    else
                    {
                        aimPos = CursorBlend.Step(restPos, targetScreen, cfg);
                    }
                    WriteAimMousePos(im, aimPos);
                    Mouse.current.WarpCursorPosition(aimPos);
                    _isLocked = true;
                }
                else
                {
                    // Diagnostic: a previously-held hip lock just dropped because no valid target this
                    // frame (lockT null = TargetSelector found none / left the cone; lockT set but no
                    // targetScreen = off-screen projection). Catches "fling" that is NOT a ring-escape.
                    if (Log.Verbose && _isLocked)
                        Log.Debug_($"[biasring] hip lock-LOST lockT={(lockT != null && lockT)} count={count} "
                            + $"stickMag={stickDeadzoned.magnitude:0.00} "
                            + $"firing={(Gamepad.current != null && Gamepad.current.rightTrigger.isPressed)}");
                    // Reset blend so the next lock reacquires from the rest cursor, not the stale lock pos.
                    CursorBlend.Reset();
                    _isLocked = false;
                }
            }
            catch (Exception e)
            {
                _suppressedExceptions++;
                var frame = Time.frameCount;
                if (frame - _logSuppressFrame > 600)
                {
                    Log.Warn($"AutoAim threw {_suppressedExceptions}x: {e.Message}");
                    _logSuppressFrame = frame;
                    _suppressedExceptions = 0;
                }
            }
        }

        private static Vector2 ReadAimMousePos(InputManager im)
        {
            if (_aimMousePosProp != null)
            {
                var v = _aimMousePosProp.GetValue(im);
                if (v is Vector2 vv) return vv;
            }
            if (_aimMousePosBackingField != null)
            {
                var v = _aimMousePosBackingField.GetValue(im);
                if (v is Vector2 vv) return vv;
            }
            return Mouse.current != null ? Mouse.current.position.ReadValue()
                                         : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }

        private static void WriteAimMousePos(InputManager im, Vector2 v)
        {
            if (_aimMousePosProp != null && _aimMousePosProp.CanWrite)
            {
                _aimMousePosProp.SetValue(im, v);
                return;
            }
            if (_aimMousePosBackingField != null)
            {
                _aimMousePosBackingField.SetValue(im, v);
                return;
            }
            // No channel — disable for the session.
            _reflectionFailed = true;
        }

        private static float ReadFloatField(FieldInfo? f, float fallback)
        {
            if (f == null) return fallback;
            try
            {
                var v = f.GetValue(null);
                return v is float fv ? fv : fallback;
            }
            catch { return fallback; }
        }
    }
}
