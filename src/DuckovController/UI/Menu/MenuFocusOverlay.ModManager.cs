using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuckovController.UI.Menu
{
    // ModManagerUI reflection and reorder handling.
    internal sealed partial class MenuFocusOverlay : MonoBehaviour
    {
        private static System.Type? GetModEntryType()
        {
            if (_modEntryType != null) return _modEntryType;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("Duckov.Modding.UI.ModEntry", throwOnError: false);
                if (t != null) { _modEntryType = t; return _modEntryType; }
            }
            return null;
        }

        // Walks parents of `t` for a ModEntry component (invoked via reflection).
        private static Component? FindModEntryFor(Transform? t)
        {
            var type = GetModEntryType();
            if (type == null) return null;
            while (t != null)
            {
                var c = t.GetComponent(type) as Component;
                if (c != null) return c;
                t = t.parent;
            }
            return null;
        }

        // One-shot dump of On*/Toggle*/Set*/Click*/Interact* no-arg methods on ModEntry type.
        private static void LogModEntryMethodsOnce(Component entry)
        {
            if (_modEntryMethodsLogged || entry == null) return;
            _modEntryMethodsLogged = true;
            try
            {
                var t = entry.GetType();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var sb = new System.Text.StringBuilder();
                sb.Append("MenuOverlay ModEntry methods on ").Append(t.FullName).Append(": ");
                bool first = true;
                foreach (var m in t.GetMethods(flags))
                {
                    var n = m.Name;
                    if (!(n.StartsWith("On") || n.StartsWith("Toggle") || n.StartsWith("Set") || n.Contains("Click") || n.Contains("Interact"))) continue;
                    if (m.GetParameters().Length != 0) continue;
                    if (!first) sb.Append(", ");
                    first = false;
                    sb.Append(n);
                }
                Log.Info(sb.ToString());
            }
            catch (Exception e) { Log.Warn($"LogModEntryMethodsOnce: {e.Message}"); }
        }

        // Tries each candidate no-arg method name in order; returns true on first success.
        // Multiple candidates because game method naming drifts between builds.
        private static bool TryInvokeNoArgs(Component target, params string[] candidates)
        {
            if (target == null) return false;
            var type = target.GetType();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            foreach (var name in candidates)
            {
                MethodInfo? m;
                try { m = type.GetMethod(name, flags, null, System.Type.EmptyTypes, null); }
                catch { m = null; }
                if (m == null) continue;
                try { m.Invoke(target, null); return true; }
                catch (Exception e) { Log.Warn($"MenuOverlay: invoke {name} failed: {e.Message}"); }
            }
            return false;
        }

        // LB/LT = reorder up, RB/RT = reorder down. Consumes shoulder press from HandleNav.
        private bool HandleModReorder(Gamepad pad)
        {
            if (!IsInsideModManager() || _focused == null) return false;
            bool upEdge   = pad.leftShoulder.wasPressedThisFrame  || pad.leftTrigger.wasPressedThisFrame;
            bool downEdge = pad.rightShoulder.wasPressedThisFrame || pad.rightTrigger.wasPressedThisFrame;
            if (!upEdge && !downEdge) return false;

            var entry = FindModEntryFor(_focused.transform);
            if (entry == null) return upEdge || downEdge; // still consume

            var preCol = BuildModManagerColumn();
            int oldIdx = preCol.FindIndex(s => ReferenceEquals(s, _focused));
            // Mod rows: [0, Count-2]; Return is the last entry.
            int mods = Mathf.Max(0, preCol.Count - 1);

            int delta = 0;
            if (upEdge   && TryInvokeNoArgs(entry, "OnButtonReorderUpClicked",   "OnReorderUpClicked"))
            { delta = -1; Log.Info($"MenuOverlay: reorder UP on {entry.gameObject.name} (idx {oldIdx})."); }
            if (downEdge && TryInvokeNoArgs(entry, "OnButtonReorderDownClicked", "OnReorderDownClicked"))
            { delta = +1; Log.Info($"MenuOverlay: reorder DOWN on {entry.gameObject.name} (idx {oldIdx})."); }

            if (delta != 0 && oldIdx >= 0)
            {
                int target = Mathf.Clamp(oldIdx + delta, 0, Mathf.Max(0, mods - 1));
                _modRefocusTargetIdx = target;
                _modRefocusFramesLeft = 6;
                // Apply immediately (in-frame layout pass may have already run); repeats cover deferred case.
                ApplyModRefocus();
            }
            return true;
        }

        // Re-pins _focused to target column index each frame until layout settles.
        private void ApplyModRefocus()
        {
            if (_modRefocusTargetIdx < 0) return;
            var col = BuildModManagerColumn();
            if (_modRefocusTargetIdx < col.Count)
                _focused = col[_modRefocusTargetIdx];
        }
    }
}
