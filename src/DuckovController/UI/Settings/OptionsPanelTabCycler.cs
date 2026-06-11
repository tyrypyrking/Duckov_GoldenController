using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DuckovController.UI.Settings
{
    // Cycles OptionsPanel tabs via LB/RB/LT/RT (SetSelection reflect). Gates on FadeGroup.IsShown
    // so it doesn't fire while a dropdown overlay is active. wasPressedThisFrame survives reconnects.
    internal sealed class OptionsPanelTabCycler : MonoBehaviour
    {
        internal MonoBehaviour? OptionsPanel;
        internal Type? TabButtonType;

        // Cached reflection handles.
        private FieldInfo? _tabButtonsField;
        private MethodInfo? _setSelectionMethod;
        private MethodInfo? _getSelectionMethod;
        private FieldInfo? _fadeGroupField;

        private void Start()
        {
            CacheReflection();
        }

        private void OnEnable()
        {
            // Reset to first tab on every open; avoids stale highlight on Controller Mod tab.
            CacheReflection();
            ResetSelectionToFirst();
        }

        private void ResetSelectionToFirst()
        {
            if (OptionsPanel == null) return;
            if (_tabButtonsField == null || _setSelectionMethod == null) return;
            try
            {
                if (_tabButtonsField.GetValue(OptionsPanel) is System.Collections.IList list
                    && list.Count > 0)
                {
                    _setSelectionMethod.Invoke(OptionsPanel, new[] { list[0] });
                }
                // Clear EventSystem selection: removes stale tint on our cloned tab button.
                var es = EventSystem.current;
                if (es != null) es.SetSelectedGameObject(null);
            }
            catch (Exception e) { Log.Warn($"ResetSelectionToFirst: {e.Message}"); }
        }

        private void CacheReflection()
        {
            if (OptionsPanel == null) return;
            var t = OptionsPanel.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            _tabButtonsField = t.GetField("tabButtons", flags);
            _setSelectionMethod = t.GetMethod("SetSelection", flags);
            _getSelectionMethod = t.GetMethod("GetSelection", flags);
            // fadeGroup is on UIPanel base class — walk up.
            for (var cursor = t; cursor != null && cursor != typeof(MonoBehaviour); cursor = cursor.BaseType)
            {
                var f = cursor.GetField("fadeGroup", flags);
                if (f != null) { _fadeGroupField = f; break; }
            }
        }

        // Reflected: the tab field on OptionsPanel_TabButton that points at
        // the content GameObject we should focus inside.
        private FieldInfo? _tabContentField;

        private void Update()
        {
            if (OptionsPanel == null) return;
            if (_tabButtonsField == null || _setSelectionMethod == null || _getSelectionMethod == null) return;

            // Only act when this panel's FadeGroup is fully shown.
            if (!IsPanelShown()) return;

            var pad = Gamepad.current;
            if (pad == null) return;

            bool prev = pad.leftShoulder.wasPressedThisFrame
                     || pad.leftTrigger.wasPressedThisFrame;
            bool next = pad.rightShoulder.wasPressedThisFrame
                     || pad.rightTrigger.wasPressedThisFrame;
            bool reset = pad.buttonWest.wasPressedThisFrame;

            if (reset)
            {
                TryResetFocusedRow();
                return;
            }
            if (!prev && !next) return;

            try
            {
                var rawList = _tabButtonsField.GetValue(OptionsPanel);
                if (rawList is not System.Collections.IList list || list.Count == 0) return;

                var currentSel = _getSelectionMethod.Invoke(OptionsPanel, null);
                int idx = -1;
                for (int i = 0; i < list.Count; i++)
                {
                    if (ReferenceEquals(list[i], currentSel)) { idx = i; break; }
                }
                if (idx < 0) idx = 0;

                int delta = next ? +1 : -1;
                int n = list.Count;
                int newIdx = ((idx + delta) % n + n) % n;
                var newSel = list[newIdx];
                if (newSel == null) return;

                _setSelectionMethod.Invoke(OptionsPanel, new[] { newSel });
                Log.Info($"OptionsPanelTabCycler: tab {idx} → {newIdx} (n={n}).");
                // Land focus on first interactable in the new tab.
                FocusFirstInsideTab(newSel);
            }
            catch (Exception e)
            {
                Log.Error($"OptionsPanelTabCycler.Update: {e.Message}");
            }
        }

        // OptionsPanel_TabButton.tab field — resolved lazily to avoid JIT-time type touch.
        private FieldInfo? GetTabContentField(object tabButton)
        {
            if (_tabContentField != null) return _tabContentField;
            _tabContentField = tabButton.GetType().GetField("tab",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return _tabContentField;
        }

        private GameObject? GetActiveTabContent()
        {
            if (OptionsPanel == null || _getSelectionMethod == null) return null;
            try
            {
                var sel = _getSelectionMethod.Invoke(OptionsPanel, null);
                if (sel == null) return null;
                var f = GetTabContentField(sel);
                return f?.GetValue(sel) as GameObject;
            }
            catch { return null; }
        }

        private bool IsOurTab(GameObject content)
        {
            // Discriminate by ControllerSettingsRowMarker presence — vanilla tabs have none.
            return content.GetComponentInChildren<ControllerSettingsRowMarker>(true) != null;
        }

        private void FocusFirstInsideTab(object tabButton)
        {
            var f = GetTabContentField(tabButton);
            if (f?.GetValue(tabButton) is GameObject content)
            {
                if (IsOurTab(content)) FocusFirstInside(content);
            }
        }

        private static void FocusFirstInside(GameObject content)
        {
            var es = EventSystem.current;
            if (es == null) return;
            var selectables = content.GetComponentsInChildren<Selectable>(includeInactive: false);
            foreach (var s in selectables)
            {
                if (s == null) continue;
                if (!s.interactable) continue;
                if (!s.gameObject.activeInHierarchy) continue;
                // Skip dropdown Template items (hidden until expanded).
                if (s.transform.parent != null
                    && s.transform.parent.name == "Item"
                    && s.transform.parent.parent != null
                    && s.transform.parent.parent.name == "Content")
                {
                    continue;
                }
                es.SetSelectedGameObject(s.gameObject);
                return;
            }
        }

        // B: close any expanded TMP_Dropdown; return true so B doesn't bubble to panel-close.
        private bool TryCloseExpandedDropdown()
        {
            try
            {
                var content = GetActiveTabContent();
                if (content == null) return false;
                var dropdowns = content.GetComponentsInChildren<TMPro.TMP_Dropdown>(true);
                foreach (var dd in dropdowns)
                {
                    if (dd == null) continue;
                    if (IsDropdownExpanded(dd))
                    {
                        dd.Hide();
                        Log.Info($"OptionsPanelTabCycler: B closed expanded dropdown {dd.gameObject.name}.");
                        return true;
                    }
                }
            }
            catch (Exception e) { Log.Warn($"TryCloseExpandedDropdown: {e.Message}"); }
            return false;
        }

        // m_Dropdown (private) holds the expanded list GO; null = collapsed.
        private static FieldInfo? _tmpDropdownExpandedField;
        private static bool IsDropdownExpanded(TMPro.TMP_Dropdown dd)
        {
            try
            {
                if (_tmpDropdownExpandedField == null)
                {
                    _tmpDropdownExpandedField = typeof(TMPro.TMP_Dropdown).GetField(
                        "m_Dropdown",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                }
                if (_tmpDropdownExpandedField == null) return false;
                var val = _tmpDropdownExpandedField.GetValue(dd) as GameObject;
                return val != null;
            }
            catch { return false; }
        }

        // X: walk focused GO ancestors for ControllerSettingsRowMarker and invoke ResetToDefault.
        private void TryResetFocusedRow()
        {
            var es = EventSystem.current;
            var sel = es != null ? es.currentSelectedGameObject : null;
            if (sel == null) return;
            var cursor = sel.transform;
            while (cursor != null)
            {
                var marker = cursor.GetComponent<ControllerSettingsRowMarker>();
                if (marker != null && marker.ResetToDefault != null)
                {
                    try
                    {
                        marker.ResetToDefault();
                        Log.Info($"OptionsPanelTabCycler: X reset row {cursor.name}.");
                    }
                    catch (Exception e) { Log.Error($"Row reset: {e.Message}"); }
                    return;
                }
                cursor = cursor.parent;
            }
        }

        private bool IsPanelShown()
        {
            if (OptionsPanel == null) return false;
            if (!OptionsPanel.gameObject.activeInHierarchy) return false;
            if (_fadeGroupField == null) return true; // best effort
            try
            {
                var fg = _fadeGroupField.GetValue(OptionsPanel);
                if (fg == null) return true;
                var prop = fg.GetType().GetProperty("IsShown",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop == null) return true;
                var val = prop.GetValue(fg);
                return val is bool b ? b : true;
            }
            catch { return true; }
        }
    }
}
