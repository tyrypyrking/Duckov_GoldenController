using UnityEngine;
using UnityEngine.UI;

namespace DuckovController.UI.Menu
{
    // Chevron visual: creation, positioning, scroll-into-view, and debug logging.
    internal sealed partial class MenuFocusOverlay : MonoBehaviour
    {
        // Guards debug log to fire only on focus change.
        private Selectable? _lastDebugFocused;

        private void EnsureChevron()
        {
            if (_chevronGo != null && _chevronRt != null && _chevronGraphic != null) return;
            _chevronGo = new GameObject("MenuFocusChevron",
                typeof(RectTransform), typeof(CanvasRenderer));
            _chevronRt = _chevronGo.GetComponent<RectTransform>();
            _chevronRt.sizeDelta = new Vector2(24f, 24f);
            _chevronRt.anchorMin = new Vector2(0.5f, 0.5f);
            _chevronRt.anchorMax = new Vector2(0.5f, 0.5f);
            _chevronRt.pivot     = new Vector2(0.5f, 0.5f);

            _chevronGraphic = _chevronGo.AddComponent<TriangleGraphic>();
            _chevronGraphic.color = new Color(1f, 0.84f, 0f, 1f);
            _chevronGraphic.raycastTarget = false;  // never intercept pointer events.

            // Sub-Canvas with overrideSorting floats chevron above all other UI, canvas-agnostic.
            var subCanvas = _chevronGo.AddComponent<Canvas>();
            subCanvas.overrideSorting = true;
            subCanvas.sortingOrder = short.MaxValue;

            var le = _chevronGo.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
        }

        private void HideChevron()
        {
            if (_chevronGo != null) _chevronGo.SetActive(false);
        }

        private void DestroyChevron()
        {
            if (_chevronGo != null)
            {
                Destroy(_chevronGo);
                _chevronGo = null;
                _chevronRt = null;
                _chevronGraphic = null;
            }
        }

        // Scrolls focused widget into view in the nearest ScrollRect ancestor via Content.anchoredPosition.
        private static void ScrollFocusedIntoView(RectTransform target)
        {
            ScrollRect? scroll = null;
            var cur = target.parent;
            int hops = 0;
            while (cur != null && hops++ < 20)
            {
                var sr = cur.GetComponent<ScrollRect>();
                if (sr != null) { scroll = sr; break; }
                cur = cur.parent;
            }
            if (scroll == null || scroll.viewport == null || scroll.content == null) return;
            var viewport = scroll.viewport;
            var content = scroll.content;
            if (content.rect.height <= viewport.rect.height) return; // nothing to scroll

            Vector3 targetWorldCenter = target.TransformPoint((Vector3)target.rect.center);
            Vector3 targetLocal = content.InverseTransformPoint(targetWorldCenter);

            Vector3 vpWorldCenter = viewport.TransformPoint((Vector3)viewport.rect.center);
            Vector3 vpLocal = content.InverseTransformPoint(vpWorldCenter);

            float halfRow = 30f;
            if (Mathf.Abs(targetLocal.y - vpLocal.y) < halfRow) return;

            float deltaY = vpLocal.y - targetLocal.y;
            Vector2 pos = content.anchoredPosition;
            pos.y += deltaY;
            float maxScroll = Mathf.Max(0, content.rect.height - viewport.rect.height);
            pos.y = Mathf.Clamp(pos.y, 0f, maxScroll);
            content.anchoredPosition = pos;
        }

        private void UpdateChevron()
        {
            UpdateCharacterCreatorSwatchOutline();
            if (_focused == null) { HideChevron(); ClearActiveHoverIndicator(); return; }

            // Difficulty cards: use game's per-card HoveringIndicator (gold-glow) instead of chevron.
            // Confirm button at the bottom keeps the chevron.
            if (IsInsideDifficultySelection() && IsDifficultyCard(_focused.transform))
            {
                HideChevron();
                ActivateHoverIndicator(_focused.transform);
                return;
            }
            ClearActiveHoverIndicator();

            var targetRt = _focused.transform as RectTransform;
            if (targetRt == null) { HideChevron(); return; }

            var parentRt = targetRt.parent as RectTransform;
            if (parentRt == null) { HideChevron(); return; }

            EnsureChevron();
            if (_chevronGo == null || _chevronRt == null) return;
            _chevronGo.SetActive(true);

            if (!ReferenceEquals(_chevronRt.parent, parentRt))
                _chevronRt.SetParent(parentRt, worldPositionStays: false);

            _chevronRt.anchorMin = new Vector2(0.5f, 0.5f);
            _chevronRt.anchorMax = new Vector2(0.5f, 0.5f);
            _chevronRt.pivot     = new Vector2(0.5f, 0.5f);
            _chevronRt.localScale = Vector3.one;
            _chevronRt.localRotation = Quaternion.identity;

            var corners = new Vector3[4];
            targetRt.GetWorldCorners(corners);
            // Size relative to button HEIGHT (not width): wide-but-short buttons like "Reset to Default"
            // get a reasonable chevron. Capped at row height.
            float buttonWorldHeight = Vector3.Distance(corners[0], corners[1]);
            float chevronWorldSize = Mathf.Clamp(buttonWorldHeight * 0.5f, 16f, 32f);
            float parentScale = parentRt.lossyScale.x;
            float sizeLocal = parentScale > 0f ? chevronWorldSize / parentScale : 24f;
            _chevronRt.sizeDelta = new Vector2(sizeLocal, sizeLocal);

            ScrollFocusedIntoView(targetRt);

            Vector3 leftCenterWorld = (corners[0] + corners[1]) * 0.5f;
            float marginWorld = chevronWorldSize * 0.9f;
            Vector3 chevronWorld = new Vector3(leftCenterWorld.x - marginWorld,
                                               leftCenterWorld.y,
                                               leftCenterWorld.z);
            _chevronRt.position = chevronWorld;

            // Log full chevron state on focus change (canvas, mask, rect, world pos — chevron debug).
            if (!ReferenceEquals(_lastDebugFocused, _focused))
            {
                _lastDebugFocused = _focused;
                LogChevronState(targetRt, parentRt, corners, leftCenterWorld, chevronWorld);
            }
        }

        private void LogChevronState(RectTransform btnRt, RectTransform parentRt,
                                     Vector3[] btnCorners, Vector3 btnLeftCenterWorld,
                                     Vector3 chevronWorld)
        {
            try
            {
                var canvas = btnRt.GetComponentInParent<Canvas>();
                string canvasInfo = canvas != null
                    ? $"canvas={canvas.name} mode={canvas.renderMode} scaleFactor={canvas.scaleFactor:F3} sortingOrder={canvas.sortingOrder}"
                    : "canvas=<none>";

                string maskInfo = "mask=";
                var mask = btnRt.GetComponentInParent<UnityEngine.UI.Mask>();
                var rectMask = btnRt.GetComponentInParent<UnityEngine.UI.RectMask2D>();
                maskInfo += mask != null ? $"Mask@{mask.gameObject.name}" : "none";
                if (rectMask != null) maskInfo += $",RectMask2D@{rectMask.gameObject.name}";

                string chRt = _chevronRt != null
                    ? $"chevronRt sizeDelta={_chevronRt.sizeDelta} anchoredPos={_chevronRt.anchoredPosition} " +
                      $"localPos={_chevronRt.localPosition} worldPos={_chevronRt.position} " +
                      $"lossyScale={_chevronRt.lossyScale.x:F4} active={_chevronGo!.activeInHierarchy}"
                    : "chevronRt=<null>";

                string chGr = _chevronGraphic != null
                    ? $"chevronGraphic color={_chevronGraphic.color} raycastTarget={_chevronGraphic.raycastTarget} enabled={_chevronGraphic.enabled} material={(_chevronGraphic.material != null ? _chevronGraphic.material.name : "<null>")}"
                    : "chevronGraphic=<null>";

                Log.Info(
                    $"MenuOverlay chevron focus=\"{_focused?.gameObject.name}\"\n" +
                    $"  {canvasInfo}\n" +
                    $"  {maskInfo}\n" +
                    $"  button rect: pivot={btnRt.pivot} anchorMin={btnRt.anchorMin} anchorMax={btnRt.anchorMax} " +
                    $"sizeDelta={btnRt.sizeDelta} lossyScale={btnRt.lossyScale.x:F4}\n" +
                    $"  button worldCorners: BL={btnCorners[0]} TL={btnCorners[1]} TR={btnCorners[2]} BR={btnCorners[3]}\n" +
                    $"  button leftCenterWorld={btnLeftCenterWorld}\n" +
                    $"  chevron targetWorldPos={chevronWorld}\n" +
                    $"  parentRt={parentRt.gameObject.name} pivot={parentRt.pivot} sizeDelta={parentRt.sizeDelta} lossyScale={parentRt.lossyScale.x:F4}\n" +
                    $"  {chRt}\n" +
                    $"  {chGr}");
            }
            catch (System.Exception e)
            {
                Log.Warn($"MenuOverlay chevron debug log threw: {e.Message}");
            }
        }
    }
}
