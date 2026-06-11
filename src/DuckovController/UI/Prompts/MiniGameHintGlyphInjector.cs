using System;
using System.Reflection;
using Duckov.MiniGames;
using DuckovController.UI.Common;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DuckovController.UI.Prompts
{
    // Rewrites GamingConsoleHUD InputIndicators to controller glyphs (pad never pairs → keyboard shown).
    // Start/Select hidden (1:1 mapping adds no info). A→X, B→Y, Axis→DPad, Exit row added.
    // Re-applied after every Refresh and for a short window after console-enter.
    internal sealed class MiniGameHintGlyphInjector : MonoBehaviour
    {
        // D-Pad icon is the reference size; A/B/Exit render at 2× and are pinned to it.
        private static float _refIconH = -1f;

        private bool _subscribedConsole;
        private int _applyFrames;   // re-apply countdown after console-enter

        private void OnEnable()
        {
            InputIndicator.OnAfterRefresh += OnIndicatorRefreshed;
            try
            {
                GamingConsole.OnGamingConsoleInteractChanged += OnConsoleInteract;
                _subscribedConsole = true;
            }
            catch (Exception e) { Log.Debug_($"MiniGameHintGlyphInjector subscribe failed: {e.Message}"); }
        }

        private void OnDisable()
        {
            InputIndicator.OnAfterRefresh -= OnIndicatorRefreshed;
            if (_subscribedConsole)
            {
                try { GamingConsole.OnGamingConsoleInteractChanged -= OnConsoleInteract; } catch { /* ignore */ }
                _subscribedConsole = false;
            }
        }

        private void OnConsoleInteract(bool entering)
        {
            // Re-apply for 12 frames to survive panel fade-in and late refresh.
            if (entering) _applyFrames = 12;
        }

        private void Update()
        {
            if (!DuckovController.Diagnostics.PerfFlags.GlyphInjectors) return;
            if (_applyFrames <= 0) return;
            _applyFrames--;
            ApplyAll();
        }

        // Apply D-Pad row first so its reference size is captured before A/B/Exit rows match it.
        private static void ApplyAll()
        {
            if (Gamepad.current == null) return;
            var hud = UnityEngine.Object.FindObjectOfType<GamingConsoleHUD>();
            if (hud == null) return;
            EnsureExitRow(hud);
            var indicators = hud.GetComponentsInChildren<InputIndicator>(true);
            foreach (var ind in indicators)
                if (RowName(ind.transform) == "Axis") Apply(ind);
            foreach (var ind in indicators)
                if (RowName(ind.transform) != "Axis") Apply(ind);
        }

        // Appends "Exit" row (B glyph) once — native panel has none. Cloned from "B" row.
        private static void EnsureExitRow(GamingConsoleHUD hud)
        {
            try
            {
                var content = hud.transform.Find("Content");
                if (content == null || content.Find("Exit") != null) return;
                var bRow = content.Find("B");
                if (bRow == null) return;
                var clone = UnityEngine.Object.Instantiate(bRow.gameObject, content);
                clone.name = "Exit";
                clone.transform.SetAsLastSibling();
                // The label is the row's direct TMP child (not the InputIndicator's).
                foreach (Transform c in clone.transform)
                {
                    var tmp = c.GetComponent<TMPro.TextMeshProUGUI>();
                    if (tmp != null && c.GetComponent<InputIndicator>() == null)
                    {
                        var loc = c.GetComponent("TextLocalizor") as MonoBehaviour;
                        if (loc != null) loc.enabled = false;   // don't let it overwrite "Exit"
                        tmp.text = "Exit";
                        break;
                    }
                }
            }
            catch (Exception e) { Log.Debug_($"MiniGameHintGlyphInjector.EnsureExitRow: {e.Message}"); }
        }

        private void OnIndicatorRefreshed(InputIndicator ind)
        {
            if (!DuckovController.Diagnostics.PerfFlags.GlyphInjectors) return;
            if (ind == null) return;
            if (Gamepad.current == null) return;
            if (ind.GetComponentInParent<GamingConsoleHUD>() == null) return;
            Apply(ind);
        }

        private static void Apply(InputIndicator ind)
        {
            try
            {
                if (ind == null) return;
                var rowT = RowTransform(ind.transform, out string row);
                switch (row)
                {
                    case "Start":
                    case "Select":
                        // 1:1 mapping adds no info — hide the whole row.
                        if (rowT != null) rowT.gameObject.SetActive(false);
                        return;
                    case "A":    SetGlyph(ind, ButtonGlyph.X, isReference: false); return; // X → MiniGameA
                    case "B":    SetGlyph(ind, ButtonGlyph.Y, isReference: false); return; // Y → MiniGameB
                    case "Exit": SetGlyph(ind, ButtonGlyph.B, isReference: false); return; // B/East exits
                    case "Axis":
                        // Collapse 4 WASD indicators to one D-Pad (row label already reads "D-Pad").
                        // First sibling is the reference size; others are hidden.
                        if (ind.transform.parent != null && ind.transform.GetSiblingIndex() == 0)
                            SetGlyph(ind, ButtonGlyph.DPad, isReference: true);
                        else
                            ind.gameObject.SetActive(false);
                        return;
                }
            }
            catch (Exception e)
            {
                Log.Debug_($"MiniGameHintGlyphInjector.Apply: {e.Message}");
            }
        }

        // Swaps indicator to controller glyph, left-aligns, and pins a uniform square box.
        // Without this, glyphs render at native opaque size (button=96px, DPad=48px) and
        // centre in a label-width-varying box → misaligned. isReference=true for D-Pad (sets shared size).
        private static void SetGlyph(InputIndicator ind, ButtonGlyph glyph, bool isReference)
        {
            var sprite = GlyphProvider.Get(glyph);
            if (sprite == null) return;

            var icon          = InputIndicatorFields.Icon(ind);
            var textContainer = InputIndicatorFields.TextContainer(ind);
            var text          = InputIndicatorFields.Text(ind);
            if (icon == null) return;

            icon.sprite = sprite;
            icon.preserveAspect = true;
            textContainer?.SetActive(false);
            icon.gameObject.SetActive(true);
            text?.gameObject.SetActive(false);

            if (isReference)
            {
                float h = icon.rectTransform.rect.height;
                if (h > 0f) _refIconH = h;
            }

            // Left-align so every row's glyph starts at the same edge.
            var hlg = ind.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
            {
                hlg.childAlignment = TextAnchor.MiddleLeft;
                hlg.childControlWidth = true;
                hlg.childForceExpandWidth = false;
            }

            // Uniform square box: flexibleWidth=0 is critical — without it the box
            // stretches to fill the row and the centred glyph drifts right by half the slack.
            var le = icon.GetComponent<LayoutElement>();
            if (le != null && _refIconH > 0f)
            {
                le.preferredHeight = _refIconH;
                le.preferredWidth  = _refIconH;
                le.flexibleWidth   = 0f;
            }
        }

        private static string RowName(Transform t)
        {
            RowTransform(t, out string name);
            return name;
        }

        // Direct child of "Content" that owns this indicator, plus its name.
        private static Transform? RowTransform(Transform t, out string name)
        {
            name = "";
            for (int i = 0; i < 8 && t != null; i++)
            {
                var p = t.parent;
                if (p == null) break;
                if (p.name == "Content") { name = t.name; return t; }
                t = p;
            }
            return null;
        }

    }
}
