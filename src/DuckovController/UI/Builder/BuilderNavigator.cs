using System;
using System.Reflection;
using Duckov.Buildings;        // BuildingManager, BuildingArea, BuildingData
using Duckov.Buildings.UI;     // BuilderView
using DuckovController.UI.Inventory;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace DuckovController.UI.Builder
{
    // Owns all per-frame BuilderView analog behaviour + the controller verbs.
    // Resolves the view's private handles once per open (reflection). Drives the
    // game's mouse Point via InputState.Change so the native ghost / hover-indicator
    // / OnPointerClick all follow unchanged (the view is entirely Point-driven).
    internal sealed class BuilderNavigator
    {
        private BuilderView? _view;
        private FieldInfo?   _modeField;       // private Mode mode  (0=None,1=Placing,2=Destroying)
        private FieldInfo?   _previewRotField; // private BuildingRotation previewRotation
        private FieldInfo?   _targetAreaField; // private BuildingArea targetArea
        private FieldInfo?   _camDistField;    // private float cameraDistance
        private MethodInfo?  _setModeMethod;   // private void SetMode(Mode)
        private MethodInfo?  _returnBuilding;  // internal static UniTask<bool> BuildingManager.ReturnBuilding(int,string)
        private FieldInfo?   _camCursorField;  // private Vector3 cameraCursor
        private FieldInfo?   _vcamField;       // private CinemachineVirtualCamera vcam (use as Component)
        private FieldInfo?   _camSpeedField;   // private float cameraSpeed (=10)
        private FieldInfo?   _moveCamField;    // private InputActionReference input_MoveCamera

        private Vector2        _cursor;        // screen-space build cursor (pixels)
        private bool           _seeded;
        private bool           _wasPlacing;
        private bool           _sawReleaseSincePlacing;

        // Reticle overlay (screen-space gold ring parented on the view's canvas).
        private readonly BuilderCursor _reticle  = new BuilderCursor();
        private RectTransform?         _canvasRt;
        private Camera?                _canvasCam;
        private Transform?             _moveHint;   // vanilla "Navigate" WASD hint, hidden on Deck

        // Tunables (dial in on device).
        private const float CursorSpeed = 1400f; // px/sec at full deflection
        private const float ZoomSpeed   = 22f;   // cameraDistance units/sec
        private const float ZoomMin     = 15f;
        private const float ZoomMax     = 70f;
        private const float Deadzone    = 0.18f;

        public int  Mode      => (_view != null && _modeField != null)
                                 ? Convert.ToInt32(_modeField.GetValue(_view)) : 0;
        public bool IsPlacing => Mode == 1;

        // Destroy the reticle GO (parented on game-side UI; outlives the mod's GameObject).
        public void DestroyOverlay() => _reticle.Destroy();

        private bool Resolve()
        {
            var inst = BuilderView.Instance;
            if (inst == null)
            {
                if (_view != null)
                {
                    _reticle.Destroy();
                    _view      = null;
                    _canvasRt  = null;
                }
                _seeded = false;
                return false;
            }
            if (!ReferenceEquals(inst, _view))
            {
                _view = inst;
                var t = typeof(BuilderView);
                const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic;
                _modeField       = t.GetField("mode",             BF);
                _previewRotField = t.GetField("previewRotation",  BF);
                _targetAreaField = t.GetField("targetArea",       BF);
                _camDistField    = t.GetField("cameraDistance",   BF);
                _setModeMethod   = t.GetMethod("SetMode",         BF);
                _returnBuilding  = typeof(BuildingManager).GetMethod("ReturnBuilding",
                    BindingFlags.Static | BindingFlags.NonPublic);
                _camCursorField  = t.GetField("cameraCursor",     BF);
                _vcamField       = t.GetField("vcam",             BF);
                _camSpeedField   = t.GetField("cameraSpeed",      BF);
                _moveCamField    = t.GetField("input_MoveCamera", BF);
                _seeded = false;

                var canvas = _view.GetComponentInParent<Canvas>();
                _canvasRt  = canvas != null ? canvas.transform as RectTransform : null;
                _canvasCam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    ? canvas.worldCamera : null;

                // Vanilla WASD "Navigate" hint — hidden each frame (PlayerInput never pairs pad on Deck).
                _moveHint = _view.transform.Find("Layout/InputIndicators/Navigate");

                Log.Debug_($"BuilderNav.Resolve: mode={_modeField != null} rot={_previewRotField != null} " +
                           $"area={_targetAreaField != null} cam={_camDistField != null} " +
                           $"setMode={_setModeMethod != null} return={_returnBuilding != null} " +
                           $"cursor={_camCursorField != null} vcam={_vcamField != null} " +
                           $"speed={_camSpeedField != null} moveInput={_moveCamField != null} " +
                           $"canvasRt={_canvasRt != null}");
            }
            return true;
        }

        public void Tick(InventoryVerbRouter router)
        {
            if (!Resolve()) return;
            var pad = Gamepad.current;
            if (pad == null) return;
            float dt = Time.unscaledDeltaTime;
            if (!_seeded)
            {
                _cursor = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                _seeded = true;
            }
            // Two-tap place: require A released+re-pressed after placing begins.
            // The press that begins placing must NOT also confirm (frame-timing race).
            bool placing = IsPlacing;
            if (placing && !_wasPlacing) _sawReleaseSincePlacing = false; // just entered placing
            if (placing && !pad.buttonSouth.isPressed) _sawReleaseSincePlacing = true;
            if (placing && pad.buttonSouth.wasPressedThisFrame && _sawReleaseSincePlacing)
                ConfirmPlacement();
            _wasPlacing = placing;
            Pan(pad, dt);
            DriveCursor(pad, dt);
            Zoom(pad, dt);

            // Suppress the vanilla keyboard WASD move hint (our hint panel shows LS = Pan).
            if (_moveHint != null && _moveHint.gameObject.activeSelf)
                _moveHint.gameObject.SetActive(false);

            // Reticle: show + track the cursor while the view is active.
            if (_canvasRt != null)
            {
                _reticle.EnsureCreated(_canvasRt, _canvasCam);
                _reticle.SetScreenPos(_cursor);
                _reticle.SetActive(true);
            }
        }

        // LS drives cameraCursor (replicates UpdateCamera math). Skip if native MoveAxis is pressed (Perf.ApplyGamepadBindings on).
        private void Pan(Gamepad pad, float dt)
        {
            if (_view == null || _camCursorField == null || _vcamField == null || _targetAreaField == null) return;
            if (_moveCamField?.GetValue(_view) is InputActionReference mc && mc.action != null && mc.action.IsPressed())
                return;
            Vector2 s = pad.leftStick.ReadValue();
            if (s.magnitude < Deadzone) return;
            if (!(_vcamField.GetValue(_view) is Component vcam)) return;
            Transform tr = vcam.transform;
            float fUp = Mathf.Abs(Vector3.Dot(tr.forward, Vector3.up));
            float uUp = Mathf.Abs(Vector3.Dot(tr.up,      Vector3.up));
            Vector3 fwd   = Vector3.ProjectOnPlane((fUp > uUp) ? tr.up : tr.forward, Vector3.up);
            Vector3 right = Vector3.ProjectOnPlane(tr.right, Vector3.up);
            float speed = _camSpeedField != null ? Convert.ToSingle(_camSpeedField.GetValue(_view)) : 10f;
            Vector3 cur = (Vector3)_camCursorField.GetValue(_view)!;
            cur += (right * s.x + fwd * s.y) * speed * dt;
            if (_targetAreaField.GetValue(_view) is BuildingArea area)
            {
                Vector3 ap = area.transform.position; var sz = area.Size;
                cur.x = Mathf.Clamp(cur.x, ap.x - sz.x, ap.x + sz.x);
                cur.z = Mathf.Clamp(cur.z, ap.z - sz.y, ap.z + sz.y);
            }
            _camCursorField.SetValue(_view, cur);
        }

        private void DriveCursor(Gamepad pad, float dt)
        {
            Vector2 r = pad.rightStick.ReadValue();
            if (r.magnitude >= Deadzone)
            {
                _cursor.x = Mathf.Clamp(_cursor.x + r.x * CursorSpeed * dt, 0f, Screen.width);
                _cursor.y = Mathf.Clamp(_cursor.y + r.y * CursorSpeed * dt, 0f, Screen.height);
            }
            // InputState.Change is synchronous (same-frame raycasts). WarpCursorPosition corrupts the input pipeline.
            if (Mouse.current != null)
            {
                try { InputState.Change(Mouse.current.position, _cursor); }
                catch (Exception e) { Log.Debug_($"BuilderNav cursor write failed: {e.Message}"); }
            }
        }

        private void Zoom(Gamepad pad, float dt)
        {
            if (_view == null || _camDistField == null) return;
            float lt = pad.leftTrigger.ReadValue();
            float rt = pad.rightTrigger.ReadValue();
            float d  = (lt - rt) * ZoomSpeed * dt; // LT = zoom out (more distance), RT = zoom in
            if (Mathf.Abs(d) < 1e-4f) return;
            float cur = Convert.ToSingle(_camDistField.GetValue(_view));
            _camDistField.SetValue(_view, Mathf.Clamp(cur + d, ZoomMin, ZoomMax));
        }

        // A while placing: confirm via native OnPointerClick. Cursor write above ensures TryGetPointingCoord resolves the cell.
        public void ConfirmPlacement()
        {
            if (_view == null) return;
            var ped = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left
            };
            try { _view.OnPointerClick(ped); }
            catch (Exception e) { Log.Debug_($"BuilderNav confirm failed: {e.Message}"); }
        }

        // B while placing: cancel placing -> None via private SetMode(None).
        public void CancelPlacing()
        {
            if (_view == null || _modeField == null || _setModeMethod == null) return;
            object none = Enum.ToObject(_modeField.FieldType, 0); // Mode.None
            try { _setModeMethod.Invoke(_view, new[] { none }); }
            catch (Exception e) { Log.Debug_($"BuilderNav cancel failed: {e.Message}"); }
        }

        // Y while placing: rotate the ghost 90 deg CW (previewRotation = (v+1) % 4).
        public void Rotate()
        {
            if (_view == null || _previewRotField == null) return;
            int cur = Convert.ToInt32(_previewRotField.GetValue(_view));
            object next = Enum.ToObject(_previewRotField.FieldType, (cur + 1) % 4);
            _previewRotField.SetValue(_view, next);
        }

        // X while browsing: recycle the building under the cursor (refunds via
        // BuildingManager.ReturnBuilding). Resolved through the public coord/area path.
        public void RecycleHovered()
        {
            if (_view == null || _targetAreaField == null || _returnBuilding == null) return;
            if (!_view.TryGetPointingCoord(out var coord)) return;
            if (!(_targetAreaField.GetValue(_view) is BuildingArea area)) return;
            var data = area.AreaData?.GetBuildingAt(coord);
            if (data == null) return;
            try { _returnBuilding.Invoke(null, new object?[] { data.GUID, null }); }
            catch (Exception e) { Log.Debug_($"BuilderNav recycle failed: {e.Message}"); }
        }
    }
}
