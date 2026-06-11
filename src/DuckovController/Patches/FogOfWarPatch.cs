using System.Reflection;
using DuckovController.Aim;
using HarmonyLib;
using UnityEngine;

namespace DuckovController.Patches
{
    // Overrides the FOW reveal cone to follow the stick instead of the auto-aim cursor.
    // Runs after FogOfWarManager.Update; overwrites mainVis.transform.rotation when AutoAim + decouple are on.
    // mainVis is FogOfWarRevealer3D (FOW assembly, not referenced); accessed as Component via reflection.
    [HarmonyPatch(typeof(FogOfWarManager), "Update")]
    internal static class FogOfWarPatch
    {
        private static FieldInfo? _mainVisField;
        private static bool _resolved;
        private static bool _resolveFailed;

        private static void ResolveReflection()
        {
            if (_resolved) return;
            _resolved = true;
            _mainVisField = typeof(FogOfWarManager).GetField("mainVis",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (_mainVisField == null)
            {
                _resolveFailed = true;
                Log.Warn("FogOfWarPatch: mainVis field not found via reflection; "
                         + "view-cone decouple disabled this session.");
            }
        }

        [HarmonyPostfix]
        internal static void Postfix(FogOfWarManager __instance)
        {
            try
            {
                var cfg = AimDriverPatch.Cfg;
                if (cfg == null) return;
                if (!cfg.AutoAim.Enabled) return;
                if (!cfg.AutoAim.DecoupleViewFromAim) return;
                if (!ViewDirectionDriver.HasDirection) return;
                if (__instance == null) return;

                ResolveReflection();
                if (_resolveFailed) return;

                var mainVis = _mainVisField!.GetValue(__instance) as Component;
                if (mainVis == null) return;

                var dir = ViewDirectionDriver.CurrentDirection;
                if (dir.sqrMagnitude < 1e-6f) return;

                mainVis.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }
            catch
            {
                // Silent — vanilla rotation was already applied; failing here
                // just means we don't override this frame.
            }
        }
    }
}
