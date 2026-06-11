using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace DuckovController.UI.Prompts
{
    // Shared cache for InputIndicator's private icon/textContainer/text fields (resolved once).
    internal static class InputIndicatorFields
    {
        private static FieldInfo? _icon;
        private static FieldInfo? _textContainer;
        private static FieldInfo? _text;
        private static bool _resolved;

        private static void Ensure()
        {
            if (_resolved) return;
            _resolved = true;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var t = typeof(InputIndicator);
            _icon          = t.GetField("icon",          flags);
            _textContainer = t.GetField("textContainer", flags);
            _text          = t.GetField("text",          flags);
        }

        internal static Image? Icon(InputIndicator ind)
        { Ensure(); return _icon?.GetValue(ind) as Image; }

        internal static GameObject? TextContainer(InputIndicator ind)
        { Ensure(); return _textContainer?.GetValue(ind) as GameObject; }

        internal static TMPro.TextMeshProUGUI? Text(InputIndicator ind)
        { Ensure(); return _text?.GetValue(ind) as TMPro.TextMeshProUGUI; }
    }
}
