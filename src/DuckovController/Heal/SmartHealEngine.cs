using System.Collections.Generic;
using DuckovController.Config;
using Duckov;
using Duckov.Buffs;
using ItemStatsSystem;
using UnityEngine;

namespace DuckovController.Heal
{
    // Smart-Heal cascade evaluator. Pure-ish (reads character/inventory state only).
    // Returns one Item to use (null = no-op); caller invokes MainCharacter.UseItem.
    internal static class SmartHealEngine
    {
        internal enum Rank
        {
            None = 0,
            BleedDanger = 1,
            ComboHeal = 2,
            StopBleed = 3,
            Pain = 4,
            Fire = 5,
            Electric = 6,
            Space = 7,
            Cold = 8,
            Poison = 9,
            HealHp = 10,
            HotbarBuff = 11,
        }

        internal readonly struct Decision
        {
            internal readonly Item? Item;
            internal readonly Rank  Rank;
            internal Decision(Item? item, Rank rank) { Item = item; Rank = rank; }
        }

        internal static Decision Pick(CharacterMainControl character, SmartHealRules rules)
        {
            if (character == null || rules == null) return default;

            // Player state.
            var health = character.Health;
            if (health == null) return default;
            float hp    = health.CurrentHealth;
            float maxHp = health.MaxHealth;
            float missing = Mathf.Max(0f, maxHp - hp);

            var buffMgr = TryGetBuffManager(character);
            var activeBuffIDs = CollectActiveBuffIDs(buffMgr);
            int bleedLayers = CountBleedLayers(buffMgr);
            bool bleeding   = bleedLayers > 0;
            bool hasPain     = AnyOfBuffIDs(activeBuffIDs, BuffIdRegistry.PainID);
            bool hasFire     = AnyOfBuffIDs(activeBuffIDs, BuffIdRegistry.BurnID);
            bool hasElectric = AnyOfBuffIDs(activeBuffIDs, BuffIdRegistry.ElectricID);
            bool hasSpace    = AnyOfBuffIDs(activeBuffIDs, BuffIdRegistry.SpaceID);
            bool hasPoison   = AnyOfBuffIDs(activeBuffIDs, BuffIdRegistry.PoisonID);
            bool hasCold     = AnyOfBuffIDs(activeBuffIDs, BuffIdRegistry.ColdID);

            // Candidate enumeration — done once per Pick.
            var candidates = new List<(Item item, ItemMedicalProfile prof)>();
            EnumerateCandidates(character, candidates);

            // Rank 1: bleed danger
            if (bleeding && IsBleedDanger(hp, maxHp, rules))
            {
                var bandaid = PickAntiBleed(candidates, bleedLayers, missing, character, prefersHpOnTie: false);
                if (bandaid != null) return new Decision(bandaid, Rank.BleedDanger);
            }

            // Rank 2: combo-heal (BleedAggressive)
            if (bleeding && rules.BleedAggressive)
            {
                var combo = PickComboHeal(candidates, missing, character);
                if (combo != null) return new Decision(combo, Rank.ComboHeal);
            }

            // Rank 3: stop bleed (no danger, no combo found)
            if (bleeding)
            {
                var bandaid = PickAntiBleed(candidates, bleedLayers, missing, character, prefersHpOnTie: false);
                if (bandaid != null) return new Decision(bandaid, Rank.StopBleed);
            }

            // Rank 4: Pain
            if (rules.TreatPain && hasPain)
            {
                var p = PickByPredicate(candidates, character, prof => prof.RemovesPain);
                if (p != null) return new Decision(p, Rank.Pain);
            }
            // Rank 5..8: Fire, Electric, Space, Poison
            if (rules.TreatFire && hasFire)
            {
                var p = PickByPredicate(candidates, character, prof => prof.RemovesFire);
                if (p != null) return new Decision(p, Rank.Fire);
            }
            if (rules.TreatElectric && hasElectric)
            {
                var p = PickByPredicate(candidates, character, prof => prof.RemovesElectric);
                if (p != null) return new Decision(p, Rank.Electric);
            }
            if (rules.TreatSpace && hasSpace)
            {
                var p = PickByPredicate(candidates, character, prof => prof.RemovesSpace);
                if (p != null) return new Decision(p, Rank.Space);
            }
            if (rules.TreatCold && hasCold)
            {
                var p = PickByPredicate(candidates, character, prof => prof.RemovesCold);
                if (p != null) return new Decision(p, Rank.Cold);
            }
            if (rules.TreatPoison && hasPoison)
            {
                var p = PickByPredicate(candidates, character, prof => prof.RemovesPoison);
                if (p != null) return new Decision(p, Rank.Poison);
            }

            // Rank 9: HP restore (configurable: HealAmount/Price/Off)
            if (rules.HealMissingHp && missing > 0f)
            {
                var heal = PickHeal(candidates, missing, character, rules.HealPick);
                if (heal != null) return new Decision(heal, Rank.HealHp);
            }

            // Rank 10: hotbar buff syringe at full HP
            if (rules.AutoBuffAtFullHp && missing <= 0f)
            {
                var buff = PickHotbarBuffSyringe(activeBuffIDs, character, rules);
                if (buff != null) return new Decision(buff, Rank.HotbarBuff);
            }

            return default;
        }

        // helpers

        private static CharacterBuffManager? TryGetBuffManager(CharacterMainControl ch)
        {
            try { return ch.GetBuffManager(); }
            catch { return null; }
        }

        private static HashSet<int> CollectActiveBuffIDs(CharacterBuffManager? mgr)
        {
            var set = new HashSet<int>();
            if (mgr == null) return set;
            try
            {
                foreach (var b in mgr.Buffs)
                {
                    if (b != null) set.Add(b.ID);
                }
            }
            catch { /* race-during-add — accept partial */ }
            return set;
        }

        private static int CountBleedLayers(CharacterBuffManager? mgr)
        {
            if (mgr == null) return 0;
            int total = 0;
            try
            {
                foreach (var b in mgr.Buffs)
                {
                    if (b == null) continue;
                    if (BuffIdRegistry.BleedIDs.Contains(b.ID)) total += Mathf.Max(1, b.CurrentLayers);
                }
            }
            catch { }
            return total;
        }

        private static bool AnyOfBuffIDs(HashSet<int> active, int id)
        {
            return id >= 0 && active.Contains(id);
        }

        private static bool IsBleedDanger(float hp, float maxHp, SmartHealRules rules)
        {
            float flat = rules.BleedDangerHpThreshold;
            float frac = rules.BleedDangerHpFraction * maxHp;
            float trigger = Mathf.Max(flat, frac);
            return hp <= trigger;
        }

        private static void EnumerateCandidates(
            CharacterMainControl character,
            List<(Item item, ItemMedicalProfile prof)> sink)
        {
            try
            {
                var charItem = character.CharacterItem;
                if (charItem == null) return;

                if (charItem.Inventory != null)
                {
                    foreach (var i in (IEnumerable<Item>)charItem.Inventory)
                    {
                        if (i == null) continue;
                        var prof = ItemHealClassifier.Classify(i);
                        if (prof.HasUsage) sink.Add((i, prof));
                    }
                }
                if (charItem.Slots != null)
                {
                    foreach (var slot in charItem.Slots)
                    {
                        if (slot == null) continue;
                        var content = slot.Content;
                        if (content == null) continue;
                        var prof = ItemHealClassifier.Classify(content);
                        if (prof.HasUsage) sink.Add((content, prof));
                    }
                }
            }
            catch (System.Exception e)
            {
                Log.Debug_($"SmartHealEngine.EnumerateCandidates: {e.Message}");
            }
        }

        private static bool IsItemUsable(Item item, CharacterMainControl character)
        {
            try
            {
                var u = item.GetComponent<ItemStatsSystem.UsageUtilities>();
                if (u == null) return false;
                return u.IsUsable(item, character);
            }
            catch { return false; }
        }

        private static Item? PickAntiBleed(
            List<(Item item, ItemMedicalProfile prof)> candidates,
            int bleedLayers,
            float missingHp,
            CharacterMainControl character,
            bool prefersHpOnTie)
        {
            Item? best = null;
            int bestRemainingBleed = int.MaxValue;
            int bestHealOvershoot  = int.MaxValue;
            bool bestPureBleed     = false;

            foreach (var (item, prof) in candidates)
            {
                if (!prof.RemovesBleed) continue;
                if (!IsItemUsable(item, character)) continue;

                int totalLayersRemoved = SumRemoveLayerCount(item, BuffIdRegistry.BleedIDs);
                int remainingBleed = Mathf.Max(0, bleedLayers - totalLayersRemoved);
                int overshoot = Mathf.Max(0, prof.HealValue - Mathf.CeilToInt(missingHp));
                bool isPure = (prof.HealValue == 0);

                bool better;
                if (remainingBleed != bestRemainingBleed)
                    better = remainingBleed < bestRemainingBleed;
                else if (overshoot != bestHealOvershoot)
                    better = overshoot < bestHealOvershoot;
                else if (prefersHpOnTie)
                    better = !isPure && bestPureBleed; // prefer combined heal
                else
                    better = isPure && !bestPureBleed; // prefer pure bandaid

                if (best == null || better)
                {
                    best = item;
                    bestRemainingBleed = remainingBleed;
                    bestHealOvershoot = overshoot;
                    bestPureBleed = isPure;
                }
            }
            return best;
        }

        // Sum removeLayerCount for matching buffIDs. litmitRemoveLayerCount=false → returns 999 (wipes all stacks).
        private static int SumRemoveLayerCount(Item item, HashSet<int> matchingBuffIDs)
        {
            try
            {
                var u = item.GetComponent<ItemStatsSystem.UsageUtilities>();
                if (u == null || u.behaviors == null) return 0;
                int sum = 0;
                foreach (var beh in u.behaviors)
                {
                    if (beh is Duckov.ItemUsage.RemoveBuff rb && matchingBuffIDs.Contains(rb.buffID))
                    {
                        if (!rb.litmitRemoveLayerCount) return 999; // wipes all stacks
                        sum += rb.removeLayerCount;
                    }
                }
                return sum;
            }
            catch { return 0; }
        }

        private static Item? PickComboHeal(
            List<(Item item, ItemMedicalProfile prof)> candidates,
            float missingHp,
            CharacterMainControl character)
        {
            int ceilMissing = Mathf.CeilToInt(missingHp);
            Item? best = null;
            int bestOvershoot = int.MaxValue;
            foreach (var (item, prof) in candidates)
            {
                if (!prof.IsHeal || !prof.RemovesBleed) continue;
                if (prof.HealValue < ceilMissing) continue;
                if (!IsItemUsable(item, character)) continue;
                int overshoot = prof.HealValue - ceilMissing;
                if (best == null || overshoot < bestOvershoot)
                {
                    best = item;
                    bestOvershoot = overshoot;
                }
            }
            return best;
        }

        private static Item? PickByPredicate(
            List<(Item item, ItemMedicalProfile prof)> candidates,
            CharacterMainControl character,
            System.Func<ItemMedicalProfile, bool> pred)
        {
            foreach (var (item, prof) in candidates)
            {
                if (!pred(prof)) continue;
                if (!IsItemUsable(item, character)) continue;
                return item;
            }
            return null;
        }

        private static Item? PickHeal(
            List<(Item item, ItemMedicalProfile prof)> candidates,
            float missingHp,
            CharacterMainControl character,
            DuckovController.Config.HealPickMode mode)
        {
            int ceilMissing = Mathf.CeilToInt(missingHp);

            // Covering branch (heal >= missing): winner depends on mode.
            Item? coverBest = null;
            int   coverHeal  = 0;            // heal value of current best
            float coverPrice = float.MaxValue; // per-unit sell price of current best

            // Non-covering branch: pick largest heal to minimise chain length. Mode-independent.
            Item? underBest = null; int underBestHeal = -1;

            foreach (var (item, prof) in candidates)
            {
                if (!prof.IsHeal) continue;
                if (!IsItemUsable(item, character)) continue;

                if (prof.HealValue >= ceilMissing)
                {
                    float price = PerUnitSellPrice(item);
                    bool better;
                    if (coverBest == null)
                    {
                        better = true;
                    }
                    else
                    {
                        switch (mode)
                        {
                            case DuckovController.Config.HealPickMode.Price:
                                // cheapest; tie -> smaller heal (less overshoot)
                                better = price < coverPrice
                                      || (price == coverPrice && prof.HealValue < coverHeal);
                                break;
                            case DuckovController.Config.HealPickMode.HealAmount:
                                // least overshoot (smallest heal); tie -> cheaper
                                better = prof.HealValue < coverHeal
                                      || (prof.HealValue == coverHeal && price < coverPrice);
                                break;
                            default: // Off -> largest heal (legacy)
                                better = prof.HealValue > coverHeal;
                                break;
                        }
                    }

                    if (better)
                    {
                        coverBest = item; coverHeal = prof.HealValue; coverPrice = price;
                    }
                }
                else
                {
                    if (prof.HealValue > underBestHeal)
                    {
                        underBest = item; underBestHeal = prof.HealValue;
                    }
                }
            }
            return coverBest ?? underBest;
        }

        // GetTotalRawValue (durability-scaled, stack-multiplied) / StackCount / 2 = displayed sell price per unit.
        // NOTE: NOT Item.Value (catalog value, full durability, un-halved).
        private static float PerUnitSellPrice(Item item)
        {
            try
            {
                int stack = Mathf.Max(1, item.StackCount);
                return (item.GetTotalRawValue() / (float)stack) / 2f;
            }
            catch { return float.MaxValue; } // unknown price -> most expensive
        }

        private static Item? PickHotbarBuffSyringe(
            HashSet<int> activeBuffIDs,
            CharacterMainControl character,
            SmartHealRules rules)
        {
            int slots = rules.HotbarSlotCount > 0 ? rules.HotbarSlotCount : 6;
            for (int i = 0; i < slots; i++)
            {
                Item? hotbarItem;
                try { hotbarItem = ItemShortcut.Get(i); }
                catch { hotbarItem = null; }
                if (hotbarItem == null) continue;
                var prof = ItemHealClassifier.Classify(hotbarItem);
                if (!prof.IsBuffSyringe) continue;
                if (prof.AddsBuff == null) continue;
                if (activeBuffIDs.Contains(prof.AddsBuff.ID)) continue;     // already up
                if (!IsItemUsable(hotbarItem, character)) continue;
                return hotbarItem;
            }
            return null;
        }
    }
}
