using System.Collections.Generic;
using DuckovController.UI.Common;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DuckovController.UI.Prompts
{
    // Renders router's CurrentPrompts as a console-styled panel (bottom-right, inventory/trader screens).
    // Built under the active View's Canvas. Geometry mirrors GamingConsoleHUD dump. Render-only.
    internal sealed class ViewHintPanel : MonoBehaviour
    {
        // Set by ModBehaviour AFTER AddComponent (OnEnable runs inside AddComponent — subscribe lazily in Update).
        internal IButtonPromptSource? Source;

        // measured geometry (GamingConsoleHUD_20260531_210655_405.txt)
        private const float MARGIN = 32f;
        private const float PAD = 16f;
        private const float ROW_W = 302.1f;
        private const float ROW_H = 52.1f;
        private const float PITCH = 58.1f;  // ROW_H + ~6px so stacked glyphs don't touch
        private const float CONTENT_W = 334.1f;
        private const float ICON = 52.1f;
        private const float ICON_X = 26.1f;
        private const float ICON2_X = 92.2f;  // paired second glyph (ICON_X + ICON + ~14 spacer)
        private const float LABEL_X = 185.1f;
        private const float LABEL_W = 234f;

        // horizontal-strip metrics (trader / craft views)
        private const float H_CELL_GAP = 14f;   // gap between adjacent prompt cells
        private const float H_ICON_GAP = 6f;    // icon → label gap within a cell
        private const float H_ICON2_GAP = 6f;   // icon → paired second icon gap

        private RectTransform? _root;     // the Content panel
        private Image? _bg;
        private Canvas? _parentCanvas;
        private readonly List<Row> _rows = new();
        private bool _subscribed;          // Source.OnPromptsChanged
        private bool _sceneHooksSubscribed; // View.OnActiveViewChanged + LevelManager.OnAfterLevelInitialized
        private bool _dirty;

        // Overlay-specific prompts (e.g. the Split dialogue) shown in place of the active view's
        // verb-map prompts. Set/cleared by GridFocusController; null = use the router source.
        internal static System.Collections.Generic.IReadOnlyList<PromptEntry>? Override;
        private System.Collections.Generic.IReadOnlyList<PromptEntry>? _appliedOverride;

        private static Sprite? _bgSprite;

        private sealed class Row
        {
            public RectTransform Rt = null!;
            public Image Icon = null!;
            public Image Icon2 = null!;   // shown only for paired (two-glyph) rows
            public TextMeshProUGUI Label = null!;
        }

        // lifecycle

        private void OnPromptsChanged() => _dirty = true;

        // The active View (and on a raid, the whole UI canvas) can change/be destroyed out from under us.
        // View.ActiveView is a static cleared only in OnClose, so a LoadSceneMode.Single raid can leave it
        // pointing at a destroyed View whose canvas is mid-teardown — the per-frame canvas-resolve then
        // latches a stale handle and the panel can stay hidden forever. Drop the cached canvas/root handles
        // and force a rebuild under the live canvas next Update. (View.OnActiveViewChanged is parameterless;
        // read View.ActiveView inside if needed.)
        private void OnSceneContextChanged()
        {
            if (_root != null) { Destroy(_root.gameObject); }
            _root = null;
            _rows.Clear();
            _parentCanvas = null;
            _dirty = true;
        }

        private void Update()
        {
            // Kill switch: shares the glyph-UI flag with the other glyph injectors.
            if (!DuckovController.Diagnostics.PerfFlags.GlyphInjectors) { HidePanel(); return; }
            if (Source == null) return;

            // Subscribe once Source is available (set after AddComponent).
            if (!_subscribed)
            {
                Source.OnPromptsChanged += OnPromptsChanged;
                _subscribed = true;
                _dirty = true;
            }

            // Event-driven reset: a raid (LoadSceneMode.Single) destroys + recreates the UI canvas and Views,
            // so rebuild on active-view change and on every level (re)load instead of trusting the per-frame
            // canvas-resolve to self-heal off a possibly-destroyed View.ActiveView.
            if (!_sceneHooksSubscribed)
            {
                Duckov.UI.View.OnActiveViewChanged += OnSceneContextChanged;
                LevelManager.OnAfterLevelInitialized += OnSceneContextChanged;
                _sceneHooksSubscribed = true;
            }

            // Empty prompts = out of scope (Default verb map returns none for non-item focus).
            // GridFocusController gate prevents residue over unmanaged views (e.g. ClosureView):
            // router's _currentMap is frozen there and keeps returning stale prompts.
            // An overlay (Split dialogue) can override the view's prompts; refresh when it toggles.
            if (!ReferenceEquals(Override, _appliedOverride)) { _appliedOverride = Override; _dirty = true; }
            var effective = Override ?? Source.CurrentPrompts;

            bool show =
                UnityEngine.InputSystem.Gamepad.current != null
                && Time.timeScale != 0f
                && Duckov.UI.View.ActiveView != null
                && DuckovController.UI.GridFocusController.Instance?.IsHandlingActiveView() == true
                && effective.Count > 0;

            if (!show) { HidePanel(); return; }

            // includeInactive: the new base View can be ActiveView while its parent canvas is briefly
            // inactive (scene activation / fade-in) — without this, GetComponentInParent returns null and
            // the panel drops for that window. Genuinely-null (no canvas at all) still hides + returns.
            var canvas = (Duckov.UI.View.ActiveView as Component)?.GetComponentInParent<Canvas>(includeInactive: true);
            if (canvas == null) { HidePanel(); return; }

            bool canvasChanged = _parentCanvas != canvas;
            EnsureBuilt(canvas);
            if (_dirty || canvasChanged)
            {
                Refresh(canvas);
                _dirty = false;
            }
            if (_root != null && !_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
        }

        private void HidePanel()
        {
            if (_root != null && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            if (_subscribed && Source != null)
            {
                Source.OnPromptsChanged -= OnPromptsChanged;
                _subscribed = false;
            }
            // Static-event handlers must be released or they leak across a mod enable/disable cycle
            // (the GO persists under DontDestroyOnLoad → a re-enable would double-subscribe).
            if (_sceneHooksSubscribed)
            {
                Duckov.UI.View.OnActiveViewChanged -= OnSceneContextChanged;
                LevelManager.OnAfterLevelInitialized -= OnSceneContextChanged;
                _sceneHooksSubscribed = false;
            }
            HidePanel();
        }

        // build (under the active View's canvas)

        private void EnsureBuilt(Canvas canvas)
        {
            // Don't trust the cache: rebuild if the root or cached canvas is null/destroyed, or the live
            // canvas differs. Unity's == treats a destroyed object as null, so a destroyed _root/_parentCanvas
            // falls through to the rebuild below; never carry a destroyed handle forward.
            if (_root != null && _parentCanvas != null && _parentCanvas == canvas) return;
            if (_root != null) { Destroy(_root.gameObject); _root = null; _rows.Clear(); }
            _parentCanvas = null; // drop any destroyed handle before re-pinning

            EnsureBgSprite();

            var go = new GameObject("DuckovController.ViewHintPanel",
                typeof(RectTransform), typeof(LayoutElement), typeof(Image));
            go.transform.SetParent(canvas.transform, worldPositionStays: false);
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            _root = (RectTransform)go.transform;
            _root.anchorMin = new Vector2(1f, 0f);
            _root.anchorMax = new Vector2(1f, 0f);
            _root.pivot = new Vector2(1f, 0f);
            _root.anchoredPosition = new Vector2(-MARGIN, MARGIN);
            _root.localScale = Vector3.one;

            _bg = go.GetComponent<Image>();
            _bg.sprite = _bgSprite;
            _bg.type = Image.Type.Sliced;
            _bg.raycastTarget = false;
            _bg.color = new Color(0.05f, 0.06f, 0.09f, 0.5f); // dark, ~50% alpha

            _parentCanvas = canvas;
        }

        // Pooled rows (deactivated not destroyed). Per-row positions/sizes/alignment set each Refresh
        // because vertical/horizontal layout can change when the active view changes.
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
            irt.sizeDelta = new Vector2(ICON, ICON);
            var icon = iconGo.GetComponent<Image>();
            icon.raycastTarget = false;
            icon.preserveAspect = true;

            var icon2Go = new GameObject("Icon2", typeof(RectTransform), typeof(Image));
            var i2rt = (RectTransform)icon2Go.transform;
            i2rt.SetParent(rrt, worldPositionStays: false);
            i2rt.anchorMin = new Vector2(0f, 1f);
            i2rt.anchorMax = new Vector2(0f, 1f);
            i2rt.pivot = new Vector2(0.5f, 0.5f);
            i2rt.sizeDelta = new Vector2(ICON, ICON);
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

        // Rebuild rows from the source's current prompt list and size the panel.
        private void Refresh(Canvas canvas)
        {
            if (_root == null || Source == null) return;
            var prompts = Override ?? Source.CurrentPrompts;
            int n = prompts.Count;

            var font = ResolveFont(canvas);

            for (int i = 0; i < n; i++)
            {
                var row = EnsureRow(i, font);
                row.Rt.gameObject.SetActive(true);

                var sprite = GlyphProvider.Get(prompts[i].Glyph);
                row.Icon.sprite = sprite;
                row.Icon.enabled = sprite != null;

                bool dual = prompts[i].Glyph2.HasValue;
                if (dual)
                {
                    var sprite2 = GlyphProvider.Get(prompts[i].Glyph2!.Value);
                    row.Icon2.sprite = sprite2;
                    row.Icon2.enabled = sprite2 != null;
                }
                row.Icon2.gameObject.SetActive(dual);

                row.Label.text = prompts[i].Label;
                if (font != null && row.Label.font != font) row.Label.font = font;
            }
            for (int i = n; i < _rows.Count; i++)
                _rows[i].Rt.gameObject.SetActive(false);

            if (Override == null && Source.PromptsHorizontal) LayoutHorizontal(n);
            else LayoutVertical(n); // overlay prompts always stack vertically
        }

        // Vertical stack: glyph(s) left, label right-aligned at fixed column. Fixed panel width.
        private void LayoutVertical(int n)
        {
            if (_root == null) return;
            for (int i = 0; i < n; i++)
            {
                var row = _rows[i];
                row.Rt.sizeDelta = new Vector2(ROW_W, ROW_H);
                row.Rt.anchoredPosition = new Vector2(PAD + ROW_W / 2f, -(PAD + ROW_H / 2f + PITCH * i));

                ((RectTransform)row.Icon.transform).anchoredPosition = new Vector2(ICON_X, -ROW_H / 2f);
                ((RectTransform)row.Icon2.transform).anchoredPosition = new Vector2(ICON2_X, -ROW_H / 2f);

                var lrt = (RectTransform)row.Label.transform;
                row.Label.alignment = TextAlignmentOptions.Right;
                lrt.sizeDelta = new Vector2(LABEL_W, ROW_H);
                lrt.anchoredPosition = new Vector2(LABEL_X, -ROW_H / 2f);
            }
            // (n-1) pitches + one row height + 2×PAD so bottom margin matches top regardless of PITCH>ROW_H.
            float height = n > 0 ? (n - 1) * PITCH + ROW_H + 2f * PAD : 0f;
            _root.sizeDelta = new Vector2(CONTENT_W, height);
        }

        // Horizontal strip (trader/craft): cells left-to-right, label sized to text, panel width grows.
        private void LayoutHorizontal(int n)
        {
            if (_root == null) return;
            float x = PAD;  // left edge of the next cell, from the panel's left
            for (int i = 0; i < n; i++)
            {
                var row = _rows[i];
                bool dual = row.Icon2.gameObject.activeSelf;
                row.Label.alignment = TextAlignmentOptions.Left;

                float labelW = Mathf.Ceil(Mathf.Max(1f, row.Label.GetPreferredValues().x));
                float iconBlock = dual ? (ICON + H_ICON2_GAP + ICON) : ICON;
                float cellW = iconBlock + H_ICON_GAP + labelW;

                row.Rt.sizeDelta = new Vector2(cellW, ROW_H);
                row.Rt.anchoredPosition = new Vector2(x + cellW / 2f, -(PAD + ROW_H / 2f));

                ((RectTransform)row.Icon.transform).anchoredPosition = new Vector2(ICON / 2f, -ROW_H / 2f);
                if (dual)
                    ((RectTransform)row.Icon2.transform).anchoredPosition =
                        new Vector2(ICON + H_ICON2_GAP + ICON / 2f, -ROW_H / 2f);

                var lrt = (RectTransform)row.Label.transform;
                lrt.sizeDelta = new Vector2(labelW, ROW_H);
                lrt.anchoredPosition = new Vector2(iconBlock + H_ICON_GAP + labelW / 2f, -ROW_H / 2f);

                x += cellW + H_CELL_GAP;
            }
            float width = n > 0 ? x - H_CELL_GAP + PAD : 0f; // remove trailing gap, add right PAD
            float panelH = n > 0 ? ROW_H + 2f * PAD : 0f;
            _root.sizeDelta = new Vector2(width, panelH);
        }

        // Grab any TMP font from the active canvas; fall back to TMP default. Per-refresh (canvas can change).
        private static TMP_FontAsset? ResolveFont(Canvas canvas)
        {
            var tmp = canvas.GetComponentInChildren<TextMeshProUGUI>(includeInactive: false);
            if (tmp != null && tmp.font != null) return tmp.font;
            return TMP_Settings.defaultFontAsset;
        }

        private static void EnsureBgSprite()
        {
            if (_bgSprite != null) return;
            const int size = 64;
            const float cornerRadius = 18f;
            const float softness = 1.5f;
            const float margin = 1f;
            const int slice = 22;

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            float c = size * 0.5f;
            float half = c - margin;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) - c;
                    float dy = (y + 0.5f) - c;
                    float sd = SdRoundBox(dx, dy, half, half, cornerRadius);
                    // Alpha 1 inside (sd<=0), fades over [0,softness].
                    // Use hand-rolled SmoothStep01, NOT Mathf.SmoothStep (lerps from/to → blanks texture).
                    float a = 1f - SmoothStep01(0f, softness, sd);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);

            var border = new Vector4(slice, slice, slice, slice);
            _bgSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 100f, extrude: 0, meshType: SpriteMeshType.FullRect, border: border);
        }

        private static float SmoothStep01(float edge0, float edge1, float x)
        {
            if (edge1 <= edge0) return x < edge0 ? 0f : 1f;
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        private static float SdRoundBox(float px, float py, float bx, float by, float r)
        {
            float qx = Mathf.Abs(px) - bx + r;
            float qy = Mathf.Abs(py) - by + r;
            float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
            float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
            return outside + inside - r;
        }

        private void OnDestroy()
        {
            // OnDisable runs before OnDestroy and clears these, but guard against a surviving subscription.
            if (_sceneHooksSubscribed)
            {
                Duckov.UI.View.OnActiveViewChanged -= OnSceneContextChanged;
                LevelManager.OnAfterLevelInitialized -= OnSceneContextChanged;
                _sceneHooksSubscribed = false;
            }
            if (_root != null) Destroy(_root.gameObject);
            _root = null;
            _bg = null;
            _parentCanvas = null;
            _rows.Clear();
        }
    }
}
