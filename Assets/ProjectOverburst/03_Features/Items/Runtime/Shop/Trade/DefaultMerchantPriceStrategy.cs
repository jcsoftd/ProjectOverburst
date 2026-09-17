using UnityEngine;

public class DefaultMerchantPriceStrategy : IMerchantPriceStrategy
{
    public virtual int CalculateBuyPrice(int basePrice, MerchantDefinition merchant)
    {
        return Mathf.Max(0, basePrice);
    }

    public virtual int CalculateSellPrice(int basePrice, MerchantDefinition merchant)
    {
        return Mathf.Max(0, basePrice);
    }

    public virtual float GetBuyDiscountRate(MerchantDefinition merchant)
    {
        return 0f;
    }
}
