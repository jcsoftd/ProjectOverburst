using UnityEngine;

public sealed class ReputationDiscountPriceStrategy : DefaultMerchantPriceStrategy
{
    public override int CalculateBuyPrice(int basePrice, MerchantDefinition merchant)
    {
        if (basePrice <= 0)
            return 0;

        float discountRate = GetBuyDiscountRate(merchant);
        int discounted = Mathf.CeilToInt(basePrice * (1f - discountRate));
        return Mathf.Max(1, discounted);
    }

    public override float GetBuyDiscountRate(MerchantDefinition merchant)
    {
        return MerchantReputationService.GetDiscountRate(merchant);
    }
}
