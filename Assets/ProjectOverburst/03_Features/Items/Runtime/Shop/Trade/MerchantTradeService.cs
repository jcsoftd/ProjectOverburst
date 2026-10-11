using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class MerchantTradeService : MonoBehaviour
{
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private StashCurrencyService stashCurrencyService;
    [SerializeField] private MerchantTradeValueCalculator valueCalculator;

    private MerchantInventory merchantInventory = new MerchantInventory();
    private readonly MerchantTradeSession session = new MerchantTradeSession();
    private readonly MerchantTradeValidator tradeValidator = new MerchantTradeValidator();
    private readonly MerchantTradeCommitter tradeCommitter = new MerchantTradeCommitter();
    private MerchantDefinition currentMerchant;
    private string quotedPriceContext;

    public MerchantDefinition CurrentMerchant { get { return currentMerchant; } }
    public MerchantInventory MerchantInventory { get { return merchantInventory; } }
    public MerchantTradeSession Session { get { return session; } }
    public bool HasStashCurrencySource
    {
        get
        {
            ResolveReferences();
            return stashCurrencyService != null;
        }
    }

    public PlayerInventory PlayerInventory
    {
        get
        {
            ResolveReferences();
            return playerInventory;
        }
    }

    private void Awake()
    {
        ResolveReferences();
    }

    public void Open(MerchantDefinition merchantDefinition)
    {
        ResolveReferences();
        if (merchantDefinition != null && currentMerchant != merchantDefinition)
        {
            currentMerchant = merchantDefinition;
            MerchantInventory stockInventory = MerchantStockRefreshService.GetOrCreateInventory(currentMerchant);
            if (stockInventory != null)
                merchantInventory = stockInventory;
            else
                merchantInventory.Initialize(currentMerchant);
        }

        session.Clear(); quotedPriceContext = null;
    }

    public void ReloadCurrentMerchantInventory()
    {
        if (currentMerchant == null)
            return;

        MerchantInventory stockInventory = MerchantStockRefreshService.GetOrCreateInventory(currentMerchant);
        if (stockInventory != null)
            merchantInventory = stockInventory;

        session.Clear();
    }

    public void Close()
    {
        session.Clear();
    }

    public bool ToggleMerchantOffer(int sourceSlotIndex, out string message)
    {
        bool changed = session.ToggleMerchantOffer(merchantInventory, sourceSlotIndex, out message);
        if (changed) try { CapturePresentedPrice(); } catch (OverflowException) { quotedPriceContext = null; }
        return changed;
    }

    public bool ToggleMerchantOffer(int sourceSlotIndex, int requestedStackCount, out string message)
    {
        bool changed = session.ToggleMerchantOffer(merchantInventory, sourceSlotIndex, requestedStackCount, out message);
        if (changed) try { CapturePresentedPrice(); } catch (OverflowException) { quotedPriceContext = null; }
        return changed;
    }

    public bool TogglePlayerOffer(int sourceSlotIndex, out string message)
    {
        ResolveReferences();
        bool changed = session.TogglePlayerOffer(playerInventory, sourceSlotIndex, out message);
        if (changed) try { CapturePresentedPrice(); } catch (OverflowException) { quotedPriceContext = null; }
        return changed;
    }

    public bool TogglePlayerOffer(int sourceSlotIndex, int requestedStackCount, out string message)
    {
        ResolveReferences();
        bool changed = session.TogglePlayerOffer(playerInventory, sourceSlotIndex, requestedStackCount, out message);
        if (changed) try { CapturePresentedPrice(); } catch (OverflowException) { quotedPriceContext = null; }
        return changed;
    }

    public bool RemoveOffer(MerchantTradeOfferSide side, int offerIndex)
    {
        bool changed = session.RemoveOffer(side, offerIndex);
        if (changed) try { CapturePresentedPrice(); } catch (OverflowException) { quotedPriceContext = null; }
        return changed;
    }

    public int GetMerchantOfferValue()
    {
        return GetOfferValue(session.MerchantOffers, true);
    }

    public int GetPlayerOfferValue()
    {
        return GetOfferValue(session.PlayerOffers, false);
    }

    public int GetAutoGoldCost()
    {
        return Mathf.Max(0, GetMerchantOfferValue() - GetPlayerOfferValue());
    }

    public int GetMerchantGoldPayout()
    {
        return Mathf.Max(0, GetPlayerOfferValue() - GetMerchantOfferValue());
    }

    public int GetMerchantGoldAmount()
    {
        return merchantInventory.GetCurrencyAmount(CurrencyType.Gold);
    }

    public int GetInventoryGoldAmount()
    {
        ResolveReferences();
        if (playerInventory == null)
            return 0;

        return MerchantTradeItemUtility.GetInventoryGoldAmount(playerInventory);
    }

    public int GetStashGoldAmount()
    {
        ResolveReferences();
        return stashCurrencyService != null ? stashCurrencyService.GetAmount(CurrencyType.Gold) : 0;
    }

    public int GetTotalGoldAmount()
    {
        return checked(GetInventoryGoldAmount() + GetStashGoldAmount());
    }

    public float GetCurrentMerchantDiscountRate()
    {
        ResolveReferences();
        return valueCalculator != null ? valueCalculator.GetBuyDiscountRate(currentMerchant) : 0f;
    }

    public MerchantTradeResult ExecuteTrade()
    {
        ResolveReferences();

        MerchantTradeTransactionPlan plan;
        try
        {
            var context = ValidatePriceContext(); if (!context.success) return context;
            plan = CreateTransactionPlan();
        }
        catch (OverflowException) { return InvalidPrice(); }
        MerchantTradeResult validation;
        try { validation = tradeValidator.Validate(plan, playerInventory, merchantInventory, stashCurrencyService, valueCalculator); }
        catch (OverflowException) { return InvalidPrice(); }
        if (!validation.success)
            return validation;

        validation = tradeValidator.ValidateCurrent(plan, playerInventory, merchantInventory, stashCurrencyService);
        if (!validation.success)
            return validation;

        MerchantTradeResult result = tradeCommitter.Commit(plan, playerInventory, merchantInventory, stashCurrencyService);
        if (result.success)
            session.Clear();

        return result;
    }

    public MerchantTradeResult ValidateTrade()
    {
        ResolveReferences();
        try { return tradeValidator.Validate(CreateTransactionPlan(), playerInventory, merchantInventory, stashCurrencyService, valueCalculator); }
        catch (OverflowException) { return InvalidPrice(); }
    }

    private MerchantTradeTransactionPlan CreateTransactionPlan()
    {
        return MerchantTradeTransactionPlan.Create(
            session.MerchantOffers,
            session.PlayerOffers,
            GetMerchantOfferValue(),
            GetPlayerOfferValue());
    }

    private int GetOfferValue(IReadOnlyList<MerchantTradeOffer> offers, bool merchantOffer)
    {
        ResolveReferences();
        if (valueCalculator == null || offers == null)
            return 0;

        int total = 0;
        for (int i = 0; i < offers.Count; i++)
        {
            MerchantTradeOffer offer = offers[i];
            ItemData item = offer != null ? offer.Item : null;
            total = checked(total + (merchantOffer
                ? valueCalculator.GetBuyValue(item, currentMerchant)
                : valueCalculator.GetSellValue(item, currentMerchant)));
        }

        return total;
    }

    public void CapturePresentedPrice()
    {
        // Capture the price actually presented after an offer edit, not an unrelated account revision.
        quotedPriceContext = session.MerchantOffers.Count + session.PlayerOffers.Count == 0 ? null : PriceContext();
    }
    private MerchantTradeResult ValidatePriceContext()
    {
        string current = PriceContext();
        if (quotedPriceContext != null && current != quotedPriceContext)
        {
            quotedPriceContext = current;
            return MerchantTradeResult.Fail(MerchantTradeFailureReason.PriceChanged, "가격이 변경되었습니다. 갱신된 합계를 확인한 뒤 다시 거래해 주세요.", 0, 0, 0);
        }
        quotedPriceContext = current;
        return MerchantTradeResult.Success(0,0,0);
    }
    private string PriceContext()
    {
        ResolveReferences();
        var value = new StringBuilder(MerchantReputationService.GetStableMerchantId(currentMerchant));
        value.Append('|').Append(MerchantSalePrice.PolicyVersion).Append('|').Append(Mathf.RoundToInt(GetCurrentMerchantDiscountRate()*10000));
        foreach (var offer in session.MerchantOffers)
            value.Append('|').Append(offer?.SourceItem?.runtimeInstanceId).Append(':').Append(offer?.StackCount).Append(':').Append(valueCalculator.GetBuyValue(offer?.Item,currentMerchant));
        value.Append("/player");
        foreach (var offer in session.PlayerOffers)
            value.Append('|').Append(offer?.SourceItem?.runtimeInstanceId).Append(':').Append(offer?.StackCount).Append(':').Append(valueCalculator.GetSellValue(offer?.Item,currentMerchant));
        return value.ToString();
    }
    private static MerchantTradeResult InvalidPrice() => MerchantTradeResult.Fail(MerchantTradeFailureReason.InvalidValue,"거래 금액의 허용 범위를 초과했습니다.",0,0,0);

    private void ResolveReferences()
    {
        if (playerInventory == null)
            playerInventory = FindFirstObjectByType<PlayerInventory>(FindObjectsInactive.Include);

        if (stashCurrencyService == null)
            stashCurrencyService = FindFirstObjectByType<StashCurrencyService>(FindObjectsInactive.Include);

        if (valueCalculator == null)
            valueCalculator = GetComponent<MerchantTradeValueCalculator>() ?? FindFirstObjectByType<MerchantTradeValueCalculator>(FindObjectsInactive.Include);
    }
}
