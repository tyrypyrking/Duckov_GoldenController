using System.Collections.Generic;
using ItemStatsSystem;

namespace DuckovController.Throwables
{
    // Quick-slot (ItemShortcut) helper: classifies throwables and produces the cycle order.
    // A throwable is a quick-slot item flagged "IsSkill" (the game's own check in
    // ItemAgentHolder.IsSkillItem); when held it exposes an ItemSetting_Skill via agentHolder.Skill.
    internal static class ThrowableInventory
    {
        // ItemShortcut exposes slots 0..5 in-game (shortcuts 3-8); probe a few extra defensively.
        private const int MaxSlots = 8;

        internal static bool IsThrowable(Item? item)
        {
            if (item == null) return false;
            try { return item.GetBool("IsSkill"); }
            catch { return false; }
        }

        // Ordered list of throwables currently sitting in the quick-slot bar (nulls skipped).
        internal static List<Item> Enumerate()
        {
            var list = new List<Item>(MaxSlots);
            for (int i = 0; i < MaxSlots; i++)
            {
                Item? it;
                try { it = Duckov.ItemShortcut.Get(i); }
                catch { it = null; }
                if (it != null && IsThrowable(it)) list.Add(it);
            }
            return list;
        }

        // Pure cycle: next throwable after `current` (wraps). If `current` isn't in `list`
        // (e.g. nothing throwable is held), returns the first. Null if the list is empty.
        internal static Item? NextAfter(Item? current, List<Item>? list)
        {
            if (list == null || list.Count == 0) return null;
            int idx = current == null ? -1 : list.IndexOf(current);
            if (idx < 0) return list[0];
            return list[(idx + 1) % list.Count];
        }
    }
}
