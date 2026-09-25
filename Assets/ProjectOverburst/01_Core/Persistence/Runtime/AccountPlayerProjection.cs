using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Overburst.Persistence
{
    // Converts existing gameplay containers; it does not implement a second inventory rule engine.
    public static class AccountPlayerProjection
    {
        public static void Capture(AccountSnapshot target, PlayerInventory inventory, PlayerStash stash, PlayerProgression progression, AccountContentRegistry registry)
        {
            if (inventory == null || stash == null) throw new InvalidOperationException("Account inventory and stash must exist before capture.");
            var loadout = PlayerAccountInventoryService.Loadout;
            var merchantIds = new HashSet<string>(target.merchants.SelectMany(x => x.stock.Concat(x.currency)).Where(x => !string.IsNullOrEmpty(x)));
            var table = target.items.Where(x => merchantIds.Contains(x.instanceId)).ToDictionary(x => x.instanceId, StringComparer.Ordinal);
            Func<IReadOnlyList<ItemData>, int, List<string>> capture = (source, count) =>
            {
                var slots = new List<string>(count);
                for (int index = 0; index < count; index++)
                {
                    var item = index < source.Count ? source[index] : null;
                    if (item == null) { slots.Add(null); continue; }
                    item.EnsureRuntimeState(); item.EnsureAcquisitionOrder();
                    var snapshot = ItemSnapshotCodec.Capture(item, registry);
                    if (!table.TryAdd(snapshot.instanceId, snapshot)) throw new InvalidDataException("Multiple owners for item " + snapshot.instanceId);
                    slots.Add(snapshot.instanceId);
                }
                return slots;
            };
            target.inventoryCapacity = inventory.Capacity;
            target.unlockedSlots = inventory.UnlockedSlotCount;
            target.inventory = capture(inventory.Items, inventory.Capacity);
            target.stashCapacity = stash.Capacity;
            target.currentStashTab = stash.CurrentTabIndex;
            target.stashTabs.Clear();
            for (int tab = 0; tab < stash.TabCount; tab++)
                target.stashTabs.Add(new ItemContainerSnapshot { slots = capture(stash.GetItemsInTab(tab), stash.Capacity) });
            target.weapons = capture(loadout.Weapons, loadout.Weapons.Length);
            target.gear = capture(loadout.Gear, loadout.Gear.Length);
            target.bags = capture(loadout.Bags, loadout.Bags.Length);
            target.activeWeaponSlot = loadout.ActiveWeaponSlot;
            target.flasks = new List<string>(loadout.FlaskIds);
            target.quickSlots.Clear();
            for (int index = 0; index < loadout.QuickConsumables.Length; index++)
            {
                if (loadout.QuickSkills[index] != null) throw new InvalidOperationException("Skill content must define a persistent binding before account saving can include it.");
                target.quickSlots.Add(new QuickSlotSnapshot
                {
                    consumableContentId = loadout.QuickConsumables[index] != null ? registry.IdFor(loadout.QuickConsumables[index]) : null,
                    flaskInstanceId = loadout.QuickFlaskIds[index]
                });
            }
            target.items = table.Values.ToList();
            target.nextAcquisitionOrder = target.items.Count > 0 ? checked(target.items.Max(x => x.acquisitionOrder) + 1) : 1;
            if (progression != null) { target.level = progression.Level; target.experience = progression.Experience; }
        }

        public static Dictionary<string, ItemData> Restore(AccountSnapshot source, PlayerInventory inventory, PlayerStash stash, PlayerProgression progression, AccountContentRegistry registry, bool notify = true)
        {
            AccountInvariants.Validate(source, registry);
            if (source.weapons.Count != 1 || source.gear.Count != 7 || source.bags.Count != 1 || source.flasks.Count != 3 || source.quickSlots.Count != 10 || source.stashTabs.Count != 3)
                throw new InvalidDataException("Unsupported account loadout dimensions.");
            if (source.activeWeaponSlot != 0) throw new InvalidDataException("Invalid active weapon slot.");
            var table = source.items.ToDictionary(x => x.instanceId, x => ItemSnapshotCodec.Restore(x, registry), StringComparer.Ordinal);
            Func<IEnumerable<string>, List<ItemData>> resolve = ids => ids.Select(id => string.IsNullOrEmpty(id) ? null : table[id]).ToList();
            var next = new PlayerAccountLoadout
            {
                Weapons = resolve(source.weapons).ToArray(), Gear = resolve(source.gear).ToArray(), Bags = resolve(source.bags).ToArray(),
                ActiveWeaponSlot = source.activeWeaponSlot, EquipmentInitialized = true,
                FlaskIds = source.flasks.ToArray(), FlasksInitialized = true, LegacyFlasksImported = true
            };
            for (int index = 0; index < next.QuickConsumables.Length; index++)
            {
                var quick = source.quickSlots[index];
                if (!string.IsNullOrEmpty(quick.skillId)) throw new InvalidDataException("Saved skill binding has no installed skill content resolver.");
                next.QuickConsumables[index] = string.IsNullOrEmpty(quick.consumableContentId) ? null : registry.Resolve<ConsumableItemData>(quick.consumableContentId);
                next.QuickFlaskIds[index] = quick.flaskInstanceId;
            }
            var inventoryItems = resolve(source.inventory);
            var stashTabs = source.stashTabs.Select(x => resolve(x.slots)).ToArray();
            // Resolve and validate everything before replacing the live views.
            inventory.ApplyAccountItems(inventoryItems, source.inventoryCapacity, source.unlockedSlots);
            stash.ApplyAccountItems(stashTabs, source.stashCapacity, source.currentStashTab);
            PlayerAccountInventoryService.ReplaceLoadout(next);
            progression?.ApplyAccountProgression(source.level, source.experience, false);
            if (notify)
            {
                PlayerContext.Instance?.CurrentActorEquipment?.SynchronizeAccountLoadoutVisual();
                progression?.RefreshStats();
                PlayerAccountInventoryService.Instance?.RefreshBagBonusesForCurrentActor();
                inventory.NotifyAccountApplied(); stash.NotifyAccountApplied();
            }
            return table;
        }
    }
}
