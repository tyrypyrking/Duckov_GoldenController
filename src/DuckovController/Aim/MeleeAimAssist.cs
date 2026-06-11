using DuckovController.Config;
using Duckov.Utilities;
using UnityEngine;

namespace DuckovController.Aim
{
    // On-attack melee aim-assist: when the player presses RT with melee held, snaps facing to the
    // nearest enemy within ~5m (MeleeAcquireRange, independent of gun MaxTargetDistanceMeters) and
    // within MeleeMaxTurnDegrees of current facing. Holds aim through the swing via HoldAim.
    internal static class MeleeAimAssist
    {
        internal static bool IsLocked { get; private set; }
        private static Transform? _target;
        // Grace frames: keep the lock alive even if attackAction.Running hasn't flipped true yet
        // (StartAction can lag one frame). Lock survives for at least GraceFrames frames after acquire.
        private const int GraceFrames = 2;
        private static int _framesHeld;
        private static string _lastTrace = "";

        internal static void Reset()
        {
            IsLocked = false;
            _target = null;
            _framesHeld = 0;
            _lastTrace = "";
        }

        private static void Trace(string s)
        {
            if (s == _lastTrace) return;
            _lastTrace = s;
            Log.Debug_($"[melee-aa] {s}");
        }

        // Called on RT press-edge when melee is held. Finds and snaps to nearest valid enemy.
        internal static void OnAttackPressed(CharacterMainControl ch, AutoAimConfig cfg)
        {
            IsLocked = false;
            _target = null;
            _framesHeld = 0;

            Log.Debug_($"[melee-aa] attack: maxTurn={cfg?.MeleeMaxTurnDegrees}, meleeHeld={ch?.GetMeleeWeapon() != null}");

            if (cfg == null || cfg.MeleeMaxTurnDegrees <= 0f)
            {
                Log.Debug_("[melee-aa] skip: maxTurn<=0 or cfg null");
                return;
            }
            var melee = ch?.GetMeleeWeapon();
            if (melee == null)
            {
                Log.Debug_("[melee-aa] skip: no melee weapon");
                return;
            }

            float range = Mathf.Max(melee.AttackRange, cfg.MeleeAcquireRange);
            var t = FindNearestEnemy(ch!, range, cfg.MeleeMaxTurnDegrees);
            if (t == null)
            {
                Log.Debug_($"[melee-aa] no target (range={range:F1}m, maxTurn={cfg.MeleeMaxTurnDegrees})");
                return;
            }

            _target = t;
            IsLocked = true;
            _framesHeld = 0;

            Vector3 dir = t.position - ch!.transform.position;
            dir.y = 0f;
            float dist = dir.magnitude;
            if (dir.sqrMagnitude > 1e-6f)
            {
                dir.Normalize();
                ch.movementControl.ForceTurnTo(dir);
                ch.SetAimPoint(t.position);
            }
            Trace($"locked → {t.name} ({dist:F1}m)");
        }

        // Called each frame from AimDriverPatch while IsLocked. Returns true = still locked (caller
        // skips gun aim). Returns false = swing ended or target gone (caller falls through to normal aim).
        internal static bool HoldAim(CharacterMainControl? ch)
        {
            if (!IsLocked) return false;
            if (ch == null) { Reset(); return false; }

            _framesHeld++;

            // Clear lock when: target gone/dead, OR swing ended after the grace window.
            bool targetValid = _target != null;
            if (targetValid)
            {
                var mc = _target!.GetComponentInParent<CharacterMainControl>();
                if (mc == null || mc.Health == null || mc.Health.CurrentHealth <= 0f || mc.Hidden
                    || CloakGate.IsCloakedFromPlayer(mc)) // AIM-6: drop lock if player removes thermal mid-swing
                    targetValid = false;
            }

            bool swingRunning = ch.attackAction != null && ch.attackAction.Running;
            bool inGrace = _framesHeld <= GraceFrames;

            if (!targetValid || (!swingRunning && !inGrace))
            {
                Trace("released");
                Reset();
                return false;
            }

            if (targetValid && _target != null)
                ch.SetAimPoint(_target.position);

            return true;
        }

        // Moves the crosshair onto the locked target so the visual matches the snap.
        // Skips behind-camera targets (sp.z<=0) — SetAimPoint in HoldAim still drives the hit.
        internal static void DriveCrosshair(InputManager im, Camera? cam)
        {
            if (!IsLocked || _target == null || cam == null) return;
            var sp = cam.WorldToScreenPoint(_target.position + Vector3.up * 0.5f);
            if (sp.z <= 0f || float.IsNaN(sp.x) || float.IsNaN(sp.y)) return;
            var screen = new Vector2(sp.x, sp.y);
            RadialCursor.WriteAbsoluteRaw(im, screen);
            if (UnityEngine.InputSystem.Mouse.current != null)
                UnityEngine.InputSystem.Mouse.current.WarpCursorPosition(screen);
        }

        // OverlapSphere with the damage-receiver mask (same as ItemAgent_MeleeWeapon.OverlapSphere).
        // Skips self, dead, Hidden, Dashing, same-team. Angle-filters to maxTurnDeg of CurrentAimDirection.
        // Returns the CharacterMainControl.transform of the nearest qualifying enemy.
        private static readonly Collider[] _buf = new Collider[16];
        private static Transform? FindNearestEnemy(CharacterMainControl ch, float range, float maxTurnDeg)
        {
            Vector3 origin = ch.transform.position;
            int hit = Physics.OverlapSphereNonAlloc(
                origin, range, _buf,
                (int)GameplayDataSettings.Layers.damageReceiverLayerMask,
                QueryTriggerInteraction.Ignore);

            Transform? best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < hit; i++)
            {
                var col = _buf[i];
                if (col == null) continue;

                var dr = col.GetComponentInParent<DamageReceiver>();
                if (dr == null) continue;
                var mc = dr.GetComponentInParent<CharacterMainControl>();
                if (mc == null || mc == ch) continue;
                if (!Team.IsEnemy(ch.Team, mc.Team)) continue;
                if (mc.Health == null || mc.Health.CurrentHealth <= 0f) continue;
                if (mc.Hidden) continue;
                if (CloakGate.IsCloakedFromPlayer(mc)) continue; // AIM-6
                if (mc.Dashing) continue;

                Vector3 to = col.bounds.center - origin; to.y = 0f;
                if (to.sqrMagnitude < 1e-6f) continue;
                if (Vector3.Angle(ch.CurrentAimDirection, to) > maxTurnDeg) continue;

                float sqr = to.sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = mc.transform; }
            }

            // Clear refs so Unity doesn't pin destroyed colliders.
            for (int i = 0; i < hit; i++) _buf[i] = null!;
            return best;
        }
    }
}
