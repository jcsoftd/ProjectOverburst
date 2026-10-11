using UnityEngine;

public class MerchantTradeValueCalculator : MonoBehaviour
{
    [SerializeField] private int smallHealPotionValue = 20;
    [SerializeField] private int moveSpeedPotionValue = 30;

    public int GetValue(ItemData item)
    {
        return GetStackValue(item, null, false);
    }

    public int GetBaseValue(ItemData item)
    {
        if (item == null || !item.HasValidBaseData || item.stackCount <= 0)
            return 0;

        return checked(Mathf.Max(0, item.baseData is ElementGemItemData ? ElementGemLootPolicy.Value(item) : GetUnitValue(item.baseData)) * item.stackCount);
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
        return MerchantReputationService.GetDiscountRate(merchant);
    }

    private int GetStackValue(ItemData item, MerchantDefinition merchant, bool isBuyPrice)
    {
        if (item == null || !item.HasValidBaseData || item.stackCount <= 0)
            return 0;

        int unitValue = item.baseData is ElementGemItemData ? ElementGemLootPolicy.Value(item) : GetUnitValue(item.baseData);
        if (item.baseData is CurrencyItemData)
            return checked(Mathf.Max(0, unitValue) * item.stackCount);

        int pricedUnitValue = Mathf.Max(0, unitValue);
        if (isBuyPrice && unitValue > 0)
        {
            float discountRate = GetBuyDiscountRate(merchant);
            pricedUnitValue = MerchantSalePrice.Unit(unitValue, Mathf.RoundToInt(discountRate * 10000));
        }
        return checked(Mathf.Max(0, pricedUnitValue) * item.stackCount);
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
}
