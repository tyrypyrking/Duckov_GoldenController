using System;
using System.Collections.Generic;
using System.Reflection;
using Duckov.UI.Animations;
using DuckovController.UI.Common;
using DuckovController.UI.Prompts;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DuckovController.UI.Menu
{
    // Modal intercept: ConfirmDialogue (Quit/exit), mod-changed warning, raid-exit ClosureView + A/B glyphs.
    internal sealed partial class MenuFocusOverlay : MonoBehaviour
    {
        private static FieldInfo? _confirmBtnField;
        private static FieldInfo? _cancelBtnField;
        private static FieldInfo? _fadeGroupField;

        // Global ConfirmDialogue cache (lives outside menu root). FindObjectsOfType once; per-frame
        // reads only activeInHierarchy + fadeGroup.IsShown. Rescan occasionally for late instantiation.
        private static ConfirmDialogue[]? _cachedConfirms;
        private static float _lastConfirmScan = -10f;

        private ConfirmDialogue? TryFindActiveConfirmDialogueGlobal()
        {
            if (Gamepad.current == null) return null;
            // ConfirmDialogue only appears from menu/pause/View context. Skip in pure gameplay:
            // was ~1.2 ms avg + ~25 ms hitch (worstMs 41→16 when disabled).
            if (!_mainMenuActive && !_pauseShown && Duckov.UI.View.ActiveView == null)
            {
                _cachedConfirms = null;
                return null;
            }
            EnsureConfirmReflection();
            bool empty = _cachedConfirms == null || _cachedConfirms.Length == 0;
            if (Time.unscaledTime - _lastConfirmScan >= (empty ? 0.5f : 3f))
            {
                _lastConfirmScan = Time.unscaledTime;
                // includeInactive:false — scanning inactive objects was ~25 ms. Active-only is cheap;
                // a newly-activated dialog is picked up within the 0.5 s rescan window.
                try { _cachedConfirms = UnityEngine.Object.FindObjectsOfType<ConfirmDialogue>(includeInactive: false); }
                catch { _cachedConfirms = null; }
            }
            if (_cachedConfirms == null) return null;
            foreach (var cd in _cachedConfirms)
            {
                if (cd == null) continue;
                if (!cd.gameObject.activeInHierarchy) continue;
                if (_fadeGroupField != null)
                {
                    var fg = _fadeGroupField.GetValue(cd) as FadeGroup;
                    if (fg != null && !fg.IsShown) continue;
                }
                return cd;
            }
            return null;
        }

        private ConfirmDialogue? TryFindActiveConfirmDialogueInRoot()
        {
            if (_menuRoot == null) return null;
            EnsureConfirmReflection();
            var all = _menuRoot.GetComponentsInChildren<ConfirmDialogue>(includeInactive: false);
            foreach (var cd in all)
            {
                if (cd == null) continue;
                if (!cd.gameObject.activeInHierarchy) continue;
                if (_fadeGroupField != null)
                {
                    var fg = _fadeGroupField.GetValue(cd) as FadeGroup;
                    if (fg != null && !fg.IsShown) continue;
                }
                return cd;
            }
            return null;
        }

        private static void EnsureConfirmReflection()
        {
            if (_confirmBtnField != null) return;
            const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            _confirmBtnField = typeof(ConfirmDialogue).GetField("btnConfirm", f);
            _cancelBtnField  = typeof(ConfirmDialogue).GetField("btnCancel", f);
            _fadeGroupField  = typeof(ConfirmDialogue).GetField("fadeGroup", f);
        }

        private void HandleConfirmDialog(ConfirmDialogue dialog)
        {
            HideChevron();
            var pad = Gamepad.current;
            if (pad == null) return;
            EnsureConfirmReflection();
            if (pad.buttonSouth.wasPressedThisFrame)
            {
                (_confirmBtnField?.GetValue(dialog) as Button)?.onClick?.Invoke();
            }
            else if (pad.buttonEast.wasPressedThisFrame)
            {
                (_cancelBtnField?.GetValue(dialog) as Button)?.onClick?.Invoke();
            }
        }

        private ConfirmDialogue? _glyphedConfirm;
        private readonly List<GameObject> _confirmGlyphs = new();

        // A/B glyphs on confirm/cancel buttons; re-created when dialog instance changes.
        // Parented to button labels so they sit left-of-text.
        private void EnsureConfirmGlyphs(ConfirmDialogue dialog)
        {
            if (ReferenceEquals(dialog, _glyphedConfirm)) return;

            ClearConfirmGlyphs();
            EnsureConfirmReflection();

            var confirmBtn = _confirmBtnField?.GetValue(dialog) as Button;
            var cancelBtn  = _cancelBtnField?.GetValue(dialog) as Button;

            var goA = CreateConfirmGlyph(confirmBtn, ButtonGlyph.A);
            if (goA != null) _confirmGlyphs.Add(goA);

            var goB = CreateConfirmGlyph(cancelBtn, ButtonGlyph.B);
            if (goB != null) _confirmGlyphs.Add(goB);

            _glyphedConfirm = dialog;
        }

        private void ClearConfirmGlyphs()
        {
            foreach (var go in _confirmGlyphs)
                if (go != null) Destroy(go);
            _confirmGlyphs.Clear();
            _glyphedConfirm = null;
        }

        // ModChangedWarning: custom modal at main menu. A=Continue, B=Return (Return glyphed B by MenuBackGlyphInjector).
        private Transform? _modWarn;
        private float _lastModWarnScan = -10f;
        private GameObject? _modWarnGlyph;
        private Button? _modWarnGlyphedBtn;

        private Transform? TryFindActiveModWarning()
        {
            if (Gamepad.current == null) return null;
            // ModChangedWarning only exists at main menu. Always honor timer (not null-gate):
            // null-gating re-ran FindObjectsOfType<FadeGroup>(includeInactive) every frame — GC dips.
            if (!_mainMenuActive) { _modWarn = null; return null; }
            bool stale = _modWarn == null;
            if (Time.unscaledTime - _lastModWarnScan >= (stale ? 0.5f : 3f))
            {
                _lastModWarnScan = Time.unscaledTime;
                try
                {
                    foreach (var fg in UnityEngine.Object.FindObjectsOfType<FadeGroup>(true))
                        if (fg != null && fg.gameObject.name == "ModChangedWarning") { _modWarn = fg.transform; break; }
                }
                catch { }
            }
            if (_modWarn == null) return null;
            if (!_modWarn.gameObject.activeInHierarchy) return null;
            var fgc = _modWarn.GetComponent<FadeGroup>();
            if (fgc != null && !fgc.IsShown) return null;
            return _modWarn;
        }

        private static Button? FindModWarnConfirm(Transform warn)
        {
            foreach (var b in warn.GetComponentsInChildren<Button>(includeInactive: false))
            {
                if (b == null || b.gameObject.name != "Button") continue;
                if (b.transform.Find("Label") != null) return b;
            }
            return null;
        }

        private static Button? FindModWarnCancel(Transform warn)
        {
            foreach (var b in warn.GetComponentsInChildren<Button>(includeInactive: false))
                if (b != null && b.gameObject.name == "Return") return b;
            return null;
        }

        private void EnsureModWarnGlyph(Button? confirmBtn)
        {
            if (confirmBtn == null) return;
            if (ReferenceEquals(confirmBtn, _modWarnGlyphedBtn) && _modWarnGlyph != null) return;
            ClearModWarnGlyph();
            _modWarnGlyph = CreateConfirmGlyph(confirmBtn, ButtonGlyph.A);
            _modWarnGlyphedBtn = confirmBtn;
        }

        private void ClearModWarnGlyph()
        {
            if (_modWarnGlyph != null) Destroy(_modWarnGlyph);
            _modWarnGlyph = null;
            _modWarnGlyphedBtn = null;
        }

        // DifficultySelection Confirm: dedicated X action (button is not in the navigable column).
        // X glyph parented to the Confirm button, re-created when the button instance changes.
        private GameObject? _diffConfirmGlyph;
        private Button? _diffConfirmGlyphedBtn;

        private void EnsureDifficultyConfirmGlyph(Button? confirmBtn)
        {
            if (confirmBtn == null) { ClearDifficultyConfirmGlyph(); return; }
            if (ReferenceEquals(confirmBtn, _diffConfirmGlyphedBtn) && _diffConfirmGlyph != null) return;
            ClearDifficultyConfirmGlyph();
            _diffConfirmGlyph = CreateConfirmGlyph(confirmBtn, ButtonGlyph.X);
            _diffConfirmGlyphedBtn = confirmBtn;
        }

        private void ClearDifficultyConfirmGlyph()
        {
            if (_diffConfirmGlyph != null) Destroy(_diffConfirmGlyph);
            _diffConfirmGlyph = null;
            _diffConfirmGlyphedBtn = null;
        }

        // ClosureView: unmanaged vanilla View; ButtonGlyphHints never runs — put A glyph on continueButton directly.
        // Duckry/island raids show their results through IslandClosureView, which ClosureView delegates to via the
        // static ClosureView.OverrideClosureView (View.ActiveView stays ClosureView, but ITS continueButton is the
        // wrong/inactive one — the live button is the override's `confirmButton`). When an override is present we
        // target that instead, so the existing A-click site (MenuFocusOverlay.cs) drives the right button.
        private Button? _closureGlyphedBtn;
        private GameObject? _closureGlyph;
        private static FieldInfo? _closureContinueField;
        private static System.Type? _closureContinueFieldType;
        private static FieldInfo? _closureOverrideField;
        private static FieldInfo? _islandConfirmField;
        private static System.Type? _islandConfirmFieldType;
        private static System.Type? _closureViewType;

        private void EnsureClosureGlyph()
        {
            if (Gamepad.current == null) { ClearClosureGlyph(); return; }

            Button? btn = null;

            // Island/roguelite ("Duckry") raid-end: ClosureView.OverrideClosureView (static IClosureView)
            // holds the live IslandClosureView whose `confirmButton` is the real accept. CRITICAL: in
            // ClosureView.ShowAndReturnTask the override's Task() (the whole "Enhancements Gained" reveal +
            // WaitForConfirm) is awaited BEFORE ClosureView.Open() — so during that screen View.ActiveView
            // is NOT "ClosureView". We must read the static override directly, ungated by ActiveView, or the
            // glyph never appears and A never fires. The override is set in IslandClosureView.Awake and
            // cleared in OnDestroy, so its lifetime brackets the screen exactly.
            if (_closureViewType == null)
            {
                try { _closureViewType = typeof(Duckov.UI.View).Assembly.GetType("Duckov.UI.ClosureView"); }
                catch { }
            }
            if (_closureViewType != null)
            {
                if (_closureOverrideField == null)
                    _closureOverrideField = _closureViewType.GetField("OverrideClosureView",
                        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                var over = _closureOverrideField?.GetValue(null) as UnityEngine.Object;
                if (over != null)
                {
                    var ot = over.GetType();
                    if (_islandConfirmField == null || _islandConfirmFieldType != ot)
                    {
                        _islandConfirmField = ot.GetField("confirmButton",
                            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        _islandConfirmFieldType = ot;
                    }
                    btn = _islandConfirmField?.GetValue(over) as Button;
                }
            }

            // Normal (non-roguelite) raid-end: the ClosureView View itself is open; glyph its continueButton.
            if (btn == null)
            {
                Duckov.UI.View? view = null;
                try { view = Duckov.UI.View.ActiveView; } catch { }
                if (view != null && view.GetType().Name == "ClosureView")
                {
                    var vt = view.GetType();
                    if (_closureContinueField == null || _closureContinueFieldType != vt)
                    {
                        _closureContinueField = vt.GetField("continueButton",
                            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                        _closureContinueFieldType = vt;
                    }
                    btn = _closureContinueField?.GetValue(view) as Button;
                }
            }
            // Only glyph/click the button while it's actually live (IslandClosureView keeps confirmButton
            // inactive until the reveal sequence finishes), else A would fire into a hidden button.
            if (btn == null || !btn.gameObject.activeInHierarchy) { ClearClosureGlyph(); return; }
            if (ReferenceEquals(btn, _closureGlyphedBtn) && _closureGlyph != null) return;
            ClearClosureGlyph();
            _closureGlyph = CreateConfirmGlyph(btn, ButtonGlyph.A);
            _closureGlyphedBtn = btn;
            Log.Info($"MenuOverlay: ClosureView accept glyph latched on '{btn.gameObject.name}' (override path)");
        }

        private void ClearClosureGlyph()
        {
            if (_closureGlyph != null) Destroy(_closureGlyph);
            _closureGlyph = null;
            _closureGlyphedBtn = null;
        }

        private void HandleModWarning(Transform warn)
        {
            HideChevron();
            var confirmBtn = FindModWarnConfirm(warn);
            var cancelBtn  = FindModWarnCancel(warn);
            EnsureModWarnGlyph(confirmBtn);

            var pad = Gamepad.current;
            if (pad == null) return;
            if (pad.buttonSouth.wasPressedThisFrame && confirmBtn != null)
                DuckovController.UI.PointerEventDispatcher.Click(confirmBtn.gameObject);
            else if (pad.buttonEast.wasPressedThisFrame && cancelBtn != null)
                DuckovController.UI.PointerEventDispatcher.Click(cancelBtn.gameObject);
        }

        // Parents glyph to button's TMP label (fallback: button root), anchored left-of-text.
        private static GameObject? CreateConfirmGlyph(Button? button, ButtonGlyph glyph)
        {
            if (button == null) return null;
            var sprite = GlyphProvider.Get(glyph);
            if (sprite == null) return null;

            var go = new GameObject("ConfirmGlyph(Controller)", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.localScale = Vector3.one;
            rt.sizeDelta  = new Vector2(40f, 40f);
            rt.pivot      = new Vector2(1f, 0.5f);

            const float gap = 12f;
            var label = button.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            if (label != null)
            {
                rt.SetParent(label.rectTransform, worldPositionStays: false);
                // Anchor to label CENTRE offset by half text width: dialog labels are full-button-width
                // with centred text, so left-edge anchoring would dump the glyph at the button edge.
                float halfText = 0f;
                try { halfText = label.preferredWidth * 0.5f; } catch { }
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(-(halfText + gap), 0f);
            }
            else
            {
                rt.SetParent(button.transform, worldPositionStays: false);
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.anchoredPosition = new Vector2(-gap, 0f);
            }

            var le = go.AddComponent<LayoutElement>();
            le.ignoreLayout = true;

            var img = go.AddComponent<Image>();
            img.sprite        = sprite;
            img.preserveAspect  = true;
            img.raycastTarget = false;

            return go;
        }
    }
}
