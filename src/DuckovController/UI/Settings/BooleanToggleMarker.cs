using System;
using UnityEngine;

namespace DuckovController.UI.Settings
{
    // Tag: A press cycles Off↔On in-place instead of expanding the TMP_Dropdown.
    // MenuFocusOverlay.HandleSubmit detects this and calls Cycle() instead of submit.
    internal sealed class BooleanToggleMarker : MonoBehaviour
    {
        internal Action? Cycle;
    }
}
