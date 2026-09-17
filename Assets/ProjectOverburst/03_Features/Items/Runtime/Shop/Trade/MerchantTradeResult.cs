public enum MerchantTradeFailureReason
{
    None,
    MissingService,
    EmptyTrade,
    InvalidSource,
    InventoryFull,
    MerchantInventoryFull,
    NotEnoughGold,
    MerchantNotEnoughGold,
    GoldReceiveInventoryFull,
    PaymentFailed,
    TransferFailed
}

public struct MerchantTradeResult
{
    public readonly bool success;
    public readonly MerchantTradeFailureReason failureReason;
    public readonly string message;
    public readonly int merchantValue;
    public readonly int playerValue;
    public readonly int autoGoldCost;
    public readonly int merchantGoldPayout;

    private MerchantTradeResult(bool success, MerchantTradeFailureReason failureReason, string message, int merchantValue, int playerValue, int autoGoldCost, int merchantGoldPayout)
    {
        this.success = success;
        this.failureReason = failureReason;
        this.message = message;
        this.merchantValue = merchantValue;
        this.playerValue = playerValue;
        this.autoGoldCost = autoGoldCost;
        this.merchantGoldPayout = merchantGoldPayout;
    }

    public static MerchantTradeResult Success(int merchantValue, int playerValue, int autoGoldCost)
    {
        return Success(merchantValue, playerValue, autoGoldCost, 0);
    }

    public static MerchantTradeResult Success(int merchantValue, int playerValue, int autoGoldCost, int merchantGoldPayout)
    {
        return new MerchantTradeResult(true, MerchantTradeFailureReason.None, "거래 완료", merchantValue, playerValue, autoGoldCost, merchantGoldPayout);
    }

    public static MerchantTradeResult Fail(MerchantTradeFailureReason reason, string message, int merchantValue, int playerValue, int autoGoldCost)
    {
        return Fail(reason, message, merchantValue, playerValue, autoGoldCost, 0);
    }

    public static MerchantTradeResult Fail(MerchantTradeFailureReason reason, string message, int merchantValue, int playerValue, int autoGoldCost, int merchantGoldPayout)
    {
        return new MerchantTradeResult(false, reason, message, merchantValue, playerValue, autoGoldCost, merchantGoldPayout);
    }
}
