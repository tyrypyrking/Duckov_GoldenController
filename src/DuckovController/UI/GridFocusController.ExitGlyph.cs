using System.Reflection;
using Duckov.UI;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DuckovController.UI
{
    // Swaps the exit button's icon to the B glyph while a gamepad is connected; reverts on KBM / view change.
    // Exit button excluded from dpad nav (IsViewExitButton) so this replaces the chevron.
    internal sealed partial class GridFocusController : MonoBehaviour
    {
        private static FieldInfo? _viewExitButtonField;
        private Image? _exitGlyphImage;
        private Sprite? _exitGlyphOriginalSprite;
        private Vector3 _exitGlyphOriginalScale;
        private const float ExitGlyphTargetPx = 40f;

        // "Esc" label beside glyph so exit affordance is legible. Created with glyph, destroyed on revert.
        private TMPro.TextMeshProUGUI? _exitEscLabel;
        private const string ExitLabelText = "Esc";

        // Called each frame for a supported view. Swaps the exit-button icon to
        // the B glyph while a gamepad is connected; otherwise reverts.
        private void UpdateExitGlyph(View view)
        {
            var icon = ResolveExitIcon(view);
            if (Gamepad.current == null || icon == null) { RevertExitGlyph(); return; }

            // ATM: two back buttons; B steps gradually (keypad→Select→close) — hide Title close to keep one B glyph.
            if (view.GetType().Name == "ATMView") SyncAtmTitleExit(view);

            // Icon changed (different view) — restore the previous one first.
            if (_exitGlyphImage != null && !ReferenceEquals(icon, _exitGlyphImage))
                RevertExitGlyph();

            var sprite = GlyphProvider.Get(ButtonGlyph.B);
            if (sprite == null) return;

            if (_exitGlyphImage == null)
            {
                _exitGlyphImage = icon;
                _exitGlyphOriginalSprite = icon.sprite;
                _exitGlyphOriginalScale = icon.rectTransform.localScale;
            }
            if (icon.sprite != sprite)
            {
                icon.sprite = sprite;
                icon.preserveAspect = true;
            }
            // Normalize size across views; rect.width is reliable across anchor modes.
            float w = icon.rectTransform.rect.width;
            if (w > 1f)
                icon.rectTransform.localScale = _exitGlyphOriginalScale * (ExitGlyphTargetPx / w);

            EnsureEscLabel(view, icon);
        }

        private void RevertExitGlyph()
        {
            // Restore the ATM Title close if we'd hidden it for a keypad pane.
            if (_atmHiddenTitleExit != null)
            {
                _atmHiddenTitleExit.SetActive(true);
                _atmHiddenTitleExit = null;
            }
            if (_exitEscLabel != null)
            {
                Destroy(_exitEscLabel.gameObject);
                _exitEscLabel = null;
            }
            if (_exitGlyphImage == null) return;
            _exitGlyphImage.sprite = _exitGlyphOriginalSprite;
            _exitGlyphImage.rectTransform.localScale = _exitGlyphOriginalScale;
            _exitGlyphImage = null;
            _exitGlyphOriginalSprite = null;
        }

        // "Esc" label right of glyph, styled from a view TMP sample. Built once per view; ignoreLayout.
        private void EnsureEscLabel(View view, Image icon)
        {
            if (_exitEscLabel != null) return;
            var parent = icon.transform.parent != null ? icon.transform.parent : icon.transform;

            // Sample BEFORE creating our label — else GetComponentInChildren returns our own empty TMP (null font).
            var sample = view != null ? view.GetComponentInChildren<TMPro.TextMeshProUGUI>(false) : null;

            var go = new GameObject("EscHint(Controller)", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, worldPositionStays: false);
            rt.localScale = Vector3.one;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(38f, 0f); // just right of the centered icon
            rt.sizeDelta = new Vector2(120f, 44f);

            var le = go.AddComponent<LayoutElement>();
            le.ignoreLayout = true;

            var tmp = go.AddComponent<TMPro.TextMeshProUGUI>();
            if (sample != null)
            {
                tmp.font = sample.font;
                tmp.fontSharedMaterial = sample.fontSharedMaterial;
            }
            tmp.text = ExitLabelText;
            tmp.fontSize = 28f;
            tmp.color = Color.white;
            tmp.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
            tmp.enableWordWrapping = false;
            tmp.raycastTarget = false;
            _exitEscLabel = tmp;
        }

        // View.exitButton: base-class serialized field, cached once. Icon child = "Graphics/Icon" across all views.
        private static Image? ResolveExitIcon(View view)
        {
            if (view == null) return null;

            // ATMView: host glyph on whichever back button B currently triggers (keypad pane vs Select pane).
            if (view.GetType().Name == "ATMView")
                return IconOf(ResolveAtmBackButton(view));

            if (_viewExitButtonField == null)
                _viewExitButtonField = ReflectionUtil.WalkField(view.GetType(), "exitButton");
            var btn = _viewExitButtonField?.GetValue(view) as Button;
            if (btn == null)
            {
                // Some views leave exitButton null (e.g. EndowmentSelectionPanel.cancelButton) — fall back to "ExitButton" descendant.
                var t = TransformHelpers.FindDescendantByName(view.transform, "ExitButton");
                btn = t != null ? t.GetComponent<Button>() : null;
            }
            if (btn == null) return null;
            var iconT = btn.transform.Find("Graphics/Icon");
            return iconT != null ? iconT.GetComponent<Image>() : null;
        }

        // Hide ATM Title close while keypad pane is open (pane's own quit is back); restore on Select pane/exit.
        private GameObject? _atmHiddenTitleExit;
        private void SyncAtmTitleExit(View view)
        {
            var title = ViewExitButtonGo(view);
            if (title == null) return;
            bool keypadPane = view.GetComponentInChildren<DigitInputPanel>(false) != null;
            if (keypadPane)
            {
                if (title.activeSelf) { title.SetActive(false); _atmHiddenTitleExit = title; }
            }
            else if (_atmHiddenTitleExit != null)
            {
                _atmHiddenTitleExit.SetActive(true);
                _atmHiddenTitleExit = null;
            }
        }

        private static GameObject? ViewExitButtonGo(View view)
        {
            if (_viewExitButtonField == null)
                _viewExitButtonField = ReflectionUtil.WalkField(view.GetType(), "exitButton");
            return _viewExitButtonField?.GetValue(view) is Button b ? b.gameObject : null;
        }

        // ATM B target: active pane's quit button while keypad is up, else the Title exit.
        private static Button? ResolveAtmBackButton(View view)
        {
            if (view.GetComponentInChildren<DigitInputPanel>(false) != null)
            {
                // Keypad pane → OperationTitleBar/Btn_Exit.
                var bar = FindActiveDescendantByName(view.transform, "OperationTitleBar");
                var bt = bar != null ? bar.Find("Btn_Exit") : null;
                if (bt != null) return bt.GetComponent<Button>();
            }
            // Select pane → the view-level title exit button.
            if (_viewExitButtonField == null)
                _viewExitButtonField = ReflectionUtil.WalkField(view.GetType(), "exitButton");
            return _viewExitButtonField?.GetValue(view) as Button;
        }

        // "Graphics/Icon" child if present; else first non-raycast descendant Image (the arrow).
        // Raycast targets are click-area/background — targeting those leaves the arrow over our glyph.
        private static Image? IconOf(Button? btn)
        {
            if (btn == null) return null;
            var iconT = btn.transform.Find("Graphics/Icon");
            if (iconT != null) return iconT.GetComponent<Image>();
            Image? firstDescendant = null;
            foreach (var img in btn.GetComponentsInChildren<Image>(true))
            {
                if (img == null || img.gameObject == btn.gameObject) continue;
                firstDescendant ??= img;
                if (!img.raycastTarget) return img;   // the visual icon (the arrow)
            }
            return firstDescendant;
        }

        private static Transform? FindActiveDescendantByName(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name && root.gameObject.activeInHierarchy) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindActiveDescendantByName(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
