using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace DuckovController.UI
{
    internal static partial class TabSwitcher
    {
        private static float _lastLogTime;
        private static float _lastViewTabsDiagTime;

        // Dump active ISingleSelectionMenu<T> + ToggleGroups + active-view selectables. Rate-limited to 1.5s.
        internal static void LogAvailableMenus()
        {
            if (Time.unscaledTime - _lastLogTime < 1.5f) return;
            _lastLogTime = Time.unscaledTime;
            var lines = new List<string>();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root == null || !root.activeInHierarchy) continue;
                    var menus = FindActiveSelectionMenusUnder(root);
                    foreach (var m in menus)
                    {
                        if (m == null) continue;
                        var shape = ResolveShape(m.GetType());
                        var count = "?";
                        if (shape.Valid)
                        {
                            var list = shape.ButtonsField!.GetValue(m) as System.Collections.IList;
                            count = list == null ? "null" : list.Count.ToString();
                        }
                        lines.Add($"  {m.GetType().FullName} on '{m.gameObject.name}' (scene={scene.name}, valid={shape.Valid}, buttons={count})");
                    }
                }
            }
            var toggleGroupLines = new List<string>();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root == null || !root.activeInHierarchy) continue;
                    var groups = root.GetComponentsInChildren<ToggleGroup>(includeInactive: false);
                    foreach (var g in groups)
                    {
                        if (g == null || !g.isActiveAndEnabled) continue;
                        var togs = g.GetComponentsInChildren<Toggle>(includeInactive: false)
                            .Where(t => t != null && t.isActiveAndEnabled && t.group == g).ToArray();
                        toggleGroupLines.Add($"  ToggleGroup on '{g.gameObject.name}' (scene={scene.name}, toggles={togs.Length})");
                    }
                }
            }

            var viewChildLines = new List<string>();
            var active = Duckov.UI.View.ActiveView;
            if (active != null)
            {
                var selectables = active.gameObject.GetComponentsInChildren<Selectable>(includeInactive: false);
                foreach (var s in selectables)
                {
                    if (s == null || !s.isActiveAndEnabled || !s.IsInteractable()) continue;
                    viewChildLines.Add($"  {s.GetType().Name} on '{s.gameObject.name}' y={PointerEventDispatcher.ScreenCenterOf(s.gameObject).y:F0}");
                }
            }

            var msg = $"TabSwitcher diagnostic:\n"
                + $" ISingleSelectionMenu<>: {lines.Count}\n"
                + (lines.Count > 0 ? string.Join("\n", lines) + "\n" : "")
                + $" ToggleGroups: {toggleGroupLines.Count}\n"
                + (toggleGroupLines.Count > 0 ? string.Join("\n", toggleGroupLines) + "\n" : "")
                + $" Active view selectables: {viewChildLines.Count} (view={active?.GetType().Name ?? "null"})\n"
                + (viewChildLines.Count > 0 ? string.Join("\n", viewChildLines.Take(30)) : "");
            Log.Info(msg);
        }

        private static void LogViewTabsDiagnostic()
        {
            if (Time.unscaledTime - _lastViewTabsDiagTime < 1.5f) return;
            _lastViewTabsDiagTime = Time.unscaledTime;

            if (_viewTypeNameField == null)
                _viewTypeNameField = typeof(ViewTabDisplayEntry).GetField(
                    "viewTypeName", BindingFlags.Instance | BindingFlags.NonPublic);

            var entries = GetEntries();
            var lines = new List<string>();
            for (int i = 0; entries != null && i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null) continue;
                var name = _viewTypeNameField?.GetValue(e) as string ?? "?";
                var center = PointerEventDispatcher.ScreenCenterOf(e.gameObject);
                var btn = FindTabButton(e.gameObject);
                lines.Add(
                    $"  entry name='{e.gameObject.name}' viewType='{name}' active={e.gameObject.activeInHierarchy} enabled={e.isActiveAndEnabled}"
                    + $" pos=({center.x:F0},{center.y:F0}) parent='{e.transform.parent?.name ?? "<root>"}'"
                    + $" btn={(btn == null ? "null" : btn.gameObject.name)}"
                    + $" btnInteractable={(btn != null && btn.IsInteractable())}");
            }
            Log.Info($"ViewTabs diagnostic: {entries?.Count ?? 0} ViewTabDisplayEntry instance(s) (including inactive):\n" + string.Join("\n", lines));
        }
    }
}
