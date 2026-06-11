using UnityEngine;
using DuckovController.Config;

namespace DuckovController.UI.Settings
{
    internal interface ISettingsSection
    {
        string Title { get; }

        // INVARIANT: first row added MUST be the master (drives IsEnabled). Sub-rows hidden when false.
        void Build(ControllerSettingsPanel panel, RectTransform list, GameObject sliderTemplate);

        // When false, sub-rows collapse and header reddens. Must match the master row's toggle.
        bool IsEnabled(ControllerConfig cfg);
    }
}
