using System.Collections.Generic;
using UnityEngine;

namespace DuckovController.UI.Inventory
{
    // Per-view field→Kind tables, KindOrder dicts, BuildViewDescriptors factory, and ChooseInitialFocus.
    // To add a new View: add mapping array + KindOrder dict here, register in BuildViewDescriptors().
    internal sealed partial class PaneRegistry
    {
        internal sealed class ViewDescriptor
        {
            public readonly (string FieldName, Kind Kind)[] Mappings;
            public readonly System.Collections.Generic.Dictionary<Kind, int> KindOrder;
            // Post-discover hook: adds panes directly to registry.Panes (e.g. QuestView).
            public readonly System.Action<PaneRegistry, Duckov.UI.View>? DiscoverHook;

            public ViewDescriptor(
                (string, Kind)[] mappings,
                System.Collections.Generic.Dictionary<Kind, int> kindOrder,
                System.Action<PaneRegistry, Duckov.UI.View>? discoverHook = null)
            {
                Mappings = mappings;
                KindOrder = kindOrder;
                DiscoverHook = discoverHook;
            }
        }

        // Lazy<T>: eager init would run before the mapping arrays below are assigned (field init order),
        // producing null Mappings and a silent LT/RT regression. Deferred until first runtime call.
        private static readonly System.Lazy<System.Collections.Generic.Dictionary<string, ViewDescriptor>> _viewByNameLazy =
            new System.Lazy<System.Collections.Generic.Dictionary<string, ViewDescriptor>>(BuildViewDescriptors);

        private static System.Collections.Generic.Dictionary<string, ViewDescriptor> _viewByName => _viewByNameLazy.Value;

        // LootView: all inventory/bag/pet/filter/storeall fields.
        private static readonly (string Field, Kind Kind)[] _lootViewMapping =
        {
            ("characterSlotCollectionDisplay", Kind.CharSlots),
            ("characterInventoryDisplay",      Kind.CharBag),
            ("petInventoryDisplay",            Kind.Pet),
            ("lootTargetInventoryDisplay",     Kind.Target),
            ("lootTargetFilterDisplay",        Kind.TargetFilter),
            ("storeAllButton",                 Kind.StoreAllAction),
        };

        // StockShopView (trader) field → Kind.
        private static readonly (string Field, Kind Kind)[] _stockShopViewMapping =
        {
            ("playerInventoryDisplay", Kind.CharSlots),
            ("petInventoryDisplay",    Kind.Pet),
            ("playerStorageDisplay",   Kind.TargetFilter),
            ("entryTemplate",          Kind.Merchant),
        };

        // BlackMarketView: both panels registered; only active one surfaces.
        private static readonly (string Field, Kind Kind)[] _blackMarketViewMapping =
        {
            ("demandPanel", Kind.DemandPanel),
            ("supplyPanel", Kind.SupplyPanel),
        };

        private static readonly (string Field, Kind Kind)[] _bitcoinMinerViewMapping =
        {
            ("inventoryDisplay",      Kind.CharSlots),
            ("storageDisplay",        Kind.TargetFilter),
            ("minerSlotsDisplay",     Kind.MinerSlots),
            ("minerInventoryDisplay", Kind.MinerOutput),
        };

        private static readonly (string Field, Kind Kind)[] _formulasRegisterViewMapping =
        {
            ("inventoryDisplay",              Kind.CharSlots),
            ("playerStorageInventoryDisplay", Kind.TargetFilter),
            ("registerSlotDisplay",           Kind.SubmitSlot),
        };

        // CraftView: section tabs driven by RB/LB (CraftViewVerbMap), not registered as a pane.
        private static readonly (string Field, Kind Kind)[] _craftViewMapping =
        {
            ("listEntryTemplate",  Kind.CraftList),
        };

        private static readonly (string Field, Kind Kind)[] _perkTreeViewMapping =
        {
            ("contentParent", Kind.PerkGrid),
        };

        // Same component types as LootView; CharSlots/CharBag ChooseInitialFocus resolves correctly.
        private static readonly (string Field, Kind Kind)[] _itemRepairViewMapping =
        {
            ("slotDisplay",      Kind.CharSlots),
            ("inventoryDisplay", Kind.CharBag),
        };

        // ItemDecomposeView: backpack + storage grids (same component types as LootView).
        // CharBag/PlayerStorage both resolve to the top-left InventoryEntry via FirstInventoryEntryByPosition.
        private static readonly (string Field, Kind Kind)[] _itemDecomposeViewMapping =
        {
            ("characterInventoryDisplay", Kind.CharBag),       // player backpack
            ("storageDisplay",            Kind.PlayerStorage), // storage
        };

        // Warehouse mapped to Kind.Target: TryAdvancePage hard-codes Kind.Target when re-landing focus.
        private static readonly (string Field, Kind Kind)[] _showcaseViewMapping =
        {
            ("inventoryDisplay",               Kind.CharBag),       // player backpack
            ("inventoryDisplay_PlayerStorage", Kind.Target),        // warehouse (paged)
            ("inventoryDisplay_Target",        Kind.PlayerStorage), // showcase display inventory
            ("slotsDisplay",                   Kind.CharSlots),     // showcase equipment slots
        };

        // Single SlotDisplays focus paneRoot (SubmitSlot→paneRoot; CharSlots falls through when no child).
        // Fields verified from GamingConsoleView_20260530 dump.
        private static readonly (string Field, Kind Kind)[] _gamingConsoleViewMapping =
        {
            ("characterInventory",           Kind.CharBag),    // player backpack (Capacity=76)
            ("storageInventory",             Kind.Target),     // storage inventory
            ("petInventory",                 Kind.Pet),        // pet inventory
            ("monitorSlotDisplay",           Kind.SubmitSlot), // single monitor slot (focus paneRoot)
            ("consoleSlotDisplay",           Kind.CharSlots),  // single console slot (focus paneRoot)
            ("consoleSlotCollectionDisplay", Kind.MinerSlots), // console sub-slots: SlotDisplay(Clone)s
        };

        // questEntryParent is Transform-typed; discovery entirely in the hook.
        private static readonly (string Field, Kind Kind)[] _questViewMapping =
            System.Array.Empty<(string, Kind)>();

        // questEntriesParent is RectTransform-typed; generic loop resolves it. Shares QuestList order.
        private static readonly (string Field, Kind Kind)[] _questGiverViewMapping =
        {
            ("questEntriesParent", Kind.QuestList),
        };

        // selectionPanel roots a pooled BuildingBtnEntry list; child Button is the focus node.
        private static readonly (string Field, Kind Kind)[] _builderViewMapping =
        {
            ("selectionPanel", Kind.BuildingCatalog),
        };

        // LootView default cycle order.
        private static readonly System.Collections.Generic.Dictionary<Kind, int> _kindOrder = new()
        {
            { Kind.CharSlots,      1 },
            { Kind.TargetFilter,   2 },
            { Kind.Target,         3 },
            { Kind.CharBag,        4 },
            { Kind.Pet,            5 },
            { Kind.Merchant,       6 },
            { Kind.PlayerStorage,  7 },
            { Kind.QuickSlots,     8 },
            { Kind.StoreAllAction, 90 },
            { Kind.Unknown,        99 },
        };

        private static readonly System.Collections.Generic.Dictionary<Kind, int> _stockShopKindOrder = new()
        {
            { Kind.CharSlots,      1 },
            { Kind.Pet,            2 },
            { Kind.TargetFilter,   3 },
            { Kind.Merchant,       4 },
            { Kind.PlayerStorage,  5 },
            { Kind.CharBag,        6 },
            { Kind.Target,         7 },
            { Kind.StoreAllAction, 90 },
            { Kind.Unknown,        99 },
        };

        private static readonly System.Collections.Generic.Dictionary<Kind, int> _blackMarketKindOrder = new()
        {
            { Kind.DemandPanel,    1 },
            { Kind.SupplyPanel,    2 },
            { Kind.StoreAllAction, 90 },
            { Kind.Unknown,        99 },
        };

        private static readonly System.Collections.Generic.Dictionary<Kind, int> _bitcoinMinerKindOrder = new()
        {
            { Kind.CharSlots,      1 },
            { Kind.TargetFilter,   2 },
            { Kind.MinerSlots,     3 },
            { Kind.MinerOutput,    4 },
            { Kind.StoreAllAction, 90 },
            { Kind.Unknown,        99 },
        };

        private static readonly System.Collections.Generic.Dictionary<Kind, int> _formulasRegisterKindOrder = new()
        {
            { Kind.CharSlots,      1 },
            { Kind.TargetFilter,   2 },
            { Kind.SubmitSlot,     3 },
            { Kind.StoreAllAction, 90 },
            { Kind.Unknown,        99 },
        };

        private static readonly System.Collections.Generic.Dictionary<Kind, int> _craftViewKindOrder = new()
        {
            { Kind.CraftFilters, 1 },
            { Kind.CraftList,    2 },
            { Kind.Unknown,      99 },
        };

        // Dedup helper for single-pane views: { only→1, Unknown→99 }.
        private static System.Collections.Generic.Dictionary<Kind, int> SinglePaneOrder(Kind only)
            => new() { { only, 1 }, { Kind.Unknown, 99 } };

        private static readonly System.Collections.Generic.Dictionary<Kind, int> _questViewKindOrder = SinglePaneOrder(Kind.QuestList);

        private static readonly System.Collections.Generic.Dictionary<Kind, int> _perkTreeViewKindOrder = SinglePaneOrder(Kind.PerkGrid);

        private static readonly System.Collections.Generic.Dictionary<Kind, int> _itemRepairViewKindOrder = new()
        {
            { Kind.CharSlots, 1 },
            { Kind.CharBag,   2 },
            { Kind.Unknown,   99 },
        };

        // ItemDecomposeView cycle: backpack → storage. No Target pane → initial focus falls to CharBag (backpack).
        private static readonly System.Collections.Generic.Dictionary<Kind, int> _itemDecomposeViewKindOrder = new()
        {
            { Kind.CharBag,       1 },
            { Kind.PlayerStorage, 2 },
            { Kind.Unknown,       99 },
        };

        // GamingConsoleView cycle: backpack → storage → monitor slot → console
        // slot → console sub-slots → pet.
        private static readonly System.Collections.Generic.Dictionary<Kind, int> _gamingConsoleViewKindOrder = new()
        {
            { Kind.CharBag,    1 }, // player backpack
            { Kind.Target,     2 }, // storage
            { Kind.SubmitSlot, 3 }, // monitor slot
            { Kind.CharSlots,  4 }, // console slot
            { Kind.MinerSlots, 5 }, // console sub-slots (FcController etc.)
            { Kind.Pet,        6 }, // pet
            { Kind.Unknown,    99 },
        };

        // ShowcaseView cycle: player backpack → warehouse → showcase inv → showcase slots.
        private static readonly System.Collections.Generic.Dictionary<Kind, int> _showcaseViewKindOrder = new()
        {
            { Kind.CharBag,       1 }, // player backpack
            { Kind.Target,        2 }, // warehouse (paged)
            { Kind.PlayerStorage, 3 }, // showcase inventory
            { Kind.CharSlots,     4 }, // showcase slots
            { Kind.Unknown,       99 },
        };

        private static System.Collections.Generic.Dictionary<string, ViewDescriptor> BuildViewDescriptors()
        {
            return new System.Collections.Generic.Dictionary<string, ViewDescriptor>
            {
                ["LootView"] = new ViewDescriptor(
                    _lootViewMapping,
                    _kindOrder,
                    discoverHook: (reg, view) => reg.DiscoverShortcutEditor(view)),

                ["StockShopView"] = new ViewDescriptor(
                    _stockShopViewMapping,
                    _stockShopKindOrder),

                ["BlackMarketView"] = new ViewDescriptor(
                    _blackMarketViewMapping,
                    _blackMarketKindOrder),

                ["BitcoinMinerView"] = new ViewDescriptor(
                    _bitcoinMinerViewMapping,
                    _bitcoinMinerKindOrder),

                ["FormulasRegisterView"] = new ViewDescriptor(
                    _formulasRegisterViewMapping,
                    _formulasRegisterKindOrder),

                // Same fields as FormulasRegisterView; reuses its mapping + order.
                ["MasterKeysRegisterView"] = new ViewDescriptor(
                    _formulasRegisterViewMapping,
                    _formulasRegisterKindOrder),

                ["CraftView"] = new ViewDescriptor(
                    _craftViewMapping,
                    _craftViewKindOrder),

                ["PerkTreeView"] = new ViewDescriptor(
                    _perkTreeViewMapping,
                    _perkTreeViewKindOrder),

                ["ItemRepairView"] = new ViewDescriptor(
                    _itemRepairViewMapping,
                    _itemRepairViewKindOrder),

                ["ItemDecomposeView"] = new ViewDescriptor(
                    _itemDecomposeViewMapping,
                    _itemDecomposeViewKindOrder),

                ["ShowcaseView"] = new ViewDescriptor(
                    _showcaseViewMapping,
                    _showcaseViewKindOrder),

                ["GamingConsoleView"] = new ViewDescriptor(
                    _gamingConsoleViewMapping,
                    _gamingConsoleViewKindOrder),

                // QuestView: no fields; hook does all discovery.
                ["QuestView"] = new ViewDescriptor(
                    _questViewMapping,
                    _questViewKindOrder,
                    discoverHook: (reg, view) => reg.DiscoverQuestView(view)),

                // QuestGiverView: RectTransform-typed field; no hook; shares QuestList order.
                ["QuestGiverView"] = new ViewDescriptor(
                    _questGiverViewMapping,
                    _questViewKindOrder),

                ["MasterKeysView"] = new ViewDescriptor(
                    System.Array.Empty<(string, Kind)>(),
                    _masterKeysViewKindOrder,
                    discoverHook: (reg, view) => reg.DiscoverMasterKeysList(view)),

                ["NoteIndexView"] = new ViewDescriptor(
                    System.Array.Empty<(string, Kind)>(),
                    _noteIndexViewKindOrder,
                    discoverHook: (reg, view) => reg.DiscoverNoteIndexList(view)),

                ["StorageDock"] = new ViewDescriptor(
                    System.Array.Empty<(string, Kind)>(),
                    _storageDockViewKindOrder,
                    discoverHook: (reg, view) => reg.DiscoverStorageDock(view)),

                ["MapSelectionView"] = new ViewDescriptor(
                    System.Array.Empty<(string, Kind)>(),
                    _mapSelectionViewKindOrder,
                    discoverHook: (reg, view) => reg.DiscoverMapSelection(view)),

                ["EndowmentSelectionPanel"] = new ViewDescriptor(
                    System.Array.Empty<(string, Kind)>(),
                    _endowmentViewKindOrder,
                    discoverHook: (reg, view) => reg.DiscoverEndowment(view)),

                ["MiniMapView"] = new ViewDescriptor(
                    System.Array.Empty<(string, Kind)>(),
                    _miniMapViewKindOrder,
                    discoverHook: (reg, view) => reg.DiscoverMiniMapToolbox(view)),

                // BuilderView: cursor/placement driven by BuilderViewVerbMap+BuilderNavigator, not panes.
                ["BuilderView"] = new ViewDescriptor(
                    _builderViewMapping,
                    _builderViewKindOrder),
            };
        }

        private static readonly System.Collections.Generic.Dictionary<Kind, int> _masterKeysViewKindOrder = SinglePaneOrder(Kind.KeyList);

        private static readonly System.Collections.Generic.Dictionary<Kind, int> _noteIndexViewKindOrder = SinglePaneOrder(Kind.NoteList);

        private static readonly System.Collections.Generic.Dictionary<Kind, int> _storageDockViewKindOrder = SinglePaneOrder(Kind.StorageDockEntries);

        private static readonly System.Collections.Generic.Dictionary<Kind, int> _mapSelectionViewKindOrder = SinglePaneOrder(Kind.MapDestinations);

        private static readonly System.Collections.Generic.Dictionary<Kind, int> _endowmentViewKindOrder = SinglePaneOrder(Kind.EndowmentOptions);

        private static readonly System.Collections.Generic.Dictionary<Kind, int> _miniMapViewKindOrder = SinglePaneOrder(Kind.MarkerPalette);

        private static readonly System.Collections.Generic.Dictionary<Kind, int> _builderViewKindOrder = SinglePaneOrder(Kind.BuildingCatalog);

        // Fallback: smallest Index in active entries. GetComponentsInChildren order is not slot order.
        private static RectTransform? FirstInventoryEntryByIndex(Transform paneRoot)
        {
            var entries = paneRoot.GetComponentsInChildren<Duckov.UI.InventoryEntry>(includeInactive: false);
            Duckov.UI.InventoryEntry? best = null;
            int bestIdx = int.MaxValue;
            foreach (var e in entries)
            {
                if (e == null || !e.gameObject.activeInHierarchy) continue;
                int idx = e.Index;
                if (idx < bestIdx) { bestIdx = idx; best = e; }
            }
            return best != null ? best.transform as RectTransform : null;
        }

        // Top-left entry by anchoredPosition. Some panes enumerate Index reversed vs layout;
        // by-position is stable regardless of index order or pool rebinding.
        private static RectTransform? FirstInventoryEntryByPosition(Transform paneRoot)
        {
            var entries = paneRoot.GetComponentsInChildren<Duckov.UI.InventoryEntry>(includeInactive: false);
            Duckov.UI.InventoryEntry? best = null;
            UnityEngine.Vector2 bestPos = default;
            foreach (var e in entries)
            {
                if (e == null || !e.gameObject.activeInHierarchy) continue;
                var inv = e.Master?.Target;
                if (inv == null || e.Index >= inv.Capacity) continue; // skip ghost slots
                if (!(e.transform is RectTransform rt)) continue;
                var p = rt.anchoredPosition;
                // Top-left = highest y, then lowest x. 0.5px tolerance treats a row as level.
                if (best == null || p.y > bestPos.y + 0.5f
                    || (UnityEngine.Mathf.Abs(p.y - bestPos.y) <= 0.5f && p.x < bestPos.x))
                {
                    best = e;
                    bestPos = p;
                }
            }
            return best != null ? best.transform as RectTransform : null;
        }

        // Top-left pooled entry's child Button by anchoredPosition. Skips size<=1 clones (not yet laid out).
        // Returns child Button because the entry root isn't whitelisted as a FocusGraph node.
        private static RectTransform? FirstPooledButtonByPosition(Transform paneRoot, string entryTypeName)
        {
            var comps = paneRoot.GetComponentsInChildren<Component>(includeInactive: false);
            RectTransform? best = null;
            UnityEngine.Vector2 bestPos = default;
            foreach (var c in comps)
            {
                if (c == null || c.GetType().Name != entryTypeName) continue;
                if (!c.gameObject.activeInHierarchy) continue;
                if (!(c.transform is RectTransform rt)) continue;
                var sz = rt.rect.size;
                if (sz.x <= 1f || sz.y <= 1f) continue; // skip pre-layout clones (size-0)
                var p = rt.anchoredPosition;
                // Top-left = highest y, then lowest x. 0.5px tolerance treats a row as level.
                if (best == null || p.y > bestPos.y + 0.5f
                    || (UnityEngine.Mathf.Abs(p.y - bestPos.y) <= 0.5f && p.x < bestPos.x))
                {
                    best = rt;
                    bestPos = p;
                }
            }
            if (best == null) return null;
            var btn = best.GetComponentInChildren<UnityEngine.UI.Button>(false);
            if (btn == null || !(btn.transform is RectTransform brt)) return null;
            var bsz = brt.rect.size; // button must also be laid out
            if (bsz.x <= 1f || bsz.y <= 1f) return null;
            return brt;
        }

        // Reads NoteIndexView.displayingNote for the view that owns paneRoot.
        // Null when paneRoot isn't under a NoteIndexView or nothing is displayed.
        private static string? ReadDisplayingNote(Transform paneRoot)
        {
            // paneRoot is the entries scroll content; the NoteIndexView component is an ancestor.
            var view = paneRoot.GetComponentInParent(typeof(Duckov.UI.View)) as Component;
            if (view == null || view.GetType().Name != "NoteIndexView") return null;
            var displayingField = ReflectionUtil.WalkField(view.GetType(), "displayingNote");
            return displayingField?.GetValue(view) as string;
        }

        // NoteIndexView_Entry.key is a public property over the private `note` field.
        // Null on reflection miss or before Setup() (the getter dereferences note.key).
        private static string? ReadNoteEntryKey(Component entry)
        {
            var keyProp = entry.GetType().GetProperty("key",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public);
            if (keyProp == null) return null;
            try { return keyProp.GetValue(entry) as string; }
            catch { return null; }
        }

        // PT-2: the NoteIndexView_Entry whose key matches the game's currently-displayed note
        // (NoteIndexView.displayingNote — set by ShowNote when a note is taken/read in-raid).
        // Returns null when no note is displayed, when the matching entry isn't laid out, or on
        // any reflection miss → caller falls back to the first entry.
        private static RectTransform? FirstNoteEntryMatchingDisplayingNote(Transform paneRoot)
        {
            var displayingNote = ReadDisplayingNote(paneRoot);
            if (string.IsNullOrEmpty(displayingNote)) return null;

            var entries = paneRoot.GetComponentsInChildren<Component>(includeInactive: false);
            foreach (var c in entries)
            {
                if (c == null || c.GetType().Name != "NoteIndexView_Entry") continue;
                if (!c.gameObject.activeInHierarchy) continue;
                if (ReadNoteEntryKey(c) == displayingNote) return c.transform as RectTransform;
            }
            return null;
        }

        // Pool-reshuffle drift guard for NoteIndexView. Displaying a note runs SetNoteRead →
        // onNoteStatusChanged → RefreshEntries, which ReleaseAll+re-Gets the LIFO pool and re-binds
        // our focused entry GO to the mirror-index note. Returns the entry that SHOULD hold focus
        // (the one showing displayingNote) only when focus has drifted off it; null when nothing is
        // displayed (locked/empty inspector) or focus is already correct — so we never yank focus.
        private static RectTransform? ReconcileNoteFocus(Transform paneRoot, GameObject? current)
        {
            var displayingNote = ReadDisplayingNote(paneRoot);
            if (string.IsNullOrEmpty(displayingNote)) return null;

            // Cheap O(1) early-out: focus already on the displaying note (steady state).
            if (current != null)
            {
                var cc = current.GetComponent("NoteIndexView_Entry");
                if (cc != null && ReadNoteEntryKey(cc) == displayingNote) return null;
            }
            return FirstNoteEntryMatchingDisplayingNote(paneRoot);
        }

        // Called lazily via Pane.InitialFocusResolver.
        private static RectTransform? ChooseInitialFocus(RectTransform paneRoot, Kind kind)
        {
            switch (kind)
            {
                case Kind.StoreAllAction:
                    return paneRoot;

                case Kind.TargetFilter:
                    // LootView: InventoryFilterDisplayEntry (filter strip).
                    // StockShopView: InventoryEntry (playerStorageDisplay reuses this Kind).
                    return FirstActiveChildByTypeName(paneRoot, "InventoryFilterDisplayEntry")
                        ?? FirstActiveChildByTypeName(paneRoot, "InventoryEntry")
                        ?? paneRoot;

                case Kind.CharSlots:
                    // LootView: SlotDisplay (equipment slots).
                    // StockShopView: InventoryEntry (player inventory panel reuses this Kind).
                    return FirstActiveChildByTypeName(paneRoot, "SlotDisplay")
                        ?? FirstActiveChildByTypeName(paneRoot, "InventoryEntry")
                        ?? paneRoot;

                case Kind.CharBag:
                case Kind.Pet:
                case Kind.Target:
                case Kind.PlayerStorage:
                    // By visual position (top-left), not index: some panes enumerate index reversed vs layout.
                    return FirstInventoryEntryByPosition(paneRoot)
                        ?? FirstInventoryEntryByIndex(paneRoot)
                        ?? FirstActiveChildByTypeName(paneRoot, "InventoryEntry")
                        ?? paneRoot;

                case Kind.Merchant:
                    return FirstActiveChildByTypeName(paneRoot, "StockShopItemEntry") ?? paneRoot;

                case Kind.SubmitSlot:
                    return paneRoot; // The SlotDisplay IS the focus target.

                case Kind.MinerSlots:
                    return FirstActiveChildByTypeName(paneRoot, "SlotDisplay") ?? paneRoot;

                case Kind.MinerOutput:
                    return FirstActiveChildByTypeName(paneRoot, "InventoryEntry") ?? paneRoot;

                case Kind.DemandPanel:
                case Kind.SupplyPanel:
                {
                    // "DemandEntry"/"SupplyEntry" are GO names, not component names; use name-prefix search.
                    string namePrefix = kind == Kind.DemandPanel ? "DemandEntry" : "SupplyEntry";
                    var firstCard = FirstActiveChildByGameObjectName(paneRoot, namePrefix);
                    if (firstCard != null)
                    {
                        var btn = firstCard.gameObject.GetComponentInChildren<UnityEngine.UI.Button>(false);
                        if (btn != null) return btn.transform as RectTransform;
                    }
                    // null = "not ready"; refocus window re-picks until cards instantiate after tab switch.
                    return null;
                }

                case Kind.CraftList:
                    return FirstActiveChildByTypeName(paneRoot, "CraftView_ListEntry") ?? paneRoot;

                case Kind.CraftFilters:
                    return FirstActiveChildByTypeName(paneRoot, "CraftViewFilterBtnEntry") ?? paneRoot;

                case Kind.QuestList:
                    // No paneRoot fallback: empty list would focus bare scroll container (phantom outline).
                    return FirstActiveChildByTypeName(paneRoot, "QuestEntry");

                case Kind.PerkGrid:
                    return FirstActiveChildByTypeName(paneRoot, "PerkEntry") ?? paneRoot;

                case Kind.QuickSlots:
                    return FirstActiveChildByTypeName(paneRoot, "ItemShortcutEditorEntry") ?? paneRoot;

                case Kind.KeyList:
                    // MasterKeysView: list of KeyEntry GameObjects, each carrying
                    // a MasterKeysIndexEntry component.
                    return FirstActiveChildByTypeName(paneRoot, "MasterKeysIndexEntry") ?? paneRoot;

                case Kind.NoteList:
                    // NoteIndexView: Entry(Clone)s with NoteIndexView_Entry.
                    // PT-2: when a note is taken in-raid, NoteIndexView.ShowNote sets the game's
                    // displayingNote to the just-taken note (inspector already shows it). Our verb map
                    // synth-clicks the focused entry, so seed focus from displayingNote — else the
                    // first-by-hierarchy entry is clicked and SetDisplayTargetNote overwrites the
                    // correct note with the wrong one. Fall back to first entry when nothing is shown.
                    return FirstNoteEntryMatchingDisplayingNote(paneRoot)
                        ?? FirstActiveChildByTypeName(paneRoot, "NoteIndexView_Entry") ?? paneRoot;

                case Kind.StorageDockEntries:
                    // Visual first (top-left) by anchoredPosition; no paneRoot fallback (size-0 root stuck).
                    return FirstPooledButtonByPosition(paneRoot, "StorageDockEntry");

                case Kind.MapDestinations:
                    // MapSelectionView: teleport-destination cards (MapSelectionEntry).
                    return FirstActiveChildByTypeName(paneRoot, "MapSelectionEntry") ?? paneRoot;

                case Kind.EndowmentOptions:
                    // EndowmentSelectionPanel: talent cards (EndowmentSelectionEntry).
                    return FirstActiveChildByTypeName(paneRoot, "EndowmentSelectionEntry") ?? paneRoot;

                case Kind.MarkerPalette:
                    // First active toolbox button (icon row). MapMarkerPanelButton
                    // wraps a UnityEngine.UI.Button.
                    return FirstActiveChildByTypeName(paneRoot, "MapMarkerPanelButton")
                        ?? FirstActiveButton(paneRoot)   // fallback: any active Button
                        ?? paneRoot;

                case Kind.BuildingCatalog:
                    // Visual first (top-left) by anchoredPosition; null lets converge window re-pick.
                    return FirstPooledButtonByPosition(paneRoot, "BuildingBtnEntry");

                default:
                    return paneRoot;
            }
        }
    }
}
