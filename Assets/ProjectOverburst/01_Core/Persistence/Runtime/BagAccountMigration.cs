using System;
using System.IO;
using System.Collections.Generic;

namespace Overburst.Persistence
{
    public static class BagAccountMigration
    {
        public static AccountSnapshot Upgrade(AccountSnapshot source, AccountContentRegistry registry, out bool changed)
        {
            changed = false;
            if (source == null || source.schemaVersion != 1) throw new InvalidDataException("Unsupported bag account schema.");
            var copy = ItemSnapshotCodec.CopyValues(source);
            if (copy.inventory == null || copy.inventory.Count != copy.inventoryCapacity || copy.items == null)
                throw new InvalidDataException("Invalid legacy inventory dimensions.");
            foreach (var item in copy.items)
                changed |= UpgradeItem(item, registry.Resolve<BaseItemData>(item.contentId), copy.level);
            if (copy.inventoryCapacity < BagQuality.InventoryCapacity)
            {
                while (copy.inventory.Count < BagQuality.InventoryCapacity) copy.inventory.Add(null);
                copy.inventoryCapacity = BagQuality.InventoryCapacity;
                changed = true;
            }
            int oldUnlocked = copy.unlockedSlots;
            AccountEquipmentPolicy.RecalculateCapacity(copy, registry);
            changed |= oldUnlocked != copy.unlockedSlots;
            return changed ? copy : source;
        }

        // Missing state alone identifies the old wire format. Never repair/reroll corrupt current rows.
        public static bool UpgradeItem(ItemSnapshot item, BaseItemData data, int acquisitionLevel)
        {
            if (!(data is BagItemData)) return false;
            if (item.bag != null)
            {
                if (!BagQuality.IsValid(item.bag, item.grade)) throw new InvalidDataException("Invalid saved bag quality.");
                return false;
            }
            ValidateLegacy(item.bagRolls);
            item.level = OverburstGrowthRules.ClampLevel(acquisitionLevel);
            item.bag = BagQuality.Roll(item.grade, BagQuality.Seed(item.instanceId), item.level >= OverburstGrowthRules.MaximumLevel);
            item.bagRolls = new List<BagRandomOptionRoll>();
            return true;
        }

        public static void UpgradeRuntime(ItemData item)
        {
            if (!(item?.baseData is BagItemData) || item.bagState != null) return;
            ValidateLegacy(item.bagOptions);
            item.EnsureRuntimeInstanceId();
            item.level = OverburstGrowthRules.ClampLevel(PlayerProgression.CurrentLevel);
            item.bagState = BagQuality.Roll(item.grade, BagQuality.Seed(item.runtimeInstanceId),
                item.level >= OverburstGrowthRules.MaximumLevel);
            item.bagOptions = new List<BagRandomOptionRoll>();
        }

        private static void ValidateLegacy(List<BagRandomOptionRoll> rows)
        {
            if (rows == null) return; // Authored templates did not have serialized rolls.
            foreach (var row in rows)
                if (row == null || !Enum.IsDefined(typeof(BagRandomOptionType), row.optionType)
                    || float.IsNaN(row.value) || float.IsInfinity(row.value) || row.value < 0)
                    throw new InvalidDataException("Invalid legacy bag option.");
        }
    }
}
