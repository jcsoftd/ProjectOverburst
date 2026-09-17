using System.Collections.Generic;

public sealed class MerchantTradeItemMove
{
    public readonly MerchantTradeOffer Offer;
    public readonly MerchantTradeOfferSide FromSide;
    public readonly MerchantTradeOfferSide ToSide;
    public readonly int SourceSlotIndex;
    public readonly ItemData SourceItem;
    public readonly ItemData Item;
    public readonly int StackCount;

    public MerchantTradeItemMove(MerchantTradeOffer offer, MerchantTradeOfferSide fromSide, MerchantTradeOfferSide toSide)
    {
        Offer = offer;
        FromSide = fromSide;
        ToSide = toSide;
        SourceSlotIndex = offer != null ? offer.SourceSlotIndex : -1;
        SourceItem = offer != null ? offer.SourceItem : null;
        Item = offer != null ? offer.Item : null;
        StackCount = offer != null ? offer.StackCount : 0;
    }
}

public readonly struct MerchantTradeGoldMove
{
    public readonly MerchantTradeOfferSide FromSide;
    public readonly MerchantTradeOfferSide ToSide;
    public readonly CurrencyType CurrencyType;
    public readonly int Amount;

    public bool HasValue { get { return Amount > 0; } }

    public MerchantTradeGoldMove(MerchantTradeOfferSide fromSide, MerchantTradeOfferSide toSide, CurrencyType currencyType, int amount)
    {
        FromSide = fromSide;
        ToSide = toSide;
        CurrencyType = currencyType;
        Amount = amount;
    }
}

public sealed class MerchantTradeTransactionPlan
{
    private readonly List<MerchantTradeOffer> merchantOffers = new List<MerchantTradeOffer>();
    private readonly List<MerchantTradeOffer> playerOffers = new List<MerchantTradeOffer>();
    private readonly List<MerchantTradeItemMove> merchantToPlayerMoves = new List<MerchantTradeItemMove>();
    private readonly List<MerchantTradeItemMove> playerToMerchantMoves = new List<MerchantTradeItemMove>();
    private readonly List<ItemData> merchantIncomingItems = new List<ItemData>();
    private readonly List<ItemData> playerIncomingItems = new List<ItemData>();

    public int MerchantValue { get; private set; }
    public int PlayerValue { get; private set; }
    public int PlayerPaymentGold { get; private set; }
    public int MerchantPayoutGold { get; private set; }
    public MerchantTradeGoldMove PlayerGoldPayment { get; private set; }
    public MerchantTradeGoldMove MerchantGoldPayout { get; private set; }

    public IReadOnlyList<MerchantTradeOffer> MerchantOffers { get { return merchantOffers; } }
    public IReadOnlyList<MerchantTradeOffer> PlayerOffers { get { return playerOffers; } }
    public IReadOnlyList<MerchantTradeItemMove> MerchantToPlayerMoves { get { return merchantToPlayerMoves; } }
    public IReadOnlyList<MerchantTradeItemMove> PlayerToMerchantMoves { get { return playerToMerchantMoves; } }
    public IList<ItemData> MerchantIncomingItems { get { return merchantIncomingItems; } }
    public IList<ItemData> PlayerIncomingItems { get { return playerIncomingItems; } }
    public bool IsEmpty { get { return merchantOffers.Count == 0 && playerOffers.Count == 0; } }

    public static MerchantTradeTransactionPlan Create(
        IReadOnlyList<MerchantTradeOffer> sourceMerchantOffers,
        IReadOnlyList<MerchantTradeOffer> sourcePlayerOffers,
        int merchantValue,
        int playerValue)
    {
        MerchantTradeTransactionPlan plan = new MerchantTradeTransactionPlan();
        plan.MerchantValue = merchantValue;
        plan.PlayerValue = playerValue;
        plan.PlayerPaymentGold = UnityEngine.Mathf.Max(0, merchantValue - playerValue);
        plan.MerchantPayoutGold = UnityEngine.Mathf.Max(0, playerValue - merchantValue);
        plan.PlayerGoldPayment = new MerchantTradeGoldMove(MerchantTradeOfferSide.Player, MerchantTradeOfferSide.Merchant, CurrencyType.Gold, plan.PlayerPaymentGold);
        plan.MerchantGoldPayout = new MerchantTradeGoldMove(MerchantTradeOfferSide.Merchant, MerchantTradeOfferSide.Player, CurrencyType.Gold, plan.MerchantPayoutGold);

        CopyOffers(sourceMerchantOffers, plan.merchantOffers);
        CopyOffers(sourcePlayerOffers, plan.playerOffers);
        plan.BuildMoves();
        return plan;
    }

    public MerchantTradeResult ToSuccessResult()
    {
        return MerchantTradeResult.Success(MerchantValue, PlayerValue, PlayerPaymentGold, MerchantPayoutGold);
    }

    public MerchantTradeResult ToFailureResult(MerchantTradeFailureReason reason, string message)
    {
        return MerchantTradeResult.Fail(reason, message, MerchantValue, PlayerValue, PlayerPaymentGold, MerchantPayoutGold);
    }

    private void BuildMoves()
    {
        for (int i = 0; i < merchantOffers.Count; i++)
        {
            MerchantTradeItemMove move = new MerchantTradeItemMove(merchantOffers[i], MerchantTradeOfferSide.Merchant, MerchantTradeOfferSide.Player);
            merchantToPlayerMoves.Add(move);
            playerIncomingItems.Add(move.Item);
        }

        for (int i = 0; i < playerOffers.Count; i++)
        {
            MerchantTradeItemMove move = new MerchantTradeItemMove(playerOffers[i], MerchantTradeOfferSide.Player, MerchantTradeOfferSide.Merchant);
            playerToMerchantMoves.Add(move);
            merchantIncomingItems.Add(move.Item);
        }
    }

    private static void CopyOffers(IReadOnlyList<MerchantTradeOffer> source, List<MerchantTradeOffer> target)
    {
        if (source == null)
            return;

        for (int i = 0; i < source.Count; i++)
            target.Add(source[i]);
    }
}
