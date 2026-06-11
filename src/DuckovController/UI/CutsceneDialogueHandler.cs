using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace DuckovController.UI
{
    // 1. Cinematic skip: hold A → PlayableDirector.time→end+Stop (Timeline assets, no gamepad skip natively).
    // 2. Dialogue advance: A → DialogueBubble.Interact() / DialogueUI.Confirm() on visible bubble.
    // All game-side access is reflective; type/member misses degrade gracefully with a one-shot log.
    internal sealed class CutsceneDialogueHandler : MonoBehaviour
    {
        internal const float CutsceneSkipHoldSec = 1.5f;

        private float _cutsceneHoldStart = -1f;
        private bool  _cutsceneSkippedThisHold;
        // Latch: "not waiting" log fires once per episode, not every frame.
        private bool  _loggedNotWaiting;

        // Director probe cache: refreshed at DirectorProbeIntervalSec when null; cleared on scene load or director stop.
        private UnityEngine.Object? _cachedDirector;
        private float _lastDirectorProbeAt;
        private const float DirectorProbeIntervalSec = 0.25f;
        private bool _sceneHooked;

        // Reflection caches resolved on first use.
        private static bool _resolved;
        // DialogueBubble: NPC floating bubble (legacy fallback). DialogueUI: NodeCanvas story dialog (continueButton + WaitForConfirm).
        private static Type? _dialogueManagerType;
        private static FieldInfo? _dialogueManagerBubblesField;
        private static Type? _dialogueBubbleType;
        private static MethodInfo? _dialogueBubbleInteractMethod;
        private static FieldInfo? _dialogueBubbleFadeGroupField;
        private static FieldInfo? _dialogueBubbleInteractedField;
        private static Type? _dialogueUIType;
        private static MethodInfo? _dialogueUIConfirmMethod;
        private static FieldInfo? _dialogueUIConfirmedField;
        private static FieldInfo? _dialogueUIContinueIndicatorField;
        private static FieldInfo? _dialogueUIMainFadeGroupField;
        private static bool _dialogueUIMembersLogged;
        // PlayableDirector lives in UnityEngine.DirectorModule (not referenced at compile time) — reflective access only.
        private static Type? _directorType;
        private static PropertyInfo? _directorStateProp;
        private static PropertyInfo? _directorTimeProp;
        private static PropertyInfo? _directorDurationProp;
        private static MethodInfo?   _directorEvaluateMethod;
        private static MethodInfo?   _directorStopMethod;
        private static int _playStatePlaying = 1; // PlayState.Playing default; refined in resolve

        private void OnEnable()
        {
            if (!_sceneHooked)
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                _sceneHooked = true;
            }
        }

        private void OnDisable()
        {
            if (_sceneHooked)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                _sceneHooked = false;
            }
        }

        private void OnDestroy()
        {
            if (_sceneHooked)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                _sceneHooked = false;
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _cachedDirector = null;
        }

        private void Update()
        {
            if (!DuckovController.Diagnostics.PerfFlags.Cutscene) return;
            EnsureResolved();
            var pad = Gamepad.current;
            if (pad == null) return;

            HandleCutsceneSkip(pad);
            HandleDialogueAdvance(pad);
        }

        private void HandleCutsceneSkip(Gamepad pad)
        {
            if (_directorType == null)
            {
                _cutsceneHoldStart = -1f;
                _cutsceneSkippedThisHold = false;
                return;
            }

            // Verify cached director is still Playing; clear on stop so the next probe picks up a new one.
            if (_cachedDirector != null)
            {
                bool stillPlaying = false;
                try
                {
                    object? stateVal = null;
                    try { stateVal = _directorStateProp?.GetValue(_cachedDirector); } catch { stateVal = null; }
                    if (stateVal is int si && si == _playStatePlaying) stillPlaying = true;
                    else if (stateVal != null && Convert.ToInt32(stateVal) == _playStatePlaying) stillPlaying = true;
                }
                catch { stillPlaying = false; }
                if (!stillPlaying) _cachedDirector = null;
            }

            // Throttle FindObjectsOfType probe when cache is empty.
            if (_cachedDirector == null)
            {
                if (Time.unscaledTime - _lastDirectorProbeAt < DirectorProbeIntervalSec)
                {
                    _cutsceneHoldStart = -1f;
                    _cutsceneSkippedThisHold = false;
                    return;
                }
                _lastDirectorProbeAt = Time.unscaledTime;
                try
                {
                    var all = UnityEngine.Object.FindObjectsOfType(_directorType);
                    foreach (var d in all)
                    {
                        if (d == null) continue;
                        object? stateVal = null;
                        try { stateVal = _directorStateProp?.GetValue(d); } catch { stateVal = null; }
                        if (stateVal is int si && si == _playStatePlaying) { _cachedDirector = d; break; }
                        if (stateVal != null && Convert.ToInt32(stateVal) == _playStatePlaying) { _cachedDirector = d; break; }
                    }
                }
                catch { _cachedDirector = null; }
            }

            var director = _cachedDirector;
            if (director == null)
            {
                _cutsceneHoldStart = -1f;
                _cutsceneSkippedThisHold = false;
                return;
            }

            if (!pad.buttonSouth.isPressed)
            {
                _cutsceneHoldStart = -1f;
                _cutsceneSkippedThisHold = false;
                return;
            }

            if (_cutsceneHoldStart < 0f) { _cutsceneHoldStart = Time.unscaledTime; _cutsceneSkippedThisHold = false; }
            if (_cutsceneSkippedThisHold) return;

            if (Time.unscaledTime - _cutsceneHoldStart >= CutsceneSkipHoldSec)
            {
                _cutsceneSkippedThisHold = true;
                try
                {
                    double dur = 0;
                    try { dur = Convert.ToDouble(_directorDurationProp?.GetValue(director) ?? 0); } catch { dur = 0; }
                    if (_directorTimeProp != null)
                    {
                        try { _directorTimeProp.SetValue(director, Math.Max(0.0, dur - 0.001)); } catch { }
                    }
                    try { _directorEvaluateMethod?.Invoke(director, null); } catch { }
                    try { _directorStopMethod?.Invoke(director, null); } catch { }
                    Log.Info($"CutsceneDialogueHandler: skipped PlayableDirector '{director.name}' (duration {dur:F2}s).");
                }
                catch (Exception e) { Log.Warn($"PlayableDirector skip failed: {e.Message}"); }
            }
        }

        private void HandleDialogueAdvance(Gamepad pad)
        {
            if (!pad.buttonSouth.wasPressedThisFrame) return;

            // Primary path: DialogueUI.WaitForConfirm watches continueButton.
            if (TryAdvanceDialogueUI()) return;

            if (_dialogueManagerType == null || _dialogueBubbleType == null) return;
            if (_dialogueManagerBubblesField == null || _dialogueBubbleInteractMethod == null) return;

            UnityEngine.Object? mgr;
            try { mgr = UnityEngine.Object.FindObjectOfType(_dialogueManagerType); }
            catch { mgr = null; }
            if (mgr == null) return;

            System.Collections.IList? bubbles = null;
            try { bubbles = _dialogueManagerBubblesField.GetValue(mgr) as System.Collections.IList; }
            catch { bubbles = null; }
            if (bubbles == null)
            {
                Log.Info("CutsceneDialogueHandler dialogue A: bubbles list is null.");
                return;
            }
            if (bubbles.Count == 0)
            {
                Log.Info("CutsceneDialogueHandler dialogue A: bubbles list empty.");
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.Append("CutsceneDialogueHandler dialogue A: bubbles=").Append(bubbles.Count).Append(" [");
            for (int i = 0; i < bubbles.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                var b = bubbles[i];
                sb.Append("#").Append(i).Append(":");
                if (b == null) { sb.Append("<null>"); continue; }
                bool shown = false, interacted = false;
                try {
                    if (_dialogueBubbleFadeGroupField?.GetValue(b) is object fg)
                    {
                        var p = fg.GetType().GetProperty("IsShown",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (p?.GetValue(fg) is bool s) shown = s;
                    }
                    if (_dialogueBubbleInteractedField?.GetValue(b) is bool iv) interacted = iv;
                } catch { }
                sb.Append("shown=").Append(shown).Append(" interacted=").Append(interacted);
            }
            sb.Append("]");
            Log.Info(sb.ToString());

            // Advance first bubble that is shown and not yet interacted (mid-dismiss bubbles are ineligible).
            bool fired = false;
            foreach (var b in bubbles)
            {
                if (b == null) continue;
                if (!IsBubbleEligible(b)) continue;
                try
                {
                    _dialogueBubbleInteractMethod.Invoke(b, null);
                    Log.Info("CutsceneDialogueHandler: dialogue Interact() invoked.");
                    fired = true;
                }
                catch (Exception e) { Log.Warn($"DialogueBubble.Interact() failed: {e.Message}"); }
                break;
            }
            if (!fired)
                Log.Info("CutsceneDialogueHandler dialogue A: no eligible bubble — falling through.");
        }

        private bool TryAdvanceDialogueUI()
        {
            if (_dialogueUIType == null) return false;
            UnityEngine.Object? ui = null;
            try { ui = UnityEngine.Object.FindObjectOfType(_dialogueUIType); }
            catch { ui = null; }
            if (ui == null)
            {
                // DialogueUI may live in DontDestroyOnLoad or a disabled hierarchy — FindObjectsOfTypeAll covers it.
                try
                {
                    var all = Resources.FindObjectsOfTypeAll(_dialogueUIType);
                    foreach (var x in all) { if (x != null) { ui = x; break; } }
                }
                catch { /* tolerated */ }
            }
            if (ui == null)
            {
                Log.Info("CutsceneDialogueHandler: DialogueUI type known but no instance found.");
                return false;
            }

            // Only advance when continueIndicator is active (WaitForConfirm polling). Firing Confirm() early silently skips the next wait.
            bool waiting = false;
            try
            {
                var ind = _dialogueUIContinueIndicatorField?.GetValue(ui) as GameObject;
                if (ind != null) waiting = ind.activeInHierarchy;
            }
            catch { }
            if (!waiting)
            {
                if (!_loggedNotWaiting)
                {
                    Log.Debug_("CutsceneDialogueHandler: DialogueUI not waiting (continueIndicator inactive) — no-op.");
                    _loggedNotWaiting = true;
                }
                return true; // consume the A press anyway so it doesn't fall through to bubbles
            }
            _loggedNotWaiting = false; // reset once it's waiting again

            // Preferred: Confirm() — same path as game's OnPointerClick → Confirm.
            try
            {
                if (_dialogueUIConfirmMethod != null)
                {
                    _dialogueUIConfirmMethod.Invoke(ui, null);
                    Log.Info("CutsceneDialogueHandler: DialogueUI.Confirm() invoked.");
                    return true;
                }
            }
            catch (Exception e) { Log.Warn($"DialogueUI.Confirm() failed: {e.Message}"); }

            // Fallback: set confirmed=true — WaitForConfirm polls this.
            try
            {
                if (_dialogueUIConfirmedField != null && _dialogueUIConfirmedField.FieldType == typeof(bool))
                {
                    _dialogueUIConfirmedField.SetValue(ui, true);
                    Log.Info("CutsceneDialogueHandler: set DialogueUI.confirmed=true.");
                    return true;
                }
            }
            catch (Exception e) { Log.Warn($"DialogueUI.confirmed set failed: {e.Message}"); }

            return false;
        }

        private static void DumpMembers(Type t)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("CutsceneDialogueHandler members of ").Append(t.FullName).Append(": methods=[");
                bool first = true;
                foreach (var m in t.GetMethods(flags))
                {
                    if (!first) sb.Append(", "); first = false;
                    sb.Append(m.Name).Append("(").Append(m.GetParameters().Length).Append(")");
                }
                sb.Append("] fields=[");
                first = true;
                foreach (var f in t.GetFields(flags))
                {
                    if (!first) sb.Append(", "); first = false;
                    sb.Append(f.Name).Append(":").Append(f.FieldType.Name);
                }
                sb.Append("]");
                Log.Info(sb.ToString());
            }
            catch (Exception e) { Log.Warn($"DumpMembers({t.Name}): {e.Message}"); }
        }

        private static bool IsBubbleEligible(object bubble)
        {
            try
            {
                if (_dialogueBubbleInteractedField != null
                    && _dialogueBubbleInteractedField.GetValue(bubble) is bool interacted
                    && interacted)
                {
                    return false; // already advanced; coroutine cleaning up
                }
                if (_dialogueBubbleFadeGroupField != null)
                {
                    var fg = _dialogueBubbleFadeGroupField.GetValue(bubble);
                    if (fg == null) return true; // best-effort
                    var prop = fg.GetType().GetProperty("IsShown",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (prop?.GetValue(fg) is bool shown) return shown;
                }
            }
            catch { /* tolerate — fall through to true */ }
            return true;
        }

        private static void EnsureResolved()
        {
            if (_resolved) return;
            _resolved = true;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (_dialogueManagerType == null)
                    _dialogueManagerType = asm.GetType("Duckov.UI.DialogueBubbles.DialogueBubblesManager", false);
                if (_dialogueBubbleType == null)
                    _dialogueBubbleType = asm.GetType("Duckov.UI.DialogueBubbles.DialogueBubble", false);
                if (_dialogueUIType == null)
                    _dialogueUIType = asm.GetType("Dialogues.DialogueUI", false);
                if (_dialogueManagerType != null && _dialogueBubbleType != null && _dialogueUIType != null) break;
            }
            if (_dialogueUIType != null)
            {
                _dialogueUIConfirmMethod = _dialogueUIType.GetMethod("Confirm", flags, null, Type.EmptyTypes, null);
                _dialogueUIConfirmedField = _dialogueUIType.GetField("confirmed", flags);
                _dialogueUIContinueIndicatorField = _dialogueUIType.GetField("continueIndicator", flags);
                _dialogueUIMainFadeGroupField = _dialogueUIType.GetField("mainFadeGroup", flags);
                if (!_dialogueUIMembersLogged) { _dialogueUIMembersLogged = true; DumpMembers(_dialogueUIType); }
            }
            if (_dialogueManagerType != null)
            {
                _dialogueManagerBubblesField = _dialogueManagerType.GetField("bubbles", flags);
            }
            if (_dialogueBubbleType != null)
            {
                _dialogueBubbleInteractMethod = _dialogueBubbleType.GetMethod("Interact", flags, null, Type.EmptyTypes, null);
                _dialogueBubbleFadeGroupField = _dialogueBubbleType.GetField("fadeGroup", flags);
                _dialogueBubbleInteractedField = _dialogueBubbleType.GetField("interacted", flags);
            }
            // PlayableDirector + PlayState (the enum used by .state).
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (_directorType == null)
                    _directorType = asm.GetType("UnityEngine.Playables.PlayableDirector", false);
                if (_directorType != null) break;
            }
            if (_directorType != null)
            {
                _directorStateProp     = _directorType.GetProperty("state", flags);
                _directorTimeProp      = _directorType.GetProperty("time", flags);
                _directorDurationProp  = _directorType.GetProperty("duration", flags);
                _directorEvaluateMethod = _directorType.GetMethod("Evaluate", flags, null, Type.EmptyTypes, null);
                _directorStopMethod    = _directorType.GetMethod("Stop", flags, null, Type.EmptyTypes, null);
                // PlayState.Playing defaults to 1; check by name in case enum order changes.
                var playStateType = _directorStateProp?.PropertyType;
                if (playStateType != null && playStateType.IsEnum)
                {
                    foreach (var v in Enum.GetValues(playStateType))
                    {
                        if (v?.ToString() == "Playing") { _playStatePlaying = Convert.ToInt32(v); break; }
                    }
                }
            }

            Log.Info($"CutsceneDialogueHandler resolved: " +
                     $"DialogueUI={_dialogueUIType != null} Confirm={_dialogueUIConfirmMethod != null} confirmed={_dialogueUIConfirmedField != null} " +
                     $"indicator={_dialogueUIContinueIndicatorField != null} " +
                     $"Director={_directorType != null} state={_directorStateProp != null} stop={_directorStopMethod != null}.");
        }
    }
}
