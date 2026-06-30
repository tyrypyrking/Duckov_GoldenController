using System;
using System.IO;
using Newtonsoft.Json;
using DuckovController.Aim;

namespace DuckovController.Config
{
    // Loader + file watcher. The watcher's Changed event fires on a thread-
    // pool thread; we never invoke OnReload from there. Instead the worker
    // stashes the freshly-loaded config in `Pending`, which ModBehaviour
    // drains on the next Unity main-thread Update.
    internal static class ControllerConfigLoader
    {
        private static FileSystemWatcher? _watcher;
        private static string? _path;

        // Set by the watcher (worker thread), consumed by ModBehaviour.Update
        // (main thread). Reference assignment is atomic in .NET.
        internal static volatile ControllerConfig? Pending;

        internal static ControllerConfig LoadOrDefault(string path, string? seedDir = null)
        {
            _path = path;
            try
            {
                // Overlay user keys onto fresh defaults: present keys win, missing keys keep
                // their C# default, extra keys are ignored. ObjectCreationHandling.Replace is
                // CRITICAL — the Auto default APPENDS to already-populated arrays (e.g.
                // SmartHeal.QueueCancelButtons, SmartTake.IncludeTags), duplicating defaults.
                var settings = new JsonSerializerSettings
                {
                    ObjectCreationHandling = ObjectCreationHandling.Replace,
                };

                if (!File.Exists(path))
                {
                    // First install: seed from Settings.default.json shipped in the mod folder
                    // (seedDir), else pure C# defaults. Then materialize the file on disk.
                    // seedDir is separate from the config dir because the config now lives in
                    // persistentDataPath while the seed ships inside the (replaceable) mod folder.
                    var fresh = new ControllerConfig();
                    var dir = seedDir ?? Path.GetDirectoryName(path)!;
                    var defaultPath = Path.Combine(dir, "Settings.default.json");
                    if (File.Exists(defaultPath))
                    {
                        JsonConvert.PopulateObject(File.ReadAllText(defaultPath), fresh, settings);
                    }
                    AutoAimTiers.Apply(fresh);
                    Save(fresh, path);
                    return fresh;
                }

                var text = File.ReadAllText(path);
                var cfg = new ControllerConfig();
                JsonConvert.PopulateObject(text, cfg, settings);
                AutoAimTiers.Apply(cfg);
                // Save the merged config back so newly-introduced keys materialize on disk with
                // their defaults — the file upgrades in place. Save() stamps _lastReload so the
                // watcher's debounce swallows the resulting OS change event (no reload loop).
                Save(cfg, path);
                return cfg;
            }
            catch (Exception e)
            {
                Log.Error($"Failed to load {path}: {e.Message}. Using defaults.");
                return new ControllerConfig();
            }
        }

        // Resolve the live config path under persistentDataPath/GoldenController so it survives
        // Workshop content replacement (mod-folder files are wiped on every update). Seeds from the
        // mod folder's Settings.default.json on first run; one-time migrates a legacy in-folder
        // Settings.json written by older versions. Returns the persistent path; seedDirOut receives
        // the mod folder (where Settings.default.json lives).
        internal static string ResolveConfigPath(string modFolder, out string seedDirOut)
        {
            seedDirOut = modFolder;
            string dir;
            try { dir = Path.Combine(UnityEngine.Application.persistentDataPath, "GoldenController"); }
            catch { return Path.Combine(modFolder, "Settings.json"); } // fallback: legacy location

            try { Directory.CreateDirectory(dir); } catch { }
            var persistent = Path.Combine(dir, "Settings.json");

            // One-time migration: an older build wrote Settings.json inside the mod folder. If the
            // new location has none yet but a legacy one exists, carry it over so the user keeps
            // their tuned config exactly once. We do NOT delete the legacy file (mod folder is
            // replaced on update anyway); the new file then wins on every subsequent launch.
            try
            {
                var legacy = Path.Combine(modFolder, "Settings.json");
                if (!File.Exists(persistent) && File.Exists(legacy))
                {
                    File.Copy(legacy, persistent, overwrite: false);
                    Log.Info($"Migrated config from mod folder to {persistent}");
                }
            }
            catch (Exception e) { Log.Warn($"Config migration skipped: {e.Message}"); }

            return persistent;
        }

        internal static void Save(ControllerConfig cfg, string path)
        {
            try
            {
                // Stamp _lastReload so the watcher's debounce short-circuits the OS change event
                // from our own WriteAllText (prevents a stale Pending reload that would desync panel.Cfg).
                _lastReload = DateTime.UtcNow;
                var text = JsonConvert.SerializeObject(cfg, Formatting.Indented);
                File.WriteAllText(path, text);
            }
            catch (Exception e)
            {
                Log.Error($"Failed to save config to {path}: {e.Message}");
            }
        }

        internal static void StartWatching()
        {
            if (_path == null) return;
            StopWatching();
            var dir = Path.GetDirectoryName(_path);
            var file = Path.GetFileName(_path);
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file)) return;
            try
            {
                _watcher = new FileSystemWatcher(dir, file)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };
                _watcher.Changed += OnFileChanged;
            }
            catch (Exception e)
            {
                Log.Warn($"FileSystemWatcher unavailable: {e.Message}");
            }
        }

        internal static void StopWatching()
        {
            if (_watcher != null)
            {
                try { _watcher.Changed -= OnFileChanged; _watcher.Dispose(); }
                catch { /* ignore */ }
                _watcher = null;
            }
            Pending = null;
        }

        private static DateTime _lastReload = DateTime.MinValue;
        private static void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            // Debounce: editors often fire two events for a single save.
            if ((DateTime.UtcNow - _lastReload).TotalMilliseconds < 250) return;
            _lastReload = DateTime.UtcNow;
            try
            {
                Pending = LoadOrDefault(_path!);
            }
            catch (Exception ex)
            {
                Log.Error($"Hot reload (worker thread) failed: {ex.Message}");
            }
        }
    }
}
