using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DuckovController.UI.Menu
{
    // Column builders, initial-focus pick, hover/selection clearing, and
    // difficulty-card hover-indicator management.
    internal sealed partial class MenuFocusOverlay : MonoBehaviour
    {
        // Vertical button column sorted by world-Y descending. X-cluster filter drops off-axis
        // interactables (e.g. Steam wishlist button) not part of the main menu column.
        private List<Selectable> BuildVerticalColumn()
        {
            // ModManagerUI: 4 buttons/row — generic X-cluster picks wrong stack. Use row-only column.
            if (IsInsideModManager()) return BuildModManagerColumn();
            // DifficultySelection: horizontal cards + Confirm below.
            if (IsInsideDifficultySelection()) return BuildDifficultyColumn();
            // CharacterCreator: 7 tabs + 3 extras + 2 actions.
            if (IsCharacterCreatorActive()) return BuildCharacterCreatorTopColumn();

            var result = new List<Selectable>();
            var scope = _effectiveRoot ?? _menuRoot;
            if (scope == null) return result;

            // Enumerate all Selectable (not just Button) so dropdown popups (Toggle items)
            // and tab content (TMP_Dropdown/Slider) appear. X-cluster drops off-axis ones.
            var pool = new List<(Selectable s, float cx, float cy)>();
            var candidatesAll = scope.GetComponentsInChildren<Selectable>(includeInactive: false);
            var candidates = new List<Selectable>(candidatesAll.Length);
            foreach (var c in candidatesAll)
            {
                if (c is Scrollbar) continue; // skip scroll-bar handles
                if (c is TMPro.TMP_InputField) continue; // no keyboard on controller
                if (c is UnityEngine.UI.InputField) continue;
                candidates.Add(c);
            }
            var corners = new Vector3[4];
            foreach (var b in candidates)
            {
                if (!IsSelectableUsable(b)) continue;
                var rt = b.transform as RectTransform;
                if (rt == null) continue;
                rt.GetWorldCorners(corners);   // 0=BL,1=TL,2=TR,3=BR
                float cx = (corners[0].x + corners[2].x) * 0.5f;
                float cy = (corners[0].y + corners[2].y) * 0.5f;
                pool.Add((b, cx, cy));
            }
            if (pool.Count == 0) return result;
            if (pool.Count == 1) { result.Add(pool[0].s); return result; }

            // Cluster by X; tolerance = half the widest button (off-axis buttons sit well beyond).
            float widest = 0f;
            foreach (var p in pool)
            {
                var rt = p.s.transform as RectTransform;
                if (rt != null)
                {
                    rt.GetWorldCorners(corners);
                    float w = corners[2].x - corners[0].x;
                    if (w > widest) widest = w;
                }
            }
            float tolerance = Mathf.Max(60f, widest * 0.5f);

            var clusters = new List<List<(Selectable s, float cx, float cy)>>();
            foreach (var p in pool)
            {
                List<(Selectable s, float cx, float cy)>? target = null;
                foreach (var cluster in clusters)
                {
                    if (Mathf.Abs(cluster[0].cx - p.cx) <= tolerance) { target = cluster; break; }
                }
                if (target == null) { target = new(); clusters.Add(target); }
                target.Add(p);
            }
            List<(Selectable s, float cx, float cy)>? biggest = null;
            foreach (var c in clusters)
                if (biggest == null || c.Count > biggest.Count) biggest = c;
            if (biggest == null) return result;

            biggest.Sort((a, b) => b.cy.CompareTo(a.cy));
            foreach (var p in biggest) result.Add(p.s);
            return result;
        }

        // ModManagerUI column: OperationLayout/Button toggle per row + Return at end.
        // Entry(Clone) hierarchy: ModManagerUI/…/Content/Entry(Clone)/OperationLayout/Button
        private List<Selectable> BuildModManagerColumn()
        {
            var result = new List<Selectable>();
            var scope = _effectiveRoot ?? _menuRoot;
            if (scope == null) return result;

            var modRoot = FindAncestorByName(scope, "ModManagerUI") ?? scope;
            foreach (var t in modRoot.GetComponentsInChildren<Transform>(includeInactive: false))
            {
                if (t == null) continue;
                if (!t.name.StartsWith("Entry(Clone)")) continue;
                var opBtnT = t.Find("OperationLayout/Button");
                if (opBtnT == null) continue;
                var btn = opBtnT.GetComponent<Button>();
                if (btn != null && IsSelectableUsable(btn)) result.Add(btn);
            }

            result.Sort((a, b) => b.transform.position.y.CompareTo(a.transform.position.y));

            // Return appended last so shoulder-cycling off the last row reaches the back button.
            var retT = modRoot.Find("Return");
            if (retT != null)
            {
                var retBtn = retT.GetComponent<Button>();
                if (retBtn != null && IsSelectableUsable(retBtn)) result.Add(retBtn);
            }
            return result;
        }

        // DifficultySelection column: cards (sorted L→R) + Confirm below.
        // Cards are DifficultySelection_Entry / ButtonAnimation — not Selectable —
        // so we add a passive Button if absent so ExecuteEvents can reach them.
        private List<Selectable> BuildDifficultyColumn()
        {
            var result = new List<Selectable>();
            var ds = FindAncestorByName(_effectiveRoot, "DifficultySelection") ?? _menuRoot;
            if (ds == null) return result;
            var options = FindDescendantByName(ds, "Options");
            if (options != null)
            {
                var cards = new List<Selectable>();
                for (int i = 0; i < options.childCount; i++)
                {
                    var c = options.GetChild(i);
                    if (c == null || !c.gameObject.activeInHierarchy) continue;
                    if (!c.name.StartsWith("DifficultyEntry")) continue;
                    var s = c.GetComponent<Selectable>();
                    if (s == null)
                    {
                        s = c.gameObject.AddComponent<Button>();
                        s.transition = Selectable.Transition.None;
                    }
                    if (IsSelectableUsable(s)) cards.Add(s);
                }
                cards.Sort((a, b) => a.transform.position.x.CompareTo(b.transform.position.x));
                result.AddRange(cards);
            }
            // Confirm at the bottom of the panel.
            var confirmT = FindDescendantByName(ds, "Confirm");
            if (confirmT != null)
            {
                var b = confirmT.GetComponent<Button>();
                if (b != null && IsSelectableUsable(b)) result.Add(b);
            }
            return result;
        }

        // Initial focus: (1) EventSystem.currentSelectedGameObject if usable under root,
        // (2) topmost usable non-back Selectable (ties: leftmost).
        private Selectable? PickInitialFocusInRoot()
        {
            var scope = _effectiveRoot ?? _menuRoot;
            if (scope == null) return null;

            // ModManagerUI: generic pick lands on OrderUp/OrderDown cluster. Use row column.
            if (IsInsideModManager())
            {
                var col = BuildModManagerColumn();
                if (col.Count > 0) return col[0];
            }
            // CharacterCreator: land on Head tab.
            if (IsCharacterCreatorActive())
            {
                var col = BuildCharacterCreatorTopColumn();
                if (col.Count > 0) return col[0];
            }
            // DifficultySelection: prefer game's SelectionIndicator-active card; fall back to leftmost.
            if (IsInsideDifficultySelection())
            {
                var col = BuildDifficultyColumn();
                if (col.Count > 0)
                {
                    foreach (var s in col)
                    {
                        var indicator = s.transform.Find("Content/SelectionIndicator");
                        if (indicator != null && indicator.gameObject.activeInHierarchy)
                            return s;
                    }
                    return col[0];
                }
            }

            var es = EventSystem.current;
            if (es != null)
            {
                var current = es.currentSelectedGameObject;
                if (current != null)
                {
                    var s = current.GetComponent<Selectable>();
                    if (IsSelectableUsable(s) && s != null && s.transform.IsChildOf(scope) && !IsBackButton(s))
                        return s;
                }
            }

            // Enumerate Selectable (not just Button): dropdowns use Toggle, tabs use TMP_Dropdown/Slider.
            // Back-button names excluded: Credits has only a back button (null = no chevron, correct);
            // SaveSlots back is first but user wants the save list; B-cancel still works via HandleCancel.
            Selectable? best = null;
            float bestY = float.NegativeInfinity;
            float bestX = float.PositiveInfinity;
            var candidates = scope.GetComponentsInChildren<Selectable>(includeInactive: false);
            foreach (var b in candidates)
            {
                if (!IsSelectableUsable(b)) continue;
                if (b is Scrollbar) continue; // never target scroll-bar handles
                if (b is TMPro.TMP_InputField) continue; // no keyboard on controller
                if (b is UnityEngine.UI.InputField) continue;
                if (IsBackButton(b)) continue; // skip back/return/close/cancel
                var pos = b.transform.position;
                if (pos.y > bestY + 0.01f || (Mathf.Abs(pos.y - bestY) < 0.01f && pos.x < bestX))
                {
                    best = b;
                    bestY = pos.y;
                    bestX = pos.x;
                }
            }
            return best;
        }

        // Same name conventions as BackButtonNames in MenuFocusOverlay.Input.cs.
        private static bool IsBackButton(Selectable s)
        {
            if (s == null) return false;
            string n = s.gameObject.name.ToLowerInvariant();
            foreach (var key in BackButtonNames)
            {
                if (n == key) return true;
                if (n.StartsWith(key + "_")) return true;
                if (n.StartsWith(key + " ")) return true;
            }
            return false;
        }

        // Clears EventSystem selection and fires pointer-exit so vanilla white-tint/Hovering
        // stops on buttons we've moved away from. Called on every _focused change.
        private static void ClearVanillaHoverAndSelection(Selectable? previous)
        {
            try
            {
                var es = EventSystem.current;
                if (es != null) es.SetSelectedGameObject(null);
                if (previous != null && previous.gameObject != null)
                    DuckovController.UI.PointerEventDispatcher.Hover(previous.gameObject, null);
            }
            catch (System.Exception e)
            {
                Log.Warn($"MenuOverlay ClearVanillaHover failed: {e.Message}");
            }
        }

        private static bool IsDifficultyCard(Transform? t)
            => t != null && t.name.StartsWith("DifficultyEntry");

        private void ActivateHoverIndicator(Transform card)
        {
            var hovering = card.Find("Content/HoveringIndicator");
            if (hovering == null) { ClearActiveHoverIndicator(); return; }
            if (ReferenceEquals(hovering, _activeHoverIndicator) && hovering.gameObject.activeSelf) return;
            ClearActiveHoverIndicator();
            hovering.gameObject.SetActive(true);
            _activeHoverIndicator = hovering;
        }

        private void ClearActiveHoverIndicator()
        {
            if (_activeHoverIndicator == null) return;
            try { _activeHoverIndicator.gameObject.SetActive(false); }
            catch { /* destroyed */ }
            _activeHoverIndicator = null;
        }
    }
}
