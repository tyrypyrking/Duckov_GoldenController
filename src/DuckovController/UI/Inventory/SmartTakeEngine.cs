using System.Collections.Generic;
using System.Reflection;
using DuckovController.Config;
using DuckovController.UI.Common;
using Duckov.UI;
using Duckov.Utilities;
using ItemStatsSystem;
using UnityEngine;

namespace DuckovController.UI.Inventory
{
    // Rule-based Take-all. Gates AND (any rejection excludes); includes OR (any match takes).
    // "No includes" is deliberate no-op — long-press Select is the vanilla take-all escape hatch.
    internal static class SmartTakeEngine
    {
        internal static int Execute(in SmartTakeContext ctx)
        {
            if (ctx.Source == null) return 0;
            var rules = ctx.Rules;

            bool anyInclude = rules.TakeAmmoForOwnedGuns
                              || rules.TakeWishlisted
                              || rules.TakeQuestRequired
                              || rules.TakeBuildingRequired
                              || rules.TakeAboveValue
                              || rules.TakeAboveValuePerWeight
                              || rules.TopUpExistingStacks
                              || (rules.IncludeTags != null && rules.IncludeTags.Length > 0);
            if (!anyInclude) return 0;

            // Snapshot: Transfer.Send mutates Source; cast to IEnumerable<Item> explicitly
            // (Inventory also implements Sirenix ISelfValidator; Odin not referenced → overload ambiguity).
            var snapshot = new List<Item>();
            foreach (var i in (IEnumerable<Item>)ctx.Source) snapshot.Add(i);

            HashSet<string>? ownedCalibers = rules.TakeAmmoForOwnedGuns
                ? CollectOwnedGunCalibers()
                : null;

            System.Func<Item, bool>? activeFilterPredicate = null; // one GetSelection per Execute, not per item
            if (rules.RespectActiveFilter && ctx.SourceFilter != null)
            {
                try
                {
                    var sel = ctx.SourceFilter.GetSelection();
                    if (sel != null)
                    {
                        // GetFunction returns null for "show all" (no requireTags); null = admit all.
                        activeFilterPredicate = sel.Filter.GetFunction();
                    }
                }
                catch (System.Exception e)
                {
                    Log.Debug_($"SmartTakeEngine: filter resolution failed: {e.Message}");
                }
            }

            int taken = 0;
            foreach (var item in snapshot)
            {
                if (item == null) continue;

                // Gates
                if (rules.SkipLockedInventoryIndices)
                {
                    int idx = ctx.Source.GetIndex(item);
                    if (idx >= 0 && ctx.Source.lockedIndexes != null
                        && ctx.Source.lockedIndexes.Contains(idx)) continue;
                }
                if (rules.RequireInspected
                    && ctx.LootBox != null && ctx.LootBox.needInspect && !item.Inspected) continue;
                if (activeFilterPredicate != null && !activeFilterPredicate(item)) continue;

                // Includes
                bool otherInclude = AnyNonTopUpIncludeFires(item, rules, ownedCalibers);
                bool topUp = rules.TopUpExistingStacks
                             && FillsExistingStack(item, ctx.Destination);

                if (Log.Verbose)
                {
                    int raw = 0; float tw = 0f, usw = 0f, sw = 0f; int val = 0;
                    try
                    {
                        raw = item.GetTotalRawValue(); tw = item.TotalWeight;
                        usw = item.UnitSelfWeight; sw = item.SelfWeight; val = item.Value;
                    }
                    catch { }
                    float sell = raw / 2f;
                    float vpw = tw > 0.0001f ? sell / tw : 0f;
                    // Re-evaluate each include in isolation so the log shows EXACTLY which rule fired.
                    bool fAmmo  = rules.TakeAmmoForOwnedGuns   && IsOwnedAmmo(item, ownedCalibers, rules.MinAmmoTier);
                    bool fWish  = rules.TakeWishlisted         && IsWishlisted(item);
                    bool fQuest = rules.TakeQuestRequired      && IsQuestRequired(item);
                    bool fBuild = rules.TakeBuildingRequired   && IsBuildingRequired(item);
                    bool fVal   = rules.TakeAboveValue         && IsAboveValue(item, rules.ValueThreshold);
                    bool fVpw   = rules.TakeAboveValuePerWeight && IsAboveValuePerWeight(item, rules.ValuePerWeightThreshold);
                    bool fTag   = rules.IncludeTags != null    && MatchesAnyIncludeTag(item, rules.IncludeTags);
                    Log.Debug_($"SmartTake item='{item.DisplayName}' stack={item.StackCount} " +
                        $"Value(perUnit)={val} rawTotal={raw} sell(raw/2)={sell:F0} " +
                        $"totalWeight={tw:F3} unitWeight={usw:F3} selfWeight={sw:F3} " +
                        $"valuePerKg={vpw:F1} (thr={rules.ValuePerWeightThreshold}) " +
                        $"value(thr={rules.ValueThreshold}) canBeSold={item.CanBeSold} " +
                        $"| ammo={fAmmo} wish={fWish} quest={fQuest} build={fBuild} " +
                        $"aboveVal={fVal} aboveValPerKg={fVpw} tag={fTag} topUp={topUp} " +
                        $"=> {(otherInclude || topUp ? "TAKE" : "skip")}");
                }

                if (!otherInclude && !topUp) continue;

                // Transfer
                if (otherInclude || rules.AllowStackOverflowOnTopUp)
                {
                    ctx.Transfer.Send(item); // take whole stack; merge fills partials, overflows to new slot
                    taken++;
                }
                else
                {
                    // Top-up only, overflow disallowed: fill partials, leave remainder behind.
                    if (TopUpInto(item, ctx.Destination)) taken++;
                }
            }

            if (taken > 0 && rules.AudioOnSmartTake) PostUiConfirm();
            return taken;
        }

        private static void PostUiConfirm() => GameRef.PostAudio("UI/confirm");

        private static bool AnyNonTopUpIncludeFires(Item item, SmartTakeRules rules, HashSet<string>? ownedCalibers)
        {
            if (rules.TakeAmmoForOwnedGuns && IsOwnedAmmo(item, ownedCalibers, rules.MinAmmoTier)) return true;
            if (rules.TakeWishlisted && IsWishlisted(item)) return true;
            if (rules.TakeQuestRequired && IsQuestRequired(item)) return true;
            if (rules.TakeBuildingRequired && IsBuildingRequired(item)) return true;
            if (ValueRulesFire(item, rules)) return true;
            if (rules.IncludeTags != null && MatchesAnyIncludeTag(item, rules.IncludeTags)) return true;
            return false;
        }

        // The two value rules combine by AND or OR per ValueRulesRequireBoth. AND only binds when
        // BOTH are enabled; with one enabled it degrades to that single rule (so the disabled rule's
        // always-false result can't veto the take).
        private static bool ValueRulesFire(Item item, SmartTakeRules rules)
        {
            bool aOn = rules.TakeAboveValue;
            bool wOn = rules.TakeAboveValuePerWeight;
            if (!aOn && !wOn) return false;
            bool a = aOn && IsAboveValue(item, rules.ValueThreshold);
            bool w = wOn && IsAboveValuePerWeight(item, rules.ValuePerWeightThreshold);
            if (aOn && wOn && rules.ValueRulesRequireBoth) return a && w;
            return a || w;
        }

        // True when the destination already holds a non-full stack of this item's
        // type that we could top up.
        private static bool FillsExistingStack(Item item, ItemStatsSystem.Inventory? dest)
        {
            if (dest == null || !item.Stackable) return false;
            try
            {
                foreach (var d in (IEnumerable<Item>)dest)
                    if (d != null && d.TypeID == item.TypeID
                        && d.StackCount < d.MaxStackCount) return true;
            }
            catch { /* ignore */ }
            return false;
        }

        // Combine into partial destination stacks; remainder stays in source. Returns true if any moved.
        private static bool TopUpInto(Item item, ItemStatsSystem.Inventory? dest)
        {
            if (dest == null || !item.Stackable) return false;
            int before = item.StackCount;
            try
            {
                var partials = new List<Item>(); // snapshot: Combine mutates dest
                foreach (var d in (IEnumerable<Item>)dest)
                    if (d != null && d.TypeID == item.TypeID && d.StackCount < d.MaxStackCount)
                        partials.Add(d);

                foreach (var d in partials)
                {
                    if (item.StackCount <= 0) break;
                    d.Combine(item); // moves min(capacity, item.StackCount) into d
                }
            }
            catch (System.Exception e)
            {
                Log.Debug_($"SmartTakeEngine.TopUpInto: {e.Message}");
            }
            return item == null || item.StackCount < before;
        }

        // Predicates

        private static bool IsOwnedAmmo(Item item, HashSet<string>? ownedCalibers, Config.AmmoTier minTier)
        {
            if (ownedCalibers == null || ownedCalibers.Count == 0) return false;
            try
            {
                var bulletTag = GameplayDataSettings.Tags.Bullet;
                if (bulletTag == null || !item.Tags.Contains(bulletTag)) return false;
                var meta = ItemAssetsCollection.GetMetaData(item.TypeID);
                if (string.IsNullOrEmpty(meta.caliber)) return false;
                if (!ownedCalibers.Contains(meta.caliber)) return false;
                if (minTier != Config.AmmoTier.Off
                    && BulletRarityLevel(item) < MinRarityLevelFor(minTier)) return false;
                return true;
            }
            catch { return false; }
        }

        // Shared with BulletTypeColorPatch — single source of truth for bullet-rarity formula.
        private static int BulletRarityLevel(Item item)
        {
            try { return AmmoRarity.Level((int)item.DisplayQuality, item.Quality, item.Value); }
            catch { return 0; }
        }

        // Mythic uses LightRed(5) so both red tiers qualify.
        private static int MinRarityLevelFor(Config.AmmoTier tier) => tier switch
        {
            Config.AmmoTier.Trash     => 0, // White
            Config.AmmoTier.Common    => 1, // Green
            Config.AmmoTier.Rare      => 2, // Blue
            Config.AmmoTier.VeryRare  => 3, // Purple
            Config.AmmoTier.Legendary => 4, // Orange
            Config.AmmoTier.Mythic    => 5, // LightRed/Red
            _ => 0,
        };

        private static bool IsWishlisted(Item item)
        {
            try { return ItemWishlist.Instance != null && ItemWishlist.Instance.IsManuallyWishlisted(item.TypeID); }
            catch { return false; }
        }

        private static bool IsQuestRequired(Item item)
        {
            try { return ItemWishlist.Instance != null && ItemWishlist.Instance.IsQuestRequired(item.TypeID); }
            catch { return false; }
        }

        private static bool IsBuildingRequired(Item item)
        {
            try { return ItemWishlist.Instance != null && ItemWishlist.Instance.IsBuildingRequired(item.TypeID); }
            catch { return false; }
        }

        // Matches the displayed sell price (GetTotalRawValue()/2 with int division = floored).
        // Do NOT use /2f here — must match the on-screen number exactly.
        // GetTotalRawValue is durability-scaled and stack-multiplied (unlike Item.Value).
        private static bool IsAboveValue(Item item, int threshold)
        {
            try { return item.CanBeSold && (item.GetTotalRawValue() / 2) >= threshold; }
            catch { return false; }
        }

        // Weightless items (~0 weight) never match — plain value rule covers them.
        private static bool IsAboveValuePerWeight(Item item, int threshold)
        {
            try
            {
                if (!item.CanBeSold) return false;
                float w = item.TotalWeight;
                if (w <= 0.0001f) return false;
                float ratio = (item.GetTotalRawValue() / 2f) / w;
                return ratio >= threshold;
            }
            catch { return false; }
        }

        private static bool MatchesAnyIncludeTag(Item item, string[] tags)
        {
            try
            {
                foreach (var t in tags)
                {
                    if (string.IsNullOrEmpty(t)) continue;
                    if (item.Tags.Contains(t)) return true;
                }
            }
            catch { /* ignore */ }
            return false;
        }

        // Walks character Inventory + SlotCollection. No recursion into magazines.
        private static HashSet<string> CollectOwnedGunCalibers()
        {
            var set = new HashSet<string>();
            try
            {
                var main = LevelManager.Instance?.MainCharacter;
                var charItem = main?.CharacterItem;
                if (charItem == null) return set;

                var gunTag = GameplayDataSettings.Tags.Gun;
                if (gunTag == null) return set;

                if (charItem.Inventory != null)
                {
                    foreach (var i in charItem.Inventory)
                        AddCaliberIfGun(set, i, gunTag);
                }
                if (charItem.Slots != null)
                {
                    foreach (var slot in charItem.Slots)
                    {
                        if (slot == null) continue;
                        AddCaliberIfGun(set, slot.Content, gunTag);
                    }
                }
            }
            catch (System.Exception e)
            {
                Log.Debug_($"SmartTakeEngine: caliber enumeration failed: {e.Message}");
            }
            return set;
        }

        private static void AddCaliberIfGun(HashSet<string> set, Item? item, Tag gunTag)
        {
            if (item == null) return;
            try
            {
                if (!item.Tags.Contains(gunTag)) return;
                var meta = ItemAssetsCollection.GetMetaData(item.TypeID);
                if (!string.IsNullOrEmpty(meta.caliber)) set.Add(meta.caliber);
            }
            catch { /* ignore single-item failure */ }
        }
    }
}
