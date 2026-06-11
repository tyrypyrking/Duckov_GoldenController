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

        internal static ControllerConfig LoadOrDefault(string path)
        {
            _path = path;
            try
            {
                if (!File.Exists(path))
                {
                    var fresh = new ControllerConfig();
                    AutoAimTiers.Apply(fresh);
                    Save(fresh, path);
                    return fresh;
                }
                var text = File.ReadAllText(path);
                var cfg = JsonConvert.DeserializeObject<ControllerConfig>(text)
                          ?? new ControllerConfig();
                AutoAimTiers.Apply(cfg);
                return cfg;
            }
            catch (Exception e)
            {
                Log.Error($"Failed to load {path}: {e.Message}. Using defaults.");
                return new ControllerConfig();
            }
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
