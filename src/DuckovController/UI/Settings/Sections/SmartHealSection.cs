using DuckovController.Config;
using UnityEngine;

namespace DuckovController.UI.Settings.Sections
{
    internal sealed class SmartHealSection : ISettingsSection
    {
        public string Title => "Smart Heal";

        public void Build(ControllerSettingsPanel panel, RectTransform list, GameObject sliderTemplate)
        {
            ControllerConfig cfg() => panel.Cfg!;
            var tmpl = ControllerSettingsPanelBuilder.ActiveDropdownTemplate!;
            // MASTER ROW — must be first (always-visible when collapsed).
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Smart Heal enabled",
                () => cfg().SmartHeal.Enabled, v => cfg().SmartHeal.Enabled = v, true);
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Aggressive bleed treatment",
                () => cfg().SmartHeal.BleedAggressive,
                v => cfg().SmartHeal.BleedAggressive = v, true);
            ControllerSettingsPanelBuilder.AddIntRow(panel, list, tmpl, "Bleed-danger HP threshold",
                new[] { 10, 20, 30, 40, 50, 75, 100, 150 },
                () => cfg().SmartHeal.BleedDangerHpThreshold,
                v => cfg().SmartHeal.BleedDangerHpThreshold = v, 35);
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Treat Pain",
                () => cfg().SmartHeal.TreatPain, v => cfg().SmartHeal.TreatPain = v, true);
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Treat Fire",
                () => cfg().SmartHeal.TreatFire, v => cfg().SmartHeal.TreatFire = v, true);
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Treat Electric",
                () => cfg().SmartHeal.TreatElectric, v => cfg().SmartHeal.TreatElectric = v, true);
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Treat Space (storm)",
                () => cfg().SmartHeal.TreatSpace, v => cfg().SmartHeal.TreatSpace = v, true);
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Treat Cold (frostbite)",
                () => cfg().SmartHeal.TreatCold, v => cfg().SmartHeal.TreatCold = v, true);
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Treat Poison",
                () => cfg().SmartHeal.TreatPoison, v => cfg().SmartHeal.TreatPoison = v, true);
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Heal missing HP",
                () => cfg().SmartHeal.HealMissingHp, v => cfg().SmartHeal.HealMissingHp = v, true);
            ControllerSettingsPanelBuilder.AddEnumRow(panel, list, tmpl, "Heal item preference",
                new[] { "Off", "Price", "Heal Amount" },
                () => (int)cfg().SmartHeal.HealPick,
                v => cfg().SmartHeal.HealPick = (HealPickMode)v,
                @defaultIndex: (int)HealPickMode.HealAmount);
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Auto-buff at full HP",
                () => cfg().SmartHeal.AutoBuffAtFullHp, v => cfg().SmartHeal.AutoBuffAtFullHp = v, true);
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "LB hold = chain heals",
                () => cfg().SmartHeal.QueueOnHold, v => cfg().SmartHeal.QueueOnHold = v, true);
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Play sound when no-op",
                () => cfg().SmartHeal.AudioOnNoOp, v => cfg().SmartHeal.AudioOnNoOp = v, true);
        }

        public bool IsEnabled(ControllerConfig cfg) => cfg.SmartHeal.Enabled;
    }
}
