using Duckov.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DuckovController.UI.Inventory
{
    // A-button carry state machine.
    //   Idle        → A-down → Pressing(t0)
    //   Pressing    → quick A-up → Transfer+Idle; held past threshold+carriable → Carrying; B → Idle
    //   Carrying    → A-down over slot → Place+Idle; B → CancelCarry+Idle; X → no-op; view-close → Cancel+Idle
    //
    // Transfer: synth-hover + InventoryDisplay.NotifyItemDoubleClicked (fast-pick path).
    // Place: PointerEventDispatcher BeginDrag/EndDrag synth-drag.
    internal sealed class InventoryCarryState
    {
        internal enum Phase { Idle, Pressing, Carrying }
        internal enum CarryResult { None, Transfer, Place }

        internal Phase Current { get; private set; } = Phase.Idle;
        internal float HoldThresholdSec = 0.25f;

        private float _pressStart;
        private GameObject? _carrySource;
        private PointerEventData? _carryPed;

        // Idle → Pressing (None). Carrying → EndDrag+Idle (Place).
        internal CarryResult OnAPressed(GameObject? focus)
        {
            if (Current == Phase.Idle)
            {
                _pressStart = Time.unscaledTime;
                Current = Phase.Pressing;
                return CarryResult.None;
            }
            if (Current == Phase.Carrying)
            {
                PointerEventDispatcher.EndDrag(_carryPed, focus);
                _carrySource = null;
                _carryPed = null;
                Current = Phase.Idle;
                DuckovController.Haptics.HapticEngine.Instance?.Play(DuckovController.Haptics.HapticCue.CarryPlace);
                Log.Debug_("Haptic: CarryPlace");
                return CarryResult.Place;
            }
            return CarryResult.None;
        }

        // Pressing → Carrying once hold threshold passes and focus is carriable.
        internal void OnAHeldTick(GameObject? focus)
        {
            if (Current != Phase.Pressing) return;
            if (focus == null) return;
            if (Time.unscaledTime - _pressStart < HoldThresholdSec) return;
            if (!IsCarriable(focus)) return;

            _carrySource = focus;
            _carryPed = PointerEventDispatcher.BeginDrag(focus);
            Current = Phase.Carrying;
            DuckovController.Haptics.HapticEngine.Instance?.Play(DuckovController.Haptics.HapticCue.CarryPickup);
            Log.Debug_("Haptic: CarryPickup");
        }

        // Pressing quick-tap → Transfer. Carrying release → None (stay in carry until next A-press places).
        internal CarryResult OnAReleased(GameObject? focus)
        {
            if (Current == Phase.Pressing)
            {
                bool quickTap = (Time.unscaledTime - _pressStart) < HoldThresholdSec;
                Current = Phase.Idle;
                return quickTap ? CarryResult.Transfer : CarryResult.None;
            }
            // Carrying: release is a no-op — stay in Carry until next A-press places.
            return CarryResult.None;
        }

        // Update synth-drag destination as focus moves during Carrying.
        internal void OnFocusChanged(GameObject? newFocus)
        {
            if (Current == Phase.Carrying && _carryPed != null)
            {
                PointerEventDispatcher.DragOver(_carryPed, newFocus);
            }
        }

        // Abandon synth-drag without a drop. EndDrag(null) fires only endDragHandler (not dropHandler);
        // InventoryEntry.OnEndDrag is empty, so the item stays in its origin slot.
        internal void Cancel()
        {
            if (Current == Phase.Carrying && _carryPed != null)
            {
                PointerEventDispatcher.EndDrag(_carryPed, null);
            }
            _carrySource = null;
            _carryPed = null;
            Current = Phase.Idle;
        }

        private static bool IsCarriable(GameObject focus)
        {
            // Same test as GridFocusController.IsDraggable.
            return focus.GetComponent<IBeginDragHandler>() != null;
        }
    }
}
