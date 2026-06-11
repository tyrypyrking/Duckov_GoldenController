using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DuckovController.UI
{
    internal sealed partial class GridFocusController : MonoBehaviour
    {
        // Slider hold-repeat state (independent cadence from grid-nav).
        private float _sliderHoldStarted;
        private float _sliderLastRepeat = -10f;

        // Left stick virtual dpad: sampled once per frame, OR'd with physical dpad via DirEdge/DirHeld.
        private readonly StickDpad _stick = new StickDpad();

        private static UnityEngine.InputSystem.Controls.ButtonControl DpadButton(Gamepad pad, NavDir d)
            => d switch
            {
                NavDir.Up   => pad.dpad.up,
                NavDir.Down => pad.dpad.down,
                NavDir.Left => pad.dpad.left,
                _           => pad.dpad.right,
            };

        // _stick contributes only while StickAsDpad is on (Sample() called with that flag).
        private bool DirEdge(Gamepad pad, NavDir d) => DpadButton(pad, d).wasPressedThisFrame || _stick.Edge(d);
        private bool DirHeld(Gamepad pad, NavDir d) => DpadButton(pad, d).isPressed || _stick.Held(d);

        // Focused Slider: dpad adjusts value (left/right=fine ±1, up/down=coarse ×60) instead of navigating.
        // Returns true → caller skips spatial PollNav (focus stays on slider for fixed-control screens like SleepView).
        private bool HandleFocusedSlider(Gamepad pad)
        {
            if (_focused == null || Cfg == null) return false;
            var slider = _focused.GetComponent<Slider>();
            if (slider == null) return false;

            // ItemDecomposeView: trapped count slider — Left/Right = ±1 item, Up/Down = ±5% of the
            // span (rounded, min 1). D-pad never navigates off the slider; LB/RB switch panels instead.
            if (_activeView != null && _activeView.GetType().Name == "ItemDecomposeView")
                return HandleDecomposeSlider(pad, slider);

            float fine = slider.wholeNumbers
                ? 1f
                : Mathf.Max(0.01f, (slider.maxValue - slider.minValue) * 0.01f);
            float coarse = fine * 60f;

            // Horizontal=fine, vertical=coarse; horizontal wins if both pressed. Combined dpad+stick input.
            float EdgeDelta()
            {
                if (DirEdge(pad, NavDir.Right)) return +fine;
                if (DirEdge(pad, NavDir.Left))  return -fine;
                if (DirEdge(pad, NavDir.Up))    return +coarse;
                if (DirEdge(pad, NavDir.Down))  return -coarse;
                return 0f;
            }
            float HeldDelta()
            {
                if (DirHeld(pad, NavDir.Right)) return +fine;
                if (DirHeld(pad, NavDir.Left))  return -fine;
                if (DirHeld(pad, NavDir.Up))    return +coarse;
                if (DirHeld(pad, NavDir.Down))  return -coarse;
                return 0f;
            }

            float edge = EdgeDelta();
            if (edge != 0f)
            {
                _sliderHoldStarted = Time.unscaledTime;
                _sliderLastRepeat = Time.unscaledTime;
                AdjustSlider(slider, edge);
                return true;
            }

            float held = HeldDelta();
            if (held != 0f
                && Time.unscaledTime - _sliderHoldStarted >= Cfg.Ui.NavRepeatDelaySec
                && Time.unscaledTime - _sliderLastRepeat >= Cfg.Ui.NavRepeatRateSec)
            {
                AdjustSlider(slider, held);
                _sliderLastRepeat = Time.unscaledTime;
            }
            return true; // slider focused — consume nav regardless
        }

        // ItemDecomposeView count slider — TRAPPED (post-playtest): D-pad never navigates off the
        // slider (that broke slider movement). Left/Right = ±1 item; Up/Down = ±5% of (max−min)
        // rounded, min 1 (so small stacks still move by ≥1). Integer round-and-clamp (mirroring
        // AdjustSplitSlider — the underlying Slider isn't wholeNumbers); hold-repeats on the grid
        // cadence. Always returns true (consume): leaving the slider is done with LB/RB only.
        private bool HandleDecomposeSlider(Gamepad pad, Slider slider)
        {
            if (Cfg == null) return true;

            float coarse = Mathf.Max(1f, Mathf.Round((slider.maxValue - slider.minValue) * 0.05f));
            float Edge()
            {
                if (DirEdge(pad, NavDir.Right)) return +1f;
                if (DirEdge(pad, NavDir.Left))  return -1f;
                if (DirEdge(pad, NavDir.Up))    return +coarse;
                if (DirEdge(pad, NavDir.Down))  return -coarse;
                return 0f;
            }
            float Held()
            {
                if (DirHeld(pad, NavDir.Right)) return +1f;
                if (DirHeld(pad, NavDir.Left))  return -1f;
                if (DirHeld(pad, NavDir.Up))    return +coarse;
                if (DirHeld(pad, NavDir.Down))  return -coarse;
                return 0f;
            }

            float edge = Edge();
            if (edge != 0f)
            {
                _sliderHoldStarted = Time.unscaledTime;
                _sliderLastRepeat  = Time.unscaledTime;
                AdjustSplitSlider(slider, edge);
                return true;
            }

            float held = Held();
            if (held != 0f
                && Time.unscaledTime - _sliderHoldStarted >= Cfg.Ui.NavRepeatDelaySec
                && Time.unscaledTime - _sliderLastRepeat >= Cfg.Ui.NavRepeatRateSec)
            {
                AdjustSplitSlider(slider, held);
                _sliderLastRepeat = Time.unscaledTime;
            }
            return true; // slider focused — consume nav regardless (trapped; LB/RB leaves)
        }

        // Direct poll: runtime-added 2DVector composite bindings don't reliably fire UIInputManager.OnNavigate.
        private void PollNav()
        {
            var pad = Gamepad.current;
            if (pad == null) return;

            Vector2 oneShot = Vector2.zero;
            if (DirEdge(pad, NavDir.Up))    oneShot.y += 1f;
            if (DirEdge(pad, NavDir.Down))  oneShot.y -= 1f;
            if (DirEdge(pad, NavDir.Left))  oneShot.x -= 1f;
            if (DirEdge(pad, NavDir.Right)) oneShot.x += 1f;
            if (oneShot != Vector2.zero)
            {
                _lastNavTime = Time.unscaledTime;
                _navHoldStarted = Time.unscaledTime;
                _lastNavAxis = oneShot;
                _navHoldStepCount = 0;
                Step(Cardinal(oneShot));
                DuckovController.Haptics.HapticEngine.Instance?.Play(DuckovController.Haptics.HapticCue.FocusTick);
                Log.Debug_("Haptic: FocusTick (grid edge)");
                return;
            }

            // Held → repeat after delay then at repeat rate.
            Vector2 held = Vector2.zero;
            if (DirHeld(pad, NavDir.Up))    held.y += 1f;
            if (DirHeld(pad, NavDir.Down))  held.y -= 1f;
            if (DirHeld(pad, NavDir.Left))  held.x -= 1f;
            if (DirHeld(pad, NavDir.Right)) held.x += 1f;
            if (held == Vector2.zero)
            {
                _lastNavAxis = Vector2.zero;
                _navHoldStepCount = 0;
                return;
            }

            var now = Time.unscaledTime;
            bool sameAxis = Vector2.Dot(_lastNavAxis, held) > 0.7f;
            if (sameAxis)
            {
                // Pay initial delay once (from hold-start); then repeat at effRate (from last fire).
                if (now - _navHoldStarted < Cfg!.Ui.NavRepeatDelaySec) return;
                // NavAccel curve: shrink interval after sustained hold for long lists (60+ items).
                float effRate = NavAccel.EffectiveRate(Cfg.Ui.NavRepeatRateSec, _navHoldStepCount);
                if (now - _lastNavTime < effRate) return;
            }
            else
            {
                _navHoldStepCount = 0;
                _navHoldStarted = now; // new direction → re-pay the initial delay
            }
            _lastNavTime = now;
            _lastNavAxis = held;
            _navHoldStepCount++;
            Step(Cardinal(held));
            DuckovController.Haptics.HapticEngine.Instance?.Play(DuckovController.Haptics.HapticCue.FocusTick);
            Log.Debug_("Haptic: FocusTick (grid repeat)");
        }

        private static NavDir Cardinal(Vector2 v)
        {
            if (Mathf.Abs(v.x) > Mathf.Abs(v.y))
                return v.x > 0 ? NavDir.Right : NavDir.Left;
            return v.y > 0 ? NavDir.Up : NavDir.Down;
        }

        // Setting .value fires onValueChanged so game listeners (e.g. SleepView wake-time) run as for a mouse drag.
        private static void AdjustSlider(Slider slider, float delta)
        {
            float before = slider.value;
            slider.value = Mathf.Clamp(slider.value + delta, slider.minValue, slider.maxValue);
            if (!Mathf.Approximately(before, slider.value))
                Log.Debug_($"GFC slider adjust: {slider.gameObject.name} {before} → {slider.value}");
        }

        private void Step(NavDir dir)
        {
            // Don't nav while page-flip settling: second press on stale focus → cross-pane jump.
            if (_pendingPagedFocus) return;

            // Op-menu focus: constrain to menu buttons (top→bottom Y); bypasses FocusGraph so inventory entries don't leak in.
            if (IsOperationMenuOpen() && _focused != null && IsFocusInMenu(_focused))
            {
                var menuNext = FindMenuNeighbor(_focused, dir);
                if (menuNext != null) SetFocus(menuNext);
                return; // no fall-through to spatial nav
            }

            var next = _graph.Neighbor(_focused, dir);

            // Page-advance: if vertical nav would cross out of a paged pane,
            // flip the page first and land on the new page's first slot.
            if ((dir == NavDir.Up || dir == NavDir.Down) && _focused != null)
            {
                var entry = _focused.GetComponent<Duckov.UI.InventoryEntry>();
                if (entry?.Master != null && entry.Master.UsePages)
                {
                    bool crossesOut = next == null
                        || _panes.IndexOfPaneContaining(_focused.transform)
                           != _panes.IndexOfPaneContaining(next.transform);

                    if (crossesOut)
                    {
                        if (TryAdvancePage(entry.Master, dir, out var newFocus))
                        {
                            StampMiniMapNav(newFocus);
                            SetFocus(newFocus);
                            return;
                        }
                    }
                }
            }

            if (next == null)
            {
                // NoteIndexView: wrap vertical nav (past last → first, past first → last).
                if ((dir == NavDir.Up || dir == NavDir.Down) && IsWrapAroundListView())
                {
                    var wrap = dir == NavDir.Down ? _graph.InitialFocus() : _graph.LastFocus();
                    if (wrap != null && !ReferenceEquals(wrap, _focused)) SetFocus(wrap);
                }
                return;
            }
            StampMiniMapNav(next);
            SetFocus(next);
        }

        // Single-column pooled lists that should cycle at the ends instead of dead-stopping.
        private bool IsWrapAroundListView()
            => _activeView != null && _activeView.GetType().Name == "NoteIndexView";

        // Record nav destination screen-centre for drift-correct. No-op outside MiniMapView/StorageDock.
        private void StampMiniMapNav(GameObject? dest)
        {
            if (dest == null) return;
            if (_activeView == null || !UsesScreenPosDriftPin(_activeView.GetType().Name)) return;
            _mmRememberedScreenPos = PointerEventDispatcher.ScreenCenterOf(dest);
            _mmHasRememberedPos    = true;
        }

        private bool IsFocusInMenu(GameObject go)
        {
            if (_cachedOperationMenu == null) return false;
            var menuT = _cachedOperationMenu.transform;
            var t = go.transform;
            while (t != null)
            {
                if (t == menuT) return true;
                t = t.parent;
            }
            return false;
        }

        private GameObject? FindMenuNeighbor(GameObject current, NavDir dir)
        {
            if (_cachedOperationMenu == null) return null;
            var allButtons = _cachedOperationMenu.GetComponentsInChildren<UnityEngine.UI.Button>(false);
            var list = new System.Collections.Generic.List<UnityEngine.UI.Button>();
            foreach (var b in allButtons)
            {
                if (b == null) continue;
                if (!b.interactable) continue;
                if (!b.gameObject.activeInHierarchy) continue;
                list.Add(b);
            }
            if (list.Count == 0) return null;
            // Sort top→bottom by world Y (descending).
            list.Sort((a, b) => b.transform.position.y.CompareTo(a.transform.position.y));
            int curIdx = -1;
            for (int i = 0; i < list.Count; i++)
                if (list[i].gameObject == current) { curIdx = i; break; }
            // If current not found, return topmost button.
            if (curIdx < 0) return list[0].gameObject;
            int nextIdx;
            if (dir == NavDir.Up || dir == NavDir.Left)
                nextIdx = (curIdx == 0) ? list.Count - 1 : curIdx - 1;
            else
                nextIdx = (curIdx + 1) % list.Count;
            return list[nextIdx].gameObject;
        }

        private void DriveScrollIntoView()
        {
            if (_focused == null) return;
            var rt = _focused.transform as RectTransform;
            if (rt == null) return;
            var scroll = _focused.GetComponentInParent<ScrollRect>();
            if (scroll == null || scroll.content == null) return;
            var viewport = scroll.viewport != null ? scroll.viewport : scroll.GetComponent<RectTransform>();
            if (viewport == null) return;

            // Calculate vertical position of focused entry relative to content.
            Vector2 viewportLocalCenter = viewport.InverseTransformPoint(rt.position);
            var viewportRect = viewport.rect;
            float halfHeight = viewportRect.height * 0.5f;
            float overshootTop = viewportLocalCenter.y - (viewportRect.yMax - halfHeight * 0.2f);
            float overshootBot = (viewportRect.yMin + halfHeight * 0.2f) - viewportLocalCenter.y;

            if (overshootTop > 0f || overshootBot > 0f)
            {
                // Snap normalized position so focus is visible. Cheap: use
                // the content's anchored Y delta.
                var contentRect = scroll.content.rect;
                float scrollRange = contentRect.height - viewportRect.height;
                if (scrollRange <= 1f) return;
                Vector2 contentLocalCenter = scroll.content.InverseTransformPoint(rt.position);
                float topOfContent = scroll.content.rect.yMax;
                float desired = topOfContent - contentLocalCenter.y - viewportRect.height * 0.5f;
                float normY = 1f - Mathf.Clamp01(desired / scrollRange);
                scroll.verticalNormalizedPosition = normY;
            }
        }
    }
}
