using DuckovController.Config;

namespace DuckovController.Aim
{
    // Preset bundler: Apply() writes curated defaults into cfg when tier != Custom.
    // Custom = sentinel to leave JSON values intact (settings UI edits individual knobs).
    // Magnetism/Slowdown knobs in AimConfig are also rolled so one tier choice scales the whole stack.
    internal static class AutoAimTiers
    {
        internal static void Apply(ControllerConfig cfg)
        {
            var aa = cfg.AutoAim;
            var aim = cfg.Aim;
            var sc = cfg.Scope;
            var rc = cfg.Recoil;
            var br = cfg.BiasRing;
            switch (aa.Tier)
            {
                case AutoAimTier.Off:
                    aa.Enabled = false;
                    aa.MeleeMaxTurnDegrees = 0f;
                    aim.MagnetismEnabled = false;
                    aim.SlowdownEnabled = false;
                    // Scope: pure free-look, no assist.
                    sc.SoftAssistEnabled = false;
                    sc.SlowdownFactor = 1.0f;
                    sc.SlowdownRadiusPx = 0f;
                    sc.SettleRadiusPx = 0f;
                    rc.KickScale = 1.00f; rc.RecoverRate = 300f; rc.RecoverHoldSec = 0.12f;
                    rc.CounterGainToward = 0f; rc.CounterGainAway = 0f; rc.LeadFraction = 0f;
                    br.RingRadiusPx = 0f; // ring inactive => CursorBlend fallback (existing Off behavior)
                    break;

                case AutoAimTier.Light:
                    aa.Enabled = false;
                    aa.MeleeMaxTurnDegrees = 0f;
                    aim.MagnetismEnabled = true;
                    aim.MagnetismStrength = 0.40f;
                    aim.SlowdownEnabled = false;
                    sc.SoftAssistEnabled = true;
                    sc.SlowdownFactor = 0.60f;
                    sc.SlowdownRadiusPx = 80f;
                    sc.SettleRadiusPx = 50f;
                    sc.SettleTauSeconds = 0.20f;
                    sc.EscapeStickMag = 0.45f;
                    rc.KickScale = 0.85f; rc.RecoverRate = 340f; rc.RecoverHoldSec = 0.11f;
                    rc.CounterGainToward = 1.0f; rc.CounterGainAway = 0.10f; rc.LeadFraction = 0.40f;
                    br.RingRadiusPx = 170f; br.BiasStrength = 0.50f; br.EscapeDwellSec = 0.40f; // heaviest recoil => biggest ring + longest dwell to recoil-control without escaping
                    break;

                case AutoAimTier.Standard:
                    aa.Enabled = false;
                    aa.MeleeMaxTurnDegrees = 0f;
                    aim.MagnetismEnabled = true;
                    aim.MagnetismStrength = 0.70f;
                    aim.SlowdownEnabled = true;
                    aim.SlowdownFactor = 0.50f;
                    sc.SoftAssistEnabled = true;
                    sc.SlowdownFactor = 0.45f;
                    sc.SlowdownRadiusPx = 110f;
                    sc.SettleRadiusPx = 80f;
                    sc.SettleTauSeconds = 0.14f;
                    sc.EscapeStickMag = 0.55f;
                    rc.KickScale = 0.60f; rc.RecoverRate = 420f; rc.RecoverHoldSec = 0.10f;
                    // CounterGainToward 1.6 instantly bled the whole recoil offset on a hard push ("no
                    // recoil when overcorrected"); 0.9 lets recoil persist as a felt force while still rewarding the right pull.
                    rc.CounterGainToward = 0.9f; rc.CounterGainAway = 0.05f; rc.LeadFraction = 0.70f;
                    br.RingRadiusPx = 130f; br.BiasStrength = 0.45f; br.EscapeDwellSec = 0.15f; // 0.45 lets the stick roam ~55% of the ring; 0.15 dwell so the ADS safety guard releases faster
                    break;

                case AutoAimTier.Aggressive:
                    aa.Enabled = true;
                    aa.MeleeMaxTurnDegrees = 130f;
                    aa.SnapStrength = 0.60f;
                    aa.ReturnSpeed = 0.40f;
                    aa.WeightScreenDist = 1.0f;
                    aa.WeightWorldDist = 0.3f;
                    aa.WeightCenterness = 0.5f;
                    aa.WeightLowHp = 0.0f;
                    aa.SwitchMargin = 0.30f;
                    aa.MinLockTimeMs = 200;
                    aa.TargetThroughWalls = false;
                    aa.MaxTargetDistanceMeters = 22f;
                    aa.DecoupleViewFromAim = true;
                    aim.MagnetismEnabled = true;
                    aim.MagnetismStrength = 0.70f;
                    aim.SlowdownEnabled = true;
                    aim.SlowdownFactor = 0.35f;
                    sc.SoftAssistEnabled = true;
                    sc.SlowdownFactor = 0.30f;
                    sc.SlowdownRadiusPx = 140f;
                    sc.SettleRadiusPx = 110f;
                    sc.SettleTauSeconds = 0.10f;
                    sc.EscapeStickMag = 0.65f;
                    rc.KickScale = 0.40f; rc.RecoverRate = 520f; rc.RecoverHoldSec = 0.08f;
                    rc.CounterGainToward = 2.2f; rc.CounterGainAway = 0.0f; rc.LeadFraction = 0.90f;
                    br.RingRadiusPx = 100f; br.BiasStrength = 0.85f; br.EscapeDwellSec = 0.18f; // low recoil => little control needed: smaller ring + shorter dwell
                    break;

                case AutoAimTier.Cheat:
                    aa.Enabled = true;
                    aa.MeleeMaxTurnDegrees = 180f;
                    aa.SnapStrength = 1.00f;
                    aa.ReturnSpeed = 0.80f;
                    aa.WeightScreenDist = 1.0f;
                    aa.WeightWorldDist = 0.6f;
                    aa.WeightCenterness = 0.3f;
                    aa.WeightLowHp = 0.2f;
                    aa.SwitchMargin = 0.00f;
                    aa.MinLockTimeMs = 0;
                    aa.TargetThroughWalls = true;
                    aa.MaxTargetDistanceMeters = 28f;
                    aa.DecoupleViewFromAim = true;
                    aim.MagnetismEnabled = true;
                    aim.MagnetismStrength = 1.00f;
                    aim.SlowdownEnabled = true;
                    aim.SlowdownFactor = 0.35f;
                    // Scope NEVER hard-locks even at Cheat: strong/sticky settle, still escapable.
                    sc.SoftAssistEnabled = true;
                    sc.SlowdownFactor = 0.20f;
                    sc.SlowdownRadiusPx = 180f;
                    sc.SettleRadiusPx = 150f;
                    sc.SettleTauSeconds = 0.06f;
                    sc.EscapeStickMag = 0.80f;
                    rc.KickScale = 0.00f; rc.RecoverRate = 600f; rc.RecoverHoldSec = 0.06f;
                    rc.CounterGainToward = 3.0f; rc.CounterGainAway = 0.0f; rc.LeadFraction = 1.00f;
                    br.RingRadiusPx = 75f; br.BiasStrength = 1.00f; br.EscapeDwellSec = 0.12f; // no recoil => no control need: smallest ring + shortest dwell (glued anyway at strength 1.0)
                    break;

                case AutoAimTier.Custom:
                    // Intentionally no-op: respect JSON-supplied fields.
                    break;
            }

            // AIM-6: cloak gate is tier-independent (a correctness gate, not a feel knob), so it is
            // applied unconditionally here — including for the Custom tier branch above.
            CloakGate.Enabled = aa.RespectCloak;

            // AIM-1: hand the (possibly tier-overwritten) recoil block to the sim so the
            // per-frame Apply/OnShot read live values. Mirrors the CloakGate pattern: applied
            // unconditionally, including for the Custom tier.
            RecoilAssist.Configure(cfg.Recoil);
            // AIM-1: share the same live RecoilConfig with the predictive-lead layers.
            AutoAim.RecoilCfg = cfg.Recoil;
            // AIM-1 v2.x: counter-recoil suppression cone, code-authoritative (so on-device calibration
            // isn't blocked by a value pinned in Settings.json). ~100° (cos100°≈-0.17): 135° felt too
            // sticky. Only the ADS path consults this now (hip-fire has no safety guard).
            cfg.BiasRing.CounterSuppressDot = -0.17f;
            // AIM-1 v2: share the (possibly tier-overwritten) bias-ring config with the lock.
            BiasRing.Configure(cfg.BiasRing);

            // DIAG (zero-assist hunt): always-on snapshot of the EFFECTIVE aim state right after a tier
            // apply. Logged for BOTH boot (LoadOrDefault) and the settings toggle so the two can be
            // compared line-for-line — if Standard works after a Cheat→Standard cycle but not at boot,
            // these lines reveal whether the static config differs or whether something else (runtime
            // state) is the culprit. Log.Info (not Debug_) so it survives DebugLog=false.
            Log.Info($"[aimapply] tier={aa.Tier} aa.Enabled={aa.Enabled} "
                + $"biasRing.Enabled={br.Enabled} ringRadiusPx={br.RingRadiusPx:0.#} "
                + $"=> TierActive={BiasRing.TierActive} recoil.Enabled={rc.Enabled} "
                + $"magnetism={aim.MagnetismEnabled} slowdown={aim.SlowdownEnabled} "
                + $"baselineAssist={aim.BaselineAssistEnabled} respectCloak={aa.RespectCloak}");
        }
    }
}
