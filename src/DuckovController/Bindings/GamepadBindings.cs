using System;
using System.Collections.Generic;
using DuckovController.Config;
using UnityEngine.InputSystem;

namespace DuckovController.Bindings
{
    // Adds Unity InputSystem bindings to the game's action asset. No Harmony, no per-frame polling.
    internal static class GamepadBindings
    {
        // Track by Guid: composite-safe (each part binding has its own id) + asset-swap-safe (missing ids = no-op Remove).
        private static readonly List<(InputAction action, Guid id)> _added = new();

        internal static void Apply(InputActionAsset actions, ControllerConfig cfg)
        {
            if (actions == null)
            {
                Log.Warn("GamepadBindings.Apply: actions is null, skipping");
                return;
            }
            Remove(actions);

            var b = cfg.Bindings;

            // UI_NextPage/UI_PreviousPage removed (BUG-1): RB/LB now fire the game's page
            // events directly from InventoryVerbRouter.Tick (see UIPageEvents).

            // Redundant with Harmony direct-drive (GameplayInputDriverPatch/AimDriverPatch) + direct menu polling;
            // InputSystem still processes all of these every frame (~5 ms). Opt-in only via Perf.ApplyGamepadBindings.
            if (cfg.Perf.ApplyGamepadBindings)
            {
                // Gameplay verbs.
                AddSimple(actions, "MoveAxis", b.MoveAxis, processors: "stickDeadzone(min=0.18,max=0.95)");
                AddSimple(actions, "Run", b.Run);
                AddSimple(actions, "ADS", b.ADS, processors: "axisDeadzone(min=0.25)");
                AddSimple(actions, "Trigger", b.Trigger, processors: "axisDeadzone(min=0.25)");
                AddSimple(actions, "Dash", b.Dash);
                AddSimple(actions, "Reload", b.Reload);
                AddSimple(actions, "Interact", b.Interact);
                AddSimple(actions, "PutAway", b.PutAway);
                AddSimple(actions, "CancelSkill", b.CancelSkill);
                AddSimple(actions, "UI_Inventory", b.UI_Inventory);
                AddSimple(actions, "UI_Map", b.UI_Map);
                AddSimple(actions, "ToggleNightVision", b.ToggleNightVision);
                AddSimple(actions, "ToggleView", b.ToggleView);
                AddSimple(actions, "Quack", b.Quack);
                AddSimple(actions, "StopAction", b.StopAction);

                // Item shortcut row 1-2 (D-pad cardinal; ItemShortcut_Melee on up).
                AddSimple(actions, "ItemShortcut1", "<Gamepad>/dpad/left");
                AddSimple(actions, "ItemShortcut2", "<Gamepad>/dpad/right");
                AddSimple(actions, "ItemShortcut_Melee", "<Gamepad>/dpad/up");

                // 1D Axis composites so the existing handlers (read float, sign=direction)
                // get a meaningful value.
                AddOneDAxis(actions, "SwitchWeapon", b.SwitchWeaponNegative, b.SwitchWeaponPositive);
                AddOneDAxis(actions, "SwitchInteractAndBulletType",
                    b.SwitchInteractAndBulletTypeNegative, b.SwitchInteractAndBulletTypePositive);

                // UI mode actions.
                AddSimple(actions, "UI_Confirm", b.UI_Confirm);
                AddSimple(actions, "UI_Cancel", b.UI_Cancel);
                AddSimple(actions, "Click", b.Click);
                AddSimple(actions, "UI_Item_Drop", b.UI_Item_Drop);
                AddSimple(actions, "UI_Item_use", b.UI_Item_use);

                // UI_Navigate — 2D Vector composite from dpad.
                AddTwoDVector(actions, "UI_Navigate",
                    b.UI_Navigate_Up, b.UI_Navigate_Down, b.UI_Navigate_Left, b.UI_Navigate_Right);
            }

            // MiniGame* actions intentionally NOT bound: PlayerInput stays KeyAndMouse on the Deck so bindings
            // don't resolve; MiniGameInputGate direct-drives from Gamepad.current instead. Binding would risk double-drive.

            // Runtime-added bindings require a disable/enable cycle to participate in binding resolution;
            // without it AddBinding succeeds but the callback never fires.
            var maps = new System.Collections.Generic.HashSet<InputActionMap>();
            foreach (var (action, _) in _added)
            {
                if (action?.actionMap != null) maps.Add(action.actionMap);
            }
            foreach (var map in maps)
            {
                try
                {
                    var wasEnabled = map.enabled;
                    if (wasEnabled) map.Disable();
                    if (wasEnabled) map.Enable();
                }
                catch (Exception e)
                {
                    Log.Warn($"Failed to re-enable action map {map.name}: {e.Message}");
                }
            }

            Log.Info($"Applied {_added.Count} gamepad binding entries across {maps.Count} action maps.");
        }

        internal static void Remove(InputActionAsset actions)
        {
            if (actions == null) { _added.Clear(); return; }
            // Re-resolve idx by id each time so multiple erases on the same action handle index-shifting.
            foreach (var (action, id) in _added)
            {
                if (action == null) continue;
                try
                {
                    var idx = -1;
                    var bindings = action.bindings;
                    for (int i = 0; i < bindings.Count; i++)
                    {
                        if (bindings[i].id == id) { idx = i; break; }
                    }
                    if (idx < 0) continue;  // already gone (asset swapped)
                    action.ChangeBinding(idx).Erase();
                }
                catch (Exception e)
                {
                    Log.Debug_($"Erase binding on {action.name} failed: {e.Message}");
                }
            }
            _added.Clear();
        }

        // Scan newly-appended bindings (index >= beforeCount) and record their ids.
        private static void Track(InputAction action, int beforeCount)
        {
            var bindings = action.bindings;
            for (int i = beforeCount; i < bindings.Count; i++)
            {
                _added.Add((action, bindings[i].id));
            }
        }

        private static void AddSimple(InputActionAsset actions, string actionName, string path,
            string? processors = null)
        {
            if (string.IsNullOrEmpty(path)) return;
            var action = actions.FindAction(actionName, throwIfNotFound: false);
            if (action == null)
            {
                Log.Debug_($"Action '{actionName}' not found, skipping binding {path}");
                return;
            }
            try
            {
                var before = action.bindings.Count;
                var syntax = action.AddBinding(path);
                if (!string.IsNullOrEmpty(processors)) syntax.WithProcessor(processors);
                Track(action, before);
            }
            catch (Exception e)
            {
                Log.Warn($"AddBinding({actionName}, {path}) failed: {e.Message}");
            }
        }

        private static void AddOneDAxis(InputActionAsset actions, string actionName,
            string negativePath, string positivePath)
        {
            var action = actions.FindAction(actionName, throwIfNotFound: false);
            if (action == null) return;
            if (string.IsNullOrEmpty(negativePath) || string.IsNullOrEmpty(positivePath)) return;
            try
            {
                var before = action.bindings.Count;
                action.AddCompositeBinding("1DAxis")
                    .With("Negative", negativePath)
                    .With("Positive", positivePath);
                Track(action, before);
            }
            catch (Exception e)
            {
                Log.Warn($"AddCompositeBinding 1DAxis({actionName}) failed: {e.Message}");
            }
        }

        private static void AddTwoDVector(InputActionAsset actions, string actionName,
            string upPath, string downPath, string leftPath, string rightPath)
        {
            var action = actions.FindAction(actionName, throwIfNotFound: false);
            if (action == null) return;
            if (string.IsNullOrEmpty(upPath) || string.IsNullOrEmpty(downPath) ||
                string.IsNullOrEmpty(leftPath) || string.IsNullOrEmpty(rightPath)) return;
            try
            {
                var before = action.bindings.Count;
                action.AddCompositeBinding("2DVector")
                    .With("Up", upPath)
                    .With("Down", downPath)
                    .With("Left", leftPath)
                    .With("Right", rightPath);
                Track(action, before);
            }
            catch (Exception e)
            {
                Log.Warn($"AddCompositeBinding 2DVector({actionName}) failed: {e.Message}");
            }
        }
    }
}
