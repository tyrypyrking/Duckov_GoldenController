using DuckovController.Aim;
using DuckovController.Config;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuckovController.Patches
{
    // Diverts the main character's per-shot recoil into RecoilAssist when the pad is driving
    // aim, and skips the game's own recoil accumulation (we own it). KBM users (no diverting)
    // keep vanilla recoil.
    [HarmonyPatch(typeof(InputManager), "AddRecoil")]
    internal static class AddRecoilPatch
    {
        private static int _logSuppressFrame = -1;

        // Return false => skip original AddRecoil.
        [HarmonyPrefix]
        internal static bool Prefix(InputManager __instance, ItemAgent_Gun gun)
        {
            try
            {
                if (!DuckovController.Diagnostics.PerfFlags.AimDriver) return true;
                if (!RecoilAssist.Enabled) return true;
                if (Gamepad.current == null) return true;     // KBM -> vanilla recoil
                if (gun == null || __instance == null) return true;

                var ch = __instance.ControllingCharacter;
                if (ch == null) return true;

                // Mirror AddRecoil's magnitudes (caliber-faithful).
                float mult = LevelManager.Rule.RecoilMultiplier;
                float inv = gun.CharacterRecoilControl != 0f ? 1f / gun.CharacterRecoilControl : 1f;
                float recoilV = Random.Range(gun.RecoilVMin, gun.RecoilVMax) * gun.RecoilScaleV * inv * mult;
                float recoilH = Random.Range(gun.RecoilHMin, gun.RecoilHMax) * gun.RecoilScaleH * inv * mult;

                // Basis: current cursor vs player screen pos.
                Vector2 cursor = RadialCursor.TryReadAim(__instance, out var c) ? c
                    : (Mouse.current != null ? Mouse.current.position.ReadValue()
                                             : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
                Vector2 playerScreen = cursor;
                var cam = Camera.main;
                if (cam != null)
                {
                    var sp = cam.WorldToScreenPoint(ch.transform.position);
                    if (sp.z > 0f && !float.IsNaN(sp.x)) playerScreen = new Vector2(sp.x, sp.y);
                }

                RecoilAssist.OnShot(recoilV, recoilH, cursor, playerScreen);
                return false;   // we own recoil; suppress the game's accumulation
            }
            catch (System.Exception e)
            {
                var frame = Time.frameCount;
                if (frame - _logSuppressFrame > 600)
                {
                    Log.Warn($"AddRecoilPatch threw: {e.Message}");
                    _logSuppressFrame = frame;
                }
                return true;    // fail safe: let the game apply its own recoil
            }
        }
    }
}
