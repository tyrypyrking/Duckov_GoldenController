using DuckovController.Config;
using UnityEngine;

namespace DuckovController.Aim
{
    // Gated boot self-check for the pure recoil/lead math. Runs once when DebugLog is on;
    // logs [selfcheck] PASS/FAIL. Zero cost in release (caller gates on DebugLog).
    internal static class RecoilLeadSelfCheck
    {
        private static bool _ran;

        internal static void RunOnce()
        {
            if (_ran) return;
            _ran = true;
            bool ok = true;

            var cfg = new RecoilConfig
            {
                LeadFraction = 1.0f, MaxLeadMeters = 100f, MaxFlightSeconds = 10f,
                RecoverRate = 100f, RecoverHoldSec = 0f,
                CounterGainToward = 1.0f, CounterGainAway = 0.0f,
            };

            // Lead: target 10 m away, bullet 10 m/s -> 1 s flight; vel (2,0,0) -> lead 2 m.
            var p = TargetLead.Compute(
                bodyCenter: new Vector3(10f, 0f, 0f), enemyVelocity: new Vector3(2f, 0f, 0f),
                shooterPos: Vector3.zero, effBulletSpeed: 10f, cfg: cfg);
            ok &= Approx(p.x, 12f) && Approx(p.y, 0f) && Approx(p.z, 0f);

            // Lead Off: LeadFraction 0 returns body center unchanged.
            var cfg0 = new RecoilConfig { LeadFraction = 0f };
            var p0 = TargetLead.Compute(new Vector3(10f, 0f, 0f), new Vector3(9f, 0f, 0f),
                                        Vector3.zero, 10f, cfg0);
            ok &= Approx(p0.x, 10f);

            // Lead cap: huge velocity clamps to MaxLeadMeters.
            var cfgCap = new RecoilConfig { LeadFraction = 1f, MaxLeadMeters = 3f, MaxFlightSeconds = 10f };
            var pc = TargetLead.Compute(new Vector3(10f, 0f, 0f), new Vector3(1000f, 0f, 0f),
                                        Vector3.zero, 10f, cfgCap);
            ok &= Approx(Vector3.Distance(pc, new Vector3(10f, 0f, 0f)), 3f);

            // Counter-steer asymmetry: offset (10,0). Motion toward recover (-1,0) bleeds;
            // equal motion away (+1,0) does not (CounterGainAway = 0).
            var off = new Vector2(10f, 0f);
            var toward = RecoilAssist.Integrate(off, new Vector2(-5f, 0f), 0f, 0f, cfg); // hold passes (Hold=0,dt=0 -> no decay)
            var away   = RecoilAssist.Integrate(off, new Vector2( 5f, 0f), 0f, 0f, cfg);
            ok &= toward.x < off.x - 4f;   // bled toward zero by ~5
            ok &= Approx(away.x, 10f);     // wrong way wasted

            // Auto-recover: dt moves offset toward zero at RecoverRate after the hold.
            var rec = RecoilAssist.Integrate(new Vector2(10f, 0f), Vector2.zero, 0.05f, 1f, cfg);
            ok &= rec.x < 10f && rec.x >= 0f;

            Log.Debug_($"[selfcheck] recoil/lead {(ok ? "PASS" : "FAIL")}");

            // --- Bias-ring v2 pure geometry ---
            bool br = true;
            var ideal = new Vector2(500f, 400f);
            var stick = new Vector2(0.6f, 0f);
            // Full strength glues to ideal regardless of stick.
            br &= BiasRing.RoamDesired(ideal, stick, 100f, 1f) == ideal;
            // Centered stick => ideal.
            br &= BiasRing.RoamDesired(ideal, Vector2.zero, 100f, 0.5f) == ideal;
            // No bias => ideal + stick*R.
            var noBias = BiasRing.RoamDesired(ideal, stick, 100f, 0f);
            br &= Approx(noBias.x, ideal.x + 0.6f * 100f) && Approx(noBias.y, ideal.y);
            // Band: 0.95 in (band .08 => threshold .92); 0.50 out.
            br &= BiasRing.InEscapeBand(0.95f, 0.08f) && !BiasRing.InEscapeBand(0.50f, 0.08f);
            // Directional suppression: kick up, push down (counter) => suppressed; push up (with kick) => not.
            br &= BiasRing.IsCounterSuppressed(new Vector2(0f, -1f), new Vector2(0f, 5f), 0.25f);
            br &= !BiasRing.IsCounterSuppressed(new Vector2(0f, 1f), new Vector2(0f, 5f), 0.25f);
            Log.Debug_($"[selfcheck] bias-ring {(br ? "PASS" : "FAIL")}");
        }

        private static bool Approx(float a, float b) => Mathf.Abs(a - b) < 0.01f;
    }
}
