using System.Collections.Generic;

public sealed class MerchantTradePriceContext
{
    private readonly MerchantTradeValueCalculator valueCalculator;
    private readonly MerchantDefinition merchant;

    public MerchantTradePriceContext(MerchantTradeValueCalculator valueCalculator, MerchantDefinition merchant)
    {
        this.valueCalculator = valueCalculator;
        this.merchant = merchant;
    }

    public int GetOfferValue(IReadOnlyList<MerchantTradeOffer> offers, bool merchantOffer)
    {
        if (valueCalculator == null || offers == null)
            return 0;

        int total = 0;
        for (int i = 0; i < offers.Count; i++)
        {
            MerchantTradeOffer offer = offers[i];
            ItemData item = offer != null ? offer.Item : null;
            total += merchantOffer
                ? valueCalculator.GetBuyValue(item, merchant)
                : valueCalculator.GetSellValue(item, merchant);
        }

        return total;
    }
}
