using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace DuckovController.UI
{
    internal sealed partial class GridFocusController : MonoBehaviour
    {
        private void SetFocus(GameObject? next)
        {
            if (next == _focused) return;
            PointerEventDispatcher.Hover(_focused, next);
            _focused = next;
            RememberSlot(next);   // INV-2: track the logical slot for refresh-restore
            if (next != null)
            {
                var es = EventSystem.current;
                if (es != null) es.SetSelectedGameObject(next);
            }

            _router.OnFocusChanged(next);

            ApplyFocusOutline(next);

            // Warp cursor only during carry (needed for carry preview anchoring).
            // Otherwise keep it parked off-screen (set on view-enter) — warping to slot drags the crosshair and causes hover flash.
            bool carrying = _router.Carry.Current
                != DuckovController.UI.Inventory.InventoryCarryState.Phase.Idle;
            if (carrying && next != null && Mouse.current != null
                && next.transform is RectTransform crt)
            {
                var canvas = crt.GetComponentInParent<Canvas>();
                UnityEngine.Camera? cam = null;
                if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    cam = canvas.worldCamera;
                var screenPos = RectTransformUtility.WorldToScreenPoint(cam, crt.position);
                try { Mouse.current.WarpCursorPosition(screenPos); }
                catch (Exception e) { Log.Debug_($"Mouse warp failed: {e.Message}"); }
            }
            // Hide cursor while controller-driven; warp is for vanilla UI
            // anchoring only, the visual cursor itself is visual noise.
            Cursor.visible = false;
        }

        // Fit the golden focus outline to a target (or hide it). Shared by SetFocus
        // and RefreshFocusOutline.
        private void ApplyFocusOutline(GameObject? target)
        {
            if (Cfg != null && Cfg.Ui.FocusOutlineEnabled && target != null)
            {
                var rt = target.transform as RectTransform;
                if (rt != null && _outlineOverlay != null)
                {
                    if (ColorUtility.TryParseHtmlString(Cfg.Ui.FocusOutlineColorHex, out var col))
                        _outlineOverlay.Show(rt, col, Cfg.Ui.FocusOutlineThicknessPx);
                    else
                        _outlineOverlay.Show(rt, Color.yellow, Cfg.Ui.FocusOutlineThicknessPx);
                }
            }
            else if (_outlineOverlay != null)
            {
                _outlineOverlay.Hide();
            }
        }

        // Re-fit the outline to the CURRENT focus without a focus change — used when
        // the focused element resizes in place (e.g. the op-menu Lock/Unlock label
        // reflows the menu). One-shot; no per-frame cost.
        internal void RefreshFocusOutline() => ApplyFocusOutline(_focused);
    }
}
