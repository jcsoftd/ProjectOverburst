using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerAccountInventoryService : MonoBehaviour
{
    private static PlayerAccountInventoryService instance;
    // One account per process. UI/actor lifetime never owns these selections.
    internal static PlayerAccountLoadout Loadout { get; private set; } = new PlayerAccountLoadout();
    internal static void ReplaceLoadout(PlayerAccountLoadout value)
    {
        Loadout = value ?? throw new System.ArgumentNullException(nameof(value));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetAccountRuntime()
    {
        instance = null;
        Loadout = new PlayerAccountLoadout();
    }

    [Header("Shared Account Storage")]
    [SerializeField] private PlayerInventory sharedInventory;
    [SerializeField] private PlayerStash sharedStash;
    [SerializeField] private StashCurrencyService stashCurrencyService;

    public static PlayerAccountInventoryService Instance => ResolveInstance();
    public static ItemData EquippedBag => Loadout.Bags != null && Loadout.Bags.Length > 0 ? Loadout.Bags[0] : null;
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
    public static bool TryEquipBagFromInventorySlot(PlayerInventory inventory, int sourceIndex,
        ItemData expected, int bagSlotIndex, int baseUnlockedSlots = 16)
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
            return Overburst.Persistence.AccountGameplaySession.Run(() =>
                TryEquipBagFromInventorySlot(inventory, sourceIndex, expected, bagSlotIndex, baseUnlockedSlots));
        var bags = Loadout.Bags;
        if (!Overburst.Persistence.AccountGameplaySession.UsesAccountStorage(inventory)
            || inventory == null || bags == null || bagSlotIndex < 0 || bagSlotIndex >= bags.Length
            || expected == null || expected.itemType != "Bag" || !(expected.baseData is BagItemData)
            || sourceIndex < 0 || sourceIndex >= inventory.UnlockedSlotCount
            || !ReferenceEquals(inventory.GetItemAt(sourceIndex), expected)) return false;
        var previous = bags[bagSlotIndex];
        if (previous != null && previous.IsSameRuntimeItem(expected)) return false;
        var proposed = (ItemData[])bags.Clone(); proposed[bagSlotIndex] = expected;
        int nextSlots = inventory.CalculateUnlockedSlotCount(proposed,
            Overburst.Persistence.AccountGameplaySession.Current?.BaseUnlockedSlots ?? baseUnlockedSlots);
        if (!inventory.TryReplaceOwnedItemAt(sourceIndex, expected, previous, false)) return false;
        bags[bagSlotIndex] = expected;
        inventory.SetUnlockedSlotCountWithoutNotification(nextSlots);
        NotifyBagTrade(inventory);
        return true;
    }

    public static bool TryUnequipBagToInventorySlot(PlayerInventory inventory, int targetIndex,
        ItemData expected, int bagSlotIndex, int baseUnlockedSlots = 16)
    {
        if (Overburst.Persistence.AccountGameplaySession.ShouldRoute)
            return Overburst.Persistence.AccountGameplaySession.Run(() =>
                TryUnequipBagToInventorySlot(inventory, targetIndex, expected, bagSlotIndex, baseUnlockedSlots));
        var bags = Loadout.Bags;
        if (!Overburst.Persistence.AccountGameplaySession.UsesAccountStorage(inventory)
            || inventory == null || bags == null || bagSlotIndex < 0 || bagSlotIndex >= bags.Length
            || expected == null || !ReferenceEquals(bags[bagSlotIndex], expected)) return false;
        var proposed = (ItemData[])bags.Clone(); proposed[bagSlotIndex] = null;
        int nextSlots = inventory.CalculateUnlockedSlotCount(proposed,
            Overburst.Persistence.AccountGameplaySession.Current?.BaseUnlockedSlots ?? baseUnlockedSlots);
        if (targetIndex < 0 || targetIndex >= nextSlots || inventory.GetItemAt(targetIndex) != null) return false;
        if (!inventory.TryReplaceOwnedItemAt(targetIndex, null, expected, false)) return false;
        bags[bagSlotIndex] = null;
        inventory.SetUnlockedSlotCountWithoutNotification(nextSlots);
        NotifyBagTrade(inventory);
        return true;
    }

    private static void NotifyBagTrade(PlayerInventory inventory)
    {
        // Notifications occur after the local exchange. Listener failure cannot undo it.
        try { inventory.NotifyAccountApplied(); }
        catch (System.Exception error) { Debug.LogException(error); }
    }

    // Bag effects are read at pickup/reward boundaries. They never alter actor movement or health.
    public void RefreshBagBonusesForCurrentActor() { }
    public void RefreshBagBonuses(PlayerMovement movement, CombatHealth health) { }

}

internal sealed class PlayerAccountLoadout
{
    internal ItemData[] Weapons = new ItemData[1];
    internal ItemData[] Gear = new ItemData[6];
    internal ItemData ElementalGem;
    internal int ActiveWeaponSlot;
    internal bool EquipmentInitialized;
    internal string[] FlaskIds = new string[3];
    internal bool FlasksInitialized;
    internal ItemData[] Bags = new ItemData[1];
    internal readonly ConsumableItemData[] QuickConsumables = new ConsumableItemData[10];
    internal readonly string[] QuickFlaskIds = new string[10];
    internal readonly IQuickSlotSkill[] QuickSkills = new IQuickSlotSkill[10];
    internal bool LegacyFlasksImported;
}
