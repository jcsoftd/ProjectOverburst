using System.Collections.Generic;
using UnityEngine;

public static class MerchantTradeItemUtility
{
    public static int GetInventoryGoldAmount(PlayerInventory playerInventory)
    {
        if (playerInventory == null)
            return 0;

        int total = 0;
        int limit = playerInventory.UnlockedSlotCount;
        for (int i = 0; i < limit; i++)
        {
            ItemData item = playerInventory.GetItemAt(i);
            if (IsGold(item))
                total += item.stackCount;
        }

        return total;
    }

    public static ItemData CreateGoldItem(int amount)
    {
        CurrencyItemData goldData = CurrencyItemRegistry.Get(CurrencyType.Gold);
        if (goldData == null || amount <= 0)
            return null;

        ItemData item = new ItemData(goldData, 1, ItemGrade.Common, amount);
        item.EnsureRuntimeState();
        item.EnsureAcquisitionOrder();
        return item;
    }

    public static bool IsGold(ItemData item)
    {
        return item != null
            && item.stackCount > 0
            && item.baseData is CurrencyItemData currencyData
            && currencyData.currencyType == CurrencyType.Gold;
    }

    public static bool IsValidOfferSource(ItemData current, MerchantTradeOffer offer)
    {
        return current != null
            && offer != null
            && offer.SourceItem != null
            && offer.Item != null
            && offer.StackCount > 0
            && current.IsSameRuntimeItem(offer.SourceItem)
            && current.stackCount >= offer.StackCount;
    }

    public static int FindFirstEmptySlot(List<ItemData> items)
    {
        if (items == null)
            return -1;

        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] == null)
                return i;
        }

        return -1;
    }

    public static int GetMaxStack(ItemData item)
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

    public static bool IsStackableItem(ItemData item)
    {
        if (item == null)
            return false;

        if (item.baseData is ConsumableItemData consumableData && consumableData.IsPermanentSingleItem)
            return false;

        string itemType = item.itemType;
        return itemType == "Consumable" || itemType == "Junk" || itemType == "QuestItem" || itemType == "Currency";
    }

    public static bool CanStackItems(ItemData source, ItemData target)
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
}
