using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DuckovController.Patches
{
    // Template finders and DOM helpers for InjectSingleTab stages (partial of OptionsPanel_Setup_Patch).
    [HarmonyLib.HarmonyPatch]
    internal static partial class OptionsPanel_Setup_Patch
    {
        // Find a UI_BusVolume_* row (Shadow + Label + Value + Slider) for numeric settings template.
        internal static GameObject? FindSliderRowTemplate(MonoBehaviour optionsPanel)
        {
            try
            {
                var sliders = optionsPanel.GetComponentsInChildren<UnityEngine.UI.Slider>(includeInactive: true);
                foreach (var s in sliders)
                {
                    if (s == null) continue;
                    var p = s.transform.parent;
                    if (p == null) continue;
                    // UI_BusVolume_Master / UI_BusVolume_SFX / UI_BusVolume_Music
                    if (p.name.StartsWith("UI_BusVolume_"))
                    {
                        Log.Info($"FindSliderRowTemplate: picked '{p.name}'");
                        return p.gameObject;
                    }
                }
                Log.Warn("FindSliderRowTemplate: no UI_BusVolume_* row found.");
            }
            catch (Exception e) { Log.Warn($"FindSliderRowTemplate: {e.Message}"); }
            return null;
        }

        // Find the widest OptionsUIEntry_Dropdown (full-row template: Shadow + Label + Dropdown, ~1224px wide).
        // Logs all candidates for dump verification.
        internal static GameObject? FindDropdownRowTemplate(MonoBehaviour optionsPanel)
        {
            try
            {
                var comps = optionsPanel.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
                GameObject? best = null;
                float bestWidth = 0f;
                int seen = 0;
                foreach (var c in comps)
                {
                    if (c == null) continue;
                    if (c.GetType().Name != "OptionsUIEntry_Dropdown") continue;
                    seen++;
                    var rt = c.transform as RectTransform;
                    float w = rt != null ? rt.sizeDelta.x : 0f;
                    int childCount = c.transform.childCount;
                    Log.Info($"FindDropdownRowTemplate candidate: name={c.name} "
                        + $"width={w:F0} childCount={childCount} "
                        + $"path={BuildPathString(c.transform)}");
                    if (w > bestWidth)
                    {
                        bestWidth = w;
                        best = c.gameObject;
                    }
                }
                Log.Info($"FindDropdownRowTemplate: seen={seen} picked '{(best != null ? best.name : "<null>")}' width={bestWidth:F0}");
                return best;
            }
            catch (Exception e) { Log.Warn($"FindDropdownRowTemplate: {e.Message}"); }
            return null;
        }

        internal static string BuildPathString(Transform t)
        {
            if (t == null) return "<null>";
            var sb = new System.Text.StringBuilder();
            var cur = t;
            int depth = 0;
            while (cur != null && depth < 12)
            {
                sb.Insert(0, "/" + cur.name);
                cur = cur.parent;
                depth++;
            }
            return sb.ToString();
        }

        internal static GameObject? FindChildIndicator(GameObject root)
        {
            var named = root.transform.Find("SelectionIndicator"); // vanilla name
            if (named != null) return named.gameObject;
            // Fallback: first child whose name contains "Indicator".
            foreach (Transform child in root.transform)
            {
                if (child.name.IndexOf("Indicator", StringComparison.OrdinalIgnoreCase) >= 0)
                    return child.gameObject;
            }
            return null;
        }

        internal static void SetButtonLabel(GameObject tabButtonGo, string text)
        {
            var tmps = tabButtonGo.GetComponentsInChildren<TMP_Text>(true);
            foreach (var tmp in tmps)
            {
                if (tmp == null) continue;
                tmp.text = text;
                return; // first only
            }
            // Fallback: legacy UI Text.
            var legacy = tabButtonGo.GetComponentsInChildren<UnityEngine.UI.Text>(true);
            foreach (var t in legacy)
            {
                if (t == null) continue;
                t.text = text;
                return;
            }
        }
    }
}
