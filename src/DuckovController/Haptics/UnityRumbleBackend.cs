using UnityEngine;
using UnityEngine.InputSystem;

namespace DuckovController.Haptics
{
    internal sealed class UnityRumbleBackend : IHapticBackend
    {
        public void SetMotors(float low, float high)
        {
            var pad = Gamepad.current;
            if (pad == null) { DuckovController.Log.Debug_("UnityRumbleBackend: Gamepad.current is NULL"); return; }
            pad.SetMotorSpeeds(Mathf.Clamp01(low), Mathf.Clamp01(high));
        }

        public void Reset()
        {
            var pad = Gamepad.current;
            if (pad == null) return;
            pad.SetMotorSpeeds(0f, 0f);
            try { pad.ResetHaptics(); } catch { /* not all devices implement it */ }
        }
    }
}
