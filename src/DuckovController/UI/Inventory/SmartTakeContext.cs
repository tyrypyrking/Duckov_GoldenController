using Duckov.UI;
using ItemStatsSystem;
using UnityEngine;

namespace DuckovController.UI.Inventory
{
    // Input bundle for SmartTakeEngine.Execute. Router builds this after deciding direction.
    internal readonly struct SmartTakeContext
    {
        internal readonly ItemStatsSystem.Inventory Source;
        internal readonly ItemStatsSystem.Inventory? Destination;  // destination inventory for partial-stack top-up scan; null if not applicable
        internal readonly InteractableLootbox? LootBox;          // null when source isn't a loot box (e.g. inbound from char→storage)
        internal readonly InventoryFilterDisplay? SourceFilter;  // null if source pane has no filter UI
        internal readonly ITransferStrategy Transfer;
        internal readonly Config.SmartTakeRules Rules;

        internal SmartTakeContext(
            ItemStatsSystem.Inventory source,
            ItemStatsSystem.Inventory? destination,
            InteractableLootbox? lootBox,
            InventoryFilterDisplay? sourceFilter,
            ITransferStrategy transfer,
            Config.SmartTakeRules rules)
        {
            Source = source;
            Destination = destination;
            LootBox = lootBox;
            SourceFilter = sourceFilter;
            Transfer = transfer;
            Rules = rules;
        }
    }

    internal interface ITransferStrategy
    {
        // void return: mirrors vanilla pick-all/store-all (silently leaves items behind when full).
        void Send(Item item);
    }

    // Container → character. TryPlug first (auto-equip magazines), then SendToPlayerCharacterInventory.
    internal sealed class OutboundTransfer : ITransferStrategy
    {
        public void Send(Item item)
        {
            if (item == null) return;
            var main = LevelManager.Instance?.MainCharacter;
            bool? plugged = main?.CharacterItem?.TryPlug(item, emptyOnly: true);
            if (plugged.HasValue && plugged.Value) return;
            ItemUtilities.SendToPlayerCharacterInventory(item);
        }
    }

    // Character → PlayerStorage via AddAndMerge. Router pre-filters locked indexes.
    internal sealed class InboundTransfer : ITransferStrategy
    {
        private readonly ItemStatsSystem.Inventory _destination;
        internal InboundTransfer(ItemStatsSystem.Inventory destination) { _destination = destination; }

        public void Send(Item item)
        {
            if (item == null || _destination == null) return;
            _destination.AddAndMerge(item);
        }
    }
}
