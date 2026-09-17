public static class GoldSummaryTextFormatter
{
    private const string NegativeColor = "#E66A6A";
    private const string PositiveColor = "#F0C84B";

    public static string FormatPlayerGold(int inventoryGold, int stashGold, bool includeStash)
    {
        if (includeStash)
            return "보유 Gold : 인벤 " + inventoryGold + "G / 창고 " + stashGold + "G";

        return "보유 Gold : 인벤 " + inventoryGold + "G";
    }

    public static string FormatMerchantGold(int merchantGold)
    {
        return "상인 보유 Gold : " + merchantGold + "G";
    }

    public static string FormatTradeGoldDelta(int playerPaymentGold, int merchantPayoutGold)
    {
        if (playerPaymentGold > 0)
            return "<color=" + NegativeColor + ">Gold : -" + playerPaymentGold + "G</color>";

        if (merchantPayoutGold > 0)
            return "<color=" + PositiveColor + ">Gold : +" + merchantPayoutGold + "G</color>";

        return "Gold : 0G";
    }
}
