using UnityEngine;
using UnityEngine.EventSystems;

namespace DuckovController.UI
{
    // Synthesizes pointer events (IPointerEnter / IPointerClick / IDrag) without warping a real cursor.
    // Click: Down → Up → Click (matching PointerInputModule). Resolves ItemDisplay child for InventoryEntry clicks.
    internal static class PointerEventDispatcher
    {
        private static PointerEventData? _ped;
        private static EventSystem? _pedSystem;

        private static PointerEventData Fresh(GameObject pointerEnter)
        {
            var current = EventSystem.current;
            if (_ped == null || _pedSystem != current)
            {
                _ped = new PointerEventData(current);
                _pedSystem = current;
            }
            // Reset fields that callers shouldn't inherit from previous use.
            _ped.button = PointerEventData.InputButton.Left;
            _ped.clickCount = 0;
            _ped.clickTime = 0f;
            _ped.useDragThreshold = true;
            _ped.dragging = false;
            _ped.pointerDrag = null;
            _ped.pointerPress = null;
            _ped.pointerEnter = pointerEnter;
            return _ped;
        }

        internal static Vector2 ScreenCenterOf(GameObject? go)
        {
            if (go == null) return Vector2.zero;
            var rt = go.transform as RectTransform;
            if (rt == null) return Vector2.zero;
            var cam = FindCanvasCamera(rt);
            var world = (Vector3)rt.position;
            return RectTransformUtility.WorldToScreenPoint(cam, world);
        }

        private static Camera? FindCanvasCamera(RectTransform rt)
        {
            var canvas = rt.GetComponentInParent<Canvas>();
            if (canvas == null) return null;
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
            return canvas.worldCamera;
        }

        internal static void Hover(GameObject? previous, GameObject? next)
        {
            if (previous == next) return;
            // Periodically prune the per-node caches so destroyed pooled
            // entries don't accumulate. Cheap because the caches are small.
            if (_clickTargetCache.Count > 64) { _clickTargetCache.Clear(); _hoverChildCache.Clear(); }
            var ped = Fresh(next ?? previous!);
            if (previous != null)
            {
                ExecuteEvents.Execute(previous, ped, ExecuteEvents.pointerExitHandler);
                // Mirror the exit onto the ItemDisplay child (see enter below).
                var prevChild = ResolveHoverChild(previous);
                if (prevChild != null)
                    ExecuteEvents.Execute(prevChild, ped, ExecuteEvents.pointerExitHandler);
            }
            if (next != null)
            {
                ped.position = ScreenCenterOf(next);
                ExecuteEvents.Execute(next, ped, ExecuteEvents.pointerEnterHandler);
                // The focus node (e.g. InventoryEntry) is what we navigate, but the
                // item-info panel is raised by ItemDisplay.OnPointerEnter on a CHILD
                // GameObject — ExecuteEvents.Execute doesn't recurse, so the node enter
                // alone (which only sets InventoryEntry.hovering) never shows the panel.
                // The real OS cursor used to enter the child directly; it's now parked
                // off-slot, so deliver the synthetic enter to the child too.
                var nextChild = ResolveHoverChild(next);
                if (nextChild != null)
                {
                    ped.position = ScreenCenterOf(nextChild);
                    ExecuteEvents.Execute(nextChild, ped, ExecuteEvents.pointerEnterHandler);
                }
            }
        }

        // Child GameObject (e.g. InventoryEntry's ItemDisplay) that owns the real
        // IPointerEnterHandler raising the hover panel, when distinct from the focus
        // node. Null for nodes that handle enter on themselves (menu Button/Toggle).
        private static readonly System.Collections.Generic.Dictionary<int, GameObject?> _hoverChildCache = new();

        private static GameObject? ResolveHoverChild(GameObject node)
        {
            var id = node.GetInstanceID();
            if (_hoverChildCache.TryGetValue(id, out var cached))
                return cached != null ? cached : null;

            GameObject? child = null;
            var disp = node.GetComponentInChildren<Duckov.UI.ItemDisplay>(includeInactive: false);
            if (disp != null && disp.gameObject != node) child = disp.gameObject;

            _hoverChildCache[id] = child;
            return child;
        }

        // Clicks the most-specific IPointerClickHandler child (e.g. ItemDisplay for InventoryEntry), falling back to focus root.
        internal static void Click(GameObject? focus, PointerEventData.InputButton button = PointerEventData.InputButton.Left)
        {
            if (focus == null) return;
            var clickTarget = ResolveClickTarget(focus);
            if (clickTarget == null) return;
            var ped = Fresh(clickTarget);
            ped.position = ScreenCenterOf(clickTarget);
            ped.button = button;
            ped.clickCount = 1;
            ped.clickTime = Time.unscaledTime;
            ped.pointerPress = clickTarget;
            ExecuteEvents.Execute(clickTarget, ped, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(clickTarget, ped, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(clickTarget, ped, ExecuteEvents.pointerClickHandler);
        }

        // Click target cache by instance ID. Hover() prunes when > 64 entries.
        private static readonly System.Collections.Generic.Dictionary<int, GameObject?> _clickTargetCache = new();

        private static GameObject? ResolveClickTarget(GameObject focus)
        {
            var id = focus.GetInstanceID();
            if (_clickTargetCache.TryGetValue(id, out var cached) && cached != null)
                return cached;

            // Prefer child IPointerClickHandler (ItemDisplay) — game wires real click chain there (InventoryEntry.itemDisplay).
            GameObject? resolved = focus;
            var handler = focus.GetComponentInChildren<IPointerClickHandler>(includeInactive: false);
            if (handler is MonoBehaviour mb && mb != null && mb.gameObject != focus)
                resolved = mb.gameObject;

            _clickTargetCache[id] = resolved;
            return resolved;
        }

        internal static void InvalidateClickTargetCache()
        {
            _clickTargetCache.Clear();
        }

        // Drag sequence — caller owns the lifecycle.
        internal static PointerEventData? BeginDrag(GameObject source)
        {
            if (source == null) return null;
            var ped = new PointerEventData(EventSystem.current)
            {
                position = ScreenCenterOf(source),
                button = PointerEventData.InputButton.Left,
                pointerDrag = source,
                pointerPress = source,
                pointerEnter = source,
                dragging = true,
                useDragThreshold = false,
            };
            // Fire pointerDown before beginDrag, matching real input flow.
            ExecuteEvents.Execute(source, ped, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(source, ped, ExecuteEvents.beginDragHandler);
            return ped;
        }

        internal static void DragOver(PointerEventData? ped, GameObject? hoverTarget)
        {
            if (ped == null || ped.pointerDrag == null) return;
            if (hoverTarget != null)
            {
                ped.position = ScreenCenterOf(hoverTarget);
                ped.pointerEnter = hoverTarget;
            }
            ExecuteEvents.Execute(ped.pointerDrag, ped, ExecuteEvents.dragHandler);
        }

        internal static void EndDrag(PointerEventData? ped, GameObject? dropTarget)
        {
            if (ped == null || ped.pointerDrag == null) return;
            if (dropTarget != null)
            {
                ped.position = ScreenCenterOf(dropTarget);
                ExecuteEvents.ExecuteHierarchy(dropTarget, ped, ExecuteEvents.dropHandler);
            }
            ExecuteEvents.Execute(ped.pointerDrag, ped, ExecuteEvents.endDragHandler);
            // Fire pointerUp on the original press target.
            if (ped.pointerPress != null)
                ExecuteEvents.Execute(ped.pointerPress, ped, ExecuteEvents.pointerUpHandler);
            ped.dragging = false;
            ped.pointerDrag = null;
            ped.pointerPress = null;
        }
    }
}
