using System;
using System.Reflection;
using Duckov.UI.Animations;
using UnityEngine;
using UnityEngine.UI;

namespace DuckovController.UI.Menu
{
    // Effective-root resolution, dropdown-popup detection, scope queries,
    // and static hierarchy helpers.
    internal sealed partial class MenuFocusOverlay : MonoBehaviour
    {
        // TMP_Dropdown.m_Dropdown — private field pointing at the expanded popup GameObject.
        private static FieldInfo? _tmpDropdownPopupField;

        // Deepest active FadeGroup sub-panel with an interactable Button, or _menuRoot.
        // Lets focus follow into Settings/Options/Credits when they open over the main menu.
        private Transform? ResolveEffectiveRoot()
        {
            if (_menuRoot == null) return null;

            // Expanded TMP_Dropdown: scope to its popup for column + dpad nav.
            var expandedPopup = FindExpandedDropdownPopup(_menuRoot);
            if (expandedPopup != null) return expandedPopup;

            // OptionsPanel open: drill into active tab content. Without this, column is dominated
            // by the tab strip + back button instead of the tab's setting rows.
            var optionsContent = FindOptionsScrollViewContent(_menuRoot);
            if (optionsContent != null)
            {
                var activeTab = FindActiveDirectChild(optionsContent);
                if (activeTab != null) return activeTab;
            }

            Transform? best = null;
            int bestDepth = 0;
            FadeGroup[] fgs;
            try { fgs = _menuRoot.GetComponentsInChildren<FadeGroup>(includeInactive: false); }
            catch { return _menuRoot; }
            foreach (var fg in fgs)
            {
                if (fg == null) continue;
                if (ReferenceEquals(fg.transform, _menuRoot)) continue;
                if (!fg.gameObject.activeInHierarchy) continue;
                if (!fg.IsShown) continue;

                bool hasBtn = false;
                foreach (var b in fg.transform.GetComponentsInChildren<Button>(includeInactive: false))
                {
                    if (IsSelectableUsable(b)) { hasBtn = true; break; }
                }
                if (!hasBtn) continue;

                int depth = 0;
                var t = fg.transform;
                while (t != null && !ReferenceEquals(t, _menuRoot)) { depth++; t = t.parent; }
                if (depth > bestDepth) { bestDepth = depth; best = fg.transform; }
            }
            return best ?? _menuRoot;
        }

        // OptionsPanel/ScrollView/Viewport/Content node under menuRoot, or null.
        private static Transform? FindOptionsScrollViewContent(Transform menuRoot)
        {
            var optionsPanel = FindDescendantByName(menuRoot, "OptionsPanel");
            if (optionsPanel == null) return null;
            if (!optionsPanel.gameObject.activeInHierarchy) return null;
            var scroll = FindDescendantByName(optionsPanel, "ScrollView");
            if (scroll == null) return null;
            var viewport = FindDescendantByName(scroll, "Viewport");
            if (viewport == null) return null;
            var content = FindDescendantByName(viewport, "Content");
            return content;
        }

        // First active direct child (OptionsPanel tab system: exactly one tab active at a time).
        private static Transform? FindActiveDirectChild(Transform parent)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (c.gameObject.activeSelf) return c;
            }
            return null;
        }

        // True when scope is OptionsPanel/ScrollView/Viewport/Content/<TabName>.
        private static bool IsOptionsTabContent(Transform scope)
        {
            var p1 = scope.parent;        // Content
            if (p1 == null || p1.name != "Content") return false;
            var p2 = p1.parent;           // Viewport
            if (p2 == null || p2.name != "Viewport") return false;
            var p3 = p2.parent;           // ScrollView
            if (p3 == null || p3.name != "ScrollView") return false;
            var p4 = p3.parent;           // OptionsPanel
            if (p4 == null || p4.name != "OptionsPanel") return false;
            return true;
        }

        private bool IsInsideModManager()
            => _effectiveRoot != null && FindAncestorByName(_effectiveRoot, "ModManagerUI") != null;

        private bool IsInsideCreditsPanel()
            => _effectiveRoot != null && FindAncestorByName(_effectiveRoot, "Credits") != null;

        private bool IsInsideDifficultySelection()
            => _effectiveRoot != null
               && FindAncestorByName(_effectiveRoot, "DifficultySelection") != null
               && FindAncestorByName(_effectiveRoot, "CustomDifficultyPanel") == null;

        private bool IsInsideCustomDifficultyPanel()
            => _effectiveRoot != null && FindAncestorByName(_effectiveRoot, "CustomDifficultyPanel") != null;

        // Scan root Canvases for a known panel name. CC wins by component (not name).
        private static Transform? TryFindGenericPanelRoot()
        {
            var ccType = GetCustomFaceTabsType();
            if (ccType != null)
            {
                MonoBehaviour? any;
                try { any = UnityEngine.Object.FindObjectOfType(ccType) as MonoBehaviour; }
                catch { any = null; }
                if (any != null && any.gameObject.activeInHierarchy)
                {
                    var t = any.transform;
                    while (t != null && t.GetComponent<Canvas>() == null) t = t.parent;
                    if (t != null) return t;
                }
            }

            Canvas[]? canvases;
            try { canvases = UnityEngine.Object.FindObjectsOfType<Canvas>(); }
            catch { return null; }
            if (canvases == null) return null;
            foreach (var c in canvases)
            {
                if (c == null || !c.isRootCanvas || !c.gameObject.activeInHierarchy) continue;
                foreach (var name in GenericPanelNames)
                {
                    var t = FindDescendantByName(c.transform, name);
                    if (t != null && t.gameObject.activeInHierarchy) return t;
                }
            }
            return null;
        }

        private static Transform? FindMostPopulatedMenuCanvas()
        {
            return FindMostPopulatedMenuCanvas(out _, out _);
        }

        private static Transform? FindMostPopulatedMenuCanvas(out int canvasesSeen, out int totalInteractableButtons)
        {
            canvasesSeen = 0;
            totalInteractableButtons = 0;
            Canvas[]? canvases;
            try { canvases = UnityEngine.Object.FindObjectsOfType<Canvas>(); }
            catch { return null; }
            if (canvases == null) return null;

            Canvas? best = null;
            int bestCount = 0;
            foreach (var c in canvases)
            {
                if (c == null || !c.isRootCanvas || !c.gameObject.activeInHierarchy) continue;
                canvasesSeen++;
                int n = 0;
                var btns = c.GetComponentsInChildren<Button>(includeInactive: false);
                foreach (var b in btns)
                {
                    if (b == null) continue;
                    if (!b.interactable) continue;
                    if (!b.gameObject.activeInHierarchy) continue;
                    var rt = b.transform as RectTransform;
                    if (rt == null || rt.rect.width <= 1f || rt.rect.height <= 1f) continue;
                    n++;
                }
                totalInteractableButtons += n;
                if (n > bestCount) { bestCount = n; best = c; }
            }
            return best != null ? best.transform : null;
        }

        // DeleteData uses IPointerDown/Up hold-progress — discrete Click is ignored.
        private static bool IsHoldConfirmButton(GameObject go)
            => go != null && go.name == "DeleteData";

        private static Transform? FindDescendantByName(Transform root, string name)
            => TransformHelpers.FindDescendantByName(root, name);

        private static Transform? FindAncestorByName(Transform? t, string name)
        {
            while (t != null) { if (t.name == name) return t; t = t.parent; }
            return null;
        }

        // Calls Hide() on the first expanded TMP_Dropdown under root; used by HandleCancel.
        private static bool TryCloseAnyExpandedDropdown(Transform root)
        {
            try
            {
                var dropdowns = root.GetComponentsInChildren<TMPro.TMP_Dropdown>(includeInactive: false);
                foreach (var dd in dropdowns)
                {
                    if (dd == null) continue;
                    if (FindExpandedDropdownPopup(dd.transform) != null)
                    {
                        dd.Hide();
                        return true;
                    }
                }
            }
            catch { /* tolerated */ }
            return false;
        }

        // Expanded popup transform of the first open TMP_Dropdown under root, or null.
        private static Transform? FindExpandedDropdownPopup(Transform root)
        {
            try
            {
                if (_tmpDropdownPopupField == null)
                {
                    _tmpDropdownPopupField = typeof(TMPro.TMP_Dropdown).GetField(
                        "m_Dropdown",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                }
                if (_tmpDropdownPopupField == null) return null;
                var dropdowns = root.GetComponentsInChildren<TMPro.TMP_Dropdown>(includeInactive: false);
                foreach (var dd in dropdowns)
                {
                    if (dd == null) continue;
                    if (_tmpDropdownPopupField.GetValue(dd) is GameObject popup
                        && popup != null && popup.activeInHierarchy)
                    {
                        return popup.transform;
                    }
                }
            }
            catch (Exception e) { Log.Debug_($"FindExpandedDropdownPopup: {e.Message}"); }
            return null;
        }
    }
}
