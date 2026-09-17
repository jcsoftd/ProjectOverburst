using System.Collections.Generic;
using UnityEngine;

public enum MerchantTradeOfferSide
{
    Merchant,
    Player
}

[System.Serializable]
public class MerchantTradeOffer
{
    [SerializeField] private MerchantTradeOfferSide side;
    [SerializeField] private int sourceSlotIndex;
    [SerializeField] private ItemData sourceItem;
    [SerializeField] private ItemData item;

    public MerchantTradeOfferSide Side { get { return side; } }
    public int SourceSlotIndex { get { return sourceSlotIndex; } }
    public ItemData SourceItem { get { return sourceItem != null ? sourceItem : item; } }
    public ItemData Item { get { return item; } }
    public int StackCount { get { return item != null ? Mathf.Max(1, item.stackCount) : 0; } }
    public bool IsPartialStackOffer { get { return SourceItem != null && item != null && !SourceItem.IsSameRuntimeItem(item); } }

    public MerchantTradeOffer(MerchantTradeOfferSide side, int sourceSlotIndex, ItemData item)
        : this(side, sourceSlotIndex, item, item)
    {
    }

    public MerchantTradeOffer(MerchantTradeOfferSide side, int sourceSlotIndex, ItemData sourceItem, ItemData item)
    {
        this.side = side;
        this.sourceSlotIndex = sourceSlotIndex;
        this.sourceItem = sourceItem;
        this.item = item;
    }
}

[System.Serializable]
public class MerchantTradeSession
{
    [SerializeField] private int maxOffersPerSide = 21;

    private readonly List<MerchantTradeOffer> merchantOffers = new List<MerchantTradeOffer>();
    private readonly List<MerchantTradeOffer> playerOffers = new List<MerchantTradeOffer>();

    public IReadOnlyList<MerchantTradeOffer> MerchantOffers { get { return merchantOffers; } }
    public IReadOnlyList<MerchantTradeOffer> PlayerOffers { get { return playerOffers; } }
    public int MaxOffersPerSide { get { return Mathf.Max(1, maxOffersPerSide); } }

    public void Clear()
    {
        merchantOffers.Clear();
        playerOffers.Clear();
    }

    public bool ToggleMerchantOffer(MerchantInventory inventory, int sourceSlotIndex, out string message)
    {
        return ToggleMerchantOffer(inventory, sourceSlotIndex, 0, out message);
    }

    public bool ToggleMerchantOffer(MerchantInventory inventory, int sourceSlotIndex, int requestedStackCount, out string message)
    {
        message = string.Empty;
        ItemData item = inventory != null ? inventory.GetItemAt(sourceSlotIndex) : null;
        if (item == null)
        {
            message = "상인 슬롯이 비어 있습니다.";
            return false;
        }

        if (item.baseData is CurrencyItemData)
        {
            message = "재화 아이템은 거래창에 직접 올릴 수 없습니다.";
            return false;
        }

        return ToggleOffer(merchantOffers, MerchantTradeOfferSide.Merchant, sourceSlotIndex, item, requestedStackCount, "상인 거래창", out message);
    }

    public bool TogglePlayerOffer(PlayerInventory inventory, int sourceSlotIndex, out string message)
    {
        return TogglePlayerOffer(inventory, sourceSlotIndex, 0, out message);
    }

    public bool TogglePlayerOffer(PlayerInventory inventory, int sourceSlotIndex, int requestedStackCount, out string message)
    {
        message = string.Empty;
        ItemData item = inventory != null ? inventory.GetItemAt(sourceSlotIndex) : null;
        if (item == null)
        {
            message = "인벤토리 슬롯이 비어 있습니다.";
            return false;
        }

        if (item.baseData is CurrencyItemData currencyData)
        {
            message = currencyData.currencyType == CurrencyType.Gold
                ? "Gold는 거래창에 올리지 않고 부족분 자동 결제로 사용합니다."
                : "재화 아이템은 1차 거래창에 올릴 수 없습니다.";
            return false;
        }

        return ToggleOffer(playerOffers, MerchantTradeOfferSide.Player, sourceSlotIndex, item, requestedStackCount, "플레이어 거래창", out message);
    }

    public bool RemoveOffer(MerchantTradeOfferSide side, int offerIndex)
    {
        List<MerchantTradeOffer> offers = side == MerchantTradeOfferSide.Merchant ? merchantOffers : playerOffers;
        if (offerIndex < 0 || offerIndex >= offers.Count)
            return false;

        offers.RemoveAt(offerIndex);
        return true;
    }

    public bool ContainsSource(MerchantTradeOfferSide side, int sourceSlotIndex)
    {
        List<MerchantTradeOffer> offers = side == MerchantTradeOfferSide.Merchant ? merchantOffers : playerOffers;
        return FindOfferIndex(offers, sourceSlotIndex) >= 0;
    }

    public List<int> GetSourceIndices(MerchantTradeOfferSide side)
    {
        List<MerchantTradeOffer> offers = side == MerchantTradeOfferSide.Merchant ? merchantOffers : playerOffers;
        List<int> indices = new List<int>(offers.Count);
        for (int i = 0; i < offers.Count; i++)
            indices.Add(offers[i].SourceSlotIndex);

        return indices;
    }

    public List<ItemData> GetItems(MerchantTradeOfferSide side)
    {
        List<MerchantTradeOffer> offers = side == MerchantTradeOfferSide.Merchant ? merchantOffers : playerOffers;
        List<ItemData> result = new List<ItemData>(offers.Count);
        for (int i = 0; i < offers.Count; i++)
            result.Add(offers[i].Item);

        return result;
    }

    private bool ToggleOffer(List<MerchantTradeOffer> offers, MerchantTradeOfferSide side, int sourceSlotIndex, ItemData sourceItem, int requestedStackCount, string label, out string message)
    {
        bool partialOffer = IsPartialStackRequest(sourceItem, requestedStackCount);
        int existingIndex = FindOfferIndex(offers, sourceSlotIndex);
        if (existingIndex >= 0)
        {
            if (partialOffer)
            {
                offers[existingIndex] = new MerchantTradeOffer(side, sourceSlotIndex, sourceItem, CreateOfferItem(sourceItem, requestedStackCount));
                message = label + " 수량을 " + requestedStackCount + "개로 변경했습니다.";
                return true;
            }

            offers.RemoveAt(existingIndex);
            message = label + "에서 제거했습니다.";
            return true;
        }

        if (offers.Count >= MaxOffersPerSide)
        {
            message = label + "이 가득 찼습니다.";
            return false;
        }

        ItemData offerItem = partialOffer ? CreateOfferItem(sourceItem, requestedStackCount) : sourceItem;
        offers.Add(new MerchantTradeOffer(side, sourceSlotIndex, sourceItem, offerItem));
        message = partialOffer
            ? label + "에 " + requestedStackCount + "개를 올렸습니다."
            : label + "에 올렸습니다.";
        return true;
    }

    private bool IsPartialStackRequest(ItemData sourceItem, int requestedStackCount)
    {
        return sourceItem != null
            && requestedStackCount > 0
            && requestedStackCount < sourceItem.stackCount
            && IsStackableItem(sourceItem);
    }

    private ItemData CreateOfferItem(ItemData sourceItem, int stackCount)
    {
        ItemData offerItem = new ItemData(sourceItem.baseData, sourceItem.level, sourceItem.grade, Mathf.Max(1, stackCount));
        offerItem.acquisitionOrder = sourceItem.acquisitionOrder;
        offerItem.EnsureRuntimeState();
        return offerItem;
    }

    private bool IsStackableItem(ItemData item)
    {
        if (item == null)
            return false;

        if (item.baseData is ConsumableItemData consumableData && consumableData.IsPermanentSingleItem)
            return false;

        string itemType = item.itemType;
        return itemType == "Consumable" || itemType == "Junk" || itemType == "QuestItem";
    }

    private int FindOfferIndex(List<MerchantTradeOffer> offers, int sourceSlotIndex)
    {
        for (int i = 0; i < offers.Count; i++)
        {
            if (offers[i].SourceSlotIndex == sourceSlotIndex)
                return i;
        }

        return -1;
    }
}
