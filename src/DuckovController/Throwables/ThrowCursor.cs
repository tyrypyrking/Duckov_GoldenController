using DuckovController.Aim;
using DuckovController.Config;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuckovController.Throwables
{
    // Variable-radius throw reticle: maps stick (dir + magnitude) to a ground point within
    // castRange, projects to screen, and writes AimMousePosition. The game then projects that
    // back to inputAimPoint and clamps to castRange in GetCurrentSkillAimPoint().
    internal static class ThrowCursor
    {
        // Direction-only seed on aim-entry: place the reticle ahead of the player at a default
        // fraction of castRange so there's always a sensible starting point.
        internal static void Seed(InputManager im, CharacterMainControl ch, Camera? cam,
                                  float castRange, ThrowConfig cfg)
        {
            if (cam == null || ch == null || castRange <= 0f) return;
            Vector3 dir = ch.CurrentAimDirection; dir.y = 0f;
            if (dir.sqrMagnitude < 1e-6f) dir = ch.transform.forward;
            dir.Normalize();
            PlaceGround(im, cam, ch.transform.position + dir * (cfg.IdleSeedFactor * castRange));
        }

        // Stick-driven free pan. Idle (centered) holds the last reticle (no rewrite).
        internal static void DriveFreePan(InputManager im, CharacterMainControl ch, Camera? cam,
                                          Vector2 stick, float castRange, ThrowConfig cfg)
        {
            if (cam == null || ch == null || castRange <= 0f) return;
            float mag = stick.magnitude;
            if (mag < 1e-4f) return;                       // hold last reticle

            var camRight = cam.transform.right; camRight.y = 0f; camRight.Normalize();
            var camFwd   = cam.transform.forward; camFwd.y = 0f; camFwd.Normalize();
            Vector3 dir = camRight * stick.x + camFwd * stick.y;
            if (dir.sqrMagnitude < 1e-6f) return;
            dir.Normalize();

            float dist = cfg.DistanceFraction(mag) * castRange;
            PlaceGround(im, cam, ch.transform.position + dir * dist);
        }

        private static void PlaceGround(InputManager im, Camera cam, Vector3 ground)
        {
            var sp = cam.WorldToScreenPoint(ground);
            if (sp.z <= 0f || float.IsNaN(sp.x) || float.IsNaN(sp.y)) return;
            var screen = new Vector2(sp.x, sp.y);
            RadialCursor.WriteAbsoluteRaw(im, screen);
            im.SetMousePosition(screen);    // drives GameCamera.UpdateAimOffsetNormal look-ahead pan
            if (Mouse.current != null) Mouse.current.WarpCursorPosition(screen);
        }

        // Used by the aim-assist path to write a pre-computed screen point.
        internal static void WriteScreen(InputManager im, Vector2 screen)
        {
            RadialCursor.WriteAbsoluteRaw(im, screen);
            im.SetMousePosition(screen);    // drives GameCamera.UpdateAimOffsetNormal look-ahead pan
            if (Mouse.current != null) Mouse.current.WarpCursorPosition(screen);
        }
    }
}
