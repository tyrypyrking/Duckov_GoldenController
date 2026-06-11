using DuckovController.UI;
using DuckovController.UI.Common;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DuckovController.UI.Prompts
{
    // LB/RB glyphs flanking the shared top tab bar. Placed left of leftmost and right of rightmost tab.
    // World corners → canvas-unit nudge (RectTransform.rect is canvas-local, not screen-space).
    internal sealed class ViewTabsGlyphInjector : MonoBehaviour
    {
        private const float SIZE = 40f;  // glyph box (canvas units)
        private const float GAP = 8f;    // gap from the tab edge to the glyph (canvas units)

        private RectTransform? _root;
        private Image? _lb;
        private Image? _rb;
        private Canvas? _canvas;

        private RectTransform? _left;   // leftmost tab's (non-degenerate) rect
        private RectTransform? _right;  // rightmost tab's rect
        private float _nextScan;

        private static readonly Vector3[] _corners = new Vector3[4];

        private void Update()
        {
            if (!DuckovController.Diagnostics.PerfFlags.GlyphInjectors || Gamepad.current == null)
            {
                Hide();
                return;
            }

            // Throttle the entry scan; repositioning each frame is cheap.
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 0.1f;
                RescanEntries();
            }
            if (_left == null || _right == null) { Hide(); return; }

            var canvas = _left.GetComponentInParent<Canvas>();
            if (canvas == null) { Hide(); return; }

            EnsureBuilt(canvas);
            Reposition();
            if (_root != null && !_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
        }

        // Leftmost/rightmost active tab entries (≥2 required). Mirrors TabSwitcher's active-entry filter.
        private void RescanEntries()
        {
            _left = _right = null;
            if (Duckov.UI.View.ActiveView == null) return;
            var entries = TabSwitcher.GetEntries();
            if (entries == null) return;

            RectTransform? leftRt = null, rightRt = null;
            float minX = float.MaxValue, maxX = float.MinValue;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null || !e.isActiveAndEnabled || !e.gameObject.activeInHierarchy) continue;
                var rt = BestRect(e.transform as RectTransform);
                if (rt == null) continue;
                float x = PointerEventDispatcher.ScreenCenterOf(e.gameObject).x;
                if (x < minX) { minX = x; leftRt = rt; }
                if (x > maxX) { maxX = x; rightRt = rt; }
            }
            if (leftRt != null && rightRt != null && leftRt != rightRt)
            {
                _left = leftRt;
                _right = rightRt;
            }
        }

        // Entry often sits on a tiny indicator child; climb to nearest ancestor with a real rect.
        private static RectTransform? BestRect(RectTransform? rt)
        {
            for (int i = 0; i < 4 && rt != null; i++)
            {
                if (rt.rect.width > 4f && rt.rect.height > 4f) return rt;
                rt = rt.parent as RectTransform;
            }
            return rt;
        }

        private void EnsureBuilt(Canvas canvas)
        {
            if (_root != null && _canvas == canvas) return;
            if (_root != null) { Destroy(_root.gameObject); _root = null; }

            var go = new GameObject("DuckovController.ViewTabsHints",
                typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(canvas.transform, worldPositionStays: false);
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            _root = (RectTransform)go.transform;
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.offsetMin = Vector2.zero;
            _root.offsetMax = Vector2.zero;
            _root.localScale = Vector3.one;

            _lb = MakeGlyph("LB", ButtonGlyph.LB);
            _rb = MakeGlyph("RB", ButtonGlyph.RB);
            _canvas = canvas;
        }

        private Image MakeGlyph(string name, ButtonGlyph glyph)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_root, worldPositionStays: false);
            var img = go.GetComponent<Image>();
            img.sprite = GlyphProvider.Get(glyph);
            img.raycastTarget = false;
            img.preserveAspect = true;
            img.rectTransform.sizeDelta = new Vector2(SIZE, SIZE);
            return img;
        }

        private void Reposition()
        {
            if (_lb == null || _rb == null || _left == null || _right == null) return;

            _left.GetWorldCorners(_corners);                 // 0=BL 1=TL 2=TR 3=BR
            _lb.rectTransform.position = (_corners[0] + _corners[1]) * 0.5f;
            _lb.rectTransform.anchoredPosition += new Vector2(-(GAP + SIZE * 0.5f), 0f);

            _right.GetWorldCorners(_corners);
            _rb.rectTransform.position = (_corners[2] + _corners[3]) * 0.5f;
            _rb.rectTransform.anchoredPosition += new Vector2(GAP + SIZE * 0.5f, 0f);
        }

        private void Hide()
        {
            if (_root != null && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);
        }

        private void OnDisable() => Hide();

        private void OnDestroy()
        {
            if (_root != null) Destroy(_root.gameObject);
            _root = null;
            _lb = _rb = null;
            _canvas = null;
            _left = _right = null;
        }
    }
}
