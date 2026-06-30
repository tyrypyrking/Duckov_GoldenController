using System;
using System.IO;
using System.Reflection;
using DuckovController.Bindings;
using DuckovController.Config;
using DuckovController.Patches;
using DuckovController.UI;
using DuckovController.UI.Inventory;
using Duckov.Modding;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuckovController
{
    // Mod entry. Loads config, applies Harmony patches, injects gamepad
    // bindings into the game's existing InputActionAsset, and spawns the
    // GridFocusController that drives UI navigation.
    public sealed class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        private const string HarmonyId = "com.goldencontroller.efd";

        // object to prevent field-type resolution during ModManager.ActivateMod probe (Harmony may not
        // be in AppDomain yet); method bodies are JIT-lazy so the cast resolves at use time.
        private object? _harmony;
        private ControllerConfig? _config;
        private string? _settingsPath;
        private bool _bindingsApplied;
        private bool _wasPausedLastFrame;
        private GridFocusController? _focusController;
        private MenuFocusController? _menuController;
        private DuckovController.Diagnostics.UIStructureDumper? _uiDumper;
        private DuckovController.Heal.SmartHealController? _healController;
        private DuckovController.UI.Menu.MenuFocusOverlay? _menuOverlay;
        private DuckovController.UI.Menu.MenuBackGlyphInjector? _backGlyphInjector;
        private MiniGameInputGate? _miniGameGate;
        private DuckovController.Haptics.HapticEngine? _hapticEngine;

        // CRITICAL: no Harmony type references here — JIT must not trigger 0Harmony.dll resolution
        // before EnsureHarmonyLoaded runs (mod can load before HarmonyLoadMod Workshop 3589088839).
        protected override void OnAfterSetup()
        {
            try
            {
                // Config lives under persistentDataPath/GoldenController so a Workshop content
                // update (which replaces the mod folder) can't wipe a player's tuned settings.
                // Seeded from the mod folder's Settings.default.json; legacy in-folder config migrated.
                _settingsPath = ControllerConfigLoader.ResolveConfigPath(info.path, out var seedDir);
                // Bundled glyph PNGs live under <mod>/assets/glyphs/<profile>/.
                DuckovController.UI.Prompts.GlyphProvider.ModRoot = info.path;
                _config = ControllerConfigLoader.LoadOrDefault(_settingsPath, seedDir);
                Log.Verbose = _config.Diagnostics.DebugLog || _config.Diagnostics.DevMode;
                Log.Info($"Loaded config from {_settingsPath}");

                // AIM-1: gated boot self-check for the pure recoil/lead math (config is loaded and
                // AutoAimTiers.Apply has already run inside LoadOrDefault). Logs [selfcheck] PASS/FAIL.
                if (_config.Diagnostics.DebugLog) DuckovController.Aim.RecoilLeadSelfCheck.RunOnce();

                DuckovController.Diagnostics.PerfFlags.Apply(_config.Perf);

                LogConfigSnapshot(_config, "boot");

                DuckovController.UI.Settings.SettingsBridge.Cfg = _config;
                DuckovController.UI.Settings.SettingsBridge.SettingsPath = _settingsPath;
                DuckovController.UI.Settings.SettingsBridge.OnRulesChanged -= OnPanelRulesChanged;
                DuckovController.UI.Settings.SettingsBridge.OnRulesChanged += OnPanelRulesChanged;

                ControllerConfigLoader.StartWatching();

                AimDriverPatch.Cfg = _config;
                GameplayInputDriverPatch.Cfg = _config;
                DuckovController.Throwables.ThrowableController.Cfg = _config;

                if (!EnsureHarmonyLoaded(info.path))
                {
                    Log.Error("0Harmony.dll could not be loaded. Mod will run without Harmony patches.");
                }
                else if (_config.Perf.ApplyHarmonyPatches)
                {
                    InitHarmony(); // JIT only happens here, after 0Harmony is in AppDomain
                }
                else
                {
                    Log.Info("Harmony patches SKIPPED (Perf.ApplyHarmonyPatches=false).");
                }

                // On mod re-enable OptionsPanel already exists; inject directly. No-op on fresh launch.
                if (_harmony != null)
                    OptionsPanel_Setup_Patch.TryReinjectIntoLivePanel();

                // Must register before GridFocusController spawns — router consults registry on every Tick.
                InventoryVerbRouter.RegisterAllViewMaps();

                _focusController = gameObject.AddComponent<GridFocusController>();
                _focusController.SetConfig(_config);

                _menuController = gameObject.AddComponent<MenuFocusController>();
                _menuController.Cfg = _config;

                // Spawn dumper only in DevMode or when explicitly enabled; release ships with both off.
                if (_config.Diagnostics.DevMode || _config.Diagnostics.UIDumperEnabled)
                {
                    _uiDumper = gameObject.AddComponent<DuckovController.Diagnostics.UIStructureDumper>();
                    _uiDumper.Cfg = _config.Diagnostics;
                }

                _healController = gameObject.AddComponent<DuckovController.Heal.SmartHealController>();
                _healController.Cfg = _config;

                _menuOverlay = gameObject.AddComponent<DuckovController.UI.Menu.MenuFocusOverlay>();
                _backGlyphInjector = gameObject.AddComponent<DuckovController.UI.Menu.MenuBackGlyphInjector>();
                _backGlyphInjector.Bind(_menuOverlay);

                // Replaces keyboard "T" bullet-switch HUD prompt with Dpad-up glyph when gamepad connected.
                gameObject.AddComponent<DuckovController.UI.Prompts.BulletSwitchGlyphInjector>();

                // Rewrites GamingConsoleHUD InputIndicator rows to controller glyphs (Start/Select/X/Y/D-Pad).
                gameObject.AddComponent<DuckovController.UI.Prompts.MiniGameHintGlyphInjector>();

                // LB/RB hint on shared ViewTabs bar (one element, not per-view rows).
                gameObject.AddComponent<DuckovController.UI.Prompts.ViewTabsGlyphInjector>();

                // Native gameplay-prompt swap: interact→Y, cancel→B. Subscribes to InputIndicator.OnAfterRefresh.
                gameObject.AddComponent<DuckovController.UI.Prompts.GameplayPromptGlyphInjector>();

                gameObject.AddComponent<DuckovController.UI.CutsceneDialogueHandler>();

                gameObject.AddComponent<DuckovController.Diagnostics.PerfHud>();

                // Source assigned AFTER AddComponent: OnEnable runs inside AddComponent so the field
                // would be null if read there. Panel subscribes lazily in Update once Source is set.
                var hintPanel = gameObject.AddComponent<DuckovController.UI.Prompts.ViewHintPanel>();
                hintPanel.Source = _focusController.Router;

                _hapticEngine = gameObject.AddComponent<DuckovController.Haptics.HapticEngine>();
                _hapticEngine.Cfg = _config;

                TryApplyBindings();
            }
            catch (Exception e)
            {
                Log.Error($"OnAfterSetup failed: {e}");
            }
        }

        // Loads 0Harmony.dll into AppDomain if absent. Prefers bundled copy, falls back to Workshop path.
        // No Harmony type refs so JIT is always safe.
        private static bool EnsureHarmonyLoaded(string modFolder)
        {
            try
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name == "0Harmony") return true;
                }
            }
            catch { /* defensive — fall through to load attempt */ }

            var candidates = new[]
            {
                Path.Combine(modFolder, "0Harmony.dll"),
                // Workshop HarmonyLib (id 3589088839) on Linux Steam path.
                Path.Combine(modFolder, "..", "..", "..", "..", "..", "workshop",
                    "content", "3167020", "3589088839", "0Harmony.dll"), // Workshop HarmonyLib on Linux Steam
            };
            foreach (var c in candidates)
            {
                try
                {
                    if (string.IsNullOrEmpty(c)) continue;
                    var full = Path.GetFullPath(c);
                    if (!File.Exists(full)) continue;
                    var asm = Assembly.LoadFrom(full);
                    Log.Info($"Loaded {asm.GetName().FullName} from {full}");
                    return true;
                }
                catch (Exception e)
                {
                    Log.Debug_($"LoadFrom {c} failed: {e.Message}");
                }
            }
            return false;
        }

        // References Harmony — only called after EnsureHarmonyLoaded returns true.
        private void InitHarmony()
        {
            var harmony = new HarmonyLib.Harmony(HarmonyId);
            harmony.PatchAll(typeof(ModBehaviour).Assembly);
            _harmony = harmony;
            Log.Info("Harmony patches applied.");
        }

        protected override void OnBeforeDeactivate()
        {
            try
            {
                ControllerConfigLoader.StopWatching();

                DuckovController.UI.Settings.SettingsBridge.OnRulesChanged -= OnPanelRulesChanged;
                DuckovController.UI.Settings.SettingsBridge.Cfg = null;
                DuckovController.UI.Settings.SettingsBridge.SettingsPath = null;

                if (_miniGameGate != null)
                {
                    _miniGameGate.Shutdown();
                    _miniGameGate = null;
                }

                if (_bindingsApplied)
                {
                    var actions = TryGetActions();
                    if (actions != null) GamepadBindings.Remove(actions);
                    _bindingsApplied = false;
                }

                UnpatchHarmony();
                _harmony = null;

                // OptionsPanel outlives our GameObject; Destroy(gameObject) won't clean these up.
                OptionsPanel_Setup_Patch.TeardownInjectedTab();

                if (_focusController != null)
                {
                    Destroy(_focusController);
                    _focusController = null;
                }
                if (_menuController != null)
                {
                    Destroy(_menuController);
                    _menuController = null;
                }
                if (_uiDumper != null)
                {
                    Destroy(_uiDumper);
                    _uiDumper = null;
                }
                if (_healController != null)
                {
                    Destroy(_healController);
                    _healController = null;
                }
                if (_backGlyphInjector != null)
                {
                    Destroy(_backGlyphInjector);
                    _backGlyphInjector = null;
                }
                DuckovController.UI.Prompts.GlyphProvider.Clear();  // drop cached glyphs so a same-session re-activation reloads fresh
                if (_menuOverlay != null)
                {
                    Destroy(_menuOverlay);
                    _menuOverlay = null;
                }
                // Mod-created GOs on persistent game-side UI survive Destroy(gameObject); must clean manually.
                DuckovController.UI.Inventory.VerbMaps.MiniMapViewVerbMap.OnModDeactivate();
                DuckovController.UI.Inventory.VerbMaps.BuilderViewVerbMap.OnModDeactivate();

                if (_hapticEngine != null) { _hapticEngine.ResetNow(); Destroy(_hapticEngine); _hapticEngine = null; }

                DuckovController.Throwables.ThrowableController.Reset();
                DuckovController.Aim.MeleeAimAssist.Reset();
            }
            catch (Exception e)
            {
                Log.Error($"OnBeforeDeactivate failed: {e}");
            }
        }

        private void Update()
        {
            if (!_bindingsApplied) TryApplyBindings();
            // Marshal file-watcher config reload to main thread (watcher callback can't call Unity APIs).
            var pending = ControllerConfigLoader.Pending;
            if (pending != null)
            {
                ControllerConfigLoader.Pending = null;
                OnConfigReloaded(pending);
            }

            _miniGameGate?.Tick();

            RestoreNavEventsOnUnpauseEdge();
        }

        // BUG-1 backstop: on unpause edge, force sendNavigationEvents on.
        // Catches suppressions stranded on a swapped/untracked EventSystem that RestoreNavEvents misses.
        // GameManager.Paused reads pauseMenu.Shown — authoritative; Time.timeScale can be 0 for non-pause.
        private void RestoreNavEventsOnUnpauseEdge()
        {
            bool pausedNow;
            try { pausedNow = GameManager.Paused; }
            catch { pausedNow = false; }

            if (_wasPausedLastFrame && !pausedNow)
            {
                var es = UnityEngine.EventSystems.EventSystem.current;
                if (es != null && !es.sendNavigationEvents)
                {
                    es.sendNavigationEvents = true;
                    Log.Debug_("[navEvents] restored on un-pause edge");
                }
            }
            _wasPausedLastFrame = pausedNow;
        }

        // References Harmony — JIT happens at shutdown, Harmony definitely loaded by then.
        private void UnpatchHarmony()
        {
            (_harmony as HarmonyLib.Harmony)?.UnpatchAll(HarmonyId);
        }

        private InputActionAsset? TryGetActions()
        {
            try
            {
                var pi = GameManager.MainPlayerInput;
                if (pi == null) return null;
                return pi.actions;
            }
            catch
            {
                return null;
            }
        }

        private void TryApplyBindings()
        {
            if (_config == null) return;
            var actions = TryGetActions();
            if (actions == null) return;
            try
            {
                GamepadBindings.Apply(actions, _config);
                _bindingsApplied = true;

                if (_miniGameGate == null)
                {
                    _miniGameGate = new MiniGameInputGate(actions);
                    _miniGameGate.Initialize();
                }
                else
                {
                    _miniGameGate.UpdateConfig(actions);
                }
            }
            catch (Exception e)
            {
                Log.Error($"GamepadBindings.Apply failed: {e}");
            }
        }

        // Always-on (Log.Info) snapshot of the behavior-driving config fields, emitted on EVERY config
        // change (boot / file hot-reload / settings-panel edit). Survives DebugLog=false so a future
        // Player.log always pins exactly what aim/perf state the mod was running — including
        // Perf.EnableAimDriver, whose stale "false" from a perf bisection silently kills ALL gun assist
        // (hip/ADS/scope/sniper/throw) while leaving melee snap alive. `why` tags the trigger.
        private static void LogConfigSnapshot(ControllerConfig c, string why)
        {
            if (c == null) return;
            var aa = c.AutoAim; var br = c.BiasRing; var rc = c.Recoil; var aim = c.Aim; var p = c.Perf;
            Log.Info($"[cfgsnap] ({why}) tier={aa.Tier} aa.Enabled={aa.Enabled} "
                + $"maxDist={aa.MaxTargetDistanceMeters:0.#} throughWalls={aa.TargetThroughWalls} "
                + $"minLockMs={aa.MinLockTimeMs} melee={aa.MeleeMaxTurnDegrees:0.#} | "
                + $"biasRing.Enabled={br.Enabled} ring={br.RingRadiusPx:0.#} recoil.Enabled={rc.Enabled} | "
                + $"baselineAssist={aim.BaselineAssistEnabled} magnet={aim.MagnetismEnabled} "
                + $"slow={aim.SlowdownEnabled} | perf.AimDriver={p.EnableAimDriver} "
                + $"perf.GameplayInput={p.EnableGameplayInput} perf.Throwables={p.EnableThrowables} "
                + $"perf.Harmony={p.ApplyHarmonyPatches} | debugLog={c.Diagnostics.DebugLog}");
        }

        private void OnConfigReloaded(ControllerConfig newCfg)
        {
            _config = newCfg;
            Log.Verbose = newCfg.Diagnostics.DebugLog || newCfg.Diagnostics.DevMode;
            DuckovController.Diagnostics.PerfFlags.Apply(newCfg.Perf);
            LogConfigSnapshot(newCfg, "hot-reload");
            AimDriverPatch.Cfg = newCfg;
            GameplayInputDriverPatch.Cfg = newCfg;
            DuckovController.Throwables.ThrowableController.Cfg = newCfg;
            if (_focusController != null) _focusController.SetConfig(newCfg);
            if (_menuController != null) _menuController.Cfg = newCfg;
            if (_uiDumper != null) _uiDumper.Cfg = newCfg.Diagnostics;
            if (_healController != null) _healController.Cfg = newCfg;
            if (_hapticEngine != null) _hapticEngine.Cfg = newCfg;
            DuckovController.Heal.BuffIdRegistry.Invalidate();

            // Republish so panel.Cfg stays in lock-step with newCfg. Without this, panel.Cfg points at
            // the old instance; next NotifyValueChanged fires RefreshAll with the file-loaded values,
            // silently reverting the edit (observed: A press works first time, no-ops second time).
            DuckovController.UI.Settings.SettingsBridge.Cfg = newCfg;
            DuckovController.UI.Settings.SettingsBridge.NotifyRulesChanged();
            Log.Info("Config reloaded.");

            var actions = TryGetActions();
            if (actions != null)
            {
                GamepadBindings.Apply(actions, newCfg);
                _bindingsApplied = true;
                _miniGameGate?.UpdateConfig(actions);
            }
        }

        // Panel widget wrote a value; propagate in-process without waiting for file-watcher round-trip.
        // Panel has already called ControllerConfigLoader.Save() before raising this.
        private void OnPanelRulesChanged(ControllerConfig cfg)
        {
            if (cfg == null) return;
            Log.Verbose = cfg.Diagnostics.DebugLog || cfg.Diagnostics.DevMode;
            DuckovController.Diagnostics.PerfFlags.Apply(cfg.Perf);
            LogConfigSnapshot(cfg, "panel-edit");
            AimDriverPatch.Cfg = cfg;
            GameplayInputDriverPatch.Cfg = cfg;
            DuckovController.Throwables.ThrowableController.Cfg = cfg;
            if (_focusController != null) _focusController.SetConfig(cfg);
            if (_menuController != null) _menuController.Cfg = cfg;
            if (_uiDumper != null) _uiDumper.Cfg = cfg.Diagnostics;
            if (_healController != null) _healController.Cfg = cfg;
            DuckovController.Heal.BuffIdRegistry.Invalidate();
        }
    }
}
