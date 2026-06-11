using System;
using System.Reflection;

namespace DuckovController.UI.Inventory
{
    // Fires UIInputManager.OnNextPage/OnPreviousPage via reflection (C# events can't be invoked externally).
    // BUG-1: runtime UI_NextPage/Prev bindings are nuked by B-in-pause InputAction re-init; direct-poll immune.
    internal static class UIPageEvents
    {
        // Returns true if the backing delegate was non-null (i.e. at least one subscriber).
        internal static bool FireNext()
        {
            try
            {
                var t = typeof(UIInputManager);
                const BindingFlags sf = BindingFlags.Static | BindingFlags.NonPublic;
                var del = t.GetField("OnNextPage", sf)?.GetValue(null) as Action<UIInputEventData>;
                del?.Invoke(new UIInputEventData());
                return del != null;
            }
            catch (Exception e)
            {
                Log.Warn($"UIPageEvents.FireNext: {e.Message}");
                return false;
            }
        }

        internal static bool FirePrev()
        {
            try
            {
                var t = typeof(UIInputManager);
                const BindingFlags sf = BindingFlags.Static | BindingFlags.NonPublic;
                var del = t.GetField("OnPreviousPage", sf)?.GetValue(null) as Action<UIInputEventData>;
                del?.Invoke(new UIInputEventData());
                return del != null;
            }
            catch (Exception e)
            {
                Log.Warn($"UIPageEvents.FirePrev: {e.Message}");
                return false;
            }
        }
    }
}
