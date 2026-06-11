using System.Collections.Generic;
using System.IO;
using DuckovController.UI.Common;
using UnityEngine;

namespace DuckovController.UI.Prompts
{
    // Loads glyph PNGs → Sprite (PNG → Texture2D.LoadImage → Sprite.Create). Lazy, once per glyph.
    internal static class GlyphProvider
    {
        // Set at startup (ModBehaviour.info.path).
        internal static string? ModRoot;

        internal static IGlyphProfile Profile = new SteamDeckGlyphProfile();

        // Null entries cached too — missing asset logged+probed only once.
        private static readonly Dictionary<ButtonGlyph, Sprite?> _cache = new();

        internal static Sprite? Get(ButtonGlyph glyph)
        {
            if (_cache.TryGetValue(glyph, out var cached)) return cached;
            var sprite = Load(glyph);
            _cache[glyph] = sprite;
            return sprite;
        }

        private static Sprite? Load(ButtonGlyph glyph)
        {
            if (string.IsNullOrEmpty(ModRoot))
            {
                Log.Warn("GlyphProvider.ModRoot not set; cannot load glyphs.");
                return null;
            }
            var file = Profile.FileFor(glyph);
            if (string.IsNullOrEmpty(file))
            {
                Log.Debug_($"GlyphProvider: no mapping for {glyph} in {Profile.DirectoryName}.");
                return null;
            }
            var path = Path.Combine(ModRoot, "assets", "glyphs", Profile.DirectoryName, file + ".png");
            if (!File.Exists(path))
            {
                Log.Warn($"GlyphProvider: asset missing: {path}");
                return null;
            }
            try
            {
                var bytes = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
                if (!tex.LoadImage(bytes))
                {
                    Log.Warn($"GlyphProvider: LoadImage failed for {path}");
                    return null;
                }
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                // Kenney glyphs have ~25% transparent padding (e.g. 96/128 opaque).
                // Crop to opaque bounds so glyph fills its Image box, not the padded rect.
                var rect = OpaqueRect(tex);
                var sprite = Sprite.Create(
                    tex,
                    rect,
                    new Vector2(0.5f, 0.5f),
                    pixelsPerUnit: 100f);
                sprite.name = "glyph_" + file;
                Log.Info($"GlyphProvider: loaded {file} ({tex.width}x{tex.height}), opaque {rect.width}x{rect.height}.");
                return sprite;
            }
            catch (System.Exception e)
            {
                Log.Warn($"GlyphProvider: exception loading {path}: {e.Message}");
                return null;
            }
        }

        // Bounding rect of non-transparent pixels (bottom-left origin, Sprite.Create space).
        // Falls back to full texture if entirely transparent.
        private static Rect OpaqueRect(Texture2D tex)
        {
            int w = tex.width, h = tex.height;
            var px = tex.GetPixels32();
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    if (px[row + x].a > 8)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            if (maxX < 0) return new Rect(0, 0, w, h);
            return new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        internal static void Clear() => _cache.Clear();
    }
}
