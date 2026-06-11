using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.MiniGames;
using Duckov.MiniGames.GoldMiner;
using Duckov.MiniGames.GoldMiner.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DuckovController.Bindings
{
    // Console-scoped input gate for arcade mini-games. Drives both input channels while a console is live:
    //   1. Buttons/axis — PlayerInput stays KeyAndMouse on the Deck so bindings don't resolve; we poll
    //      Gamepad.current and call SetButton/SetInputAxis directly each frame.
    //   2. Cursor/click — cursor games (GoldMiner) read UIInputManager.MouseDelta/WasClickedThisFrame via
    //      VirtualCursor; CursorActive/CursorDelta/ClickLatch are overridden by UIInputManagerCursorPatch.
    // Also disables colliding InputActions on enter (best-effort; scheme-proof suppression is MiniGameMenuBlockPatch).
    // Subscribes once to GamingConsole.OnGamingConsoleInteractChanged (Action<bool>: true=enter, false=exit —
    // confirmed by reflection of compiled assembly; decompiled source renders it as Invoke(this)).
    internal sealed class MiniGameInputGate
    {
        // Actions to disable on console-enter so their physical buttons are
        // free for the mini-game / exit. (UI_Cancel/East stays enabled = exit.)
        private static readonly string[] SuppressedActions =
        {
            "UI_Inventory",                 // frees physical Start  → MiniGameStart
            "UI_Map",                       // frees physical Select → MiniGameSelect
            "Interact",                     // frees North/Y         → MiniGameB
            "ItemShortcut1",                // frees the D-pad
            "ItemShortcut2",
            "ItemShortcut_Melee",
            "SwitchWeapon",                 // frees the D-pad
            "SwitchInteractAndBulletType",
        };

        private InputActionAsset? _actions;

        private bool _subscribed;
        private bool _inConsole;

        // Direct-drive state.
        private GamingConsole? _activeConsole;
        private bool _prevA, _prevB, _prevStart, _prevSelect;

        // Cursor-snap nav: D-pad jumps between VirtualCursorTargets, X clicks. Gated to >=2 targets so
        // single-target gameplay (claw's play-area VCT) keeps the analog cursor + X-launch.
        private VirtualCursorTarget? _selectedVct;
        private readonly List<VirtualCursorTarget> _vctList = new();
        private int _vctRefreshFrame = -999;
        private VirtualCursor? _vc;
        private RectTransform? _vcRect;
        private const int DirNone = 0, DirUp = 1, DirDown = 2, DirLeft = 3, DirRight = 4;

        // GoldMiner gameplay (not shop): X launches claw, Y uses selected item. Shop phase = GoldMinerShopUI.enableInput.
        private GoldMiner? _goldMiner;
        private GoldMinerShopUI? _goldShopUi;

        // Hide GoldMiner's drifty visual cursor by disabling Graphic(s) — NOT the GameObject — so
        // VirtualCursor.Update keeps raycasting (item highlight via VCT.onEnter still fires). Restored on exit.
        private readonly List<(Graphic g, bool prev)> _hiddenCursorGraphics = new();
        private bool _cursorHidden;

        // VCT cache mode: -1=none, 0=generic/shop, 1=GoldMiner items-only. Force refresh on flip.
        private int _vctCacheMode = -1;

        // Shop controls (item entries, Continue, Refresh) have a native orange "Highlight" child as
        // NavEntry.selectedIndicator, but only items are under a NavGroup that toggles it — buttons never
        // light up natively. We drive it ourselves; track the last-activated GO to clear it on move.
        private GameObject? _activeShopHighlight;

        // Cursor channel (read by UIInputManagerCursorPatch).
        // Cursor games use UIInputManager.MouseDelta/WasClickedThisFrame, not SetButton.
        // While console is live: right stick → CursorDelta, X → ClickLatch.
        internal static bool CursorActive;
        internal static Vector2 CursorDelta;
        internal static bool ClickLatch;        // one-shot click; consumed by the patch
        private const float CursorSpeed = 30f;  // right-stick → px/frame (×VirtualCursor sensitivity)

        // Read by MiniGameMenuBlockPatch to suppress menu openers; disabling InputActions doesn't stick
        // (gamepad press auto-switches scheme and re-enables the maps).
        internal static bool InConsole;

        // Captured on enter; exit restores only actions we actually disabled (pre-disabled ones stay disabled).
        private readonly Dictionary<string, bool> _priorEnabled = new();

        internal MiniGameInputGate(InputActionAsset? actions)
        {
            _actions = actions;
        }

        // Subscribe once. Safe to call repeatedly (no-op after the first).
        internal void Initialize()
        {
            if (_subscribed) return;
            try
            {
                GamingConsole.OnGamingConsoleInteractChanged += OnInteractChanged;
                _subscribed = true;
                Log.Info("MiniGameInputGate subscribed to GamingConsole.OnGamingConsoleInteractChanged.");
            }
            catch (Exception e)
            {
                Log.Error($"MiniGameInputGate.Initialize failed: {e}");
            }
        }

        // Re-push action asset on reload (replaced when bindings re-apply); suppression uses _actions.
        internal void UpdateConfig(InputActionAsset? actions)
        {
            _actions = actions;
        }

        internal void Shutdown()
        {
            if (_subscribed)
            {
                try { GamingConsole.OnGamingConsoleInteractChanged -= OnInteractChanged; }
                catch (Exception e) { Log.Debug_($"MiniGameInputGate unsubscribe failed: {e.Message}"); }
                _subscribed = false;
            }
            // Never leave the pad suppressed if we go away mid-console.
            if (_inConsole) Exit();
        }

        // true = entered, false = exited (Action<bool> confirmed by reflection).
        private void OnInteractChanged(bool entering)
        {
            try
            {
                if (entering) Enter();
                else Exit();
            }
            catch (Exception e)
            {
                Log.Error($"MiniGameInputGate.OnInteractChanged({entering}) failed: {e}");
            }
        }

        // Watchdog: if HUD is gone while suppressed, force exit-restore. Otherwise drive the game.
        internal void Tick()
        {
            if (!DuckovController.Diagnostics.PerfFlags.MiniGameGate) return;
            if (!_inConsole) return;
            if (!IsConsoleHudLive())
            {
                Log.Warn("MiniGameInputGate watchdog: console HUD gone while suppressed — forcing restore.");
                Exit();
                return;
            }
            DriveMiniGame();
        }

        // Poll pad and direct-drive MiniGame each frame. Axis written every frame; buttons edge-detected
        // so SetButton's one-tick justPressed fires once.
        private void DriveMiniGame()
        {
            var console = _activeConsole;
            if (console == null || !console.Interacting)
            {
                // Self-heal: Enter may have cached null (game not yet created).
                console = null;
                foreach (var c in UnityEngine.Object.FindObjectsOfType<GamingConsole>())
                {
                    if (c != null && c.Interacting) { console = c; break; }
                }
                _activeConsole = console;
            }
            MiniGame? mg = console != null ? console.Game : null;
            var pad = Gamepad.current;
            if (console == null || mg == null || pad == null) return;

            // GoldMiner is fully D-pad/X driven — hide its drifty visual cursor.
            if (GetGoldMiner() != null) HideGoldMinerCursor();

            // GoldMiner title can't be advanced by the pad through SetButton (async
            // read timing); confirm it directly while its screen is up.
            TryAdvanceGoldMinerTitle(pad);

            // Movement: left stick + D-pad (mirrors the stick), clamped per-axis.
            Vector2 move = pad.leftStick.ReadValue() + pad.dpad.ReadValue();
            move.x = Mathf.Clamp(move.x, -1f, 1f);
            move.y = Mathf.Clamp(move.y, -1f, 1f);
            mg.SetInputAxis(move, 0);

            // Cursor channel: when a cursor menu (>=2 targets) is up, the D-pad
            // snaps between targets; otherwise the right stick drives the analog
            // cursor. Either way X is the click.
            bool xNow = pad.buttonWest.isPressed;
            bool xEdge = xNow && !_prevA;
            bool yEdge = pad.buttonNorth.isPressed && !_prevB;
            UpdateCursorNav(pad, xEdge, yEdge);

            // Buttons: X→A, Y→B, Start→Start, Select→Select. East/B stays the
            // native exit (UI_Cancel) and is intentionally not driven here.
            // (xNow reused so the click latch and MiniGameA share one press-edge.)
            EdgeButton(mg, MiniGame.Button.A, xNow, ref _prevA);
            EdgeButton(mg, MiniGame.Button.B, pad.buttonNorth.isPressed, ref _prevB);
            EdgeButton(mg, MiniGame.Button.Start, pad.startButton.isPressed, ref _prevStart);
            EdgeButton(mg, MiniGame.Button.Select, pad.selectButton.isPressed, ref _prevSelect);
        }

        private static void EdgeButton(MiniGame mg, MiniGame.Button button, bool pressed, ref bool prev)
        {
            if (pressed != prev)
            {
                mg.SetButton(button, pressed);
                prev = pressed;
            }
        }

        // GoldMiner title reads GetButtonDown in an async UniTask loop that runs before our SetButton, so
        // the one-frame edge is never caught. Set private `titleConfirmed` directly on X/Start via reflection.
        private static FieldInfo? _gmTitleScreenField;
        private static FieldInfo? _gmTitleConfirmedField;
        private void TryAdvanceGoldMinerTitle(Gamepad pad)
        {
            var gm = GetGoldMiner();
            if (gm == null) return;
            if (!pad.buttonWest.wasPressedThisFrame && !pad.startButton.wasPressedThisFrame) return;
            try
            {
                const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic;
                _gmTitleScreenField ??= typeof(GoldMiner).GetField("titleScreen", f);
                _gmTitleConfirmedField ??= typeof(GoldMiner).GetField("titleConfirmed", f);
                var ts = _gmTitleScreenField?.GetValue(gm) as GameObject;
                if (ts == null || !ts.activeSelf) return;   // only while the title is showing
                _gmTitleConfirmedField?.SetValue(gm, true);
            }
            catch (Exception e) { Log.Debug_($"GoldMiner title advance failed: {e.Message}"); }
        }

        // Cursor channel — three modes (priority order):
        //   1. GoldMiner gameplay: D-pad navigates NavEntry VCTs only; X launches claw, Y uses item.
        //   2. Cursor menu (>=2 targets, e.g. shop): D-pad snaps, X clicks selected.
        //   3. Single-target / pure-button: analog cursor + X-click.
        private void UpdateCursorNav(Gamepad pad, bool xEdge, bool yEdge)
        {
            var gm = GetGoldMiner();

            // Mode 1 — GoldMiner gameplay. Independent of target count so the claw
            // always launches even with 0/1 usable items.
            if (gm != null && !IsGoldMinerShopOpen())
            {
                CursorDelta = Vector2.zero;
                ClickLatch = false;
                ClearShopHighlight();
                var items = GetVcts(pad, itemsOnly: true);
                if (items.Count > 0)
                {
                    // Default to top item so tooltip renders at the top edge, not over the play field.
                    if (_selectedVct == null || !_selectedVct.isActiveAndEnabled || !items.Contains(_selectedVct))
                        _selectedVct = TopmostItem(items);
                    int d = ReadDpadDir(pad);
                    if (d != DirNone)
                    {
                        var next = NearestInDirection(_selectedVct, d, items);
                        if (next != null) _selectedVct = next;
                    }
                    SnapCursorTo(_selectedVct);   // fires the item's onEnter highlight (cursor is hidden)
                }
                else _selectedVct = null;

                if (xEdge)
                {
                    try { gm.LaunchHook(); }
                    catch (Exception e) { Log.Debug_($"LaunchHook failed: {e.Message}"); }
                }
                if (yEdge) SafeClick(_selectedVct);   // use selected item
                return;
            }

            var vcts = GetVcts(pad, itemsOnly: false);
            if (vcts.Count < 2)
            {
                // Mode 3 — single-target / pure-button game: analog cursor + click.
                _selectedVct = null;
                ClearShopHighlight();
                Vector2 rs = pad.rightStick.ReadValue();
                CursorDelta = rs.sqrMagnitude > 0.04f ? rs * CursorSpeed : Vector2.zero;
                if (xEdge) ClickLatch = true;
                return;
            }

            // Mode 2 — GoldMiner shop: D-pad snaps, analog off. Default to first item entry (not Continue/Refresh).
            CursorDelta = Vector2.zero;
            if (_selectedVct == null || !_selectedVct.isActiveAndEnabled || !vcts.Contains(_selectedVct))
                _selectedVct = DefaultShopSelection(vcts);

            int dir = ReadDpadDir(pad);
            if (dir != DirNone)
            {
                var next = NearestInDirection(_selectedVct, dir, vcts);
                if (next != null) _selectedVct = next;
            }

            SnapCursorTo(_selectedVct);           // fires item onEnter → description text
            ApplyShopHighlight(_selectedVct, vcts); // native orange Highlight on the selection
            ClickLatch = false;  // snap mode never fires the analog cursor click

            if (xEdge) SafeClick(_selectedVct);   // buy / select / continue
        }

        // First GoldMinerShopUIEntry, or vcts[0] if stock empty.
        private static VirtualCursorTarget DefaultShopSelection(List<VirtualCursorTarget> vcts)
        {
            foreach (var v in vcts)
                if (v != null && v.GetComponent<GoldMinerShopUIEntry>() != null) return v;
            return vcts[0];
        }

        // Highest item in the gameplay panel (max world Y).
        private static VirtualCursorTarget TopmostItem(List<VirtualCursorTarget> items)
        {
            var best = items[0];
            float bestY = best.transform.position.y;
            for (int i = 1; i < items.Count; i++)
            {
                float y = items[i].transform.position.y;
                if (y > bestY) { bestY = y; best = items[i]; }
            }
            return best;
        }

        // Drive the native orange Highlight on the selection; clear all others.
        // (NavGroup only manages items, so buttons can stay lit after focus moves — we override.)
        private void ApplyShopHighlight(VirtualCursorTarget? selected, List<VirtualCursorTarget> vcts)
        {
            GameObject? want = selected != null ? selected.GetComponent<NavEntry>()?.selectedIndicator : null;
            foreach (var v in vcts)
            {
                if (v == null) continue;
                var ind = v.GetComponent<NavEntry>()?.selectedIndicator;
                if (ind != null && ind != want) SafeSetActive(ind, false);
            }
            if (want != null) SafeSetActive(want, true);
            _activeShopHighlight = want;
        }

        private void ClearShopHighlight()
        {
            if (_activeShopHighlight != null) SafeSetActive(_activeShopHighlight, false);
            _activeShopHighlight = null;
        }

        private static void SafeSetActive(GameObject go, bool value)
        {
            try { if (go != null) go.SetActive(value); } catch { /* destroyed with the game; ignore */ }
        }

        private static int ReadDpadDir(Gamepad pad)
        {
            if (pad.dpad.up.wasPressedThisFrame) return DirUp;
            if (pad.dpad.down.wasPressedThisFrame) return DirDown;
            if (pad.dpad.left.wasPressedThisFrame) return DirLeft;
            if (pad.dpad.right.wasPressedThisFrame) return DirRight;
            return DirNone;
        }

        private static void SafeClick(VirtualCursorTarget? vct)
        {
            if (vct == null) return;
            try { vct.OnClick(); }
            catch (Exception e) { Log.Debug_($"cursor-snap OnClick failed: {e.Message}"); }
        }

        // The active GoldMiner mini-game, or null if the current game isn't one.
        private GoldMiner? GetGoldMiner()
        {
            if (_goldMiner == null) _goldMiner = UnityEngine.Object.FindObjectOfType<GoldMiner>();
            return _goldMiner;
        }

        // True while GoldMiner's between-level shop is accepting input (buy phase),
        // as opposed to active play. Cached incl. inactive so we don't re-scan the
        // scene every frame during play (the shop UI is inactive then).
        private bool IsGoldMinerShopOpen()
        {
            if (_goldShopUi == null) _goldShopUi = UnityEngine.Object.FindObjectOfType<GoldMinerShopUI>(true);
            return _goldShopUi != null && _goldShopUi.isActiveAndEnabled && _goldShopUi.enableInput;
        }

        // Active VCTs; refreshed on D-pad press, mode flip, or every ~8 frames.
        //   itemsOnly=true  → NavEntry-bearing panel targets only (excludes play-area VCT so nav stays off the game view).
        //   itemsOnly=false → all active targets, minus non-shop VCTs while shop is open (gameplay icons stay active but hidden).
        private List<VirtualCursorTarget> GetVcts(Gamepad pad, bool itemsOnly)
        {
            bool dpadPress = pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame
                || pad.dpad.left.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame;
            int mode = itemsOnly ? 1 : 0;
            if (dpadPress || mode != _vctCacheMode || Time.frameCount - _vctRefreshFrame >= 8)
            {
                _vctRefreshFrame = Time.frameCount;
                _vctCacheMode = mode;
                _vctList.Clear();
                bool shop = !itemsOnly && IsGoldMinerShopOpen();
                foreach (var v in UnityEngine.Object.FindObjectsOfType<VirtualCursorTarget>())
                {
                    if (v == null || !v.isActiveAndEnabled) continue;
                    if (itemsOnly && v.GetComponent<NavEntry>() == null) continue;
                    if (shop && !IsShopVct(v)) continue;
                    _vctList.Add(v);
                }
            }
            return _vctList;
        }

        // True if this target is a GoldMiner shop control (buy entry / Continue /
        // Refresh) — the VCT sits on the same GameObject as those components.
        private static bool IsShopVct(VirtualCursorTarget v)
        {
            return v.GetComponent<GoldMinerShopUIEntry>() != null
                || v.GetComponent<GoldMinerShopUIContinueBtn>() != null
                || v.GetComponent<GoldMinerShopUIRefreshBtn>() != null;
        }

        // Snap VirtualCursor onto target so the game's raycast fires onEnter highlight/description.
        private void SnapCursorTo(VirtualCursorTarget? vct)
        {
            if (vct == null) return;
            if (_vc == null) { _vc = UnityEngine.Object.FindObjectOfType<VirtualCursor>(); _vcRect = null; }
            if (_vc == null) return;
            if (_vcRect == null)
            {
                try
                {
                    var f = typeof(VirtualCursor).GetField("rectTransform",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    _vcRect = f?.GetValue(_vc) as RectTransform;
                }
                catch { /* best-effort */ }
            }
            if (_vcRect == null) return;
            try { _vcRect.position = vct.transform.position; }
            catch (Exception e) { Log.Debug_($"cursor-snap position failed: {e.Message}"); }
        }

        // Nearest target in direction (along-axis + off-axis penalty), null if none.
        private static VirtualCursorTarget? NearestInDirection(VirtualCursorTarget? from, int dir,
            List<VirtualCursorTarget> all)
        {
            if (from == null) return null;
            Vector2 c = from.transform.position;
            VirtualCursorTarget? best = null;
            float bestScore = float.MaxValue;
            foreach (var v in all)
            {
                if (v == null || v == from) continue;
                Vector2 d = (Vector2)v.transform.position - c;
                float along, off;
                switch (dir)
                {
                    case DirUp:    if (d.y <= 1f) continue; along = d.y; off = Mathf.Abs(d.x); break;
                    case DirDown:  if (d.y >= -1f) continue; along = -d.y; off = Mathf.Abs(d.x); break;
                    case DirLeft:  if (d.x >= -1f) continue; along = -d.x; off = Mathf.Abs(d.y); break;
                    default:       if (d.x <= 1f) continue; along = d.x; off = Mathf.Abs(d.y); break; // DirRight
                }
                float score = along + off * 2f;
                if (score < bestScore) { bestScore = score; best = v; }
            }
            return best;
        }

        // Disable Graphic(s), not the GO — Update() keeps raycasting. Idempotent; captures state for restore.
        private void HideGoldMinerCursor()
        {
            if (_cursorHidden) return;
            if (_vc == null) { _vc = UnityEngine.Object.FindObjectOfType<VirtualCursor>(); _vcRect = null; }
            if (_vc == null) return;
            try
            {
                foreach (var g in _vc.GetComponentsInChildren<Graphic>(true))
                {
                    if (g == null) continue;
                    _hiddenCursorGraphics.Add((g, g.enabled));
                    g.enabled = false;
                }
                _cursorHidden = true;
            }
            catch (Exception e) { Log.Debug_($"hide GoldMiner cursor failed: {e.Message}"); }
        }

        private void RestoreCursorVisual()
        {
            foreach (var (g, prev) in _hiddenCursorGraphics)
            {
                if (g == null) continue;
                try { g.enabled = prev; } catch { /* destroyed with the game; ignore */ }
            }
            _hiddenCursorGraphics.Clear();
            _cursorHidden = false;
        }

        private void Enter()
        {
            if (_inConsole) return;
            _inConsole = true;
            CursorActive = true;
            InConsole = true;

            // Cache active console for Tick (event gives no reference).
            _activeConsole = null;
            foreach (var c in UnityEngine.Object.FindObjectsOfType<GamingConsole>())
            {
                if (c != null && c.Interacting) { _activeConsole = c; break; }
            }
            _prevA = _prevB = _prevStart = _prevSelect = false;

            var actions = _actions;
            if (actions == null)
            {
                Log.Warn("MiniGameInputGate.Enter: actions is null, cannot suppress.");
                return;
            }

            // Disable colliding actions; direct-drive reads Gamepad.current so no bindings needed.
            _priorEnabled.Clear();
            foreach (var name in SuppressedActions)
            {
                var action = actions.FindAction(name, throwIfNotFound: false);
                if (action == null)
                {
                    Log.Debug_($"MiniGameInputGate: action '{name}' not found, skipping suppress.");
                    continue;
                }
                _priorEnabled[name] = action.enabled;
                if (action.enabled) action.Disable();
            }

            Log.Info($"MiniGameInputGate: entered console — suppressed {_priorEnabled.Count} actions.");
        }

        private void Exit()
        {
            // Always attempt restore even if we never fully entered; idempotent.
            var actions = _actions;

            // Re-enable exactly the actions we disabled (honor prior state).
            if (actions != null)
            {
                foreach (var kv in _priorEnabled)
                {
                    if (!kv.Value) continue;  // was already disabled on enter — leave it
                    var action = actions.FindAction(kv.Key, throwIfNotFound: false);
                    if (action == null) continue;
                    if (!action.enabled) action.Enable();
                }
            }

            int restored = 0;
            foreach (var kv in _priorEnabled) if (kv.Value) restored++;
            if (_inConsole)
                Log.Info($"MiniGameInputGate: exited console — restored {restored} actions.");

            _priorEnabled.Clear();
            _inConsole = false;
            _activeConsole = null;
            _prevA = _prevB = _prevStart = _prevSelect = false;
            CursorActive = false;
            CursorDelta = Vector2.zero;
            ClickLatch = false;
            InConsole = false;
            RestoreCursorVisual();
            ClearShopHighlight();
            _selectedVct = null;
            _vc = null;
            _vcRect = null;
            _vctList.Clear();
            _vctCacheMode = -1;
            _goldMiner = null;
            _goldShopUi = null;
        }

        // True while the GamingConsoleHUD exists and its GameObject is active.
        private static bool IsConsoleHudLive()
        {
            var hud = GetHud();
            if (hud == null) return false;
            try { return hud.isActiveAndEnabled || hud.gameObject.activeInHierarchy; }
            catch { return false; }
        }

        // Try Instance property, then _instance_cache field (both private static), then FindObjectOfType.
        private static GamingConsoleHUD? GetHud()
        {
            try
            {
                var t = typeof(GamingConsoleHUD);
                var flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

                var prop = t.GetProperty("Instance", flags);
                if (prop != null && prop.GetValue(null) is GamingConsoleHUD viaProp && viaProp != null)
                    return viaProp;

                var field = t.GetField("_instance_cache", flags);
                if (field != null && field.GetValue(null) is GamingConsoleHUD viaField && viaField != null)
                    return viaField;
            }
            catch (Exception e)
            {
                Log.Debug_($"MiniGameInputGate: HUD reflection failed: {e.Message}");
            }
            return UnityEngine.Object.FindObjectOfType<GamingConsoleHUD>();
        }
    }
}
