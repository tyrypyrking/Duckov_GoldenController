using System;
using System.Collections.Generic;
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

                // Kill CSF/layout-group — vanilla sized to 1224 via parent; lock it ourselves.
                foreach (var csf in clone.GetComponents<ContentSizeFitter>())
                    UnityEngine.Object.DestroyImmediate(csf);
                foreach (var hlg in clone.GetComponents<HorizontalOrVerticalLayoutGroup>())
                    UnityEngine.Object.DestroyImmediate(hlg);

                rt.sizeDelta = new Vector2(1224f, 50f);

                var pos = rt.anchoredPosition;
                pos.x = 612f;
                pos.y = RowYStart - idx * RowYStep;
                rt.anchoredPosition = pos;

                // LayoutElement: defensive in case any ancestor is layout-group-controlled.
                var le = clone.GetComponent<LayoutElement>() ?? clone.AddComponent<LayoutElement>();
                le.minWidth = 1224f;
                le.preferredWidth = 1224f;
                le.minHeight = 50f;
                le.preferredHeight = 50f;
            }

            if (_collectRowsInto != null && rt != null) _collectRowsInto.Add(rt);
            return clone;
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
            contentRt.anchorMin = new Vector2(0.5f, 1f);
            contentRt.anchorMax = new Vector2(0.5f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.sizeDelta = new Vector2(1224f, 4000f); // grown via SetContentHeight after build
            contentRt.anchoredPosition = Vector2.zero;
            scroll.content = contentRt;

            return contentRt;
        }

        // Size scroll Content to actual row count for accurate scrollbar.
        internal static void SetContentHeight(RectTransform contentRt, int rowCount)
        {
            float height = Mathf.Max(rowCount * RowYStep + 32f, 464f);
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
