using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DuckovController.UI.Settings
{
    internal static partial class ControllerSettingsPanelBuilder
    {
        // Per-content row counter; key = instanceID of parent RectTransform.
        private static readonly Dictionary<int, int> _rowIndexByParent = new();

        // Row Y: -25, -83, -141, ... (step 58).
        internal const float RowYStart = -25f;
        internal const float RowYStep  = 58f;

        // When non-null, cloned row RTs are appended here for section collapse/reflow tracking.
        internal static System.Collections.Generic.List<RectTransform>? _collectRowsInto;

        // Row width in canvas units (vanilla rows are laid out for this width by their
        // now-destroyed parent layout group). Rows keep this fixed width and are centered
        // within the host-dependent scroll Content (see ApplyRowHorizontalLayout).
        internal const float RowWidth = 1224f;
        internal const float RowHeight = 50f;

        // Center a fixed-width row horizontally within the (now host-dependent) scroll
        // Content. Anchoring X to center keeps the row centered whatever the Content width
        // is — in the 1224-wide main-menu Content this is identical to the old left-anchored
        // x=612 placement; in the wider pause-menu Content it stays centered instead of
        // left-biased. Y anchor/stacking is left to the caller.
        private static void ApplyRowHorizontalLayout(RectTransform rt)
        {
            var aMin = rt.anchorMin; aMin.x = 0.5f; rt.anchorMin = aMin;
            var aMax = rt.anchorMax; aMax.x = 0.5f; rt.anchorMax = aMax;
            var size = rt.sizeDelta; size.x = RowWidth; rt.sizeDelta = size;
            var pos = rt.anchoredPosition; pos.x = 0f; rt.anchoredPosition = pos;
        }

        private static GameObject? CloneDropdownRow(GameObject template, RectTransform parent)
        {
            if (template == null) return null;
            var clone = UnityEngine.Object.Instantiate(template, parent, worldPositionStays: false);
            clone.name = $"Row_{template.name}";
            clone.SetActive(true);

            var rt = clone.transform as RectTransform;
            int key = parent.GetInstanceID();
            if (!_rowIndexByParent.TryGetValue(key, out int idx)) idx = 0;
            _rowIndexByParent[key] = idx + 1;

            if (rt != null)
            {
                Log.Info($"CloneRow[{idx}] BEFORE: name={template.name} childCount={rt.childCount} "
                    + $"size=({rt.sizeDelta.x:F0},{rt.sizeDelta.y:F0})");

                // Kill CSF/layout-group — vanilla sized to RowWidth via parent; lock it ourselves.
                foreach (var csf in clone.GetComponents<ContentSizeFitter>())
                    UnityEngine.Object.DestroyImmediate(csf);
                foreach (var hlg in clone.GetComponents<HorizontalOrVerticalLayoutGroup>())
                    UnityEngine.Object.DestroyImmediate(hlg);

                rt.sizeDelta = new Vector2(RowWidth, RowHeight);

                // Center the fixed-width row in the host-dependent Content; then stack by Y.
                ApplyRowHorizontalLayout(rt);
                var pos = rt.anchoredPosition;
                pos.y = RowYStart - idx * RowYStep;
                rt.anchoredPosition = pos;

                // LayoutElement: defensive in case any ancestor is layout-group-controlled.
                var le = clone.GetComponent<LayoutElement>() ?? clone.AddComponent<LayoutElement>();
                le.minWidth = RowWidth;
                le.preferredWidth = RowWidth;
                le.minHeight = RowHeight;
                le.preferredHeight = RowHeight;
            }

            if (_collectRowsInto != null && rt != null) _collectRowsInto.Add(rt);
            return clone;
        }

        // Make our tab content root fill its host container the way native tabs do.
        // Prefer copying the live Common-tab content RectTransform (project lesson:
        // "measure/copy the native sibling, don't hardcode") so it adapts to both the
        // smaller main-menu OptionsPanel and the larger pause-menu one without us ever
        // knowing the two sizes. Falls back to explicit full stretch if the sibling
        // can't be reached.
        private static void StretchRootToHost(RectTransform root)
        {
            var sibling = FindNativeTabContentSibling(root);
            if (sibling != null)
            {
                root.anchorMin = sibling.anchorMin;
                root.anchorMax = sibling.anchorMax;
                root.pivot     = sibling.pivot;
                root.offsetMin = sibling.offsetMin;
                root.offsetMax = sibling.offsetMax;
                Log.Info($"StretchRootToHost: copied native Common-tab RT — "
                    + $"anchors=({root.anchorMin.x:F2},{root.anchorMin.y:F2})-({root.anchorMax.x:F2},{root.anchorMax.y:F2}) "
                    + $"offsets=({root.offsetMin.x:F0},{root.offsetMin.y:F0})/({root.offsetMax.x:F0},{root.offsetMax.y:F0}).");
                return;
            }

            // Fallback: explicit full stretch with zero insets.
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.pivot     = new Vector2(0.5f, 0.5f);
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            Log.Warn("StretchRootToHost: native Common-tab content not found — applied fallback full stretch.");
        }

        // Walk up from our content root to the owning OptionsPanel, then read tabButtons[0]
        // ("Common") and its `tab` content GameObject — the live native sibling that already
        // fills the host. Returns null (callers fall back to explicit stretch) on any miss.
        private static RectTransform? FindNativeTabContentSibling(RectTransform root)
        {
            try
            {
                var panelType = DuckovController.Patches.OptionsPanelPatch.ResolveType();
                if (panelType == null) return null;

                Component? panel = null;
                for (var cur = root.parent; cur != null; cur = cur.parent)
                {
                    if (cur.GetComponent(panelType) is Component c) { panel = c; break; }
                }
                if (panel == null) return null;

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                if (panelType.GetField("tabButtons", flags)?.GetValue(panel)
                    is not System.Collections.IList tabButtons || tabButtons.Count == 0)
                    return null;

                // tabButtons[0] is "Common"; our cloned tab is appended at the end (Inject.cs),
                // so index 0 is always a genuine native tab, never our clone.
                if (tabButtons[0] is not Component commonTab) return null;
                var tabField = commonTab.GetType().GetField("tab", flags);
                if (tabField?.GetValue(commonTab) is not GameObject commonContent) return null;

                return commonContent.transform as RectTransform;
            }
            catch (Exception e)
            {
                Log.Warn($"FindNativeTabContentSibling: {e.Message}");
                return null;
            }
        }

        // Inner ScrollRect filling the cloned tab; Content sized after all rows are added.
        private static RectTransform CreateInnerScroll(RectTransform root)
        {
            var scrollGo = new GameObject("ControllerSettings_Scroll",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            var scrollRt = (RectTransform)scrollGo.transform;
            scrollRt.SetParent(root, false);
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = Vector2.zero;
            scrollRt.offsetMax = Vector2.zero;
            scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f); // invisible
            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 40f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var viewportGo = new GameObject("Viewport",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
            var viewportRt = (RectTransform)viewportGo.transform;
            viewportRt.SetParent(scrollRt, false);
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = Vector2.zero;
            viewportRt.offsetMax = Vector2.zero;
            viewportGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
            viewportGo.GetComponent<Mask>().showMaskGraphic = false;
            scroll.viewport = viewportRt;

            var contentGo = new GameObject("Content", typeof(RectTransform));
            var contentRt = (RectTransform)contentGo.transform;
            contentRt.SetParent(viewportRt, false);
            // Stretch horizontally so Content width tracks the viewport (which fills the
            // now-dynamic root); height is driven by SetContentHeight via sizeDelta.y.
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.sizeDelta = new Vector2(0f, 4000f); // x=0 → exactly viewport width; height grown via SetContentHeight
            contentRt.anchoredPosition = Vector2.zero;
            scroll.content = contentRt;

            return contentRt;
        }

        // Size scroll Content to actual row count for accurate scrollbar. Floor at the
        // viewport height so Content always fills the (now host-dependent) scroll area —
        // a baked floor would leave dead space below in the taller pause-menu box.
        internal static void SetContentHeight(RectTransform contentRt, int rowCount)
        {
            // contentRt's parent is the Viewport, which stretches to the dynamic root.
            float viewportHeight = (contentRt.parent as RectTransform)?.rect.height ?? 0f;
            float floor = viewportHeight > 1f ? viewportHeight : 464f;
            float height = Mathf.Max(rowCount * RowYStep + 32f, floor);
            contentRt.sizeDelta = new Vector2(contentRt.sizeDelta.x, height);
        }

        // Destroys blacklisted game-binding components on the cloned subtree and
        // clears all widget listeners so stale game callbacks can't fire after rebind.
        private static void StripBindingComponents(GameObject root)
        {
            int destroyedCount = 0;
            int destroyedShadowAware = 0;
            var transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in transforms)
            {
                if (t == null) continue;

                // Clear listeners — we add our own after strip.
                foreach (var slider in t.GetComponents<Slider>()) slider.onValueChanged.RemoveAllListeners();
                foreach (var dd in t.GetComponents<TMP_Dropdown>()) dd.onValueChanged.RemoveAllListeners();
                foreach (var btn in t.GetComponents<Button>()) btn.onClick.RemoveAllListeners();
                foreach (var tg in t.GetComponents<Toggle>()) tg.onValueChanged.RemoveAllListeners();

                var comps = t.gameObject.GetComponents<Component>();
                foreach (var c in comps)
                {
                    if (c == null) continue;
                    var fullName = c.GetType().FullName ?? "";
                    var shortName = c.GetType().Name;
                    bool blacklisted = false;
                    foreach (var prefix in _stripComponentPrefixes)
                    {
                        if (shortName.StartsWith(prefix) || fullName.StartsWith(prefix))
                        {
                            blacklisted = true;
                            break;
                        }
                    }
                    if (!blacklisted) continue;
                    try
                    {
                        Log.Info($"Strip: destroying {shortName} on {t.name}");
                        UnityEngine.Object.DestroyImmediate(c);
                        destroyedCount++;
                        if (t.name.Contains("Shadow")) destroyedShadowAware++;
                    }
                    catch (Exception e) { Log.Warn($"Strip {shortName}: {e.Message}"); }
                }
            }

            Log.Info($"Strip done: destroyedCount={destroyedCount} childCountAfter={root.transform.childCount} "
                + $"(of which Shadow-named transforms touched: {destroyedShadowAware})");
        }

        private static TextMeshProUGUI? FindLabelInRow(GameObject row)
        {
            // Vanilla label is child "Label"; fall back to first TMP child.
            var tLabel = row.transform.Find("Label");
            if (tLabel != null)
            {
                var tmp = tLabel.GetComponent<TextMeshProUGUI>();
                if (tmp != null) return tmp;
            }
            return row.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
        }

        private static void AddPlainText(RectTransform parent, string text)
        {
            var go = new GameObject("PlainText",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(LayoutElement));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 20f;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Left;
            var le = go.GetComponent<LayoutElement>();
            le.preferredHeight = 32f;
        }
    }
}
