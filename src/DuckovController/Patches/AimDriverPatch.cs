using DuckovController.Aim;
using DuckovController.Config;
using Duckov.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuckovController.Patches
{
    // Drives the game's aim pipeline from the right stick as a Postfix (additive; KBM preserved).
    [HarmonyPatch(typeof(CharacterInputControl), "Update")]
    internal static class AimDriverPatch
    {
        internal static ControllerConfig? Cfg;

        // Camera.main does a FindGameObjectWithTag scan on every call — cache it; == null re-fetches after scene reload.
        private static Camera? _mainCam;
        private static Camera? MainCam()
        {
            if (_mainCam == null) _mainCam = Camera.main;
            return _mainCam;
        }

        private static int _logSuppressFrame = -1;
        private static int _suppressedExceptionCount;

        // Two-cursor freeze state: last good gameplay AimMousePosition; View-open flag for restore-on-exit.
        private static Vector2 _savedAim;
        private static bool _savedAimValid;
        private static bool _wasViewOpen;
        // AIM-4: true on the frame(s) ScopeAim owned the cursor; drives the clean release-edge exit.
        private static bool _wasScoped;
        // AIM-1: previous radial-base cursor, for the free-aim counter-steer motion delta.
        private static Vector2 _lastBasePx;
        private static bool _hasLastBase;

        [HarmonyPostfix]
        internal static void Postfix(CharacterInputControl __instance)
        {
            try
            {
                if (!DuckovController.Diagnostics.PerfFlags.AimDriver) return;
                if (Cfg == null) return;
                if (!Application.isFocused) return;
                var pad = Gamepad.current;
                if (pad == null) return;
                // The game's SetAimInputUsingMouse calls Mouse.current.WarpCursorPosition
                // unconditionally — skip the postfix if no mouse device is registered.
                if (Mouse.current == null) return;
                if (__instance == null || __instance.inputManager == null) return;

                var imAim = __instance.inputManager;
                bool viewIsOpen = View.ActiveView != null;

                // Two-cursor freeze: AimMousePosition is slaved to the OS cursor, which GridFocusController
                // parks off-slot while a View is open. Save live aim, re-assert each frame, restore on exit.
                if (!viewIsOpen
                    && RadialCursor.TryReadAim(imAim, out var liveAim)
                    && !float.IsNaN(liveAim.x) && liveAim.sqrMagnitude > 1f)
                {
                    _savedAim = liveAim;
                    _savedAimValid = true;
                }

                if (viewIsOpen)
                {
                    if (_savedAimValid) RadialCursor.WriteAbsoluteRaw(imAim, _savedAim);
                    // Abort any in-progress throw aim so we don't throw blind; keep throwable equipped.
                    DuckovController.Throwables.ThrowableController.CancelAim(imAim);
                    ScopeAim.Reset();
                    RecoilAssist.Reset();
                    BiasRing.ResetHard();
                    _hasLastBase = false;
                    _wasScoped = false;
                    _wasViewOpen = true;
                    return;
                }
                if (_wasViewOpen)
                {
                    // First frame after View close: restore crosshair before hold/idle path re-applies AimMousePosition.
                    _wasViewOpen = false;
                    if (_savedAimValid) RadialCursor.WriteAbsoluteRaw(imAim, _savedAim);
                }

                if (!InputManager.InputActived) return;
                if (__instance.inputManager.ControllingCharacter == null) return;

                var im = __instance.inputManager;

                // Throw-aim owns the cursor: drive free-pan / aim-assist, skip gun radial/AutoAim/AdsLock.
                if (DuckovController.Diagnostics.PerfFlags.Throwables
                    && DuckovController.Throwables.ThrowableController.IsAiming)
                {
                    DuckovController.Throwables.ThrowableController.DriveCursor(__instance, Cfg, pad);
                    im.SetAimInputUsingMouse(Vector2.zero);
                    return;
                }

                // Melee swing lock: hold facing toward acquired target through the swing.
                if (DuckovController.Aim.MeleeAimAssist.IsLocked)
                {
                    var meleeCh = __instance.inputManager.ControllingCharacter;
                    if (DuckovController.Aim.MeleeAimAssist.HoldAim(meleeCh))
                    {
                        // HoldAim set inputAimPoint = target (authoritative for the hit-arc).
                        // Drive the crosshair onto the target for the visual lock. Do NOT call
                        // SetAimInputUsingMouse — it re-derives inputAimPoint from the stale cursor
                        // and reverts the swing direction.
                        DuckovController.Aim.MeleeAimAssist.DriveCrosshair(im, MainCam());
                        return;
                    }
                }

                // AIM-4: scoped-weapon free-look + soft assist owns the cursor while LT is held.
                // Branch BEFORE the radial/AutoAim/AdsLock path; non-scoped guns are unaffected.
                var chScope = im.ControllingCharacter;
                var gunScope = chScope != null ? chScope.GetGun() : null;
                ScopeDetector.LogIfChanged(gunScope, Cfg.Scope, Cfg.Diagnostics.DebugLog);
                bool scopedAds = chScope != null
                    && chScope.GetMeleeWeapon() == null
                    && pad.leftTrigger.isPressed
                    && ScopeDetector.IsScoped(gunScope, Cfg.Scope);
                if (scopedAds)
                {
                    AdsLock.Reset();        // ensure no competing cursor writer
                    AutoAim.ClearLock();
                    ScopeAim.Run(__instance, Cfg, pad);
                    im.SetAimInputUsingMouse(Vector2.zero);   // absolute write; no delta scaling
                    // AIM-1: don't carry the hip-fire recoil offset into a scope session
                    // (scope recoil is out of scope for v1; predictive lead still applies).
                    RecoilAssist.Reset(); _hasLastBase = false;
                    BiasRing.ResetHard();
                    _wasScoped = true;
                    return;
                }
                if (_wasScoped)
                {
                    _wasScoped = false;
                    ScopeAim.Exit(im, Cfg);  // clean release edge: re-seed cursor, reset
                }

                // AIM-1 v2: decay the post-escape re-acquire window once per gun-path frame.
                BiasRing.Tick(Time.unscaledDeltaTime);

                var raw = pad.rightStick.ReadValue();
                var stick = RadialDeadzone.Apply(raw, Cfg.Aim.DeadzoneInner, Cfg.Aim.DeadzoneOuter);
                var ch = im.ControllingCharacter;
                bool idle = stick.sqrMagnitude < 1e-7f;

                // 1. Base cursor: fixed-radius radial (stick → direction; hold last pos when idle).
                // Written absolutely — SetAimInputUsingMouse scales delta by MouseSensitivity/10 (crawl + jitter).
                bool placed = false;
                if (!idle)
                {
                    placed = RadialCursor.WriteAbsolute(
                        im, ch, stick.normalized, Cfg.Aim.CursorCircleRadiusFactor);
                }

                // 2. View direction (the sight cone follows the stick/aim).
                Vector3 stickWorldDir = Vector3.zero;
                var cam = MainCam();
                if (cam != null && raw.sqrMagnitude > 1e-6f)
                {
                    var camRight = cam.transform.right; camRight.y = 0f; camRight.Normalize();
                    var camFwd = cam.transform.forward; camFwd.y = 0f; camFwd.Normalize();
                    stickWorldDir = (camRight * raw.x + camFwd * raw.y).normalized;
                }
                // While a target is locked, DON'T let the stick swing the view cone. The recoil-
                // control stick push would otherwise rotate the cone off the target, drop it from the
                // cone query (count=0) and break the lock — the player "turns around" mid-burst. The
                // lock paths keep the cone pinned on the target; an escape drops the lock and restores
                // free look on the next frame. (lockedLastFrame reads this frame's pre-Run state.)
                bool lockedLastFrame = AutoAim.IsLocked || AdsLock.IsActive;
                if (!idle && !lockedLastFrame)
                    ViewDirectionDriver.Update(
                        stickWorldDir, stick.magnitude, Cfg.AutoAim.ViewDirectionMinStickMag);

                // 3. Lock — may override AimMousePosition. MUST run before SetAimInputUsingMouse so gun aim uses
                // the locked position. (Running after caused "lock only works on release": gun aimed at radial
                // cursor while visual snapped to target; converged only once radial write stopped.)
                // Suppress the gun lock entirely while melee is held — it searches at 22–28m and would
                // pin the cursor to a distant target with no knife reach. Clear any stale lock on
                // the weapon-switch frame so the cursor isn't left pinned.
                bool meleeHeld = ch?.GetMeleeWeapon() != null;
                if (!meleeHeld)
                {
                    if (!AdsLock.Run(__instance, Cfg, pad, stick))
                        AutoAim.Run(__instance, Cfg.AutoAim,
                            idle ? Vector2.zero : raw,
                            idle ? Vector3.zero : stickWorldDir,
                            idle ? Vector2.zero : stick);
                }
                else
                {
                    AdsLock.Reset();
                    AutoAim.ClearLock();
                }

                // 3.5 AIM-1: layer the mod-owned recoil offset onto the base/lock cursor.
                // intendedMotion = player's aim intent this frame: base-delta in free-aim,
                // raw-stick px while locked (the lock pins the base, so base-delta ~ 0).
                bool locked = AdsLock.IsActive || AutoAim.IsLocked;
                Vector2 intendedMotion;
                if (RadialCursor.TryReadAim(im, out var curBase))
                {
                    if (locked)
                    {
                        intendedMotion = stick * Cfg.Aim.Sensitivity;   // stick intent in px
                    }
                    else
                    {
                        intendedMotion = _hasLastBase ? (curBase - _lastBasePx) : Vector2.zero;
                    }
                    _lastBasePx = curBase;
                    _hasLastBase = true;
                }
                else intendedMotion = Vector2.zero;

                RecoilAssist.Apply(im, intendedMotion);

                // 4. Recompute gun aim + warp OS cursor from final AimMousePosition.
                // Pass zero whenever an absolute base was written (placed) or recoil is actively
                // warping the cursor, so the game's delta path doesn't fight the absolute write.
                // Gate on a live offset (not just Enabled) so the off-screen velocity fallback
                // (else-branch) still fires when no shot has been fired and the player projection
                // fails — e.g. behind-camera edge case. (AIM-1 M1)
                bool recoilOwns = RecoilAssist.Enabled && RecoilAssist.HasOffset;
                if (idle || placed || recoilOwns)
                {
                    im.SetAimInputUsingMouse(Vector2.zero);
                }
                else
                {
                    var sens = Cfg.Aim.Sensitivity;
                    if (pad.leftTrigger.isPressed) sens *= Cfg.Aim.AdsSensitivityMultiplier;
                    im.SetAimInputUsingMouse(stick * sens);
                }
            }
            catch (System.Exception e)
            {
                // Rate-limit to 1 warning per ~600 frames so a persistent failure surfaces without spamming Player.log.
                _suppressedExceptionCount++;
                var frame = Time.frameCount;
                if (frame - _logSuppressFrame > 600)
                {
                    Log.Warn($"AimDriver postfix threw {_suppressedExceptionCount}x: {e.Message}");
                    _logSuppressFrame = frame;
                    _suppressedExceptionCount = 0;
                }
            }
        }
    }
}
