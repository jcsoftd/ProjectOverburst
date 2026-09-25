using System;
using System.Linq;

namespace Overburst.Persistence
{
    public static class AccountGameplayProjection
    {
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
            var items = AccountPlayerProjection.Restore(state, account.Inventory, account.Stash, PlayerProgression.Current, registry, false);
            AccountMerchantProjection.Restore(state, registry, items, false);
            PlayerContext.Instance?.CurrentActorEquipment?.SynchronizeAccountLoadoutVisual();
            PlayerProgression.Current?.RefreshStats();
            account.RefreshBagBonusesForCurrentActor();
            account.Inventory.NotifyAccountApplied();
            account.Stash.NotifyAccountApplied();
            MerchantStockRefreshService.NotifyAccountApplied();
        }
    }
}
