using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DuckovController.UI.Settings
{
    internal static partial class ControllerSettingsPanelBuilder
    {
        internal static void AddBoolRow(
            ControllerSettingsPanel p, RectTransform list, GameObject template, string label,
            Func<bool> get, Action<bool> set, bool @default)
        {
            string[] options = { "Off", "On" };
            int Idx(bool b) => b ? 1 : 0;
            var rowGo = BuildEnumRow(p, list, template, label, options,
                () => Idx(get()),
                v => set(v != 0),
                @defaultIndex: Idx(@default));
            if (rowGo == null) return;
            // Tag dropdown: A toggles Off↔On in-place (BooleanToggleMarker).
            var dd = rowGo.GetComponentInChildren<TMP_Dropdown>(includeInactive: true);
            if (dd != null)
            {
                var marker = dd.gameObject.AddComponent<BooleanToggleMarker>();
                marker.Cycle = () =>
                {
                    int v = (dd.value + 1) % 2;
                    dd.value = v; // fires onValueChanged → writes through
                };
            }
        }

        // Slider-backed int row cloned from UI_BusVolume_*. D-pad ±step via ControllerSliderInputMarker.
        internal static void AddSliderIntRow(
            ControllerSettingsPanel p, RectTransform list, GameObject sliderTemplate, string label,
            int min, int max, int step, Func<int> get, Action<int> set, int @default)
        {
            if (sliderTemplate == null)
            {
                Log.Warn($"AddSliderIntRow '{label}': no slider template — falling back to dropdown bucket.");
                AddIntRow(p, list, null!,
                    label, new[] { min, (min + max) / 2, max },
                    get, set, @default);
                return;
            }

            var rowGo = CloneSliderRow(sliderTemplate, list);
            if (rowGo == null) { Log.Warn($"AddSliderIntRow: clone failed for '{label}'."); return; }

            StripBindingComponents(rowGo);

            // Remove LeTai's leftover original Shadow sibling (non-Clone).
            for (int i = 0; i < rowGo.transform.childCount; i++)
            {
                var child = rowGo.transform.GetChild(i);
                if (child.name.EndsWith("'s Shadow") && !child.name.Contains("(Clone)"))
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                    i--;
                }
            }

            var labelTmp = FindLabelInRow(rowGo);
            if (labelTmp != null) labelTmp.text = label;

            // Direct child "Slider" in BusVolume layout.
            var sliderT = rowGo.transform.Find("Slider");
            var slider = sliderT != null ? sliderT.GetComponent<Slider>() : null;
            if (slider == null) { Log.Warn($"AddSliderIntRow: 'Slider' child missing for '{label}'."); return; }
            slider.gameObject.SetActive(true);

            // "Value" sibling: may have height 0 in template, clips text — fix.
            TextMeshProUGUI? valueTmp = null;
            var valueT = rowGo.transform.Find("Value");
            if (valueT != null) valueTmp = valueT.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);

            if (valueT is RectTransform valueRt && valueRt.rect.height < 1f)
                valueRt.sizeDelta = new Vector2(valueRt.sizeDelta.x, 40f);

            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = true;
            int initial = Mathf.Clamp(get(), min, max);
            slider.SetValueWithoutNotify(initial);
            if (valueTmp != null) valueTmp.text = initial.ToString();

            slider.onValueChanged.RemoveAllListeners();
            slider.onValueChanged.AddListener(v =>
            {
                int iv = Mathf.Clamp(Mathf.RoundToInt(v), min, max);
                try { set(iv); } catch (Exception e) { Log.Error($"Slider '{label}' set: {e.Message}"); }
                if (valueTmp != null) valueTmp.text = iv.ToString();
                p.NotifyValueChanged();
            });

            p.RefreshCallbacks.Add(() =>
            {
                if (slider == null) return;
                int iv = Mathf.Clamp(get(), min, max);
                slider.SetValueWithoutNotify(iv);
                if (valueTmp != null) valueTmp.text = iv.ToString();
            });

            // Step marker for MenuFocusOverlay.
            var stepMarker = slider.gameObject.AddComponent<ControllerSliderInputMarker>();
            stepMarker.Step = step;

            // Row marker for X-reset.
            var marker = rowGo.AddComponent<ControllerSettingsRowMarker>();
            marker.ResetToDefault = () =>
            {
                int dv = Mathf.Clamp(@default, min, max);
                try { set(dv); } catch (Exception e) { Log.Error($"Slider '{label}' reset: {e.Message}"); }
                slider.SetValueWithoutNotify(dv);
                if (valueTmp != null) valueTmp.text = dv.ToString();
                p.NotifyValueChanged();
            };
        }

        // Clone slider row: lock sizeDelta to 1224×50, step Y per row index.
        private static GameObject? CloneSliderRow(GameObject template, RectTransform parent)
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
                foreach (var csf in clone.GetComponents<ContentSizeFitter>())
                    UnityEngine.Object.DestroyImmediate(csf);
                foreach (var hlg in clone.GetComponents<HorizontalOrVerticalLayoutGroup>())
                    UnityEngine.Object.DestroyImmediate(hlg);

                rt.sizeDelta = new Vector2(1224f, 50f);
                var pos = rt.anchoredPosition;
                pos.x = 612f;
                pos.y = RowYStart - idx * RowYStep;
                rt.anchoredPosition = pos;

                var le = clone.GetComponent<LayoutElement>() ?? clone.AddComponent<LayoutElement>();
                le.minWidth = 1224f;
                le.preferredWidth = 1224f;
                le.minHeight = 50f;
                le.preferredHeight = 50f;
            }
            if (_collectRowsInto != null && rt != null) _collectRowsInto.Add(rt);
            return clone;
        }

        internal static void AddIntRow(
            ControllerSettingsPanel p, RectTransform list, GameObject template, string label,
            int[] choices, Func<int> get, Action<int> set, int @default)
        {
            string[] options = new string[choices.Length];
            for (int i = 0; i < choices.Length; i++) options[i] = choices[i].ToString();

            int FindIdx(int v)
            {
                int best = 0;
                int bestDiff = int.MaxValue;
                for (int i = 0; i < choices.Length; i++)
                {
                    int diff = Math.Abs(choices[i] - v);
                    if (diff < bestDiff) { bestDiff = diff; best = i; }
                }
                return best;
            }

            AddEnumRow(p, list, template, label, options,
                () => FindIdx(get()),
                v => set(choices[Mathf.Clamp(v, 0, choices.Length - 1)]),
                @defaultIndex: FindIdx(@default));
        }

        // Wrapper for callers that don't need the returned GameObject.
        internal static void AddEnumRow(
            ControllerSettingsPanel p, RectTransform list, GameObject template, string label,
            string[] options, Func<int> get, Action<int> set, int @defaultIndex)
        {
            BuildEnumRow(p, list, template, label, options, get, set, @defaultIndex);
        }

        private static GameObject? BuildEnumRow(
            ControllerSettingsPanel p, RectTransform list, GameObject template, string label,
            string[] options, Func<int> get, Action<int> set, int @defaultIndex)
        {
            var rowGo = CloneDropdownRow(template, list);
            if (rowGo == null) { Log.Warn($"Could not clone row template for '{label}'."); return null; }

            StripBindingComponents(rowGo);

            var labelTmp = FindLabelInRow(rowGo);
            if (labelTmp != null) labelTmp.text = label;

            // Force-activate: UI_Language template arrives active=false after Instantiate
            // (OptionsUIEntry_Dropdown Awake or LeTai TrueShadow rebuild side-effect).
            var dropdown = rowGo.GetComponentInChildren<TMP_Dropdown>(includeInactive: true);
            if (dropdown == null) { Log.Warn($"Row template missing TMP_Dropdown for '{label}'."); return null; }
            dropdown.gameObject.SetActive(true);

            // Hide UI_Language's flag Image child (language-flag overlay, irrelevant here).
            for (int i = 0; i < dropdown.transform.childCount; i++)
            {
                var c = dropdown.transform.GetChild(i);
                if (c.name == "Image" && c.GetComponent<Image>() != null)
                {
                    c.gameObject.SetActive(false);
                }
            }

            // LeTai leaves inactive "X's Shadow" + active "X(Clone)'s Shadow" — remove the old one.
            for (int i = 0; i < rowGo.transform.childCount; i++)
            {
                var child = rowGo.transform.GetChild(i);
                if (child.name.EndsWith("'s Shadow") && !child.name.Contains("(Clone)"))
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                    i--;
                }
            }

            dropdown.onValueChanged.RemoveAllListeners();
            dropdown.ClearOptions();
            dropdown.AddOptions(new List<string>(options));

            int initial = Mathf.Clamp(get(), 0, options.Length - 1);
            dropdown.SetValueWithoutNotify(initial);

            dropdown.onValueChanged.AddListener(v =>
            {
                try { set(Mathf.Clamp(v, 0, options.Length - 1)); }
                catch (Exception e) { Log.Error($"Row '{label}' set: {e.Message}"); }
                p.NotifyValueChanged();
            });

            p.RefreshCallbacks.Add(() =>
            {
                if (dropdown == null) return;
                int idx = Mathf.Clamp(get(), 0, options.Length - 1);
                dropdown.SetValueWithoutNotify(idx);
            });

            var marker = rowGo.AddComponent<ControllerSettingsRowMarker>();
            marker.ResetToDefault = () =>
            {
                try { set(@defaultIndex); }
                catch (Exception e) { Log.Error($"Row '{label}' reset: {e.Message}"); }
                dropdown.SetValueWithoutNotify(Mathf.Clamp(get(), 0, options.Length - 1));
                p.NotifyValueChanged();
            };
            return rowGo;
        }

        private static TextMeshProUGUI? AddSectionHeader(RectTransform list, GameObject template, string text, out RectTransform? headerRt)
        {
            headerRt = null;
            var rowGo = CloneDropdownRow(template, list);
            if (rowGo == null)
            {
                AddPlainText(list, text);
                return null;
            }
            headerRt = rowGo.transform as RectTransform;
            StripBindingComponents(rowGo);
            var dropdown = rowGo.GetComponentInChildren<TMP_Dropdown>(includeInactive: true);
            if (dropdown != null) dropdown.gameObject.SetActive(false);
            var labelTmp = FindLabelInRow(rowGo);
            if (labelTmp != null)
            {
                labelTmp.text = text;
                labelTmp.color = new Color(1f, 0.84f, 0f, 1f);
                labelTmp.fontStyle |= FontStyles.Bold;
                labelTmp.alignment = TextAlignmentOptions.Left;
            }
            return labelTmp;
        }
    }
}
