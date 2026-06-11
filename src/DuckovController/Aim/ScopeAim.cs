using System.Reflection;
using DuckovController.Config;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuckovController.Aim
{
    // AIM-4 sniper scope aim. Engaged from AimDriverPatch when LT is held on a scoped gun.
    // Free-look VELOCITY reticle (holds in place when the stick is centered) that the game's
    // GameCamera pans toward natively; plus an ESCAPABLE soft assist (slowdown + gentle settle)
    // that NEVER hard-locks. Owns AimMousePosition while active; the patch skips
    // radial/AutoAim/AdsLock and feeds SetAimInputUsingMouse(zero) so no delta scaling applies.
    internal static class ScopeAim
    {
        private const int MaxCandidates = 12;
        private static readonly ViewConeQuery.CandidateInfo[] _candidates =
            new ViewConeQuery.CandidateInfo[MaxCandidates];

        private static bool _active;
        private static Vector2 _settleVel;    // SmoothDamp velocity for the settle ease
        private static int _settleTargetId;   // instanceID of current settle target (reset vel on switch)

        private static FieldInfo? _mainCamField;
        private static bool _resolved;

        private static string _lastState = "";
        private static int _lastDetailFrame = -1000;   // throttle for the decision-snapshot diagnostic

        internal static bool IsActive => _active;

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            _mainCamField = typeof(InputManager).GetField(
                "mainCam", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        // Scene/view transitions: drop all state so re-entry re-seeds cleanly.
        internal static void Reset()
        {
            _active = false;
            _settleVel = Vector2.zero;
            _settleTargetId = 0;
            _lastState = "";
        }

        // LT-release edge: re-seed the cursor to the normal player-relative radial rest in the
        // current look direction, so the normal path resumes with NO residual far-aim. Then reset.
        internal static void Exit(InputManager im, ControllerConfig cfg)
        {
            try
            {
                if (im != null && cfg != null)
                {
                    Resolve();
                    var ch = im.ControllingCharacter;
                    var cam = _mainCamField?.GetValue(im) as Camera;
                    if (ch != null && cam != null)
                    {
                        Vector3 p0 = ch.transform.position;
                        Vector3 p1 = p0 + ViewDirectionDriver.CurrentDirection;
                        Vector3 s0 = cam.WorldToScreenPoint(p0);
                        Vector3 s1 = cam.WorldToScreenPoint(p1);
                        Vector2 dir = (Vector2)s1 - (Vector2)s0;
                        if (dir.sqrMagnitude > 1e-4f)
                            RadialCursor.WriteAbsolute(im, ch, dir.normalized,
                                                       cfg.Aim.CursorCircleRadiusFactor);
                    }
                }
            }
            catch { }
            Reset();
        }

        private static void Trace(ControllerConfig cfg, string state)
        {
            if (state == _lastState) return;
            _lastState = state;
            if (cfg?.Diagnostics.DebugLog == true) Log.Debug_($"[scope] {state}");
        }

        // Per-frame while scoped. Drives AimMousePosition (free-look + escapable soft assist).
        internal static void Run(CharacterInputControl ctl, ControllerConfig cfg, Gamepad pad)
        {
            try
            {
                if (ctl == null || cfg == null || pad == null) return;
                var im = ctl.inputManager;
                if (im == null) return;
                var ch = im.ControllingCharacter;
                if (ch == null) return;
                Resolve();
                var cam = _mainCamField?.GetValue(im) as Camera;
                if (cam == null) return;

                if (!_active)
                {
                    _active = true;
                    _settleVel = Vector2.zero;
                    _settleTargetId = 0;
                    Trace(cfg, "enter");
                }

                // Current reticle: the radial cursor's last pos on the entry frame, then ours.
                if (!RadialCursor.TryReadAim(im, out Vector2 reticle))
                    reticle = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

                Vector2 raw = pad.rightStick.ReadValue();
                Vector2 stick = RadialDeadzone.Apply(raw, cfg.Aim.DeadzoneInner, cfg.Aim.DeadzoneOuter);
                float stickMag = stick.magnitude;

                // Camera-plane world direction of the stick (for the view cone + escape check).
                Vector3 stickWorldDir = Vector3.zero;
                if (raw.sqrMagnitude > 1e-6f)
                {
                    Vector3 camRight = cam.transform.right; camRight.y = 0f; camRight.Normalize();
                    Vector3 camFwd = cam.transform.forward; camFwd.y = 0f; camFwd.Normalize();
                    stickWorldDir = (camRight * raw.x + camFwd * raw.y).normalized;
                }

                // View cone follows the reticle (raycast it onto the player's ground plane), so it
                // keeps pointing where you aim even when the stick is centered.
                Vector3 playerPos = ch.transform.position;
                Vector3 aimWorld = ScreenToGroundPlane(cam, reticle, playerPos);
                Vector3 toAim = aimWorld - playerPos; toAim.y = 0f;
                if (toAim.sqrMagnitude > 1e-4f)
                    ViewDirectionDriver.SeedFromAimDirection(toAim.normalized);
                Vector3 coneAxis = ViewDirectionDriver.HasDirection
                    ? ViewDirectionDriver.CurrentDirection : ch.CurrentAimDirection;

                // Candidate enemies out to the gun's full range (respects fog/cloak/LOS/on-screen).
                var gun = ch.GetGun();
                float gunRange = 0f;
                try { gunRange = gun != null ? gun.BulletDistance : 0f; } catch { gunRange = 0f; }
                float cap = cfg.Scope.MaxTargetDistanceMeters > 0f
                    ? cfg.Scope.MaxTargetDistanceMeters
                    : (gunRange > 0f ? gunRange : ch.ViewDistance);
                float halfAngle = ch.ViewAngle * 0.5f;
                float senseRange = ch.SenseRange;

                // Respect the tier's wall policy, exactly like AdsLock did before AIM-4
                // (aacfg.TargetThroughWalls). Hardcoding LOS here was the regression: on Cheat
                // (TargetThroughWalls=true) the old ADS lock saw through walls and found targets,
                // but ScopeAim forced the LOS SphereCast → every candidate rejected (los=N, ok=0).
                int count = ViewConeQuery.FindEnemiesInCone(
                    im, playerPos, coneAxis, halfAngle, cap, senseRange,
                    ch.Team, cfg.AutoAim.TargetThroughWalls, cam, AutoAim.OnScreenMarginFrac, _candidates);

                // Nearest candidate to the reticle (screen space). Lead each body center by the
                // bullet flight time so the settle point sits ahead of a strafing target.
                var scopeGun = ch.GetGun();
                int best = -1;
                float bestD = float.MaxValue;
                Vector2 bestScreen = Vector2.zero;
                for (int i = 0; i < count; i++)
                {
                    var leadBody = TargetLead.Compute(
                        _candidates[i].BodyCenter,
                        _candidates[i].MainControl != null ? _candidates[i].MainControl.Velocity : Vector3.zero,
                        playerPos,
                        TargetLead.EffectiveBulletSpeed(scopeGun, ch),
                        AutoAim.RecoilCfg);
                    Vector3 sp = cam.WorldToScreenPoint(leadBody);
                    if (sp.z <= 0f || float.IsNaN(sp.x) || float.IsNaN(sp.y)) continue;
                    float d = ((Vector2)sp - reticle).magnitude;
                    if (d < bestD) { bestD = d; best = i; bestScreen = new Vector2(sp.x, sp.y); }
                }

                bool soft = cfg.Scope.SoftAssistEnabled && cfg.Aim.BaselineAssistEnabled;

                // Escape is DIRECTIONAL, not magnitude-based. The assist yields ONLY when the stick
                // is actively pushing AWAY from the target (past EscapeStickMag AND beyond the
                // override angle) — so you can break off / switch targets. Holding the stick TOWARD
                // a target keeps the assist engaged, which is exactly when slowdown (dwell) and
                // settle should help. (Root cause of "no assist while aiming on Cheat": the old
                // binary `stickMag >= EscapeStickMag` gate killed ALL assist whenever the stick was
                // pushed at all — including straight at the target — and so also made the slowdown
                // dead code, since it could only apply when the stick was already released.)
                // AIM-1: while firing, raise the escape bar so RS counters recoil rather than
                // breaking the soft assist; a deliberate bigger push still escapes.
                float escapeMag = cfg.Scope.EscapeStickMag;
                if (pad.rightTrigger.isPressed && AutoAim.RecoilCfg != null)
                    escapeMag *= AutoAim.RecoilCfg.FireEscapeMultiplier;

                float awayAngle = -1f;
                bool pushingAway = false;
                if (best >= 0 && stickWorldDir != Vector3.zero && stickMag >= escapeMag)
                {
                    Vector3 toTarget = _candidates[best].BodyCenter - playerPos; toTarget.y = 0f;
                    if (toTarget.sqrMagnitude > 1e-6f)
                    {
                        awayAngle = Vector3.Angle(stickWorldDir, toTarget.normalized);
                        if (awayAngle > cfg.AutoAim.OverrideAngleDegrees) pushingAway = true;
                    }
                }
                bool assistTarget = soft && best >= 0 && !pushingAway;

                // Throttled decision snapshot (~2 Hz; DebugLog only) so the next on-device test
                // shows WHY the assist did/didn't engage (candidate count, distance, escape state).
                if (cfg.Diagnostics.DebugLog && Time.frameCount - _lastDetailFrame >= 30)
                {
                    _lastDetailFrame = Time.frameCount;
                    Log.Debug_($"[scope] count={count} best={best} "
                               + $"bestD={(best >= 0 ? bestD : -1f):0} stickMag={stickMag:0.00} "
                               + $"away={awayAngle:0} pushAway={pushingAway} assist={assistTarget} "
                               + $"soft={soft} settleR={cfg.Scope.SettleRadiusPx:0} "
                               + $"slowR={cfg.Scope.SlowdownRadiusPx:0} esc={cfg.Scope.EscapeStickMag:0.00}");
                    var st = ViewConeQuery.LastStats;
                    Log.Debug_($"[scope.q] hits={st.OverlapHits} noRecv={st.NoReceiver} noCtl={st.NoControl} "
                               + $"hidden={st.HiddenSkip} cloak={st.CloakSkip} notEnemy={st.NotEnemy} "
                               + $"dead={st.DeadSkip} range={st.RangeSkip} los={st.LosBlocked} off={st.OffScreen} "
                               + $"ok={st.Accepted} | cap={cap:0} sense={senseRange:0} halfAng={halfAngle:0} "
                               + $"coneAxis=({coneAxis.x:0.0},{coneAxis.z:0.0}) tw={cfg.AutoAim.TargetThroughWalls}");
                }

                // Free-look velocity (slowed near a target).
                Vector2 vel = stick * cfg.Scope.FreelookSpeedPxPerSec * Time.deltaTime;
                if (assistTarget && bestD <= cfg.Scope.SlowdownRadiusPx)
                    vel *= cfg.Scope.SlowdownFactor;

                Vector2 next = reticle + vel;

                // Settle: gently ease onto the target body-center (never a snap). Escapable.
                if (assistTarget && bestD <= cfg.Scope.SettleRadiusPx)
                {
                    int tid = _candidates[best].Transform.GetInstanceID();
                    if (tid != _settleTargetId) { _settleTargetId = tid; _settleVel = Vector2.zero; }
                    next = Vector2.SmoothDamp(next, bestScreen, ref _settleVel,
                                              Mathf.Max(0.01f, cfg.Scope.SettleTauSeconds));
                    Trace(cfg, "settle");
                }
                else
                {
                    _settleTargetId = 0;
                    _settleVel = Vector2.zero;
                    Trace(cfg, (assistTarget && bestD <= cfg.Scope.SlowdownRadiusPx) ? "slow" : "freelook");
                }

                // Clamp to the viewport so the reticle can reach the edge (sustaining the camera
                // pan) without leaving the screen.
                next.x = Mathf.Clamp(next.x, 0f, Screen.width);
                next.y = Mathf.Clamp(next.y, 0f, Screen.height);

                RadialCursor.WriteAbsoluteRaw(im, next);
                if (Mouse.current != null) Mouse.current.WarpCursorPosition(next);
            }
            catch { /* AimDriverPatch's rate-limited catch surfaces persistent failures */ }
        }

        // Raycast a screen point onto the horizontal plane through the player (mirrors
        // GameCamera.ScreenPointToCharacterPlane). Returns playerPos if the ray misses.
        private static Vector3 ScreenToGroundPlane(Camera cam, Vector2 screen, Vector3 playerPos)
        {
            var plane = new Plane(Vector3.up, playerPos + Vector3.up * 0.5f);
            Ray ray = cam.ScreenPointToRay(screen);
            if (plane.Raycast(ray, out float enter)) return ray.GetPoint(enter);
            return playerPos;
        }
    }
}
