using System.Collections.Generic;
using System.Reflection;
using Duckov.UI;
using UnityEngine;

namespace DuckovController.UI.Inventory
{
    // Discovers panes inside a known View via a static field→Kind dict (see PaneRegistry.Mappings.cs).
    // Pool-template fields (Merchant, CraftList, CraftFilters) use parent container as pane root.
    // Adding a new View is a one-line Mappings.cs edit.
    internal sealed partial class PaneRegistry
    {
        internal enum Kind
        {
            Unknown,
            CharSlots,
            CharBag,
            Pet,
            Target,
            TargetFilter,
            StoreAllAction,
            QuickSlots,     // LootView bottom shortcut bar
            Merchant,       // StockShopView merchant item list
            PlayerStorage,  // StockShopView player storage panel
            SubmitSlot,     // FormulasRegisterView single-slot submit
            MinerSlots,     // BitcoinMinerView GPU slot collection
            MinerOutput,    // BitcoinMinerView bitcoin output inventory
            DemandPanel,    // BlackMarketView demand (sell) grid
            SupplyPanel,    // BlackMarketView supply (buy) grid
            CraftList,      // CraftView recipe list entries
            CraftFilters,   // CraftView category filter buttons
            QuestList,      // QuestView quest entry list
            PerkGrid,       // PerkTreeView perk node grid
            KeyList,        // MasterKeysView KeyEntry grid
            NoteList,       // NoteIndexView Entry list
            MapDestinations,// MapSelectionView teleport-destination card grid
            EndowmentOptions,// EndowmentSelectionPanel ("choose talent") card row
            MarkerPalette,  // MiniMapView marker toolbox (icon/color buttons)
            StorageDockEntries,// StorageDock ("Wasp Pickup Locker") claim card list
            BuildingCatalog,   // BuilderView building entry list (BuildingBtnEntry)
        }

        internal sealed class Pane
        {
            internal RectTransform Root = default!;
            internal Kind Kind;
            internal string Name = "";
            // Re-searches each call: pool/activate can happen after DiscoverFrom (e.g. BlackMarket tab swap).
            internal System.Func<RectTransform?>? InitialFocusResolver;
            internal RectTransform? InitialFocus => InitialFocusResolver?.Invoke();
            // Drift re-pin for pooled lists whose focused GO can be recycled to a mirror slot
            // (NoteIndexView: displaying a note marks it read → RefreshEntries reshuffles the LIFO pool).
            // Takes the current focus; returns the entry that should hold focus, or null = no re-pin.
            internal System.Func<GameObject?, RectTransform?>? ReconcileFocusResolver;
        }

        internal readonly List<Pane> Panes = new();

        internal void Clear()
        {
            Panes.Clear();
        }

        // Reflects each registered field; active Components become Panes (pool-templates use parent).
        // DiscoverHook fires after the mapping loop (QuestView, LootView ShortcutEditor).
        internal void DiscoverFrom(View view)
        {
            Clear();
            if (view == null) return;

            var typeName = view.GetType().Name;
            if (!_viewByName.TryGetValue(typeName, out var descriptor))
            {
                // Unsupported view — silently skip (no log spam in hot path).
                return;
            }

            var t = view.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            foreach (var (fieldName, kind) in descriptor.Mappings)
            {
                var f = FindField(t, fieldName, flags);
                if (f == null) continue;
                object? val = SafeGetValue(f, view);
                if (val is not Component comp || comp == null) continue;

                // StoreAllAction is a single-button "pane" — reachable via
                // the spatial FocusGraph but excluded from LT/RT traversal.
                if (kind == Kind.StoreAllAction) continue;

                // Pool-template: template is inactive; use parent container as pane root.
                bool isPoolTemplate = kind == Kind.Merchant
                    || kind == Kind.CraftList
                    || kind == Kind.CraftFilters;
                if (!isPoolTemplate && !comp.gameObject.activeInHierarchy) continue;

                var rt = comp.transform as RectTransform;
                if (rt == null) continue;

                if (isPoolTemplate && rt.parent is RectTransform parentRt)
                    rt = parentRt;

                // After potential reparent, verify the pane root itself is active.
                if (!rt.gameObject.activeInHierarchy) continue;

                var paneRoot = rt;
                var paneKind = kind;
                Panes.Add(new Pane
                {
                    Root = paneRoot,
                    Kind = paneKind,
                    Name = fieldName,
                    InitialFocusResolver = () => ChooseInitialFocus(paneRoot, paneKind),
                });
            }

            // Optional per-view hook (QuestView, LootView ShortcutEditor).
            descriptor.DiscoverHook?.Invoke(this, view);

            // Sort by per-view cycle order.
            var order = descriptor.KindOrder;
            Panes.Sort((a, b) =>
            {
                int oa = order.TryGetValue(a.Kind, out var v1) ? v1 : 99;
                int ob = order.TryGetValue(b.Kind, out var v2) ? v2 : 99;
                return oa.CompareTo(ob);
            });
        }

        // questEntryParent is declared as Transform (not Component); cast to RectTransform safe at runtime.
        private void DiscoverQuestView(View view)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var f = FindField(view.GetType(), "questEntryParent", flags);
            if (f == null) return;
            var val = SafeGetValue(f, view);
            var rt = (val as Transform) as RectTransform;
            if (rt == null) return;
            if (!rt.gameObject.activeInHierarchy) return;
            var paneRoot = rt;
            Panes.Add(new Pane
            {
                Root = paneRoot,
                Kind = Kind.QuestList,
                Name = "questEntryParent",
                InitialFocusResolver = () => ChooseInitialFocus(paneRoot, Kind.QuestList),
            });
        }

        internal Pane? PrevActive(int currentIndex)
        {
            if (Panes.Count == 0) return null;
            if (currentIndex < 0) return Panes[Panes.Count - 1];
            int i = currentIndex - 1;
            if (i < 0) i = Panes.Count - 1;
            return Panes[i];
        }

        internal Pane? NextActive(int currentIndex)
        {
            if (Panes.Count == 0) return null;
            if (currentIndex < 0) return Panes[0];
            int i = currentIndex + 1;
            if (i >= Panes.Count) i = 0;
            return Panes[i];
        }

        // Returns the index of the pane whose RectTransform is an ancestor of
        // `focus`, or -1 if focus is not inside any registered pane.
        internal int IndexOfPaneContaining(Transform? focus)
        {
            if (focus == null) return -1;
            for (int i = 0; i < Panes.Count; i++)
            {
                if (IsAncestor(Panes[i].Root, focus)) return i;
            }
            return -1;
        }

        // Returns the Kind of the pane containing the focused transform, or
        // Kind.Unknown if focus is null or outside any registered pane.
        internal Kind KindAt(Transform? focus)
        {
            int idx = IndexOfPaneContaining(focus);
            if (idx < 0) return Kind.Unknown;
            return Panes[idx].Kind;
        }

        private static bool IsAncestor(Transform ancestor, Transform t)
        {
            var cur = t;
            while (cur != null)
            {
                if (cur == ancestor) return true;
                cur = cur.parent;
            }
            return false;
        }

        // KeyEntry grid at Content/Content/Scroll View/Viewport/Content (no field to reflect; walk by name).
        private void DiscoverMasterKeysList(View view)
        {
            var t = view.transform;
            var content = FindByPath(t, "Content/Content/Scroll View/Viewport/Content");
            if (content == null || !content.gameObject.activeInHierarchy) return;
            if (content is not RectTransform rt) return;
            Panes.Add(new Pane
            {
                Root = rt,
                Kind = Kind.KeyList,
                Name = "MasterKeysContent",
                InitialFocusResolver = () => ChooseInitialFocus(rt, Kind.KeyList),
            });
        }

        // Entry list at Content/LeftLayout/Entries Scroll View/Viewport/Content.
        private void DiscoverNoteIndexList(View view)
        {
            var t = view.transform;
            var content = FindByPath(t, "Content/LeftLayout/Entries Scroll View/Viewport/Content");
            if (content == null || !content.gameObject.activeInHierarchy) return;
            if (content is not RectTransform rt) return;
            Panes.Add(new Pane
            {
                Root = rt,
                Kind = Kind.NoteList,
                Name = "NoteIndexContent",
                InitialFocusResolver = () => ChooseInitialFocus(rt, Kind.NoteList),
                ReconcileFocusResolver = current => ReconcileNoteFocus(rt, current),
            });
        }

        // StorageDockEntry claim cards at Content/Scroll View/Viewport/Content.
        private void DiscoverStorageDock(View view)
        {
            var t = view.transform;
            var content = FindByPath(t, "Content/Scroll View/Viewport/Content");
            if (content == null || !content.gameObject.activeInHierarchy) return;
            if (content is not RectTransform rt) return;
            Panes.Add(new Pane
            {
                Root = rt,
                Kind = Kind.StorageDockEntries,
                Name = "StorageDockContent",
                InitialFocusResolver = () => ChooseInitialFocus(rt, Kind.StorageDockEntries),
            });
        }

        // MapSelectionEntry destination cards under direct child "Content".
        private void DiscoverMapSelection(View view)
        {
            var content = view.transform.Find("Content");
            if (content == null || !content.gameObject.activeInHierarchy) return;
            if (content is not RectTransform rt) return;
            Panes.Add(new Pane
            {
                Root = rt,
                Kind = Kind.MapDestinations,
                Name = "MapSelectionContent",
                InitialFocusResolver = () => ChooseInitialFocus(rt, Kind.MapDestinations),
            });
        }

        // Marker toolbox: find by MapMarkerSettingsPanel component; fallback to child named "MarkerToolBox".
        private void DiscoverMiniMapToolbox(View view)
        {
            var t = view.transform;
            RectTransform? toolbox = FirstActiveChildByTypeName(t, "MapMarkerSettingsPanel");
            if (toolbox == null)
            {
                var named = FindChildByName(t, "MarkerToolBox");
                if (named != null && named.gameObject.activeInHierarchy)
                    toolbox = named as RectTransform;
            }
            if (toolbox == null) return;
            var paneRoot = toolbox;
            Panes.Add(new Pane
            {
                Root = paneRoot,
                Kind = Kind.MarkerPalette,
                Name = "MarkerToolBox",
                InitialFocusResolver = () => ChooseInitialFocus(paneRoot, Kind.MarkerPalette),
            });
        }

        // EndowmentSelectionEntry cards at Panel/Scroll View/Viewport/Content/Options.
        private void DiscoverEndowment(View view)
        {
            var options = FindByPath(view.transform, "Panel/Scroll View/Viewport/Content/Options");
            if (options == null || !options.gameObject.activeInHierarchy) return;
            if (options is not RectTransform rt) return;
            Panes.Add(new Pane
            {
                Root = rt,
                Kind = Kind.EndowmentOptions,
                Name = "EndowmentOptions",
                InitialFocusResolver = () => ChooseInitialFocus(rt, Kind.EndowmentOptions),
            });
        }

        // Walk a "/"-separated path from `root`. Returns null on any miss.
        private static Transform? FindByPath(Transform root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path)) return null;
            var parts = path.Split('/');
            var cur = root;
            foreach (var p in parts)
            {
                if (cur == null) return null;
                cur = cur.Find(p);
            }
            return cur;
        }

        // ShortcutEditor is not a [SerializeField]; walk children by name (LootView/Main/ShortcutEditor).
        private void DiscoverShortcutEditor(View view)
        {
            var t = view.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                var found = FindChildByName(child, "ShortcutEditor");
                if (found == null) continue;
                if (!found.gameObject.activeInHierarchy) continue;
                if (found is not RectTransform rt) continue;
                var paneRoot = rt;
                Panes.Add(new Pane
                {
                    Root = paneRoot,
                    Kind = Kind.QuickSlots,
                    Name = "ShortcutEditor",
                    InitialFocusResolver = () => ChooseInitialFocus(paneRoot, Kind.QuickSlots),
                });
                return;
            }
        }

        private static Transform? FindChildByName(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var found = FindChildByName(t.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private static RectTransform? FirstActiveChildByTypeName(Transform root, string typeName)
        {
            var comps = root.GetComponentsInChildren<Component>(includeInactive: false);
            foreach (var c in comps)
            {
                if (c == null) continue;
                if (c.GetType().Name == typeName)
                {
                    var rt = c.transform as RectTransform;
                    if (rt != null && c.gameObject.activeInHierarchy) return rt;
                }
            }
            return null;
        }

        // MarkerPalette fallback when MapMarkerPanelButton search finds nothing.
        private static RectTransform? FirstActiveButton(Transform root)
        {
            var buttons = root.GetComponentsInChildren<UnityEngine.UI.Button>(includeInactive: false);
            foreach (var b in buttons)
            {
                if (b == null || !b.gameObject.activeInHierarchy) continue;
                if (b.transform is RectTransform rt) return rt;
            }
            return null;
        }

        // BlackMarket cards are named "DemandEntry"/"SupplyEntry" — not component class names; use name-prefix.
        private static RectTransform? FirstActiveChildByGameObjectName(Transform root, string namePrefix)
        {
            var transforms = root.GetComponentsInChildren<Transform>(includeInactive: false);
            foreach (var t in transforms)
            {
                if (t == null || t == root) continue;
                if (t.gameObject.name.StartsWith(namePrefix) && t.gameObject.activeInHierarchy)
                    return t as RectTransform;
            }
            return null;
        }

        private static FieldInfo? FindField(System.Type t, string name, BindingFlags flags)
        {
            for (var cursor = t; cursor != null && cursor != typeof(MonoBehaviour); cursor = cursor.BaseType)
            {
                var f = cursor.GetField(name, flags);
                if (f != null) return f;
            }
            return null;
        }

        private static object? SafeGetValue(FieldInfo f, object target)
        {
            try { return f.GetValue(target); }
            catch { return null; }
        }
    }
}
