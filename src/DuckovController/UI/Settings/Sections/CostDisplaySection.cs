using DuckovController.Config;
using UnityEngine;

namespace DuckovController.UI.Settings.Sections
{
    internal sealed class CostDisplaySection : ISettingsSection
    {
        public string Title => "Detailed Cost List";

        public void Build(ControllerSettingsPanel panel, RectTransform list, GameObject sliderTemplate)
        {
            ControllerConfig cfg() => panel.Cfg!;
            var tmpl = ControllerSettingsPanelBuilder.ActiveDropdownTemplate!;

            // MASTER ROW — vertical name+icon+count list everywhere item costs are shown.
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl,
                "Show item names in cost lists",
                () => cfg().CostDisplay.Enabled,
                v => cfg().CostDisplay.Enabled = v, true);
        }

        public bool IsEnabled(ControllerConfig cfg) => cfg.CostDisplay.Enabled;
    }
}
