using System.Collections.Generic;
using Duckov.Buffs;
using Duckov.ItemUsage;
using ItemStatsSystem;

namespace DuckovController.Heal
{
    // Reads UsageUtilities.behaviors and reports the medical profile. Pure; no caching
    // (behaviors is per-prefab; instance cache wouldn't survive inventory hot-swap).
    internal readonly struct ItemMedicalProfile
    {
        public readonly bool   HasUsage;
        public readonly int    HealValue;
        public readonly HashSet<int> RemovesBuffIDs;
        public readonly Buff?  AddsBuff;

        public ItemMedicalProfile(int healValue, HashSet<int> removes, Buff? addsBuff, bool hasUsage)
        {
            HealValue       = healValue;
            RemovesBuffIDs  = removes;
            AddsBuff        = addsBuff;
            HasUsage        = hasUsage;
        }

        public bool IsHeal           => HealValue > 0;
        public bool IsBuffSyringe    => AddsBuff != null;

        // Anti-X queries pull canonical IDs from BuffIdRegistry, evaluated at call time.
        public bool RemovesBleed     => Intersects(RemovesBuffIDs, BuffIdRegistry.BleedIDs);
        public bool RemovesPain      => RemovesBuffIDs?.Contains(BuffIdRegistry.PainID) == true;
        public bool RemovesFire      => RemovesBuffIDs?.Contains(BuffIdRegistry.BurnID) == true;
        public bool RemovesElectric  => RemovesBuffIDs?.Contains(BuffIdRegistry.ElectricID) == true;
        public bool RemovesSpace     => RemovesBuffIDs?.Contains(BuffIdRegistry.SpaceID) == true;
        public bool RemovesPoison    => RemovesBuffIDs?.Contains(BuffIdRegistry.PoisonID) == true;
        public bool RemovesCold      => RemovesBuffIDs?.Contains(BuffIdRegistry.ColdID) == true;

        private static bool Intersects(HashSet<int>? a, HashSet<int>? b)
        {
            if (a == null || b == null || a.Count == 0 || b.Count == 0) return false;
            foreach (var x in a) if (b.Contains(x)) return true;
            return false;
        }
    }

    internal static class ItemHealClassifier
    {
        internal static ItemMedicalProfile Classify(Item item)
        {
            if (item == null) return default;
            UsageUtilities? usage = null;
            try { usage = item.GetComponent<UsageUtilities>(); }
            catch { return default; }
            if (usage == null) return default;

            int healValue = 0;
            var removes = new HashSet<int>();
            Buff? addsBuff = null;

            // UsageUtilities.behaviors: public List<UsageBehavior>; care about Drug, RemoveBuff, AddBuff.
            var list = usage.behaviors;
            if (list != null)
            {
                foreach (var beh in list)
                {
                    if (beh == null) continue;
                    switch (beh)
                    {
                        case Drug drug:
                            if (drug.healValue > healValue) healValue = drug.healValue;
                            break;
                        case RemoveBuff rb:
                            if (rb.buffID != 0) removes.Add(rb.buffID);
                            break;
                        case AddBuff ab:
                            // Last wins if multiple AddBuffs exist — rare in vanilla.
                            if (ab.buffPrefab != null) addsBuff = ab.buffPrefab;
                            break;
                    }
                }
            }

            return new ItemMedicalProfile(healValue, removes, addsBuff, hasUsage: true);
        }
    }
}
