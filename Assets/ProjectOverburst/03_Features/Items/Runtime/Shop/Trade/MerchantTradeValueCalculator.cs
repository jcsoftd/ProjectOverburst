using UnityEngine;

public class MerchantTradeValueCalculator : MonoBehaviour
{
    [SerializeField] private int smallHealPotionValue = 20;
    [SerializeField] private int moveSpeedPotionValue = 30;

    private IMerchantPriceStrategy priceStrategy;

    private void Awake()
    {
        priceStrategy = new ReputationDiscountPriceStrategy();
    }

    public int GetValue(ItemData item)
    {
        return GetStackValue(item, null, false);
    }

    public int GetBaseValue(ItemData item)
    {
        if (item == null || !item.HasValidBaseData || item.stackCount <= 0)
            return 0;

        return Mathf.Max(0, GetUnitValue(item.baseData)) * Mathf.Max(1, item.stackCount);
    }

    public int GetBuyValue(ItemData item, MerchantDefinition merchant)
    {
        return GetStackValue(item, merchant, true);
    }

    public int GetSellValue(ItemData item, MerchantDefinition merchant)
    {
        return GetStackValue(item, merchant, false);
    }

    public float GetBuyDiscountRate(MerchantDefinition merchant)
    {
        EnsurePriceStrategy();
        return priceStrategy != null ? priceStrategy.GetBuyDiscountRate(merchant) : 0f;
    }

    private int GetStackValue(ItemData item, MerchantDefinition merchant, bool isBuyPrice)
    {
        if (item == null || !item.HasValidBaseData || item.stackCount <= 0)
            return 0;

        int unitValue = GetUnitValue(item.baseData);
        if (item.baseData is CurrencyItemData)
            return Mathf.Max(0, unitValue) * Mathf.Max(1, item.stackCount);

        EnsurePriceStrategy();
        int pricedUnitValue = isBuyPrice && priceStrategy != null
            ? priceStrategy.CalculateBuyPrice(unitValue, merchant)
            : priceStrategy != null ? priceStrategy.CalculateSellPrice(unitValue, merchant) : Mathf.Max(0, unitValue);
        return Mathf.Max(0, pricedUnitValue) * Mathf.Max(1, item.stackCount);
    }

    public int GetUnitValue(BaseItemData itemData)
    {
        if (itemData == null)
            return 0;

        if (itemData is CurrencyItemData currencyData && currencyData.currencyType == CurrencyType.Gold)
            return 1;

        string assetName = itemData.name;
        if (assetName == "SmallHealPotion" || itemData.itemName == "회복물약(소)")
            return smallHealPotionValue;

        if (assetName == "MoveSpeedPotion" || itemData.itemName == "이동속도 증가 물약")
            return moveSpeedPotionValue;

        if (itemData is ConsumableItemData)
            return Mathf.Max(0, itemData.sellPrice);

        return Mathf.Max(0, itemData.sellPrice);
    }

    private void EnsurePriceStrategy()
    {
        if (priceStrategy == null)
            priceStrategy = new ReputationDiscountPriceStrategy();
    }
}
