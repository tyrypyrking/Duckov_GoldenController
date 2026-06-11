using DuckovController.Config;
using UnityEngine;

namespace DuckovController.Aim
{
    // Pure predictive-lead math: where to aim so a bullet of finite speed intercepts a
    // moving target. No game-object coupling (takes primitives) so it is self-checkable.
    internal static class TargetLead
    {
        // Returns the world aim point = bodyCenter + clamped predicted lead.
        // leadFraction <= 0 (Off tier) returns bodyCenter unchanged.
        internal static Vector3 Compute(
            Vector3 bodyCenter, Vector3 enemyVelocity, Vector3 shooterPos,
            float effBulletSpeed, RecoilConfig? cfg)
        {
            if (cfg == null || cfg.LeadFraction <= 0f) return bodyCenter;
            float speed = Mathf.Max(0.01f, effBulletSpeed);
            float dist = Vector3.Distance(shooterPos, bodyCenter);
            float flight = Mathf.Min(dist / speed, cfg.MaxFlightSeconds);
            Vector3 lead = enemyVelocity * (flight * cfg.LeadFraction);
            float maxLead = cfg.MaxLeadMeters;
            if (maxLead > 0f && lead.sqrMagnitude > maxLead * maxLead)
                lead = lead.normalized * maxLead;
            return bodyCenter + lead;
        }

        // Effective muzzle speed (caliber-faithful): gun stat x holder multiplier.
        internal static float EffectiveBulletSpeed(ItemAgent_Gun gun, CharacterMainControl holder)
        {
            if (gun == null) return 0f;
            float s = gun.BulletSpeed;
            if (holder != null) s *= holder.GunBulletSpeedMultiplier;
            return s;
        }
    }
}
