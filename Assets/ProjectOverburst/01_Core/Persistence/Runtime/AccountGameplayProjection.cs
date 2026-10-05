using System;
using System.Linq;

namespace Overburst.Persistence
{
    public static class AccountGameplayProjection
    {
        // Copy only the inventory container and currency values. Other immutable snapshot
        // collections are shared, just as WithProgression shares unchanged item data.
        internal static AccountSnapshot CaptureCurrencyInventory(AccountSnapshot baseline,
            PlayerInventory inventory, AccountContentRegistry registry)
        {
            var oldSlots = new System.Collections.Generic.HashSet<string>(baseline.inventory);
            var byId = baseline.items.ToDictionary(x => x.instanceId, StringComparer.Ordinal);
            var items = new System.Collections.Generic.List<ItemSnapshot>(baseline.items.Count + 1);
            foreach (var item in baseline.items) if (!oldSlots.Contains(item.instanceId)) items.Add(item);
            var slots = new System.Collections.Generic.List<string>(inventory.Capacity);
            long nextOrder = baseline.nextAcquisitionOrder;
            for (int i = 0; i < inventory.Capacity; i++)
            {
                var item = inventory.GetItemAt(i);
                slots.Add(item == null ? null : item.runtimeInstanceId);
                if (item == null) continue;
                ItemSnapshot captured;
                if (item.baseData is CurrencyItemData) captured = ItemSnapshotCodec.Capture(item, registry);
                else
                {
                    if (!byId.TryGetValue(item.runtimeInstanceId, out captured)
                        || !oldSlots.Contains(item.runtimeInstanceId) || captured.count != item.stackCount)
                        throw new System.IO.InvalidDataException("Currency acquisition changed a non-currency item.");
                }
                items.Add(captured);
                nextOrder = Math.Max(nextOrder, checked(captured.acquisitionOrder + 1));
            }
            return baseline.WithCurrencyInventory(slots, items, nextOrder);
        }

        public static AccountSnapshot Capture(AccountSnapshot baseline, PlayerAccountInventoryService account, AccountContentRegistry registry)
        {
            if (account == null) throw new ArgumentNullException(nameof(account));
            var state = ItemSnapshotCodec.CopyValues(baseline);
            // Rebuild both sides together: a purchased item can move from merchant to player.
            state.items.Clear(); state.merchants.Clear();
            AccountPlayerProjection.Capture(state, account.Inventory, account.Stash, PlayerProgression.Current, registry);
            AccountMerchantProjection.Capture(state, registry);
            state.nextAcquisitionOrder = state.items.Count > 0 ? checked(state.items.Max(x => x.acquisitionOrder) + 1) : 1;
            AccountEquipmentPolicy.RecalculateCapacity(state, registry);
            AccountInvariants.Validate(state, registry);
            return state;
        }

        public static void Restore(AccountSnapshot state, PlayerAccountInventoryService account, AccountContentRegistry registry)
        {
            RestoreCore(state, account, registry, equipment => equipment.SynchronizeAccountLoadoutVisual());
        }

        internal static void RestoreForSession(AccountSnapshot state, PlayerAccountInventoryService account, AccountContentRegistry registry)
        {
            RestoreCore(state, account, registry, equipment => equipment.RequireAccountLoadoutVisual());
        }

        private static void RestoreCore(AccountSnapshot state, PlayerAccountInventoryService account, AccountContentRegistry registry, Action<PlayerEquipment> applyEquipment)
        {
            var items = AccountPlayerProjection.Restore(state, account.Inventory, account.Stash, PlayerProgression.Current, registry, false);
            AccountMerchantProjection.Restore(state, registry, items, false);
            var equipment = PlayerContext.Instance?.CurrentActorEquipment;
            if (equipment != null) applyEquipment(equipment);
            PlayerProgression.Current?.RefreshStats();
            account.RefreshBagBonusesForCurrentActor();
            account.Inventory.NotifyAccountApplied();
            account.Stash.NotifyAccountApplied();
            MerchantStockRefreshService.NotifyAccountApplied();
        }
    }
}
