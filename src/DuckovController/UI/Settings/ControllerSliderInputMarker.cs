using UnityEngine;

namespace DuckovController.UI.Settings
{
    // Attached to slider widgets in the Controller Mod settings panel so
    // MenuFocusOverlay.HandleNav can intercept dpad left/right and adjust
    // the slider value by `Step` per press. Hold-to-repeat is handled by
    // the existing nav-hold timer in MenuFocusOverlay.
    internal sealed class ControllerSliderInputMarker : MonoBehaviour
    {
        internal int Step = 1;
    }
}
