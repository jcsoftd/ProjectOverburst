public interface IMerchantPriceStrategy
{
    int CalculateBuyPrice(int basePrice, MerchantDefinition merchant);
    int CalculateSellPrice(int basePrice, MerchantDefinition merchant);
    float GetBuyDiscountRate(MerchantDefinition merchant);
}
