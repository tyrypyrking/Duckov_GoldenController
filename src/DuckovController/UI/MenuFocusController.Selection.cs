using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DuckovController.UI
{
    internal sealed partial class MenuFocusController
    {
        // Auto-select first interactable Selectable so the game's UI input module can nav. Never overrides active selection.
        private void TryEnsureSelection()
        {
            // GridFocusController focused nodes are non-Selectable (InventoryEntry etc.) — scan would replace them.
            if (GridFocusController.Instance?.IsHandlingActiveView() == true) return;
            // MenuFocusOverlay avoids ES selection intentionally; TryEnsureSelection would put it back every 0.16s.
            if (DuckovController.UI.Menu.MenuFocusOverlay.Instance?.IsActive == true) return;

            var es = EventSystem.current;
            if (es == null) return;
            var current = es.currentSelectedGameObject;
            if (current != null && current.activeInHierarchy)
            {
                _lastEnforcedSelection = current;
                return;
            }
            var now = Time.unscaledTime;
            if (now - _lastSelectionScanTime < SelectionScanIntervalSec) return;
            _lastSelectionScanTime = now;

            var first = FindFirstInteractableSelectable();
            if (first == null) return;
            if (first == _lastEnforcedSelection && current == null)
            {
                // Avoid thrashing: if we just set this and it didn't stick, back off.
                return;
            }
            ChangeFocus(current, first.gameObject);
        }

        private void StepFocus(Vector2 v)
        {
            var es = EventSystem.current;
            if (es == null) return;
            var current = es.currentSelectedGameObject;
            if (current == null) return;
            var sel = current.GetComponent<Selectable>();
            if (sel == null) return;

            // Navigation graph first; returns null for Navigation=None (most menus were mouse-designed).
            Selectable? next;
            if (Mathf.Abs(v.x) > Mathf.Abs(v.y))
            {
                next = v.x > 0 ? sel.FindSelectableOnRight() : sel.FindSelectableOnLeft();
            }
            else
            {
                next = v.y > 0 ? sel.FindSelectableOnUp() : sel.FindSelectableOnDown();
            }

            // Fallback: screen-space cardinal cone over all interactable
            // Selectables in the scene. Works regardless of how Navigation is
            // configured.
            if (next == null)
            {
                next = FindSelectableCardinal(current, v);
            }

            if (next != null)
            {
                ChangeFocus(current, next.gameObject);
                DuckovController.Haptics.HapticEngine.Instance?.Play(DuckovController.Haptics.HapticCue.FocusTick);
                Log.Debug_("Haptic: FocusTick (menu)");
            }
        }

        // Sets ES selection + synthesizes pointerEnter/Exit: game buttons style from hover events, not Selectable.Selected.
        private void ChangeFocus(GameObject? previous, GameObject? next)
        {
            var es = EventSystem.current;
            if (es == null) return;
            PointerEventDispatcher.Hover(previous, next);
            if (next != null)
            {
                es.SetSelectedGameObject(next);
                _lastEnforcedSelection = next;
            }
        }

        // Cardinal-cone: parallel distance + perpendicular penalty, same heuristic as FocusGraph.
        private static Selectable? FindSelectableCardinal(GameObject from, Vector2 dir)
        {
            var snapshot = Selectable.allSelectablesArray;
            if (snapshot == null || snapshot.Length == 0) return null;
            var fromPos = PointerEventDispatcher.ScreenCenterOf(from);
            Vector2 axis = Mathf.Abs(dir.x) > Mathf.Abs(dir.y)
                ? (dir.x > 0 ? Vector2.right : Vector2.left)
                : (dir.y > 0 ? Vector2.up : Vector2.down);
            Selectable? best = null;
            float bestScore = float.PositiveInfinity;
            for (int i = 0; i < snapshot.Length; i++)
            {
                var s = snapshot[i];
                if (s == null) continue;
                if (s.gameObject == from) continue;
                if (!s.IsInteractable()) continue;
                if (!s.isActiveAndEnabled) continue;
                var toPos = PointerEventDispatcher.ScreenCenterOf(s.gameObject);
                var delta = toPos - fromPos;
                var alongAxis = Vector2.Dot(delta, axis);
                if (alongAxis <= 1f) continue;  // not in the requested cone
                var perp = delta - axis * alongAxis;
                var score = alongAxis + 2.0f * Mathf.Abs(perp.x + perp.y);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = s;
                }
            }
            return best;
        }

        private static Selectable? FindFirstInteractableSelectable()
        {
            // allSelectablesArray: cheaper than FindObjectsOfType for repeated scans.
            var snapshot = Selectable.allSelectablesArray;
            // Prefer Button/Toggle over Slider/Scrollbar/Dropdown (fiddlers intercept left/right and steal focus).
            Selectable? bestPrimary = null;
            Selectable? bestFiddler = null;
            float bestPrimaryY = float.NegativeInfinity;
            float bestPrimaryX = float.PositiveInfinity;
            float bestFiddlerY = float.NegativeInfinity;
            float bestFiddlerX = float.PositiveInfinity;
            for (int i = 0; i < snapshot.Length; i++)
            {
                var s = snapshot[i];
                if (s == null) continue;
                if (!s.IsInteractable()) continue;
                if (!s.isActiveAndEnabled) continue;
                var rt = s.transform as RectTransform;
                if (rt == null) continue;
                var center = PointerEventDispatcher.ScreenCenterOf(s.gameObject);
                bool fiddler = s is Slider or Scrollbar or Dropdown;
                if (fiddler)
                {
                    if (center.y > bestFiddlerY + 1f ||
                        (Mathf.Abs(center.y - bestFiddlerY) <= 1f && center.x < bestFiddlerX))
                    {
                        bestFiddler = s;
                        bestFiddlerY = center.y;
                        bestFiddlerX = center.x;
                    }
                }
                else
                {
                    if (center.y > bestPrimaryY + 1f ||
                        (Mathf.Abs(center.y - bestPrimaryY) <= 1f && center.x < bestPrimaryX))
                    {
                        bestPrimary = s;
                        bestPrimaryY = center.y;
                        bestPrimaryX = center.x;
                    }
                }
            }
            return bestPrimary ?? bestFiddler;
        }
    }
}
