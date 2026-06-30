using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DuckovController.UI.Menu
{
    // Character-creator: cache management, column builders, swatch-grid nav, rotation synthesis, submit.
    internal sealed partial class MenuFocusOverlay : MonoBehaviour
    {
        private static System.Type? GetCustomFaceTabsType()
        {
            if (_customFaceTabsType != null) return _customFaceTabsType;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("CustomFaceTabs", false);
                if (t != null) { _customFaceTabsType = t; return _customFaceTabsType; }
            }
            return null;
        }

        // Caches root + tab/panel transforms once per activation; IsCharacterCreatorActive() is then a field check.
        private void EnsureCharacterCreatorCacheFresh()
        {
            // Sticky: root cached and alive → cheap early-out.
            if (_ccRoot != null)
            {
                if (_ccRoot.gameObject.activeInHierarchy) return;
                // Creator closed: invalidate.
                ClearCharacterCreatorCache();
                return;
            }
            // Throttle the FindObjectOfType probe when the creator is closed.
            if (Time.unscaledTime < _ccNextProbeAt) return;
            _ccNextProbeAt = Time.unscaledTime + 0.5f;

            var type = GetCustomFaceTabsType();
            if (type == null) return;
            MonoBehaviour? any;
            try { any = UnityEngine.Object.FindObjectOfType(type) as MonoBehaviour; }
            catch { any = null; }
            if (any == null || !any.gameObject.activeInHierarchy) return;

            var t2 = any.transform;
            while (t2 != null && t2.GetComponent<Canvas>() == null) t2 = t2.parent;
            if (t2 == null) return;
            _ccRoot = t2;

            // Capture all 8 *Panel transforms once.
            _ccPanelTransforms = new List<Transform>();
            foreach (var name in new[] { "MainPanel", "HairPanel", "EyePanel", "EybrowePanel",
                                          "MouthPanel", "WingPanel", "TailPanel", "FootPanel" })
            {
                var pt = FindDescendantByName(_ccRoot, name);
                if (pt != null) _ccPanelTransforms.Add(pt);
            }
            _ccActivePanelMask = -1;

            // Cache the DragHandler overlay for right-stick rotation drag.
            var dh = FindDescendantByName(_ccRoot, "DragHandler");
            _ccDragHandlerGo = dh != null ? dh.gameObject : null;

            // Cache all color-picker components under the root (Item 1: sub-panel nav).
            _ccPickers = null;
            var pickerType = GetColorPickerType();
            if (pickerType != null)
            {
                var found = _ccRoot.GetComponentsInChildren(pickerType, includeInactive: true);
                _ccPickers = new List<Component>(found.Length);
                foreach (var c in found) if (c is Component cc) _ccPickers.Add(cc);
            }

            Log.Info($"CC cache initialised: root={_ccRoot.name} panels={_ccPanelTransforms.Count} dragHandler={(_ccDragHandlerGo != null ? "yes" : "no")} pickers={(_ccPickers?.Count ?? 0)}");
        }

        private static void ClearCharacterCreatorCache()
        {
            _ccRoot = null;
            _ccTopColumn = null;
            _ccPanelTransforms = null;
            _ccSwatchCol = null;
            _ccGridRows = null;
            _ccActivePanelMask = -1;
            _ccDragHandlerGo = null;
            _ccPickers = null;
        }

        private bool IsCharacterCreatorActive()
        {
            EnsureCharacterCreatorCacheFresh();
            return _ccRoot != null;
        }

        private Transform? FindCharacterCreatorRoot()
        {
            EnsureCharacterCreatorCacheFresh();
            return _ccRoot;
        }

        // Bitmask of which cached panel transforms are activeSelf (no GetComponent/walk).
        private int ComputeActivePanelMask()
        {
            if (_ccPanelTransforms == null) return 0;
            int mask = 0;
            for (int i = 0; i < _ccPanelTransforms.Count; i++)
            {
                var t = _ccPanelTransforms[i];
                if (t != null && t.gameObject.activeSelf) mask |= (1 << i);
            }
            return mask;
        }

        // Ordered top-level cycle: 7 tabs + 3 extras + Confirm + Cancle.
        // Names match what the dump shows for the PREPARE-scene creator UI.
        private static readonly string[] CcTabNames    = { "Head", "Eye", "Hair", "Mouth", "Wing", "Tail", "Foot" };
        private static readonly string[] CcExtraNames  = { "YellowDuck", "Copy", "Paste" };
        private static readonly string[] CcActionNames = { "Confirm", "Cancle" };

        private List<Selectable> BuildCharacterCreatorTopColumn()
        {
            EnsureCharacterCreatorCacheFresh();
            if (_ccTopColumn != null) return _ccTopColumn;
            var result = new List<Selectable>();
            var root = _ccRoot;
            if (root == null) { _ccTopColumn = result; return result; }
            void Add(string name)
            {
                var t = FindDescendantByName(root, name);
                if (t == null) return;
                var s = t.GetComponent<Selectable>();
                if (s == null)
                {
                    s = t.gameObject.AddComponent<Button>();
                    s.transition = Selectable.Transition.None;
                }
                result.Add(s);
            }
            foreach (var n in CcTabNames) Add(n);
            foreach (var n in CcExtraNames) Add(n);
            foreach (var n in CcActionNames) Add(n);
            _ccTopColumn = result;
            return result;
        }

        private List<Selectable> BuildCharacterCreatorSwatchColumn()
        {
            EnsureCharacterCreatorCacheFresh();
            EnsureSwatchGridFreshForActivePanels();
            return _ccSwatchCol ?? new List<Selectable>();
        }

        private List<List<Selectable>> GetCharacterCreatorGridRows()
        {
            EnsureCharacterCreatorCacheFresh();
            EnsureSwatchGridFreshForActivePanels();
            return _ccGridRows ?? new List<List<Selectable>>();
        }

        // Rebuilds swatch grid only when active-panel set changes (e.g. Eye tab activates EyePanel+EybrowePanel).
        private void EnsureSwatchGridFreshForActivePanels()
        {
            if (_ccPanelTransforms == null) return;
            int mask = ComputeActivePanelMask();
            if (mask == _ccActivePanelMask && _ccSwatchCol != null && _ccGridRows != null) return;
            _ccActivePanelMask = mask;

            var flat = new List<Selectable>();
            for (int i = 0; i < _ccPanelTransforms.Count; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                var panel = _ccPanelTransforms[i];
                if (panel == null) continue;
                var all = panel.GetComponentsInChildren<Selectable>(includeInactive: false);
                foreach (var s in all)
                {
                    if (s == null) continue;
                    if (s is Scrollbar) continue;
                    if (s is TMPro.TMP_InputField) continue;
                    if (s is UnityEngine.UI.InputField) continue;
                    if (!IsSelectableUsable(s)) continue;
                    flat.Add(s);
                }
            }
            // Top-to-bottom then left-to-right so two-column layouts (EyePanel+EybrowePanel) interleave per row.
            flat.Sort((a, b) =>
            {
                float dy = b.transform.position.y - a.transform.position.y;
                if (Mathf.Abs(dy) > 2f) return dy > 0 ? 1 : -1;
                return a.transform.position.x.CompareTo(b.transform.position.x);
            });

            // Bucket into rows by Y.
            var rows = new List<List<Selectable>>();
            List<Selectable>? current = null;
            float lastY = float.PositiveInfinity;
            foreach (var s in flat)
            {
                float y = s.transform.position.y;
                if (current == null || Mathf.Abs(y - lastY) > 2f)
                {
                    current = new List<Selectable>();
                    rows.Add(current);
                    lastY = y;
                }
                current.Add(s);
            }
            _ccSwatchCol = flat;
            _ccGridRows = rows;
            Log.Info($"CC swatch grid rebuilt: mask=0x{mask:X} items={flat.Count} rows={rows.Count}");
        }

        private static bool IsCharacterCreatorTab(Selectable? s)
        {
            if (s == null) return false;
            var n = s.gameObject.name;
            foreach (var t in CcTabNames) if (n == t) return true;
            return false;
        }

        // Most CC buttons have only IPointerClickHandler wiring (Button.onClick = 0 listeners).
        private static void FirePointerClick(GameObject? go)
        {
            if (go == null) return;
            DuckovController.UI.PointerEventDispatcher.Click(go);
        }

        // Gold-border outline around the active swatch when top focus is a CC tab; hidden otherwise.
        private void UpdateCharacterCreatorSwatchOutline()
        {
            bool show = _ccSwatchFocused != null
                     && IsCharacterCreatorActive()
                     && IsCharacterCreatorTab(_focused);
            if (!show)
            {
                if (_ccSwatchOutline != null) _ccSwatchOutline.Hide();
                return;
            }
            if (_ccSwatchOutline == null)
            {
                _ccSwatchOutline = gameObject.AddComponent<FocusOutlineOverlay>();
            }
            var rt = _ccSwatchFocused!.transform as RectTransform;
            if (rt == null) { _ccSwatchOutline.Hide(); return; }
            Color col = Color.yellow;
            try
            {
                var cfg = DuckovController.UI.Settings.SettingsBridge.Cfg?.Ui;
                if (cfg != null && ColorUtility.TryParseHtmlString(cfg.FocusOutlineColorHex, out var c)) col = c;
            }
            catch { /* tolerated */ }
            float thick = 4f;
            try { thick = Mathf.Max(2f, DuckovController.UI.Settings.SettingsBridge.Cfg?.Ui?.FocusOutlineThicknessPx ?? 4f); }
            catch { }
            _ccSwatchOutline.Show(rt, col, thick);
        }

        private bool HandleCharacterCreatorNav(Gamepad pad)
        {
            if (!IsCharacterCreatorActive() || _focused == null)
            {
                if (_ccOpenPicker != null) ExitPickerMode();
                return false;
            }

            // RS-X → preview rotation via synthesized drag events (IBeginDrag/IDrag/IEndDrag).
            // Kept available while a picker is open (rotation is independent of focus nav).
            HandleCharacterCreatorRotation(pad);

            // Color-picker sub-panel: when one is open, nav is LOCKED inside it (Item 1).
            UpdateOpenPickerState();
            if (_ccOpenPicker != null)
            {
                HandlePickerNav(pad);
                return true; // consume: no tab cycle / swatch nav while the picker owns input
            }

            // Top-level cycle via LB/RB/LT/RT.
            bool prevEdge = pad.leftShoulder.wasPressedThisFrame  || pad.leftTrigger.wasPressedThisFrame;
            bool nextEdge = pad.rightShoulder.wasPressedThisFrame || pad.rightTrigger.wasPressedThisFrame;
            bool prevHeld = pad.leftShoulder.isPressed  || pad.leftTrigger.isPressed;
            bool nextHeld = pad.rightShoulder.isPressed || pad.rightTrigger.isPressed;

            int shoulderDir = 0;
            if (prevEdge)      { shoulderDir = -1; ResetHold(); }
            else if (nextEdge) { shoulderDir = +1; ResetHold(); }
            else if (prevHeld || nextHeld)
            {
                float held = Time.unscaledTime - _navHoldStarted;
                if (held >= RepeatDelay && (Time.unscaledTime - _lastNavAt) >= RepeatRate)
                {
                    shoulderDir = prevHeld ? -1 : +1;
                    _lastNavAt = Time.unscaledTime;
                }
            }

            var topCol = BuildCharacterCreatorTopColumn();
            if (topCol.Count == 0) return true;
            int curIdx = topCol.FindIndex(s => ReferenceEquals(s, _focused));
            if (curIdx < 0) curIdx = 0;

            if (shoulderDir != 0)
            {
                int n = topCol.Count;
                int next = ((curIdx + shoulderDir) % n + n) % n;
                _focused = topCol[next];
                Log.Info($"CharacterCreator top {curIdx} → {next} ({_focused.gameObject.name})");
                // Tabs auto-activate their panel when clicked. Fire click
                // so the *Panel switches and the swatch column refreshes.
                if (IsCharacterCreatorTab(_focused))
                {
                    FirePointerClick(_focused.gameObject);
                    // Switch tab panel; swatch cursor resets (restored from _ccSwatchLastIdxByTab below).
                    _ccSwatchFocused = null;
                }
                curIdx = next;
            }

            // DPad navigates swatches when top focus is a tab.
            if (IsCharacterCreatorTab(_focused))
            {
                var swatches = BuildCharacterCreatorSwatchColumn();
                var rows = GetCharacterCreatorGridRows();
                if (swatches.Count > 0 && rows.Count > 0)
                {
                    if (_ccSwatchFocused == null || !swatches.Contains(_ccSwatchFocused))
                    {
                        int last = _ccSwatchLastIdxByTab.TryGetValue(curIdx, out int li) ? li : 0;
                        _ccSwatchFocused = swatches[Mathf.Clamp(last, 0, swatches.Count - 1)];
                    }

                    bool upE = DirEdge(pad, NavDir.Up);
                    bool dnE = DirEdge(pad, NavDir.Down);
                    bool lfE = DirEdge(pad, NavDir.Left);
                    bool rtE = DirEdge(pad, NavDir.Right);

                    // Slider widgets: dpad L/R adjusts value; U/D moves out
                    // to neighbouring row.
                    if (_ccSwatchFocused is Slider slider && (lfE || rtE))
                    {
                        AdjustSlider(slider, lfE ? -1 : +1);
                        return true;
                    }

                    // Locate the current swatch in the row grid.
                    int curRow = -1, curCol = -1;
                    for (int r = 0; r < rows.Count && curRow < 0; r++)
                    {
                        for (int c = 0; c < rows[r].Count; c++)
                        {
                            if (ReferenceEquals(rows[r][c], _ccSwatchFocused)) { curRow = r; curCol = c; break; }
                        }
                    }
                    if (curRow < 0) { curRow = 0; curCol = 0; }

                    int newRow = curRow, newCol = curCol;
                    if (lfE)      newCol = Mathf.Max(0, curCol - 1);
                    else if (rtE) newCol = Mathf.Min(rows[curRow].Count - 1, curCol + 1);
                    else if (upE) newRow = Mathf.Max(0, curRow - 1);
                    else if (dnE) newRow = Mathf.Min(rows.Count - 1, curRow + 1);

                    if (newRow != curRow)
                    {
                        // Clamp col to new row's length.
                        newCol = Mathf.Min(newCol, rows[newRow].Count - 1);
                    }

                    if (newRow != curRow || newCol != curCol)
                    {
                        _ccSwatchFocused = rows[newRow][newCol];
                        _ccSwatchLastIdxByTab[curIdx] = swatches.IndexOf(_ccSwatchFocused);
                    }
                }
                else _ccSwatchFocused = null;
            }
            else
            {
                _ccSwatchFocused = null;
            }
            return true;
        }

        // ── Color-picker sub-panel (Item 1) ──────────────────────────────────────────────

        private static System.Type? GetColorPickerType()
        {
            if (_ccPickerType != null) return _ccPickerType;
            _ccPickerType = System.Type.GetType("CustomFaceUIColorPicker, TeamSoda.Duckov.Core");
            if (_ccPickerType == null)
            {
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    var t = asm.GetType("CustomFaceUIColorPicker", false);
                    if (t != null) { _ccPickerType = t; break; }
                }
            }
            if (_ccPickerType != null)
            {
                const System.Reflection.BindingFlags f =
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                _ccPickerButtonParentField = _ccPickerType.GetField("buttonParent", f);
                _ccPickerCurrentColorProp = _ccPickerType.GetProperty("CurrentColor", f);
                _ccPickerSetColorMethod = _ccPickerType.GetMethod("SetColor", f, null, new[] { typeof(Color) }, null);
            }
            return _ccPickerType;
        }

        private static Transform? GetPickerButtonParent(Component picker)
            => _ccPickerButtonParentField?.GetValue(picker) as Transform;

        private static Color GetPickerCurrentColor(Component picker)
        {
            try { return (Color)(_ccPickerCurrentColorProp?.GetValue(picker) ?? Color.white); }
            catch { return Color.white; }
        }

        private static void PickerSetColor(Component picker, Color color)
        {
            try { _ccPickerSetColorMethod?.Invoke(picker, new object[] { color }); }
            catch (System.Exception e) { Log.Debug_($"CC picker SetColor: {e.Message}"); }
        }

        private static Color GetSwatchColor(Selectable swatch)
        {
            // CustomFaceUIColorPickerButton.Color getter (cached PropertyInfo).
            if (_ccPickerBtnColorProp == null)
            {
                var bt = System.Type.GetType("CustomFaceUIColorPickerButton, TeamSoda.Duckov.Core");
                if (bt == null)
                    foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                    { bt = asm.GetType("CustomFaceUIColorPickerButton", false); if (bt != null) break; }
                if (bt != null)
                    _ccPickerBtnColorProp = bt.GetProperty("Color",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            }
            var comp = swatch.GetComponent("CustomFaceUIColorPickerButton");
            if (comp != null && _ccPickerBtnColorProp != null)
            {
                try { return (Color)(_ccPickerBtnColorProp.GetValue(comp) ?? Color.white); }
                catch { }
            }
            return Color.white;
        }

        // Detect the currently-open picker (buttonParent active). Captures open-time color on entry.
        private void UpdateOpenPickerState()
        {
            if (GetColorPickerType() == null) { if (_ccOpenPicker != null) ExitPickerMode(); return; }

            Component? open = null;
            if (_ccPickers != null)
            {
                foreach (var p in _ccPickers)
                {
                    if (p == null) continue;
                    var bp = GetPickerButtonParent(p);
                    if (bp != null && bp.gameObject.activeInHierarchy) { open = p; break; }
                }
            }

            if (open == null) { if (_ccOpenPicker != null) ExitPickerMode(); return; }
            if (ReferenceEquals(open, _ccOpenPicker)) return; // already in picker mode for this one

            // Entering picker mode.
            _ccOpenPicker = open;
            _ccPickerOpenColor = GetPickerCurrentColor(open);
            BuildPickerGrid(open);
            // Focus the swatch matching the current color, else first.
            _ccPickerFocused = null;
            if (_ccPickerSwatches != null)
            {
                foreach (var s in _ccPickerSwatches)
                {
                    var c = GetSwatchColor(s);
                    if (Mathf.Approximately(c.r, _ccPickerOpenColor.r) &&
                        Mathf.Approximately(c.g, _ccPickerOpenColor.g) &&
                        Mathf.Approximately(c.b, _ccPickerOpenColor.b)) { _ccPickerFocused = s; break; }
                }
                if (_ccPickerFocused == null && _ccPickerSwatches.Count > 0) _ccPickerFocused = _ccPickerSwatches[0];
            }
            DuckovController.Haptics.HapticEngine.Instance?.Play(DuckovController.Haptics.HapticCue.PageTab);
            Log.Info($"CC picker opened: swatches={_ccPickerSwatches?.Count ?? 0}");
        }

        // Flatten + Y-bucket the open picker's swatch buttons into a grid (mirrors the swatch-grid logic).
        private void BuildPickerGrid(Component picker)
        {
            _ccPickerSwatches = new List<Selectable>();
            _ccPickerRows = new List<List<Selectable>>();
            var bp = GetPickerButtonParent(picker);
            if (bp == null) return;

            var flat = new List<Selectable>();
            var all = bp.GetComponentsInChildren<Selectable>(includeInactive: false);
            foreach (var s in all)
            {
                if (s == null) continue;
                if (s.GetComponent("CustomFaceUIColorPickerButton") == null) continue;
                if (!IsSelectableUsable(s)) continue;
                flat.Add(s);
            }
            flat.Sort((a, b) =>
            {
                float dy = b.transform.position.y - a.transform.position.y;
                if (Mathf.Abs(dy) > 2f) return dy > 0 ? 1 : -1;
                return a.transform.position.x.CompareTo(b.transform.position.x);
            });

            List<Selectable>? current = null;
            float lastY = float.PositiveInfinity;
            foreach (var s in flat)
            {
                float y = s.transform.position.y;
                if (current == null || Mathf.Abs(y - lastY) > 2f)
                {
                    current = new List<Selectable>();
                    _ccPickerRows.Add(current);
                    lastY = y;
                }
                current.Add(s);
            }
            _ccPickerSwatches = flat;
        }

        // Grid nav inside the open picker (d-pad + stick), with hold-to-repeat for fast traversal.
        private void HandlePickerNav(Gamepad pad)
        {
            var rows = _ccPickerRows;
            if (rows == null || rows.Count == 0 || _ccPickerSwatches == null || _ccPickerSwatches.Count == 0)
            {
                UpdatePickerOutline();
                return;
            }
            if (_ccPickerFocused == null || !_ccPickerSwatches.Contains(_ccPickerFocused))
                _ccPickerFocused = _ccPickerSwatches[0];

            // Resolve the active direction this frame: an edge moves immediately; a sustained hold
            // repeats after RepeatDelay at the accelerating NavAccel rate (parity with the other nav
            // handlers) so the left stick / d-pad can sweep the grid quickly.
            NavDir? dir = null;
            NavDir? heldDir =
                  DirHeld(pad, NavDir.Left)  ? NavDir.Left
                : DirHeld(pad, NavDir.Right) ? NavDir.Right
                : DirHeld(pad, NavDir.Up)    ? NavDir.Up
                : DirHeld(pad, NavDir.Down)  ? (NavDir?)NavDir.Down
                : null;

            NavDir? edgeDir =
                  DirEdge(pad, NavDir.Left)  ? NavDir.Left
                : DirEdge(pad, NavDir.Right) ? NavDir.Right
                : DirEdge(pad, NavDir.Up)    ? NavDir.Up
                : DirEdge(pad, NavDir.Down)  ? (NavDir?)NavDir.Down
                : null;

            if (edgeDir.HasValue)
            {
                dir = edgeDir;
                _ccPickerHeldDir = edgeDir;
                _navHoldStarted = Time.unscaledTime;
                _lastNavAt = Time.unscaledTime;
                _navHoldSteps = 0;
            }
            else if (heldDir.HasValue)
            {
                if (heldDir != _ccPickerHeldDir)
                {
                    // Direction changed mid-hold → re-pay the initial delay.
                    _ccPickerHeldDir = heldDir;
                    _navHoldStarted = Time.unscaledTime;
                    _lastNavAt = Time.unscaledTime;
                    _navHoldSteps = 0;
                }
                else
                {
                    float held = Time.unscaledTime - _navHoldStarted;
                    float rate = NavAccel.EffectiveRate(RepeatRate, _navHoldSteps);
                    if (held >= RepeatDelay && (Time.unscaledTime - _lastNavAt) >= rate)
                    {
                        dir = heldDir;
                        _lastNavAt = Time.unscaledTime;
                        _navHoldSteps++;
                    }
                }
            }
            else
            {
                _ccPickerHeldDir = null;
                _navHoldSteps = 0;
            }

            if (dir.HasValue)
            {
                int curRow = -1, curCol = -1;
                for (int r = 0; r < rows.Count && curRow < 0; r++)
                    for (int c = 0; c < rows[r].Count; c++)
                        if (ReferenceEquals(rows[r][c], _ccPickerFocused)) { curRow = r; curCol = c; break; }
                if (curRow < 0) { curRow = 0; curCol = 0; }

                int newRow = curRow, newCol = curCol;
                switch (dir.Value)
                {
                    case NavDir.Left:  newCol = Mathf.Max(0, curCol - 1); break;
                    case NavDir.Right: newCol = Mathf.Min(rows[curRow].Count - 1, curCol + 1); break;
                    case NavDir.Up:    newRow = Mathf.Max(0, curRow - 1); break;
                    case NavDir.Down:  newRow = Mathf.Min(rows.Count - 1, curRow + 1); break;
                }
                if (newRow != curRow) newCol = Mathf.Min(newCol, rows[newRow].Count - 1);

                if (newRow != curRow || newCol != curCol)
                {
                    _ccPickerFocused = rows[newRow][newCol];
                    DuckovController.Haptics.HapticEngine.Instance?.Play(DuckovController.Haptics.HapticCue.FocusTick);
                }
            }
            UpdatePickerOutline();
        }

        private void UpdatePickerOutline()
        {
            if (_ccOpenPicker == null || _ccPickerFocused == null)
            {
                _ccPickerOutline?.Hide();
                return;
            }
            var rt = _ccPickerFocused.transform as RectTransform;
            if (rt == null) { _ccPickerOutline?.Hide(); return; }
            if (_ccPickerOutline == null) _ccPickerOutline = gameObject.AddComponent<FocusOutlineOverlay>();
            Color col = Color.yellow;
            float thick = 4f;
            try
            {
                var cfg = DuckovController.UI.Settings.SettingsBridge.Cfg?.Ui;
                if (cfg != null && ColorUtility.TryParseHtmlString(cfg.FocusOutlineColorHex, out var c)) col = c;
                thick = Mathf.Max(2f, cfg?.FocusOutlineThicknessPx ?? 4f);
            }
            catch { }
            _ccPickerOutline.Show(rt, col, thick);
        }

        // Apply the focused swatch's color. keepOpen=true re-opens buttonParent (A); false closes (X).
        private void PickerApplyFocused(bool keepOpen)
        {
            if (_ccOpenPicker == null || _ccPickerFocused == null) return;
            var color = GetSwatchColor(_ccPickerFocused);
            PickerSetColor(_ccOpenPicker, color); // applies live + closes buttonParent
            DuckovController.Haptics.HapticEngine.Instance?.Play(DuckovController.Haptics.HapticCue.Confirm);
            if (keepOpen)
            {
                var bp = GetPickerButtonParent(_ccOpenPicker);
                if (bp != null) bp.gameObject.SetActive(true); // stay in picker mode; refresh selection
                UpdatePickerOutline();
            }
            else
            {
                ExitPickerMode(); // X: leave the picker
            }
        }

        // B: revert to the color the picker had when it opened, then close + exit.
        private void PickerCancel()
        {
            if (_ccOpenPicker == null) return;
            PickerSetColor(_ccOpenPicker, _ccPickerOpenColor);
            DuckovController.Haptics.HapticEngine.Instance?.Play(DuckovController.Haptics.HapticCue.Cancel);
            ExitPickerMode();
        }

        private void ExitPickerMode()
        {
            _ccOpenPicker = null;
            _ccPickerFocused = null;
            _ccPickerHeldDir = null;
            _ccPickerSwatches = null;
            _ccPickerRows = null;
            _ccPickerOutline?.Hide();
        }

        // Top-right hint panel: A Apply · X Apply & Close · B Cancel, shown only while a picker is open.
        private static readonly DuckovController.UI.Prompts.PromptEntry[] _ccPickerPrompts =
        {
            new DuckovController.UI.Prompts.PromptEntry(DuckovController.UI.Common.ButtonGlyph.A, "Apply"),
            new DuckovController.UI.Prompts.PromptEntry(DuckovController.UI.Common.ButtonGlyph.X, "Apply & Close"),
            new DuckovController.UI.Prompts.PromptEntry(DuckovController.UI.Common.ButtonGlyph.B, "Cancel"),
        };

        private void UpdatePickerHints()
        {
            if (_ccOpenPicker == null || _ccRoot == null)
            {
                _ccPickerHints?.Hide();
                return;
            }
            var canvas = _ccRoot.GetComponentInParent<Canvas>();
            if (canvas == null) { _ccPickerHints?.Hide(); return; }
            _ccPickerHints ??= new DuckovController.UI.Prompts.MenuHintPanel();
            _ccPickerHints.SetPrompts(_ccPickerPrompts);
            _ccPickerHints.ShowUnder(canvas);
        }

        // Adjusts slider by dir * step (same step logic as HandleSliderHorizontal).
        private static void AdjustSlider(Slider slider, int dir)
        {
            float step = slider.wholeNumbers ? 1f : Mathf.Max(0.001f, (slider.maxValue - slider.minValue) * 0.02f);
            float next = Mathf.Clamp(slider.value + dir * step, slider.minValue, slider.maxValue);
            slider.value = next;
        }

        // RS-X → preview rotation via synthesized Begin/Drag/End pointer events on DragHandler.
        private void HandleCharacterCreatorRotation(Gamepad pad)
        {
            if (_ccDragHandlerGo == null) return;
            var es = EventSystem.current;
            if (es == null) return;

            float sx = pad.rightStick.x.ReadValue();
            float mag = Mathf.Abs(sx);
            bool above = _ccRotating ? mag > CcRotateDeadzoneExit : mag > CcRotateDeadzoneEnter;

            if (_ccRotatePed == null) _ccRotatePed = new PointerEventData(es) { pointerId = -10 };
            var ped = _ccRotatePed;

            if (above)
            {
                float dt = Time.unscaledDeltaTime;
                // Remap above exit deadzone to [0,1] to avoid slamming to full speed at threshold.
                float normalized = Mathf.Sign(sx) * Mathf.Clamp01((mag - CcRotateDeadzoneExit) / (1f - CcRotateDeadzoneExit));
                float deltaX = normalized * CcRotatePxPerSec * dt;

                if (!_ccRotating)
                {
                    _ccRotatePressPos = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                    _ccRotatePos = _ccRotatePressPos;
                    ped.button = PointerEventData.InputButton.Left;
                    ped.position = _ccRotatePos;
                    ped.pressPosition = _ccRotatePressPos;
                    ped.delta = Vector2.zero;
                    try
                    {
                        ExecuteEvents.Execute(_ccDragHandlerGo, ped, ExecuteEvents.pointerDownHandler);
                        ExecuteEvents.Execute(_ccDragHandlerGo, ped, ExecuteEvents.beginDragHandler);
                    }
                    catch (Exception e) { Log.Warn($"CC rotate beginDrag: {e.Message}"); }
                    _ccRotating = true;
                }

                _ccRotatePos.x += deltaX;
                ped.position = _ccRotatePos;
                ped.pressPosition = _ccRotatePressPos; // stable
                ped.delta = new Vector2(deltaX, 0);
                try { ExecuteEvents.Execute(_ccDragHandlerGo, ped, ExecuteEvents.dragHandler); }
                catch (Exception e) { Log.Warn($"CC rotate drag: {e.Message}"); }
            }
            else if (_ccRotating)
            {
                ped.position = _ccRotatePos;
                ped.pressPosition = _ccRotatePressPos;
                ped.delta = Vector2.zero;
                try
                {
                    ExecuteEvents.Execute(_ccDragHandlerGo, ped, ExecuteEvents.endDragHandler);
                    ExecuteEvents.Execute(_ccDragHandlerGo, ped, ExecuteEvents.pointerUpHandler);
                }
                catch (Exception e) { Log.Warn($"CC rotate endDrag: {e.Message}"); }
                _ccRotating = false;
            }
        }

        // A in CC scope: apply swatch (on tab) or fire top-level item.
        // Start/Select are game-owned; don't shortcut Confirm/Cancle to them.
        private bool HandleCharacterCreatorSubmit(Gamepad pad)
        {
            if (!IsCharacterCreatorActive() || _focused == null) return false;

            // Color-picker sub-panel owns A/X while open: A applies + keeps open, X applies + closes.
            if (_ccOpenPicker != null)
            {
                if (pad.buttonSouth.wasPressedThisFrame) { PickerApplyFocused(keepOpen: true); return true; }
                if (pad.buttonWest.wasPressedThisFrame)  { PickerApplyFocused(keepOpen: false); return true; }
                return true; // consume all other submit input while the picker is open
            }

            if (!pad.buttonSouth.wasPressedThisFrame) return true; // consumed (we own A handling here)
            if (IsCharacterCreatorTab(_focused))
            {
                if (_ccSwatchFocused != null)
                {
                    FirePointerClick(_ccSwatchFocused.gameObject);
                    Log.Info($"CC A: applied swatch {_ccSwatchFocused.gameObject.name}");
                }
                return true;
            }
            FirePointerClick(_focused.gameObject);
            Log.Info($"CC A: fired top-level {_focused.gameObject.name}");
            return true;
        }
    }
}
