using System;
using System.Collections.Generic;

namespace Overburst.Persistence
{
    public static class NewAccountFactory
    {
        public const string StarterWeaponId = "item.weapon.greatsword.catalog.024";
        // Explicit new-account policy; no Inspector inventory or legacy PlayerPrefs capture.
        public static AccountSnapshot Create(AccountContentRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            var state = new AccountSnapshot { level = 1, experience = 0, inventoryCapacity = BagQuality.InventoryCapacity,
                baseUnlockedSlots = 16, unlockedSlots = 16, stashCapacity = 63, currentStashTab = 0,
                activeWeaponSlot = 0, lastTransactionId = "new-account-" + Guid.NewGuid().ToString("N") };
            for (int i = 0; i < state.inventoryCapacity; i++) state.inventory.Add(null);
            for (int tab = 0; tab < 3; tab++)
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
            state.nextAcquisitionOrder = checked(snapshot.acquisitionOrder + 1);
            AccountInvariants.Validate(state, registry);
            return state;
        }
    }
}
