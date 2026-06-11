using System.Collections.Generic;

namespace DuckovController.UI.Common
{
    internal static class ViewVerbMapRegistry
    {
        private static readonly Dictionary<string, IViewVerbMap> _byName = new Dictionary<string, IViewVerbMap>();
        private static IViewVerbMap? _default;

        public static void SetDefault(IViewVerbMap map)
        {
            _default = map;
            if (map != null && !string.IsNullOrEmpty(map.ViewTypeName))
                _byName[map.ViewTypeName] = map;
        }

        public static void Register(IViewVerbMap map)
        {
            if (map == null || string.IsNullOrEmpty(map.ViewTypeName)) return;
            _byName[map.ViewTypeName] = map;
        }

        public static IViewVerbMap? For(object? view)
        {
            if (view == null) return _default;
            var name = view.GetType().Name;
            if (_byName.TryGetValue(name, out var m)) return m;
            return _default;
        }

        public static IViewVerbMap? Default => _default;
    }
}
