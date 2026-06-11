using System.Reflection;
using DuckovController.UI;
using Duckov.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DuckovController.Patches
{
    // ItemHoveringUI normally follows the mouse. With controller focus the cursor is locked to the item centre
    // so the panel covers the item. This prefix places it BESIDE the focused item (side with more room) while
    // GridFocusController.IsDrivingFocus(). KBM unaffected; any failure falls back to vanilla.
    [HarmonyPatch(typeof(ItemHoveringUI), "RefreshPosition")]
    internal static class ItemHoveringUIPatch
    {
        private static FieldInfo? _contentsField;
        private static FieldInfo? _rectField;
        private static FieldInfo? _indicatorsField;

        [HarmonyPrefix]
        internal static bool Prefix(ItemHoveringUI __instance)
        {
            if (__instance == null) return true;
            var inst = GridFocusController.Instance;
            if (inst == null || !GridFocusController.IsDrivingFocus()) return true; // vanilla mouse-follow

            try
            {
                _contentsField ??= AccessTools.Field(typeof(ItemHoveringUI), "contents");
                _rectField ??= AccessTools.Field(typeof(ItemHoveringUI), "rectTransform");
                if (_contentsField?.GetValue(__instance) is not RectTransform contents) return true;
                if (_rectField?.GetValue(__instance) is not RectTransform rect) return true;

                // Strip the KBM hint bar (SetupAndShow re-enables it each hover). Force layout rebuild so
                // the panel size is correct this frame — stale large size pushes tall panels off the top.
                _indicatorsField ??= AccessTools.Field(typeof(ItemHoveringUI), "interactionIndicatorsContainer");
                if (_indicatorsField?.GetValue(__instance) is GameObject indicators && indicators.activeSelf)
                {
                    indicators.SetActive(false);
                    var layoutParent = __instance.LayoutParent;
                    if (layoutParent != null) LayoutRebuilder.ForceRebuildLayoutImmediate(layoutParent);
                }

                if (!inst.TryGetHoverInfoAnchor(contents, out var screenTopLeft)) return true;

                // Pivot-agnostic: measure current top-left corner, shift anchoredPosition by delta to desired top-left.
                // (Panels are centre-pivoted; writing top-left directly into anchoredPosition offsets half-a-width.)
                var corners = new Vector3[4];
                contents.GetWorldCorners(corners);            // [1] = top-left
                Vector2 curTopLeft = RectTransformUtility.WorldToScreenPoint(null, corners[1]);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenTopLeft, null, out var wantLocal);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, curTopLeft, null, out var curLocal);
                contents.anchoredPosition += (wantLocal - curLocal);
                return false; // skip the vanilla mouse-follow
            }
            catch
            {
                return true; // on any failure, defer to vanilla
            }
        }
    }
}
