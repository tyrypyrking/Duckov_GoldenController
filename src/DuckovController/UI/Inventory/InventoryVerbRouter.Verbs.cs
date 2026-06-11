using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.UI;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DuckovController.UI.Inventory
{
    // Partial: Transfer, shop interactions, smart-take dispatch, view-button reflection helpers.
    internal sealed partial class InventoryVerbRouter
    {
        // Synth Fast-Pick: sets hovering=true then invokes InventoryDisplay.NotifyItemDoubleClicked via reflection.
        // Non-entry paths: Button onClick, StockShopItemEntry pointer-click, IPointerClickHandler synth-click.
        internal void Transfer(GameObject focus)
        {
            var entry = focus.GetComponent<InventoryEntry>();
            if (entry == null)
            {
                var btn = focus.GetComponent<UnityEngine.UI.Button>();
                if (btn != null && btn.interactable)
                {
                    btn.onClick.Invoke();
                    return;
                }

                if (focus.GetComponent("StockShopItemEntry") != null
                    && View.ActiveView?.GetType().Name == "StockShopView")
                {
                    DoShopAction(focus, View.ActiveView);
                    return;
                }

                var clickHandler = focus.GetComponent<IPointerClickHandler>();
                if (clickHandler != null)
                {
                    var ped = new PointerEventData(EventSystem.current)
                    {
                        button = PointerEventData.InputButton.Left,
                    };
                    ExecuteEvents.Execute(focus, ped, ExecuteEvents.pointerClickHandler);
                    return;
                }

                Log.Debug_("VerbRouter: Transfer on non-actionable focus, ignoring.");
                return;
            }

            // StockShopView: use select+confirm (sell via interactionButton) instead of Fast-Pick.
            if (entry.Content != null
                && View.ActiveView?.GetType().Name == "StockShopView")
            {
                DoShopAction(focus, View.ActiveView);
                return;
            }

            PointerEventDispatcher.Hover(null, focus); // ensure hovering=true

            // Invoke NotifyItemDoubleClicked directly — can't fire its C# event from outside the class.
            try
            {
                if (_notifyItemDoubleClickedMethod == null)
                {
                    var displayType = typeof(InventoryDisplay);
                    _notifyItemDoubleClickedMethod = displayType.GetMethod(
                        "NotifyItemDoubleClicked",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                }
                if (_notifyItemDoubleClickedMethod == null)
                {
                    Log.Error("VerbRouter: InventoryDisplay.NotifyItemDoubleClicked method not found via reflection.");
                    return;
                }
                var master = entry.Master;
                if (master == null) return;
                var ped = new PointerEventData(EventSystem.current);
                _notifyItemDoubleClickedMethod.Invoke(master, new object[] { entry, ped });
            }
            catch (Exception e)
            {
                Log.Error($"VerbRouter.Transfer failed: {e}");
            }
        }

        // A in shop: InventoryEntry → ItemUIUtilities.Select (activates Details+interactionButton); X commits.
        // StockShopItemEntry → pointer-click (sets buy-side state).
        internal void DoShopAction(GameObject focus, View shopView)
        {
            var entry = focus.GetComponent<InventoryEntry>();
            if (entry != null && entry.Content != null)
            {
                var itemDisplay = focus.GetComponentInChildren<ItemDisplay>(includeInactive: false);
                if (itemDisplay != null)
                {
                    ItemUIUtilities.Select(itemDisplay);
                    return;
                }
            }
            // Merchant-side StockShopItemEntry or empty slot fallback.
            PointerEventDispatcher.Click(focus);
        }

        // INV-1: true when active view has an ItemDetailsDisplay field. Cached per Type.
        private static readonly Dictionary<Type, bool> _detailsCapCache = new Dictionary<Type, bool>();

        internal bool ActiveViewHasDetailsPanel()
        {
            var view = View.ActiveView;
            if (view == null) return false;
            var t = view.GetType();
            if (_detailsCapCache.TryGetValue(t, out var has)) return has;
            bool found = false;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            foreach (var f in t.GetFields(flags))
            {
                if (typeof(ItemDetailsDisplay).IsAssignableFrom(f.FieldType)) { found = true; break; }
            }
            _detailsCapCache[t] = found;
            return found;
        }

        // INV-1: toggle Details via ItemUIUtilities selection (same lever as DoShopAction).
        // Hold-Y again on same item → Select(null) to close. Gates on ItemDisplay.Target so
        // it works for both InventoryEntry and StockShopItemEntry; empty slots return false.
        internal bool OpenItemDetails(GameObject? focus)
        {
            if (focus == null) return false;
            var itemDisplay = focus.GetComponentInChildren<ItemDisplay>(includeInactive: false);
            if (itemDisplay == null || itemDisplay.Target == null) return false;
            // Toggle: holding Y again on the already-shown item dismisses Details;
            // holding Y on a different item switches the panel to that item.
            if (ReferenceEquals(ItemUIUtilities.SelectedItemDisplay, itemDisplay))
                ItemUIUtilities.Select(null);
            else
                ItemUIUtilities.Select(itemDisplay);
            return true;
        }

        // INV-1: item selected AND view has a Details panel.
        internal bool IsItemDetailsShown()
        {
            return ActiveViewHasDetailsPanel() && ItemUIUtilities.SelectedItem != null;
        }

        // Select(null) is safe — setter null-checks before NotifyUnselected.
        internal static void DismissShopDetails()
        {
            ItemUIUtilities.Select(null);
        }

        // Reflect a Button field on a view and invoke its onClick if interactable.
        internal bool TryClickViewField(View view, string fieldName)
        {
            try
            {
                var f = view.GetType().GetField(fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var btn = f?.GetValue(view) as UnityEngine.UI.Button;
                if (btn != null && btn.interactable)
                {
                    btn.onClick.Invoke();
                    return true;
                }
            }
            catch (Exception e) { Log.Debug_($"VerbRouter.TryClickViewField({fieldName}): {e.Message}"); }
            return false;
        }

        // Smart-Take direction: Target-pane focus → Outbound (loot→char);
        // CharBag/CharSlots/Pet focus on storage view → Inbound (char→storage).
        // Standalone LootView or other views → no-op.
        internal void TrySmartTake(GameObject? focus)
        {
            var view = View.ActiveView;
            if (view == null) return;

            var rules = Rules;
            if (rules == null)
            {
                Log.Debug_("VerbRouter.TrySmartTake: no SmartTakeRules — skipping.");
                return;
            }

            if (!rules.Enabled)
            {
                // Master switch off: X behaves like vanilla take-all.
                if (view is LootView lvAll) FireRawTakeAll(lvAll);
                return;
            }

            // Only LootView is wired in v1.
            var lootView = view as LootView;
            if (lootView == null)
            {
                Log.Debug_($"VerbRouter.TrySmartTake: view={view.GetType().Name} not supported v1, no-op.");
                return;
            }

            var t = view.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            var lootBoxField = t.GetField("targetLootBox", flags);
            var filterField  = t.GetField("lootTargetFilterDisplay", flags);
            var lootBox = lootBoxField?.GetValue(view) as InteractableLootbox;
            var lootFilter = filterField?.GetValue(view) as InventoryFilterDisplay;

            var target = lootView.TargetInventory;
            if (target == null)
            {
                Log.Debug_("VerbRouter.TrySmartTake: standalone LootView (TargetInventory=null), no-op.");
                return;
            }

            // Pane the focus is currently on.
            var focusedKind = Panes?.KindAt(focus?.transform) ?? PaneRegistry.Kind.Unknown;

            bool isStorage = (target == PlayerStorage.Inventory);
            ITransferStrategy? strategy = null;
            ItemStatsSystem.Inventory? source = null;
            InventoryFilterDisplay? sourceFilter = null;
            InteractableLootbox? sourceLootBox = null;

            ItemStatsSystem.Inventory? destination = null;

            if (focusedKind == PaneRegistry.Kind.Target)
            {
                // Outbound: target inventory → character.
                source = target;
                sourceLootBox = lootBox;
                sourceFilter = (lootFilter != null && lootFilter.gameObject.activeInHierarchy) ? lootFilter : null;
                strategy = new OutboundTransfer();
                destination = LevelManager.Instance?.MainCharacter?.CharacterItem?.Inventory;
            }
            else if (isStorage
                     && (focusedKind == PaneRegistry.Kind.CharBag
                         || focusedKind == PaneRegistry.Kind.CharSlots
                         || focusedKind == PaneRegistry.Kind.Pet))
            {
                // Inbound: character bag → storage.
                var main = LevelManager.Instance?.MainCharacter;
                var charInv = main?.CharacterItem?.Inventory;
                if (charInv == null)
                {
                    Log.Debug_("VerbRouter.TrySmartTake: character inventory unavailable for inbound.");
                    return;
                }
                source = charInv;
                sourceLootBox = null;       // RequireInspected gate is a no-op (not a loot box)
                sourceFilter = null;        // character side has no filter UI
                strategy = new InboundTransfer(target);
                destination = target;       // storage is the destination for the stack-scan
            }
            else
            {
                Log.Debug_($"VerbRouter.TrySmartTake: focus kind={focusedKind} on " +
                           $"{(isStorage ? "storage" : "loot")} view — no defined direction, no-op.");
                return;
            }

            if (source == null || strategy == null) return;

            var ctx = new SmartTakeContext(source, destination, sourceLootBox, sourceFilter, strategy, rules);
            int taken = SmartTakeEngine.Execute(in ctx);
            Log.Debug_($"VerbRouter.TrySmartTake: kind={focusedKind} direction={(strategy is OutboundTransfer ? "out" : "in")} taken={taken}");
        }
    }
}
