using System.Reflection;
using Duckov.UI;
using HarmonyLib;
using ItemStatsSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DuckovController.Patches
{
    // INV-3: injects Lock/Unlock into the item operation menu for every editable slot (filled or empty).
    // ItemOperationMenu is a reused singleton; Setup() runs on every open. Postfix creates the button once,
    // reconfigures per open. Setup() early-returns on null item (empty slot) — we do the fix-up it skipped.
    [HarmonyPatch(typeof(ItemOperationMenu), "Setup")]
    internal static class ItemOperationMenuLockPatch
    {
        private const string LockButtonName = "DC_LockButton";

        private static bool _resolved;
        private static FieldInfo? _fiTargetDisplay, _fiIcon, _fiNameText, _fiWeightText, _fiContent;
        private static FieldInfo? _fiUse, _fiSplit, _fiDump, _fiEquip, _fiModify, _fiUnload, _fiWishlist;
        private static MethodInfo? _miRefreshPosition;

        [HarmonyPostfix]
        internal static void Postfix(ItemOperationMenu __instance)
        {
            try
            {
                if (__instance == null) return;
                EnsureReflection();

                var lockBtn = EnsureLockButton(__instance);
                if (lockBtn == null) return;

                var td = _fiTargetDisplay?.GetValue(__instance) as ItemDisplay;
                var entry = td != null ? td.GetComponentInParent<InventoryEntry>() : null;
                bool lockable = entry != null && entry.Master != null && entry.Master.Editable;

                if (!lockable)
                {
                    lockBtn.gameObject.SetActive(false);
                    return;
                }

                bool empty = td == null || td.Target == null;
                if (empty)
                {
                    // Setup() bailed on the null item: hide its (stale) buttons +
                    // header so only Lock shows, and position the menu ourselves.
                    HideStandardButtons(__instance);
                    ClearHeader(__instance);
                }
                else
                {
                    // Setup re-shows 7 managed buttons but NOT icon GO or btn_Wishlist; restore those here.
                    SetActive(_fiIcon, __instance, true);
                    SetActive(_fiWishlist, __instance, true);
                }

                bool locked = false;
                try
                {
                    var inv = entry!.Master?.Target;
                    if (inv != null) locked = inv.IsIndexLocked(entry.Index);
                }
                catch (System.Exception e) { Log.Debug_($"LockPatch.state: {e.Message}"); }
                SetButtonLabel(lockBtn, locked ? "Unlock" : "Lock");
                lockBtn.gameObject.SetActive(true);

                // Position last, after the buttons/header settle (empty only — for a
                // filled slot Setup already called RefreshPosition).
                if (empty) _miRefreshPosition?.Invoke(__instance, null);
            }
            catch (System.Exception e) { Log.Debug_($"ItemOperationMenuLockPatch: {e.Message}"); }
        }

        // Create-once, name-guarded against mod destroy/recreate duplication. Clones btn_Wishlist (safe template).
        private static Button? EnsureLockButton(ItemOperationMenu menu)
        {
            foreach (var b in menu.GetComponentsInChildren<Button>(true))
                if (b != null && b.gameObject.name == LockButtonName) return b;

            var template = _fiWishlist?.GetValue(menu) as Button;
            if (template == null) return null;
            var clone = UnityEngine.Object.Instantiate(template.gameObject, template.transform.parent);
            clone.name = LockButtonName;
            clone.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
            var btn = clone.GetComponent<Button>();
            if (btn == null) { UnityEngine.Object.Destroy(clone); return null; }
            btn.onClick.RemoveAllListeners();   // drop any serialized template handler
            btn.onClick.AddListener(() => OnLockClicked(menu));
            return btn;
        }

        // Reads current target at click time, toggles lock, stays open (mirrors Mark/Wishlist toggle pattern).
        // Single-activation guaranteed by BUG-4 A-press suppression.
        private static void OnLockClicked(ItemOperationMenu menu)
        {
            try
            {
                var td = _fiTargetDisplay?.GetValue(menu) as ItemDisplay;
                var entry = td != null ? td.GetComponentInParent<InventoryEntry>() : null;
                if (entry == null) return;
                entry.ToggleLock();
                var lockBtn = EnsureLockButton(menu);
                if (lockBtn == null) return;
                SetButtonLabel(lockBtn, IsLocked(entry) ? "Unlock" : "Lock");

                // Lock↔Unlock label change reflows layout; rebuild + re-fit focus outline immediately.
                // One-shot on toggle only (in-place resize can't be tracked via focus changes).
                if (_fiContent?.GetValue(menu) is RectTransform content)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                DuckovController.UI.GridFocusController.Instance?.RefreshFocusOutline();
            }
            catch (System.Exception e) { Log.Debug_($"ItemOperationMenuLockPatch.click: {e.Message}"); }
        }

        private static bool IsLocked(InventoryEntry entry)
        {
            try
            {
                var inv = entry.Master?.Target;
                return inv != null && inv.IsIndexLocked(entry.Index);
            }
            catch (System.Exception e) { Log.Debug_($"LockPatch.state: {e.Message}"); return false; }
        }

        private static void HideStandardButtons(ItemOperationMenu menu)
        {
            SetActive(_fiUse, menu, false);
            SetActive(_fiSplit, menu, false);
            SetActive(_fiDump, menu, false);
            SetActive(_fiEquip, menu, false);
            SetActive(_fiModify, menu, false);
            SetActive(_fiUnload, menu, false);
            SetActive(_fiWishlist, menu, false);
        }

        private static void ClearHeader(ItemOperationMenu menu)
        {
            SetActive(_fiIcon, menu, false);
            if (_fiNameText?.GetValue(menu) is TextMeshProUGUI n) n.text = "";
            if (_fiWeightText?.GetValue(menu) is TextMeshProUGUI w) w.text = "";
        }

        private static void SetActive(FieldInfo? fi, ItemOperationMenu menu, bool active)
        {
            if (fi?.GetValue(menu) is Component comp && comp != null)
                comp.gameObject.SetActive(active);
        }

        private static void SetButtonLabel(Button btn, string text)
        {
            var tmp = btn.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp == null) return;
            // Stop the game's localizer from overwriting our label.
            if (tmp.GetComponent("TextLocalizor") is Behaviour loc && loc != null) loc.enabled = false;
            tmp.text = text;
        }

        private static void EnsureReflection()
        {
            if (_resolved) return;
            _resolved = true;
            const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
            var t = typeof(ItemOperationMenu);
            _fiTargetDisplay = t.GetField("TargetDisplay", F);
            _fiIcon       = t.GetField("icon", F);
            _fiNameText   = t.GetField("nameText", F);
            _fiWeightText = t.GetField("weightText", F);
            _fiContent    = t.GetField("contentRectTransform", F);
            _fiUse      = t.GetField("btn_Use", F);
            _fiSplit    = t.GetField("btn_Split", F);
            _fiDump     = t.GetField("btn_Dump", F);
            _fiEquip    = t.GetField("btn_Equip", F);
            _fiModify   = t.GetField("btn_Modify", F);
            _fiUnload   = t.GetField("btn_Unload", F);
            _fiWishlist = t.GetField("btn_Wishlist", F);
            _miRefreshPosition = t.GetMethod("RefreshPosition", F);
        }
    }
}
