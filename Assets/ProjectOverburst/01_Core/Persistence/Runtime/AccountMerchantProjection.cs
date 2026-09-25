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
            foreach (var definition in registry.Entries.Select(x => x.asset).OfType<MerchantDefinition>())
            {
                MerchantStockRefreshService.AccountInventories.TryGetValue(definition, out var inventory);
                var merchant = new MerchantSnapshot
                {
                    contentId = registry.IdFor(definition), capacity = inventory != null ? inventory.Capacity : 0, stockInitialized = inventory != null,
                    reputationLevel = MerchantReputationService.GetLevel(definition), reputationExperience = MerchantReputationService.GetExperience(definition)
                };
                if (inventory != null)
                {
                    foreach (var item in inventory.Items) merchant.stock.Add(CaptureItem(item, table, registry));
                    foreach (var item in inventory.CurrencyItems) merchant.currency.Add(CaptureItem(item, table, registry));
                }
                target.merchants.Add(merchant);
            }
            target.items = table.Values.ToList();
        }

        // Replace only candidate merchant state. Live stock changes after the whole settlement is saved.
        public static void RefreshForSuccessfulRun(AccountSnapshot target, AccountContentRegistry registry)
        {
            var replacements = MerchantStockRefreshService.CreateSuccessfulRunStocks();
            var table = target.items.ToDictionary(x => x.instanceId, StringComparer.Ordinal);
            foreach (var pair in replacements)
            {
                string contentId = registry.IdFor(pair.Key);
                var merchant = target.merchants.Find(x => x.contentId == contentId);
                if (merchant == null)
                {
                    merchant = new MerchantSnapshot
                    {
                        contentId = contentId,
                        reputationLevel = MerchantReputationService.GetLevel(pair.Key),
                        reputationExperience = MerchantReputationService.GetExperience(pair.Key)
                    };
                    target.merchants.Add(merchant);
                }
                foreach (var id in merchant.stock.Concat(merchant.currency))
                    if (!string.IsNullOrEmpty(id)) table.Remove(id);
                merchant.capacity = pair.Value.Capacity;
                merchant.stockInitialized = true;
                merchant.stock = pair.Value.Items.Select(item => CaptureItem(item, table, registry)).ToList();
                merchant.currency = pair.Value.CurrencyItems.Select(item => CaptureItem(item, table, registry)).ToList();
            }
            target.items = table.Values.ToList();
            target.nextAcquisitionOrder = target.items.Count == 0 ? 1 : checked(target.items.Max(x => x.acquisitionOrder) + 1);
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
                if (!merchant.stockInitialized) continue; // Reputation may exist before the first shop visit.
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
