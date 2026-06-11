using DuckovController.Config;
using UnityEngine;

namespace DuckovController.UI.Settings.Sections
{
    internal sealed class SmartTakeSection : ISettingsSection
    {
        public string Title => "Smart Loot";

        public void Build(ControllerSettingsPanel panel, RectTransform list, GameObject sliderTemplate)
        {
            ControllerConfig cfg() => panel.Cfg!;
            var tmpl = ControllerSettingsPanelBuilder.ActiveDropdownTemplate!;

            // MASTER ROW — must be first (always-visible when collapsed).
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Smart Loot enabled",
                () => cfg().SmartTake.Enabled,
                v => cfg().SmartTake.Enabled = v, true);

            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Respect active filter tab",
                () => cfg().SmartTake.RespectActiveFilter,
                v => cfg().SmartTake.RespectActiveFilter = v, true);
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Skip locked inventory slots",
                () => cfg().SmartTake.SkipLockedInventoryIndices,
                v => cfg().SmartTake.SkipLockedInventoryIndices = v, true);
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Skip pet slot",
                () => cfg().SmartTake.SkipPetSlot,
                v => cfg().SmartTake.SkipPetSlot = v, true);
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Take ammo for guns I own",
                () => cfg().SmartTake.TakeAmmoForOwnedGuns,
                v => cfg().SmartTake.TakeAmmoForOwnedGuns = v, true);
            ControllerSettingsPanelBuilder.AddEnumRow(panel, list, tmpl, "Min ammo tier",
                new[] { "Off", "Trash", "Common", "Rare", "Very rare", "Legendary", "Mythic" },
                () => (int)cfg().SmartTake.MinAmmoTier,
                v => cfg().SmartTake.MinAmmoTier = (AmmoTier)v,
                @defaultIndex: 0);
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Take wishlisted items",
                () => cfg().SmartTake.TakeWishlisted,
                v => cfg().SmartTake.TakeWishlisted = v, true);
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Take quest-required items",
                () => cfg().SmartTake.TakeQuestRequired,
                v => cfg().SmartTake.TakeQuestRequired = v, true);
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Take building-required items",
                () => cfg().SmartTake.TakeBuildingRequired,
                v => cfg().SmartTake.TakeBuildingRequired = v, false);

            // Value rule (now compares displayed sell price).
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Take above sell value",
                () => cfg().SmartTake.TakeAboveValue,
                v => cfg().SmartTake.TakeAboveValue = v, true);
            ControllerSettingsPanelBuilder.AddSliderIntRow(panel, list, sliderTemplate, "Sell value ≥ (shown)",
                min: 10, max: 2000, step: 10,
                () => cfg().SmartTake.ValueThreshold,
                v => cfg().SmartTake.ValueThreshold = v,
                @default: 100);

            // Value/weight rule.
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Take above value/weight",
                () => cfg().SmartTake.TakeAboveValuePerWeight,
                v => cfg().SmartTake.TakeAboveValuePerWeight = v, true);
            ControllerSettingsPanelBuilder.AddSliderIntRow(panel, list, sliderTemplate, "Value per kg ≥",
                min: 5, max: 2000, step: 15,
                () => cfg().SmartTake.ValuePerWeightThreshold,
                v => cfg().SmartTake.ValuePerWeightThreshold = v,
                @default: 500);

            // Combine the two value rules: on = both (AND, default), off = either (OR).
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Value rules: require both (AND)",
                () => cfg().SmartTake.ValueRulesRequireBoth,
                v => cfg().SmartTake.ValueRulesRequireBoth = v, true);

            // Stack top-up rule.
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Top up existing stacks",
                () => cfg().SmartTake.TopUpExistingStacks,
                v => cfg().SmartTake.TopUpExistingStacks = v, true);
            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Allow overflow when topping up",
                () => cfg().SmartTake.AllowStackOverflowOnTopUp,
                v => cfg().SmartTake.AllowStackOverflowOnTopUp = v, false);

            ControllerSettingsPanelBuilder.AddBoolRow(panel, list, tmpl, "Play sound on Smart-Take",
                () => cfg().SmartTake.AudioOnSmartTake,
                v => cfg().SmartTake.AudioOnSmartTake = v, true);
        }

        public bool IsEnabled(ControllerConfig cfg) => cfg.SmartTake.Enabled;
    }
}
