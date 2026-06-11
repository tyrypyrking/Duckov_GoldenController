using Duckov.MiniMaps;                        // MapMarkerManager, MapMarkerPOI, PointsOfInterests
using Duckov.MiniMaps.UI;                     // MiniMapView, MiniMapDisplay
using Duckov.Scenes;                          // MultiSceneCore
using DuckovController.UI.Inventory;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DuckovController.UI.MiniMap
{
    // Per-frame MiniMapView analog: LS pan, RS cursor, LT/RT zoom, X place/remove marker.
    // One instance held by MiniMapViewVerbMap. Resolves private handles once per open.
    internal sealed class MiniMapNavigator
    {
        private MiniMapView?    _view;
        private Transform?      _displayTf;    // MiniMapDisplay.transform — pan target
        private MiniMapDisplay? _display;      // cast ref — for TryConvertToWorldPosition
        private Slider?         _zoomSlider;
        private RectTransform?  _viewport;     // display.parent — framing rect

        private Camera? _canvasCam; // cached once per open (ClampPan + TryScreenAabb call it 2-3×/frame)

        private readonly Vector3[] _corners = new Vector3[4]; // reused; no per-frame alloc

        private readonly MiniMapCursor _cursor = new MiniMapCursor();

        // Tunables (dial in on device).
        private const float PanSpeed        = 1600f;  // screen px / sec at full deflection
        private const float ZoomSpeed       = 0.9f;   // slider units (0..1) / sec
        private const float CursorSpeed     = 1400f;  // screen px / sec at full deflection
        private const float MarkerHitRadius = 36f;    // screen px proximity for remove
        private const float StickDeadzone   = 0.18f;
        private const float ClampMargin     = 0.25f; // min overlap fraction so map can't be lost off-screen

        // Expose for mod-lifecycle teardown (OnBeforeDeactivate).
        public void DestroyOverlay() => _cursor.Destroy();

        private bool Resolve()
        {
            var inst = MiniMapView.Instance;
            if (inst == null)
            {
                // View closed — tear down cursor overlay so it doesn't orphan.
                if (_view != null)
                {
                    _cursor.Destroy();
                    _view             = null;
                }
                return false;
            }
            if (!ReferenceEquals(inst, _view))
            {
                _view       = inst;
                _display    = ReflectField(inst, "display") as MiniMapDisplay;
                _displayTf  = _display?.transform;
                _zoomSlider = ReflectField(inst, "zoomSlider") as Slider;
                _viewport   = _displayTf?.parent as RectTransform;
                _canvasCam  = _viewport != null ? ResolveCanvasCamera(_viewport) : null;
                Log.Debug_($"MiniMapNav.Resolve: display={(_displayTf != null)} slider={(_zoomSlider != null)} viewport={(_viewport != null)} cam={(_canvasCam != null ? _canvasCam.name : "overlay")}");
                DisableScrollbarNavigation(inst);
            }
            return _displayTf != null;
        }

        private static object? ReflectField(object target, string name)
        {
            var f = target.GetType().GetField(name,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);
            return f?.GetValue(target);
        }

        public void Tick(InventoryVerbRouter router)
        {
            if (!Resolve()) return;
            var pad = Gamepad.current;
            if (pad == null) return;
            float dt = Time.unscaledDeltaTime;
            Pan(pad, dt);
            Zoom(pad, dt);
            DriveCursor(pad, dt);
        }

        private void Pan(Gamepad pad, float dt)
        {
            Vector2 s = pad.leftStick.ReadValue();
            if (s.magnitude < StickDeadzone) return;
            // Push-right reveals map to the right => move display left (sign confirmed on device).
            Vector3 delta = new Vector3(-s.x, -s.y, 0f) * PanSpeed * dt;
            _displayTf!.position += delta;
            ClampPan();
        }

        // Keep display overlapping viewport by ClampMargin. Uses world-corner AABBs (viewport rect.size is 0 due to stretch-anchoring).
        private void ClampPan()
        {
            if (_viewport == null) return;
            if (_displayTf is not RectTransform dispRt) return;

            if (!TryScreenAabb(_viewport, out var vMin, out var vMax)) return;
            if (!TryScreenAabb(dispRt,    out var dMin, out var dMax)) return;

            Vector2 vSize  = vMax - vMin;
            float   margin = ClampMargin * Mathf.Min(vSize.x, vSize.y);

            Vector2 correction = Vector2.zero;
            if      (dMin.x > vMax.x - margin) correction.x = (vMax.x - margin) - dMin.x;
            else if (dMax.x < vMin.x + margin) correction.x = (vMin.x + margin) - dMax.x;
            if      (dMin.y > vMax.y - margin) correction.y = (vMax.y - margin) - dMin.y;
            else if (dMax.y < vMin.y + margin) correction.y = (vMin.y + margin) - dMax.y;

            if (correction == Vector2.zero) return;
            ApplyScreenCorrection(dispRt, correction);
        }

        private void Zoom(Gamepad pad, float dt)
        {
            if (_zoomSlider == null) return;
            float lt = pad.leftTrigger.ReadValue();
            float rt = pad.rightTrigger.ReadValue();
            float d  = (rt - lt) * ZoomSpeed * dt;
            if (Mathf.Abs(d) < 1e-5f) return;
            float v = Mathf.Clamp01(_zoomSlider.value + d);
            if (!Mathf.Approximately(v, _zoomSlider.value))
                _zoomSlider.value = v; // fires onValueChanged -> game Zoom/RefreshZoom
        }

        private void DriveCursor(Gamepad pad, float dt)
        {
            if (_viewport == null) return;
            _cursor.EnsureCreated(_viewport, _canvasCam);

            Vector2 r = pad.rightStick.ReadValue();
            if (r.magnitude >= StickDeadzone)
                _cursor.MoveBy(new Vector2(r.x, r.y) * CursorSpeed * dt, _viewport, _canvasCam);

            // Cursor always shows the selected marker icon. No per-frame proximity scan — X is a place/remove toggle.
            _cursor.SetPreview(MapMarkerManager.SelectedIcon, MapMarkerManager.SelectedColor);
        }

        // Nearest MapMarkerPOI within MarkerHitRadius screen-px of screenPos, or null.
        // Hoists ActiveSubSceneID out of the per-marker loop (scene-constant for the whole scan).
        private MapMarkerPOI? FindMarkerNear(Vector2 screenPos)
        {
            if (_display == null || _displayTf == null) return null;

            MapMarkerPOI? best   = null;
            float         bestD  = MarkerHitRadius;
            string        scene  = MultiSceneCore.ActiveSubSceneID;  // hoist — same for all markers

            foreach (var point in PointsOfInterests.Points)
            {
                // Unity-null check: a destroyed MapMarkerPOI can linger in the
                // list until the next PointsOfInterests.CleanUp(); skip it.
                if (point == null) continue;
                if (point is not MapMarkerPOI m) continue;
                if (!TryMarkerScreenPos(m, scene, out var sp)) continue;
                float d = Vector2.Distance(sp, screenPos);
                if (d < bestD) { bestD = d; best = m; }
            }
            return best;
        }

        // Project a MapMarkerPOI world pos to screen via the minimap display transform. sceneID hoisted by caller.
        // Caveat: projects off MiniMapDisplay root (not per-scene entry); fine for Screen-Space-Overlay (world≈screen).
        private bool TryMarkerScreenPos(MapMarkerPOI m, string sceneID, out Vector2 screen)
        {
            screen = default;
            if (!_display!.TryConvertWorldToMinimap(m.Data.worldPosition, sceneID, out Vector3 minimapLocal))
                return false;

            Vector3 worldPt = _displayTf!.localToWorldMatrix.MultiplyPoint(minimapLocal);
            screen = RectTransformUtility.WorldToScreenPoint(_canvasCam, worldPt);
            return true;
        }

        // Remove nearest marker within MarkerHitRadius, or place a new one at cursor world pos.
        public void PlaceOrRemoveAtCursor()
        {
            if (_display == null) return;

            var hit = FindMarkerNear(_cursor.ScreenPos);
            if (hit != null)
            {
                MapMarkerManager.Release(hit);
                Log.Debug_($"MiniMapNav: removed marker at {hit.Data.worldPosition}");
                return;
            }

            // TryConvertToWorldPosition takes screen coords (overlay: world≈screen; world-space canvas: remap first).
            Vector3 displayPos;
            if (_canvasCam == null)
            {
                displayPos = new Vector3(_cursor.ScreenPos.x, _cursor.ScreenPos.y, 0f);
            }
            else
            {
                if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(
                        _displayTf as RectTransform, _cursor.ScreenPos, _canvasCam, out displayPos))
                    return;
            }

            if (_display.TryConvertToWorldPosition(displayPos, out Vector3 world))
            {
                MapMarkerManager.Request(world);
                Log.Debug_($"MiniMapNav: placed marker at {world}");
            }
        }

        // Screen-space AABB of a RectTransform using the cached corners buffer.
        private bool TryScreenAabb(RectTransform rt, out Vector2 min, out Vector2 max)
        {
            min = default; max = default;
            rt.GetWorldCorners(_corners);
            Vector2 lo = new Vector2(float.MaxValue,  float.MaxValue);
            Vector2 hi = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 4; i++)
            {
                Vector2 sp = RectTransformUtility.WorldToScreenPoint(_canvasCam, _corners[i]);
                lo = Vector2.Min(lo, sp);
                hi = Vector2.Max(hi, sp);
            }
            min = lo; max = hi;
            return (hi.x - lo.x) > 1f && (hi.y - lo.y) > 1f;
        }

        // Nudge dispRt by screenDelta (screen-space), converting to world delta via unit-move measurement.
        private void ApplyScreenCorrection(RectTransform dispRt, Vector2 screenDelta)
        {
            Vector3 originWorld  = dispRt.position;
            Vector2 originScreen = RectTransformUtility.WorldToScreenPoint(_canvasCam, originWorld);
            Vector2 unitScreenX  = RectTransformUtility.WorldToScreenPoint(_canvasCam, originWorld + Vector3.right) - originScreen;
            Vector2 unitScreenY  = RectTransformUtility.WorldToScreenPoint(_canvasCam, originWorld + Vector3.up)    - originScreen;
            float sx = Mathf.Approximately(unitScreenX.x, 0f) ? 1f : unitScreenX.x;
            float sy = Mathf.Approximately(unitScreenY.y, 0f) ? 1f : unitScreenY.y;
            dispRt.position += new Vector3(screenDelta.x / sx, screenDelta.y / sy, 0f);
        }

        // Set Navigation.mode=None on the ScrollRect scrollbars so D-pad can't land on them.
        // Must NOT use sendNavigationEvents=false — that also disables Submit and breaks A-press.
        private static void DisableScrollbarNavigation(MiniMapView view)
        {
            // Scrollbar Horizontal/Vertical are siblings of Viewport under Content/Scroll View.
            var scrollViewT = view.transform.Find("Content/Scroll View");
            if (scrollViewT == null)
            {
                Log.Debug_("MiniMapNav: 'Content/Scroll View' not found — scrollbar nav not disabled");
                return;
            }
            int disabled = 0;
            foreach (var sb in scrollViewT.GetComponentsInChildren<Scrollbar>(includeInactive: true))
            {
                if (sb == null) continue;
                var nav = sb.navigation;
                if (nav.mode == Navigation.Mode.None) continue;
                nav.mode = Navigation.Mode.None;
                sb.navigation = nav;
                disabled++;
                Log.Debug_($"MiniMapNav: disabled nav on scrollbar '{sb.gameObject.name}'");
            }
            if (disabled == 0)
                Log.Debug_("MiniMapNav: no scrollbars found to disable — check path");
        }

        // Null for Screen-Space-Overlay (RectTransformUtility treats null as overlay).
        private static Camera? ResolveCanvasCamera(RectTransform rt)
        {
            var canvas = rt.GetComponentInParent<Canvas>();
            if (canvas == null) return null;
            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
            return canvas.worldCamera;
        }
    }
}
