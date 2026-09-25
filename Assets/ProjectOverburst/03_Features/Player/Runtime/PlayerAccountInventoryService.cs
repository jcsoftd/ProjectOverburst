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
    private PlayerContext playerContext;
    private PlayerMovement playerMovement;
    private PlayerStaminaController playerStaminaController;
    private CombatHealth playerHealth;
    private PlayerMovement appliedBagMovementTarget; // 이동속도 적용 대상
    private PlayerStaminaController appliedBagStaminaTarget; // 스태미너 적용 대상
    private CombatHealth appliedBagHealthTarget; // HP 적용 대상
    private float appliedBagMaxStaminaBonus; // 기존 적용 스태미너
    private float appliedBagMaxHpBonus; // 기존 적용 HP

    private void Start()
    {
        playerContext = PlayerContext.GetOrCreate();
        if (playerContext != null)
        {
            playerContext.CurrentActorChanged += HandleAccountActorChanged;
            HandleAccountActorChanged(playerContext.CurrentActor);
        }
    }

    private void OnDestroy()
    {
        if (playerContext != null) playerContext.CurrentActorChanged -= HandleAccountActorChanged;
        RefreshBagBonuses(null, null, null);
        if (instance == this) instance = null;
    }

    private void HandleAccountActorChanged(PlayerActorRuntime actor)
    {
        RefreshBagBonuses(actor != null ? actor.Movement : null,
            actor != null && actor.PlayerKit != null ? actor.PlayerKit.StaminaController : null,
            actor != null ? actor.Health : null);
    }

    public void RefreshBagBonusesForCurrentActor()
    {
        HandleAccountActorChanged(PlayerContext.Instance != null ? PlayerContext.Instance.CurrentActor : null);
    }

    public void RefreshBagBonuses(PlayerMovement movement, PlayerStaminaController stamina, CombatHealth health)
    {
        playerMovement = movement;
        playerStaminaController = stamina;
        playerHealth = health;
        CalculateEquippedBagStatBonuses(out float moveSpeedPercent, out float maxStaminaBonus, out float maxHpBonus);
        ApplyBagMoveSpeedBonus(moveSpeedPercent);
        ApplyBagMaxStaminaBonus(maxStaminaBonus);
        ApplyBagMaxHpBonus(maxHpBonus);
    }

    private void CalculateEquippedBagStatBonuses(out float moveSpeedPercent, out float maxStaminaBonus, out float maxHpBonus)
    {
        moveSpeedPercent = 0f;
        maxStaminaBonus = 0f;
        maxHpBonus = 0f;

        if (Loadout.Bags == null)
            return;

        for (int i = 0; i < Loadout.Bags.Length; i++)
        {
            ItemData bag = Loadout.Bags[i];
            if (bag == null || !(bag.baseData is BagItemData))
                continue;

            bag.EnsureRuntimeState();
            if (bag.bagOptions == null)
                continue;

            for (int optionIndex = 0; optionIndex < bag.bagOptions.Count; optionIndex++)
            {
                BagRandomOptionRoll option = bag.bagOptions[optionIndex];
                if (option == null)
                    continue;

                switch (option.optionType)
                {
                    case BagRandomOptionType.MoveSpeedPercent:
                        moveSpeedPercent += Mathf.Max(0f, option.value);
                        break;
                    case BagRandomOptionType.MaxStamina:
                        maxStaminaBonus += Mathf.Max(0f, option.value);
                        break;
                    case BagRandomOptionType.MaxHp:
                        maxHpBonus += Mathf.Max(0f, option.value);
                        break;
                }
            }
        }
    }

    private void ApplyBagMoveSpeedBonus(float moveSpeedPercent)
    {
        if (appliedBagMovementTarget != null && appliedBagMovementTarget != playerMovement)
            appliedBagMovementTarget.SetBagMoveSpeedBonusPercent(0f);

        appliedBagMovementTarget = playerMovement;
        if (appliedBagMovementTarget != null)
            appliedBagMovementTarget.SetBagMoveSpeedBonusPercent(moveSpeedPercent);
    }

    private void ApplyBagMaxStaminaBonus(float maxStaminaBonus)
    {
        maxStaminaBonus = Mathf.Max(0f, maxStaminaBonus);

        if (appliedBagStaminaTarget != null && appliedBagStaminaTarget != playerStaminaController)
        {
            float previousBase = Mathf.Max(1f, appliedBagStaminaTarget.MaxStamina - appliedBagMaxStaminaBonus);
            appliedBagStaminaTarget.SetMaxStamina(previousBase, false);
            appliedBagMaxStaminaBonus = 0f;
        }

        appliedBagStaminaTarget = playerStaminaController;
        if (appliedBagStaminaTarget == null)
            return;

        float baseMaxStamina = Mathf.Max(1f, appliedBagStaminaTarget.MaxStamina - appliedBagMaxStaminaBonus);
        appliedBagStaminaTarget.SetMaxStamina(baseMaxStamina + maxStaminaBonus, false);
        appliedBagMaxStaminaBonus = maxStaminaBonus;
    }

    private void ApplyBagMaxHpBonus(float maxHpBonus)
    {
        maxHpBonus = Mathf.Max(0f, maxHpBonus);

        if (appliedBagHealthTarget != null && appliedBagHealthTarget != playerHealth)
        {
            float previousBase = Mathf.Max(1f, appliedBagHealthTarget.MaxHp - appliedBagMaxHpBonus);
            appliedBagHealthTarget.SetMaxHp(previousBase, false);
            appliedBagMaxHpBonus = 0f;
        }

        appliedBagHealthTarget = playerHealth;
        if (appliedBagHealthTarget == null)
            return;

        float baseMaxHp = Mathf.Max(1f, appliedBagHealthTarget.MaxHp - appliedBagMaxHpBonus);
        appliedBagHealthTarget.SetMaxHp(baseMaxHp + maxHpBonus, false);
        appliedBagMaxHpBonus = maxHpBonus;
    }


}

internal sealed class PlayerAccountLoadout
{
    internal ItemData[] Weapons = new ItemData[1];
    internal ItemData[] Gear = new ItemData[7];
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
