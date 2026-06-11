using HarmonyLib;
using UnityEngine;

namespace DuckovController.Patches
{
    // Postfix on Health.Hurt(DamageInfo) — fires after the game has computed
    // finalDamage (post-armor, post-element) and applied it to CurrentHealth.
    // Fires DamageTaken haptic only for the player's Health component;
    // enemies hurting each other are silently skipped by IsMainCharacterHealth.
    [HarmonyPatch(typeof(Health), "Hurt")]
    internal static class DamageHapticPatch
    {
        [HarmonyPostfix]
        internal static void Postfix(Health __instance, DamageInfo damageInfo, bool __result)
        {
            // Only rumble when the player was actually hurt (Hurt returns false for invincible/dead/loading).
            if (!__result) return;
            if (!__instance.IsMainCharacterHealth) return;

            float maxHp = __instance.MaxHealth;
            float damage = damageInfo.finalDamage;
            float scale = (maxHp > 0f && damage > 0f)
                ? Mathf.Clamp(damage / maxHp, 0.3f, 1.0f)
                : 0.7f;

            DuckovController.Haptics.HapticEngine.Instance?.Play(
                DuckovController.Haptics.HapticCue.DamageTaken, scale);
            Log.Debug_($"Haptic: DamageTaken scale={scale:0.00} dmg={damage:0.0} maxHp={maxHp:0.0}");
        }
    }
}
