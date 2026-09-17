using UnityEngine;
using UnityEngine.Serialization;

public class StashCurrencyService : MonoBehaviour
{
    [SerializeField] private PlayerStash stash;
    [SerializeField] private CurrencyItemData goldItem;
    [FormerlySerializedAs("partFragmentItem")]
    [SerializeField] private CurrencyItemData gemPowderItem;
    [SerializeField] private CurrencyItemData mapFragmentItem;

    public PlayerStash Stash
    {
        get
        {
            ResolveReferences();
            return stash;
        }
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
    }

    public int GetAmount(CurrencyType type)
    {
        ResolveReferences();
        if (stash == null)
            return 0;

        int total = 0;
        int tabCount = stash.TabCount;
        int capacity = stash.Capacity;

        for (int tabIndex = 0; tabIndex < tabCount; tabIndex++)
        {
            for (int slotIndex = 0; slotIndex < capacity; slotIndex++)
            {
                ItemData item = stash.GetItemAt(tabIndex, slotIndex);
                if (item == null || item.stackCount <= 0 || !(item.baseData is CurrencyItemData currencyData))
                    continue;

                if (currencyData.currencyType == type)
                    total += item.stackCount;
            }
        }

        return total;
    }

    public bool CanPay(CurrencyAmount cost)
    {
        return !cost.IsValid || GetAmount(cost.type) >= cost.amount;
    }

    public bool TrySpend(CurrencyAmount cost)
    {
        ResolveReferences();
        if (!cost.IsValid)
            return true;

        if (stash == null || GetAmount(cost.type) < cost.amount)
            return false;

        int remaining = cost.amount;
        int tabCount = stash.TabCount;
        int capacity = stash.Capacity;

        for (int tabIndex = 0; tabIndex < tabCount && remaining > 0; tabIndex++)
        {
            for (int slotIndex = 0; slotIndex < capacity && remaining > 0; slotIndex++)
            {
                ItemData item = stash.GetItemAt(tabIndex, slotIndex);
                if (item == null || item.stackCount <= 0 || !IsCurrency(item, cost.type))
                    continue;

                int spendCount = Mathf.Min(item.stackCount, remaining);
                item.stackCount -= spendCount;
                remaining -= spendCount;

                if (item.stackCount <= 0)
                    stash.ClearSlot(tabIndex, slotIndex, false);
            }
        }

        stash.NotifyChanged();
        return true;
    }

    public bool TryAddCurrency(CurrencyType type, int amount)
    {
        ResolveReferences();
        if (stash == null || amount <= 0)
            return false;

        CurrencyItemData currencyData = GetCurrencyData(type);
        if (currencyData == null)
            return false;

        if (!CanAddCurrency(currencyData, amount))
            return false;

        int remaining = amount;
        int maxStack = Mathf.Max(1, currencyData.maxStack);
        int tabCount = stash.TabCount;
        int capacity = stash.Capacity;

        for (int tabIndex = 0; tabIndex < tabCount && remaining > 0; tabIndex++)
        {
            for (int slotIndex = 0; slotIndex < capacity && remaining > 0; slotIndex++)
            {
                ItemData item = stash.GetItemAt(tabIndex, slotIndex);
                if (item == null || !IsCurrency(item, type) || item.stackCount >= maxStack)
                    continue;

                int addCount = Mathf.Min(maxStack - item.stackCount, remaining);
                item.stackCount += addCount;
                remaining -= addCount;
            }
        }

        for (int tabIndex = 0; tabIndex < tabCount && remaining > 0; tabIndex++)
        {
            for (int slotIndex = 0; slotIndex < capacity && remaining > 0; slotIndex++)
            {
                if (stash.GetItemAt(tabIndex, slotIndex) != null)
                    continue;

                int stackCount = Mathf.Min(maxStack, remaining);
                ItemData stack = new ItemData(currencyData, 1, ItemGrade.Common, stackCount);
                stack.EnsureRuntimeState();
                stack.EnsureAcquisitionOrder();

                if (!stash.SetItemAt(tabIndex, slotIndex, stack, false))
                    return false;

                remaining -= stackCount;
            }
        }

        stash.NotifyChanged();
        return remaining <= 0;
    }

    public CurrencyItemData GetCurrencyData(CurrencyType type)
    {
        ResolveCurrencyAssets();

        switch (type)
        {
            case CurrencyType.Gold:
                return goldItem;
            case CurrencyType.GemPowder:
                return gemPowderItem;
            case CurrencyType.MapFragment:
                return mapFragmentItem;
            default:
                return null;
        }
    }

    private bool CanAddCurrency(CurrencyItemData currencyData, int amount)
    {
        int remaining = amount;
        int maxStack = Mathf.Max(1, currencyData.maxStack);
        int tabCount = stash.TabCount;
        int capacity = stash.Capacity;

        for (int tabIndex = 0; tabIndex < tabCount && remaining > 0; tabIndex++)
        {
            for (int slotIndex = 0; slotIndex < capacity && remaining > 0; slotIndex++)
            {
                ItemData item = stash.GetItemAt(tabIndex, slotIndex);
                if (item == null || !IsCurrency(item, currencyData.currencyType))
                    continue;

                remaining -= Mathf.Max(0, maxStack - item.stackCount);
            }
        }

        for (int tabIndex = 0; tabIndex < tabCount && remaining > 0; tabIndex++)
        {
            for (int slotIndex = 0; slotIndex < capacity && remaining > 0; slotIndex++)
            {
                if (stash.GetItemAt(tabIndex, slotIndex) == null)
                    remaining -= maxStack;
            }
        }

        return remaining <= 0;
    }

    private bool IsCurrency(ItemData item, CurrencyType type)
    {
        return item != null
            && item.baseData is CurrencyItemData currencyData
            && currencyData.currencyType == type;
    }

    private void ResolveReferences()
    {
        if (stash == null)
            stash = GetComponent<PlayerStash>() ?? FindFirstObjectByType<PlayerStash>();

        ResolveCurrencyAssets();
    }

    private void ResolveCurrencyAssets()
    {
        if (goldItem == null)
            goldItem = CurrencyItemRegistry.Get(CurrencyType.Gold);

        if (gemPowderItem == null)
            gemPowderItem = CurrencyItemRegistry.Get(CurrencyType.GemPowder);

        if (mapFragmentItem == null)
            mapFragmentItem = CurrencyItemRegistry.Get(CurrencyType.MapFragment);
    }
}
