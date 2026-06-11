using System.Collections.Generic;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DuckovController.UI.Menu
{
    // Cosmetic: swaps the arrow icon on menu "Return" / "Cancel" back buttons for a
    // controller glyph while a gamepad is connected. Never touches button behaviour.
    internal sealed class MenuBackGlyphInjector : MonoBehaviour
    {
        // Scopes with no glyphable back button — skip scan entirely.
        private static readonly HashSet<MenuScope> _skipScopes = new()
        {
            MenuScope.None,
            MenuScope.DropdownPopup,
        };

        // Back button names (confirmed from deck dumps): Options/ModManager/Credits = "Return",
        // Save Slots = "Cancel". Both are icon-only buttons (single "Image" child);
        // text confirm buttons with a Text child are excluded by the child-"Image" gate below.
        private static readonly string[] _backButtonNames = { "Return", "Cancel" };

        private MenuFocusOverlay? _overlay;

        // Some panels open without a scope change (e.g. Save Slots stays Generic),
        // so OnScopeChanged doesn't fire — throttled re-scan catches those.
        private const float ScanInterval = 0.25f;
        private float _nextScanTime;

        // State-change guard so the poll doesn't spam the log every interval.
        private Image? _lastLogged;

        // Suppresses repeated "no menu root" log lines during gameplay.
        private bool _noRootLogged;

        // All back buttons normalized to this size via localScale = target / rect.width.
        // "Return" icon box is ~30px; Save Slots' "Cancel" is ~88px — normalizing makes
        // them match regardless of host size.
        private const float GlyphTargetPx = 32f;

        private readonly struct OriginalState
        {
            public readonly Sprite? Sprite;
            public readonly Vector3 LocalScale;
            public OriginalState(Sprite? sprite, Vector3 localScale)
            {
                Sprite = sprite;
                LocalScale = localScale;
            }
        }

        // Image -> its original sprite + transform scale, so we restore exactly.
        private readonly Dictionary<Image, OriginalState> _originals = new();

        internal void Bind(MenuFocusOverlay overlay)
        {
            if (_overlay != null) _overlay.OnScopeChanged -= OnScopeChanged;
            _overlay = overlay;
            _overlay.OnScopeChanged += OnScopeChanged;
        }

        private void OnEnable() => InputSystem.onDeviceChange += OnDeviceChange;

        private void OnDisable()
        {
            InputSystem.onDeviceChange -= OnDeviceChange;
            RevertAll();
        }

        private void OnDestroy()
        {
            if (_overlay != null) _overlay.OnScopeChanged -= OnScopeChanged;
            RevertAll();
        }

        private void OnDeviceChange(InputDevice device, InputDeviceChange change)
            => Reevaluate();

        private void OnScopeChanged(MenuScope scope) => Reevaluate();

        private void Update()
        {
            if (!DuckovController.Diagnostics.PerfFlags.BackGlyph) return;
            if (_overlay == null) return;
            if (_skipScopes.Contains(_overlay.ActiveScope)) return;
            if (Time.unscaledTime < _nextScanTime) return;
            _nextScanTime = Time.unscaledTime + ScanInterval;
            Reevaluate();
        }

        private void Reevaluate()
        {
            if (_overlay == null) return;

            // Exception here propagates into overlay scope handling — swallow + log.
            try
            {
                var scope = _overlay.ActiveScope;
                if (_skipScopes.Contains(scope))
                {
                    RevertAll();
                    return;
                }

                var root = _overlay.CurrentMenuRoot;
                if (root == null)
                {
                    if (!_noRootLogged)
                    {
                        Log.Debug_($"MenuBackGlyphInjector: scope={scope}, no menu root.");
                        _noRootLogged = true;
                    }
                    return;
                }
                _noRootLogged = false;

                // Scan from canvas root: Save Slots' "Cancel" lives under SaveFileSelection,
                // a sibling of MainMenuContainer. Active + name + child-"Image" gate prevents false matches.
                var image = FindBackButtonImage(root.root);
                if (image == null)
                {
                    if (_lastLogged != null)
                        Log.Debug_($"MenuBackGlyphInjector: scope={scope}, no back button matched.");
                    _lastLogged = null;
                    return;
                }

                if (image != _lastLogged)
                {
                    Log.Debug_($"MenuBackGlyphInjector: scope={scope}, matched '{image.transform.parent?.name}' under '{image.transform.parent?.parent?.name}'.");
                    _lastLogged = image;
                }
                if (Gamepad.current != null)
                    ApplyGlyph(image, ButtonGlyph.B);
                else
                    Revert(image);
            }
            catch (System.Exception e)
            {
                Log.Warn($"MenuBackGlyphInjector.Reevaluate failed: {e.Message}");
            }
        }

        // Finds the icon Image child of an active "Return"/"Cancel" Button.
        // Does NOT require FadeGroupButton — ModManager's Return has only Button+ButtonAnimation.
        // Child named "Image" excludes text confirm buttons (which have a Text child).
        private static Image? FindBackButtonImage(Transform root)
        {
            foreach (var btn in root.GetComponentsInChildren<Button>(includeInactive: false))
            {
                var go = btn.gameObject;
                if (System.Array.IndexOf(_backButtonNames, go.name) < 0) continue;
                var child = go.transform.Find("Image");
                if (child == null) continue;
                var img = child.GetComponent<Image>();
                if (img != null) return img;
            }
            return null;
        }

        private void ApplyGlyph(Image image, ButtonGlyph glyph)
        {
            var sprite = GlyphProvider.Get(glyph);
            if (sprite == null) return;
            var rt = image.rectTransform;
            if (!_originals.ContainsKey(image))
                _originals[image] = new OriginalState(image.sprite, rt.localScale);
            if (image.sprite != sprite)
            {
                image.sprite = sprite;
                image.preserveAspect = true;
            }
            // Recomputed each call so it self-corrects once layout resolves rect.width.
            float w = rt.rect.width;
            if (w > 1f)
                rt.localScale = _originals[image].LocalScale * (GlyphTargetPx / w);
        }

        private void Revert(Image image)
        {
            if (_originals.TryGetValue(image, out var original))
            {
                image.sprite = original.Sprite;
                image.rectTransform.localScale = original.LocalScale;
                _originals.Remove(image);
                Log.Debug_($"MenuBackGlyphInjector: reverted glyph on '{image.gameObject.name}'.");
            }
        }

        private void RevertAll()
        {
            foreach (var kv in _originals)
            {
                if (kv.Key != null)
                {
                    kv.Key.sprite = kv.Value.Sprite;
                    kv.Key.rectTransform.localScale = kv.Value.LocalScale;
                }
            }
            _originals.Clear();
        }
    }
}
