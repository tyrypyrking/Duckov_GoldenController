using DuckovController.Aim;
using DuckovController.Config;
using UnityEngine;

namespace DuckovController.UI.Settings.Sections
{
    internal sealed class AimAssistSection : ISettingsSection
    {
        public string Title => "Aim Assist";

        private static readonly string[] _aimTierLabels =
        {
            "Off", "Light", "Standard", "Aggressive", "Cheat", "Custom"
        };

        public void Build(ControllerSettingsPanel panel, RectTransform list, GameObject sliderTemplate)
        {
            ControllerConfig cfg() => panel.Cfg!;
            var tmpl = ControllerSettingsPanelBuilder.ActiveDropdownTemplate!;
            ControllerSettingsPanelBuilder.AddEnumRow(panel, list, tmpl, "Auto-aim tier", _aimTierLabels,
                () => (int)cfg().AutoAim.Tier,
                v =>
                {
                    cfg().AutoAim.Tier = (AutoAimTier)v;
                    AutoAimTiers.Apply(cfg());
                },
                @defaultIndex: (int)AutoAimTier.Off);
            ControllerSettingsPanelBuilder.AddIntRow(panel, list, tmpl, "Max target distance (m)",
                new[] { 5, 10, 15, 20, 25, 30, 50, 75, 100 },
                () => Mathf.RoundToInt(cfg().AutoAim.MaxTargetDistanceMeters),
                v => cfg().AutoAim.MaxTargetDistanceMeters = v, 25);
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Decouple view cone from aim",
                () => cfg().AutoAim.DecoupleViewFromAim,
                v => cfg().AutoAim.DecoupleViewFromAim = v, true);
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Lock through obstacles",
                () => cfg().AutoAim.TargetThroughWalls,
                v => cfg().AutoAim.TargetThroughWalls = v, false);
            ControllerSettingsPanelBuilder.AddIntRow(panel, list, tmpl, "Min lock time (ms)",
                new[] { 0, 100, 200, 300, 500, 750, 1000 },
                () => cfg().AutoAim.MinLockTimeMs,
                v => cfg().AutoAim.MinLockTimeMs = v, 200);
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Aim magnetism (tier 1)",
                () => cfg().Aim.MagnetismEnabled,
                v => cfg().Aim.MagnetismEnabled = v, true);
            ControllerSettingsPanelBuilder.AddIntRow(panel, list, tmpl, "Stick sensitivity",
                new[] { 5, 10, 15, 20, 25, 30, 40, 50, 75, 100 },
                () => Mathf.RoundToInt(cfg().Aim.Sensitivity),
                v => cfg().Aim.Sensitivity = v, 28);
        }

        public bool IsEnabled(ControllerConfig cfg) => cfg.AutoAim.Tier != AutoAimTier.Off;
    }
}
