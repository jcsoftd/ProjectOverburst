using System;
using System.Collections.Generic;
using System.IO;

namespace Overburst.Persistence
{
    // Retire exactly these content IDs before the registry-backed balance migration runs.
    public static class RetiredGreatswordMigration
    {
        private static readonly HashSet<string> RetiredIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "item.weapon.greatsword.catalog.001",
            "item.weapon.greatsword.catalog.053",
            "item.weapon.greatsword.catalog.058"
        };

        public static AccountSnapshot Remove(AccountSnapshot source, out int removedCount)
        {
            removedCount = 0;
            if (source == null) return null;
            if (source.items == null) throw new InvalidDataException("Missing account items during greatsword retirement.");

            foreach (var item in source.items)
                if (item != null && RetiredIds.Contains(item.contentId)) removedCount++;
            if (removedCount == 0) return source;

            var copy = ItemSnapshotCodec.CopyValues(source);
            var removedInstances = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in copy.items)
            {
                if (item == null || !RetiredIds.Contains(item.contentId)) continue;
                if (string.IsNullOrWhiteSpace(item.instanceId) || !removedInstances.Add(item.instanceId))
                    throw new InvalidDataException("Invalid retired greatsword instance identity.");
            }

            copy.items.RemoveAll(item => item != null && RetiredIds.Contains(item.contentId));
            Clear(copy.inventory, removedInstances);
            Clear(copy.weapons, removedInstances);
            Clear(copy.gear, removedInstances);
            Clear(copy.bags, removedInstances);
            Clear(copy.flasks, removedInstances);
            if (copy.stashTabs != null)
                foreach (var tab in copy.stashTabs)
                    if (tab != null) Clear(tab.slots, removedInstances);
            if (copy.merchants != null)
                foreach (var merchant in copy.merchants)
                {
                    if (merchant == null) continue;
                    Clear(merchant.stock, removedInstances);
                    Clear(merchant.currency, removedInstances);
                }
            if (copy.quickSlots != null)
                foreach (var quick in copy.quickSlots)
                    if (quick != null && quick.flaskInstanceId != null && removedInstances.Contains(quick.flaskInstanceId))
                        quick.flaskInstanceId = null;
            return copy;
        }

        private static void Clear(List<string> slots, HashSet<string> removed)
        {
            if (slots == null) return;
            for (int index = 0; index < slots.Count; index++)
                if (slots[index] != null && removed.Contains(slots[index])) slots[index] = null;
        }
    }
}
