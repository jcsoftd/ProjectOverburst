using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerAccountInventoryService : MonoBehaviour
{
    private static PlayerAccountInventoryService instance;

    [Header("Shared Account Storage")]
    [SerializeField] private PlayerInventory sharedInventory;
    [SerializeField] private PlayerStash sharedStash;
    [SerializeField] private StashCurrencyService stashCurrencyService;

    public static PlayerAccountInventoryService Instance => ResolveInstance();
    public static PlayerInventory SharedInventory => FindSharedInventory();
    public static PlayerStash SharedStash => FindSharedStash();
    public static StashCurrencyService SharedCurrencyService => FindSharedCurrencyService();

    public PlayerInventory Inventory
    {
        get
        {
            ResolveReferences();
            return sharedInventory;
        }
    }

    public PlayerStash Stash
    {
        get
        {
            ResolveReferences();
            return sharedStash;
        }
    }

    public StashCurrencyService CurrencyService
    {
        get
        {
            ResolveReferences();
            return stashCurrencyService;
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
            return;

        instance = this;
        ResolveReferences();
    }

    private void OnEnable()
    {
        if (instance == null)
            instance = this;

        ResolveReferences();
    }

    public void Bind(PlayerInventory inventory, PlayerStash stash, StashCurrencyService currencyService)
    {
        sharedInventory = inventory;
        sharedStash = stash;
        stashCurrencyService = currencyService;
    }

    public static PlayerInventory FindSharedInventory()
    {
        PlayerAccountInventoryService service = ResolveInstance();
        if (service != null && service.Inventory != null)
            return service.Inventory;

        return Object.FindFirstObjectByType<PlayerInventory>(FindObjectsInactive.Include);
    }

    public static PlayerStash FindSharedStash()
    {
        PlayerAccountInventoryService service = ResolveInstance();
        if (service != null && service.Stash != null)
            return service.Stash;

        return Object.FindFirstObjectByType<PlayerStash>(FindObjectsInactive.Include);
    }

    public static StashCurrencyService FindSharedCurrencyService()
    {
        PlayerAccountInventoryService service = ResolveInstance();
        if (service != null && service.CurrencyService != null)
            return service.CurrencyService;

        return Object.FindFirstObjectByType<StashCurrencyService>(FindObjectsInactive.Include);
    }

    private static PlayerAccountInventoryService ResolveInstance()
    {
        if (instance != null)
            return instance;

        instance = Object.FindFirstObjectByType<PlayerAccountInventoryService>(FindObjectsInactive.Include);
        if (instance != null)
            instance.ResolveReferences();

        return instance;
    }

    private void ResolveReferences()
    {
        if (sharedInventory == null)
            sharedInventory = GetComponent<PlayerInventory>() ?? Object.FindFirstObjectByType<PlayerInventory>(FindObjectsInactive.Include);

        if (sharedStash == null)
            sharedStash = GetComponent<PlayerStash>() ?? Object.FindFirstObjectByType<PlayerStash>(FindObjectsInactive.Include);

        if (stashCurrencyService == null)
            stashCurrencyService = GetComponent<StashCurrencyService>() ?? Object.FindFirstObjectByType<StashCurrencyService>(FindObjectsInactive.Include);
    }
}
