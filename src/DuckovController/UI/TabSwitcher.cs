using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace DuckovController.UI
{
    // RB/LB tab cycling via ISingleSelectionMenu<TButton> reflection (OptionsPanel, QuestView, CraftView, etc.).
    // Finds buttons list (List<T> private field), resolves current via GetSelection(), calls SetSelection(buttons[i]).
    internal static partial class TabSwitcher
    {
        // Reflection shape cache per concrete type.
        private static readonly Dictionary<Type, ResolvedShape> _cache = new();

        private struct ResolvedShape
        {
            public MethodInfo? GetSelection;
            public MethodInfo? SetSelection;
            public FieldInfo? ButtonsField;       // List<T>-typed list
            public Type? ButtonType;
            public bool Valid => GetSelection != null && SetSelection != null
                                 && ButtonsField != null && ButtonType != null;
        }

        private static readonly Dictionary<Type, bool> _implementsCache = new();

        // Scene-invalidated ViewTabDisplayEntry cache (replaces per-call FindObjectsOfType).
        private static List<ViewTabDisplayEntry>? _entryCache;
        private static bool _entryCacheDirty = true;
        private static bool _sceneLoadedHooked;

        internal static void EnsureSceneHook()
        {
            if (_sceneLoadedHooked) return;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, _) => _entryCacheDirty = true;
            _sceneLoadedHooked = true;
        }

        internal static List<ViewTabDisplayEntry> GetEntries()
        {
            EnsureSceneHook();
            if (_entryCacheDirty || _entryCache == null)
            {
                var arr = UnityEngine.Object.FindObjectsOfType<ViewTabDisplayEntry>(true);
                _entryCache = new List<ViewTabDisplayEntry>(arr);
                _entryCacheDirty = false;
            }
            // prune stale (destroyed) entries inline
            _entryCache.RemoveAll(e => e == null);
            return _entryCache;
        }

        // Scene-wide fallback: used when View.ActiveView is null (PauseMenu/OptionsPanel are UIPanels, not Views).
        internal static bool TryCycleSceneWide(int direction)
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root == null || !root.activeInHierarchy) continue;
                    if (TryCycle(root, direction)) return true;
                }
            }
            return false;
        }

        internal static bool TryCycle(GameObject scopeRoot, int direction)
        {
            if (scopeRoot == null) return false;
            // ViewTabs (top bar) takes absolute priority — never fall through to other strategies
            // (else a non-interactable tab could accidentally delegate to QuestView's sort tabs).
            if (HasActiveViewTabs())
            {
                LogViewTabsDiagnostic();
                TryCycleViewTabs(direction);
                return true;
            }
            if (TryCycleSelectionMenu(scopeRoot, direction)) return true;
            if (TryCycleToggleGroup(scopeRoot, direction)) return true;
            // Final fallback: largest horizontal Button row.
            if (TryStepHorizontalRow(scopeRoot, direction)) return true;
            return false;
        }

        private static bool HasActiveViewTabs()
        {
            // ViewTabs shows only when View.ActiveView is non-null; require ≥2 active entries.
            if (Duckov.UI.View.ActiveView == null) return false;
            var entries = GetEntries();
            int active = 0;
            for (int i = 0; entries != null && i < entries.Count; i++)
            {
                if (entries[i] != null && entries[i].isActiveAndEnabled && entries[i].gameObject.activeInHierarchy)
                    active++;
            }
            return active >= 2;
        }

        private static bool CycleOne(MonoBehaviour menu, int direction)
        {
            var shape = ResolveShape(menu.GetType());
            if (!shape.Valid) return false;
            var buttons = shape.ButtonsField!.GetValue(menu) as IList;
            if (buttons == null || buttons.Count < 2) return false;

            var current = shape.GetSelection!.Invoke(menu, null);
            int idx = -1;
            for (int i = 0; i < buttons.Count; i++)
            {
                if (object.ReferenceEquals(buttons[i], current)) { idx = i; break; }
            }
            if (idx < 0) idx = 0;
            int next = idx + direction;
            if (next < 0) next = buttons.Count - 1;
            else if (next >= buttons.Count) next = 0;

            var nextBtn = buttons[next];
            if (nextBtn == null) return false;
            try
            {
                shape.SetSelection!.Invoke(menu, new[] { nextBtn });
                // Also fire pointerEnter on the new tab's GameObject so any
                // hover visual updates, mirroring our menu nav semantics.
                if (nextBtn is MonoBehaviour mb && mb != null)
                {
                    var prevMb = current as MonoBehaviour;
                    PointerEventDispatcher.Hover(prevMb?.gameObject, mb.gameObject);
                }
                return true;
            }
            catch (Exception e)
            {
                Log.Debug_("TabSwitcher SetSelection threw: " + e.Message);
                return false;
            }
        }

        private static List<MonoBehaviour> FindActiveSelectionMenusUnder(GameObject root)
        {
            var result = new List<MonoBehaviour>();
            var all = root.GetComponentsInChildren<MonoBehaviour>(includeInactive: false);
            for (int i = 0; i < all.Length; i++)
            {
                var c = all[i];
                if (c == null || !c.isActiveAndEnabled) continue;
                if (ImplementsSelectionMenu(c.GetType()))
                    result.Add(c);
            }
            return result;
        }

        private static bool ImplementsSelectionMenu(Type t)
        {
            if (_implementsCache.TryGetValue(t, out bool cached)) return cached;
            bool found = false;
            foreach (var iface in t.GetInterfaces())
            {
                if (iface.IsGenericType &&
                    iface.GetGenericTypeDefinition().Name.StartsWith("ISingleSelectionMenu"))
                {
                    found = true; break;
                }
            }
            _implementsCache[t] = found;
            return found;
        }

        private static ResolvedShape ResolveShape(Type t)
        {
            if (_cache.TryGetValue(t, out var cached)) return cached;
            var shape = default(ResolvedShape);

            // ISingleSelectionMenu<TButton>: GetSelection(), SetSelection(TButton), List<TButton> field.
            Type? buttonType = null;
            foreach (var iface in t.GetInterfaces())
            {
                if (iface.IsGenericType &&
                    iface.GetGenericTypeDefinition().Name.StartsWith("ISingleSelectionMenu"))
                {
                    buttonType = iface.GetGenericArguments()[0];
                    break;
                }
            }
            if (buttonType != null)
            {
                shape.ButtonType = buttonType;
                shape.GetSelection = t.GetMethod("GetSelection", BindingFlags.Instance | BindingFlags.Public);
                shape.SetSelection = t.GetMethod("SetSelection", BindingFlags.Instance | BindingFlags.Public, null, new[] { buttonType }, null);
                var listType = typeof(List<>).MakeGenericType(buttonType);
                var fields = t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                foreach (var f in fields)
                {
                    if (f.FieldType == listType) { shape.ButtonsField = f; break; }
                }
            }
            _cache[t] = shape;
            return shape;
        }
    }
}
