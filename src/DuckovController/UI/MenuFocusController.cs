using System.Collections.Generic;
using System.Reflection;
using DuckovController.Config;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DuckovController.UI
{
    // Gamepad nav for non-gameplay menus (Title, main menu, PauseMenu, OptionsPanel).
    // 1. Title: synthesize pointer click (Title is IPointerClickHandler only, no Selectable).
    // 2. Other menus: auto-select first interactable Selectable so the game's UI input module can step focus.
    internal sealed partial class MenuFocusController : MonoBehaviour
    {
        internal ControllerConfig? Cfg;

        private GameObject? _lastEnforcedSelection;
        private string? _lastSceneName;

        // Throttle expensive Selectable.allSelectablesArray scan to ~6 Hz.
        private float _lastSelectionScanTime;
        private const float SelectionScanIntervalSec = 0.16f;

        private float _lastNavTime;
        private float _navHoldStarted; // hold-start for one-time repeat delay (vs _lastNavTime = last fire)
        private Vector2 _lastNavAxis;
        private int _navHoldStepCount; // hold-repeat acceleration (parity with GridFocusController)

        // Idle-skip: run selection sweep + polls only on pad-activity frames or 10 Hz heartbeat (~7ms/frame saved).
        private float _lastMenuWork = -10f;
        private const float MenuWorkIntervalSec = 0.1f;

        // isPressed (not wasPressedThisFrame): keeps work running during held nav-repeat. 0.5-mag stick gate ignores drift.
        private static bool AnyPadActivity()
        {
            var pad = Gamepad.current;
            if (pad == null) return false;
            return pad.buttonSouth.isPressed || pad.buttonEast.isPressed
                || pad.buttonNorth.isPressed || pad.buttonWest.isPressed
                || pad.startButton.isPressed || pad.selectButton.isPressed
                || pad.leftShoulder.isPressed || pad.rightShoulder.isPressed
                || pad.dpad.up.isPressed || pad.dpad.down.isPressed
                || pad.dpad.left.isPressed || pad.dpad.right.isPressed
                || pad.leftStick.ReadValue().sqrMagnitude > 0.25f
                || pad.rightStick.ReadValue().sqrMagnitude > 0.25f;
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _lastEnforcedSelection = null;
            _lastSceneName = scene.name;
        }

        private void Update()
        {
            if (!DuckovController.Diagnostics.PerfFlags.MenuController) return;
            if (Cfg == null) return;
            // Active gameplay: GridFocusController/AimDriverPatch territory.
            if (Duckov.UI.View.ActiveView == null && InGameplay()) return;

            // Title/scene-loader advance must run during loading (no menu yet for MenuFocusOverlay).
            TryAdvanceTitleSplash();
            TryAdvanceSceneLoader();

            // MenuFocusOverlay owns chevron + nav when active; running in parallel causes desync.
            if (DuckovController.UI.Menu.MenuFocusOverlay.Instance?.IsActive == true) return;

            if (!AnyPadActivity() && Time.unscaledTime - _lastMenuWork < MenuWorkIntervalSec) return;
            _lastMenuWork = Time.unscaledTime;

            TryEnsureSelection();
            PollNav();
            PollConfirm();
            PollTabSwitch();
            PollKbmReturn();
        }

        // RB/LB tab cycle for ISingleSelectionMenu<T> panels. Gated off only in pure gameplay (RB=sprint).
        private void PollTabSwitch()
        {
            var pad = Gamepad.current;
            if (pad == null) return;
            int dir = 0;
            if (pad.rightShoulder.wasPressedThisFrame) dir = +1;
            else if (pad.leftShoulder.wasPressedThisFrame) dir = -1;
            if (dir == 0) return;

            // Pure-gameplay (no panel / no view) keeps RB = sprint, LB free.
            bool anyUiOpen = Duckov.UI.View.ActiveView != null
                             || (PauseMenu.Instance != null && PauseMenu.Instance.Shown)
                             || !InGameplay();
            if (!anyUiOpen) return;

            // Prefer the active View's hierarchy when there is one; otherwise
            // scan every loaded scene's root so we still find PauseMenu's
            // OptionsPanel (UIPanels aren't Views and so View.ActiveView is null).
            bool cycled = false;
            string viewName = Duckov.UI.View.ActiveView?.GetType().Name ?? "null";
            if (Duckov.UI.View.ActiveView != null)
            {
                cycled = TabSwitcher.TryCycle(Duckov.UI.View.ActiveView.gameObject, dir);
            }
            Log.Debug_($"PollTabSwitch: dir={dir} view={viewName} cycled-by-view={cycled}");
            if (!cycled) cycled = TabSwitcher.TryCycleSceneWide(dir);
            Log.Debug_($"PollTabSwitch: dir={dir} view={viewName} cycled-final={cycled}");
            if (cycled)
            {
                DuckovController.Haptics.HapticEngine.Instance?.Play(DuckovController.Haptics.HapticCue.PageTab);
                Log.Debug_("Haptic: PageTab (menu tab)");
            }
            if (!cycled)
            {
                // Diagnostic: enumerate what's actually in the scene so we can
                // adapt to whatever pattern this game's panel uses.
                TabSwitcher.LogAvailableMenus();
            }
        }

        // Direct poll (runtime InputAction bindings unreliable). Fires both submitHandler and pointer-click chain:
        // main-menu custom IPointerClickHandler buttons (Mods, Settings, Credits) ignore submitHandler.
        private void PollConfirm()
        {
            if (Cfg == null) return;
            if (!ShouldDriveMenuNav()) return;
            var pad = Gamepad.current;
            if (pad == null) return;
            if (!pad.buttonSouth.wasPressedThisFrame) return;

            var es = EventSystem.current;
            if (es == null) return;
            var target = _lastEnforcedSelection != null && _lastEnforcedSelection.activeInHierarchy
                ? _lastEnforcedSelection
                : es.currentSelectedGameObject;
            if (target == null) return;

            var bed = new BaseEventData(es);
            ExecuteEvents.Execute(target, bed, ExecuteEvents.submitHandler);
            // Pointer-click chain for custom IPointerClickHandler buttons.
            PointerEventDispatcher.Click(target);
            DuckovController.Haptics.HapticEngine.Instance?.Play(DuckovController.Haptics.HapticCue.Confirm);
            Log.Debug_("Haptic: Confirm (menu)");
        }

        // On controller activity after mouse: re-fire pointerEnter on focused element to restore hover visual.
        private void PollKbmReturn()
        {
            var pad = Gamepad.current;
            if (pad == null) return;
            bool padTouched =
                pad.buttonSouth.wasPressedThisFrame ||
                pad.buttonEast.wasPressedThisFrame ||
                pad.buttonNorth.wasPressedThisFrame ||
                pad.buttonWest.wasPressedThisFrame ||
                pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame ||
                pad.dpad.left.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame ||
                pad.leftStick.ReadValue().magnitude > 0.6f ||
                pad.rightStick.ReadValue().magnitude > 0.6f;
            if (!padTouched) return;

            var es = EventSystem.current;
            if (es == null) return;
            var current = es.currentSelectedGameObject;
            if (current != null && current.activeInHierarchy && current != _lastEnforcedSelection)
            {
                // Mouse moved focus elsewhere — accept it and refresh hover.
                PointerEventDispatcher.Hover(_lastEnforcedSelection, current);
                _lastEnforcedSelection = current;
            }
            else if (_lastEnforcedSelection != null && _lastEnforcedSelection.activeInHierarchy)
            {
                // Visual lost (mouse exit fired) — re-fire pointerEnter to redraw.
                PointerEventDispatcher.Hover(null, _lastEnforcedSelection);
                if (current != _lastEnforcedSelection)
                    es.SetSelectedGameObject(_lastEnforcedSelection);
            }
        }

        // Direct poll: runtime-added 2DVector composite bindings don't reliably fire OnNavigate.
        private void PollNav()
        {
            if (Cfg == null) return;
            if (!ShouldDriveMenuNav()) return;
            var pad = Gamepad.current;
            if (pad == null) return;

            Vector2 oneShot = Vector2.zero;
            if (pad.dpad.up.wasPressedThisFrame) oneShot.y += 1f;
            if (pad.dpad.down.wasPressedThisFrame) oneShot.y -= 1f;
            if (pad.dpad.left.wasPressedThisFrame) oneShot.x -= 1f;
            if (pad.dpad.right.wasPressedThisFrame) oneShot.x += 1f;
            if (oneShot != Vector2.zero)
            {
                _lastNavTime = Time.unscaledTime;
                _navHoldStarted = Time.unscaledTime;
                _lastNavAxis = oneShot;
                _navHoldStepCount = 0;
                StepFocus(oneShot);
                return;
            }

            Vector2 held = Vector2.zero;
            if (pad.dpad.up.isPressed) held.y += 1f;
            if (pad.dpad.down.isPressed) held.y -= 1f;
            if (pad.dpad.left.isPressed) held.x -= 1f;
            if (pad.dpad.right.isPressed) held.x += 1f;
            // Left stick fallback: snap to cardinal to avoid wobble.
            if (held == Vector2.zero)
            {
                var stick = pad.leftStick.ReadValue();
                if (stick.magnitude > 0.6f)
                {
                    if (Mathf.Abs(stick.x) > Mathf.Abs(stick.y))
                        held.x = stick.x > 0 ? 1f : -1f;
                    else
                        held.y = stick.y > 0 ? 1f : -1f;
                }
            }
            if (held == Vector2.zero)
            {
                _lastNavAxis = Vector2.zero;
                _navHoldStepCount = 0;
                return;
            }

            var now = Time.unscaledTime;
            bool sameAxis = Vector2.Dot(_lastNavAxis, held) > 0.7f;
            if (sameAxis)
            {
                // Pay initial delay once (from hold-start); then repeat at effRate (from last fire).
                if (now - _navHoldStarted < Cfg.Ui.NavRepeatDelaySec) return;
                // Shared NavAccel curve — parity with grid controller.
                float effRate = NavAccel.EffectiveRate(Cfg.Ui.NavRepeatRateSec, _navHoldStepCount);
                if (now - _lastNavTime < effRate) return;
            }
            else
            {
                _navHoldStepCount = 0;
                _navHoldStarted = now; // new direction → re-pay the initial delay
            }
            _lastNavTime = now;
            _lastNavAxis = held;
            _navHoldStepCount++;
            StepFocus(held);
        }

        // LevelManager.Instance full-scene scans when absent (main menu) cost ~7 ms/frame; 0.2s cache.
        private static bool _inGameplayCache;
        private static float _inGameplayCachedAt = -10f;
        private static bool InGameplay()
        {
            if (Time.unscaledTime - _inGameplayCachedAt < 0.2f) return _inGameplayCache;
            _inGameplayCachedAt = Time.unscaledTime;
            _inGameplayCache = ComputeInGameplay();
            return _inGameplayCache;
        }

        private static bool ComputeInGameplay()
        {
            // If LevelManager has a controlled character and the pause menu is
            // not shown, we're playing — menu nav isn't wanted.
            var lm = LevelManager.Instance;
            if (lm == null) return false;
            var character = lm.ControllingCharacter;
            if (character == null) return false;
            var pause = PauseMenu.Instance;
            if (pause != null && pause.Shown) return false;
            return true;
        }

        private static bool ShouldDriveMenuNav()
        {
            // Don't drive menu nav when in gameplay (the right-stick aim is
            // active and dpad belongs to item shortcuts) or when a View grid
            // is active (GridFocusController handles those).
            if (InGameplay()) return false;
            if (Duckov.UI.View.ActiveView != null) return false;
            return true;
        }
    }
}
