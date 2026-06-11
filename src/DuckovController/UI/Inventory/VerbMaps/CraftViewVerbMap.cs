using System.Collections.Generic;
using System.Reflection;
using DuckovController.UI;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using Duckov.UI;
using UnityEngine;
using UnityEngine.UI;

namespace DuckovController.UI.Inventory.VerbMaps
{
    // CraftView (crafting bench) — a single-selection recipe list with section
    // tabs and a craft button. Controls:
    //   dpad/stick → move over recipe entries (CraftView_ListEntry; tabs + craft
    //                button + exit are excluded from dpad nav, see IsCraftChrome)
    //   A          → select the focused recipe (inherited: the entry is an
    //                IPointerClickHandler whose click calls Master.SetSelection)
    //   X          → craft the SELECTED recipe; if nothing is selected yet,
    //                quick-craft the FOCUSED recipe (select it then craft)
    //   RB / LB    → next / previous section tab (also LT/RT as a trigger alias)
    //   B          → exit (router-global View.Close)
    internal sealed class CraftViewVerbMap : DefaultViewVerbMap
    {
        public override string ViewTypeName => "CraftView";
        public override bool HorizontalPrompts => true;

        private static readonly PromptEntry[] _prompts = new[]
        {
            new PromptEntry(ButtonGlyph.DPad, "Move"),
            new PromptEntry(ButtonGlyph.A, "Select"),
            new PromptEntry(ButtonGlyph.X, "Craft"),
            // Triggers cycle the craft sections (CycleFilter); one combined row.
            new PromptEntry(ButtonGlyph.LT, ButtonGlyph.RT, "Pane"),
            // No "B Exit" row: the visible exit/back button already shows the B
            // glyph, so a hint-panel row would be redundant.
        };

        // A toggles selection. Deselect has no public API — mirror SetSelection's unselect half manually.
        public override bool TryA(GameObject? focus, InventoryVerbRouter router)
        {
            if (focus == null || router == null) return base.TryA(focus, router);
            var view = View.ActiveView;
            var entry = GetComponentNamed(focus, "CraftView_ListEntry");
            if (view == null || entry == null) return base.TryA(focus, router);

            var selected = GetSelection(view);
            if (selected != null && ReferenceEquals(selected, entry))
            {
                // Toggle off: clear selection + refresh the entry visual + details.
                SetField(view, "selectedEntry", null);
                InvokeVoid(entry, "NotifyUnselected");
                InvokeVoid(view, "RefreshDetails");
                return true;
            }
            // Select (default click → CraftView_ListEntry.OnPointerClick → SetSelection).
            return base.TryA(focus, router);
        }

        public override bool TryX(GameObject? focus, InventoryVerbRouter router)
        {
            if (router == null) return true;
            var view = View.ActiveView;
            if (view == null) return true;
            var craftBtn = ResolveButton(view, "craftButton");
            if (craftBtn == null) return true;

            // A recipe is explicitly selected → craft it (the normal path).
            if (GetSelection(view) != null)
            {
                craftBtn.onClick.Invoke();
                return true;
            }

            // Quick-craft focused recipe: set selectedEntry just long enough for CraftTask's sync read, then clear.
            // Don't call NotifySelected/RefreshDetails so nothing sticks visually.
            var entry = focus != null ? GetComponentNamed(focus, "CraftView_ListEntry") : null;
            if (entry == null) return true;
            SetField(view, "selectedEntry", entry);
            craftBtn.onClick.Invoke();
            SetField(view, "selectedEntry", null);
            return true;
        }

        // Section tabs on both shoulders and triggers (RB/RT next, LB/LT prev).
        public override bool TryRB(GameObject? focus, InventoryVerbRouter router) => CycleFilter(+1);
        public override bool TryLB(GameObject? focus, InventoryVerbRouter router) => CycleFilter(-1);
        public override bool TryRT(GameObject? focus, InventoryVerbRouter router) => CycleFilter(+1);
        public override bool TryLT(GameObject? focus, InventoryVerbRouter router) => CycleFilter(-1);

        public override IReadOnlyList<PromptEntry> PromptsFor(GameObject? focus, InventoryVerbRouter router) => _prompts;

        // X glyph on the craft button (resolved via the craftButton field).
        private static readonly (string, ButtonGlyph)[] _hints = { ("craftButton", ButtonGlyph.X) };
        public override IReadOnlyList<(string FieldName, ButtonGlyph Glyph)> ButtonGlyphHints() => _hints;

        // Click next/prev tab. SetFilter rebuilds list + clears selection; request refocus to re-land on first recipe.
        private bool CycleFilter(int dir)
        {
            var view = View.ActiveView;
            if (view == null) return true;

            var tabs = GetFilterTabs(view);
            if (tabs.Count <= 1) return true;

            int cur = ReadInt(view, "currentFilterIndex");
            int pos = tabs.FindIndex(t => t.Index == cur);
            if (pos < 0) pos = 0;
            int next = ((pos + dir) % tabs.Count + tabs.Count) % tabs.Count;

            PointerEventDispatcher.Click(tabs[next].Go);
            GridFocusController.Instance?.RequestPreferredRefocus(0.6f);
            return true;
        }

        // Active CraftViewFilterBtnEntry tabs sorted by private filter index (visual order, skips unpopulated sections).
        private static List<(GameObject Go, int Index)> GetFilterTabs(View view)
        {
            var result = new List<(GameObject, int)>();
            var comps = view.GetComponentsInChildren<Component>(includeInactive: false);
            foreach (var c in comps)
            {
                if (c == null || c.GetType().Name != "CraftViewFilterBtnEntry") continue;
                if (!c.gameObject.activeInHierarchy) continue;
                result.Add((c.gameObject, ReadInt(c, "index")));
            }
            result.Sort((a, b) => a.Item2.CompareTo(b.Item2));
            return result;
        }

        // reflection helpers
        private const BindingFlags Flags =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        // The view's current selectedEntry (CraftView_ListEntry) or null.
        private static object? GetSelection(View view)
        {
            var m = view.GetType().GetMethod("GetSelection", Flags);
            var sel = m?.Invoke(view, null);
            if (sel is Object uo) return uo != null ? sel : null; // Unity fake-null guard
            return sel;
        }

        private static Component? GetComponentNamed(GameObject go, string typeName)
        {
            foreach (var c in go.GetComponents<Component>())
                if (c != null && c.GetType().Name == typeName) return c;
            return null;
        }

        private static void SetField(object target, string fieldName, object? value)
        {
            if (target == null) return;
            ReflectionUtil.WalkField(target.GetType(), fieldName)?.SetValue(target, value);
        }

        private static void InvokeVoid(object target, string methodName)
        {
            if (target == null) return;
            ReflectionUtil.WalkMethod(target.GetType(), methodName, argTypes: System.Type.EmptyTypes)
                ?.Invoke(target, null);
        }

        private static int ReadInt(object target, string fieldName)
        {
            if (target == null) return -1;
            var f = ReflectionUtil.WalkField(target.GetType(), fieldName);
            return f != null && f.GetValue(target) is int i ? i : -1;
        }

        private static Button? ResolveButton(View view, string fieldName)
            => ReflectionUtil.WalkField(view.GetType(), fieldName)?.GetValue(view) as Button;
    }
}
