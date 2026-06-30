using System.Collections.Generic;
using System.Reflection;
using Duckov.UI;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DuckovController.UI
{
    // Overlays verb-map ButtonGlyphHints on named view buttons (e.g. X on Confirm). Gamepad-gated; rebuilt on view change.
    internal sealed partial class GridFocusController : MonoBehaviour
    {
        private IViewVerbMap? _buttonHintsMap;
        private readonly List<GameObject> _buttonHintImages = new();
        private const float ButtonHintTargetPx = 44f;

        private void UpdateButtonHints(View view)
        {
            var map = ViewVerbMapRegistry.For(view);
            var hints = map?.ButtonGlyphHints();
            if (Gamepad.current == null || map == null || hints == null || hints.Count == 0)
            {
                RevertButtonHints();
                return;
            }
            // Rebuild only when the map (hence the view) changes.
            if (!ReferenceEquals(map, _buttonHintsMap)) RevertButtonHints();
            if (_buttonHintImages.Count > 0) return;

            _buttonHintsMap = map;
            foreach (var (field, glyph) in hints)
            {
                var btn = ResolveViewButton(view, field);
                if (btn == null) continue;
                var img = CreateButtonGlyph(btn, glyph);
                if (img != null) _buttonHintImages.Add(img.gameObject);
            }
        }

        private void RevertButtonHints()
        {
            for (int i = 0; i < _buttonHintImages.Count; i++)
                if (_buttonHintImages[i] != null) Destroy(_buttonHintImages[i]);
            _buttonHintImages.Clear();
            _buttonHintsMap = null;
            RevertDecomposeNativeGlyph();
        }

        // ItemDecomposeView: the decompose button carries a native "F" key prompt (its child
        // InputIndicator). While a gamepad is connected we hide it so only our X glyph (placed on the
        // button by ButtonGlyphHints) shows — the X visually REPLACES the F, in place. Reverts on KBM /
        // view change via RevertDecomposeNativeGlyph. Idempotent per frame (the game re-shows the
        // InputIndicator whenever the button is re-activated for a newly selected item).
        private GameObject? _decomposeNativeIndicator;
        private void UpdateDecomposeNativeGlyph(View view)
        {
            if (view == null || view.GetType().Name != "ItemDecomposeView") { RevertDecomposeNativeGlyph(); return; }
            var btn = ResolveViewButton(view, "decomposeButton");
            if (Gamepad.current == null || btn == null) { RevertDecomposeNativeGlyph(); return; }

            // Child named "InputIndicator" directly under the decompose button.
            var indicatorT = btn.transform.Find("InputIndicator");
            if (indicatorT == null) return;
            _decomposeNativeIndicator = indicatorT.gameObject;
            if (_decomposeNativeIndicator.activeSelf)
                _decomposeNativeIndicator.SetActive(false);
        }

        private void RevertDecomposeNativeGlyph()
        {
            if (_decomposeNativeIndicator == null) return;
            if (_decomposeNativeIndicator != null && !_decomposeNativeIndicator.activeSelf)
                _decomposeNativeIndicator.SetActive(true);
            _decomposeNativeIndicator = null;
        }

        // Glyph parented to LABEL (not button): robust to degenerate button rects (RepairAllButton = 0,0)
        // and FadeGroup-gated inactive buttons (RepairButton) — anchors resolve at render time.
        private static Image? CreateButtonGlyph(Button button, ButtonGlyph glyph)
        {
            var sprite = GlyphProvider.Get(glyph);
            if (sprite == null) return null;

            var go = new GameObject("GlyphHint(Controller)", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.localScale = Vector3.one;
            rt.sizeDelta = new Vector2(ButtonHintTargetPx, ButtonHintTargetPx);
            rt.pivot = new Vector2(1f, 0.5f); // glyph's right edge is the anchor point

            const float gap = 12f;
            // includeInactive: RepairButton's label is inactive until item selected; glyph still needs to parent ahead of time.
            var label = button.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            if (label != null)
            {
                rt.SetParent(label.rectTransform, worldPositionStays: false);
                // Anchor to the label CENTRE offset by half the text width so the glyph seats just
                // left of the RENDERED text — i.e. INSIDE the button. These action-button labels
                // (Register/Accept/Decompose/Craft/RepairAll) are full-button-width with centred
                // text, so anchoring to the label's left EDGE would dump the glyph at the button's
                // far-left edge (reading as "beside" the button). Mirrors CreateConfirmGlyph.
                float halfText = 0f;
                try { halfText = label.preferredWidth * 0.5f; } catch { }
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); // label centre
                rt.anchoredPosition = new Vector2(-(halfText + gap), 0f);
            }
            else
            {
                // No label found — fall back to the button's left-centre.
                rt.SetParent(button.transform, worldPositionStays: false);
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.anchoredPosition = new Vector2(-gap, 0f);
            }

            var le = go.AddComponent<LayoutElement>();
            le.ignoreLayout = true;

            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        private static Button? ResolveViewButton(View view, string fieldName)
        {
            if (view == null || string.IsNullOrEmpty(fieldName)) return null;
            var f = ReflectionUtil.WalkField(view.GetType(), fieldName);
            if (f == null) return null;
            var val = f.GetValue(view);
            if (val is Button b) return b;
            // Field may be a wrapper (e.g. ItemRepairView.repairAllPanel : ItemRepair_RepairAllPanel) — find first Button in subtree.
            if (val is Component c && c != null) return c.GetComponentInChildren<Button>(true);
            return null;
        }
    }
}
