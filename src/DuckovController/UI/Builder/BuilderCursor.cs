using UnityEngine;
using UnityEngine.UI;

namespace DuckovController.UI.Builder
{
    // Screen-space reticle for the build cursor. Find-or-create by name so
    // destroy/recreate cycles can't duplicate it (reference_mod_lifecycle).
    // Owned by BuilderNavigator: EnsureCreated per open, SetScreenPos each frame,
    // Destroy on close/deactivate. Procedural gold ring+dot — hard alpha so it
    // can't render-blank (cf. the SmoothStep texture gotcha).
    internal sealed class BuilderCursor
    {
        private const string Name = "DC_BuilderCursor";
        private const float  Size = 36f;

        private GameObject?    _go;
        private RectTransform? _rt;
        private RectTransform? _parent;
        private Camera?        _cam;
        private static Sprite? _sprite;

        public void EnsureCreated(RectTransform parent, Camera? canvasCam)
        {
            _parent = parent; _cam = canvasCam;
            if (_go != null) return;
            var existing = parent.Find(Name);
            if (existing != null) { _go = existing.gameObject; _rt = existing as RectTransform; return; }
            _go = new GameObject(Name, typeof(RectTransform));
            _go.transform.SetParent(parent, worldPositionStays: false);
            _rt = _go.GetComponent<RectTransform>();
            _rt.sizeDelta = new Vector2(Size, Size);
            _rt.anchorMin = _rt.anchorMax = new Vector2(0.5f, 0.5f);
            _rt.pivot     = new Vector2(0.5f, 0.5f);
            var le = _go.AddComponent<LayoutElement>(); le.ignoreLayout = true; // feedback_layout_group_eats_overlays
            var img = _go.AddComponent<Image>();
            img.raycastTarget = false;
            img.sprite = GetSprite();
            _go.transform.SetAsLastSibling();
        }

        public void SetActive(bool on) { if (_go != null && _go.activeSelf != on) _go.SetActive(on); }

        public void SetScreenPos(Vector2 screen)
        {
            if (_rt == null || _parent == null) return;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent, screen, _cam, out var local))
                _rt.anchoredPosition = local;
        }

        public void Destroy()
        {
            if (_go != null) Object.Destroy(_go);
            _go = null; _rt = null; _parent = null;
        }

        private static Sprite GetSprite()
        {
            if (_sprite != null) return _sprite;
            const int S = 32; int c = S / 2; const float R = 13f;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            var px  = new Color[S * S];
            var clear = new Color(0f, 0f, 0f, 0f);
            var col   = new Color(1f, 0.85f, 0.3f, 1f); // gold (matches focus border)
            for (int i = 0; i < px.Length; i++) px[i] = clear;
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                if ((d >= R - 1.5f && d <= R + 0.5f) || d <= 1.8f) px[y * S + x] = col;
            }
            tex.SetPixels(px); tex.Apply();
            tex.filterMode = FilterMode.Bilinear; tex.wrapMode = TextureWrapMode.Clamp;
            _sprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f);
            return _sprite;
        }
    }
}
