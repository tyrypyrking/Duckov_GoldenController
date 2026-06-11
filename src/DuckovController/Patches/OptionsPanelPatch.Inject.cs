using System;
using System.Collections;
using System.Reflection;
using DuckovController.UI.Settings;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DuckovController.Patches
{
    // Harmony binding and staged InjectSingleTab implementation.
    // The Postfix is thin — it delegates to InjectSingleTab with the
    // "Controller Mod" content builder. To inject a second tab, add another
    // InjectSingleTab call in Postfix (Decision 11).
    [HarmonyPatch]
    internal static partial class OptionsPanel_Setup_Patch
    {
        // Discover the target method at patch-resolve time rather than via
        // [HarmonyPatch(typeof(...), "...")] so Duckov.Options.UI is touched
        // only via reflection.
        [HarmonyTargetMethod]
        private static MethodBase? TargetMethod()
        {
            var t = OptionsPanelPatch.ResolveType();
            if (t == null)
            {
                Log.Warn("OptionsPanelPatch: Duckov.Options.UI.OptionsPanel type not found; tab injection disabled.");
                return null;
            }
            var m = t.GetMethod("Setup",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                binder: null, types: Type.EmptyTypes, modifiers: null);
            if (m == null)
                Log.Warn("OptionsPanelPatch: OptionsPanel.Setup() not found via reflection.");
            return m;
        }

        [HarmonyPostfix]
        private static void Postfix(MonoBehaviour __instance)
        {
            try
            {
                // Deferred: Setup runs from Start before layout pass + TrueShadow shadow GOs exist.
                // BuildControllerTabContent attaches a deferred builder (waits 2 frames, forces rebuild).
                InjectSingleTab(__instance, "Controller Mod",
                    contentRect => BuildControllerTabContent(__instance, contentRect));
            }
            catch (Exception e)
            {
                Log.Error($"OptionsPanelPatch: InjectSingleTab failed: {e}");
            }
        }

        // Generalised single-tab injector. contentBuilder receives the stripped content RectTransform.
        internal static void InjectSingleTab(
            MonoBehaviour panel,
            string tabName,
            Action<RectTransform> contentBuilder)
        {
            var (cloneGo, marker, tabButtons, cloneButton) = CloneTabButton(panel, tabName);
            if (cloneGo == null || marker == null || cloneButton == null) return;

            var contentRect = RebindTabContent(panel, cloneButton, cloneGo, tabButtons);
            if (contentRect == null)
            {
                UnityEngine.Object.Destroy(cloneGo);
                return;
            }

            StripContentChildren(contentRect);

            SetButtonLabel(cloneGo, tabName);

            // Hide the cloned tab's icon Image — it carries Common's localized
            // icon sprite, which made our tab look like a second "Common" tab.
            for (int i = 0; i < cloneGo.transform.childCount; i++)
            {
                var child = cloneGo.transform.GetChild(i);
                if (child.name == "Image") { child.gameObject.SetActive(false); break; }
            }

            WireClickProxy(cloneGo, cloneButton, panel);

            RegisterTabAndLaunchBuilder(panel, tabButtons, cloneButton, contentRect, contentBuilder);
        }

        // Stage 1: clone tabButtons[0], stamp marker, return clone + marker + tabButtons + cloneButton.
        // Returns (null,null,null,null) on failure or if already injected.
        private static (GameObject? cloneGo, OptionsPanelPatch.ControllerTabMarker? marker,
            IList? tabButtons, Component? cloneButton)
            CloneTabButton(MonoBehaviour optionsPanel, string tabName)
        {
            var t = optionsPanel.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            var tabButtonsField = t.GetField("tabButtons", flags);
            if (tabButtonsField == null)
            {
                Log.Warn("OptionsPanelPatch: tabButtons field not found.");
                return (null, null, null, null);
            }

            var rawList = tabButtonsField.GetValue(optionsPanel);
            if (rawList is not IList tabButtons || tabButtons.Count == 0)
            {
                Log.Warn("OptionsPanelPatch: tabButtons empty or wrong type.");
                return (null, null, null, null);
            }

            // Idempotency: scan existing tabs for the marker; bail if found.
            foreach (var existing in tabButtons)
            {
                if (existing is Component c && c.GetComponent<OptionsPanelPatch.ControllerTabMarker>() != null)
                    return (null, null, null, null);
            }

            var template = tabButtons[0] as Component;
            if (template == null)
            {
                Log.Warn("OptionsPanelPatch: tabButtons[0] is not a Component.");
                return (null, null, null, null);
            }

            var cloneGo = UnityEngine.Object.Instantiate(
                template.gameObject, template.transform.parent, worldPositionStays: false);
            cloneGo.name = "ControllerModTab";
            // Stamp before later stages can throw so idempotency holds.
            var marker = cloneGo.AddComponent<OptionsPanelPatch.ControllerTabMarker>();

            var tabButtonType = template.GetType();
            var cloneButton = cloneGo.GetComponent(tabButtonType);
            if (cloneButton == null)
            {
                Log.Warn("OptionsPanelPatch: clone missing OptionsPanel_TabButton component.");
                UnityEngine.Object.Destroy(cloneGo);
                return (null, null, null, null);
            }

            return (cloneGo, marker, tabButtons, cloneButton);
        }

        // Stage 2: ensure clone's `tab` field points at a fresh content GO (Instantiate doesn't redirect
        // out-of-hierarchy SerializeField refs). Also rebinds selectedIndicator when possible. Null on failure.
        private static RectTransform? RebindTabContent(
            MonoBehaviour optionsPanel,
            Component cloneButton,
            GameObject cloneGo,
            IList tabButtons)
        {
            var template = tabButtons[0] as Component;
            if (template == null) return null;

            var tabButtonType = template.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            var tabField = tabButtonType.GetField("tab", flags);
            var selectedIndicatorField = tabButtonType.GetField("selectedIndicator", flags);

            if (tabField == null)
            {
                Log.Warn("OptionsPanelPatch: OptionsPanel_TabButton.tab field not found.");
                return null;
            }

            // Determine the content GameObject — may already be a fresh clone
            // (Instantiate remapped it) or still pointing at the original.
            GameObject clonedContentGo;
            var clonedContentObj = tabField.GetValue(cloneButton);
            var origContentObj = tabField.GetValue(template);

            if (clonedContentObj is GameObject cGo
                && origContentObj is GameObject oGo
                && cGo != oGo)
            {
                // Instantiate successfully remapped to a new object.
                clonedContentGo = cGo;
                clonedContentGo.name = "ControllerModTabContent";
                tabField.SetValue(cloneButton, clonedContentGo);
            }
            else
            {
                // Still pointing at the original (or null) — instantiate manually.
                if (origContentObj is not GameObject origContentGo)
                {
                    Log.Warn("OptionsPanelPatch: original tab content GameObject not found.");
                    return null;
                }
                clonedContentGo = UnityEngine.Object.Instantiate(
                    origContentGo, origContentGo.transform.parent, worldPositionStays: false);
                clonedContentGo.name = "ControllerModTabContent";
                tabField.SetValue(cloneButton, clonedContentGo);
            }

            // Rebind selectedIndicator only if Instantiate didn't remap it (Unity auto-remaps in-hierarchy refs).
            // If still pointing at the template's indicator, find a local candidate; else leave shared (visual quirk).
            if (selectedIndicatorField != null)
            {
                var origIndicator = selectedIndicatorField.GetValue(template) as GameObject;
                var cloneIndicator = selectedIndicatorField.GetValue(cloneButton) as GameObject;
                if (cloneIndicator == origIndicator && origIndicator != null)
                {
                    var local = FindChildIndicator(cloneGo);
                    if (local != null) selectedIndicatorField.SetValue(cloneButton, local);
                }
            }

            return clonedContentGo.transform as RectTransform
                ?? clonedContentGo.AddComponent<RectTransform>();
        }

        // Stage 3: wipe cloned content children + non-essential components. Keeps RectTransform, Image, CanvasRenderer, Transform.
        private static void StripContentChildren(RectTransform contentRect)
        {
            var go = contentRect.gameObject;
            for (int i = go.transform.childCount - 1; i >= 0; i--)
            {
                var child = go.transform.GetChild(i).gameObject;
                UnityEngine.Object.DestroyImmediate(child);
            }
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                if (comp is RectTransform || comp is CanvasRenderer || comp is Image) continue;
                if (comp is Transform) continue;
                try { UnityEngine.Object.DestroyImmediate(comp); } catch { /* tolerated */ }
            }
        }

        // Stage 4: null inherited onClicked (targets template) and attach ControllerTabClickProxy.
        private static void WireClickProxy(
            GameObject cloneGo,
            Component cloneButton,
            MonoBehaviour optionsPanel)
        {
            var tabButtonType = cloneButton.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            var onClickedField = tabButtonType.GetField("onClicked", flags);
            try { onClickedField?.SetValue(cloneButton, null); }
            catch (Exception e) { Log.Warn($"OptionsPanelPatch: could not null onClicked: {e.Message}"); }

            // The content panel ref is obtained from the tab field for the proxy.
            var tabField = tabButtonType.GetField("tab", flags);
            var contentPanel = tabField?.GetValue(cloneButton) as GameObject;

            var clickProxy = cloneGo.AddComponent<OptionsPanelPatch.ControllerTabClickProxy>();
            clickProxy.OptionsPanel = optionsPanel;
            clickProxy.CloneButton  = cloneButton;
            clickProxy.ContentPanel = contentPanel;
        }

        // Stage 5: add clone to tabButtons, invoke content builder, hide content panel (Setup's SetSelection already ran),
        // attach tab cycler, reset selection to tabButtons[0] to clear inherited highlight.
        private static void RegisterTabAndLaunchBuilder(
            MonoBehaviour optionsPanel,
            IList tabButtons,
            Component cloneButton,
            RectTransform contentRect,
            Action<RectTransform> contentBuilder)
        {
            // Register so SetSelection hides our content when another tab is clicked.
            tabButtons.Add(cloneButton);

            contentBuilder(contentRect);

            // Hide content (added after Setup's SetSelection ran; game didn't hide it).
            contentRect.gameObject.SetActive(false);

            // Attach tab cycler for LB/RB/LT/RT cycling. Idempotent.
            var tabButtonType = cloneButton.GetType();
            if (optionsPanel.gameObject.GetComponent<OptionsPanelTabCycler>() == null)
            {
                var cycler = optionsPanel.gameObject.AddComponent<OptionsPanelTabCycler>();
                cycler.OptionsPanel = optionsPanel;
                cycler.TabButtonType = tabButtonType;
            }

            // Reset to tabButtons[0] (Common) to clear the selection highlight inherited via Instantiate.
            try
            {
                var setSelMethod = optionsPanel.GetType().GetMethod("SetSelection",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (setSelMethod != null && tabButtons.Count > 0)
                {
                    setSelMethod.Invoke(optionsPanel, new[] { tabButtons[0] });
                    Log.Info("OptionsPanelPatch: reset selection to tabButtons[0] post-inject.");
                }
            }
            catch (Exception e) { Log.Warn($"OptionsPanelPatch: reset selection failed: {e.Message}"); }

            var cloneGo = cloneButton.gameObject;
            Log.Info($"OptionsPanelPatch: tab injected. "
                + $"button-parent={cloneGo.transform.parent?.name ?? "<null>"}, "
                + $"content-parent={contentRect.transform.parent?.name ?? "<null>"}, "
                + $"content-active={contentRect.gameObject.activeSelf}, "
                + $"content-children={contentRect.transform.childCount}, "
                + $"tabButtons.Count={tabButtons.Count}.");
        }
    }
}
