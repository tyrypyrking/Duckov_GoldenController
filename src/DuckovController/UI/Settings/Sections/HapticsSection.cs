using DuckovController.Config;
using UnityEngine;

namespace DuckovController.UI.Settings.Sections
{
    internal sealed class HapticsSection : ISettingsSection
    {
        public string Title => "Haptics";

        public void Build(ControllerSettingsPanel panel, RectTransform list, GameObject sliderTemplate)
        {
            ControllerConfig cfg() => panel.Cfg!;
            var tmpl = ControllerSettingsPanelBuilder.ActiveDropdownTemplate!;

            // MASTER ROW — must be first.
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Haptics enabled",
                () => cfg().Haptics.Enabled, v => cfg().Haptics.Enabled = v, true);

            ControllerSettingsPanelBuilder.AddSliderIntRow(panel, list, sliderTemplate,
                "Haptics intensity", 0, 100, 5,
                () => Mathf.RoundToInt(cfg().Haptics.Intensity * 100f),
                v => cfg().Haptics.Intensity = v / 100f, 50);

            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "UI / menu haptics",
                () => cfg().Haptics.UiEnabled, v => cfg().Haptics.UiEnabled = v, true);

            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Gameplay haptics",
                () => cfg().Haptics.GameplayEnabled, v => cfg().Haptics.GameplayEnabled = v, true);
        }

        public bool IsEnabled(ControllerConfig cfg) => cfg.Haptics.Enabled;
    }
}
