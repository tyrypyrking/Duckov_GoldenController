using HarmonyLib;
using UnityEngine;

namespace DuckovController.Patches
{
    // Postfix on CharacterMainControl.TriggerShootEvent(DuckovItemAgent) —
    // called by ItemAgent_Gun once per bullet fired, after recoil is applied.
    // The DuckovItemAgent arg is the actual ItemAgent_Gun instance.
    // Player-only: TriggerShootEvent is only called on the holder's character,
    // and we gate on __instance.IsMainCharacter.
    [HarmonyPatch(typeof(CharacterMainControl), "TriggerShootEvent")]
    internal static class WeaponFireHapticPatch
    {
        private const float RecoilRef = 3.0f; // provisional — tune from logged recoilV values

        [HarmonyPostfix]
        internal static void Postfix(CharacterMainControl __instance, DuckovItemAgent shootByAgent)
        {
            if (!__instance.IsMainCharacter) return;
            if (shootByAgent == null) return;

            var gun = shootByAgent as ItemAgent_Gun;
            float recoilV = gun != null ? gun.RecoilScaleV : 0.5f; // fallback for non-gun agents
            float scale = Mathf.Clamp(recoilV / RecoilRef, 0.4f, 1.0f);

            DuckovController.Haptics.HapticEngine.Instance?.Play(
                DuckovController.Haptics.HapticCue.WeaponFire, scale);
            Log.Debug_($"Haptic: WeaponFire recoilV={recoilV:0.00} scale={scale:0.00}");
        }
    }
}
