using System;
using System.Collections.Generic;
using System.Reflection;
using DuckovController.Config;
using Duckov.Scenes;
using ItemStatsSystem;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DuckovController.Heal
{
    // Smart-Heal driver. State machine:
    //   Idle    — LB press-edge: evaluate cascade, fire → Firing; no-op → NoOp audio (rate-capped).
    //   Firing  — CA_UseItem running; complete → chain or Idle; watchdog → Idle if never starts.
    //   Chain   — LB still held after completion; re-evaluate + fire. LB release or cancel → back to Idle.
    internal sealed class SmartHealController : MonoBehaviour
    {
        internal ControllerConfig? Cfg;

        private enum Phase { Idle, Firing }

        private Phase _phase = Phase.Idle;
        private Item? _lastFiredItem;
        private float _firingStartedAt;
        private bool  _chainCancelTripped;
        private bool  _pendingChain;        // OnItemUsedByPlayer wants Update to fire the next cascade
        private float _lastNoOpAudioAt = -10f;
        private bool  _subscribed;
        private float _lastTapEdgeAt = -10f;

        // Throttled lookup: LevelManager.Instance full-scene-scans when absent (main menu → ~ms/frame).
        // In gameplay it's cached (cheap); TTL only matters for the absent case.
        private CharacterMainControl? _cachedCharacter;
        private float _characterCheckedAt = -10f;

        private void OnEnable()
        {
            if (_subscribed) return;
            try
            {
                CA_UseItem.OnItemUsedByPlayer += OnItemUsedByPlayer;
                _subscribed = true;
            }
            catch (Exception e)
            {
                Log.Error($"SmartHealController: OnItemUsedByPlayer subscribe failed: {e.Message}");
            }
        }

        private void OnDisable()
        {
            if (!_subscribed) return;
            try { CA_UseItem.OnItemUsedByPlayer -= OnItemUsedByPlayer; }
            catch { }
            _subscribed = false;
        }

        private void Update()
        {
            if (!DuckovController.Diagnostics.PerfFlags.SmartHeal) return;
            var rules = Cfg?.SmartHeal;
            if (rules == null || !rules.Enabled) return;

            // Pause / loading gate — never act when the world's frozen.
            if (Time.timeScale == 0f) return;
            try
            {
                if (MultiSceneCore.Instance != null && MultiSceneCore.Instance.IsLoading) return;
            }
            catch { /* MultiSceneCore not ready — proceed */ }

            // UI gate — Smart-Heal is gameplay-only. If any View is up, defer
            // to UI verb routing (LB does filter/page cycle there).
            try
            {
                if (Duckov.UI.View.ActiveView != null) return;
            }
            catch { }

            var pad = Gamepad.current;
            if (pad == null) return;

            if (Time.unscaledTime - _characterCheckedAt >= 0.2f)
            {
                _characterCheckedAt = Time.unscaledTime;
                _cachedCharacter = LevelManager.Instance?.MainCharacter;
            }
            var character = _cachedCharacter;
            if (character == null) return;
            // Don't fire on dead/respawning character.
            if (character.Health == null || character.Health.IsDead) return;

            bool lbDown     = pad.leftShoulder.wasPressedThisFrame;
            bool lbHeld     = pad.leftShoulder.isPressed;
            bool lbUp       = pad.leftShoulder.wasReleasedThisFrame;

            // Cancel-chain inputs (any of the configured buttons press-edge).
            bool cancelEdge = AnyCancelButtonPressed(pad, rules);

            switch (_phase)
            {
                case Phase.Idle:
                    // Deferred chain from OnItemUsedByPlayer; re-check held + cancel (one-frame gap).
                    if (_pendingChain)
                    {
                        _pendingChain = false;
                        if (lbHeld && !_chainCancelTripped) FireCascade(character, rules);
                        // Otherwise chain ends silently.
                    }
                    else if (lbDown)
                    {
                        _lastTapEdgeAt = Time.unscaledTime;
                        _chainCancelTripped = false;   // fresh tap clears prior trip
                        FireCascade(character, rules);
                    }
                    break;

                case Phase.Firing:
                    // Combat-intent press: interrupt current heal + suppress chain.
                    if (cancelEdge)
                    {
                        _chainCancelTripped = true;
                        TryStopCurrentUseAction(character);
                    }
                    // LB release stops the chain but doesn't interrupt the animation (not a combat signal).
                    if (lbUp) _chainCancelTripped = true;

                    // Watchdog: useItemAction never started → drop back.
                    var useAction = character.useItemAction;
                    if (useAction != null
                        && !useAction.Running
                        && (Time.unscaledTime - _firingStartedAt) > rules.StartWatchdogSec)
                    {
                        Log.Debug_("SmartHeal: watchdog — useItemAction never started, returning to Idle.");
                        EndFireSession();
                    }
                    break;
            }
        }

        // Stop CA_UseItem only, not an unrelated action that may have started between cascade steps.
        private static void TryStopCurrentUseAction(CharacterMainControl character)
        {
            try
            {
                var useAction = character.useItemAction;
                if (useAction != null && useAction.Running)
                {
                    useAction.StopAction();
                    Log.Debug_("SmartHeal: cancel-edge interrupted CA_UseItem.");
                }
            }
            catch (Exception e)
            {
                Log.Debug_($"SmartHeal: StopAction failed: {e.Message}");
            }
        }

        // Decide + invoke one cascade step. Phase transitions gated on CA_UseItem completion event.
        private void FireCascade(CharacterMainControl character, SmartHealRules rules)
        {
            var decision = SmartHealEngine.Pick(character, rules);
            if (decision.Item == null)
            {
                NoOpAudio(rules);
                return;
            }

            try
            {
                character.UseItem(decision.Item);
                _lastFiredItem = decision.Item;
                _firingStartedAt = Time.unscaledTime;
                _chainCancelTripped = false;
                _phase = Phase.Firing;
                Log.Debug_($"SmartHeal: fired rank={decision.Rank} item={decision.Item.DisplayNameRaw}");
            }
            catch (Exception e)
            {
                Log.Error($"SmartHeal: UseItem threw: {e.Message}");
                EndFireSession();
            }
        }

        // CA_UseItem fires this for any player item use. Filter by ReferenceEquals (destruction null-overloads ==,
        // not reference identity). CRITICAL: do NOT call UseItem here — event fires from CA_UseItem.OnFinish
        // BEFORE StopAction() runs, so the action stack still considers it Running; re-entry would no-op or
        // collide. Set _pendingChain; next Update frame fires after the action fully clears.
        private void OnItemUsedByPlayer(Item usedItem)
        {
            if (_phase != Phase.Firing) return;
            if (!ReferenceEquals(usedItem, _lastFiredItem)) return;

            var rules = Cfg?.SmartHeal;
            // Transition Firing → Idle regardless of chain decision; the chain
            // (if requested) takes effect on the next Update tick.
            _phase = Phase.Idle;
            _lastFiredItem = null;
            if (rules == null) return;

            var pad = Gamepad.current;
            bool lbStillHeld = pad != null && pad.leftShoulder.isPressed;
            bool heldLongEnoughForChain = (Time.unscaledTime - _lastTapEdgeAt) >= rules.TapWindowSec;
            bool uiOpened = false;
            try { uiOpened = Duckov.UI.View.ActiveView != null; } catch { }

            if (rules.QueueOnHold
                && lbStillHeld
                && heldLongEnoughForChain
                && !_chainCancelTripped
                && !uiOpened)
            {
                _pendingChain = true;
            }
        }

        private void EndFireSession()
        {
            _phase = Phase.Idle;
            _lastFiredItem = null;
            _pendingChain = false;
            // _chainCancelTripped preserved; cleared on next LB press-edge so a cancel doesn't bleed into a new session.
        }

        private static bool AnyCancelButtonPressed(Gamepad pad, SmartHealRules rules)
        {
            var names = rules.QueueCancelButtons;
            if (names == null) return false;
            foreach (var n in names)
            {
                if (string.IsNullOrEmpty(n)) continue;
                var ctrl = TryGetButton(pad, n);
                if (ctrl != null && ctrl.wasPressedThisFrame) return true;
            }
            return false;
        }

        // ButtonControl lookup by Gamepad property name (e.g. "buttonSouth", "leftTrigger"). Caches by name.
        private static readonly Dictionary<string, PropertyInfo?> _btnPropCache = new();
        private static UnityEngine.InputSystem.Controls.ButtonControl? TryGetButton(Gamepad pad, string name)
        {
            if (!_btnPropCache.TryGetValue(name, out var prop))
            {
                prop = typeof(Gamepad).GetProperty(name,
                    BindingFlags.Instance | BindingFlags.Public);
                _btnPropCache[name] = prop;
            }
            if (prop == null) return null;
            // Triggers are ButtonControl too (AxisControl with isButton=true; the
            // Gamepad.leftTrigger property returns ButtonControl).
            try { return prop.GetValue(pad) as UnityEngine.InputSystem.Controls.ButtonControl; }
            catch { return null; }
        }

        private void NoOpAudio(SmartHealRules rules)
        {
            if (!rules.AudioOnNoOp) return;
            if (Time.unscaledTime - _lastNoOpAudioAt < rules.NoOpAudioCooldownSec) return;
            _lastNoOpAudioAt = Time.unscaledTime;
            PostAudio(rules.NoOpAudioEvent);
        }

        private static void PostAudio(string eventName) => GameRef.PostAudio(eventName);
    }
}
