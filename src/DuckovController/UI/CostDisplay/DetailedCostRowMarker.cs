using TMPro;
using UnityEngine;

namespace DuckovController.UI.CostDisplay
{
    // Caches one ItemAmountDisplay row's original values so DetailedCostListStyler
    // can re-apply idempotently (pool reuse calls Setup repeatedly) and revert on
    // toggle-off. Added by the styler; never serialized.
    internal sealed class DetailedCostRowMarker : MonoBehaviour
    {
        public bool Styled;
        public float BaseAmountFontSize;     // amountText.fontSize before scaling
        public bool CapturedBaseFont;
        public TextMeshProUGUI? NameLabel;   // the injected "ModItemName" TMP (clone of amountText)

        // Original child geometry, captured once before we restyle, restored on toggle-off.
        public bool CapturedLayout;
        public Vector2 IconPos, IconSize, AmountPos, AmountSize;
        public int IconSibling, AmountSibling;
    }
}
