using DuckovController.Aim;
using DuckovController.Config;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuckovController.Throwables
{
    // Owns all throwable state + lifecycle. Both Update-postfix patches delegate here.
    internal static class ThrowableController
    {
        internal static ControllerConfig? Cfg;

        // True while the held item is a skill/throwable item (agentHolder.Skill != null).
        internal static bool IsHoldingThrowable { get; private set; }
        // True while RT-aim is engaged.
        internal static bool IsAiming { get; private set; }

        // --- Trigger lifecycle ---
        private static bool _lastRt;
        private static float _aimElapsed;    // seconds since StartItemSkillAim
        private static float _readyTime;     // skillReadyTime + epsilon
        private static bool _pendingThrow;   // RT released before wind-up done

        private static string _lastState = "";

        internal static void Reset()
        {
            IsHoldingThrowable = false;
            IsAiming = false;
            _lastRt = false;
            _aimElapsed = 0f;
            _readyTime = 0f;
            _pendingThrow = false;
            _lastState = "";
        }

        // Change-deduped state tracer (no per-frame spam).
        private static void Trace(string state)
        {
            if (state == _lastState) return;
            _lastState = state;
            if (Cfg?.Diagnostics.DebugLog == true) Log.Debug_($"[throw] {state}");
        }

        // Refresh the held-throwable flag from the live held item. Call once per gameplay frame.
        internal static void Refresh(InputManager im)
        {
            // Master disable: keep flags false so DriveCombat gate falls through to normal gun handling.
            if (Cfg?.Throw.Enabled != true || !DuckovController.Diagnostics.PerfFlags.Throwables)
            {
                IsHoldingThrowable = false;
                IsAiming = false;
                return;
            }
            var ch = im?.ControllingCharacter;
            IsHoldingThrowable = ch != null && ch.agentHolder != null && ch.agentHolder.Skill != null;
            if (!IsHoldingThrowable && IsAiming) IsAiming = false; // safety: lost the item mid-aim
            Trace(IsAiming ? "aiming" : (IsHoldingThrowable ? "equipped" : "idle"));
        }

        // dpad-down: equip first throwable, or cycle to the next. Returns true if handled
        // (i.e. the player owns at least one quick-slot throwable).
        internal static bool CycleNext(InputManager im)
        {
            if (Cfg?.Throw.Enabled != true || !DuckovController.Diagnostics.PerfFlags.Throwables) return false;
            if (im == null) return false;
            var ch = im.ControllingCharacter;
            if (ch == null) return false;

            var list = ThrowableInventory.Enumerate();
            if (list.Count == 0) return false;

            Item? heldItem = ch.CurrentHoldItemAgent?.Item;
            bool holdingThrowable = ch.agentHolder?.Skill != null;

            Item? next = holdingThrowable
                ? ThrowableInventory.NextAfter(heldItem, list)
                : list[0];
            if (next == null) return false;

            if (!holdingThrowable)
            {
                ch.StoreHoldWeaponBeforeUse();   // remember the gun before the first equip
                // Seed _lastRt to current RT state so the first DriveTrigger call can't fire a
                // phantom pressEdge if RT is already held (e.g. equipping mid-gunfire via dpad↓).
                // A genuine release→press is required to start aiming.
                _lastRt = Gamepad.current?.rightTrigger.ReadValue() >= 0.25f;
            }

            ch.ChangeHoldItem(next);             // no-op if already holding `next`
            im.SetAdsInput(false);               // clear any lingering gun ADS state
            Trace("equipped");
            return true;
        }

        // Return to the weapon held before we first equipped a throwable.
        internal static void ReturnToWeapon(CharacterMainControl ch)
        {
            if (ch == null) return;
            ch.SwitchToWeaponBeforeUse();
            if (ch.CurrentHoldItemAgent == null || ch.agentHolder?.Skill != null)
                ch.SwitchToFirstAvailableWeapon();   // fallback: nothing was stored
            IsAiming = false;
            Trace("returned");
        }

        // --- Trigger lifecycle (Batch 2) ---

        private static float CastRange(CharacterMainControl ch)
        {
            var skill = ch.agentHolder?.Skill?.Skill;   // ItemSetting_Skill.Skill (SkillBase)
            return skill != null ? skill.SkillContext.castRange : 0f;
        }

        // RT press = StartItemSkillAim; RT release = throw (buffered if early). Called from
        // GameplayInputDriverPatch.DriveCombat whenever a throwable is held.
        internal static void DriveTrigger(Gamepad pad, InputManager im)
        {
            if (im == null) return;
            var ch = im.ControllingCharacter;
            if (ch == null) return;

            bool pressed = pad.rightTrigger.ReadValue() >= 0.25f;
            bool pressEdge = pressed && !_lastRt;
            bool releaseEdge = !pressed && _lastRt;
            _lastRt = pressed;

            if (pressEdge && !IsAiming)
            {
                im.SetAimType(AimTypes.handheldSkill);
                im.StartItemSkillAim();
                var skill = ch.skillAction?.CurrentRunningSkill;
                if (skill == null)
                {
                    // releaseOnStartAim (instant) already threw, or the start failed.
                    ReturnToWeapon(ch);
                    return;
                }
                IsAiming = true;
                _aimElapsed = 0f;
                _readyTime = skill.SkillContext.skillReadyTime + (Cfg?.Throw.ReadyEpsilonSeconds ?? 0.05f);
                _pendingThrow = false;
                ThrowCursor.Seed(im, ch, ThrowCamera.Get(im), CastRange(ch), Cfg!.Throw);
                Trace("aiming");
            }
            else if (releaseEdge && IsAiming)
            {
                if (_aimElapsed >= _readyTime || Cfg?.Throw.BufferEarlyRelease != true)
                    ThrowNow(im, ch);
                else
                    _pendingThrow = true;   // buffer; resolved in Tick()
            }
        }

        // Per-frame cursor while aiming: ticks the wind-up timer, then drives the reticle
        // (LT lock in Batch 4, else RS free-pan). Called from AimDriverPatch.
        internal static void DriveCursor(CharacterInputControl ctl, ControllerConfig cfg, Gamepad pad)
        {
            if (ctl == null) return;
            var im = ctl.inputManager;
            if (im == null) return;
            var ch = im.ControllingCharacter;
            if (ch == null) return;

            // Tick must run once per aim frame; it lives here (not in DriveTrigger) so it always
            // fires while IsAiming, regardless of whether RT is held or released.
            Tick(im, Time.deltaTime);
            if (!IsAiming) return;                          // a buffered throw may have just fired

            var cam = ThrowCamera.Get(im);
            float castRange = CastRange(ch);
            if (castRange <= 0f) return;

            // LT aim-assist: snap landing point onto nearest enemy, clamped to castRange.
            // Falls through to free-pan if LT is released or no target in cone.
            if (cfg.Throw.AimAssistEnabled
                && cfg.Aim.BaselineAssistEnabled
                && pad.leftTrigger.isPressed
                && ThrowAimAssist.TryGetLockScreenPoint(ctl, cfg, cam, castRange, out var lockScreen))
            {
                ThrowCursor.WriteScreen(im, lockScreen);
                return;
            }

            var raw = pad.rightStick.ReadValue();
            var stick = RadialDeadzone.Apply(raw, cfg.Aim.DeadzoneInner, cfg.Aim.DeadzoneOuter);
            ThrowCursor.DriveFreePan(im, ch, cam, stick, castRange, cfg.Throw);
        }

        // Per-frame while aiming: accumulate the wind-up timer, resolve a buffered throw, and
        // detect an externally-cancelled skill. Called from DriveCursor.
        internal static void Tick(InputManager im, float deltaTime)
        {
            if (!IsAiming) return;
            if (im == null) { IsAiming = false; return; }
            var ch = im.ControllingCharacter;
            if (ch == null) { IsAiming = false; return; }

            if (ch.skillAction?.CurrentRunningSkill == null && !_pendingThrow)
            {
                IsAiming = false;           // game cancelled the aim from under us
                Trace("equipped");
                return;
            }

            _aimElapsed += deltaTime;
            if (_pendingThrow && _aimElapsed >= _readyTime)
                ThrowNow(im, ch);
        }

        private static void ThrowNow(InputManager im, CharacterMainControl ch)
        {
            im.ReleaseItemSkill();
            _pendingThrow = false;
            IsAiming = false;
            Trace("threw");
            ReturnToWeapon(ch);
        }

        // B button: always returns to the weapon (cancels aim first if needed).
        internal static void HandleCancelOrHolster(InputManager im)
        {
            if (im == null) return;
            var ch = im.ControllingCharacter;
            if (ch == null) return;
            if (IsAiming)
            {
                im.CancleSkill();
                _pendingThrow = false;
            }
            ReturnToWeapon(ch);   // covers both aiming and idle-holding cases
        }

        // View-open path: abort an in-progress aim WITHOUT returning to the weapon.
        // Keeps the grenade equipped so the player can re-aim immediately after closing.
        internal static void CancelAim(InputManager im)
        {
            if (!IsAiming) return;
            im?.CancleSkill();
            IsAiming = false;
            _pendingThrow = false;
            Trace("cancelled");
        }
    }
}
