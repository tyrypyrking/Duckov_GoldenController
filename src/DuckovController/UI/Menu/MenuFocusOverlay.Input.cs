using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.UI.Animations;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DuckovController.UI.Menu
{
    // Per-button verb handlers: nav, slider, cancel, submit, hold-release,
    // credits scroll, difficulty nav, back-button finders.
    // Modal intercept (confirm dialog / mod-warning / closure) lives in MenuFocusOverlay.Modals.cs.
    internal sealed partial class MenuFocusOverlay : MonoBehaviour
    {
        private void HandleNav(Gamepad pad)
        {
            if (_focused == null) return;

            // Sample the left stick as a virtual d-pad ONCE per frame, before any sub-handler reads
            // direction. All overlay nav (this method + the Handle* helpers below) then treats stick
            // and d-pad identically via DirEdge/DirHeld.
            _menuStick.Sample(pad.leftStick.ReadValue(), enabled: true);

            // Credits: only Return is interactable — route dpad/stick to ScrollRect.
            if (HandleCreditsScroll(pad)) return;

            // DifficultySelection: L/R cycles cards; D drops to Confirm; U from Confirm restores card.
            if (HandleDifficultyNav(pad)) return;

            // CharacterCreator: LB/RB cycle top items; dpad navigates swatches on a tab.
            if (HandleCharacterCreatorNav(pad)) return;

            // Slider: dpad L/R adjusts ± step; hold-to-repeat; consumes so U/D nav doesn't fire.
            if (HandleSliderHorizontal(pad)) return;

            // ModManagerUI: shoulders/triggers = OrderUp/OrderDown; dpad U/D = row nav.
            if (HandleModReorder(pad)) return;

            bool upEdge    = DirEdge(pad, NavDir.Up);
            bool downEdge  = DirEdge(pad, NavDir.Down);
            bool upHeld    = DirHeld(pad, NavDir.Up);
            bool downHeld  = DirHeld(pad, NavDir.Down);

            int dir = 0;
            if (upEdge)        { dir = -1; ResetHold(); }
            else if (downEdge) { dir = +1; ResetHold(); }
            else if (upHeld || downHeld)
            {
                float held = Time.unscaledTime - _navHoldStarted;
                // Shared NavAccel curve: long lists (options, mod manager) don't crawl on hold.
                float rate = NavAccel.EffectiveRate(RepeatRate, _navHoldSteps);
                if (held >= RepeatDelay && (Time.unscaledTime - _lastNavAt) >= rate)
                {
                    dir = upHeld ? -1 : +1;
                    _lastNavAt = Time.unscaledTime;
                    _navHoldSteps++;
                }
            }
            else
            {
                _navHoldStarted = Time.unscaledTime;
                _navHoldSteps = 0;
            }

            if (dir == 0) return;
            var previous = _focused;

            var column = BuildVerticalColumn();
            if (column.Count == 0) return;
            int idx = column.FindIndex(b => ReferenceEquals(b, _focused));

            var sb = new System.Text.StringBuilder();
            sb.Append("MenuOverlay nav dir=").Append(dir).Append(" cur=")
              .Append(_focused != null ? _focused.gameObject.name : "<null>")
              .Append(" idx=").Append(idx).Append(" column=[");
            for (int i = 0; i < column.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(i).Append(":").Append(column[i].gameObject.name)
                  .Append("@y=").Append(column[i].transform.position.y.ToString("F1"));
            }
            sb.Append("]");
            Log.Info(sb.ToString());

            if (idx < 0)
            {
                idx = dir > 0 ? -1 : column.Count;
            }
            int nextIdx = Mathf.Clamp(idx + dir, 0, column.Count - 1);
            if (nextIdx == idx) return;
            _focused = column[nextIdx];
            Log.Info($"MenuOverlay nav → idx={nextIdx} name={_focused.gameObject.name}");
            DuckovController.Haptics.HapticEngine.Instance?.Play(DuckovController.Haptics.HapticCue.FocusTick);
            Log.Debug_("Haptic: FocusTick (menu-overlay)");
            ClearVanillaHoverAndSelection(previous);
        }

        // True if focused is a Slider and L/R dpad was consumed.
        // ControllerSliderInputMarker.Step overrides default (1 for wholeNumbers, 1% of range).
        private bool HandleSliderHorizontal(Gamepad pad)
        {
            if (_focused == null) return false;
            var slider = _focused as Slider;
            if (slider == null) return false;
            var stepMarker = slider.GetComponent<DuckovController.UI.Settings.ControllerSliderInputMarker>();
            float stepSize = stepMarker != null
                ? stepMarker.Step
                : (slider.wholeNumbers
                    ? 1f
                    : Mathf.Max(0.01f, (slider.maxValue - slider.minValue) * 0.01f));

            bool leftEdge  = DirEdge(pad, NavDir.Left);
            bool rightEdge = DirEdge(pad, NavDir.Right);
            bool leftHeld  = DirHeld(pad, NavDir.Left);
            bool rightHeld = DirHeld(pad, NavDir.Right);

            int dir = 0;
            if (leftEdge)        { dir = -1; _hAxisHoldStarted = Time.unscaledTime; _lastHAxisAt = Time.unscaledTime; }
            else if (rightEdge)  { dir = +1; _hAxisHoldStarted = Time.unscaledTime; _lastHAxisAt = Time.unscaledTime; }
            else if (leftHeld || rightHeld)
            {
                float held = Time.unscaledTime - _hAxisHoldStarted;
                if (held >= RepeatDelay && (Time.unscaledTime - _lastHAxisAt) >= RepeatRate)
                {
                    dir = leftHeld ? -1 : +1;
                    _lastHAxisAt = Time.unscaledTime;
                }
            }
            else
            {
                _hAxisHoldStarted = Time.unscaledTime;
            }

            if (dir == 0) return false;

            float before = slider.value;
            float newVal = Mathf.Clamp(slider.value + dir * stepSize, slider.minValue, slider.maxValue);
            slider.value = newVal; // fires onValueChanged → writes config
            Log.Info($"MenuOverlay SliderHorizontal: {slider.gameObject.name} dir={dir} step={stepSize} {before} → {slider.value}");
            return true;
        }

        // B closes the current sub-panel: (1) click FadeGroupButton, (2) click back-named button,
        // (3) UIPanel.Close. Top-level skipped — vanilla StopAction on B handles pause-menu close.
        private void HandleCancel(Gamepad pad)
        {
            if (!pad.buttonEast.wasPressedThisFrame) return;

            // Color-picker sub-panel owns B: revert to the open-time color + close. Must run before
            // any other B handling so it does NOT bubble to the CC-level cancel that exits the creator.
            if (_ccOpenPicker != null)
            {
                Log.Info("MenuOverlay HandleCancel: color-picker B → revert + close");
                PickerCancel();
                return;
            }

            if (_menuRoot == null) return;
            string focusName = _focused != null ? _focused.gameObject.name : "<null>";
            string effName   = _effectiveRoot != null ? _effectiveRoot.gameObject.name : "<null>";
            Log.Info($"MenuOverlay HandleCancel: B pressed, focus={focusName} effective={effName}");

            // Top-level: let vanilla StopAction on B handle it.
            if (_effectiveRoot == null || ReferenceEquals(_effectiveRoot, _menuRoot))
            {
                Log.Info("MenuOverlay HandleCancel: top-level menu, deferring to vanilla cancel");
                return;
            }

            // FadeGroupButton preferred: Execute closes panel's FadeGroup AND reopens parent.
            // UIPanel.Close alone skips the openOnClick step that un-hides the main menu.
            // First close any expanded TMP_Dropdown (detected via reflection on m_Dropdown).
            if (TryCloseAnyExpandedDropdown(_menuRoot))
            {
                Log.Info("MenuOverlay HandleCancel: closed expanded TMP_Dropdown — B consumed.");
                return;
            }

            // OptionsPanel tab content: Return lives at OptionsPanel level, not inside the tab.
            Transform? searchRoot = _effectiveRoot;
            if (IsOptionsTabContent(searchRoot))
            {
                var p = searchRoot.parent; // Content
                while (p != null && p.name != "OptionsPanel") p = p.parent;
                if (p != null) searchRoot = p;
            }

            // ModManagerUI/Credits: Return is at panel root, not inside MainContent/effective root.
            var modAncestor = FindAncestorByName(searchRoot, "ModManagerUI");
            if (modAncestor != null) searchRoot = modAncestor;
            var creditsAncestor = FindAncestorByName(searchRoot, "Credits");
            if (creditsAncestor != null && creditsAncestor != _menuRoot)
                searchRoot = creditsAncestor;

            var fadeBtn = FindFadeGroupButtonIn(searchRoot);
            if (fadeBtn != null)
            {
                Log.Info($"MenuOverlay HandleCancel: clicking FadeGroupButton {fadeBtn.gameObject.name}");
                DuckovController.UI.PointerEventDispatcher.Click(fadeBtn.gameObject);
                _focused = null;
                _justActivated = true;
                return;
            }

            // Fallback: named back button — pointer-click (IPointerClickHandler) + submit (Button.onClick).
            var backBtn = FindBackButtonIn(searchRoot);
            if (backBtn != null)
            {
                Log.Info($"MenuOverlay HandleCancel: clicking back-named button {backBtn.gameObject.name}");
                var go = backBtn.gameObject;
                DuckovController.UI.PointerEventDispatcher.Click(go);
                var es = EventSystem.current;
                if (es != null)
                    ExecuteEvents.Execute(go, new BaseEventData(es), ExecuteEvents.submitHandler);
                _focused = null;
                _justActivated = true;
                return;
            }

            // DifficultySelection has no back button; B → LoadScene(MainMenu).
            // Only when difficulty is active root (not CustomDifficultyPanel — its FadeGroupButton closes it).
            if (IsInsideDifficultySelection())
            {
                try
                {
                    Log.Info("MenuOverlay HandleCancel: difficulty scope → SceneManager.LoadScene(MainMenu)");
                    UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
                    _focused = null;
                    _justActivated = true;
                    return;
                }
                catch (Exception e) { Log.Warn($"MenuOverlay LoadScene(MainMenu) failed: {e.Message}"); }
            }

            // Last resort: UIPanel.Close — calls parent?.NotifyChildClosed which re-shows the pause menu.
            // Safe whether or not the panel hid its parent (e.g. OptionsPanel opened from pause menu).
            UIPanel? panel = FindEnclosingPanel(_effectiveRoot, _menuRoot);
            if (panel != null)
            {
                try
                {
                    Log.Info($"MenuOverlay HandleCancel: closing enclosing UIPanel {panel.gameObject.name}");
                    panel.Close();
                    _focused = null;
                    _justActivated = true;
                    return;
                }
                catch (Exception e) { Log.Warn($"MenuOverlay UIPanel.Close failed: {e.Message}"); }
            }

            Log.Info("MenuOverlay HandleCancel: sub-panel present but no close button found — no-op");
        }

        private void HandleSubmit(Gamepad pad)
        {
            if (_focused == null) return;
            if (HandleCharacterCreatorSubmit(pad)) return;
            var go = _focused.gameObject;

            // Hold-to-confirm (DeleteData): fire pointerDown on A press, pointerUp on release.
            // Button.onClick has no listeners — discrete Click is ignored.
            if (IsHoldConfirmButton(go))
            {
                if (pad.buttonSouth.wasPressedThisFrame && !_holdingSubmit)
                {
                    var es0 = EventSystem.current;
                    if (es0 != null)
                    {
                        var data = new PointerEventData(es0)
                        {
                            position = (Vector2)go.transform.position,
                            button = PointerEventData.InputButton.Left,
                        };
                        try { ExecuteEvents.Execute(go, data, ExecuteEvents.pointerDownHandler); }
                        catch (Exception e) { Log.Warn($"MenuOverlay HoldDown: {e.Message}"); }
                        _holdTarget = go;
                        _holdingSubmit = true;
                        Log.Info($"MenuOverlay: hold started on {go.name}.");
                    }
                }
                return;
            }

            if (!pad.buttonSouth.wasPressedThisFrame) return;
            var es = EventSystem.current;

            // ModManagerUI: click fires animation but mod-enable logic is on InteractButton — invoke via reflection.
            if (IsInsideModManager())
            {
                var entry = FindModEntryFor(go.transform);
                if (entry != null)
                {
                    LogModEntryMethodsOnce(entry);
                    if (TryInvokeNoArgs(entry,
                        "OnInteractButtonClicked",
                        "OnToggleButtonClicked",
                        "OnButtonClicked",
                        "OnButtonClick",
                        "OnButtonA",
                        "OnInteractButtonDown",
                        "OnInteractClicked",
                        "ToggleEnabled",
                        "Toggle"))
                    {
                        Log.Info($"MenuOverlay: ModEntry toggled via reflection on {entry.gameObject.name}.");
                        return;
                    }
                    Log.Warn($"MenuOverlay: no matching toggle method on ModEntry — see logged methods above.");
                }
            }

            // 2-option TMP_Dropdown: cycle 0↔1 on A without opening popup.
            // SetValueWithoutNotify + manual Invoke: the public setter silently no-ops on alternate
            // presses (TMP_Dropdown refresh-state race); SetValueWithoutNotify writes m_Value directly.
            var ddSubmit = go.GetComponent<TMPro.TMP_Dropdown>();
            if (ddSubmit != null && ddSubmit.options != null && ddSubmit.options.Count == 2)
            {
                int beforeVal = ddSubmit.value;
                int newVal = (beforeVal + 1) % 2;
                var rowName = go.transform.parent != null ? go.transform.parent.name : "<no-parent>";
                var labelTmp = go.transform.parent != null
                    ? go.transform.parent.Find("Label")?.GetComponent<TMPro.TextMeshProUGUI>() : null;
                var labelTxt = labelTmp != null ? labelTmp.text : "<no-label>";
                ddSubmit.SetValueWithoutNotify(newVal);
                ddSubmit.onValueChanged.Invoke(newVal);
                Log.Info($"MenuOverlay BooleanToggle (auto): goId={go.GetInstanceID()} row={rowName} label='{labelTxt}' {beforeVal} → {ddSubmit.value}");
                return;
            }

            if (es != null)
                ExecuteEvents.Execute(go, new BaseEventData(es), ExecuteEvents.submitHandler);

            // Skip pointer-click for Sliders: OnPointerDown sets value by click position
            // (synthesized pointer has no real coord → clobbers slider value).
            if (_focused is Slider) return;

            // Pointer-click required for IPointerClickHandler buttons (Mods/Settings/Credits).
            DuckovController.UI.PointerEventDispatcher.Click(go);
            DuckovController.Haptics.HapticEngine.Instance?.Play(DuckovController.Haptics.HapticCue.DebugStrong);
            Log.Debug_("Haptic: DebugStrong (menu-overlay A)");
        }

        // Fires pointerUp when A released, completing IPointerUpHandler hold-confirm protocol.
        private void HandleHoldRelease(Gamepad pad)
        {
            if (!_holdingSubmit || _holdTarget == null) return;
            if (pad.buttonSouth.isPressed) return;
            var es = EventSystem.current;
            if (es != null)
            {
                var data = new PointerEventData(es)
                {
                    position = (Vector2)_holdTarget.transform.position,
                    button = PointerEventData.InputButton.Left,
                };
                try { ExecuteEvents.Execute(_holdTarget, data, ExecuteEvents.pointerUpHandler); }
                catch (Exception e) { Log.Warn($"MenuOverlay HoldRelease: {e.Message}"); }
            }
            Log.Info($"MenuOverlay: hold released on {_holdTarget.name}.");
            _holdTarget = null;
            _holdingSubmit = false;
        }

        private void ResetHold()
        {
            _navHoldStarted = Time.unscaledTime;
            _lastNavAt = Time.unscaledTime;
            _navHoldSteps = 0;
        }

        // Drives Credits ScrollRect from dpad U/D + left stick Y. Returns true to consume nav input.
        private bool HandleCreditsScroll(Gamepad pad)
        {
            if (!IsInsideCreditsPanel()) return false;
            var creditsRoot = FindAncestorByName(_effectiveRoot, "Credits");
            if (creditsRoot == null) return true; // still consume nav input
            var svT = creditsRoot.Find("ScrollView");
            var sr = svT != null ? svT.GetComponent<ScrollRect>() : null;
            if (sr == null) return true;

            float dpad = 0f;
            if (pad.dpad.up.isPressed)   dpad += 1f;
            if (pad.dpad.down.isPressed) dpad -= 1f;
            float stickY = pad.leftStick.ReadValue().y;
            if (Mathf.Abs(stickY) < 0.18f) stickY = 0f;

            float input = dpad + stickY; // up = +1, down = -1
            if (Mathf.Abs(input) < 0.01f) return true;

            // 0.6 normalized/sec: ~1.7s top→bottom on 9347-tall content (~9× viewport).
            float step = 0.6f * Time.unscaledDeltaTime * input;
            sr.verticalNormalizedPosition = Mathf.Clamp01(
                sr.verticalNormalizedPosition + step);
            return true;
        }

        private bool HandleDifficultyNav(Gamepad pad)
        {
            if (!IsInsideDifficultySelection() || _focused == null)
            {
                ClearDifficultyConfirmGlyph();
                return false;
            }

            // Confirm is a dedicated X action (focus-independent), glyphed like other panels.
            var confirmBtn = FindDifficultyConfirmButton();
            EnsureDifficultyConfirmGlyph(confirmBtn);
            if (pad.buttonWest.wasPressedThisFrame && confirmBtn != null && IsSelectableUsable(confirmBtn))
            {
                Log.Info("MenuOverlay difficulty: X → Confirm");
                DuckovController.UI.PointerEventDispatcher.Click(confirmBtn.gameObject);
                return true;
            }

            bool leftEdge   = DirEdge(pad, NavDir.Left);
            bool rightEdge  = DirEdge(pad, NavDir.Right);
            bool leftHeld   = DirHeld(pad, NavDir.Left);
            bool rightHeld  = DirHeld(pad, NavDir.Right);
            bool anyDpad    = leftHeld || rightHeld || DirHeld(pad, NavDir.Up) || DirHeld(pad, NavDir.Down);
            if (!anyDpad) { _navHoldStarted = Time.unscaledTime; }

            int hdir = 0;
            if (leftEdge)        { hdir = -1; ResetHold(); }
            else if (rightEdge)  { hdir = +1; ResetHold(); }
            else if (leftHeld || rightHeld)
            {
                float held = Time.unscaledTime - _navHoldStarted;
                if (held >= RepeatDelay && (Time.unscaledTime - _lastNavAt) >= RepeatRate)
                {
                    hdir = leftHeld ? -1 : +1;
                    _lastNavAt = Time.unscaledTime;
                }
            }

            // Column is cards only (Confirm excluded); D-pad/stick cycles cards horizontally.
            var col = BuildDifficultyColumn();
            if (col.Count == 0) return true;
            int curIdx = col.FindIndex(s => ReferenceEquals(s, _focused));
            if (curIdx < 0) curIdx = Mathf.Clamp(_lastDifficultyCardIdx, 0, col.Count - 1);

            if (hdir == 0) return true;
            int nextIdx = Mathf.Clamp(curIdx + hdir, 0, col.Count - 1);
            if (nextIdx == curIdx) return true;
            _focused = col[nextIdx];
            _lastDifficultyCardIdx = nextIdx;
            Log.Info($"MenuOverlay difficulty nav: card[{curIdx}] → card[{nextIdx}] name={_focused.gameObject.name}");
            return true;
        }

        // The Confirm button on the difficulty screen (not in the navigable column).
        private Button? FindDifficultyConfirmButton()
        {
            var ds = FindAncestorByName(_effectiveRoot, "DifficultySelection") ?? _menuRoot;
            if (ds == null) return null;
            var confirmT = FindDescendantByName(ds, "Confirm");
            return confirmT != null ? confirmT.GetComponent<Button>() : null;
        }

        // First active Button with a FadeGroupButton component under root.
        private static Button? FindFadeGroupButtonIn(Transform root)
        {
            if (root == null) return null;
            var btns = root.GetComponentsInChildren<Button>(includeInactive: false);
            foreach (var b in btns)
            {
                if (b == null || !b.interactable) continue;
                if (!b.gameObject.activeInHierarchy) continue;
                if (b.GetComponent<FadeGroupButton>() != null) return b;
            }
            return null;
        }

        private static readonly string[] BackButtonNames = {
            "return", "back", "close", "cancel", "exit", "btn_back", "btn_close", "btn_return", "btn_cancel"
        };

        // Nearest UIPanel ancestor of `from` stopping before `stopAt` (menu root).
        private static UIPanel? FindEnclosingPanel(Transform? from, Transform? stopAt)
        {
            var cur = from;
            while (cur != null && !ReferenceEquals(cur, stopAt))
            {
                UIPanel? p = null;
                try { p = cur.GetComponent<UIPanel>(); }
                catch { p = null; }
                if (p != null) return p;
                cur = cur.parent;
            }
            return null;
        }

        private static Button? FindBackButtonIn(Transform root)
        {
            if (root == null) return null;
            var btns = root.GetComponentsInChildren<Button>(includeInactive: false);
            foreach (var b in btns)
            {
                if (b == null || !b.interactable) continue;
                if (!b.gameObject.activeInHierarchy) continue;
                string n = b.gameObject.name.ToLowerInvariant();
                foreach (var key in BackButtonNames)
                {
                    if (n == key) return b;
                    if (n.StartsWith(key + "_")) return b;
                    if (n.StartsWith(key + " ")) return b;
                }
            }
            return null;
        }
    }
}
