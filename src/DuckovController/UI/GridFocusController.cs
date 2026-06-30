using System;
using DuckovController.Config;
using Duckov.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DuckovController.UI
{
    // D-pad focus controller for inventory/loot grids: synthesizes PointerEnter/Exit
    // so hover state updates without cursor warping. Click → PointerClick; hold A → drag.
    internal sealed partial class GridFocusController : MonoBehaviour
    {
        // TODO: BlackMarketView card desc on Y (need hover-owner dump while card is hovered).

        internal static GridFocusController? Instance { get; private set; }
        internal ControllerConfig? Cfg;

        private View? _activeView;
        private FocusGraph _graph = new();
        private GameObject? _focused;

        // External owners (e.g. BuilderViewVerbMap while placing) can suspend our
        // catalog focus so the native EventSystem Submit can't double-fire on A.
        private bool _externalFocusSuspend;
        internal void SetExternalFocusSuspended(bool suspended) => _externalFocusSuspend = suspended;

        // Focus seam for verb maps that move focus programmatically (e.g. ItemDecomposeView LB/RB
        // grid↔slider panel toggle). Pins so the next graph rebuild doesn't reassign it back.
        internal GameObject? CurrentFocus => _focused;
        internal void SetFocusExternal(GameObject? target)
        {
            SetFocus(target);
            PinFocusFor(0.5f);
        }

        private float _lastNavTime;
        private float _navHoldStarted; // hold-start time for the one-time repeat delay (vs _lastNavTime = last fire)
        private Vector2 _lastNavAxis;
        // Resets on axis change or release (PollNav clears via _lastNavAxis = zero).
        private int _navHoldStepCount;

        private bool _graphDirty = true;
        private int _frameOfLastRebuild = -1;

        private DuckovController.UI.Inventory.PaneRegistry _panes = new();
        private DuckovController.UI.Inventory.InventoryVerbRouter _router = new();
        internal DuckovController.UI.Inventory.InventoryVerbRouter Router => _router;
        private bool _viewChangeSubscribed;
        private View? _pendingDiscoveryView;
        // Write-only today. TODO: timeout-based forced discovery fallback if FadeGroup never reports settled.
        private float _pendingDiscoverySince;
        private System.Action<UnityEngine.GameObject?>? _setFocusAction;

        private FocusOutlineOverlay? _outlineOverlay;

        // Re-hide cursor on controller activity (trackpad touches re-show it).
        private float _lastGamepadActivity = -10f;

        // Cached ItemOperationMenu reference for menu-open guard in initial-focus block.
        private Duckov.UI.ItemOperationMenu? _cachedOperationMenu;

        // Cached ItemShortcutPanel root — lives outside the LootView hierarchy so
        // FocusGraph.Build(view.gameObject) doesn't walk it. Appended separately.
        private GameObject? _cachedShortcutPanel;

        internal void PinFocusFor(float seconds)
        {
            _focusPinUntil = Time.unscaledTime + seconds;
        }

        // Lazy-load tolerance for tab-swap panels whose entry cards instantiate
        // a few frames after the swap (e.g. BlackMarketView's Supply/Demand
        // panels). Clears focus and suppresses the spatial-default fallback in
        // the initial-focus block for `window` seconds, so the periodic re-pick
        // keeps trying PickPreferredInitialFocus until the cards appear — instead
        // of landing on the whole panel / a tab button and sticking there.
        internal void RequestPreferredRefocus(float window)
        {
            ResetFocus();
            _lastPreferredPick = null;
            _preferredRefocusUntil = Time.unscaledTime + window;
            // Re-discover panes. A tab swap (e.g. BlackMarket Demand↔Supply)
            // activates a different panel, but panes are only discovered at
            // view-open — DiscoverFrom skips inactive panels, so the newly-active
            // panel is absent from _panes and focus can never reach its cards.
            // Re-running discovery registers the now-active panel; its lazy
            // InitialFocusResolver then resolves to the first card as soon as the
            // lazy-loaded entries instantiate.
            _pendingDiscoveryView = View.ActiveView;
            _pendingDiscoverySince = Time.unscaledTime;
            _graphDirty = true;
        }

        // Called by MenuFocusController to skip its selection-enforcement when
        // GridFocusController is already managing focus in the active view.
        internal bool IsHandlingActiveView()
        {
            var v = View.ActiveView;
            return v != null && IsSupportedView(v);
        }

        private bool IsOperationMenuOpen()
        {
            if (_cachedOperationMenu == null)
                _cachedOperationMenu = UnityEngine.Object.FindObjectOfType<Duckov.UI.ItemOperationMenu>(true);
            return _cachedOperationMenu != null && _cachedOperationMenu.gameObject.activeInHierarchy;
        }

        // After JumpPane: prevents initial-focus from stealing focus while destination pane entries are loading.
        private float _focusPinUntil = -1f;

        // While > now: initial-focus re-picks each frame (converging on first slot as pool settles); suppresses spatial fallback. Set on view-open and tab-swap.
        private float _preferredRefocusUntil = -1f;
        // Release the window if user navigated away from this auto-pick.
        private GameObject? _lastPreferredPick;

        // Armed during the open/refocus window. LayoutGroup computes anchoredPosition at willRenderCanvases
        // (post-LateUpdate); re-pick there so focus lands on the true top-left the same frame — no mirror flash.
        private bool _willRenderHooked;

        // INV-2 remembered slot: backing Inventory (by ref) + anchoredPosition.
        // GOs are pool-recycled; re-resolve by position rather than index (index-match lands on mirrored row).
        private object? _rememberedSlotInv;
        private Vector2 _rememberedSlotPos;
        private int _rememberedSlotIndex = -1; // diagnostics only
        // Bounds the wait for the remembered cell to return after a mid-reload; past it accept nearest.
        private float _restoreWaitUntil = -1f;
        private int _dbgFocusIdx = int.MinValue;
        private Vector2 _dbgFocusPos;
        private string _dbgLastSelName = "";

        // Cursor snapshot captured on managed-view entry; restored on exit so inventory nav doesn't displace the aim cursor.
        private Vector2 _savedCursorPos;
        private bool _hasSavedCursor;
        private Vector2 _dbgLastCursorPos = new Vector2(float.NaN, float.NaN);
        // Set during slot-restore rebuilds so SetFocus → RememberSlot doesn't overwrite the identity mid-rebind.
        private bool _suppressRemember;

        // MiniMapView: MapMarkerSettingsPanel.Setup() reshuffles pooled buttons after SelectIcon/SelectColor.
        // Remember last focus position (screen-centre) and re-pin to nearest node when the GO drifts.
        private Vector2 _mmRememberedScreenPos;
        private bool    _mmHasRememberedPos;
        private Vector2 _lastNoteFocusPos; // NoteIndexView golden re-fit dedup (entry GO moves on pool reshuffle)

        // Deferred page-flip focus: resolved on first rebuild after the flip (not a fixed timer).
        // _pendingPagedFocusAt is a safety-net timeout only.
        private bool _pendingPagedFocus;
        private DuckovController.UI.Inventory.PaneRegistry.Kind _pendingPagedFocusKind;
        private bool _pendingPagedFocusIsBottom;
        private float _pendingPagedFocusX;
        private float _pendingPagedFocusAt;
        private int _pendingPagedFocusFrame;

        private void Awake()
        {
            Instance = this;
            _setFocusAction = SetFocus;
            _outlineOverlay = gameObject.AddComponent<FocusOutlineOverlay>();
        }

        // Call from OnEnable and on hot-reload config delivery.
        internal void SetConfig(ControllerConfig cfg)
        {
            Cfg = cfg;
            _router.Cfg = cfg.Ui;
            _router.Rules = cfg.SmartTake;
            _graph.CrossPanePenalty = cfg.Ui.CrossPaneDistancePenalty;
        }

        private void OnEnable()
        {
            if (!_viewChangeSubscribed)
            {
                View.OnActiveViewChanged += OnActiveViewChanged;
                _viewChangeSubscribed = true;
            }
            _graph.IsDifferentPane = (from, candidate) =>
            {
                int a = _panes.IndexOfPaneContaining(from?.transform);
                int b = _panes.IndexOfPaneContaining(candidate?.transform);
                return a != b && a >= 0 && b >= 0;
            };
            if (Cfg != null) SetConfig(Cfg);
            _router.Panes = _panes;
        }

        private void OnDisable()
        {
            if (_viewChangeSubscribed)
            {
                View.OnActiveViewChanged -= OnActiveViewChanged;
                _viewChangeSubscribed = false;
            }
            _router.Carry.Cancel();
            _graph.IsDifferentPane = null;
            RevertExitGlyph();
            RevertButtonHints();
            HookWillRender(false);
            // Restore cursor for KBM users after controller mode exits.
            Cursor.visible = true;
        }

        // Clear neon outline + EventSystem selection before pool recycles the focused GO into the next container.
        // Without this, both ride onto the mirror cell on reopen ("stale mirrored focus" flash).
        // Selection cleared only when it's still our slot so game menus are undisturbed.
        private void ClearFocusVisuals()
        {
            // Balance PointerEnter with PointerExit: hover-only highlights (e.g. ButtonAnimation.hoveringIndicator
            // on StorageDock claim buttons) aren't driven by ES selection and would persist on the recycled GO.
            if (_focused != null) PointerEventDispatcher.Hover(_focused, null);
            if (_outlineOverlay != null) _outlineOverlay.Hide();
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null && _focused != null
                && ReferenceEquals(es.currentSelectedGameObject, _focused))
            {
                es.SetSelectedGameObject(null);
            }
            if (Log.Verbose && _focused != null)
                Log.Debug_($"GFC.MIRROR clear-visuals f={Time.frameCount} was={_focused.name}");
        }

        private void OnActiveViewChanged()
        {
            _router.Carry.Cancel();
            _panes.Clear();
            ClearFocusVisuals(); // drop stale hover/outline/selection before recycle
            _focused = null;
            ForgetSlot(); // INV-2: don't carry a slot memory across views
            ForgetMiniMapDriftMemory(); // UI-2: drop toolbox pos memory on view change
            _graphDirty = true;
            _cachedShortcutPanel = null; // re-resolve from pane on next LootView build
            _pendingDiscoveryView = View.ActiveView;
            _pendingDiscoverySince = Time.unscaledTime;
            if (Log.Verbose)
                Log.Debug_($"GFC.OnActiveViewChanged: view={View.ActiveView?.GetType().Name ?? "null"} supported={View.ActiveView != null && IsSupportedView(View.ActiveView)}");
        }

        private bool IsViewSettled(View v)
        {
            // Reflection: FadeGroup lives in Duckov.UI.Animations; soft dependency.
            var fg = v.GetComponent("FadeGroup") as Component;
            if (fg == null) return true;
            var t = fg.GetType();
            try
            {
                var isShown = (bool)(t.GetProperty("IsShown")?.GetValue(fg) ?? false);
                var inProg = (bool)(t.GetProperty("IsShowingInProgress")?.GetValue(fg) ?? false);
                return isShown && !inProg;
            }
            catch { return true; } // if we can't read, assume settled
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            HookWillRender(false);
            _router?.Carry?.Cancel();
            if (_graph != null) _graph.IsDifferentPane = null;
            if (_outlineOverlay != null) Destroy(_outlineOverlay);
        }


        // True when a supported View is open, gamepad connected, and we hold focus.
        // ItemHoveringUI patch consults this to pin the info panel beside the item instead of on top of it.
        internal static bool IsDrivingFocus()
        {
            var inst = Instance;
            if (inst == null || inst.Cfg == null || !inst.Cfg.Ui.EnableGridFocus) return false;
            if (inst._activeView == null || inst._focused == null) return false;
            // Don't gate on recent activity — that snapped the panel back after idle seconds.
            return UnityEngine.InputSystem.Gamepad.current != null;
        }

        // Screen-space top-left for the item-info panel beside (not on) the focused item.
        // Returns false if no focus or rect is degenerate.
        internal bool TryGetHoverInfoAnchor(RectTransform contents, out Vector2 screenTopLeft)
        {
            screenTopLeft = default;
            var focusRt = _focused != null ? _focused.transform as RectTransform : null;
            if (focusRt == null || contents == null) return false;

            Rect item = ScreenRectOf(focusRt);
            if (item.width <= 1f || item.height <= 1f) return false;

            // Anchor beside the whole pane (not the item) for stable X across columns; vertical tracks the item row.
            Rect bounds = item;
            int paneIdx = _panes != null ? _panes.IndexOfPaneContaining(focusRt) : -1;
            if (paneIdx >= 0 && paneIdx < _panes!.Panes.Count)
            {
                var paneRoot = _panes.Panes[paneIdx].Root;
                if (paneRoot != null)
                {
                    Rect pr = ScreenRectOf(paneRoot);
                    if (pr.width > 1f && pr.height > 1f) bounds = pr;
                }
            }

            // Panel size in screen px: contents.rect is canvas-local (CanvasScaler ≈2x on Deck), not screen px.
            Rect panelScreen = ScreenRectOf(contents);
            float panelW = panelScreen.width;
            float panelH = panelScreen.height;
            const float gap = 24f;
            float sw = Screen.width, sh = Screen.height;

            // Place on the centre-facing edge of the list (right-of-centre→left, left→right, below→above, above→below).
            float dx = bounds.center.x - sw * 0.5f;
            float dy = bounds.center.y - sh * 0.5f;

            float left, top;
            if (Mathf.Abs(dx) >= Mathf.Abs(dy))
            {
                // Horizontal placement; the OTHER axis tracks the item row.
                left = dx >= 0f ? bounds.xMin - gap - panelW   // list right of centre
                                : bounds.xMax + gap;           // list left of centre
                top = item.center.y + panelH * 0.5f;           // centred on the item
            }
            else
            {
                // Vertical placement; the OTHER axis tracks the item column.
                top = dy < 0f ? bounds.yMax + gap + panelH       // list below centre -> above it
                              : bounds.yMin - gap;               // list above centre -> below it
                left = item.center.x - panelW * 0.5f;            // centred on the item
            }

            // Keep fully on-screen (top is the panel's top edge, screen y up).
            left = Mathf.Clamp(left, 0f, Mathf.Max(0f, sw - panelW));
            top = Mathf.Clamp(top, Mathf.Min(panelH, sh), sh);

            screenTopLeft = new Vector2(left, top);
            return true;
        }

        // World-corner → screen Rect for a RectTransform, honoring its canvas
        // render mode (overlay → null camera).
        private static Rect ScreenRectOf(RectTransform rt)
        {
            var canvas = rt.GetComponentInParent<Canvas>();
            Camera? cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                ? canvas.worldCamera : null;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners); // 0=BL, 1=TL, 2=TR, 3=BR
            Vector2 bl = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            Vector2 tr = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
            return new Rect(bl.x, bl.y, tr.x - bl.x, tr.y - bl.y);
        }

        private void Update()
        {
            if (!DuckovController.Diagnostics.PerfFlags.GridFocus) return;
            if (Cfg == null || !Cfg.Ui.EnableGridFocus) { ResetFocus(); return; }

            // Steam Deck trackpad re-shows the OS cursor; suppress unconditionally inside managed views
            // (outside, keep the activity gate so KBM gameplay is untouched).
            var padForCursor = Gamepad.current;
            if (padForCursor != null && Cursor.visible)
            {
                var cursorView = View.ActiveView;
                bool inManagedView = cursorView != null && IsSupportedView(cursorView);
                if (inManagedView || HasRecentGamepadActivity(padForCursor))
                    Cursor.visible = false;
            }

            // "New craft" popup (StrongNotification) floats over the register view but is not a View,
            // and the pad can't dismiss it natively (UI_Confirm isn't bound by default). Own input
            // here while it's up so A/B dismiss it. Checked BEFORE the IsSupportedView gate so it
            // works regardless of what (if anything) sits underneath.
            if (IsStrongNotificationOpen())
            {
                HandleStrongNotification();
                return;
            }
            _strongNotifWasOpen = false;
            _strongNotifAArmed = false;

            var view = View.ActiveView;
            if (view == null || !IsSupportedView(view))
            {
                if (_activeView != null)
                {
                    ResetFocus();
                    // Leaving a managed view → put the gameplay crosshair back where
                    // it was before we warped the cursor around the inventory.
                    if (_hasSavedCursor && Mouse.current != null)
                    {
                        try { Mouse.current.WarpCursorPosition(_savedCursorPos); } catch { }
                        _hasSavedCursor = false;
                    }
                }
                RevertExitGlyph();
                RevertButtonHints();
                if (_questRewardWasOpen) EndQuestReward(); // don't leak the modal's glyph/prompt override out of the view
                _activeView = null;
                _pendingDiscoveryView = null;
                return;
            }

            if (view != _activeView)
            {
                // Entering a managed view from gameplay (no prior managed view):
                // snapshot the crosshair so we can restore it on exit.
                if (_activeView == null && !_hasSavedCursor && Mouse.current != null)
                {
                    _savedCursorPos = Mouse.current.position.ReadValue();
                    _hasSavedCursor = true;
                    // Park the cursor off-screen so it can't linger over a slot
                    // (whose hover blinks on a refresh) or drag the crosshair.
                    // BuilderView drives the cursor itself (RS build cursor) —
                    // suppress the park there or it fights the navigator's write.
                    if (!IsCursorDrivenView(view))
                        try { Mouse.current.WarpCursorPosition(new Vector2(-100f, -100f)); } catch { }
                }
                _activeView = view;
                _focused = null;
                ForgetSlot(); // INV-2: fresh view → drop any slot memory
                ForgetMiniMapDriftMemory(); // UI-2: drop toolbox pos memory on view change
                _graphDirty = true;
                RevertExitGlyph(); // restore the previous view's exit icon
            }

            // Quest reward-claim modal (QuestCompletePanel) floats over the quest board after a turn-in
            // but is NOT a View. While it's up it owns input — A claims the top reward (cycle), X claims
            // all, B skips — and the board is fully locked out. Mirrors the Split overlay handoff.
            {
                var rewardPanel = GetActiveQuestRewardPanel();
                if (rewardPanel != null)
                {
                    HandleQuestReward(rewardPanel);
                    return;
                }
                if (_questRewardWasOpen) EndQuestReward(); // modal closed → drop glyph + prompt override
            }

            // Split overlay (opened from the op-menu) floats over this view but is NOT a View, so it
            // never reaches the verb maps. While it's shown it owns input — drive its count slider +
            // A/B here and return, suppressing grid nav, the exit glyph, and the verb router below.
            if (IsSplitDialogOpen())
            {
                HandleSplitDialog();
                return;
            }
            if (_splitWasOpen)
            {
                // Split overlay just closed → re-debounce the next open and restore focus visuals.
                // _focused is usually the now-inactive op-menu button it was opened from; drop it so
                // TryInitialFocus re-picks a live grid slot (re-showing that item's hover + outline).
                // If it somehow still points at a live item, just re-show in place.
                _splitWasOpen = false;
                _splitAArmed = false;
                DuckovController.UI.Prompts.ViewHintPanel.Override = null; // restore the view's own prompts
                if (_splitConfirmGlyph != null) { Destroy(_splitConfirmGlyph); _splitConfirmGlyph = null; }
                if (_focused == null || !_focused.activeInHierarchy)
                    SetFocus(null);
                else
                {
                    PointerEventDispatcher.Hover(null, _focused);
                    ApplyFocusOutline(_focused);
                }
            }

            // Host the B glyph on this view's exit button + any verb-map button
            // glyph hints (e.g. X on Confirm). Both gamepad-gated.
            UpdateExitGlyph(view);
            UpdateButtonHints(view);
            // ItemDecomposeView: hide the decompose button's native "F" prompt so our X glyph replaces it in place.
            UpdateDecomposeNativeGlyph(view);

            if (_externalFocusSuspend)
            {
                // Cursor-driven sub-mode (BuilderView placing): drop catalog focus so EventSystem Submit can't double-fire on A.
                // Router still ticks so BuilderViewVerbMap.TickView can clear the suspend.
                if (_focused != null) SetFocus(null);
                var esSuspend = UnityEngine.EventSystems.EventSystem.current;
                if (esSuspend != null && esSuspend.currentSelectedGameObject != null)
                    esSuspend.SetSelectedGameObject(null);
            }
            else
            {
                TryInitialFocus(view);
                TryFlushPagedFocus();
                TryFlushPaneDiscovery();

                // Diagnostics: log the EventSystem selection whenever it changes, so a
                // game-side flash (re-select to another slot, or a null blip then a
                // first-slot grab) is visible even though our focus stays put.
                {
                    var dbgEs = UnityEngine.EventSystems.EventSystem.current;
                    var selName = dbgEs?.currentSelectedGameObject != null
                        ? dbgEs.currentSelectedGameObject.name : "null";
                    if (selName != _dbgLastSelName)
                    {
                        Log.Debug_($"GFC.SelState: sel={selName} focused={(_focused != null ? _focused.name : "null")}");
                        _dbgLastSelName = selName;
                    }
                    // Where the real cursor actually settles (on change). Reveals if
                    // anything drags it back onto a slot after we park it off-screen.
                    var mp = dbgEs != null && Mouse.current != null
                        ? Mouse.current.position.ReadValue() : Vector2.zero;
                    if (!(Mathf.Approximately(mp.x, _dbgLastCursorPos.x) && Mathf.Approximately(mp.y, _dbgLastCursorPos.y)))
                    {
                        Log.Debug_($"GFC.CursorPos: {mp}");
                        _dbgLastCursorPos = mp;
                    }
                }

                // Slider focused: dpad adjusts value (skips spatial nav). NoFocusView: skip dpad entirely.
                // Yield while hard-paused (timeScale=0): MenuFocusOverlay owns the pause menu; outline freezes on slot.
                // MiniMapView/BuilderView: LS reserved for pan — StickAsDpad gated off, D-pad still navigates.
                if (!IsNoFocusView(view) && Time.timeScale != 0f)
                {
                    var navPad = Gamepad.current;
                    bool stickIsDpad = Cfg.Ui.StickAsDpad
                        && view.GetType().Name != "MiniMapView"
                        && view.GetType().Name != "BuilderView";
                    _stick.Sample(navPad != null ? navPad.leftStick.ReadValue() : Vector2.zero, stickIsDpad);
                    if (navPad == null || !HandleFocusedSlider(navPad))
                        PollNav();
                }
            }
            _router.Tick(_focused, _setFocusAction!);
            // INV-1: the full Details panel (Y-hold) already shows the item's info — suppress the
            // redundant hover-info panel so the two don't overlay each other on the same item.
            if (_router.IsItemDetailsShown()) HideItemHoverPanel();
            DriveScrollIntoView();
        }

        // LateUpdate: runs after game UI Update repositions pooled entries; corrects drift same frame before render.
        private void LateUpdate()
        {
            if (!DuckovController.Diagnostics.PerfFlags.GridFocus) return;
            if (Cfg == null || !Cfg.Ui.EnableGridFocus) return;
            if (IsSplitDialogOpen()) return; // overlay owns input — don't drift-correct / re-pin behind it
            if (_activeView == null || _focused == null) return;
            if (IsOperationMenuOpen() || _pendingPagedFocus) return;
            if (Time.unscaledTime < _focusPinUntil) return;

            if (Time.unscaledTime < _preferredRefocusUntil)
            {
                // Actual re-pick deferred to OnWillRenderCanvases (post-LateUpdate, pre-render) for POST-layout positions.
                HookWillRender(true);
                // Re-park cursor every frame of window: one-shot park on entry can lose the race to WarpCursor lag.
                // BuilderView drives cursor itself — suppress or it fights the navigator.
                if (Mouse.current != null && !IsCursorDrivenView(_activeView))
                {
                    try { Mouse.current.WarpCursorPosition(new Vector2(-100f, -100f)); } catch { }
                }
                // Fall through to selection re-pin — don't return.
            }
            else
            {
                HookWillRender(false);
                if (_rememberedSlotInv != null) TryDriftCorrect();
                TryMiniMapDriftCorrect();
            }

            // Pooled-list reconcile: re-pin focus to the displaying entry after a pool reshuffle
            // recycled our focused GO to the mirror slot (NoteIndexView marks notes read on display).
            // Runs in both branches so it covers the post-converge nav case too.
            TryReconcileFocus();

            // Re-assert ES selection if stolen to another non-null element (white tint or ~0.16s re-select after refresh).
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null && _focused != null && _focused.activeInHierarchy)
            {
                var cur = es.currentSelectedGameObject;
                if (cur != null && !ReferenceEquals(cur, _focused))
                {
                    Log.Debug_($"GFC.PinSelection: ES stole → {cur.name}; restoring {_focused.name}");
                    es.SetSelectedGameObject(_focused);
                }
            }

            // NoteIndexView: the pool reshuffle relocates the focused entry GO a frame after we re-pin,
            // and FocusOutlineOverlay.Show pins the golden frame once — so re-fit it whenever the
            // focused entry actually moves, so the outline tracks the settled position.
            if (_activeView != null && _focused != null
                && _activeView.GetType().Name == "NoteIndexView"
                && _focused.transform is RectTransform nfrt)
            {
                var p = nfrt.anchoredPosition;
                if ((p - _lastNoteFocusPos).sqrMagnitude > 0.25f)
                {
                    _lastNoteFocusPos = p;
                    ApplyFocusOutline(_focused);
                }
            }
        }

        private void HookWillRender(bool on)
        {
            if (on == _willRenderHooked) return;
            if (on) Canvas.willRenderCanvases += OnWillRenderCanvases;
            else    Canvas.willRenderCanvases -= OnWillRenderCanvases;
            _willRenderHooked = on;
        }

        // Post-layout re-pin (willRenderCanvases): re-picks true top-left and re-asserts ES selection.
        // Self-unhooks when window closes — no cost outside the ~0.4s window.
        private void OnWillRenderCanvases()
        {
            if (Cfg == null || !Cfg.Ui.EnableGridFocus
                || _activeView == null || _focused == null
                || IsOperationMenuOpen() || _pendingPagedFocus
                || Time.unscaledTime < _focusPinUntil
                || Time.unscaledTime >= _preferredRefocusUntil)
            {
                HookWillRender(false);
                return;
            }
            if (ReferenceEquals(_focused, _lastPreferredPick))
            {
                var repick = PickPreferredInitialFocus();
                if (repick != null && !ReferenceEquals(repick, _focused))
                {
                    SetFocus(repick);
                    _lastPreferredPick = repick;
                }
            }
            // Re-assert selection after layout so the white tint tracks our focus
            // this same frame (the GO the pool moved away keeps the tint otherwise).
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null && _focused != null && _focused.activeInHierarchy)
            {
                var cur = es.currentSelectedGameObject;
                if (cur != null && !ReferenceEquals(cur, _focused))
                    es.SetSelectedGameObject(_focused);
            }
        }

        // MiniMapView/StorageDock: focused GO rides to mirrored position after pool reshuffle.
        // Not InventoryEntry, so anchored-position remember-slot can't cover them — pin by screen position instead.
        private static bool UsesScreenPosDriftPin(string? viewName)
            => viewName == "MiniMapView" || viewName == "StorageDock";

        private void TryMiniMapDriftCorrect()
        {
            if (_focused == null || !_mmHasRememberedPos) return;
            if (_activeView == null || !UsesScreenPosDriftPin(_activeView.GetType().Name)) return;

            float sinceNav = Time.unscaledTime - _lastNavTime;
            if (sinceNav < 0.05f) return; // back off right after a nav step

            var currentCenter = PointerEventDispatcher.ScreenCenterOf(_focused);
            float driftSq = (currentCenter - _mmRememberedScreenPos).sqrMagnitude;
            if (Log.Verbose)
                Log.Debug_($"GFC.MiniMapDrift check: drift={Mathf.Sqrt(driftSq):F1}px sinceNav={sinceNav:F2}s stamp={_mmRememberedScreenPos} cur={currentCenter}");
            // 16px² = 4px radius — catches reshuffle, ignores sub-pixel jitter.
            if (driftSq <= 16f) return;

            // Re-pin to the graph node now occupying the remembered screen position.
            var repinTarget = _graph.NearestTo(_mmRememberedScreenPos);
            if (repinTarget == null || ReferenceEquals(repinTarget, _focused)) return;

            Log.Debug_($"GFC.MiniMapDrift: focused '{_focused.name}' drifted {Mathf.Sqrt(driftSq):F0}px → re-pin to '{repinTarget.name}'");
            _suppressRemember = true;
            SetFocus(repinTarget);
            _suppressRemember = false;
        }

        // Pooled-list focus reconcile: after a pool reshuffle (e.g. NoteIndexView marks a note read on
        // display → RefreshEntries ReleaseAll+re-Gets the LIFO pool), our focused GO can be re-bound to
        // the mirror-index entry. Re-pin to the pane-declared target (the entry holding the shown note).
        private void TryReconcileFocus()
        {
            if (_focused == null || _panes == null) return;
            for (int i = 0; i < _panes.Panes.Count; i++)
            {
                var resolver = _panes.Panes[i].ReconcileFocusResolver;
                if (resolver == null) continue;
                var target = resolver(_focused);
                if (target == null) continue;
                var go = target.gameObject;
                if (go == null || !go.activeInHierarchy || ReferenceEquals(go, _focused)) continue;
                Log.Debug_($"GFC.Reconcile: '{_focused.name}' drifted off displaying entry → re-pin '{go.name}'");
                _suppressRemember = true;
                SetFocus(go);
                _suppressRemember = false;
                _graphDirty = true; // pool changed GO→slot mapping; rebuild so Neighbor uses fresh positions
                return;
            }
        }

        // Re-pin to the remembered cell if the pool moved the focused GO. Exact-cell only; retries next frame if absent.
        private bool TryDriftCorrect()
        {
            if (_focused == null || _rememberedSlotInv == null) return false;
            var frt = _focused.transform as RectTransform;
            var fie = _focused.GetComponent<Duckov.UI.InventoryEntry>();
            if (frt == null || fie == null) return false;
            if (!ReferenceEquals(fie.Master?.Target, _rememberedSlotInv)) return false;
            if (((Vector2)frt.anchoredPosition - _rememberedSlotPos).sqrMagnitude <= 4f) return false;

            var target = TryResolveRememberedSlot(out float driftSqr);
            if (target == null || driftSqr > 3600f || ReferenceEquals(target, _focused)) return false;

            Log.Debug_($"GFC.DriftFix: focused now at {frt.anchoredPosition} (want {_rememberedSlotPos}) → re-resolve");
            _suppressRemember = true;
            SetFocus(target);
            _suppressRemember = false;
            return true;
        }

        // Periodic rebuild (every 10 frames or dirty) + initial-focus pick on fresh build.
        private void TryInitialFocus(View view)
        {
            // During preferred-refocus window: rebuild every frame so lazy-loaded cards get focus the frame they appear.
            if (!_graphDirty && (Time.frameCount - _frameOfLastRebuild) <= 10
                && Time.unscaledTime >= _preferredRefocusUntil) return;

            var vn = view.GetType().Name;
            // MiniMapView: hoist toolbox root lookup before Build so the per-node predicate is a cheap parent-walk.
            System.Func<GameObject, bool>? miniMapExclude = null;
            if (vn == "MiniMapView")
            {
                var toolboxPane = _panes.Panes.Find(p =>
                    p.Kind == DuckovController.UI.Inventory.PaneRegistry.Kind.MarkerPalette);
                var toolboxRoot = toolboxPane?.Root;
                miniMapExclude = go =>
                {
                    // toolboxRoot == null: pane not yet discovered — don't exclude anything.
                    if (go == null || toolboxRoot == null) return false;
                    var t = go.transform;
                    while (t != null)
                    {
                        if (t == toolboxRoot) return false; // inside toolbox — keep
                        t = t.parent;
                    }
                    return true; // outside toolbox — exclude
                };
            }

            _graph.ExcludeNode = (vn == "QuestGiverView" || vn == "QuestView")
                ? IsQuestChromeButton
                : vn == "EndowmentSelectionPanel"
                    ? IsEndowmentChrome     // dpad stays on the talent cards (X confirms)
                    : vn == "ItemRepairView"
                        ? IsRepairChrome    // dpad stays on items (A repairs, X repairs all)
                        : vn == "ItemDecomposeView"
                        ? IsDecomposeChrome // dpad stays on items + count slider (A selects, X decomposes)
                        : vn == "CraftView"
                            ? IsCraftChrome // dpad stays on recipes (RB/LB sections, X crafts)
                            : vn == "ATMView"
                                ? IsAtmChrome // dpad stays on keypad/select; B-exits excluded
                                : vn == "StorageDock"
                                    ? IsStorageDockChrome // dpad stays on claim entries (LB/RB pages, B exits)
                                    : miniMapExclude  // dpad confined to marker toolbox (UI-2); null = use default below
                                        ?? IsViewExitButton;

            _graph.Build(view.gameObject);
            // ItemShortcutPanel lives outside LootView hierarchy — append from QuickSlots pane.
            if (view is LootView)
            {
                if (_cachedShortcutPanel == null)
                {
                    var qsPane = _panes.Panes.Find(p => p.Kind == DuckovController.UI.Inventory.PaneRegistry.Kind.QuickSlots);
                    if (qsPane != null) _cachedShortcutPanel = qsPane.Root.gameObject;
                }
                if (_cachedShortcutPanel != null && _cachedShortcutPanel.activeInHierarchy)
                    _graph.AppendFrom(_cachedShortcutPanel);
            }
            // ItemOperationMenu lives on a separate Canvas root — append its buttons for dpad nav.
            if (_cachedOperationMenu != null && _cachedOperationMenu.gameObject.activeInHierarchy)
                _graph.AppendFrom(_cachedOperationMenu.gameObject);
            _frameOfLastRebuild = Time.frameCount;
            _graphDirty = false;
            if (Log.Verbose)
                Log.Debug_($"GFC.GraphRebuild: view={_activeView?.GetType().Name ?? "null"} nodes={_graph.Count}");

            // Diagnostics: surface drift of the focused entry (same GameObject,
            // new index/position) that happens without a SetFocus — e.g. a loot
            // auto-sort after a take. Logs only when it actually changes.
            if (_focused != null)
            {
                var dfie = _focused.GetComponent<Duckov.UI.InventoryEntry>();
                var dfrt = _focused.transform as RectTransform;
                if (dfie != null && dfrt != null)
                {
                    var p = (Vector2)dfrt.anchoredPosition;
                    if (dfie.Index != _dbgFocusIdx || (p - _dbgFocusPos).sqrMagnitude > 4f)
                    {
                        Log.Debug_($"GFC.FocusState: index={dfie.Index} pos={p} contains={_graph.Contains(_focused)}");
                        _dbgFocusIdx = dfie.Index;
                        _dbgFocusPos = p;
                    }
                }
            }

            bool refocusWindow = Time.unscaledTime < _preferredRefocusUntil;
            if ((_focused == null || !_graph.Contains(_focused) || refocusWindow)
                && !IsOperationMenuOpen()
                && !_pendingPagedFocus
                && Time.unscaledTime >= _focusPinUntil)
            {
                // Suppress initial pick while pane discovery pending (else spatial default for ~3-10 frames → chevron jump).
                // Allow fallback after 0.5s timeout for views with no FadeGroup/hook.
                bool discoveryPending = _pendingDiscoveryView != null;
                bool discoveryTimedOut = discoveryPending
                    && Time.unscaledTime - _pendingDiscoverySince >= 0.5f;
                if (discoveryPending && !discoveryTimedOut) return;

                // If user navigated away from our last auto-pick, release the window immediately.
                if (refocusWindow && _focused != null
                    && !ReferenceEquals(_focused, _lastPreferredPick))
                {
                    _preferredRefocusUntil = -1f;
                }
                else
                {
                    var prevFocused = _focused;
                    // INV-2: on plain refresh restore remembered slot; view-open/tab-swap keep first-slot landing.
                    GameObject? restored = null;
                    if (!refocusWindow && _rememberedSlotInv != null)
                    {
                        restored = TryResolveRememberedSlot(out float bestSqr);
                        // Require exact cell (<60px). A take rebuilds partially (35→10→35); committing early
                        // lands off by ≥1 row then corrects — visible "switch". Wait it out; fall back to nearest if slot gone.
                        bool exact = restored != null && bestSqr <= 3600f;
                        if (!exact)
                        {
                            if (_restoreWaitUntil < 0f) _restoreWaitUntil = Time.unscaledTime + 0.4f;
                            if (Time.unscaledTime < _restoreWaitUntil) return;
                            // settled — accept nearest as graceful fallback
                        }
                        else
                        {
                            _restoreWaitUntil = -1f;
                        }
                    }
                    GameObject? preferred = restored ?? PickPreferredInitialFocus();
                    // NoFocusView (PlayerStatsView): no navigable content — never plant a chevron.
                    if (preferred == null && IsNoFocusView(view))
                    {
                        if (_focused != null) ResetFocus();
                        return;
                    }
                    // Lazy-load: while window open and entries not yet instantiated, retry next frame (no spatial fallback).
                    if (preferred == null && refocusWindow) return;
                    if (preferred != null && !refocusWindow) _preferredRefocusUntil = -1f;
                    GameObject? initial = preferred ?? _graph.InitialFocus();
                    if (!ReferenceEquals(initial, _focused))
                        Log.Debug_($"GFC initial-focus: prev={(prevFocused != null ? prevFocused.name : "null")} → {(initial != null ? initial.name : "null")} window={refocusWindow} discoveryPending={discoveryPending} restored={(restored != null)}");
                    // INV-2: freeze identity during restore burst so intermediate picks don't drift it per rebuild.
                    bool preserveIdentity = _rememberedSlotInv != null && !refocusWindow;
                    _suppressRemember = preserveIdentity;
                    SetFocus(initial);
                    _suppressRemember = false;
                    _lastPreferredPick = initial;
                    _restoreWaitUntil = -1f;
                }
            }
        }

        // Pane-aware: loot box → Target (container first); else → CharBag; else → first pane. Null if no panes yet.
        private GameObject? PickPreferredInitialFocus()
        {
            // Fixed-control views (SleepView): focus time slider; A=Sleep routed by SleepViewVerbMap regardless.
            var fixedView = View.ActiveView;
            if (fixedView != null && fixedView.GetType().Name == "SleepView")
            {
                var sld = fixedView.GetComponentInChildren<UnityEngine.UI.Slider>(false);
                if (sld != null) return sld.gameObject;
            }
            if (_panes.Panes.Count == 0) return null;
            // Target pane only exists when loot box display is active (PaneRegistry filters inactive GOs).
            var target = _panes.Panes.Find(p =>
                p.Kind == DuckovController.UI.Inventory.PaneRegistry.Kind.Target);
            if (target?.InitialFocus != null) return target.InitialFocus.gameObject;

            var charBag = _panes.Panes.Find(p =>
                p.Kind == DuckovController.UI.Inventory.PaneRegistry.Kind.CharBag);
            if (charBag?.InitialFocus != null) return charBag.InitialFocus.gameObject;

            // First pane in registry order — used by views without CharBag
            // (BlackMarket, BitcoinMiner, FormulasRegister, Quest, Notes, …).
            return _panes.Panes[0].InitialFocus?.gameObject;
        }

        // Resolves deferred page-flip focus on first rebuild after the flip (not a timer).
        // Does NOT gate on !_graphDirty: re-dirty every frame would stall until the 0.30s timeout.
        private void TryFlushPagedFocus()
        {
            if (!_pendingPagedFocus) return;
            bool rebuiltSinceFlip = _frameOfLastRebuild > _pendingPagedFocusFrame;
            bool timedOut = Time.unscaledTime - _pendingPagedFocusAt >= 0.30f;
            if (!rebuiltSinceFlip && !timedOut) return;

            _pendingPagedFocus = false;
            var pane = _panes.Panes.Find(p => p.Kind == _pendingPagedFocusKind);
            if (pane != null)
            {
                var slot = FindEntryAtColumnRow(pane.Root.gameObject,
                    _pendingPagedFocusX, _pendingPagedFocusIsBottom);
                if (slot != null) SetFocus(slot);
            }
        }

        // Waits for FadeGroup show-transition to finish before DiscoverFrom (first frame: alpha=0, ghost-pane state).
        private void TryFlushPaneDiscovery()
        {
            if (_pendingDiscoveryView == null) return;
            if (View.ActiveView != _pendingDiscoveryView) return;
            if (!IsViewSettled(_pendingDiscoveryView)) return;

            _panes.DiscoverFrom(_pendingDiscoveryView);
            _pendingDiscoveryView = null;
            _graphDirty = true;

            if (Log.Verbose)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append($"GFC.PaneDiscovery: view={_activeView?.GetType().Name ?? "null"} panes={_panes.Panes.Count}");
                foreach (var p in _panes.Panes)
                {
                    int childCount = p.Root.childCount;
                    sb.Append($" | {p.Kind}({childCount}ch)=[");
                    int shown = 0;
                    for (int ci = 0; ci < p.Root.childCount && shown < 3; ci++)
                    {
                        var ch = p.Root.GetChild(ci);
                        if (ch != null && ch.gameObject.activeInHierarchy)
                        {
                            if (shown > 0) sb.Append(',');
                            sb.Append(ch.name);
                            shown++;
                        }
                    }
                    sb.Append(']');
                }
                // Graph not yet rebuilt here (graphDirty=true); count after rebuild fires.
                Log.Debug_(sb.ToString());
            }

            UnityEngine.GameObject? postDiscoveryFocus = PickPreferredInitialFocus();
            // Open converge window unconditionally: lazy views (StorageDock) return null here.
            // Without it, late SetFocus is un-windowed → focus lands on stale/mirror then corrects ("mirror, then jump").
            _preferredRefocusUntil = Time.unscaledTime + 0.4f;
            if (postDiscoveryFocus != null)
            {
                SetFocus(postDiscoveryFocus);
                _lastPreferredPick = postDiscoveryFocus;
                Log.Debug_($"Post-discovery focus → {postDiscoveryFocus.name} (refocus window 0.4s)");
            }
            else
            {
                _lastPreferredPick = null;
                Log.Debug_("Post-discovery focus → null (lazy cards; refocus window 0.4s holds until they lay out)");
            }
        }

        private bool HasRecentGamepadActivity(Gamepad pad)
        {
            if (pad.leftStick.ReadValue().sqrMagnitude > 0.04f
                || pad.rightStick.ReadValue().sqrMagnitude > 0.04f
                || pad.dpad.up.isPressed || pad.dpad.down.isPressed
                || pad.dpad.left.isPressed || pad.dpad.right.isPressed
                || pad.buttonSouth.isPressed || pad.buttonNorth.isPressed
                || pad.buttonEast.isPressed || pad.buttonWest.isPressed
                || pad.leftShoulder.isPressed || pad.rightShoulder.isPressed
                || pad.leftTrigger.isPressed || pad.rightTrigger.isPressed)
            {
                _lastGamepadActivity = Time.unscaledTime;
            }
            return Time.unscaledTime - _lastGamepadActivity < 2.0f;
        }

        internal void NotifyInventoryChanged()
        {
            _graphDirty = true;
        }

        private void ResetFocus()
        {
            if (_focused != null)
            {
                ClearFocusVisuals(); // hover-exit + hide outline + drop our selection before recycle
                _focused = null;
            }
            ForgetSlot();
            // Clear router focus so CurrentPrompts empty; else ViewHintPanel lingers over the next view (e.g. ClosureView).
            _router.OnFocusChanged(null);
        }

        // Non-slot focus (button, card) drops the memory so a later refresh can't yank back to an old pane slot.
        private void RememberSlot(GameObject? go)
        {
            if (_suppressRemember) return; // restoring — don't clobber the identity
            if (go == null) return;
            var ie = go.GetComponent<Duckov.UI.InventoryEntry>();
            var inv = ie?.Master?.Target;
            var rt = go.transform as RectTransform;
            if (ie == null || inv == null || rt == null) { ForgetSlot(); return; }
            _rememberedSlotInv = inv;
            _rememberedSlotPos = rt.anchoredPosition;
            _rememberedSlotIndex = ie.Index;
            Log.Debug_($"GFC.RememberSlot: inv={inv.GetHashCode():X} index={ie.Index} pos={_rememberedSlotPos}");
        }

        private void ForgetSlot()
        {
            _rememberedSlotInv   = null;
            _rememberedSlotIndex = -1;
            _restoreWaitUntil    = -1f;
            // _mmHasRememberedPos NOT cleared here: ForgetSlot fires on every non-InventoryEntry SetFocus
            // (every toolbox button nav step) — that would kill the MiniMap drift stamp. Cleared in view-change path.
        }

        private void ForgetMiniMapDriftMemory()
        {
            _mmHasRememberedPos = false;
        }

        // Nearest graph node at the remembered anchoredPosition in the same inventory.
        // bestSqr lets callers require exact cell (≈0) and wait out partial mid-reload panes. Null = no match.
        private GameObject? TryResolveRememberedSlot(out float bestSqr)
        {
            bestSqr = float.PositiveInfinity;
            if (_rememberedSlotInv == null) return null;
            GameObject? best = null;
            int bestIdx = -1;
            int matchingPaneNodes = 0;
            foreach (var go in _graph.Nodes)
            {
                if (go == null || !go.activeInHierarchy) continue;
                var ie = go.GetComponent<Duckov.UI.InventoryEntry>();
                if (ie == null) continue;
                if (!ReferenceEquals(ie.Master?.Target, _rememberedSlotInv)) continue;
                var rt = go.transform as RectTransform;
                if (rt == null) continue;
                matchingPaneNodes++;
                float sqr = ((Vector2)rt.anchoredPosition - _rememberedSlotPos).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = go; bestIdx = ie.Index; }
            }
            Log.Debug_($"GFC.ResolveSlot: wantPos={_rememberedSlotPos} (wasIndex={_rememberedSlotIndex}) " +
                       $"paneNodes={matchingPaneNodes} → {(best != null ? $"index={bestIdx} dist={Mathf.Sqrt(bestSqr):F0}" : "null")}");
            return best;
        }
    }
}
