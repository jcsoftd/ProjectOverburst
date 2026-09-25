using System.Collections.Generic;
using UnityEngine;

public class PlayerInventory : MonoBehaviour
{
    [Header("Inventory")]
    [SerializeField] private int capacity = 35;
    [SerializeField] private int unlockedSlotCount = 16;
    [SerializeField] private List<ItemData> items = new List<ItemData>();

    public event System.Action Changed;

    public IReadOnlyList<ItemData> Items => items;
    public int Capacity => capacity;
    public int UnlockedSlotCount => Mathf.Clamp(unlockedSlotCount, 0, capacity);
    public bool IsOverCapacity => HasItemsAtOrBeyond(UnlockedSlotCount);

    internal void ApplyAccountItems(List<ItemData> values, int savedCapacity, int savedUnlockedSlots)
    {
        if (values == null || values.Count != savedCapacity || savedUnlockedSlots < 0 || savedUnlockedSlots > savedCapacity)
            throw new System.ArgumentException("Invalid account inventory projection.");
        capacity = savedCapacity;
        unlockedSlotCount = savedUnlockedSlots;
        items = values;
    }

    internal void NotifyAccountApplied() => Overburst.Persistence.AccountGameplaySession.Notify(RaiseChanged);

    private void Awake()
    {
        unlockedSlotCount = Mathf.Clamp(unlockedSlotCount, 0, capacity);
        SanitizeInvalidItems();
    }

    public bool AddItem(ItemData item)
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
            return Overburst.Persistence.AccountGameplaySession.Run(() => AddItem(item));
        Overburst.Persistence.AccountGameplaySession.TrackStack(item);
        if (IsOverCapacity) return false;
        if (item == null || !item.HasValidBaseData)
            return false;

        item.EnsureRuntimeState();
        item.EnsureAcquisitionOrder();

        if (ContainsItem(item))
            return false;

        int activeCapacity = UnlockedSlotCount;
        int remainingCount = Mathf.Max(1, item.stackCount);
        int maxStack = GetMaxStack(item);

        if (!CanFitItemStack(item, remainingCount, activeCapacity, maxStack))
            return false;

        bool changed = false;
        if (IsStackableItem(item))
        {
            for (int i = 0; i < items.Count && i < activeCapacity && remainingCount > 0; i++)
            {
                ItemData targetItem = items[i];
                if (!CanStackItems(item, targetItem))
                    continue;

                int addCount = Mathf.Min(maxStack - targetItem.stackCount, remainingCount);
                targetItem.stackCount += addCount;
                remainingCount -= addCount;
                changed = true;
            }
        }

        bool originalPlaced = false;
        while (remainingCount > 0)
        {
            int stackCount = Mathf.Min(maxStack, remainingCount);
            ItemData stackItem;
            if (!originalPlaced)
            {
                item.stackCount = stackCount;
                stackItem = item;
                originalPlaced = true;
            }
            else
            {
                stackItem = item.CopyStack(stackCount, true);
            }

            stackItem.EnsureRuntimeState();
            stackItem.EnsureAcquisitionOrder();

            if (!PlaceItemInFirstEmptySlot(stackItem, activeCapacity))
                return false;

            remainingCount -= stackCount;
            changed = true;
        }

        if (changed)
            Overburst.Persistence.AccountGameplaySession.Notify(RaiseChanged);

        return changed;
    }

    public bool RemoveItem(ItemData item)
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
            return Overburst.Persistence.AccountGameplaySession.Run(() => RemoveItem(item));
        if (item == null)
            return false;

        return ClearFirstMatchingItem(item);
    }

    public bool ConsumeItem(ItemData item, int amount)
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
            return Overburst.Persistence.AccountGameplaySession.Run(() => ConsumeItem(item, amount));
        if (item == null || amount <= 0)
            return false;

        int index = FindFirstMatchingItemIndex(item);
        if (index < 0)
            return false;

        ItemData target = items[index];
        Overburst.Persistence.AccountGameplaySession.TrackStack(target);
        if (target == null || target.stackCount <= 0 || amount > target.stackCount)
            return false;

        if (target.stackCount > amount)
            target.stackCount -= amount;
        else
            items[index] = null;

        Overburst.Persistence.AccountGameplaySession.Notify(RaiseChanged);
        return true;
    }

    public ItemData GetFirstWeaponItem()
    {
        for (int i = 0; i < items.Count; i++)
        {
            ItemData item = items[i];
            if (item != null && item.itemType == "Weapon")
                return item;
        }

        return null;
    }

    public ItemData GetItemAt(int index)
    {
        if (index < 0 || index >= items.Count)
            return null;

        return items[index];
    }

    public bool ContainsItem(ItemData item)
    {
        return FindFirstMatchingItemIndex(item) >= 0;
    }

    public ItemData FindFirstItemByBaseData(BaseItemData baseData)
    {
        if (baseData == null)
            return null;

        for (int i = 0; i < items.Count; i++)
        {
            ItemData item = items[i];
            if (item != null && item.baseData == baseData && item.stackCount > 0)
                return item;
        }

        return null;
    }

    public int CountItemsByBaseData(BaseItemData baseData)
    {
        if (baseData == null)
            return 0;

        int count = 0;
        for (int i = 0; i < items.Count; i++)
        {
            ItemData item = items[i];
            if (item != null && item.baseData == baseData && item.stackCount > 0)
                count += item.stackCount;
        }

        return count;
    }

    public int FindFirstMatchingItemIndex(ItemData item)
    {
        if (item == null)
            return -1;

        for (int i = 0; i < items.Count; i++)
        {
            if (IsSameRuntimeItem(items[i], item))
                return i;
        }

        return -1;
    }

    public bool SetItemAt(int index, ItemData item)
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
            return Overburst.Persistence.AccountGameplaySession.Run(() => SetItemAt(index, item));
        if (index < 0 || index >= capacity || (item != null && index >= UnlockedSlotCount))
            return false;
        if (item != null && IsOverCapacity && !ContainsItem(item))
            return false;

        if (item != null && !item.HasValidBaseData)
            return false;

        if (item != null)
        {
            item.EnsureRuntimeState();
            ClearFirstMatchingItem(item, index);
        }

        EnsureSlotExists(index);
        items[index] = item;
        Overburst.Persistence.AccountGameplaySession.Notify(RaiseChanged);
        return true;
    }

    public bool CanReplaceOwnedItemAt(int index, ItemData expected, ItemData replacement)
    {
        if (index < 0 || index >= capacity)
            return false;
        if (replacement != null && (index >= UnlockedSlotCount ||
            (IsOverCapacity && !ContainsItem(replacement))))
            return false;

        ItemData current = index < items.Count ? items[index] : null;
        if (!AreSameStoredItem(current, expected))
            return false; // 예상 소유권 불일치

        if (replacement == null)
            return true;

        if (!replacement.HasValidBaseData)
            return false;

        int duplicateIndex = FindFirstMatchingItemIndex(replacement);
        return duplicateIndex < 0 || duplicateIndex == index;
    }

    public bool TryReplaceOwnedItemAt(int index, ItemData expected, ItemData replacement)
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
            return Overburst.Persistence.AccountGameplaySession.Run(() => TryReplaceOwnedItemAt(index, expected, replacement));
        if (!CanReplaceOwnedItemAt(index, expected, replacement))
            return false;

        replacement?.EnsureRuntimeState();
        EnsureSlotExists(index);
        items[index] = replacement; // 한 슬롯에서 소유권 교체
        Overburst.Persistence.AccountGameplaySession.Notify(RaiseChanged);
        return true;
    }

    public bool MoveOrSwapItems(int fromIndex, int toIndex)
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
            return Overburst.Persistence.AccountGameplaySession.Run(() => MoveOrSwapItems(fromIndex, toIndex));
        return MoveMergeOrSwapItems(fromIndex, toIndex);
    }

    public bool CanSplitStackAt(int index)
    {
        ItemData item = GetItemAt(index);
        return IsStackableItem(item) && item.stackCount > 1 && FindFirstEmptySlot() >= 0;
    }

    public bool SplitStackAt(int index, int amount)
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
            return Overburst.Persistence.AccountGameplaySession.Run(() => SplitStackAt(index, amount));
        int activeCapacity = UnlockedSlotCount;
        if (index < 0 || index >= items.Count || index >= activeCapacity)
            return false;

        ItemData source = items[index];
        if (!IsStackableItem(source) || source.stackCount <= 1)
            return false;

        int splitCount = Mathf.Clamp(amount, 1, source.stackCount - 1);
        int emptySlot = FindFirstEmptySlot();
        if (emptySlot < 0)
            return false;

        ItemData splitItem = source.CopyStack(splitCount, true);
        splitItem.EnsureRuntimeState();
        splitItem.acquisitionOrder = source.acquisitionOrder;

        source.stackCount -= splitCount;
        EnsureSlotExists(emptySlot);
        items[emptySlot] = splitItem;
        Overburst.Persistence.AccountGameplaySession.Notify(RaiseChanged);
        return true;
    }

    public bool MoveMergeOrSwapItems(int fromIndex, int toIndex)
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
            return Overburst.Persistence.AccountGameplaySession.Run(() => MoveMergeOrSwapItems(fromIndex, toIndex));
        int activeCapacity = UnlockedSlotCount;

        if (fromIndex < 0 || fromIndex >= items.Count || toIndex < 0 || toIndex >= activeCapacity)
            return false;

        if (fromIndex == toIndex)
            return false;

        ItemData fromItem = items[fromIndex];
        if (fromItem == null)
            return false;

        EnsureSlotExists(toIndex);

        ItemData targetItem = items[toIndex];
        if (CanStackItems(fromItem, targetItem))
        {
            int addCount = Mathf.Min(GetMaxStack(targetItem) - targetItem.stackCount, fromItem.stackCount);
            targetItem.stackCount += addCount;
            fromItem.stackCount -= addCount;

            if (fromItem.stackCount <= 0)
                items[fromIndex] = null;

            Overburst.Persistence.AccountGameplaySession.Notify(RaiseChanged);
            return true;
        }

        items[fromIndex] = targetItem;
        items[toIndex] = fromItem;
        Overburst.Persistence.AccountGameplaySession.Notify(RaiseChanged);
        return true;
    }

    public bool ClearSlot(int index)
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
            return Overburst.Persistence.AccountGameplaySession.Run(() => ClearSlot(index));
        if (index < 0 || index >= items.Count)
            return false;

        if (items[index] == null)
            return false;

        items[index] = null;
        Overburst.Persistence.AccountGameplaySession.Notify(RaiseChanged);
        return true;
    }

    public bool ClearFirstMatchingItem(ItemData item, int exceptIndex = -1)
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
            return Overburst.Persistence.AccountGameplaySession.Run(() => ClearFirstMatchingItem(item, exceptIndex));
        if (item == null)
            return false;

        for (int i = 0; i < items.Count; i++)
        {
            if (i == exceptIndex || !IsSameRuntimeItem(items[i], item))
                continue;

            items[i] = null;
            Overburst.Persistence.AccountGameplaySession.Notify(RaiseChanged);
            return true;
        }

        return false;
    }

    public bool ClearAllMatchingItems(ItemData item, int exceptIndex = -1)
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
            return Overburst.Persistence.AccountGameplaySession.Run(() => ClearAllMatchingItems(item, exceptIndex));
        if (item == null)
            return false;

        bool changed = false;

        for (int i = 0; i < items.Count; i++)
        {
            if (i == exceptIndex || !IsSameRuntimeItem(items[i], item))
                continue;

            items[i] = null;
            changed = true;
        }

        if (changed)
            Overburst.Persistence.AccountGameplaySession.Notify(RaiseChanged);

        return changed;
    }

    public int FindFirstEmptySlot()
    {
        if (IsOverCapacity) return -1;
        for (int i = 0; i < UnlockedSlotCount; i++)
        {
            EnsureSlotExists(i);

            if (items[i] == null)
                return i;
        }

        return -1;
    }

    public bool SetUnlockedSlotCount(int value)
    {
        int clamped = Mathf.Clamp(value, 0, capacity);

        if (unlockedSlotCount == clamped)
            return true;

        unlockedSlotCount = clamped;
        Overburst.Persistence.AccountGameplaySession.Notify(RaiseChanged);
        return true;
    }

    public bool HasItemsAtOrBeyond(int slotIndex)
    {
        int start = Mathf.Clamp(slotIndex, 0, capacity);

        for (int i = start; i < items.Count; i++)
        {
            if (items[i] != null)
                return true;
        }

        return false;
    }

    public int FindFirstEmptySlotWithin(int slotCount)
    {
        int limit = Mathf.Clamp(slotCount, 0, capacity);

        for (int i = 0; i < limit; i++)
        {
            EnsureSlotExists(i);

            if (items[i] == null)
                return i;
        }

        return -1;
    }

    public bool TryGetItem(int index, out ItemData item)
    {
        item = null;

        if (index < 0 || index >= items.Count)
            return false;

        item = items[index];
        return item != null;
    }

    public void Clear()
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
        {
            Overburst.Persistence.AccountGameplaySession.Run(() => { Clear(); return true; });
            return;
        }
        items.Clear();
        Overburst.Persistence.AccountGameplaySession.Notify(RaiseChanged);
    }

    public bool SortUnlockedSlots(ItemSortMode sortMode, ItemSortDirection sortDirection)
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
            return Overburst.Persistence.AccountGameplaySession.Run(() => SortUnlockedSlots(sortMode, sortDirection));
        int activeCapacity = UnlockedSlotCount;
        if (activeCapacity <= 1)
            return false;

        for (int i = 0; i < activeCapacity; i++)
            EnsureSlotExists(i);

        ConsolidateUnlockedStacks(activeCapacity);
        ItemSortComparer.Sort(items, 0, activeCapacity, sortMode, sortDirection);
        Overburst.Persistence.AccountGameplaySession.Notify(RaiseChanged);
        return true;
    }

    private void ConsolidateUnlockedStacks(int activeCapacity)
    {
        for (int targetIndex = 0; targetIndex < activeCapacity; targetIndex++)
        {
            ItemData target = items[targetIndex];
            if (!IsStackableItem(target))
                continue;

            int maxStack = GetMaxStack(target);
            if (target.stackCount >= maxStack)
                continue;

            for (int sourceIndex = targetIndex + 1; sourceIndex < activeCapacity && target.stackCount < maxStack; sourceIndex++)
            {
                ItemData source = items[sourceIndex];
                if (!CanStackItems(source, target))
                    continue;

                int moveCount = Mathf.Min(maxStack - target.stackCount, source.stackCount);
                target.stackCount += moveCount;
                source.stackCount -= moveCount;

                if (source.stackCount <= 0)
                    items[sourceIndex] = null;
            }
        }
    }

    private bool PlaceItemInFirstEmptySlot(ItemData item, int activeCapacity)
    {
        for (int i = 0; i < items.Count && i < activeCapacity; i++)
        {
            if (items[i] != null)
                continue;

            items[i] = item;
            return true;
        }

        if (items.Count >= activeCapacity)
            return false;

        items.Add(item);
        return true;
    }

    private void EnsureSlotExists(int index)
    {
        while (items.Count <= index && items.Count < capacity)
            items.Add(null);
    }

    private void SanitizeInvalidItems()
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] != null && !items[i].HasValidBaseData)
                items[i] = null;
        }
    }

    private bool CanStackItems(ItemData source, ItemData target)
    {
        if (source == null || target == null)
            return false;

        if (source.baseData == null || target.baseData == null || source.baseData != target.baseData)
            return false;

        if (source.grade != target.grade || source.level != target.level || source.originRunId != target.originRunId)
            return false;

        if (source.stackCount <= 0 || target.stackCount <= 0)
            return false;

        return IsStackableItem(source) && target.stackCount < GetMaxStack(target);
    }

    private bool CanFitItemStack(ItemData source, int count, int activeCapacity, int maxStack)
    {
        int available = 0;

        if (IsStackableItem(source))
        {
            for (int i = 0; i < items.Count && i < activeCapacity; i++)
            {
                ItemData target = items[i];
                if (target != null && CanStackItems(source, target))
                    available += Mathf.Max(0, maxStack - target.stackCount);
            }
        }

        for (int i = 0; i < activeCapacity; i++)
        {
            ItemData target = i < items.Count ? items[i] : null;
            if (target == null)
                available += maxStack;

            if (available >= count)
                return true;
        }

        return available >= count;
    }

    private int GetMaxStack(ItemData item)
    {
        if (item != null && item.baseData is ConsumableItemData consumableData)
        {
            if (consumableData.IsPermanentSingleItem)
                return 1;

            return Mathf.Max(1, consumableData.maxStack);
        }

        if (item != null && item.baseData is CurrencyItemData currencyData)
            return Mathf.Max(1, currencyData.maxStack);

        return IsStackableItem(item) ? 99 : 1;
    }

    private bool IsStackableItem(ItemData item)
    {
        if (item == null)
            return false;

        string itemType = item.itemType;
        if (item.baseData is ConsumableItemData consumableData && consumableData.IsPermanentSingleItem)
            return false;

        return itemType == "Consumable" || itemType == "Junk" || itemType == "QuestItem" || itemType == "Currency";
    }

    private bool IsSameRuntimeItem(ItemData left, ItemData right)
    {
        if (left == null || right == null)
            return false;

        return left.IsSameRuntimeItem(right);
    }

    private bool AreSameStoredItem(ItemData left, ItemData right)
    {
        if (left == null || right == null)
            return left == null && right == null;

        return IsSameRuntimeItem(left, right);
    }

    private void RaiseChanged() => Changed?.Invoke();
}
