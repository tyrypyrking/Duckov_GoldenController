using UnityEngine;

namespace DuckovController.Aim
{
    // Stateless candidate finder. Returns enemies inside sight cone (angle<=halfAngleDeg, dist<=viewDist)
    // OR inside 360° sense radius (dist<=senseRange), with SphereCast LOS unless ignoreObstacles.
    // SenseRange inclusion future-proofs melee aim assist (close enemies behind player).
    // No allocations: reuses a static Collider[] buffer.
    internal static class ViewConeQuery
    {
        // Shared per-frame buffers. Mod is main-thread only; plain static is safe.
        private static readonly Collider[] _overlapBuf = new Collider[32];

        internal struct CandidateInfo
        {
            public Transform Transform;
            public CharacterMainControl MainControl;
            public Vector3 BodyCenter;     // best world-space point to aim at
            public float WorldDist;
            public float AngleFromAxisDeg;
            public float HpFraction;
            public bool InCone;            // true = cone hit, false = sense-only
        }

        // Diagnostic stage tallies — filled on every call; logged by callers under DebugLog only.
        // Lets us see WHERE candidates are rejected (e.g. all RangeSkip vs all LosBlocked).
        internal struct Stats
        {
            public int OverlapHits, NoReceiver, NoControl, SelfSkip, HiddenSkip, CloakSkip,
                       NotEnemy, DeadSkip, RangeSkip, LosBlocked, OffScreen, Accepted;
        }
        internal static Stats LastStats;

        // Mirror AimTargetFinder's LayerMask rather than guessing the layer index. Resolved once.
        private static int _dmgReceiverMask = -1;
        private static int _obstacleMask = -1;
        private static bool _maskResolved;
        private static bool _masksLogged;

        // DIAG (zero-assist hunt): change-deduped tally so we see WHERE candidates die — e.g.
        // OverlapHits=0 (mask wrong / no enemies in radius) vs OverlapHits>0 + all in one reject
        // bucket (NotEnemy / RangeSkip / LosBlocked / OffScreen). Only prints when the tally changes.
        private static string _lastStatsSig = "";
        private static void LogStats()
        {
            if (!Log.Verbose) return;
            var s = LastStats;
            string sig = $"{s.OverlapHits},{s.NoReceiver},{s.NoControl},{s.SelfSkip},{s.HiddenSkip},"
                + $"{s.CloakSkip},{s.NotEnemy},{s.DeadSkip},{s.RangeSkip},{s.LosBlocked},{s.OffScreen},{s.Accepted}";
            if (sig == _lastStatsSig) return;
            _lastStatsSig = sig;
            Log.Debug_($"[conestats] overlap={s.OverlapHits} noRecv={s.NoReceiver} noCtrl={s.NoControl} "
                + $"self={s.SelfSkip} hidden={s.HiddenSkip} cloak={s.CloakSkip} notEnemy={s.NotEnemy} "
                + $"dead={s.DeadSkip} range={s.RangeSkip} los={s.LosBlocked} offScreen={s.OffScreen} "
                + $"ACCEPTED={s.Accepted}");
        }

        private static void ResolveMasks(InputManager im)
        {
            if (_maskResolved) return;
            _maskResolved = true;
            try
            {
                var fld = typeof(InputManager).GetField("damageReceiverLayerMask",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic);
                if (fld != null)
                {
                    var v = fld.GetValue(im);
                    if (v is LayerMask lm) _dmgReceiverMask = lm.value;
                    else if (v is int i) _dmgReceiverMask = i;
                }
                if (_dmgReceiverMask < 0)
                {
                    int idx = LayerMask.NameToLayer("DamageReceiver"); // fallback: game layer is "DamageReceiver"
                    _dmgReceiverMask = idx >= 0 ? (1 << idx) : Physics.DefaultRaycastLayers;
                    Log.Warn("ViewConeQuery: damageReceiverLayerMask field not found; "
                             + $"falling back to layer-name mask 0x{_dmgReceiverMask:X}");
                }
                _obstacleMask = ~_dmgReceiverMask & Physics.DefaultRaycastLayers;
            }
            catch (System.Exception e)
            {
                Log.Warn($"ViewConeQuery: mask resolution threw: {e.Message}");
                _dmgReceiverMask = Physics.DefaultRaycastLayers;
                _obstacleMask = Physics.DefaultRaycastLayers;
            }
        }

        // Returns count written. Outputs are filled into outBuffer[0..count-1].
        internal static int FindEnemiesInCone(
            InputManager im,
            Vector3 playerPos,
            Vector3 aimDir,
            float halfAngleDeg,
            float viewDist,
            float senseRange,
            Teams playerTeam,
            bool ignoreObstacles,
            Camera? cam,
            float onScreenMarginFrac,
            CandidateInfo[] outBuffer)
        {
            if (im == null || outBuffer == null || outBuffer.Length == 0) return 0;
            ResolveMasks(im);

            // DIAG (zero-assist hunt): one-time dump of the RESOLVED masks. The damageReceiverLayerMask
            // reflection on InputManager fails (logged "field not found; falling back to 0x8"), so the
            // OverlapSphere mask + derived obstacle mask are suspect — this shows exactly what they are.
            if (Log.Verbose && !_masksLogged)
            {
                _masksLogged = true;
                Log.Debug_($"[conestats] masks dmgReceiver=0x{_dmgReceiverMask:X} obstacle=0x{_obstacleMask:X} "
                    + $"defaultRaycast=0x{Physics.DefaultRaycastLayers:X}");
            }

            // On-screen gate: perception range can exceed Deck screen; locking off-screen feels like cheating.
            // Margin gives edge hysteresis so a target at the edge doesn't flicker.
            float screenMarginX = onScreenMarginFrac * Screen.width;
            float screenMarginY = onScreenMarginFrac * Screen.height;

            var radius = Mathf.Max(viewDist, senseRange);
            if (radius <= 0f) return 0;

            var axis = aimDir; axis.y = 0f;
            if (axis.sqrMagnitude < 1e-6f) axis = Vector3.forward; else axis.Normalize();

            float cosHalf = Mathf.Cos(halfAngleDeg * Mathf.Deg2Rad);

            int hitCount = Physics.OverlapSphereNonAlloc(
                playerPos, radius, _overlapBuf, _dmgReceiverMask,
                QueryTriggerInteraction.Ignore);

            LastStats = default;
            LastStats.OverlapHits = hitCount;

            int written = 0;
            for (int i = 0; i < hitCount && written < outBuffer.Length; i++)
            {
                var col = _overlapBuf[i];
                if (col == null) continue;

                var dr = col.GetComponentInParent<DamageReceiver>();
                if (dr == null) { LastStats.NoReceiver++; continue; }
                var mc = dr.GetComponentInParent<CharacterMainControl>();
                if (mc == null) { LastStats.NoControl++; continue; }

                // Skip self.
                if (mc.transform.position == playerPos) { LastStats.SelfSkip++; continue; }

                // mc.Hidden = DuckovHider-driven FOW state; never target unperceived enemies.
                if (mc.Hidden) { LastStats.HiddenSkip++; continue; }
                // AIM-6: never target a render-cloaked enemy the player can't currently see.
                if (CloakGate.IsCloakedFromPlayer(mc)) { LastStats.CloakSkip++; continue; }

                Teams team = mc.Team;
                if (!Team.IsEnemy(playerTeam, team)) { LastStats.NotEnemy++; continue; }
                if (mc.Health == null || mc.Health.MaxHealth <= 0f) { LastStats.DeadSkip++; continue; }
                float hpFraction = mc.Health.CurrentHealth / mc.Health.MaxHealth;
                if (hpFraction <= 0f) { LastStats.DeadSkip++; continue; }

                var bodyCenter = col.bounds.center;
                var to = bodyCenter - playerPos;
                var toFlat = to; toFlat.y = 0f;
                float dist = toFlat.magnitude;

                bool inSense = dist <= senseRange;
                bool inConeGeom = false;
                float angleDeg = 180f;
                if (dist <= viewDist && dist > 0f)
                {
                    float cos = Vector3.Dot(toFlat / dist, axis);
                    if (cos >= cosHalf)
                    {
                        inConeGeom = true;
                        angleDeg = Mathf.Acos(Mathf.Clamp(cos, -1f, 1f)) * Mathf.Rad2Deg;
                    }
                }
                if (!inSense && !inConeGeom) { LastStats.RangeSkip++; continue; }

                if (!ignoreObstacles)
                {
                    // SphereCast LOS; small radius = permissive (mirrors game's cone bullet cast).
                    var rayOrigin = playerPos + Vector3.up * 1.4f;
                    var rayDir = (bodyCenter - rayOrigin);
                    float rayLen = rayDir.magnitude;
                    if (rayLen > 0.01f)
                    {
                        rayDir /= rayLen;
                        if (Physics.SphereCast(rayOrigin, 0.05f, rayDir, out var hit,
                                rayLen - 0.05f, _obstacleMask, QueryTriggerInteraction.Ignore))
                        {
                            if (hit.collider != null && hit.collider.GetComponentInParent<DamageReceiver>() == null)
                            { LastStats.LosBlocked++; continue; }
                        }
                    }
                }

                if (cam != null)
                {
                    var sp = cam.WorldToScreenPoint(bodyCenter);
                    if (sp.z <= 0f
                        || sp.x < -screenMarginX || sp.x > Screen.width + screenMarginX
                        || sp.y < -screenMarginY || sp.y > Screen.height + screenMarginY)
                    { LastStats.OffScreen++; continue; }
                }

                LastStats.Accepted++;
                outBuffer[written++] = new CandidateInfo
                {
                    Transform = mc.transform,
                    MainControl = mc,
                    BodyCenter = bodyCenter,
                    WorldDist = dist,
                    AngleFromAxisDeg = inConeGeom ? angleDeg : 180f, // sense-only = no cone weight
                    HpFraction = hpFraction,
                    InCone = inConeGeom,
                };
            }

            for (int i = 0; i < hitCount; i++) _overlapBuf[i] = null; // release refs so Unity doesn't pin destroyed colliders

            LogStats();
            return written;
        }
    }
}
