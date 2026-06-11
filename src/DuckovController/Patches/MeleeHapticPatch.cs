using HarmonyLib;
using UnityEngine;

namespace DuckovController.Patches
{
    // Postfix on ItemAgent_MeleeWeapon.CheckAndDealDamage() — fires exactly
    // when the melee weapon sweeps for damage, once per swing.
    // Player-only: gated on Holder.IsMainCharacter.
    [HarmonyPatch(typeof(ItemAgent_MeleeWeapon), "CheckAndDealDamage")]
    internal static class MeleeHapticPatch
    {
        private const float MeleeRef = 50f; // provisional — tune from logged dmg values

        [HarmonyPostfix]
        internal static void Postfix(ItemAgent_MeleeWeapon __instance)
        {
            var holder = __instance.Holder;
            if (holder == null || !holder.IsMainCharacter) return;

            float dmg = __instance.Damage;
            float scale = Mathf.Clamp(dmg / MeleeRef, 0.4f, 1.0f);

            DuckovController.Haptics.HapticEngine.Instance?.Play(
                DuckovController.Haptics.HapticCue.MeleeHit, scale);
            Log.Debug_($"Haptic: MeleeHit dmg={dmg:0.0} scale={scale:0.00}");
        }
    }
}
