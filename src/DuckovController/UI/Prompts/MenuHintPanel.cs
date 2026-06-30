using System.Collections.Generic;
using DuckovController.UI.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DuckovController.UI.Prompts
{
    // Menu-world hint panel: a top-right console-styled glyph+label panel for menu overlays (no router).
    // Driven imperatively by MenuFocusOverlay: SetPrompts(...) then ShowUnder(canvas) each frame while
    // active, Hide() otherwise. Mirrors ViewHintPanel's vertical layout via shared HintPanelChrome.
    internal sealed class MenuHintPanel
    {
        private RectTransform? _root;
        private Image? _bg;
        private Canvas? _parentCanvas;
        private readonly List<Row> _rows = new();
        private IReadOnlyList<PromptEntry> _prompts = System.Array.Empty<PromptEntry>();
        private bool _dirty;

        private sealed class Row
        {
            public RectTransform Rt = null!;
            public Image Icon = null!;
            public Image Icon2 = null!;
            public TextMeshProUGUI Label = null!;
        }

        // Replace the displayed rows. Cheap no-op if unchanged (same list reference).
        internal void SetPrompts(IReadOnlyList<PromptEntry> prompts)
        {
            if (ReferenceEquals(prompts, _prompts)) return;
            _prompts = prompts ?? System.Array.Empty<PromptEntry>();
            _dirty = true;
        }

        // Build/repin under the given canvas and show. Call each frame while the panel should be visible.
        internal void ShowUnder(Canvas canvas)
        {
            if (canvas == null) { Hide(); return; }
            bool canvasChanged = _parentCanvas != canvas || _root == null;
            EnsureBuilt(canvas);
            if (_dirty || canvasChanged) { Refresh(canvas); _dirty = false; }
            if (_root != null && !_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
        }

        internal void Hide()
        {
            if (_root != null && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);
        }

        internal void Destroy()
        {
            if (_root != null) Object.Destroy(_root.gameObject);
            _root = null;
            _bg = null;
            _parentCanvas = null;
            _rows.Clear();
        }

        private void EnsureBuilt(Canvas canvas)
        {
            // Unity == treats destroyed objects as null, so a destroyed _root/_parentCanvas falls through.
            if (_root != null && _parentCanvas != null && _parentCanvas == canvas) return;
            if (_root != null) { Object.Destroy(_root.gameObject); _root = null; _rows.Clear(); }
            _parentCanvas = null;

            var bgSprite = HintPanelChrome.RoundedBgSprite();

            var go = new GameObject("DuckovController.MenuHintPanel",
                typeof(RectTransform), typeof(LayoutElement), typeof(Image));
            go.transform.SetParent(canvas.transform, worldPositionStays: false);
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            _root = (RectTransform)go.transform;
            // Top-right anchor (ViewHintPanel is bottom-right; this one sits at the top).
            _root.anchorMin = new Vector2(1f, 1f);
            _root.anchorMax = new Vector2(1f, 1f);
            _root.pivot = new Vector2(1f, 1f);
            _root.anchoredPosition = new Vector2(-HintPanelChrome.MARGIN, -HintPanelChrome.MARGIN);
            _root.localScale = Vector3.one;

            // Render on top of the character-creator UI (sub-canvas with max sort order).
            var sub = go.AddComponent<Canvas>();
            sub.overrideSorting = true;
            sub.sortingOrder = short.MaxValue;

            _bg = go.GetComponent<Image>();
            _bg.sprite = bgSprite;
            _bg.type = Image.Type.Sliced;
            _bg.raycastTarget = false;
            _bg.color = HintPanelChrome.BgColor;

            _parentCanvas = canvas;
        }

        private Row EnsureRow(int i, TMP_FontAsset? font)
        {
            if (i < _rows.Count) return _rows[i];

            var rowGo = new GameObject("Row" + i, typeof(RectTransform));
            var rrt = (RectTransform)rowGo.transform;
            rrt.SetParent(_root, worldPositionStays: false);
            rrt.anchorMin = new Vector2(0f, 1f);
            rrt.anchorMax = new Vector2(0f, 1f);
            rrt.pivot = new Vector2(0.5f, 0.5f);
            rrt.localScale = Vector3.one;

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            var irt = (RectTransform)iconGo.transform;
            irt.SetParent(rrt, worldPositionStays: false);
            irt.anchorMin = new Vector2(0f, 1f);
            irt.anchorMax = new Vector2(0f, 1f);
            irt.pivot = new Vector2(0.5f, 0.5f);
            irt.sizeDelta = new Vector2(HintPanelChrome.ICON, HintPanelChrome.ICON);
            var icon = iconGo.GetComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;

            var icon2Go = new GameObject("Icon2", typeof(RectTransform), typeof(Image));
            var i2rt = (RectTransform)icon2Go.transform;
            i2rt.SetParent(rrt, worldPositionStays: false);
            i2rt.anchorMin = new Vector2(0f, 1f);
            i2rt.anchorMax = new Vector2(0f, 1f);
            i2rt.pivot = new Vector2(0.5f, 0.5f);
            i2rt.sizeDelta = new Vector2(HintPanelChrome.ICON, HintPanelChrome.ICON);
            var icon2 = icon2Go.GetComponent<Image>();
            icon2.raycastTarget = false;
            icon2.preserveAspect = true;
            icon2Go.SetActive(false);

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            var lrt = (RectTransform)labelGo.transform;
            lrt.SetParent(rrt, worldPositionStays: false);
            lrt.anchorMin = new Vector2(0f, 1f);
            lrt.anchorMax = new Vector2(0f, 1f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            var label = labelGo.GetComponent<TextMeshProUGUI>();
            label.raycastTarget = false;
            label.enableWordWrapping = false;
            label.fontSize = 26f;
            label.color = Color.white;
            if (font != null) label.font = font;

            var row = new Row { Rt = rrt, Icon = icon, Icon2 = icon2, Label = label };
            _rows.Add(row);
            return row;
        }

        private void Refresh(Canvas canvas)
        {
            if (_root == null) return;
            var font = HintPanelChrome.ResolveFont(canvas);
            int n = _prompts.Count;

            for (int i = 0; i < n; i++)
            {
                var row = EnsureRow(i, font);
                row.Rt.gameObject.SetActive(true);

                var sprite = GlyphProvider.Get(_prompts[i].Glyph);
                row.Icon.sprite = sprite;
                row.Icon.enabled = sprite != null;

                bool dual = _prompts[i].Glyph2.HasValue;
                if (dual)
                {
                    var sprite2 = GlyphProvider.Get(_prompts[i].Glyph2!.Value);
                    row.Icon2.sprite = sprite2;
                    row.Icon2.enabled = sprite2 != null;
                }
                row.Icon2.gameObject.SetActive(dual);

                row.Label.text = _prompts[i].Label;
                if (font != null && row.Label.font != font) row.Label.font = font;
            }
            for (int i = n; i < _rows.Count; i++)
                _rows[i].Rt.gameObject.SetActive(false);

            LayoutVertical(n);
        }

        // Vertical stack: glyph(s) left, label right-aligned at fixed column (mirrors ViewHintPanel).
        private void LayoutVertical(int n)
        {
            if (_root == null) return;
            const float ROW_W = HintPanelChrome.ROW_W;
            const float ROW_H = HintPanelChrome.ROW_H;
            const float PAD = HintPanelChrome.PAD;
            const float PITCH = HintPanelChrome.PITCH;
            for (int i = 0; i < n; i++)
            {
                var row = _rows[i];
                row.Rt.sizeDelta = new Vector2(ROW_W, ROW_H);
                row.Rt.anchoredPosition = new Vector2(PAD + ROW_W / 2f, -(PAD + ROW_H / 2f + PITCH * i));

                ((RectTransform)row.Icon.transform).anchoredPosition = new Vector2(HintPanelChrome.ICON_X, -ROW_H / 2f);
                ((RectTransform)row.Icon2.transform).anchoredPosition = new Vector2(HintPanelChrome.ICON2_X, -ROW_H / 2f);

                var lrt = (RectTransform)row.Label.transform;
                row.Label.alignment = TextAlignmentOptions.Right;
                lrt.sizeDelta = new Vector2(HintPanelChrome.LABEL_W, ROW_H);
                lrt.anchoredPosition = new Vector2(HintPanelChrome.LABEL_X, -ROW_H / 2f);
            }
            float height = n > 0 ? (n - 1) * PITCH + ROW_H + 2f * PAD : 0f;
            _root.sizeDelta = new Vector2(HintPanelChrome.CONTENT_W, height);
        }
    }
}
