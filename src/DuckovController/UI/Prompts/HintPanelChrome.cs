using UnityEngine;

namespace DuckovController.UI.Prompts
{
    // Shared chrome for console-styled hint panels (the bottom-right ViewHintPanel and the menu-world
    // MenuHintPanel). One rounded-rect background sprite + the math behind it, generated once and cached.
    internal static class HintPanelChrome
    {
        // Vertical-stack row metrics (measured from GamingConsoleHUD_20260531_210655_405.txt).
        internal const float MARGIN = 32f;
        internal const float PAD = 16f;
        internal const float ROW_W = 302.1f;
        internal const float ROW_H = 52.1f;
        internal const float PITCH = 58.1f;   // ROW_H + ~6px so stacked glyphs don't touch
        internal const float CONTENT_W = 334.1f;
        internal const float ICON = 52.1f;
        internal const float ICON_X = 26.1f;
        internal const float ICON2_X = 92.2f; // paired second glyph (ICON_X + ICON + ~14 spacer)
        internal const float LABEL_X = 185.1f;
        internal const float LABEL_W = 234f;

        // Panel background tint (dark, ~50% alpha).
        internal static readonly Color BgColor = new Color(0.05f, 0.06f, 0.09f, 0.5f);

        private static Sprite? _bgSprite;

        // Rounded-rect sliced sprite for a hint-panel background. Generated once, cached static.
        internal static Sprite RoundedBgSprite()
        {
            if (_bgSprite != null) return _bgSprite;
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
                    // Hand-rolled SmoothStep01, NOT Mathf.SmoothStep (that lerps from/to → blanks texture).
                    float a = 1f - SmoothStep01(0f, softness, sd);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);

            var border = new Vector4(slice, slice, slice, slice);
            _bgSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f),
                pixelsPerUnit: 100f, extrude: 0, meshType: SpriteMeshType.FullRect, border: border);
            return _bgSprite;
        }

        internal static float SmoothStep01(float edge0, float edge1, float x)
        {
            if (edge1 <= edge0) return x < edge0 ? 0f : 1f;
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        internal static float SdRoundBox(float px, float py, float bx, float by, float r)
        {
            float qx = Mathf.Abs(px) - bx + r;
            float qy = Mathf.Abs(py) - by + r;
            float outside = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f));
            float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
            return outside + inside - r;
        }

        // Grab any TMP font from a canvas; fall back to TMP default. Caller passes the active canvas.
        internal static TMPro.TMP_FontAsset? ResolveFont(Canvas canvas)
        {
            var tmp = canvas.GetComponentInChildren<TMPro.TextMeshProUGUI>(includeInactive: false);
            if (tmp != null && tmp.font != null) return tmp.font;
            return TMPro.TMP_Settings.defaultFontAsset;
        }
    }
}
