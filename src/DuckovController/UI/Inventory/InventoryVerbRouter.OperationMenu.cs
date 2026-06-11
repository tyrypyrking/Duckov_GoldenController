using System;
using Duckov.UI;
using UnityEngine;

namespace DuckovController.UI.Inventory
{
    // Partial: ItemOperationMenu open/close + first-button helper.
    // All members are internal so the IViewVerbMap impls can invoke them.
    internal sealed partial class InventoryVerbRouter
    {
        // Mirrors InventoryEntry.OnItemDisplayPointerClicked right-click path.
        internal void OpenOperationMenu(GameObject focus)
        {
            var entry = focus.GetComponent<InventoryEntry>();
            if (entry == null) return;

            // includeInactive: empty slot's display is still-active child.
            var itemDisplay = entry.GetComponentInChildren<ItemDisplay>(includeInactive: true);
            if (itemDisplay == null) return;
            // Empty slots in EDITABLE inventories can be Lock/Unlocked (INV-3); block loot/merchant.
            if (entry.Content == null && entry.Master?.Editable != true) return;
            _focusBeforeMenu = focus;
            try
            {
                ItemOperationMenu.Show(itemDisplay);
                _pendingFocusOperationMenu = true;
                _pendingOperationMenuAt = Time.unscaledTime;
            }
            catch (Exception e)
            {
                Log.Error($"VerbRouter.OpenOperationMenu failed: {e}");
            }
        }

        internal bool IsOperationMenuOpen()
        {
            if (_operationMenu == null)
                _operationMenu = UnityEngine.Object.FindObjectOfType<ItemOperationMenu>(true);
            return _operationMenu != null && _operationMenu.gameObject.activeInHierarchy;
        }

        internal void CloseOperationMenu()
        {
            if (_operationMenu != null) _operationMenu.gameObject.SetActive(false);
            if (_focusBeforeMenu != null)
            {
                _pendingFocusRestore = true;
                _pendingFocusRestoreAt = Time.unscaledTime;
            }
        }

        internal static GameObject? FindFirstActiveButton(GameObject root)
        {
            var btns = root.GetComponentsInChildren<UnityEngine.UI.Button>(false);
            UnityEngine.UI.Button? best = null;
            float bestY = float.NegativeInfinity;
            foreach (var b in btns)
            {
                if (b == null || !b.interactable || !b.gameObject.activeInHierarchy) continue;
                float y = b.transform.position.y;
                if (y > bestY) { bestY = y; best = b; }
            }
            return best?.gameObject;
        }
    }
}
