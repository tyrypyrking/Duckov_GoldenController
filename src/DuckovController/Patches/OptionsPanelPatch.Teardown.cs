using System;
using System.Collections;
using System.Reflection;
using DuckovController.UI.Settings;
using UnityEngine;

namespace DuckovController.Patches
{
    // Lifecycle for the injected tab. OptionsPanel.Setup() runs ONCE from Start() (persistent UIPanel):
    //   Fresh launch: Postfix injects; TryReinjectIntoLivePanel no-ops (panel doesn't exist yet).
    //   Mod re-enable: panel exists, Setup won't run again; TryReinjectIntoLivePanel injects directly.
    // TeardownInjectedTab required: without it, our cloned tab + cycler lingered on the game's OptionsPanel after disable.
    internal static partial class OptionsPanel_Setup_Patch
    {
        // Shared between Postfix and TryReinjectIntoLivePanel so both paths build identical content.
        internal static void BuildControllerTabContent(MonoBehaviour optionsPanel, RectTransform contentRect)
        {
            var cfg = SettingsBridge.Cfg;
            var path = SettingsBridge.SettingsPath;
            if (cfg == null || string.IsNullOrEmpty(path))
            {
                Log.Warn("OptionsPanelPatch: SettingsBridge not populated yet — panel built with empty cfg.");
                cfg ??= new Config.ControllerConfig();
                path ??= "";
            }

            var deferred = contentRect.gameObject.AddComponent<ControllerSettingsDeferredBuilder>();
            deferred.OptionsPanel = optionsPanel;
            deferred.Target = contentRect;
            deferred.Cfg = cfg;
            deferred.SettingsPath = path!;
            deferred.TemplateFinder = FindDropdownRowTemplate;
            deferred.SliderTemplateFinder = FindSliderRowTemplate;
            Log.Info("OptionsPanelPatch: deferred builder attached — will fire after layout settles.");
        }

        // Injects into an already-Setup panel. No-op on fresh launch (panel not yet created); idempotent via marker.
        internal static void TryReinjectIntoLivePanel()
        {
            try
            {
                var type = OptionsPanelPatch.ResolveType();
                if (type == null) return;
                var found = UnityEngine.Object.FindObjectsOfType(type, includeInactive: true);
                if (found == null || found.Length == 0) return;  // not created yet — postfix handles first injection
                if (found[0] is not MonoBehaviour panel) return;
                InjectSingleTab(panel, "Controller Mod",
                    contentRect => BuildControllerTabContent(panel, contentRect));
            }
            catch (Exception e) { Log.Warn($"OptionsPanelPatch.TryReinjectIntoLivePanel: {e.Message}"); }
        }

        // Detach everything attached to the game's OptionsPanel: cloned tab, content panel, cycler, deferred builder.
        internal static void TeardownInjectedTab()
        {
            try
            {
                var markers = UnityEngine.Object.FindObjectsOfType<OptionsPanelPatch.ControllerTabMarker>(true);
                foreach (var marker in markers)
                {
                    if (marker == null) continue;
                    var cloneGo = marker.gameObject;
                    UnlinkTabFromPanel(cloneGo);          // drop from tabButtons + reset selection + destroy content
                    UnityEngine.Object.Destroy(cloneGo);  // takes the marker + click proxy with it
                }

                foreach (var cyc in UnityEngine.Object.FindObjectsOfType<OptionsPanelTabCycler>(true))
                    if (cyc != null) UnityEngine.Object.Destroy(cyc);

                foreach (var db in UnityEngine.Object.FindObjectsOfType<ControllerSettingsDeferredBuilder>(true))
                    if (db != null) UnityEngine.Object.Destroy(db);

                if (markers.Length > 0)
                    Log.Info($"OptionsPanelPatch: torn down {markers.Length} injected tab(s).");
            }
            catch (Exception e) { Log.Warn($"OptionsPanelPatch.TeardownInjectedTab: {e.Message}"); }
        }

        // Remove clone from tabButtons, reset selection if it was current, destroy content GO.
        private static void UnlinkTabFromPanel(GameObject cloneGo)
        {
            var type = OptionsPanelPatch.ResolveType();
            if (type == null) return;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            var panel = FindOwningPanel(cloneGo, type);
            Component? cloneButton = null;

            if (panel != null && type.GetField("tabButtons", flags)?.GetValue(panel) is IList list)
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i] is Component c && c != null && c.gameObject == cloneGo)
                    {
                        cloneButton = c;
                        list.RemoveAt(i);
                    }
                }

                // If our tab was selected (or selection stale), reset to first remaining tab.
                var cur = type.GetMethod("GetSelection", flags)?.Invoke(panel, null) as Component;
                var setSel = type.GetMethod("SetSelection", flags);
                if (setSel != null && list.Count > 0 && (cur == null || cur.gameObject == cloneGo))
                    setSel.Invoke(panel, new[] { list[0] });
            }

            // Destroy content (lives outside cloneGo's hierarchy; not taken by Destroy(cloneGo)).
            if (cloneButton != null
                && cloneButton.GetType().GetField("tab", flags)?.GetValue(cloneButton) is GameObject content)
            {
                UnityEngine.Object.Destroy(content);
            }
        }

        // Manual parent walk (works regardless of active state / Unity overloads).
        private static MonoBehaviour? FindOwningPanel(GameObject go, Type type)
        {
            for (var cur = go.transform; cur != null; cur = cur.parent)
            {
                if (cur.GetComponent(type) is MonoBehaviour c) return c;
            }
            return null;
        }
    }
}
