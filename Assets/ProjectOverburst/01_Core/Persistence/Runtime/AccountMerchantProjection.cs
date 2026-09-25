using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Overburst.Persistence
{
    public static class AccountMerchantProjection
    {
        public static void Capture(AccountSnapshot target, AccountContentRegistry registry)
        {
            var oldMerchantItems = new HashSet<string>(target.merchants.SelectMany(x => x.stock.Concat(x.currency)).Where(x => !string.IsNullOrEmpty(x)));
            var table = target.items.Where(x => !oldMerchantItems.Contains(x.instanceId)).ToDictionary(x => x.instanceId, StringComparer.Ordinal);
            target.merchants.Clear();
            foreach (var entry in MerchantStockRefreshService.AccountInventories)
            {
                var definition = entry.Key;
                var inventory = entry.Value;
                var merchant = new MerchantSnapshot
                {
                    contentId = registry.IdFor(definition), capacity = inventory.Capacity, stockInitialized = true,
                    reputationLevel = MerchantReputationService.GetLevel(definition), reputationExperience = MerchantReputationService.GetExperience(definition)
                };
                foreach (var item in inventory.Items) merchant.stock.Add(CaptureItem(item, table, registry));
                foreach (var item in inventory.CurrencyItems) merchant.currency.Add(CaptureItem(item, table, registry));
                target.merchants.Add(merchant);
            }
            target.items = table.Values.ToList();
        }

        private static string CaptureItem(ItemData item, Dictionary<string, ItemSnapshot> table, AccountContentRegistry registry)
        {
            if (item == null) return null;
            item.EnsureRuntimeState(); item.EnsureAcquisitionOrder();
            var value = ItemSnapshotCodec.Capture(item, registry);
            if (!table.TryAdd(value.instanceId, value)) throw new InvalidDataException("Item is owned by multiple containers: " + value.instanceId);
            return value.instanceId;
        }

        public static void Restore(AccountSnapshot source, AccountContentRegistry registry, Dictionary<string, ItemData> items, bool notify = true)
        {
            var restored = new Dictionary<MerchantDefinition, MerchantInventory>();
            foreach (var merchant in source.merchants)
            {
                var definition = registry.Resolve<MerchantDefinition>(merchant.contentId);
                if (!merchant.stockInitialized) throw new InvalidDataException("Merchant snapshot has no initialized stock.");
                var inventory = new MerchantInventory();
                inventory.ApplyAccountItems(merchant.capacity,
                    merchant.stock.Select(id => string.IsNullOrEmpty(id) ? null : items[id]),
                    merchant.currency.Select(id => string.IsNullOrEmpty(id) ? null : items[id]));
                restored.Add(definition, inventory);
            }
            MerchantStockRefreshService.ApplyAccountInventories(restored);
            MerchantReputationService.ClearAccountReputations();
            foreach (var merchant in source.merchants)
                MerchantReputationService.RestoreAccountReputation(registry.Resolve<MerchantDefinition>(merchant.contentId), merchant.reputationLevel, merchant.reputationExperience);
            if (notify) MerchantStockRefreshService.NotifyAccountApplied();
        }
    }
}
