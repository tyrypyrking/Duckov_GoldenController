using System;
using DuckovController.Config;

namespace DuckovController.UI.Settings
{
    // Glue: ModBehaviour publishes Cfg+SettingsPath; OptionsPanelPatch reads them;
    // panel saves via ControllerConfigLoader.Save() and raises OnRulesChanged.
    internal static class SettingsBridge
    {
        internal static ControllerConfig? Cfg;
        internal static string? SettingsPath;

        // Raised on Unity main thread after panel writes a value to disk.
        internal static event Action<ControllerConfig>? OnRulesChanged;

        internal static void NotifyRulesChanged()
        {
            try { if (Cfg != null) OnRulesChanged?.Invoke(Cfg); }
            catch (Exception e) { Log.Error($"SettingsBridge.NotifyRulesChanged: {e.Message}"); }
        }

        internal static void Save()
        {
            if (Cfg == null || string.IsNullOrEmpty(SettingsPath)) return;
            ControllerConfigLoader.Save(Cfg, SettingsPath!);
        }
    }
}
