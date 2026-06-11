using UnityEngine;
using UnityEngine.UI;

namespace DuckovController.UI
{
    // Neon focus frame: 9-sliced SDF border + glow parented under the target's Canvas.
    // Sits outside the item (root expanded by thicknessPx). Show() repositions; Hide() disables.
    internal sealed class FocusOutlineOverlay : MonoBehaviour
    {
        private RectTransform? _root;
        private Image? _glow;
        private Image? _border;
        private Image? _core; // thin white core → yellow/white/yellow neon cross-section
        private Canvas? _parentCanvas;

        private static Sprite? _borderSprite;
        private static Sprite? _coreSprite;
        private static Sprite? _glowSprite;

        // How far the glow halo extends beyond the border (canvas units).
        private const float GlowPad = 18f;

        internal void Show(RectTransform target, Color color, float thicknessPx)
        {
            if (target != null)
            {
                // Prefer inner ItemDisplay so frame reads as "this item" not "this slot card".
                var inner = FindChildOfType(target, "ItemDisplay");
                if (inner != null) target = inner;
            }
            if (target == null) { Hide(); return; }

            // Pre-layout/pooled nodes report size-0 for a frame or two → skip to avoid "1x1 corner" flash.
            var tsz = target.rect.size;
            if (tsz.x <= 1f || tsz.y <= 1f) { Hide(); return; }

            var canvas = target.GetComponentInParent<Canvas>();
            if (canvas == null) { Hide(); return; }

            EnsureBuilt(canvas);
            if (_root == null || _border == null || _glow == null) return;

            // Reparent under target's parent for same coordinate space + correct clipping inside masked scroll viewports.
            if (_root.parent != target.parent)
                _root.SetParent(target.parent, worldPositionStays: false);
            _root.anchorMin = target.anchorMin;
            _root.anchorMax = target.anchorMax;
            _root.pivot = target.pivot;
            _root.anchoredPosition = target.anchoredPosition;
            _root.sizeDelta = target.sizeDelta + new Vector2(thicknessPx * 2f, thicknessPx * 2f);
            _root.localScale = Vector3.one;
            _root.localRotation = Quaternion.identity;
            _root.SetAsLastSibling();

            StretchFill(_border.rectTransform, 0f);
            StretchFill(_glow.rectTransform, GlowPad);
            if (_core != null) StretchFill(_core.rectTransform, 0f);

            _border.color = Brighten(color);
            _glow.color = Glow(color);
            if (_core != null) _core.color = Color.white; // bright neon core

            _root.gameObject.SetActive(true);
        }

        internal void Hide()
        {
            if (_root != null) _root.gameObject.SetActive(false);
        }

        // build

        private void EnsureBuilt(Canvas canvas)
        {
            if (_root != null && _parentCanvas == canvas) return;

            EnsureSprites();

            var go = new GameObject("DuckovController.FocusOutline",
                typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(canvas.transform, worldPositionStays: false);
            _root = (RectTransform)go.transform;
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.sizeDelta = Vector2.zero;
            _root.anchoredPosition = Vector2.zero;
            go.GetComponent<LayoutElement>().ignoreLayout = true;

            _glow = MakeImage("Glow", _root, _glowSprite!);       // behind
            _border = MakeImage("Border", _root, _borderSprite!); // gold ring
            _core = MakeImage("Core", _root, _coreSprite!);       // white core on top
            _parentCanvas = canvas;
        }

        private static Image MakeImage(string name, RectTransform parent, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, worldPositionStays: false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            return img;
        }

        private static void StretchFill(RectTransform rt, float pad)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(-pad, -pad);
            rt.offsetMax = new Vector2(pad, pad);
            rt.localScale = Vector3.one;
        }

        private static Color Brighten(Color c)
        {
            Color.RGBToHSV(c, out var h, out var s, out var v);
            var bright = Color.HSVToRGB(h, s * 0.9f, Mathf.Clamp01(v * 1.15f + 0.05f));
            bright.a = 1f;
            return bright;
        }

        private static Color Glow(Color c)
        {
            Color.RGBToHSV(c, out var h, out var s, out var v);
            var glow = Color.HSVToRGB(h, Mathf.Clamp01(s * 1.1f), v * 0.8f);
            glow.a = 0.5f;
            return glow;
        }

        // procedural sprites

        private static void EnsureSprites()
        {
            if (_borderSprite != null && _coreSprite != null && _glowSprite != null) return;
            // Neon cross-section: gold ring + thinner white core on top (yellow / white / yellow).
            _borderSprite = BuildRoundedRing(size: 64, cornerRadius: 26, halfBand: 2.5f, softness: 1.5f, slice: 30, margin: 1f);
            _coreSprite = BuildRoundedRing(size: 64, cornerRadius: 26, halfBand: 0.8f, softness: 1.2f, slice: 30, margin: 1f);
            // Glow: outward halo only (zero inside the boundary); falloff to 0 within GlowPad.
            _glowSprite = BuildRoundedRing(size: 96, cornerRadius: 22, halfBand: 2f, softness: 10f, slice: 34, margin: 18f, outwardOnly: true);
        }

        // SDF rounded-rectangle ring: alpha peaks on boundary, falls off over halfBand+softness.
        // 9-slice `slice` must exceed cornerRadius so corners live in corner slices.
        private static Sprite BuildRoundedRing(int size, float cornerRadius, float halfBand, float softness, int slice, float margin, bool outwardOnly = false)
        {
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
                    // Symmetric ring: |sd|. Outward-only (glow): zero inside boundary.
                    float a;
                    if (outwardOnly)
                        a = sd < 0f ? 0f : 1f - SmoothStep01(halfBand, halfBand + softness, sd);
                    else
                        a = 1f - SmoothStep01(halfBand, halfBand + softness, Mathf.Abs(sd));
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);

            var border = new Vector4(slice, slice, slice, slice);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 100f, extrude: 0, meshType: SpriteMeshType.FullRect, border: border);
        }

        // GLSL smoothstep (NOT Mathf.SmoothStep which lerps from/to — that produced all-transparent textures).
        private static float SmoothStep01(float edge0, float edge1, float x)
        {
            if (edge1 <= edge0) return x < edge0 ? 0f : 1f;
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        // Signed distance to a rounded box centered at origin (half-extents b, radius r).
        private static float SdRoundBox(float px, float py, float bx, float by, float r)
        {
            float qx = Mathf.Abs(px) - bx + r;
            float qy = Mathf.Abs(py) - by + r;
            float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
            float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
            return outside + inside - r;
        }

        private static RectTransform? FindChildOfType(RectTransform root, string componentTypeName)
        {
            var comps = root.GetComponentsInChildren<Component>(includeInactive: false);
            foreach (var comp in comps)
            {
                if (comp == null) continue;
                if (comp.GetType().Name == componentTypeName)
                    return comp.transform as RectTransform;
            }
            return null;
        }

        private void OnDestroy()
        {
            if (_root != null) Destroy(_root.gameObject);
            _root = null;
            _glow = _border = _core = null;
            _parentCanvas = null;
        }
    }
}
