using System.Reflection;
using DuckovController.Config;
using Duckov.UI;
using HarmonyLib;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuckovController.Patches
{
    // Drives all gameplay verbs from the gamepad via InputManager calls (runtime-added bindings don't
    // reliably propagate in this game's PlayerInput config). Postfix on CharacterInputControl.Update so
    // KBM runs first; continuous inputs last-writer-win, one-shots fire only on fresh press.
    [HarmonyPatch(typeof(CharacterInputControl), "Update")]
    internal static class GameplayInputDriverPatch
    {
        internal static ControllerConfig? Cfg;

        // Long-press threshold: Y held at least this long fires ToggleInventory;
        // shorter press fires Interact only.
        private const float InventoryLongPressSeconds = 0.5f;

        // B held at least this long holsters the weapon (PutAway); a shorter tap keeps
        // its StopAction / throwable-cancel behaviour.
        private const float HolsterHoldSeconds = 0.35f;
        private static float _bPressStartTime = -1f;
        private static bool  _bHolsterFired;

        // Tracks the active shortcut slot for dpad weapon-cycling (1=Primary,
        // 2=Secondary, 3=Melee).
        private static int _shortcutCycleIndex = 1;

        // Cached MethodInfo for private CharacterInputControl.ShortCutInput.
        private static MethodInfo? _shortCutInputMethod;
        private static bool _shortCutInputResolved;

        [HarmonyPostfix]
        internal static void Postfix(CharacterInputControl __instance)
        {
            try
            {
                if (!DuckovController.Diagnostics.PerfFlags.GameplayInput) return;
                if (Cfg == null) return;
                if (!Application.isFocused) return;
                var pad = Gamepad.current;
                if (pad == null) return;
                if (Mouse.current == null) return;
                if (__instance == null || __instance.inputManager == null) return;

                var im = __instance.inputManager;
                if (im.ControllingCharacter == null) return;

                // Fishing mini-game owns input while it is the active gameplay
                // action: A/X hook the fish, anything else (except Start=pause)
                // cancels so combat is immediately available. Handled before
                // everything else so it overrides the system buttons (Y/Select)
                // and every drive below.
                if (FishingInputHandler.TryHandle(pad)) return;

                // Inventory/map toggles run even when a View is open so the
                // same button that opened it also closes it.
                DriveSystemButtons(pad, im);

                if (View.ActiveView != null) return;
                if (!InputManager.InputActived) return;

                DuckovController.Throwables.ThrowableController.Refresh(im);
                DriveMovement(pad, im);
                DriveCombat(pad, im);
                DriveOneShots(pad, im);
                DriveDpad(pad, im, __instance);
            }
            catch (System.Exception e)
            {
                Log.Debug_("GameplayInputDriver postfix threw: " + e.Message);
            }
        }

        private static bool _lastFrameStickAboveThreshold;

        private static void DriveMovement(Gamepad pad, InputManager im)
        {
            var stick = pad.leftStick.ReadValue();
            bool above = stick.magnitude > 0.18f;
            if (above)
            {
                im.SetMoveInput(stick);
            }
            else if (_lastFrameStickAboveThreshold)
            {
                // Release-edge: write zero once so the character stops; subsequent frames skip SetMoveInput so KBM works.
                im.SetMoveInput(Vector2.zero);
            }
            _lastFrameStickAboveThreshold = above;

            // Sprint: same release-edge pattern to avoid clobbering KBM Shift.
            DriveSprintEdge(pad, im);
        }

        private static bool _lastFrameSprintPressed;
        private static void DriveSprintEdge(Gamepad pad, InputManager im)
        {
            bool now = pad.rightShoulder.isPressed;
            if (now)
            {
                im.SetRunInput(true);
            }
            else if (_lastFrameSprintPressed)
            {
                im.SetRunInput(false);
            }
            _lastFrameSprintPressed = now;
        }

        private static bool _lastFrameAdsPressed;
        private static void DriveCombat(Gamepad pad, InputManager im)
        {
            // While a throwable is held, RT drives the throw lifecycle, not gun fire/ADS.
            // Keep _lastTriggerPressed fresh so no phantom gun press/release fires on return.
            if (DuckovController.Throwables.ThrowableController.IsHoldingThrowable)
            {
                _lastTriggerPressed = pad.rightTrigger.ReadValue() >= 0.25f;
                DuckovController.Throwables.ThrowableController.DriveTrigger(pad, im);
                return;
            }

            // ADS: edge semantics so KBM right-click isn't clobbered when LT is at rest.
            bool adsNow = pad.leftTrigger.isPressed;
            if (adsNow)
            {
                im.SetAdsInput(true);
            }
            else if (_lastFrameAdsPressed)
            {
                im.SetAdsInput(false);
            }
            _lastFrameAdsPressed = adsNow;

            // Fire: full trigger semantics so Attack() fires on press edge and skill-aim lifecycle is correct.
            var rt = pad.rightTrigger;
            // axisDeadzone-equivalent: treat as released below 0.25.
            bool pressed = rt.ReadValue() >= 0.25f;
            bool wasPressedNow = pressed && !_lastTriggerPressed;
            bool wasReleasedNow = !pressed && _lastTriggerPressed;
            var meleeHolder = im.ControllingCharacter;
            if (wasPressedNow && Cfg != null && meleeHolder != null && meleeHolder.GetMeleeWeapon() != null)
                DuckovController.Aim.MeleeAimAssist.OnAttackPressed(meleeHolder, Cfg.AutoAim);
            im.SetTrigger(pressed, wasPressedNow, wasReleasedNow);
            _lastTriggerPressed = pressed;
        }
        private static bool _lastTriggerPressed;

        private static void DriveOneShots(Gamepad pad, InputManager im)
        {
            // A = Dash, X = Reload, B = StopAction (cancel heal/skill/animation).
            // Y is handled in DriveSystemButtons (Interact-then-Inventory)
            // so it can run regardless of whether a view is open.
            if (pad.buttonSouth.wasPressedThisFrame) im.Dash();
            if (pad.buttonWest.wasPressedThisFrame)
                CharacterMainControl.Main?.TryToReload((Item?)null);

            // B-HOLD → holster the held weapon (PutAway). Holstering used to be a side
            // effect of cycling the dpad onto an empty weapon slot; that's now skipped
            // (PT-4), so an explicit hold is the deliberate "put weapon away". The short
            // tap below keeps its StopAction / throwable-cancel behaviour. Suppressed
            // while a throwable is held/aimed (B already means cancel there).
            if (pad.buttonEast.wasPressedThisFrame) { _bPressStartTime = Time.unscaledTime; _bHolsterFired = false; }
            if (pad.buttonEast.isPressed
                && !_bHolsterFired
                && _bPressStartTime >= 0f
                && (Time.unscaledTime - _bPressStartTime) >= HolsterHoldSeconds
                && !DuckovController.Throwables.ThrowableController.IsHoldingThrowable
                && !DuckovController.Throwables.ThrowableController.IsAiming)
            {
                _bHolsterFired = true;
                im.PutAway();
            }
            if (pad.buttonEast.wasReleasedThisFrame) _bPressStartTime = -1f;

            // B: throwable cancel/holster takes priority; else fall through to the general stop paths.
            if (pad.buttonEast.wasPressedThisFrame)
            {
                if (DuckovController.Throwables.ThrowableController.IsHoldingThrowable
                    || DuckovController.Throwables.ThrowableController.IsAiming)
                {
                    DuckovController.Throwables.ThrowableController.HandleCancelOrHolster(im);
                    return;   // don't also run the generic StopAction path
                }

                var character = CharacterMainControl.Main;
                if (character != null)
                {
                    // Path 1: useItemAction (CA_UseItem) — proven working in SmartHealController.TryStopCurrentUseAction.
                    var useAction = character.useItemAction;
                    bool useRunning   = useAction != null && useAction.Running;
                    bool useStopped   = false;
                    if (useRunning)
                    {
                        try { useStopped = useAction!.StopAction(); } catch { }
                    }

                    // Path 2: CurrentAction (CA_Skill, CA_Interact, CA_Carry, fishing, etc.).
                    var curAction = character.CurrentAction;
                    bool curRunning  = curAction != null && curAction.Running;
                    bool curStopable = curRunning && curAction!.IsStopable();
                    bool curStopped  = false;
                    if (curRunning && curStopable)
                    {
                        try { curStopped = curAction!.StopAction(); } catch { }
                    }

                    // Path 3: im.StopAction() fallback (guards on InputActived + IsStopable; may no-op).
                    im.StopAction();

                    if (Cfg?.Diagnostics.DebugLog == true)
                    {
                        string useType  = useAction  != null ? useAction.GetType().Name  : "null";
                        string curType  = curAction   != null ? curAction.GetType().Name  : "null";
                        Log.Debug_($"[B-cancel] useAction={useType} running={useRunning} stopped={useStopped}"
                            + $" | curAction={curType} running={curRunning} stopable={curStopable} stopped={curStopped}"
                            + $" | InputActived={InputManager.InputActived}");
                    }
                }
                else
                {
                    im.StopAction();
                }
            }

            // L3 / R3 (stick clicks).
            if (pad.leftStickButton.wasPressedThisFrame && !GameManager.Paused)
                im.ToggleNightVision();
            if (pad.rightStickButton.wasPressedThisFrame && !GameManager.Paused)
                im.ToggleView();
        }

        private static void DriveDpad(Gamepad pad, InputManager im, CharacterInputControl ctl)
        {
            // RB held + dpad acts as a 4-slot shortcut keypad (slots 3-6).
            if (pad.rightShoulder.isPressed)
            {
                if (pad.dpad.up.wasPressedThisFrame) ShortcutInput(ctl, 3);
                else if (pad.dpad.right.wasPressedThisFrame) ShortcutInput(ctl, 4);
                else if (pad.dpad.down.wasPressedThisFrame) ShortcutInput(ctl, 5);
                else if (pad.dpad.left.wasPressedThisFrame) ShortcutInput(ctl, 6);
                return;
            }
            // No modifier: dpad ↔ cycles weapon agent slots; dpad ↕ switches weapon/bullet/interact per ScrollWheelBehaviour.
            if (pad.dpad.left.wasPressedThisFrame)
            {
                int slot = NextOccupiedWeaponSlot(im, -1);
                if (slot > 0) { _shortcutCycleIndex = slot; im.SwitchItemAgent(slot); }
            }
            else if (pad.dpad.right.wasPressedThisFrame)
            {
                int slot = NextOccupiedWeaponSlot(im, +1);
                if (slot > 0) { _shortcutCycleIndex = slot; im.SwitchItemAgent(slot); }
            }

            if (pad.dpad.up.wasPressedThisFrame)
            {
                if ((int)ScrollWheelBehaviour.CurrentBehaviour == 0)
                {
                    im.SetSwitchInteractInput(1);
                    // Ammo cycle: UP-only (down reserved for throwables); suppressed near actionables to avoid misfires.
                    if (!NearActionable()) CycleBulletType(1);
                }
                else
                {
                    im.SetSwitchWeaponInput(1);
                }
            }
            else if (pad.dpad.down.wasPressedThisFrame)
            {
                // Throwables own dpad-down when the player has any — but yield near a
                // trader/actionable so dpad-down drives the interact menu (mirrors the
                // dpad-up ammo-cycle suppression). Otherwise the throwable cycle hijacked
                // the menu's "previous option" near interactables (PT-3).
                if (!NearActionable() && DuckovController.Throwables.ThrowableController.CycleNext(im))
                    return;

                if ((int)ScrollWheelBehaviour.CurrentBehaviour == 0)
                {
                    im.SetSwitchInteractInput(-1);
                }
                else
                {
                    im.SetSwitchWeaponInput(-1);
                }
            }
        }

        // True when an interactable (trader/item/door prompt) is in range. Used
        // to suppress the dpad-up ammo cycle so it doesn't collide with the
        // game's interact-selection cycling near actionables.
        private static bool NearActionable()
        {
            try
            {
                var main = CharacterMainControl.Main;
                return main != null && main.interactAction != null
                    && main.interactAction.InteractTarget != null;
            }
            catch { return false; }
        }

        // Next weapon-slot index (1=Primary, 2=Secondary, 3=Melee) in `dir` from the
        // current cycle index that actually HOLDS a weapon. Empty slots are skipped:
        // switching to one just calls ChangeHoldItem(null) (i.e. holster), which is
        // now the explicit B-hold action, not a cycle stop (PT-4). Returns -1 when no
        // other occupied slot exists (cycle is a no-op).
        private static int NextOccupiedWeaponSlot(InputManager im, int dir)
        {
            var c = im.ControllingCharacter;
            if (c == null) return -1;
            for (int step = 1; step <= 3; step++)
            {
                int idx = (((_shortcutCycleIndex - 1 + dir * step) % 3) + 3) % 3 + 1;
                if (WeaponSlotOccupied(c, idx)) return idx;
            }
            return -1;
        }

        private static bool WeaponSlotOccupied(CharacterMainControl c, int idx)
        {
            try
            {
                var slot = idx switch
                {
                    1 => c.PrimWeaponSlot(),
                    2 => c.SecWeaponSlot(),
                    3 => c.MeleeWeaponSlot(),
                    _ => null
                };
                return slot != null && slot.Content != null;
            }
            catch { return false; }
        }

        private static void CycleBulletType(int dir)
        {
            try
            {
                var main = CharacterMainControl.Main;
                if (main == null) return;
                var gun = main.GetGun();
                if (gun == null) return;
                var gunSetting = gun.GunItemSetting;
                if (gunSetting == null) return;
                var inv = main.CharacterItem?.Inventory;
                if (inv == null) return;
                var types = gunSetting.GetBulletTypesInInventory(inv);
                if (types == null || types.Count == 0) return;
                var ids = new System.Collections.Generic.List<int>(types.Keys);
                int idx = ids.IndexOf(gunSetting.TargetBulletID);
                if (idx < 0) idx = 0;
                int next = ids[((idx + dir) % ids.Count + ids.Count) % ids.Count];
                if (next == gunSetting.TargetBulletID) return;
                gunSetting.SetTargetBulletType(next);
                // Reload into new type (swaps chambered rounds; also allows re-cycle mid-reload).
                main.TryToReload((Item?)null);
                // BulletCountHUD refreshes TOTAL only on inventory/gun-change, not on bullet-type change.
                // Force ChangeTotalCount() so the count reflects the new ammo type immediately.
                RefreshBulletCountHud();
            }
            catch (System.Exception e) { Log.Debug_($"CycleBulletType: {e.Message}"); }
        }

        // Cached BulletCountHUD + private ChangeTotalCount(); re-find on null (scene reload).
        private static BulletCountHUD? _bulletCountHud;
        private static MethodInfo? _changeTotalCountMethod;
        private static void RefreshBulletCountHud()
        {
            try
            {
                if (_bulletCountHud == null)
                    _bulletCountHud = UnityEngine.Object.FindObjectOfType<BulletCountHUD>();
                if (_bulletCountHud == null) return;
                if (_changeTotalCountMethod == null)
                    _changeTotalCountMethod = typeof(BulletCountHUD).GetMethod(
                        "ChangeTotalCount", BindingFlags.Instance | BindingFlags.NonPublic);
                _changeTotalCountMethod?.Invoke(_bulletCountHud, null);
            }
            catch (System.Exception e) { Log.Debug_($"RefreshBulletCountHud: {e.Message}"); }
        }

        // Y long-press: _yPressStartTime set on press-edge; _yInventoryFired prevents a second fire on release.
        private static float _yPressStartTime = -1f;
        private static bool  _yInventoryFired;

        private static void DriveSystemButtons(Gamepad pad, InputManager im)
        {
            // Start = Pause menu (exit, settings, etc.).
            if (pad.startButton.wasPressedThisFrame) PauseMenu.Toggle();
            // Select = Map toggle.
            if (pad.selectButton.wasPressedThisFrame) ToggleMap();

            // Y: long-press → ToggleInventory; short-tap → Interact only.
            if (pad.buttonNorth.wasPressedThisFrame)
            {
                _yPressStartTime  = Time.unscaledTime;
                _yInventoryFired  = false;
            }

            // Held past threshold: fire ToggleInventory once.
            if (pad.buttonNorth.isPressed
                && !_yInventoryFired
                && _yPressStartTime >= 0f
                && (Time.unscaledTime - _yPressStartTime) >= InventoryLongPressSeconds)
            {
                _yInventoryFired = true;
                // Router owns Y while a supported view is active; skip inventory (still holding).
                bool routerOwnsY = View.ActiveView != null
                    && DuckovController.UI.GridFocusController.Instance != null;
                if (!routerOwnsY)
                    ToggleInventory();
            }

            // Release-edge: short tap → Interact only; long press already handled.
            if (pad.buttonNorth.wasReleasedThisFrame && _yPressStartTime >= 0f)
            {
                bool wasShortTap = !_yInventoryFired;
                _yPressStartTime = -1f;

                if (wasShortTap
                    && !GameManager.Paused
                    && !Dialogues.DialogueUI.Active
                    && !SceneLoader.IsSceneLoading)
                {
                    // Router owns Y while a supported view is open; skip entirely.
                    bool routerOwnsY = View.ActiveView != null
                        && DuckovController.UI.GridFocusController.Instance != null;
                    if (!routerOwnsY)
                        im.Interact();
                }
            }
        }

        private static void ShortcutInput(CharacterInputControl ctl, int index)
        {
            if (!_shortCutInputResolved)
            {
                _shortCutInputResolved = true;
                _shortCutInputMethod = typeof(CharacterInputControl).GetMethod(
                    "ShortCutInput", BindingFlags.Instance | BindingFlags.NonPublic);
            }
            try { _shortCutInputMethod?.Invoke(ctl, new object[] { index }); }
            catch (System.Exception e) { Log.Debug_($"ShortCutInput({index}) threw: {e.Message}"); }
        }

        private static void ToggleInventory()
        {
            if (GameManager.Paused) return;
            if (Dialogues.DialogueUI.Active) return;
            if (SceneLoader.IsSceneLoading) return;
            var active = View.ActiveView;
            if (active != null)
            {
                ((ManagedUIElement)active).Close();
                return;
            }
            if (LevelManager.Instance == null) return;
            if (LevelManager.Instance.IsBaseLevel)
            {
                var storage = PlayerStorage.Instance;
                if (storage != null && storage.InteractableLootBox != null)
                    ((InteractableBase)storage.InteractableLootBox).InteractWithMainCharacter();
            }
            else
            {
                InventoryView.Show();
            }
        }

        private static void ToggleMap()
        {
            if (GameManager.Paused) return;
            if (SceneLoader.IsSceneLoading) return;
            var active = View.ActiveView;
            if (active is Duckov.MiniMaps.UI.MiniMapView mini)
            {
                mini.Close();
                return;
            }
            if (active != null) return;
            Duckov.MiniMaps.UI.MiniMapView.Show();
        }
    }
}
