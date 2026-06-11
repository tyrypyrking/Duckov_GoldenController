using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace DuckovController.UI
{
    internal static partial class TabSwitcher
    {
        // ViewTabDisplayEntry bar: identify active tab by View.ActiveView.GetType().Name, click the neighbor.
        private static FieldInfo? _viewTypeNameField;
        private static bool TryCycleViewTabs(int direction)
        {
            var active = Duckov.UI.View.ActiveView;
            if (active == null) return false;

            var entries = GetEntries();
            if (entries == null || entries.Count < 2) return false;

            if (_viewTypeNameField == null)
            {
                _viewTypeNameField = typeof(ViewTabDisplayEntry).GetField(
                    "viewTypeName", BindingFlags.Instance | BindingFlags.NonPublic);
                if (_viewTypeNameField == null) return false;
            }

            // Sort left-to-right by screen X.
            var sorted = entries
                .Where(e => e != null && e.isActiveAndEnabled && e.gameObject.activeInHierarchy)
                .OrderBy(e => PointerEventDispatcher.ScreenCenterOf(e.gameObject).x)
                .ToList();
            if (sorted.Count < 2) return false;

            string activeTypeName = active.GetType().Name;
            int currentIdx = -1;
            for (int i = 0; i < sorted.Count; i++)
            {
                var name = _viewTypeNameField.GetValue(sorted[i]) as string;
                if (name == activeTypeName) { currentIdx = i; break; }
            }
            if (currentIdx < 0) currentIdx = 0;

            // Tab bar uses custom IPointerClickHandler (btn=null per diagnostic) — synthesize pointer click.
            // View.Show() reflection kept as last resort.
            int targetIdx = currentIdx + direction;
            if (targetIdx < 0) targetIdx = sorted.Count - 1;
            else if (targetIdx >= sorted.Count) targetIdx = 0;
            if (targetIdx == currentIdx) return false;
            var target = sorted[targetIdx];
            if (target == null) return false;

            // Try the pointer-click chain on the entry GameObject.
            PointerEventDispatcher.Click(target.gameObject);

            // Verify the click actually changed the active view; if not,
            // fall back to the static Show() reflection path.
            if (Duckov.UI.View.ActiveView != null
                && Duckov.UI.View.ActiveView.GetType().Name != activeTypeName)
            {
                return true;
            }
            var nextName = _viewTypeNameField.GetValue(target) as string;
            if (!string.IsNullOrEmpty(nextName))
            {
                var t = FindViewTypeByName(nextName!);
                var show = t?.GetMethod("Show", BindingFlags.Public | BindingFlags.Static);
                if (show != null)
                {
                    try { show.Invoke(null, null); return true; }
                    catch (Exception e) { Log.Debug_($"View.Show({nextName}) threw: {e.Message}"); }
                }
            }
            return false;
        }

        // Entry component is often a child indicator of the real clickable button — check self, parent, then children.
        private static Button? FindTabButton(GameObject entryGo)
        {
            var local = entryGo.GetComponent<Button>();
            if (local != null) return local;
            var parent = entryGo.GetComponentInParent<Button>();
            if (parent != null) return parent;
            return entryGo.GetComponentInChildren<Button>(includeInactive: false);
        }

        private static Type? FindViewTypeByName(string typeName)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var t in a.GetTypes())
                    {
                        if (t.Name == typeName) return t;
                    }
                }
                catch { /* GetTypes can throw on partially-loaded asms */ }
            }
            return null;
        }

        // Shift focus in the largest horizontal Button row (or snap into it if focus is outside).
        private static bool TryStepHorizontalRow(GameObject scopeRoot, int direction)
        {
            var buttons = scopeRoot.GetComponentsInChildren<Button>(includeInactive: false);
            if (buttons.Length < 2) return false;
            // Bucket by screen Y / 5 to tolerate ~5px baseline drift.
            var byRow = new Dictionary<int, List<Button>>();
            foreach (var b in buttons)
            {
                if (b == null || !b.IsInteractable() || !b.isActiveAndEnabled) continue;
                var y = (int)Mathf.Round(PointerEventDispatcher.ScreenCenterOf(b.gameObject).y / 5f);
                if (!byRow.TryGetValue(y, out var list))
                {
                    list = new List<Button>();
                    byRow[y] = list;
                }
                list.Add(b);
            }
            // Pick the row with the most buttons; tiebreak by topmost Y.
            List<Button>? bestRow = null;
            int bestRowY = int.MinValue;
            foreach (var kv in byRow)
            {
                if (kv.Value.Count < 2) continue;
                if (bestRow == null || kv.Value.Count > bestRow.Count
                    || (kv.Value.Count == bestRow.Count && kv.Key > bestRowY))
                {
                    bestRow = kv.Value;
                    bestRowY = kv.Key;
                }
            }
            if (bestRow == null) return false;
            bestRow.Sort((a, b) =>
                PointerEventDispatcher.ScreenCenterOf(a.gameObject).x
                    .CompareTo(PointerEventDispatcher.ScreenCenterOf(b.gameObject).x));

            // Where is current focus relative to the row?
            var es = UnityEngine.EventSystems.EventSystem.current;
            GameObject? current = es?.currentSelectedGameObject;
            int idx = -1;
            for (int i = 0; i < bestRow.Count; i++)
            {
                if (bestRow[i] != null && bestRow[i].gameObject == current) { idx = i; break; }
            }
            int nextIdx;
            if (idx < 0)
            {
                // Focus is elsewhere — snap into the row.
                nextIdx = direction > 0 ? 0 : bestRow.Count - 1;
            }
            else
            {
                nextIdx = idx + direction;
                if (nextIdx < 0) nextIdx = bestRow.Count - 1;
                else if (nextIdx >= bestRow.Count) nextIdx = 0;
            }
            var next = bestRow[nextIdx];
            if (next == null) return false;
            PointerEventDispatcher.Hover(current, next.gameObject);
            if (es != null) es.SetSelectedGameObject(next.gameObject);
            return true;
        }

        private static bool TryCycleSelectionMenu(GameObject scopeRoot, int direction)
        {
            var menus = FindActiveSelectionMenusUnder(scopeRoot);
            if (menus.Count == 0) return false;

            // Prefer topmost tab strip (highest screen-Y); fall back to first.
            MonoBehaviour? chosen = null;
            float bestY = float.NegativeInfinity;
            foreach (var menu in menus)
            {
                var shape = ResolveShape(menu.GetType());
                if (!shape.Valid) continue;
                var buttons = shape.ButtonsField!.GetValue(menu) as IList;
                if (buttons == null || buttons.Count < 2) continue;
                var first = buttons[0] as MonoBehaviour;
                if (first == null) continue;
                var y = PointerEventDispatcher.ScreenCenterOf(first.gameObject).y;
                if (y > bestY) { bestY = y; chosen = menu; }
            }
            if (chosen == null) return false;
            return CycleOne(chosen, direction);
        }

        // ToggleGroup tab cycling: find largest active group, tick neighbor. Works for POI-filter-style horizontal Toggle rows.
        private static bool TryCycleToggleGroup(GameObject scopeRoot, int direction)
        {
            var groups = scopeRoot.GetComponentsInChildren<ToggleGroup>(includeInactive: false);
            ToggleGroup? bestGroup = null;
            List<Toggle>? bestList = null;
            float bestY = float.NegativeInfinity;
            foreach (var group in groups)
            {
                if (group == null || !group.isActiveAndEnabled) continue;
                // Toggle.group references this group; gather all of them.
                var allToggles = group.GetComponentsInChildren<Toggle>(includeInactive: false);
                var members = new List<Toggle>();
                foreach (var t in allToggles)
                {
                    if (t == null) continue;
                    if (!t.IsInteractable() || !t.isActiveAndEnabled) continue;
                    if (t.group != group) continue;
                    members.Add(t);
                }
                if (members.Count < 2) continue;
                // Sort horizontally so left/right cycling matches visual order.
                members.Sort((a, b) =>
                    PointerEventDispatcher.ScreenCenterOf(a.gameObject).x
                        .CompareTo(PointerEventDispatcher.ScreenCenterOf(b.gameObject).x));
                var topY = PointerEventDispatcher.ScreenCenterOf(members[0].gameObject).y;
                if (topY > bestY) { bestY = topY; bestGroup = group; bestList = members; }
            }
            if (bestGroup == null || bestList == null) return false;
            // Find current.
            int idx = -1;
            for (int i = 0; i < bestList.Count; i++)
            {
                if (bestList[i].isOn) { idx = i; break; }
            }
            if (idx < 0) idx = 0;
            int next = idx + direction;
            if (next < 0) next = bestList.Count - 1;
            else if (next >= bestList.Count) next = 0;
            var toggle = bestList[next];
            if (toggle == null) return false;
            try
            {
                toggle.isOn = true;
                PointerEventDispatcher.Hover(bestList[idx]?.gameObject, toggle.gameObject);
                return true;
            }
            catch (Exception e)
            {
                Log.Debug_("ToggleGroup cycle threw: " + e.Message);
                return false;
            }
        }
    }
}
