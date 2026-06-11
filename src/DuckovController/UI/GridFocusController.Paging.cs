using System;
using UnityEngine;

namespace DuckovController.UI
{
    internal sealed partial class GridFocusController : MonoBehaviour
    {
        private bool TryAdvancePage(Duckov.UI.InventoryDisplay display, NavDir dir, out GameObject? newFocus)
        {
            newFocus = null;
            try
            {
                // Capture X before flip so deferred-focus lands in the same column on the new page.
                var focusedRt = _focused?.transform as RectTransform;
                _pendingPagedFocusX = focusedRt != null ? focusedRt.anchoredPosition.x : 0f;
                _pendingPagedFocusAt = Time.unscaledTime;
                _pendingPagedFocusFrame = Time.frameCount;

                // Hide outline during settle to avoid flickering to CharBag.
                if (_outlineOverlay != null) _outlineOverlay.Hide();

                int pageBefore = display.SelectedPage;
                if (dir == NavDir.Down)
                {
                    if (display.SelectedPage >= display.MaxPage) return false;
                    display.NextPage();
                }
                else
                {
                    if (display.SelectedPage <= 0) return false;
                    display.PreviousPage();
                }

                // No-op guard: if page didn't change the method did nothing.
                if (display.SelectedPage == pageBefore) return false;

                // Defer focus to frame after graph rebuilds (async re-pool/re-fill).
                _graphDirty = true;
                _pendingPagedFocus = true;
                _pendingPagedFocusKind = DuckovController.UI.Inventory.PaneRegistry.Kind.Target;
                _pendingPagedFocusIsBottom = (dir == NavDir.Up);
                newFocus = null; // SetFocus deferred to next-frame rebuild
                return true;
            }
            catch (Exception e)
            {
                Log.Warn($"GridFocusController.TryAdvancePage: {e.Message}");
                return false;
            }
        }

        // Find entry in top/bottom extreme row nearest targetX (two-pass: extreme Y, then nearest X in that row).
        private static GameObject? FindEntryAtColumnRow(GameObject root, float targetX, bool wantBottomRow)
        {
            var entries = root.GetComponentsInChildren<Duckov.UI.InventoryEntry>(includeInactive: false);
            GameObject? best = null;
            float bestY = wantBottomRow ? float.PositiveInfinity : float.NegativeInfinity;
            float bestXDelta = float.PositiveInfinity;

            foreach (var e in entries)
            {
                if (e == null) continue;
                var inv = e.Master?.Target;
                if (inv == null || e.Index >= inv.Capacity) continue;
                var rt = e.transform as RectTransform;
                if (rt == null) continue;

                float y = rt.anchoredPosition.y;
                bool isNewExtremeRow = wantBottomRow ? y < bestY : y > bestY;
                if (isNewExtremeRow)
                {
                    bestY = y;
                    bestXDelta = Mathf.Abs(rt.anchoredPosition.x - targetX);
                    best = e.gameObject;
                }
                else if (Mathf.Abs(y - bestY) < 25f)
                {
                    float dx = Mathf.Abs(rt.anchoredPosition.x - targetX);
                    if (dx < bestXDelta) { bestXDelta = dx; best = e.gameObject; }
                }
            }
            return best;
        }
    }
}
