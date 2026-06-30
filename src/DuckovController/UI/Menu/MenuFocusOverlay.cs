using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.UI.Animations;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DuckovController.UI.Menu
{
    // Focus driver for non-View menus. Event-driven activation via PauseMenu.onPauseMenuOn/Off
    // and MainMenu.OnMainMenuAwake/Destroy — no FindObjectsOfType. Chevron tracks _focused by
    // reference. ConfirmDialogue: A=Confirm, B=Cancel, no chevron.
    internal sealed partial class MenuFocusOverlay : MonoBehaviour
    {
        internal static MenuFocusOverlay? Instance { get; private set; }
        internal bool IsActive => _menuRoot != null;

        public DuckovController.UI.Common.MenuScope ActiveScope { get; private set; }
            = DuckovController.UI.Common.MenuScope.None;

        public event System.Action<DuckovController.UI.Common.MenuScope>? OnScopeChanged;

        // Exposed for cosmetic injectors (e.g. MenuBackGlyphInjector) to search the active subtree.
        internal Transform? CurrentMenuRoot => _menuRoot;

        private void SetActiveScope(DuckovController.UI.Common.MenuScope s)
        {
            if (s == ActiveScope) return;
            ActiveScope = s;
            OnScopeChanged?.Invoke(s);
        }

        private Transform? _menuRoot;
        private bool _pauseShown;
        private bool _mainMenuActive;
        private Transform? _mainMenuRoot;
        // MainMenu.Awake fires immediately but canvases aren't ready until after "click to continue".
        // Poll at 0.5 Hz until we get a root.
        private float _lastMainMenuCanvasProbe = -10f;
        private const float MainMenuCanvasProbeIntervalSec = 0.5f;
        private float _lastGenericPanelProbe = -10f;
        private const float GenericPanelProbeIntervalSec = 0.3f;
        // Panels to attach to when no PauseMenu/MainMenu is active (e.g. PREPARE scene).
        private static readonly string[] GenericPanelNames = { "DifficultySelection" };

        private Selectable? _focused;
        private bool _justActivated;
        // Usually _menuRoot; switches to a deeper modal sub-panel (Settings/Credits/Options).
        private Transform? _effectiveRoot;
        // The row that opened the active dropdown popup: set on switch INTO a popup, consumed on switch OUT —
        // restores the chevron there on popup close instead of re-picking the first row in the tab.
        private Selectable? _focusBeforePopup;

        // Hold-to-confirm (e.g. DeleteData): fire pointerDown on A press, pointerUp on release.
        private GameObject? _holdTarget;
        private bool _holdingSubmit;

        // ModEntry type — resolved lazily on first ModManagerUI interaction.
        private static System.Type? _modEntryType;
        private static bool _modEntryMethodsLogged;

        // CustomFaceTabs type — lazily resolved; any active instance = CC open.
        private static System.Type? _customFaceTabsType;
        // CC swatch cursor: tracks the color-picker grid independently of the top-level chevron.
        private Selectable? _ccSwatchFocused;
        private FocusOutlineOverlay? _ccSwatchOutline;
        // Maps top-column index → last swatch index so tab-cycling restores position.
        private readonly System.Collections.Generic.Dictionary<int, int> _ccSwatchLastIdxByTab = new();

        // Color-picker sub-panel (Item 1): when a CustomFaceUIColorPicker's buttonParent is open, nav is
        // locked INTO the picker. A=apply+keep-open, X=apply+close, B=revert(open-time color)+close.
        private Component? _ccOpenPicker;                 // the live CustomFaceUIColorPicker (reflected)
        private Color _ccPickerOpenColor;                 // CurrentColor captured when the picker opened (B reverts here)
        private Selectable? _ccPickerFocused;             // focused swatch inside the open picker
        private NavDir? _ccPickerHeldDir;                 // direction currently held (for hold-to-repeat)
        private System.Collections.Generic.List<Selectable>? _ccPickerSwatches;
        private System.Collections.Generic.List<System.Collections.Generic.List<Selectable>>? _ccPickerRows;
        private FocusOutlineOverlay? _ccPickerOutline;
        private DuckovController.UI.Prompts.MenuHintPanel? _ccPickerHints;
        // Reflected picker members (resolved once).
        private static System.Type? _ccPickerType;
        private static System.Reflection.FieldInfo? _ccPickerButtonParentField;
        private static System.Reflection.PropertyInfo? _ccPickerCurrentColorProp;
        private static System.Reflection.MethodInfo? _ccPickerSetColorMethod;
        private static System.Reflection.PropertyInfo? _ccPickerBtnColorProp;
        // All picker components under _ccRoot (refreshed with the CC cache).
        private static System.Collections.Generic.List<Component>? _ccPickers;

        // RS rotation: cached DragHandler is the pointer-event target; active while RS-X > deadzone.
        private static GameObject? _ccDragHandlerGo;
        private bool _ccRotating;
        private Vector2 _ccRotatePos;
        private Vector2 _ccRotatePressPos;
        // Hysteresis: start only above Enter, stop below Exit — prevents per-frame begin/end micro-stutter at edge.
        private const float CcRotateDeadzoneEnter = 0.20f;
        private const float CcRotateDeadzoneExit  = 0.08f;
        // px/sec "drag" at full deflection (game spin is delta-based; matches a brisk mouse drag).
        private const float CcRotatePxPerSec = 1200f;
        // Reused PED to avoid per-frame GC.
        private PointerEventData? _ccRotatePed;

        // CC sticky caches — set once on activation, cleared when root goes inactive.
        // Per-frame nav reads only these fields; no GetComponentsInChildren on hot path.
        private static Transform? _ccRoot;
        private static System.Collections.Generic.List<Selectable>? _ccTopColumn;
        // The 8 *Panel transforms (MainPanel + 7 named).
        private static System.Collections.Generic.List<Transform>? _ccPanelTransforms;
        // Bitmask of active panels; swatch grid rebuilds only when this changes.
        private static int _ccActivePanelMask;
        private static System.Collections.Generic.List<Selectable>? _ccSwatchCol;
        private static System.Collections.Generic.List<System.Collections.Generic.List<Selectable>>? _ccGridRows;
        // Throttle: FindObjectOfType for first detection shouldn't run every frame after CC closes.
        private float _ccNextProbeAt = 0f;

        // Mod reorder refocus: track pre-reorder column index and re-pin for a few frames
        // across layout repaint. Index (not component ref) works for both rebind-data and
        // reparent-GameObject reorder models.
        private int _modRefocusTargetIdx = -1;
        private int _modRefocusFramesLeft;

        private GameObject? _chevronGo;
        private RectTransform? _chevronRt;
        private TriangleGraphic? _chevronGraphic;

        private float _navHoldStarted;
        private float _lastNavAt = -10f;
        private int _navHoldSteps; // hold-repeat acceleration (parity with GridFocusController)
        // Reads config so menu nav cadence matches grid controller / MFC. Was hardcoded 0.35/0.12 outlier.
        private static float RepeatDelay => DuckovController.UI.Settings.SettingsBridge.Cfg?.Ui?.NavRepeatDelaySec ?? 0.35f;
        private static float RepeatRate  => DuckovController.UI.Settings.SettingsBridge.Cfg?.Ui?.NavRepeatRateSec ?? 0.08f;

        // Left stick as a virtual d-pad so menu/overlay surfaces accept stick AND d-pad identically.
        // Sampled exactly once per frame at the top of HandleNav; the Dir* helpers OR it with the
        // physical d-pad. Menus always unify (no StickAsDpad gate — that flag is gameplay-grid only).
        private readonly StickDpad _menuStick = new StickDpad();

        private bool DirEdge(Gamepad pad, NavDir d) => DpadButton(pad, d).wasPressedThisFrame || _menuStick.Edge(d);
        private bool DirHeld(Gamepad pad, NavDir d) => DpadButton(pad, d).isPressed || _menuStick.Held(d);

        private static UnityEngine.InputSystem.Controls.ButtonControl DpadButton(Gamepad pad, NavDir d)
            => d switch
            {
                NavDir.Up   => pad.dpad.up,
                NavDir.Down => pad.dpad.down,
                NavDir.Left => pad.dpad.left,
                _           => pad.dpad.right,
            };


        private bool _suppressedNavEvents;
        private bool _savedSendNavigationEvents;

        // Separate hold tracking for horizontal slider axis so U/D and L/R repeat independently.
        private float _hAxisHoldStarted;
        private float _lastHAxisAt = -10f;

        // Dpad-up from Confirm restores to previously focused card (not leftmost).
        private int _lastDifficultyCardIdx = 0;

        // Cached active HoveringIndicator so we can turn it off when focus moves.
        private Transform? _activeHoverIndicator;

        private void Awake() { Instance = this; }

        private void OnEnable()
        {
            try
            {
                PauseMenu.onPauseMenuOn   += HandlePauseOn;
                PauseMenu.onPauseMenuOff  += HandlePauseOff;
                MainMenu.OnMainMenuAwake  += HandleMainMenuAwake;
                MainMenu.OnMainMenuDestroy += HandleMainMenuDestroy;
                Log.Info("MenuOverlay OnEnable: subscribed to PauseMenu/MainMenu events");
            }
            catch (Exception e) { Log.Error($"MenuFocusOverlay: subscribe failed: {e.Message}"); }

            // Catch the case where main-menu was already active when this component enabled.
            try
            {
                var existing = UnityEngine.Object.FindObjectOfType<MainMenu>();
                Log.Info($"MenuOverlay OnEnable: FindObjectOfType<MainMenu>() = {(existing != null ? existing.gameObject.name : "<null>")}");
                if (existing != null) HandleMainMenuAwakeFor(existing);
            }
            catch (Exception e) { Log.Warn($"MenuOverlay OnEnable MainMenu probe: {e.Message}"); }
            try
            {
                bool shown = PauseMenu.Instance != null && PauseMenu.Instance.Shown;
                Log.Info($"MenuOverlay OnEnable: PauseMenu.Instance={(PauseMenu.Instance != null ? "set" : "<null>")} Shown={shown}");
                if (shown) HandlePauseOn();
            }
            catch (Exception e) { Log.Warn($"MenuOverlay OnEnable PauseMenu probe: {e.Message}"); }
        }

        private void OnDisable()
        {
            try
            {
                PauseMenu.onPauseMenuOn   -= HandlePauseOn;
                PauseMenu.onPauseMenuOff  -= HandlePauseOff;
                MainMenu.OnMainMenuAwake  -= HandleMainMenuAwake;
                MainMenu.OnMainMenuDestroy -= HandleMainMenuDestroy;
            }
            catch { }
            ClearAll();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            ClearAll();
        }

        private void ClearAll()
        {
            RestoreNavEvents();
            DestroyChevron();
            ClearConfirmGlyphs();
            ClearModWarnGlyph();
            ClearDifficultyConfirmGlyph();
            ExitPickerMode();
            if (_ccPickerOutline != null) { Destroy(_ccPickerOutline); _ccPickerOutline = null; }
            if (_ccPickerHints != null) { _ccPickerHints.Destroy(); _ccPickerHints = null; }
            _menuRoot = null;
            _effectiveRoot = null;
            _focused = null;
            _pauseShown = false;
            _mainMenuActive = false;
            _mainMenuRoot = null;
        }

        private void HandlePauseOn()
        {
            _pauseShown = true;
            if (PauseMenu.Instance != null) _menuRoot = PauseMenu.Instance.transform;
            _justActivated = true;
            _focused = null;
        }

        private void HandlePauseOff()
        {
            _pauseShown = false;
            _focused = null;
            HideChevron();
            RestoreNavEvents();
            // Fall back to main menu if active; otherwise disengage.
            _menuRoot = _mainMenuActive ? _mainMenuRoot : null;
            if (_menuRoot != null) _justActivated = true;
        }

        private void HandleMainMenuAwake() { HandleMainMenuAwakeFor(null); }
        private void HandleMainMenuAwakeFor(MainMenu? known)
        {
            _mainMenuActive = true;
            _mainMenuRoot = FindMostPopulatedMenuCanvas();
            Log.Info($"MenuOverlay HandleMainMenuAwake: _mainMenuRoot={(_mainMenuRoot != null ? _mainMenuRoot.gameObject.name : "<null>")} _pauseShown={_pauseShown}");
            if (!_pauseShown && _mainMenuRoot != null)
            {
                _menuRoot = _mainMenuRoot;
                _focused = null;
                _justActivated = true;
                Log.Info($"MenuOverlay → _menuRoot set to {_menuRoot.gameObject.name}, justActivated=true");
            }
            // If null here, Update polls at 0.5 Hz until the player clicks through the splash.
        }

        private void HandleMainMenuDestroy()
        {
            _mainMenuActive = false;
            _mainMenuRoot = null;
            if (!_pauseShown) { _menuRoot = null; _focused = null; HideChevron(); RestoreNavEvents(); }
        }

        // Heavy scans (effective-root + scope + ConfirmDialogue) run on input frames or 10 Hz heartbeat.
        private float _lastHeavyScan = -10f;
        private const float HeavyScanIntervalSec = 0.1f;

        // True if pad had actionable input this frame. Stick gate 0.5 mag prevents drift defeating throttle.
        private static bool AnyPadActivityThisFrame()
        {
            var pad = Gamepad.current;
            if (pad == null) return false;
            return pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame
                || pad.buttonNorth.wasPressedThisFrame || pad.buttonWest.wasPressedThisFrame
                || pad.startButton.wasPressedThisFrame || pad.selectButton.wasPressedThisFrame
                || pad.leftShoulder.wasPressedThisFrame || pad.rightShoulder.wasPressedThisFrame
                || pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame
                || pad.dpad.left.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame
                || pad.leftStick.ReadValue().sqrMagnitude > 0.25f
                || pad.rightStick.ReadValue().sqrMagnitude > 0.25f;
        }

        private void Update()
        {
            if (!DuckovController.Diagnostics.PerfFlags.MenuOverlay) return;
            // Deferred canvas search at 0.5 Hz until root found (covers "click to continue" splash).
            if (_mainMenuActive && _mainMenuRoot == null
                && Time.unscaledTime - _lastMainMenuCanvasProbe >= MainMenuCanvasProbeIntervalSec)
            {
                _lastMainMenuCanvasProbe = Time.unscaledTime;
                int canvasesSeen, totalButtons;
                _mainMenuRoot = FindMostPopulatedMenuCanvas(out canvasesSeen, out totalButtons);
                Log.Info($"MenuOverlay canvas probe: canvases={canvasesSeen} totalInteractableButtons={totalButtons} result={(_mainMenuRoot != null ? _mainMenuRoot.gameObject.name : "<null>")}");
                if (_mainMenuRoot != null && !_pauseShown)
                {
                    _menuRoot = _mainMenuRoot;
                    _focused = null;
                    _justActivated = true;
                    Log.Info($"MenuOverlay → _menuRoot set to {_menuRoot.gameObject.name}, justActivated=true");
                }
            }

            // Generic-panel probe at 0.3 Hz for scenes without PauseMenu/MainMenu (PREPARE, CC, etc).
            if (!_pauseShown && !_mainMenuActive
                && Time.unscaledTime - _lastGenericPanelProbe >= GenericPanelProbeIntervalSec)
            {
                _lastGenericPanelProbe = Time.unscaledTime;
                // Skip the expensive DifficultySelection Canvas scan in gameplay (it only appears
                // pre-character) — TryFindGenericPanelRoot's FindObjectsOfType<Canvas> was a ~25 ms
                // base hitch. BUT the character creator IS reachable in-base via Interact_CustomFace
                // (the mirror) WITH a controlling character, so don't gate CC out: run the probe when
                // no character exists, OR CC is currently active, OR we still hold a generic root that
                // went inactive (so the disengage/cleanup branch below can drop it). IsCharacterCreatorActive
                // is a cheap sticky check — the FindObjectOfType<CustomFaceTabs> only fires at 0.5 Hz
                // while CC is closed.
                bool holdsStaleGenericRoot = _menuRoot != null
                    && !ReferenceEquals(_menuRoot, _mainMenuRoot)
                    && !_menuRoot.gameObject.activeInHierarchy;
                if (LevelManager.Instance?.ControllingCharacter == null
                    || IsCharacterCreatorActive()
                    || holdsStaleGenericRoot)
                {
                    var genericRoot = TryFindGenericPanelRoot();
                    if (genericRoot != null && !ReferenceEquals(_menuRoot, genericRoot))
                    {
                        _menuRoot = genericRoot;
                        _focused = null;
                        _justActivated = true;
                        Log.Info($"MenuOverlay → generic panel root set to {genericRoot.gameObject.name}.");
                    }
                    else if (genericRoot == null && _menuRoot != null
                        && !ReferenceEquals(_menuRoot, _mainMenuRoot)
                        && _menuRoot != null && !_menuRoot.gameObject.activeInHierarchy)
                    {
                        _menuRoot = null;
                        _focused = null;
                        HideChevron();
                        RestoreNavEvents();
                    }
                }
            }

            // BUG-1: B-in-pause Resume closes menu without firing onPauseMenuOff → overlay stays
            // phantom-active. Self-correct via GameManager.Paused (authoritative live read).
            if (_pauseShown)
            {
                bool actuallyPaused;
                try { actuallyPaused = GameManager.Paused; }
                catch { actuallyPaused = false; }
                if (!actuallyPaused)
                {
                    Log.Debug_("MenuOverlay: stale _pauseShown (game not paused) — running HandlePauseOff cleanup");
                    HandlePauseOff();
                }
            }

            // ConfirmDialogue lives OUTSIDE menu root — check globally before the IsActive gate.
            // Don't restore nav events here (could double-fire vanilla submit on the menu behind it).
            var globalConfirm = TryFindActiveConfirmDialogueGlobal();
            if (globalConfirm != null)
            {
                EnsureConfirmGlyphs(globalConfirm);
                HideChevron();
                HandleConfirmDialog(globalConfirm);
                return;
            }
            if (_glyphedConfirm != null) ClearConfirmGlyphs();

            var modWarn = TryFindActiveModWarning();
            if (modWarn != null) { HandleModWarning(modWarn); return; }
            if (_modWarnGlyph != null) ClearModWarnGlyph();

            // ClosureView: unmanaged vanilla view — just glyph Continue with A (self-gating).
            // A-confirm via direct click: bypasses EventSystem Submit which dies when InputActived=false.
            EnsureClosureGlyph();
            if (_closureGlyphedBtn != null && Gamepad.current != null
                && Gamepad.current.buttonSouth.wasPressedThisFrame)
                PointerEventDispatcher.Click(_closureGlyphedBtn.gameObject);

            if (!IsActive) { RestoreNavEvents(); return; }
            // Pause over a View: don't bail to View when pause is shown — it must take precedence.
            // GridFocusController yields dpad while paused (timeScale==0) so they don't fight.
            try { if (Duckov.UI.View.ActiveView != null && !_pauseShown) { HideChevron(); RestoreNavEvents(); return; } }
            catch { }

            SuppressNavEvents();

            // Heavy scans on input frames or 10 Hz heartbeat; idle frames use cached state.
            bool doHeavy = _justActivated
                || _effectiveRoot == null
                || _glyphedConfirm != null
                || AnyPadActivityThisFrame()
                || (Time.unscaledTime - _lastHeavyScan >= HeavyScanIntervalSec);
            if (doHeavy)
            {
            _lastHeavyScan = Time.unscaledTime;

            // Scoped ConfirmDialogue takes precedence over normal nav.
            var confirm = TryFindActiveConfirmDialogueInRoot();
            if (confirm != null) { HandleConfirmDialog(confirm); return; }

            // Track modal sub-panel changes so B-cancel and focus follow Credits/Settings/Options.
            var newEffective = ResolveEffectiveRoot();
            if (!ReferenceEquals(newEffective, _effectiveRoot))
            {
                bool nowIsPopup = newEffective != null && _menuRoot != null
                    && ReferenceEquals(FindExpandedDropdownPopup(_menuRoot), newEffective);
                bool wasInPopup = _focusBeforePopup != null;

                if (!wasInPopup && nowIsPopup)
                {
                    // Save pre-popup row to restore on popup close.
                    _focusBeforePopup = _focused;
                }

                Log.Info($"MenuOverlay: effective root → {(newEffective != null ? newEffective.gameObject.name : "<null>")}");
                _effectiveRoot = newEffective;
                _focused = null;
                _justActivated = true;

                if (wasInPopup && !nowIsPopup)
                {
                    // Popup closed — restore pre-popup row. Without this, PickInitialFocusInRoot
                    // teleports chevron to the first row in the tab.
                    if (_focusBeforePopup != null
                        && IsSelectableUsable(_focusBeforePopup)
                        && newEffective != null
                        && _focusBeforePopup.transform.IsChildOf(newEffective))
                    {
                        _focused = _focusBeforePopup;
                        _justActivated = false;
                        Log.Info($"MenuOverlay: restored focus to {_focused.gameObject.name} after popup close.");
                    }
                    _focusBeforePopup = null;
                }
            }

            // Compute ActiveScope: after ResolveEffectiveRoot, before handler dispatch.
            {
                DuckovController.UI.Common.MenuScope scope;
                if (_effectiveRoot == null && _menuRoot == null && _mainMenuRoot == null)
                    scope = DuckovController.UI.Common.MenuScope.None;
                else if ((_menuRoot ?? _effectiveRoot) is Transform dropRoot && FindExpandedDropdownPopup(dropRoot) != null)
                    scope = DuckovController.UI.Common.MenuScope.DropdownPopup;
                else if (_ccRoot != null || IsCharacterCreatorActive())
                    scope = DuckovController.UI.Common.MenuScope.CharacterCreator;
                else if (IsInsideCustomDifficultyPanel())
                    scope = DuckovController.UI.Common.MenuScope.CustomDifficulty;
                else if (IsInsideDifficultySelection())
                    scope = DuckovController.UI.Common.MenuScope.DifficultySelection;
                else if (IsInsideCreditsPanel())
                    scope = DuckovController.UI.Common.MenuScope.Credits;
                else if (IsInsideModManager())
                    scope = DuckovController.UI.Common.MenuScope.ModManager;
                else if (_effectiveRoot != null && IsOptionsTabContent(_effectiveRoot))
                    scope = DuckovController.UI.Common.MenuScope.OptionsTab;
                else
                    scope = DuckovController.UI.Common.MenuScope.Generic;
                SetActiveScope(scope);
            }
            } // end doHeavy idle-skip gate

            // Repick on fresh activation or destroyed focus. Don't repick on transient
            // non-interactable (TMP_Dropdown transition) — would teleport to first row.
            bool focusedDestroyed = _focused == null
                || _focused.gameObject == null;
            if (_justActivated || focusedDestroyed)
            {
                Selectable? previous = _focused;
                _focused = PickInitialFocusInRoot();
                _justActivated = false;
                if (_focused != null) ClearVanillaHoverAndSelection(previous);
            }
            if (_focused == null)
            {
                HideChevron();
                // Credits panel: initial-focus skips the sole back button (_focused stays null).
                // Still need HandleCreditsScroll and HandleCancel to run — dispatch them here.
                var padNoFocus = Gamepad.current;
                if (padNoFocus != null)
                {
                    HandleCreditsScroll(padNoFocus);
                    HandleCancel(padNoFocus);
                }
                return;
            }
            // Skip this frame if focused is mid-transition (transient unusable).
            if (!IsSelectableUsable(_focused)) { return; }

            // Mod reorder: re-pin chevron for a few frames until layout repaint settles.
            if (_modRefocusFramesLeft > 0 && _modRefocusTargetIdx >= 0)
            {
                ApplyModRefocus();
                _modRefocusFramesLeft--;
                if (_modRefocusFramesLeft <= 0) _modRefocusTargetIdx = -1;
            }

            var pad = Gamepad.current;
            if (pad != null)
            {
                HandleNav(pad);
                HandleSubmit(pad);
                HandleHoldRelease(pad);
                HandleCancel(pad);
            }
            UpdateChevron();
            UpdatePickerHints();
        }

        // Track suppressed EventSystem ID: a restore against a different instance (scene reload)
        // would leave navEvents globally off — verbose-only logging detects this.
        private int _suppressedEsId;

        private void SuppressNavEvents()
        {
            if (_suppressedNavEvents) return;
            var es = EventSystem.current;
            if (es == null) return;
            _savedSendNavigationEvents = es.sendNavigationEvents;
            es.sendNavigationEvents = false;
            _suppressedNavEvents = true;
            _suppressedEsId = es.GetInstanceID();
            Log.Debug_($"[navEvents] suppressed on es={_suppressedEsId} (saved={_savedSendNavigationEvents})");
        }

        private void RestoreNavEvents()
        {
            if (!_suppressedNavEvents) return;
            var es = EventSystem.current;
            if (es != null)
            {
                if (es.GetInstanceID() != _suppressedEsId)
                    Log.Debug_($"[navEvents] RESTORE MISMATCH: suppressed es={_suppressedEsId} "
                        + $"but current es={es.GetInstanceID()} — original EventSystem may be leaking navEvents=false");
                es.sendNavigationEvents = _savedSendNavigationEvents;
                Log.Debug_($"[navEvents] restored on es={es.GetInstanceID()} (→{_savedSendNavigationEvents})");
            }
            _suppressedNavEvents = false;
        }

        private static bool IsSelectableUsable(Selectable? s)
        {
            if (s == null) return false;
            if (!s.interactable) return false;
            if (!s.gameObject.activeInHierarchy) return false;
            var rt = s.transform as RectTransform;
            if (rt == null) return false;
            if (rt.rect.width <= 1f || rt.rect.height <= 1f) return false;
            return true;
        }
    }

    // Minimal right-pointing triangle Graphic — no font, no sprite asset.
    internal sealed class TriangleGraphic : Graphic
    {
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            var c = color;

            var topLeft    = UIVertex.simpleVert; topLeft.color = c;
            topLeft.position    = new Vector3(r.xMin, r.yMax);
            var bottomLeft = UIVertex.simpleVert; bottomLeft.color = c;
            bottomLeft.position = new Vector3(r.xMin, r.yMin);
            var rightTip   = UIVertex.simpleVert; rightTip.color = c;
            rightTip.position   = new Vector3(r.xMax, (r.yMin + r.yMax) * 0.5f);

            vh.AddVert(topLeft);
            vh.AddVert(bottomLeft);
            vh.AddVert(rightTip);
            vh.AddTriangle(0, 1, 2);
        }
    }
}
