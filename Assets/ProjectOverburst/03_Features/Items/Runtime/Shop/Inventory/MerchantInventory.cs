using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class MerchantInventory
{
    private readonly List<ItemData> items = new List<ItemData>();
    private readonly List<ItemData> currencyItems = new List<ItemData>();
    private int capacity = 20;

    public IReadOnlyList<ItemData> Items { get { return items; } }
    public int Capacity { get { return capacity; } }

    public void InitializeEmpty(int inventoryCapacity)
    {
        capacity = Mathf.Max(1, inventoryCapacity);
        items.Clear();
        currencyItems.Clear();
        for (int i = 0; i < capacity; i++)
            items.Add(null);
    }

    public void Initialize(MerchantDefinition definition)
    {
        InitializeEmpty(definition != null ? definition.InventoryCapacity : 20);

        if (definition == null)
            return;

        TryAddCurrency(CurrencyType.Gold, definition.MerchantGold);

        if (definition.StockItems == null)
            return;

        for (int i = 0; i < definition.StockItems.Count; i++)
        {
            MerchantStockItemEntry entry = definition.StockItems[i];
            if (entry == null || !entry.HasItem || !ItemGradeAvailabilityPolicy.IsEnabled(entry.Grade))
                continue;

            ItemData item = new ItemData(entry.ItemData, 1, entry.Grade, entry.StackCount);
            item.EnsureRuntimeState();
            item.EnsureAcquisitionOrder();
            AddItem(item);
        }
    }

    public ItemData GetItemAt(int index)
    {
        return index >= 0 && index < items.Count ? items[index] : null;
    }

    public bool ContainsItemAt(int index, ItemData item)
    {
        ItemData current = GetItemAt(index);
        return current != null && item != null && current.IsSameRuntimeItem(item);
    }

    public bool AddItem(ItemData item)
    {
        if (item == null || !item.HasValidBaseData)
            return false;

        item.EnsureRuntimeState();
        item.EnsureAcquisitionOrder();

        if (ContainsRuntimeItem(item))
            return false;

        int remaining = Mathf.Max(1, item.stackCount);
        int maxStack = GetMaxStack(item);

        if (!CanAcceptItem(item, null))
            return false;

        if (IsStackableItem(item))
        {
            for (int i = 0; i < items.Count && remaining > 0; i++)
            {
                ItemData target = items[i];
                if (!CanStackItems(item, target))
                    continue;

                int moveCount = Mathf.Min(maxStack - target.stackCount, remaining);
                target.stackCount += moveCount;
                remaining -= moveCount;
            }
        }

        bool originalPlaced = false;
        while (remaining > 0)
        {
            int slotIndex = FindFirstEmptySlot();
            if (slotIndex < 0)
                return false;

            int stackCount = Mathf.Min(maxStack, remaining);
            ItemData stackItem;
            if (!originalPlaced)
            {
                item.stackCount = stackCount;
                stackItem = item;
                originalPlaced = true;
            }
            else
            {
                stackItem = new ItemData(item.baseData, item.level, item.grade, stackCount);
            }

            stackItem.EnsureRuntimeState();
            stackItem.EnsureAcquisitionOrder();
            items[slotIndex] = stackItem;
            remaining -= stackCount;
        }

        return true;
    }

    public int GetCurrencyAmount(CurrencyType type)
    {
        int total = 0;
        for (int i = 0; i < currencyItems.Count; i++)
        {
            ItemData item = currencyItems[i];
            if (IsCurrency(item, type))
                total += item.stackCount;
        }

        return total;
    }

    public bool CanSpendCurrency(CurrencyType type, int amount)
    {
        return amount <= 0 || GetCurrencyAmount(type) >= amount;
    }

    public bool TrySpendCurrency(CurrencyType type, int amount)
    {
        int remaining = Mathf.Max(0, amount);
        if (remaining <= 0)
            return true;

        if (!CanSpendCurrency(type, remaining))
            return false;

        for (int i = 0; i < currencyItems.Count && remaining > 0; i++)
        {
            ItemData item = currencyItems[i];
            if (!IsCurrency(item, type))
                continue;

            int spendCount = Mathf.Min(item.stackCount, remaining);
            item.stackCount -= spendCount;
            remaining -= spendCount;

            if (item.stackCount <= 0)
                currencyItems[i] = null;
        }

        CompactCurrencyItems();
        return remaining <= 0;
    }

    public bool TryAddCurrency(CurrencyType type, int amount)
    {
        int remaining = Mathf.Max(0, amount);
        if (remaining <= 0)
            return true;

        CurrencyItemData currencyData = CurrencyItemRegistry.Get(type);
        if (currencyData == null)
            return false;

        int maxStack = Mathf.Max(1, currencyData.maxStack);
        for (int i = 0; i < currencyItems.Count && remaining > 0; i++)
        {
            ItemData target = currencyItems[i];
            if (!IsCurrency(target, type) || target.stackCount >= maxStack)
                continue;

            int addCount = Mathf.Min(maxStack - target.stackCount, remaining);
            target.stackCount += addCount;
            remaining -= addCount;
        }

        while (remaining > 0)
        {
            int stackCount = Mathf.Min(maxStack, remaining);
            ItemData stack = new ItemData(currencyData, 1, ItemGrade.Common, stackCount);
            stack.EnsureRuntimeState();
            stack.EnsureAcquisitionOrder();
            currencyItems.Add(stack);
            remaining -= stackCount;
        }

        return true;
    }

    public bool RemoveItemAt(int index, ItemData expected)
    {
        return RemoveItemAt(index, expected, expected != null ? expected.stackCount : 0);
    }

    public bool RemoveItemAt(int index, ItemData expected, int amount)
    {
        if (!ContainsItemAt(index, expected))
            return false;

        ItemData current = items[index];
        int removeCount = Mathf.Max(1, amount);
        if (current.stackCount > removeCount)
            current.stackCount -= removeCount;
        else
            items[index] = null;

        return true;
    }

    public bool CanAcceptItemsAfterRemoving(IList<int> removeIndices, IList<ItemData> incomingItems)
    {
        List<ItemData> simulation = CopyItems();
        if (removeIndices != null)
        {
            for (int i = 0; i < removeIndices.Count; i++)
            {
                int index = removeIndices[i];
                if (index >= 0 && index < simulation.Count)
                    simulation[index] = null;
            }
        }

        return CanAcceptIntoSimulation(simulation, incomingItems);
    }

    public bool CanAcceptItem(ItemData item, IList<int> removeIndices)
    {
        List<ItemData> incoming = new List<ItemData>();
        if (item != null)
            incoming.Add(item);

        return CanAcceptItemsAfterRemoving(removeIndices, incoming);
    }

    public bool CanAcceptItemsAfterOutgoingOffers(IReadOnlyList<MerchantTradeOffer> outgoingOffers, IList<ItemData> incomingItems)
    {
        List<ItemData> simulation = CopyItems();
        if (outgoingOffers != null)
        {
            for (int i = 0; i < outgoingOffers.Count; i++)
            {
                MerchantTradeOffer offer = outgoingOffers[i];
                if (offer == null || !RemoveOfferFromSimulation(simulation, offer))
                    return false;
            }
        }

        return CanAcceptIntoSimulation(simulation, incomingItems);
    }

    private bool CanAcceptIntoSimulation(List<ItemData> simulation, IList<ItemData> incomingItems)
    {
        if (incomingItems == null)
            return true;

        for (int i = 0; i < incomingItems.Count; i++)
        {
            ItemData incoming = incomingItems[i];
            if (incoming == null || !incoming.HasValidBaseData)
                continue;

            if (!SimulateAdd(simulation, incoming))
                return false;
        }

        return true;
    }

    private bool RemoveOfferFromSimulation(List<ItemData> simulation, MerchantTradeOffer offer)
    {
        int index = offer.SourceSlotIndex;
        if (index < 0 || index >= simulation.Count)
            return false;

        ItemData current = simulation[index];
        if (current == null || offer.SourceItem == null || !current.IsSameRuntimeItem(offer.SourceItem))
            return false;

        int removeCount = Mathf.Max(1, offer.StackCount);
        if (current.stackCount > removeCount)
        {
            ItemData clone = CloneForSimulation(current);
            clone.stackCount = current.stackCount - removeCount;
            simulation[index] = clone;
        }
        else
        {
            simulation[index] = null;
        }

        return true;
    }

    private bool SimulateAdd(List<ItemData> simulation, ItemData item)
    {
        int remaining = Mathf.Max(1, item.stackCount);
        int maxStack = GetMaxStack(item);

        if (IsStackableItem(item))
        {
            for (int i = 0; i < simulation.Count && remaining > 0; i++)
            {
                ItemData target = simulation[i];
                if (!CanStackItems(item, target))
                    continue;

                int addCount = Mathf.Min(maxStack - target.stackCount, remaining);
                target = CloneForSimulation(target);
                target.stackCount += addCount;
                simulation[i] = target;
                remaining -= addCount;
            }
        }

        while (remaining > 0)
        {
            int empty = FindFirstEmptySlot(simulation);
            if (empty < 0)
                return false;

            int stackCount = Mathf.Min(maxStack, remaining);
            ItemData clone = CloneForSimulation(item);
            clone.stackCount = stackCount;
            simulation[empty] = clone;
            remaining -= stackCount;
        }

        return true;
    }

    private List<ItemData> CopyItems()
    {
        List<ItemData> copy = new List<ItemData>(capacity);
        for (int i = 0; i < capacity; i++)
            copy.Add(i < items.Count ? items[i] : null);

        return copy;
    }

    private bool ContainsRuntimeItem(ItemData item)
    {
        if (item == null)
            return false;

        for (int i = 0; i < items.Count; i++)
        {
            ItemData current = items[i];
            if (current != null && current.IsSameRuntimeItem(item))
                return true;
        }

        return false;
    }

    private int FindFirstEmptySlot()
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] == null)
                return i;
        }

        return -1;
    }

    private int FindFirstEmptySlot(List<ItemData> source)
    {
        for (int i = 0; i < source.Count; i++)
        {
            if (source[i] == null)
                return i;
        }

        return -1;
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

        if (item.baseData is ConsumableItemData consumableData && consumableData.IsPermanentSingleItem)
            return false;

        string itemType = item.itemType;
        return itemType == "Consumable" || itemType == "Junk" || itemType == "QuestItem" || itemType == "Currency";
    }

    private bool CanStackItems(ItemData source, ItemData target)
    {
        if (source == null || target == null || source.baseData == null || target.baseData == null)
            return false;

        return source.baseData == target.baseData
            && source.grade == target.grade
            && source.level == target.level
            && target.stackCount > 0
            && target.stackCount < GetMaxStack(target)
            && IsStackableItem(source);
    }

    private ItemData CloneForSimulation(ItemData item)
    {
        return new ItemData(item.baseData, item.level, item.grade, item.stackCount);
    }

    private bool IsCurrency(ItemData item, CurrencyType type)
    {
        return item != null
            && item.stackCount > 0
            && item.baseData is CurrencyItemData currencyData
            && currencyData.currencyType == type;
    }

    private void CompactCurrencyItems()
    {
        for (int i = currencyItems.Count - 1; i >= 0; i--)
        {
            if (currencyItems[i] == null || currencyItems[i].stackCount <= 0)
                currencyItems.RemoveAt(i);
        }
    }
}
