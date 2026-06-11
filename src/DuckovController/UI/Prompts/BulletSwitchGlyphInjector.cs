using System;
using System.Reflection;
using DuckovController.UI.Common;
using UnityEngine;
using UnityEngine.UI;

namespace DuckovController.UI.Prompts
{
    // Subscribes to InputIndicator.OnAfterRefresh and, whenever the refreshed
    // indicator belongs to a BulletTypeHUD and a gamepad is connected, forces
    // it to show the Dpad-up controller glyph instead of the keyboard "T" text.
    // Self-subscribing MonoBehaviour — no external Bind() call needed.
    internal sealed class BulletSwitchGlyphInjector : MonoBehaviour
    {
        private void OnEnable()
        {
            InputIndicator.OnAfterRefresh += OnIndicatorRefreshed;
        }

        private void OnDisable()
        {
            InputIndicator.OnAfterRefresh -= OnIndicatorRefreshed;
        }

        private void OnIndicatorRefreshed(InputIndicator ind)
        {
            try
            {
                if (!DuckovController.Diagnostics.PerfFlags.GlyphInjectors) return;
                if (ind == null) return;
                if (UnityEngine.InputSystem.Gamepad.current == null) return;
                if (ind.GetComponentInParent<BulletTypeHUD>() == null) return;

                var sprite = GlyphProvider.Get(ButtonGlyph.DPadUp);
                if (sprite == null) return;

                var icon          = InputIndicatorFields.Icon(ind);
                var textContainer = InputIndicatorFields.TextContainer(ind);
                var text          = InputIndicatorFields.Text(ind);

                if (icon == null) return;

                icon.sprite         = sprite;
                icon.preserveAspect = true;

                // Replicate InputIndicator.ShowIcon():
                textContainer?.SetActive(false);
                icon.gameObject.SetActive(true);
                text?.gameObject.SetActive(false);
            }
            catch (Exception e)
            {
                Log.Debug_($"BulletSwitchGlyphInjector: exception in OnIndicatorRefreshed: {e.Message}");
            }
        }
    }
}
