using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using DuckovController.Config;
using Duckov.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DuckovController.Diagnostics
{
    // Hotkey (default F8 held 1 s) dumps View.ActiveView transform depth-first to
    // Application.persistentDataPath/<OutputSubdir>/<ViewType>_<UTC>.txt.
    // Also reflects [SerializeField] fields so child Transforms can be mapped to field names.
    internal sealed class UIStructureDumper : MonoBehaviour
    {
        internal DiagnosticsConfig? Cfg;

        private float _holdStart = -1f;
        private bool _firedThisHold;
        private static readonly HashSet<string> _warnedUnknownButtons = new();

        private void Update()
        {
            if (Cfg == null || !Cfg.UIDumperEnabled) return;
            // Keyboard hotkey avoids gamepad binding collisions. Bind Deck R5 → this key in Steam Input.
            bool held = KeyHeld(Cfg.HotkeyKey);
            if (!held) { _holdStart = -1f; _firedThisHold = false; return; }

            if (_holdStart < 0f) { _holdStart = Time.unscaledTime; _firedThisHold = false; }
            if (_firedThisHold) return;

            if (Time.unscaledTime - _holdStart >= Cfg.HotkeyHoldSec)
            {
                _firedThisHold = true;
                try { Dump(); }
                catch (Exception e) { Log.Error($"UIStructureDumper.Dump failed: {e}"); }
            }
        }

        private static bool KeyHeld(string name)
        {
            var kb = Keyboard.current;
            if (kb == null)
            {
                if (_warnedUnknownButtons.Add("<no-keyboard>"))
                    Log.Warn("UIStructureDumper: no Keyboard.current detected.");
                return false;
            }
            // Resolve by Unity InputSystem key name (case-insensitive).
            if (!Enum.TryParse<Key>(name, ignoreCase: true, out var keyEnum))
            {
                if (_warnedUnknownButtons.Add(name))
                    Log.Warn($"UIStructureDumper: unknown keyboard key \"{name}\" — hotkey cannot fire.");
                return false;
            }
            try { return kb[keyEnum].isPressed; }
            catch { return false; }
        }

        private void Dump()
        {
            // Preferred target: active View. Null for non-View panels (PauseMenu, main-menu Settings).
            var view = View.ActiveView;
            if (view != null)
            {
                WriteDumpFor(view.GetType().FullName, view.GetType().Name, view, view.transform);
                return;
            }

            // Fallback 1: PauseMenu is a UIPanel (not a View).
            var pause = TryGetShownPauseMenu();
            if (pause != null)
            {
                WriteDumpFor(pause.GetType().FullName, "PauseMenu", pause, pause.transform);
                return;
            }

            // Fallback 1b: GamingConsoleHUD is neither a View nor UIPanel; F8 captures its structure while a console is up.
            var consoleHud = UnityEngine.Object.FindObjectOfType<Duckov.MiniGames.GamingConsoleHUD>();
            if (consoleHud != null && consoleHud.gameObject.activeInHierarchy)
            {
                WriteDumpFor(consoleHud.GetType().FullName, "GamingConsoleHUD", consoleHud, consoleHud.transform);
                return;
            }

            // Fallback 1c: gameplay HUD prompts (TaskSkipperUI, InteractSelectionHUD, InteractHUD).
            // Skip checked first (rarer state).
            var skipper = UnityEngine.Object.FindObjectOfType<TaskSkipperUI>();
            if (skipper != null && skipper.gameObject.activeInHierarchy)
            {
                WriteDumpFor(skipper.GetType().FullName, "TaskSkipperUI", skipper, skipper.transform);
                return;
            }
            var interactSel = UnityEngine.Object.FindObjectOfType<InteractSelectionHUD>();
            if (interactSel != null && interactSel.gameObject.activeInHierarchy)
            {
                WriteDumpFor(interactSel.GetType().FullName, "InteractSelectionHUD", interactSel, interactSel.transform);
                return;
            }
            var interactHud = UnityEngine.Object.FindObjectOfType<InteractHUD>();
            if (interactHud != null && interactHud.gameObject.activeInHierarchy)
            {
                WriteDumpFor(interactHud.GetType().FullName, "InteractHUD", interactHud, interactHud.transform);
                return;
            }

            // Fallback 2: MainMenu (separate scene). Actual menu on a separate Canvas; find most-populated root Canvas.
            var mainMenu = TryGetMainMenu();
            if (mainMenu != null)
            {
                var canvas = FindMostPopulatedMenuCanvas();
                if (canvas != null)
                {
                    WriteDumpFor(canvas.name, "MainMenu", mainMenu, canvas);
                    return;
                }
                // Fallback: dump MainMenu transform itself so the attempt is recorded.
                WriteDumpFor(mainMenu.GetType().FullName, "MainMenu", mainMenu, mainMenu.transform);
                return;
            }

            // Fallback 3: dump most-populated active root Canvas, labeled by scene name.
            var generic = FindMostPopulatedMenuCanvas();
            if (generic != null)
            {
                var sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                string label = string.IsNullOrEmpty(sceneName) ? "Screen" : sceneName;
                WriteDumpFor(generic.name, label, this, generic);
                Log.Info($"UIStructureDumper: dumped generic canvas '{generic.name}' for scene '{label}'.");
                return;
            }

            Log.Info("UIStructureDumper: no active View, PauseMenu, or MainMenu — nothing to dump.");
        }

        private static MonoBehaviour? TryGetMainMenu()
        {
            try
            {
                var t = Type.GetType("MainMenu, TeamSoda.Duckov.Core");
                if (t == null) return null;
                return UnityEngine.Object.FindObjectOfType(t) as MonoBehaviour;
            }
            catch { return null; }
        }

        private static Transform? FindMostPopulatedMenuCanvas()
        {
            Canvas[]? canvases;
            try { canvases = UnityEngine.Object.FindObjectsOfType<Canvas>(); }
            catch { return null; }
            if (canvases == null) return null;

            Canvas? best = null;
            int bestCount = 0;
            foreach (var c in canvases)
            {
                if (c == null || !c.isRootCanvas || !c.gameObject.activeInHierarchy) continue;
                int n = 0;
                foreach (var b in c.GetComponentsInChildren<UnityEngine.UI.Button>(includeInactive: false))
                    if (b != null && b.interactable && b.gameObject.activeInHierarchy) n++;
                if (n > bestCount) { bestCount = n; best = c; }
            }
            return best != null ? best.transform : null;
        }

        // nameLabel = filename stem; target = MonoBehaviour whose [SerializeField] fields are reflected; root = tree root.
        private void WriteDumpFor(string? fullTypeName, string nameLabel, MonoBehaviour target, Transform root)
        {
            var sb = new StringBuilder(16 * 1024);
            sb.AppendLine($"# UI dump for {fullTypeName ?? nameLabel}");
            sb.AppendLine($"# UTC: {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}");
            sb.AppendLine($"# Active scene: {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
            sb.AppendLine();

            WriteReflectedSerializedFields(sb, target);
            sb.AppendLine();
            sb.AppendLine("## Hierarchy");
            WriteTransform(sb, root, 0, root);

            string subdir = Cfg?.OutputSubdir ?? "ControllerDumps";
            string dir = Path.Combine(Application.persistentDataPath, subdir);
            Directory.CreateDirectory(dir);
            string fname = $"{nameLabel}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.txt";
            string fullPath = Path.Combine(dir, fname);

            File.WriteAllText(fullPath, sb.ToString(), Encoding.UTF8);

            int bytes = sb.Length;
            if (Cfg != null && bytes > Cfg.SoftCapBytes)
                Log.Warn($"UIStructureDumper: dump exceeded soft cap ({bytes} > {Cfg.SoftCapBytes} bytes) — {fullPath}");
            else
                Log.Info($"UIStructureDumper: wrote {bytes} bytes → {fullPath}");
        }

        // Soft-typed reflection probe so a missing PauseMenu type doesn't break the dumper at load time.
        private static MonoBehaviour? TryGetShownPauseMenu()
        {
            try
            {
                var t = Type.GetType("PauseMenu, TeamSoda.Duckov.Core");
                if (t == null) return null;
                var instanceProp = t.GetProperty("Instance",
                    BindingFlags.Public | BindingFlags.Static);
                var inst = instanceProp?.GetValue(null) as MonoBehaviour;
                if (inst == null) return null;
                var shownProp = t.GetProperty("Shown",
                    BindingFlags.Public | BindingFlags.Instance);
                if (shownProp?.GetValue(inst) is bool shown && shown) return inst;
                return null;
            }
            catch { return null; }
        }

        private static void WriteReflectedSerializedFields(StringBuilder sb, MonoBehaviour target)
        {
            sb.AppendLine("## Serialized fields");
            var t = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            // Walk up the hierarchy; stop before MonoBehaviour itself.
            for (var cursor = t; cursor != null && cursor != typeof(MonoBehaviour) && cursor != typeof(Behaviour) && cursor != typeof(Component) && cursor != typeof(UnityEngine.Object); cursor = cursor.BaseType)
            {
                foreach (var f in cursor.GetFields(flags | BindingFlags.DeclaredOnly))
                {
                    try
                    {
                        bool serialized = f.GetCustomAttribute<SerializeField>() != null || f.IsPublic;
                        if (!serialized) continue;
                        object? val = SafeGetValue(f, target);
                        string typeName = f.FieldType.Name;
                        string disp = DescribeFieldValue(val, target.transform);
                        sb.AppendLine($"- {cursor.Name}.{f.Name} : {typeName} = {disp}");
                    }
                    catch (Exception fieldEx)
                    {
                        sb.AppendLine($"- {cursor.Name}.{f.Name} : <error: {fieldEx.GetType().Name}>");
                    }
                }
            }
        }

        private static object? SafeGetValue(FieldInfo f, object target)
        {
            try { return f.GetValue(target); }
            catch { return "<error reading>"; }
        }

        private static string DescribeFieldValue(object? val, Transform viewRoot)
        {
            if (val == null) return "null";
            if (val is Component c && c != null)
            {
                string path = PathRelativeTo(c.transform, viewRoot);
                return $"Component@\"{path}\"";
            }
            if (val is GameObject go && go != null)
            {
                string path = PathRelativeTo(go.transform, viewRoot);
                return $"GameObject@\"{path}\"";
            }
            return val.ToString() ?? "<null tostring>";
        }

        private static string PathRelativeTo(Transform t, Transform root)
        {
            if (t == null) return "<null>";
            var parts = new List<string>();
            var cur = t;
            while (cur != null && cur != root)
            {
                parts.Add(cur.name);
                cur = cur.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        private static void WriteTransform(StringBuilder sb, Transform t, int depth, Transform root)
        {
            string indent = new string(' ', depth * 2);
            var rt = t as RectTransform;
            string rect = rt != null
                ? $"rect(anchored={V2(rt.anchoredPosition)} size={V2(rt.sizeDelta)} pivot={V2(rt.pivot)})"
                : "rect=<non-RectTransform>";

            sb.Append(indent).Append("- ").Append(t.name)
              .Append("  active=").Append(t.gameObject.activeInHierarchy)
              .Append("  sib=").Append(t.GetSiblingIndex())
              .Append("  ").Append(rect)
              .AppendLine();

            // Component list (filtered to interesting types).
            foreach (var line in DescribeComponents(t, root))
                sb.Append(indent).Append("    ").AppendLine(line);

            int n = t.childCount;
            for (int i = 0; i < n; i++)
                WriteTransform(sb, t.GetChild(i), depth + 1, root);
        }

        private static IEnumerable<string> DescribeComponents(Transform t, Transform root)
        {
            var comps = t.GetComponents<Component>();
            foreach (var c in comps)
            {
                if (c == null) { yield return "[missing component]"; continue; }

                string line;
                try
                {
                    // Log relevant types + any IPointer*/IDrag* implementors.
                    string n = c.GetType().Name;
                    bool interesting =
                        n == "InventoryDisplay" || n == "InventoryEntry"
                        || n == "ItemSlotCollectionDisplay" || n == "SlotDisplay"
                        || n == "InventoryFilterDisplay" || n == "InventoryFilterDisplayEntry"
                        || n == "ItemOperationMenu"
                        || c is ScrollRect || c is Button || c is Toggle || c is TMP_Dropdown
                        || c is IPointerClickHandler || c is IPointerEnterHandler || c is IDragHandler
                        || IsFadeGroup(c) || IsSingleSelectionMenu(c);

                    if (!interesting) continue;

                    string detail = "";

                    // Per-type detail via reflection (no hard dep on game-side classes).
                    if (n == "InventoryDisplay")     detail = DescribeInventoryDisplay(c);
                    else if (n == "InventoryFilterDisplay") detail = DescribeInventoryFilterDisplay(c);
                    else if (c is ScrollRect sr)     detail = $"vp_size={(sr.viewport != null ? V2(sr.viewport.rect.size) : "<no viewport>")} content_size={(sr.content != null ? V2(sr.content.rect.size) : "<no content>")} normY={sr.verticalNormalizedPosition:F3}";
                    else if (c is Button btn)        detail = $"interactable={btn.interactable} listeners={btn.onClick.GetPersistentEventCount()}";
                    else if (c is Toggle tg)         detail = $"on={tg.isOn} interactable={tg.interactable}";
                    else if (c is TMP_Dropdown dd)   detail = $"value={dd.value} options={dd.options.Count}";
                    else if (IsFadeGroup(c))         detail = DescribeFadeGroup(c);

                    line = string.IsNullOrEmpty(detail) ? $"<{n}>" : $"<{n}> {detail}";
                }
                catch (Exception compEx)
                {
                    line = $"[component-error: {compEx.GetType().Name}]";
                }
                yield return line;
            }
        }

        private static bool IsFadeGroup(Component c)
        {
            return c.GetType().FullName == "Duckov.UI.Animations.FadeGroup";
        }

        private static bool IsSingleSelectionMenu(Component c)
        {
            // ISingleSelectionMenu<> is generic; identify by interface name match.
            var ifaces = c.GetType().GetInterfaces();
            foreach (var i in ifaces)
            {
                if (i.IsGenericType && i.GetGenericTypeDefinition().Name.StartsWith("ISingleSelectionMenu"))
                    return true;
                if (i.Name == "ISingleSelectionMenu") return true;
            }
            return false;
        }

        private static string DescribeInventoryDisplay(Component c)
        {
            try
            {
                var t = c.GetType();
                var targetProp = t.GetProperty("Target") ?? t.GetProperty("target");
                var editableProp = t.GetProperty("Editable") ?? t.GetProperty("editable");
                var movableProp = t.GetProperty("Movable") ?? t.GetProperty("movable");
                var usePagesProp = t.GetProperty("UsePages") ?? t.GetProperty("usePages");

                object? inv = targetProp?.GetValue(c);
                string invDesc = "null";
                if (inv != null)
                {
                    var it = inv.GetType();
                    var cap = it.GetProperty("Capacity")?.GetValue(inv);
                    var dn = it.GetProperty("DisplayName")?.GetValue(inv);
                    invDesc = $"Capacity={cap} DisplayName=\"{dn}\"";
                }
                string ed = editableProp?.GetValue(c)?.ToString() ?? "?";
                string mv = movableProp?.GetValue(c)?.ToString() ?? "?";
                string up = usePagesProp?.GetValue(c)?.ToString() ?? "?";
                return $"target=({invDesc}) Editable={ed} Movable={mv} UsePages={up}";
            }
            catch (Exception e) { return $"<reflect-error: {e.GetType().Name}>"; }
        }

        private static string DescribeInventoryFilterDisplay(Component c)
        {
            try
            {
                var t = c.GetType();
                var entriesField = t.GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic);
                int n = 0;
                if (entriesField?.GetValue(c) is System.Collections.ICollection col) n = col.Count;
                var selProp = t.GetMethod("GetSelection");
                object? sel = selProp?.Invoke(c, null);
                return $"entries={n} selection={(sel == null ? "null" : "set")}";
            }
            catch (Exception e) { return $"<reflect-error: {e.GetType().Name}>"; }
        }

        private static string DescribeFadeGroup(Component c)
        {
            try
            {
                var t = c.GetType();
                var isShown = t.GetProperty("IsShown")?.GetValue(c);
                var inProg = t.GetProperty("IsShowingInProgress")?.GetValue(c);
                var alpha = t.GetProperty("Alpha")?.GetValue(c);
                return $"IsShown={isShown} InProgress={inProg} Alpha={alpha}";
            }
            catch (Exception e) { return $"<reflect-error: {e.GetType().Name}>"; }
        }

        private static string V2(Vector2 v) => $"({v.x:F1},{v.y:F1})";
    }
}
