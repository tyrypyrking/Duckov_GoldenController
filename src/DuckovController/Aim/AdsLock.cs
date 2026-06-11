using System.Reflection;
using DuckovController.Config;
using Duckov.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuckovController.Aim
{
    // ADS hard-lock: while LT held, picks the nearest-to-crosshair enemy, points the
    // view cone at it each frame, and blends the cursor onto it. Drops on Hidden/LOS/dead/range
    // (gated by ViewConeQuery). While active, AimDriverPatch skips AutoAim.Run.
    internal static class AdsLock
    {
        private const int MaxCandidates = 8;
        private static readonly ViewConeQuery.CandidateInfo[] _candidates =
            new ViewConeQuery.CandidateInfo[MaxCandidates];

        private static Transform? _lock;
        private static bool _wasAdsHeld;

        private static FieldInfo? _mainCamField;
        private static PropertyInfo? _aimMousePosProp;
        private static FieldInfo? _aimMousePosField;
        private static bool _resolved;

        // True while lock held this frame; caller skips AutoAim.Run to avoid cursor fights.
        internal static bool IsActive { get; private set; }

        internal static void Reset()
        {
            _lock = null;
            _wasAdsHeld = false;
            IsActive = false;
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            var im = typeof(InputManager);
            _mainCamField = im.GetField("mainCam", BindingFlags.Instance | BindingFlags.NonPublic);
            _aimMousePosProp = im.GetProperty("AimMousePosition", BindingFlags.Instance | BindingFlags.Public);
            if (_aimMousePosProp == null || !_aimMousePosProp.CanWrite)
                _aimMousePosField = im.GetField("_aimMousePosCache", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        // Called from AimDriverPatch.Postfix. Returns true = owns cursor, caller skips AutoAim.Run.
        internal static bool Run(CharacterInputControl ctl, ControllerConfig cfg, Gamepad pad, Vector2 stickDeadzoned)
        {
            IsActive = false;
            try
            {
                if (cfg == null || !cfg.Aim.BaselineAssistEnabled) { _lock = null; _wasAdsHeld = false; return false; }
                if (pad == null) { _lock = null; _wasAdsHeld = false; return false; }
                bool adsHeld = pad.leftTrigger.isPressed;
                if (!adsHeld) { _lock = null; _wasAdsHeld = false; return false; }
                if (View.ActiveView != null) { _lock = null; _wasAdsHeld = false; return false; }
                if (ctl == null || ctl.inputManager == null) return false;
                var im = ctl.inputManager;
                var ch = im.ControllingCharacter;
                if (ch == null || ch.CurrentHoldItemAgent == null) { _lock = null; _wasAdsHeld = adsHeld; return false; }

                // AIM-1 v2: hold off on (re)acquiring during the post-escape window.
                if (BiasRing.SuppressingReacquire) { _lock = null; return false; }

                Resolve();
                var cam = _mainCamField?.GetValue(im) as Camera;
                if (cam == null) return false;

                var aacfg = cfg.AutoAim;
                float halfAngle = ch.ViewAngle * 0.5f;
                float viewDist = ch.ViewDistance;
                float senseRange = ch.SenseRange;
                if (aacfg.MaxTargetDistanceMeters > 0f)
                {
                    viewDist = Mathf.Min(viewDist, aacfg.MaxTargetDistanceMeters);
                    senseRange = Mathf.Min(senseRange, aacfg.MaxTargetDistanceMeters);
                }

                Vector3 coneAxis = ViewDirectionDriver.HasDirection
                    ? ViewDirectionDriver.CurrentDirection : ch.CurrentAimDirection;

                int count = ViewConeQuery.FindEnemiesInCone(
                    im, ch.transform.position, coneAxis, halfAngle, viewDist, senseRange,
                    ch.Team, aacfg.TargetThroughWalls, cam, AutoAim.OnScreenMarginFrac, _candidates);

                // Re-acquire on press-edge or if lock died.
                bool pressEdge = !_wasAdsHeld;
                _wasAdsHeld = true;
                bool firing = pad.rightTrigger.isPressed;
                // While firing, only re-pick on press-edge or if the current lock died — never
                // switch to a "better" target mid-burst (RS is for recoil control then).
                if (pressEdge || !IsLockStillValid(count))
                {
                    if (!firing || _lock == null)
                        _lock = PickByAimDirection(ch.transform.position, coneAxis, count);
                }

                if (_lock == null) return false;

                Vector3 body = Vector3.zero;
                bool found = false;
                for (int i = 0; i < count; i++)
                {
                    if (_candidates[i].Transform == _lock) { body = _candidates[i].BodyCenter; found = true; break; }
                }
                if (!found) { _lock = null; return false; }

                Vector3 toTarget = body - ch.transform.position; toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 1e-4f)
                    ViewDirectionDriver.SeedFromAimDirection(toTarget.normalized);

                // Blend cursor onto body center; drop lock if off-screen to avoid cursor flying to a corner.
                CharacterMainControl? lockMc = null;
                for (int j = 0; j < count; j++)
                    if (_candidates[j].Transform == _lock) { lockMc = _candidates[j].MainControl; break; }
                var leadBody = TargetLead.Compute(
                    body, lockMc != null ? lockMc.Velocity : Vector3.zero,
                    ch.transform.position,
                    TargetLead.EffectiveBulletSpeed(ch.GetGun(), ch),
                    AutoAim.RecoilCfg);
                var sp = cam.WorldToScreenPoint(leadBody);
                float marginX = Screen.width * 0.25f;
                float marginY = Screen.height * 0.25f;
                if (sp.z <= 0f || float.IsNaN(sp.x) || float.IsNaN(sp.y)
                    || sp.x < -marginX || sp.x > Screen.width + marginX
                    || sp.y < -marginY || sp.y > Screen.height + marginY)
                {
                    if (Log.Verbose)
                        Log.Debug_($"[biasring] ads lock-LOST off-screen sp=({sp.x:0},{sp.y:0}) "
                            + $"stickMag={stickDeadzoned.magnitude:0.00} "
                            + $"firing={(Gamepad.current != null && Gamepad.current.rightTrigger.isPressed)}");
                    _lock = null;
                    CursorBlend.Reset();
                    return false;
                }
                Vector2 targetScreen = new Vector2(sp.x, sp.y);
                Vector2 rest = ReadAim(im);
                var psp = cam.WorldToScreenPoint(ch.transform.position);
                Vector2 playerScreen = new Vector2(psp.x, psp.y);
                Vector2 aimPos;
                if (BiasRing.TierActive)
                {
                    if (!BiasRing.Step(targetScreen, stickDeadzoned, rest, playerScreen, true, aacfg, out aimPos))
                    {
                        _lock = null;
                        CursorBlend.Reset();
                        BiasRing.Reset();
                        return false;
                    }
                }
                else
                {
                    aimPos = CursorBlend.Step(rest, targetScreen, aacfg);
                }
                WriteAim(im, aimPos);
                if (Mouse.current != null) Mouse.current.WarpCursorPosition(aimPos);

                IsActive = true;
                return true;
            }
            catch
            {
                _lock = null;
                return false;
            }
        }

        // Lock valid = still in candidate set (ViewConeQuery gates Hidden/LOS/range/hp).
        private static bool IsLockStillValid(int count)
        {
            if (_lock == null) return false;
            for (int i = 0; i < count; i++)
                if (_candidates[i].Transform == _lock) return true;
            return false;
        }

        // Delegates to the shared picker so gun ADS and throw aim-assist use identical target logic.
        private static Transform? PickByAimDirection(Vector3 playerPos, Vector3 aimDir, int count)
            => TargetPicker.PickByAimDirection(playerPos, aimDir, _candidates, count);

        private static Vector2 ReadAim(InputManager im)
        {
            if (_aimMousePosProp != null) { var v = _aimMousePosProp.GetValue(im); if (v is Vector2 vv) return vv; }
            if (_aimMousePosField != null) { var v = _aimMousePosField.GetValue(im); if (v is Vector2 vv) return vv; }
            return Mouse.current != null ? Mouse.current.position.ReadValue()
                                         : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }

        private static void WriteAim(InputManager im, Vector2 v)
        {
            if (_aimMousePosProp != null && _aimMousePosProp.CanWrite) { _aimMousePosProp.SetValue(im, v); return; }
            if (_aimMousePosField != null) _aimMousePosField.SetValue(im, v);
        }
    }
}
