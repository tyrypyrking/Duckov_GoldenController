using UnityEngine;

namespace DuckovController.Diagnostics
{
    // 1 Hz frame-time logger. When Enabled (Perf.LogFps), writes "[perf] fps=… avgMs=… worstMs=…" to Player.log.
    // Near-zero cost when disabled; hot-reloadable via PerfFlags.Apply.
    internal sealed class PerfHud : MonoBehaviour
    {
        internal static bool Enabled;

        private float _accum;
        private int _frames;
        private float _worst;
        private bool _stackTraceSuppressed;

        private void Update()
        {
            if (!Enabled) return;
            if (!_stackTraceSuppressed)
            {
                _stackTraceSuppressed = true;
                // Debug.Log captures a stack trace by default — caused a ~40 ms/s spike that inflated averages.
                // Suppress for Log/Warning while LogFps is active.
                try
                {
                    Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
                    Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
                }
                catch { /* non-fatal */ }
            }
            float dt = Time.unscaledDeltaTime;
            _accum += dt;
            _frames++;
            if (dt > _worst) _worst = dt;
            if (_accum >= 1f && _frames > 0)
            {
                float fps = _frames / _accum;
                float avgMs = (_accum / _frames) * 1000f;
                float worstMs = _worst * 1000f;
                Log.Info($"[perf] fps={fps:F1} avgMs={avgMs:F2} worstMs={worstMs:F2}");
                _accum = 0f;
                _frames = 0;
                _worst = 0f;
            }
        }
    }
}
