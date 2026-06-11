using System.Reflection;
using HarmonyLib;
using UnityEngine;

// The enclosing namespace segment "CostDisplay" shadows the global game type of the
// same name, so the game type is referenced via this alias throughout this file.
using GameCostDisplay = global::CostDisplay;

namespace DuckovController.UI.CostDisplay
{
    internal static class DetailedCostListStyler
    {
        private static FieldInfo? _itemsContainerF;
        private static FieldInfo? _iconF;        // Image
        private static FieldInfo? _amountTextF;  // TextMeshProUGUI
        private static bool _resolved;

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            _itemsContainerF = AccessTools.Field(typeof(GameCostDisplay), "itemsContainer");
            _iconF = AccessTools.Field(typeof(ItemAmountDisplay), "icon");
            _amountTextF = AccessTools.Field(typeof(ItemAmountDisplay), "amountText");
        }

        // Returns the active ItemAmountDisplay rows under the CostDisplay's itemsContainer.
        private static ItemAmountDisplay[] Rows(GameCostDisplay cd)
        {
            Resolve();
            if (_itemsContainerF?.GetValue(cd) is not GameObject items) return System.Array.Empty<ItemAmountDisplay>();
            return items.GetComponentsInChildren<ItemAmountDisplay>(includeInactive: false);
        }

        // 1.75x enlargement for the menu cost lists; the in-raid build/fix world
        // billboard (CostTakerHUD_Entry) keeps our readable vertical layout but WITHOUT
        // the enlargement — native is too small to read, 1.75x was comically big. Tune
        // InRaidBuildFixScale on device if 1.0 still reads small in-world.
        internal const float DefaultScale = 1.75f;
        internal const float InRaidBuildFixScale = 1.0f;

        private static void StyleRow(ItemAmountDisplay row, float scale)
        {
            if (_iconF?.GetValue(row) is not UnityEngine.UI.Image icon) return;
            if (_amountTextF?.GetValue(row) is not TMPro.TextMeshProUGUI amount) return;

            var entry = (RectTransform)row.transform;
            var marker = entry.GetComponent<DetailedCostRowMarker>()
                         ?? entry.gameObject.AddComponent<DetailedCostRowMarker>();

            if (!marker.CapturedBaseFont)
            {
                marker.BaseAmountFontSize = amount.fontSize;
                marker.CapturedBaseFont = true;
            }

            // Snapshot the original child geometry once (before any reorder/resize) for restore.
            if (!marker.CapturedLayout)
            {
                marker.CapturedLayout = true;
                var ir0 = (RectTransform)icon.transform;
                marker.IconPos = ir0.anchoredPosition; marker.IconSize = ir0.sizeDelta; marker.IconSibling = ir0.GetSiblingIndex();
                var ar0 = (RectTransform)amount.transform;
                marker.AmountPos = ar0.anchoredPosition; marker.AmountSize = ar0.sizeDelta; marker.AmountSibling = ar0.GetSiblingIndex();
            }

            // Name label — clone amountText once so it inherits theming.
            if (marker.NameLabel == null)
            {
                var clone = Object.Instantiate(amount.gameObject, entry);
                clone.name = "ModItemName";
                marker.NameLabel = clone.GetComponent<TMPro.TextMeshProUGUI>();
                marker.NameLabel.enableWordWrapping = false;
                marker.NameLabel.overflowMode = TMPro.TextOverflowModes.Overflow;
                marker.NameLabel.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
            }
            marker.NameLabel.text = row.MetaData.DisplayName;

            // Row layout: ( count ) [icon] Name, left-aligned, content-hugging.
            var hlg = entry.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>()
                      ?? entry.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            if (hlg == null) return; // entry already carried a conflicting layout group; skip
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.spacing = 8f * scale;
            hlg.childControlWidth = true;  hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false; hlg.childForceExpandHeight = false;

            var csf = entry.GetComponent<UnityEngine.UI.ContentSizeFitter>()
                      ?? entry.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            csf.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;

            // Order: count, icon, name.
            amount.transform.SetSiblingIndex(0);
            icon.transform.SetSiblingIndex(1);
            marker.NameLabel.transform.SetSiblingIndex(2);

            // Scale: icon via LayoutElement, fonts absolute (never multiply — pool reuses rows).
            var iconLE = icon.GetComponent<UnityEngine.UI.LayoutElement>()
                         ?? icon.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            iconLE.preferredWidth = 36f * scale;
            iconLE.preferredHeight = 36f * scale;
            amount.fontSize = marker.BaseAmountFontSize * scale;
            marker.NameLabel.fontSize = marker.BaseAmountFontSize * scale;

            marker.Styled = true;
        }

        private static void SetContainerVertical(GameCostDisplay cd, bool vertical, float scale)
        {
            Resolve();
            if (_itemsContainerF?.GetValue(cd) is not GameObject items) return;

            var marker = items.GetComponent<DetailedCostContainerMarker>()
                         ?? items.AddComponent<DetailedCostContainerMarker>();

            if (vertical)
            {
                // Any LayoutGroup-derived component (the game uses a GridLayoutGroup) blocks
                // AddComponent<VLG> via DisallowMultipleComponent. Snapshot it by type+JSON so
                // restore can recreate the exact group, then remove it so the VLG can be added.
                var existing = items.GetComponent<UnityEngine.UI.LayoutGroup>();
                if (existing != null && existing is not UnityEngine.UI.VerticalLayoutGroup)
                {
                    if (!marker.Captured)
                    {
                        marker.Captured = true;
                        marker.OrigSizeDelta = ((RectTransform)items.transform).sizeDelta;
                        if (existing is UnityEngine.UI.GridLayoutGroup g)
                        {
                            marker.WasGrid = true;
                            marker.GridPadding = g.padding;
                            marker.GridCellSize = g.cellSize;
                            marker.GridSpacing = g.spacing;
                            marker.GridStartCorner = g.startCorner;
                            marker.GridStartAxis = g.startAxis;
                            marker.GridChildAlignment = g.childAlignment;
                            marker.GridConstraint = g.constraint;
                            marker.GridConstraintCount = g.constraintCount;
                        }
                    }
                    Object.DestroyImmediate(existing);
                }

                var vlg = items.GetComponent<UnityEngine.UI.VerticalLayoutGroup>()
                          ?? items.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
                if (vlg == null) return; // defensive — never NRE on a blocked AddComponent again
                vlg.enabled = true;
                vlg.childAlignment = TextAnchor.UpperLeft;
                vlg.spacing = 4f * scale;
                vlg.childControlWidth = true;  vlg.childControlHeight = true;
                vlg.childForceExpandWidth = false; vlg.childForceExpandHeight = false;

                var csf = items.GetComponent<UnityEngine.UI.ContentSizeFitter>();
                if (csf == null) { csf = items.AddComponent<UnityEngine.UI.ContentSizeFitter>(); marker.AddedCsf = true; }
                csf.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            }
            else
            {
                // Tear down everything we added, in reverse, so vanilla layout fully returns.
                var vlg = items.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
                if (vlg != null) Object.DestroyImmediate(vlg);
                if (marker.AddedCsf)
                {
                    var csf = items.GetComponent<UnityEngine.UI.ContentSizeFitter>();
                    if (csf != null) Object.DestroyImmediate(csf);
                    marker.AddedCsf = false;
                }

                // Restore the original container size, then recreate the original group
                // (any type) from the JSON snapshot.
                if (marker.Captured)
                {
                    ((RectTransform)items.transform).sizeDelta = marker.OrigSizeDelta;
                    if (marker.WasGrid && items.GetComponent<UnityEngine.UI.LayoutGroup>() == null)
                    {
                        var g = items.AddComponent<UnityEngine.UI.GridLayoutGroup>();
                        g.padding = marker.GridPadding;
                        g.cellSize = marker.GridCellSize;
                        g.spacing = marker.GridSpacing;
                        g.startCorner = marker.GridStartCorner;
                        g.startAxis = marker.GridStartAxis;
                        g.childAlignment = marker.GridChildAlignment;
                        g.constraint = marker.GridConstraint;
                        g.constraintCount = marker.GridConstraintCount;
                    }
                    marker.Captured = false;
                }
            }
        }

        // True for the ONE in-raid build/fix CostDisplay: the world-space CostTakerHUD_Entry
        // billboard (decompiled CostTakerHUD_Entry hosts a CostDisplay and Setup()s it from a
        // CostTaker; CostTakerHUD pools one per active CostTaker, e.g. a ConstructionSite). That
        // panel is already natively enlarged, so our 1.75x restyle makes it look comical.
        // We detect it by HOST TYPE (positive match) rather than "in a raid", so legitimate
        // in-raid menu cost lists keep their styling. No menu host (CraftView, PerkDetails,
        // BuildingBtnEntry, DemandPanel/SupplyPanel_Entry, ItemDecomposeView, MapSelection*,
        // DeathLotteryCard, CellContextDisplay) sits under a CostTakerHUD_Entry, so this can't
        // false-positive on them.
        internal static bool IsInRaidBuildFixHost(GameCostDisplay cd)
        {
            return cd.GetComponentInParent<CostTakerHUD_Entry>() != null;
        }

        internal static void Apply(GameCostDisplay cd) => Apply(cd, DefaultScale);

        internal static void Apply(GameCostDisplay cd, float scale)
        {
            SetContainerVertical(cd, true, scale);
            foreach (var row in Rows(cd)) StyleRow(row, scale);
        }

        internal static void Restore(GameCostDisplay cd)
        {
            SetContainerVertical(cd, false, DefaultScale);
            if (_itemsContainerF?.GetValue(cd) is not GameObject items) return;
            foreach (var row in items.GetComponentsInChildren<ItemAmountDisplay>(includeInactive: true))
            {
                var marker = row.GetComponent<DetailedCostRowMarker>();
                if (marker == null || !marker.Styled) continue;
                RestoreRow(row, marker);
            }
        }

        private static void RestoreRow(ItemAmountDisplay row, DetailedCostRowMarker marker)
        {
            var entry = (RectTransform)row.transform;
            if (marker.NameLabel != null) { Object.DestroyImmediate(marker.NameLabel.gameObject); marker.NameLabel = null; }

            // Destroy the components we added so the entry returns to the grid's control.
            var hlg = entry.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            if (hlg != null) Object.DestroyImmediate(hlg);
            var csf = entry.GetComponent<UnityEngine.UI.ContentSizeFitter>();
            if (csf != null) Object.DestroyImmediate(csf);

            if (_iconF?.GetValue(row) is UnityEngine.UI.Image icon)
            {
                var iconLE = icon.GetComponent<UnityEngine.UI.LayoutElement>();
                if (iconLE != null) Object.DestroyImmediate(iconLE);
                if (marker.CapturedLayout)
                {
                    var ir = (RectTransform)icon.transform;
                    ir.SetSiblingIndex(marker.IconSibling);
                    ir.anchoredPosition = marker.IconPos; ir.sizeDelta = marker.IconSize;
                }
            }
            if (_amountTextF?.GetValue(row) is TMPro.TextMeshProUGUI amount)
            {
                if (marker.CapturedBaseFont) amount.fontSize = marker.BaseAmountFontSize;
                if (marker.CapturedLayout)
                {
                    var ar = (RectTransform)amount.transform;
                    ar.SetSiblingIndex(marker.AmountSibling);
                    ar.anchoredPosition = marker.AmountPos; ar.sizeDelta = marker.AmountSize;
                }
            }

            marker.Styled = false;
        }
    }
}
