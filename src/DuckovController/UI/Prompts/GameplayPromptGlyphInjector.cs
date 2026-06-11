using System;
using System.Reflection;
using DuckovController.UI.Common;
using UnityEngine;
using UnityEngine.UI;

namespace DuckovController.UI.Prompts
{
    // Swaps native gameplay InputIndicators to controller glyphs (pad never pairs on Deck →
    // keyboard text shown). Mappings: Interact HUD → Y, "Cancle" cancel → B, TaskSkipperUI → hold A,
    // fishing → A. Re-applied on every refresh so Refresh doesn't revert to keyboard.
    internal sealed class GameplayPromptGlyphInjector : MonoBehaviour
    {

        private void OnEnable()  => InputIndicator.OnAfterRefresh += OnIndicatorRefreshed;
        private void OnDisable() => InputIndicator.OnAfterRefresh -= OnIndicatorRefreshed;

        private void OnIndicatorRefreshed(InputIndicator ind)
        {
            try
            {
                if (!DuckovController.Diagnostics.PerfFlags.GlyphInjectors) return;
                if (ind == null) return;
                if (UnityEngine.InputSystem.Gamepad.current == null) return;

                if (!TryResolve(ind, out var glyph, out bool normalizeSize, out bool skipLabel)) return;

                var sprite = GlyphProvider.Get(glyph);
                if (sprite == null) return;

                var icon          = InputIndicatorFields.Icon(ind);
                var textContainer = InputIndicatorFields.TextContainer(ind);
                var text          = InputIndicatorFields.Text(ind);
                if (icon == null) return;

                icon.sprite = sprite;
                icon.preserveAspect = true;

                // Replicate InputIndicator.ShowIcon().
                textContainer?.SetActive(false);
                icon.gameObject.SetActive(true);
                text?.gameObject.SetActive(false);

                // Face-button glyphs crop to ~96px and render at native size (~3×). Pin to indicator box.
                // Cancel prompt already constrained (normalizeSize=false there).
                if (normalizeSize) NormalizeIconSize(ind, icon);

                // Cutscene skip is a HOLD action; rewrite the prompt to read
                // "Hold (A) to Skip" instead of the bare "(A) Skip".
                if (skipLabel) ApplySkipLabel(ind);
            }
            catch (Exception e)
            {
                Log.Debug_($"GameplayPromptGlyphInjector: {e.Message}");
            }
        }

        // Maps indicator to glyph + normalizeSize + skipLabel. Returns false to leave alone.
        private static bool TryResolve(InputIndicator ind, out ButtonGlyph glyph, out bool normalizeSize, out bool skipLabel)
        {
            glyph = default;
            normalizeSize = false;
            skipLabel = false;

            // "Cancle" (game's typo): action/skill cancel bar. B = StopAction. Already constrained.
            var parent = ind.transform.parent;
            if (parent != null && parent.name == "Cancle") { glyph = ButtonGlyph.B; return true; }

            // Interact HUD: Y = short-tap Interact.
            if (ind.GetComponentInParent<InteractSelectionHUD>() != null
                || ind.GetComponentInParent<InteractHUD>() != null)
            {
                glyph = ButtonGlyph.Y;
                normalizeSize = true;
                return true;
            }

            // TaskSkipperUI: A = hold to skip.
            if (ind.GetComponentInParent<TaskSkipperUI>() != null)
            {
                glyph = ButtonGlyph.A;
                normalizeSize = true;
                skipLabel = true;
                return true;
            }

            // Fishing reel-in: native resolves Dash (Space) since pad never pairs. A = catch.
            // Checked last so Cancle/Interact/Skip keep their glyphs.
            // First refresh fires inside Action_FishingV2.OnStart before currentAction assigned;
            // FishingInputHandler pokes NotifyBindingChanged to re-refresh once this gate sees it.
            if (IsFishingActive())
            {
                glyph = ButtonGlyph.A;
                normalizeSize = true;
                return true;
            }

            return false;
        }

        // Type-name match avoids compile-time dependency on the game's fishing type.
        private static bool IsFishingActive()
        {
            var ca = CharacterMainControl.Main?.CurrentAction;
            return ca != null && ca.Running && ca.GetType().Name == "Action_FishingV2";
        }

        // Rewrites skip prompt to "Hold (A) to Skip". Sibling label → "to Skip";
        // one-off "Hold" prefix inserted (guarded by DC_HoldPrefix name). Idempotent.
        private static void ApplySkipLabel(InputIndicator ind)
        {
            try
            {
                var row = ind.transform.parent;
                if (row == null) return;

                // Visible label = TMP under the row but not inside the InputIndicator.
                TMPro.TextMeshProUGUI? label = null;
                foreach (Transform child in row)
                {
                    if (child == ind.transform) continue;
                    var tmp = child.GetComponent<TMPro.TextMeshProUGUI>()
                              ?? child.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                    if (tmp != null && tmp.GetComponentInParent<InputIndicator>() == null) { label = tmp; break; }
                }
                if (label == null) return;

                DisableLocalizor(label.gameObject);
                if (label.text != "to Skip") label.text = "to Skip";

                if (row.Find("DC_HoldPrefix") == null)
                {
                    var clone = UnityEngine.Object.Instantiate(label.gameObject, row);
                    clone.name = "DC_HoldPrefix";
                    clone.transform.SetSiblingIndex(0);   // leftmost → "Hold (A) to Skip"
                    DisableLocalizor(clone);
                    var ctmp = clone.GetComponent<TMPro.TextMeshProUGUI>()
                               ?? clone.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                    if (ctmp != null) ctmp.text = "Hold";
                }
            }
            catch (Exception e)
            {
                Log.Debug_($"GameplayPromptGlyphInjector.ApplySkipLabel: {e.Message}");
            }
        }

        // Disable TextLocalizor (if present) so it can't overwrite our text. Soft-typed.
        private static void DisableLocalizor(GameObject go)
        {
            var loc = go.GetComponent("TextLocalizor") as Behaviour;
            if (loc != null) loc.enabled = false;
        }

        // Pins icon to indicator's own box so 96px face-button glyph doesn't render at native size.
        private static void NormalizeIconSize(InputIndicator ind, Image icon)
        {
            float target = 36f;
            var indRt = ind.transform as RectTransform;
            if (indRt != null)
            {
                float h = indRt.rect.height;
                if (h > 8f) target = Mathf.Clamp(h, 24f, 48f);
            }

            var hlg = ind.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
            {
                hlg.childControlWidth = true;
                hlg.childForceExpandWidth = false;
            }

            var le = icon.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.preferredWidth = target;
                le.preferredHeight = target;
                le.flexibleWidth = 0f;
                le.flexibleHeight = 0f;
            }
            icon.rectTransform.sizeDelta = new Vector2(target, target);
        }

    }
}
