using System;
using UnityEngine;

namespace DuckovController.UI.Settings
{
    // Attached by ControllerSettingsPanelBuilder to each generated row.
    // OptionsPanelTabCycler walks the currently-focused GameObject's
    // ancestors looking for this component, and invokes ResetToDefault
    // when the X button is pressed.
    internal sealed class ControllerSettingsRowMarker : MonoBehaviour
    {
        internal Action? ResetToDefault;
    }
}
