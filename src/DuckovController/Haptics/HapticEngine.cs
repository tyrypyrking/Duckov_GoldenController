using System.Collections.Generic;
using UnityEngine;

namespace DuckovController.Haptics
{
    internal sealed class HapticEngine : MonoBehaviour
    {
        internal static HapticEngine? Instance { get; private set; }

        internal Config.ControllerConfig? Cfg;

        private IHapticBackend _backend = new UnityRumbleBackend();

        private struct Segment { public float Low, High; public int DurMs; }
        private readonly List<Segment> _segments = new List<Segment>(8);
        private int _segIndex = -1;
        private float _segEndTime;
        private int _activePriority = int.MinValue;
        private bool _motorsActive;

        private float _lastFocusTickTime = -10f;
        private int _focusRepeatCount;
        private const float FocusTickMinIntervalSec = 0.05f;
        private const float MaxGain = 3.0f;

        private void Awake() { Instance = this; }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            try { _backend.Reset(); } catch { }
        }

        internal void Play(HapticCue cue, float scale = 1f)
        {
            if (Cfg == null) return;
            var h = Cfg.Haptics;
            if (!h.Enabled) return;

            var p = HapticCatalog.Get(cue);
            if (p.Category == HapticCategory.Ui && !h.UiEnabled) return;
            if (p.Category == HapticCategory.Gameplay && !h.GameplayEnabled) return;

            float intensity = Mathf.Clamp01(h.Intensity) * MaxGain * Mathf.Clamp01(scale);
            if (intensity <= 0.001f) return;

            if (cue == HapticCue.FocusTick)
            {
                float now = Time.unscaledTime;
                if (now - _lastFocusTickTime < FocusTickMinIntervalSec) return;   // rate-limit
                _focusRepeatCount = (now - _lastFocusTickTime < 0.25f) ? _focusRepeatCount + 1 : 0;
                _lastFocusTickTime = now;
                if (_focusRepeatCount > 0) intensity *= 0.6f;                     // damp held sweeps
            }

            // Priority: a new cue only replaces a strictly-lower in-flight pulse.
            if (_motorsActive && p.Priority < _activePriority) return;

            Log.Debug_($"HapticEngine.Play cue={cue} cat={p.Category} en={h.Enabled} ui={h.UiEnabled} gp={h.GameplayEnabled} I={intensity:0.000}");
            BuildSegments(p, intensity);
        }

        private void BuildSegments(in HapticProfile p, float intensity)
        {
            _segments.Clear();
            float low  = Mathf.Clamp01(p.Low  * intensity);
            float high = Mathf.Clamp01(p.High * intensity);
            int pulses = Mathf.Max(1, p.Pulses);
            for (int i = 0; i < pulses; i++)
            {
                _segments.Add(new Segment { Low = low, High = high, DurMs = p.DurationMs });
                if (i < pulses - 1 && p.GapMs > 0)
                    _segments.Add(new Segment { Low = 0f, High = 0f, DurMs = p.GapMs });
            }
            _segIndex = -1;
            _activePriority = p.Priority;
            AdvanceSegment();
        }

        private void AdvanceSegment()
        {
            _segIndex++;
            if (_segIndex >= _segments.Count) { StopMotors(); return; }
            var s = _segments[_segIndex];
            _backend.SetMotors(s.Low, s.High);
            Log.Debug_($"HapticEngine.drive seg={_segIndex} low={s.Low:0.000} high={s.High:0.000} durMs={s.DurMs}");
            _motorsActive = true;
            _segEndTime = Time.unscaledTime + s.DurMs / 1000f;
        }

        private void StopMotors()
        {
            _backend.SetMotors(0f, 0f);
            _motorsActive = false;
            _segIndex = -1;
            _segments.Clear();
            _activePriority = int.MinValue;
        }

        private void Update()
        {
            if (!_motorsActive) return;                       // zero cost at rest
            if (Time.unscaledTime >= _segEndTime) AdvanceSegment();
        }

        private void OnApplicationFocus(bool focus)
        {
            if (!focus) { StopMotors(); try { _backend.Reset(); } catch { } }
        }

        internal void ResetNow()
        {
            StopMotors();
            try { _backend.Reset(); } catch { }
        }
    }
}
