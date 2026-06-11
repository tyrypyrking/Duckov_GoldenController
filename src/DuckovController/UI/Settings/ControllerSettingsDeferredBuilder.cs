using System;
using DuckovController.Config;
using UnityEngine;
using UnityEngine.UI;

namespace DuckovController.UI.Settings
{
    // Defers BuildInto by 2 frames so vanilla row templates are fully laid out
    // (746→1224 by parent layout group) and LeTai.TrueShadow children exist.
    // Without this, cloned rows have childCount=2 and width=746 and look empty.
    // Attached by OptionsPanelPatch.InjectTab; self-destructs after first build.
    internal sealed class ControllerSettingsDeferredBuilder : MonoBehaviour
    {
        internal MonoBehaviour? OptionsPanel;
        internal RectTransform? Target;
        internal ControllerConfig? Cfg;
        internal string? SettingsPath;
        internal Func<MonoBehaviour, GameObject?>? TemplateFinder;
        internal Func<MonoBehaviour, GameObject?>? SliderTemplateFinder;

        private bool _built;
        private int _waitedFrames;

        private void Update()
        {
            if (_built) return;

            // 2-frame wait: layout + TrueShadow must run on originals before clone.
            _waitedFrames++;
            if (_waitedFrames < 2) return;

            if (OptionsPanel == null || Target == null || Cfg == null || SettingsPath == null
                || TemplateFinder == null)
            {
                Log.Warn("ControllerSettingsDeferredBuilder: missing field; aborting.");
                _built = true;
                UnityEngine.Object.Destroy(this);
                return;
            }

            try
            {
                // Force layout rebuild so widths are accurate before clone.
                LayoutRebuilder.ForceRebuildLayoutImmediate(OptionsPanel.transform as RectTransform);
            }
            catch (Exception e) { Log.Debug_($"ForceRebuildLayoutImmediate: {e.Message}"); }

            try
            {
                var template = TemplateFinder(OptionsPanel);
                var sliderTemplate = SliderTemplateFinder != null ? SliderTemplateFinder(OptionsPanel) : null;
                if (template == null)
                    Log.Warn("DeferredBuilder: dropdown template still null on deferred build.");
                if (sliderTemplate == null)
                    Log.Warn("DeferredBuilder: slider template still null on deferred build.");
                ControllerSettingsPanelBuilder.BuildInto(Target, Cfg, SettingsPath, template, sliderTemplate);
                _built = true;
                Log.Info("ControllerSettingsDeferredBuilder: build complete.");
            }
            catch (Exception e)
            {
                Log.Error($"ControllerSettingsDeferredBuilder: build failed: {e}");
                _built = true;
            }

            UnityEngine.Object.Destroy(this);
        }
    }
}
