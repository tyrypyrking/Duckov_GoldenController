using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DuckovController.Patches
{
    // Lazy type resolver + MonoBehaviour helpers for OptionsPanelPatch.
    // OptionsPanel_Setup_Patch split: Inject.cs (Postfix + stages), Templates.cs (template finders + helpers).
    internal static class OptionsPanelPatch
    {
        // Lazy: Duckov.Options.UI assembly resolution is deferred; don't JIT the ref before Harmony loads the DLLs.
        private static System.Type? _optionsPanelType;

        internal static System.Type? ResolveType()
        {
            if (_optionsPanelType != null) return _optionsPanelType;
            try
            {
                _optionsPanelType = System.Type.GetType(
                    "Duckov.Options.UI.OptionsPanel, TeamSoda.Duckov.Core");
                if (_optionsPanelType == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        var t = asm.GetType("Duckov.Options.UI.OptionsPanel");
                        if (t != null) { _optionsPanelType = t; break; }
                    }
                }
            }
            catch (Exception e) { Log.Warn($"OptionsPanelPatch.ResolveType: {e.Message}"); }
            return _optionsPanelType;
        }

        // Idempotency guard: presence on a candidate means this OptionsPanel already has our tab.
        internal sealed class ControllerTabMarker : MonoBehaviour { }

        // Click handler for the cloned tab. The cloned OptionsPanel_TabButton's onClicked still targets
        // the template's SetSelection — we null it and let this proxy call SetSelection(cloneButton) instead.
        internal sealed class ControllerTabClickProxy : MonoBehaviour, IPointerClickHandler, ISubmitHandler
        {
            internal MonoBehaviour? OptionsPanel;
            internal Component? CloneButton;
            internal GameObject? ContentPanel;

            public void OnPointerClick(PointerEventData eventData)
            {
                eventData?.Use();
                Activate("pointer-click");
            }

            // EventSystem.Submit (controller A or KBM Enter) also lands here
            // so the MenuFocusOverlay's submit-on-A flow can open our tab.
            public void OnSubmit(BaseEventData eventData)
            {
                eventData?.Use();
                Activate("submit");
            }

            private void Activate(string source)
            {
                try
                {
                    if (OptionsPanel == null || CloneButton == null)
                    {
                        Log.Warn($"ControllerTabClickProxy[{source}]: missing OptionsPanel or CloneButton ref.");
                        return;
                    }
                    var t = OptionsPanel.GetType();
                    var m = t.GetMethod("SetSelection",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (m == null) { Log.Warn($"ControllerTabClickProxy[{source}]: SetSelection method not found."); return; }
                    var result = m.Invoke(OptionsPanel, new object[] { CloneButton });
                    bool active = ContentPanel != null && ContentPanel.activeInHierarchy;
                    Log.Info($"ControllerTabClickProxy[{source}]: SetSelection returned {result}; content active={active}.");
                }
                catch (Exception e)
                {
                    Log.Error($"ControllerTabClickProxy[{source}]: {e.Message}");
                }
            }
        }
    }
}
