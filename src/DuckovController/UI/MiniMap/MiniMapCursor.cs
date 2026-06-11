using UnityEngine;
using UnityEngine.UI;

namespace DuckovController.UI.MiniMap
{
    // Screen-space marker cursor overlay parented under the MiniMapDisplay viewport.
    // Find-or-create by name (mod-lifecycle safe). Owned by MiniMapNavigator.
    // Visual: selected marker icon tinted selected color. No ring; X is place/remove toggle.
    internal sealed class MiniMapCursor
    {
        private const string Name = "DC_MiniMapCursor";

        private const float CursorSize = 52f;

        private GameObject?    _go;
        private RectTransform? _rt;
        private Image?         _icon;

        // Change-dedup: avoids dirtying canvas every frame when unchanged.
        private Sprite? _lastSprite;
        private Color   _lastColor     = Color.clear;

        private readonly Vector3[] _corners = new Vector3[4]; // reused; no per-call alloc

        // Screen-space position (pixels). Init = viewport centre on open.
        public Vector2 ScreenPos { get; private set; }

        public void EnsureCreated(RectTransform viewport, Camera? canvasCam)
        {
            if (_go != null) return;

            var existing = viewport.Find(Name);
            if (existing != null)
            {
                BindTo(existing.gameObject, viewport, canvasCam);
                return;
            }

            _go             = new GameObject(Name, typeof(RectTransform));
            _go.transform.SetParent(viewport, worldPositionStays: false);
            _rt             = _go.GetComponent<RectTransform>();
            _rt.sizeDelta   = new Vector2(CursorSize, CursorSize);
            _rt.anchorMin   = _rt.anchorMax = new Vector2(0.5f, 0.5f);
            _rt.pivot       = new Vector2(0.5f, 0.5f);

            _icon                = _go.AddComponent<Image>();
            _icon.preserveAspect = true;
            _icon.raycastTarget  = false;
            _icon.enabled        = false;   // hidden until a sprite is assigned

            CenterOn(viewport, canvasCam);
            Log.Debug_("MiniMapCursor: created overlay GO.");
        }

        private void BindTo(GameObject go, RectTransform viewport, Camera? canvasCam)
        {
            _go   = go;
            _rt   = go.GetComponent<RectTransform>();
            _icon = go.GetComponent<Image>();
            if (_icon == null)
                _icon = go.GetComponentInChildren<Image>(includeInactive: true);
            CenterOn(viewport, canvasCam);
            Log.Debug_("MiniMapCursor: re-bound to existing overlay GO.");
        }

        private void CenterOn(RectTransform viewport, Camera? canvasCam)
        {
            viewport.GetWorldCorners(_corners);
            Vector3 worldCenter = (_corners[0] + _corners[2]) * 0.5f;
            ScreenPos = RectTransformUtility.WorldToScreenPoint(canvasCam, worldCenter);
            SyncAnchoredPos(viewport, canvasCam);
        }

        public void MoveBy(Vector2 screenDelta, RectTransform viewport, Camera? canvasCam)
        {
            viewport.GetWorldCorners(_corners);
            Vector2 vpMin = RectTransformUtility.WorldToScreenPoint(canvasCam, _corners[0]);
            Vector2 vpMax = RectTransformUtility.WorldToScreenPoint(canvasCam, _corners[2]);

            Vector2 newPos;
            newPos.x  = Mathf.Clamp(ScreenPos.x + screenDelta.x, vpMin.x, vpMax.x);
            newPos.y  = Mathf.Clamp(ScreenPos.y + screenDelta.y, vpMin.y, vpMax.y);
            ScreenPos = newPos;
            SyncAnchoredPos(viewport, canvasCam);
        }

        // Show the selected marker icon tinted the selected color.
        public void SetPreview(Sprite? sprite, Color color)
        {
            if (_icon == null) return;
            bool spriteChanged = !ReferenceEquals(sprite, _lastSprite);
            bool colorChanged  = color != _lastColor;
            if (!spriteChanged && !colorChanged) return;

            _lastSprite   = sprite;
            _lastColor    = color;
            _icon.sprite  = sprite;
            _icon.color   = color;
            _icon.enabled = sprite != null;
        }

        public void Destroy()
        {
            if (_go != null)
            {
                Object.Destroy(_go);
                Log.Debug_("MiniMapCursor: destroyed overlay GO.");
            }
            _go         = null;
            _rt         = null;
            _icon       = null;
            _lastSprite = null;
            _lastColor  = Color.clear;
        }

        private void SyncAnchoredPos(RectTransform viewport, Camera? canvasCam)
        {
            if (_rt == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                viewport, ScreenPos, canvasCam, out Vector2 local);
            _rt.anchoredPosition = local;
        }
    }
}
