using System;
using System.Collections.Generic;

namespace Overburst.Persistence
{
    public static class NewAccountFactory
    {
        public const string StarterWeaponId = "item.weapon.greatsword.catalog.024";
        public const string StarterElementGemId = "item.elementgem.fire.common";
        // Explicit new-account policy; no Inspector inventory or legacy PlayerPrefs capture.
        public static AccountSnapshot Create(AccountContentRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            var state = new AccountSnapshot { level = 1, experience = 0, inventoryCapacity = BagQuality.InventoryCapacity,
                baseUnlockedSlots = 16, unlockedSlots = 16, stashCapacity = 63, currentStashTab = 0,
                activeWeaponSlot = 0, lastTransactionId = "new-account-" + Guid.NewGuid().ToString("N") };
            for (int i = 0; i < state.inventoryCapacity; i++) state.inventory.Add(null);
            for (int tab = 0; tab < PlayerStash.TotalTabCount; tab++)
            {
                var container = new ItemContainerSnapshot();
                for (int i = 0; i < state.stashCapacity; i++) container.slots.Add(null);
                state.stashTabs.Add(container);
            }
            for (int i = 0; i < 6; i++) state.gear.Add(null);
            state.weapons.Add(null); state.bags.Add(null);
            for (int i = 0; i < 3; i++) state.flasks.Add(null);
            for (int i = 0; i < 10; i++) state.quickSlots.Add(new QuickSlotSnapshot());
            var weapon = new ItemData(registry.Resolve<WeaponItemData>(StarterWeaponId), 1, ItemGrade.Common);
            weapon.EnsureAcquisitionOrder();
            var snapshot = ItemSnapshotCodec.Capture(weapon, registry);
            state.items.Add(snapshot); state.weapons[0] = snapshot.instanceId;
            var gem = new ItemData(registry.Resolve<ElementGemItemData>(StarterElementGemId), 1, ItemGrade.Common);
            gem.EnsureRuntimeState();
            gem.EnsureAcquisitionOrder();
            var gemSnapshot = ItemSnapshotCodec.Capture(gem, registry);
            state.items.Add(gemSnapshot); state.elementalGemInstanceId = gemSnapshot.instanceId;
            state.nextAcquisitionOrder = checked(Math.Max(snapshot.acquisitionOrder, gemSnapshot.acquisitionOrder) + 1);
            GrantTesterSupport(state, registry);
            AccountInvariants.Validate(state, registry);
            return state;
        }

        public static bool CanGrantTesterSupport(AccountSnapshot state)
        {
            if (state.testerSupportGranted) return false;
            for (int i = 0; i < state.unlockedSlots; i++)
                if (string.IsNullOrEmpty(state.inventory[i])) return true;
            return false;
        }

        public static void GrantTesterSupport(AccountSnapshot state, AccountContentRegistry registry)
        {
            if (!CanGrantTesterSupport(state)) return;
            var item = new ItemData(registry.Resolve<TesterSupportItemData>(TesterSupportItemData.ContentId), 1, ItemGrade.Common);
            item.EnsureAcquisitionOrder();
            var snapshot = ItemSnapshotCodec.Capture(item, registry);
            snapshot.acquisitionOrder = state.nextAcquisitionOrder++;
            int slot = state.inventory.FindIndex(0, state.unlockedSlots, string.IsNullOrEmpty);
            state.items.Add(snapshot);
            state.inventory[slot] = snapshot.instanceId;
            state.testerSupportGranted = true;
        }
    }
}
