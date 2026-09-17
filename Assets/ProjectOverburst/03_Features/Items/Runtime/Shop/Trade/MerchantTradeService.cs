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

        session.Clear();
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
        return session.ToggleMerchantOffer(merchantInventory, sourceSlotIndex, out message);
    }

    public bool ToggleMerchantOffer(int sourceSlotIndex, int requestedStackCount, out string message)
    {
        return session.ToggleMerchantOffer(merchantInventory, sourceSlotIndex, requestedStackCount, out message);
    }

    public bool TogglePlayerOffer(int sourceSlotIndex, out string message)
    {
        ResolveReferences();
        return session.TogglePlayerOffer(playerInventory, sourceSlotIndex, out message);
    }

    public bool TogglePlayerOffer(int sourceSlotIndex, int requestedStackCount, out string message)
    {
        ResolveReferences();
        return session.TogglePlayerOffer(playerInventory, sourceSlotIndex, requestedStackCount, out message);
    }

    public bool RemoveOffer(MerchantTradeOfferSide side, int offerIndex)
    {
        return session.RemoveOffer(side, offerIndex);
    }

    public int GetMerchantOfferValue()
    {
        return CreatePriceContext().GetOfferValue(session.MerchantOffers, true);
    }

    public int GetPlayerOfferValue()
    {
        return CreatePriceContext().GetOfferValue(session.PlayerOffers, false);
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
        return GetInventoryGoldAmount() + GetStashGoldAmount();
    }

    public float GetCurrentMerchantDiscountRate()
    {
        ResolveReferences();
        return valueCalculator != null ? valueCalculator.GetBuyDiscountRate(currentMerchant) : 0f;
    }

    public MerchantTradeResult ExecuteTrade()
    {
        ResolveReferences();

        MerchantTradeTransactionPlan plan = CreateTransactionPlan();
        MerchantTradeResult validation = tradeValidator.Validate(plan, playerInventory, merchantInventory, stashCurrencyService, valueCalculator);
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
        return tradeValidator.Validate(CreateTransactionPlan(), playerInventory, merchantInventory, stashCurrencyService, valueCalculator);
    }

    private MerchantTradeTransactionPlan CreateTransactionPlan()
    {
        return MerchantTradeTransactionPlan.Create(
            session.MerchantOffers,
            session.PlayerOffers,
            GetMerchantOfferValue(),
            GetPlayerOfferValue());
    }

    private MerchantTradePriceContext CreatePriceContext()
    {
        ResolveReferences();
        return new MerchantTradePriceContext(valueCalculator, currentMerchant);
    }

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
