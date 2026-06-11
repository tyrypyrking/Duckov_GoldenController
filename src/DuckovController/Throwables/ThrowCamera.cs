using System.Reflection;
using UnityEngine;

namespace DuckovController.Throwables
{
    // Resolves the same camera the Aim helpers use (InputManager.mainCam, private instance field),
    // so throw-reticle projection matches the game's aim-point raycast.
    internal static class ThrowCamera
    {
        private static FieldInfo? _mainCamField;
        private static bool _resolved;

        internal static Camera? Get(InputManager im)
        {
            if (im == null) return null;
            if (!_resolved)
            {
                _resolved = true;
                _mainCamField = typeof(InputManager).GetField(
                    "mainCam", BindingFlags.Instance | BindingFlags.NonPublic);
            }
            var cam = _mainCamField?.GetValue(im) as Camera;
            return cam != null ? cam : Camera.main;
        }
    }
}
