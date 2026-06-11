using System;
using System.Collections.Generic;
using System.Reflection;
using DuckovController.Config;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using Duckov.UI;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuckovController.UI.Inventory
{
    // Per-frame gamepad dispatcher. Owned by GridFocusController.
    // Button→verb delegated to per-view IViewVerbMap (VerbMaps/). Router owns priority B
    // (carry-cancel → menu-close → details-dismiss → view-close), LT/RT pane-jump,
    // BlackMarket tab swap, and Select long-press take-all.
    // Implements IButtonPromptSource so the prompt strip tracks view+focus changes.
    internal sealed partial class InventoryVerbRouter : IButtonPromptSource
    {
        internal UiConfig? Cfg;
        internal SmartTakeRules? Rules;
        internal PaneRegistry? Panes;
        internal InventoryCarryState Carry = new InventoryCarryState();

        // Method handle for the internal Master.NotifyItemDoubleClicked.
        // Resolved lazily on first use.
        private MethodInfo? _notifyItemDoubleClickedMethod;

        // Cached reference to the ItemOperationMenu singleton.
        private ItemOperationMenu? _operationMenu;

        // Deferred focus: after OpenOperationMenu, wait ~1 frame for menu
        // buttons to become active before focusing the first one.
        private bool _pendingFocusOperationMenu;
        private float _pendingOperationMenuAt;

        // Focus restoration: after CloseOperationMenu, restore the slot that
        // was focused before the menu opened.
        private GameObject? _focusBeforeMenu;
        private bool _pendingFocusRestore;
        private float _pendingFocusRestoreAt;

        // Select long-press → raw Take-All/Store-All. Latch prevents multi-fire per hold.
        private float _selectDownAt = -1f;
        private bool _selectLongPressFired;

        // INV-1: tap Y → op-menu, hold Y → Details panel. Only deferred in views with
        // ItemDetailsDisplay (ActiveViewHasDetailsPanel); elsewhere Y fires immediately.
        private float _yDownAt = -1f;
        private bool _yLongPressFired;

        // BUG-4: op-menu button A-press: game's UI Submit fires onClick on A-down;
        // our A-release Transfer would fire a second time, double-toggling "Mark".
        // Latch at PRESS time so it holds even if the menu closes before A-up.
        private bool _suppressAReleaseTransfer;

        // Drives CurrentPrompts; prompt strip fires OnPromptsChanged on focus changes within a view.
        private GameObject? _lastFocus;

        // Recomputed in Tick when View.ActiveView changes; fires OnPromptsChanged on map change.
        private IViewVerbMap? _currentMap;

        public event Action? OnPromptsChanged;

        public IReadOnlyList<PromptEntry> CurrentPrompts
            => _currentMap?.PromptsFor(_lastFocus, this) ?? Array.Empty<PromptEntry>();

        public bool PromptsHorizontal => _currentMap?.HorizontalPrompts ?? false;

        internal IViewVerbMap? CurrentMap => _currentMap;

        // Call once at mod init (ModBehaviour.Awake).
        internal static void RegisterAllViewMaps()
        {
            ViewVerbMapRegistry.SetDefault(new VerbMaps.DefaultViewVerbMap());
            ViewVerbMapRegistry.Register(new VerbMaps.LootViewVerbMap());
            ViewVerbMapRegistry.Register(new VerbMaps.StockShopViewVerbMap());
            ViewVerbMapRegistry.Register(new VerbMaps.BlackMarketViewVerbMap());
            ViewVerbMapRegistry.Register(new VerbMaps.QuestViewVerbMap());
            ViewVerbMapRegistry.Register(new VerbMaps.QuestGiverViewVerbMap());
            ViewVerbMapRegistry.Register(new VerbMaps.NoteIndexViewVerbMap());
            ViewVerbMapRegistry.Register(new VerbMaps.SleepViewVerbMap());
            ViewVerbMapRegistry.Register(new VerbMaps.MapSelectionViewVerbMap());
            ViewVerbMapRegistry.Register(new VerbMaps.MiniMapViewVerbMap());
            ViewVerbMapRegistry.Register(new VerbMaps.PerkTreeViewVerbMap());
            ViewVerbMapRegistry.Register(new VerbMaps.EndowmentSelectionPanelVerbMap());
            ViewVerbMapRegistry.Register(new VerbMaps.RepairViewVerbMap());
            ViewVerbMapRegistry.Register(new VerbMaps.ItemDecomposeViewVerbMap());
            ViewVerbMapRegistry.Register(new VerbMaps.CraftViewVerbMap());
            ViewVerbMapRegistry.Register(new VerbMaps.ATMViewVerbMap());
            ViewVerbMapRegistry.Register(new VerbMaps.MasterKeysRegisterViewVerbMap());
            ViewVerbMapRegistry.Register(new VerbMaps.StorageDockViewVerbMap());
            ViewVerbMapRegistry.Register(new VerbMaps.BuilderViewVerbMap());
        }

        internal void OnFocusChanged(GameObject? newFocus)
        {
            _lastFocus = newFocus;
            Carry.OnFocusChanged(newFocus);
            // Let the verb map react (e.g. NoteIndexViewVerbMap synth-clicks to mirror focused note).
            try { _currentMap?.OnFocusChanged(newFocus, this); }
            catch (System.Exception e) { Log.Warn($"VerbMap.OnFocusChanged: {e.Message}"); }
            // Prompt set can differ even within the same view (e.g. moving off an inventory entry).
            OnPromptsChanged?.Invoke();
        }

        internal void Tick(GameObject? focus, Action<GameObject?> setFocus)
        {
            if (Cfg == null) return;
            Carry.HoldThresholdSec = Cfg.CarryHoldThresholdSec;

            if (Time.timeScale == 0f) return; // suspended while paused

            var view = View.ActiveView;
            var newMap = ViewVerbMapRegistry.For(view);
            if (!ReferenceEquals(newMap, _currentMap))
            {
                _currentMap = newMap;
                OnPromptsChanged?.Invoke();
            }

            // Deferred: land on first op-menu button after OpenOperationMenu.
            if (_pendingFocusOperationMenu)
            {
                if (Time.unscaledTime - _pendingOperationMenuAt >= 0.05f)
                {
                    _pendingFocusOperationMenu = false;
                    if (_operationMenu == null)
                        _operationMenu = UnityEngine.Object.FindObjectOfType<ItemOperationMenu>(true);
                    if (_operationMenu != null)
                    {
                        var firstBtn = FindFirstActiveButton(_operationMenu.gameObject);
                        if (firstBtn != null) setFocus(firstBtn);
                    }
                }
            }

            // Deferred: restore pre-menu focus after CloseOperationMenu (graph needs a frame to rebuild).
            if (_pendingFocusRestore)
            {
                if (Time.unscaledTime - _pendingFocusRestoreAt >= 0.05f)
                {
                    _pendingFocusRestore = false;
                    if (_focusBeforeMenu != null && _focusBeforeMenu.activeInHierarchy)
                        setFocus(_focusBeforeMenu);
                    _focusBeforeMenu = null;
                }
            }

            var pad = Gamepad.current;
            if (pad == null) return;

            _currentMap?.TickView(focus, this); // per-frame analog (e.g. MiniMapView pan/zoom)

            // InputSystem wasPressedThisFrame: immune to null→reconnect phantom edges.
            bool aPressed  = pad.buttonSouth.wasPressedThisFrame;
            bool aHeld     = pad.buttonSouth.isPressed;
            bool aReleased = pad.buttonSouth.wasReleasedThisFrame;
            bool bPressed  = pad.buttonEast.wasPressedThisFrame;
            bool xPressed  = pad.buttonWest.wasPressedThisFrame;
            bool ltPressed = pad.leftTrigger.wasPressedThisFrame;
            bool rtPressed = pad.rightTrigger.wasPressedThisFrame;

            // A button
            if (aPressed)
            {
                // BUG-4: op-menu open → game Submit fires onClick; suppress our redundant Transfer on release.
                _suppressAReleaseTransfer = IsOperationMenuOpen();

                var pressVerb = Carry.OnAPressed(focus);
                // CarryResult.Place: EndDrag already fired inside OnAPressed (carry placed).
                // CarryResult.None: entered Pressing phase or was already Idle.
                _ = pressVerb;
            }
            if (aHeld) Carry.OnAHeldTick(focus);
            if (aReleased)
            {
                var verb = Carry.OnAReleased(focus);
                if (verb == InventoryCarryState.CarryResult.Transfer && focus != null
                    && !_suppressAReleaseTransfer)
                {
                    _currentMap?.TryA(focus, this);
                    DuckovController.Haptics.HapticEngine.Instance?.Play(DuckovController.Haptics.HapticCue.Confirm);
                    Log.Debug_("Haptic: Confirm");
                }
                // CarryResult.None: Carrying release is a no-op (stays in carry).
                _suppressAReleaseTransfer = false;
            }

            // B button (priority: carry-cancel > menu-close > details-dismiss > view-close)
            if (bPressed)
            {
                DuckovController.Haptics.HapticEngine.Instance?.Play(DuckovController.Haptics.HapticCue.Cancel);
                Log.Debug_("Haptic: Cancel");
                Log.Debug_($"VerbRouter B: carry={Carry.Current} opMenuOpen={IsOperationMenuOpen()} view={view?.GetType().Name ?? "null"} mapNull={_currentMap == null}");
                if (_currentMap != null && _currentMap.TryB(focus, this))
                {
                    // Map consumed B — do not also close the view or menu.
                }
                else if (IsOperationMenuOpen())
                {
                    CloseOperationMenu();
                }
                else if (IsItemDetailsShown())
                {
                    // INV-1: dismiss Details before closing view (all inventory-family views).
                    DismissShopDetails();
                }
                else if (View.ActiveView != null)
                {
                    // Same path as GameplayInputDriverPatch.ToggleInventory.
                    try { ((ManagedUIElement)View.ActiveView).Close(); }
                    catch (Exception e) { Log.Warn($"VerbRouter: View.Close failed: {e.Message}"); }
                }
            }

            // X button
            if (xPressed)
            {
                if (Carry.Current == InventoryCarryState.Phase.Carrying)
                {
                    // Smart-Take suppressed while carrying.
                }
                else
                {
                    _currentMap?.TryX(focus, this);
                    DuckovController.Haptics.HapticEngine.Instance?.Play(DuckovController.Haptics.HapticCue.SmartAction);
                    Log.Debug_("Haptic: SmartAction");
                }
            }

            // Y button: tap → op-menu, hold → Details (INV-1, details-capable views only)
            HandleYButton(pad, focus);

            // LT/RT: pane jump (LT=next/right, RT=prev/left). BlackMarketView handled separately.
            bool lbPressed = pad.leftShoulder.wasPressedThisFrame;
            bool rbPressed = pad.rightShoulder.wasPressedThisFrame;
            if (ltPressed || rtPressed)
            {
                Log.Debug_($"VerbRouter LT/RT: lt={ltPressed} rt={rtPressed} view={view?.GetType().Name ?? "null"} paneCount={Panes?.Panes.Count ?? -1} focusInPane={Panes?.IndexOfPaneContaining(focus?.transform) ?? -999}");
            }
            if (View.ActiveView?.GetType().Name == "BlackMarketView")
            {
                if (ltPressed || lbPressed) TryClickViewField(View.ActiveView, "btn_demandPanel");
                if (rtPressed || rbPressed) TryClickViewField(View.ActiveView, "btn_supplyPanel");
                // BlackMarket panels lazy-load cards after tab switch; refocus window waits for them.
                if (ltPressed || lbPressed || rtPressed || rbPressed)
                    GridFocusController.Instance?.RequestPreferredRefocus(0.6f);
            }
            else
            {
                // LT/RT: per-view map first (e.g. CraftView section tabs); fall back to pane jump.
                if (ltPressed && !(_currentMap?.TryLT(focus, this) ?? false))
                {
                    JumpPane(focus, setFocus, dir: +1);
                    DuckovController.Haptics.HapticEngine.Instance?.Play(DuckovController.Haptics.HapticCue.PageTab);
                    Log.Debug_("Haptic: PageTab (LT pane)");
                }
                if (rtPressed && !(_currentMap?.TryRT(focus, this) ?? false))
                {
                    JumpPane(focus, setFocus, dir: -1);
                    DuckovController.Haptics.HapticEngine.Instance?.Play(DuckovController.Haptics.HapticCue.PageTab);
                    Log.Debug_("Haptic: PageTab (RT pane)");
                }
                // BUG-1: runtime UI_NextPage/Prev bindings unreliable; fire via UIPageEvents instead.
                if (lbPressed)
                {
                    bool lbConsumed = _currentMap?.TryLB(focus, this) ?? false;
                    if (!lbConsumed && View.ActiveView != null)
                    {
                        UIPageEvents.FirePrev();
                        DuckovController.Haptics.HapticEngine.Instance?.Play(DuckovController.Haptics.HapticCue.PageTab);
                        Log.Debug_("Haptic: PageTab (LB page)");
                    }
                }
                if (rbPressed)
                {
                    bool rbConsumed = _currentMap?.TryRB(focus, this) ?? false;
                    if (!rbConsumed && View.ActiveView != null)
                    {
                        UIPageEvents.FireNext();
                        DuckovController.Haptics.HapticEngine.Instance?.Play(DuckovController.Haptics.HapticCue.PageTab);
                        Log.Debug_("Haptic: PageTab (RB page)");
                    }
                }
            }

            // Select long-press → raw Take-All/Store-All (LootView only, bypasses Smart-Take)
            HandleSelectLongPress(pad);
        }

        // INV-1: tap Y → TryY (op-menu); hold Y → Details. Deferral only in details-capable views.
        private void HandleYButton(Gamepad pad, GameObject? focus)
        {
            bool yPressed  = pad.buttonNorth.wasPressedThisFrame;
            bool yHeld     = pad.buttonNorth.isPressed;
            bool yReleased = pad.buttonNorth.wasReleasedThisFrame;

            if (!ActiveViewHasDetailsPanel()) // no Details panel: fire immediately on press
            {
                if (yPressed) _currentMap?.TryY(focus, this);
                _yDownAt = -1f;
                _yLongPressFired = false;
                return;
            }

            float threshold = Cfg?.DetailsHoldThresholdSec ?? 0.35f;
            if (threshold <= 0f) threshold = 0.35f;

            if (yPressed)
            {
                _yDownAt = Time.unscaledTime;
                _yLongPressFired = false;
            }

            // If OpenItemDetails returns false (no item), don't latch — release still fires tap.
            if (!_yLongPressFired && yHeld && _yDownAt > 0f
                && Time.unscaledTime - _yDownAt >= threshold)
            {
                if (OpenItemDetails(focus))
                    _yLongPressFired = true;
            }

            if (yReleased)
            {
                if (!_yLongPressFired) _currentMap?.TryY(focus, this);
                _yDownAt = -1f;
                _yLongPressFired = false;
            }
        }

        private void HandleSelectLongPress(Gamepad pad)
        {
            bool selectPressed  = pad.selectButton.wasPressedThisFrame;
            bool selectHeld     = pad.selectButton.isPressed;
            bool selectReleased = pad.selectButton.wasReleasedThisFrame;

            float threshold = Cfg?.SelectLongPressSec ?? 0.5f;
            if (threshold <= 0f) threshold = 0.5f;

            if (selectPressed)
            {
                _selectDownAt = Time.unscaledTime;
                _selectLongPressFired = false;
            }

            if (!_selectLongPressFired && selectHeld && _selectDownAt > 0f)
            {
                // Guard: LootView only (avoids map-toggle and LB+RB+Y dump chord).
                if (View.ActiveView is LootView lv
                    && Time.unscaledTime - _selectDownAt >= threshold)
                {
                    FireRawTakeAll(lv);
                    _selectLongPressFired = true;
                }
            }

            if (selectReleased)
            {
                _selectDownAt = -1f;
                _selectLongPressFired = false;
            }
        }

        private void FireRawTakeAll(LootView lv)
        {
            // Fire pickAllButton + storeAllButton onClick. Game's own gates decide which acts.
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            try
            {
                var t = lv.GetType();
                var pickField  = t.GetField("pickAllButton",  flags);
                var storeField = t.GetField("storeAllButton", flags);
                var pickBtn  = pickField?.GetValue(lv)  as UnityEngine.UI.Button;
                var storeBtn = storeField?.GetValue(lv) as UnityEngine.UI.Button;

                int fired = 0;
                if (pickBtn != null)
                {
                    pickBtn.onClick.Invoke();
                    fired++;
                }
                if (storeBtn != null)
                {
                    storeBtn.onClick.Invoke();
                    fired++;
                }
                Log.Debug_($"VerbRouter: Select-long-press fired {fired} vanilla take-all listener(s).");
            }
            catch (Exception e)
            {
                Log.Error($"VerbRouter.FireRawTakeAll: {e.Message}");
            }
        }

        private void JumpPane(GameObject? focus, Action<GameObject?> setFocus, int dir)
        {
            if (Panes == null || Panes.Panes.Count == 0) return;
            int currentIdx = Panes.IndexOfPaneContaining(focus?.transform);
            var target = dir < 0 ? Panes.PrevActive(currentIdx) : Panes.NextActive(currentIdx);
            if (target?.InitialFocus == null) return;
            setFocus(target.InitialFocus.gameObject);

            // Pin focus so the next graph rebuild doesn't reassign back to a default pane
            // (pool-managed entries like merchant cards can briefly disappear from the graph).
            GridFocusController.Instance?.PinFocusFor(0.5f);
        }
    }
}
