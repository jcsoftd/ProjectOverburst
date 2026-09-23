using UnityEngine;

public class ActionSlotHudUI : MonoBehaviour
{
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private InventoryQuickSlotBindingController quickSlotBindingController;
    [SerializeField] private InventoryItemActionService actionService;
    [SerializeField] private ActionSlotHudSlotUI[] quickSlots = new ActionSlotHudSlotUI[InventoryQuickSlotBindingController.SlotCount];

    private void Awake()
    {
        BindVisuals();
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        Refresh();
    }

    private void Update()
    {
        ResolveReferences();
        HandleQuickSlotHotkeys();
        Refresh();
    }

    public void Refresh()
    {
        RefreshQuickSlots();
    }

    private void RefreshQuickSlots()
    {
        BindVisuals();
        if (quickSlotBindingController != null)
        {
            quickSlotBindingController.ImportLegacyFlasks();
            if (playerInventory != null)
                quickSlotBindingController.ClearMissingConsumables(playerInventory);
            quickSlotBindingController.ClearMissingFlasks();
        }

        for (int i = 0; i < quickSlots.Length; i++)
        {
            int key = i + InventoryQuickSlotBindingController.FirstKey;
            ActionSlotHudSlotUI slot = quickSlots[i];
            if (slot == null)
                continue;

            slot.SetKeyNumber(key);
            ItemData boundFlask = quickSlotBindingController != null ? quickSlotBindingController.GetBoundFlask(key) : null;
            if (boundFlask != null)
            {
                var flasks = PlayerFlaskController.Current;
                int equippedIndex = FlaskRuntime.State(boundFlask)?.equippedSlot ?? -1;
                slot.SetFlask(boundFlask, flasks != null && equippedIndex >= 0 ? flasks.Remaining(equippedIndex) : 0f,
                    flasks != null && equippedIndex >= 0 ? flasks.CooldownRemaining(equippedIndex) : 0f,
                    flasks != null && boundFlask.baseData is FlaskItemData f && flasks.MatchesWeapon(f));
                continue;
            }

            IQuickSlotSkill skill = quickSlotBindingController != null ? quickSlotBindingController.GetBoundSkill(key) : null;
            if (skill != null)
            {
                slot.SetSkill(skill);
                continue;
            }
            ConsumableItemData consumableData = actionService != null ? actionService.GetQuickSlotConsumable(key) : quickSlotBindingController != null ? quickSlotBindingController.GetBoundConsumable(key) : null;
            if (consumableData == null)
            {
                slot.SetEmpty(false);
                slot.SetCooldown(0f);
                continue;
            }

            ItemData displayItem = playerInventory != null ? playerInventory.FindFirstItemByBaseData(consumableData) : null;
            int quantity = actionService != null ? actionService.GetConsumableQuantity(consumableData) : playerInventory != null ? playerInventory.CountItemsByBaseData(consumableData) : 0;
            if (quantity <= 0)
            {
                quickSlotBindingController?.Clear(key);
                slot.SetEmpty(false);
                slot.SetCooldown(0f);
                continue;
            }

            slot.SetConsumable(consumableData, displayItem, quantity);
            slot.SetCooldown(actionService != null ? actionService.GetConsumableCooldownRemaining(consumableData) : 0f);
        }
    }

    private void HandleQuickSlotHotkeys()
    {
        // One gameplay action per key; only the first pressed slot runs this frame.
        PlayerInputFacade facade = PlayerInputFacade.Current;
        if (facade == null || GameplayInputBlocker.IsGameplayInputBlocked || actionService == null)
            return;

        for (int key = InventoryQuickSlotBindingController.FirstKey; key <= InventoryQuickSlotBindingController.SlotCount; key++)
            if (facade.QuickSlotPressedThisFrame(key))
            {
                actionService.UseQuickSlot(key);
                break;
            }
    }

    private void ResolveReferences()
    {
        PlayerContext context = PlayerContext.GetOrCreate();
        if (context != null)
        {
            playerInventory = context.CurrentActorInventory;
        }

        if (quickSlotBindingController == null)
            quickSlotBindingController = FindFirstObjectByType<InventoryQuickSlotBindingController>(FindObjectsInactive.Include);

        if (actionService == null)
            actionService = FindFirstObjectByType<InventoryItemActionService>(FindObjectsInactive.Include);
    }



    private void BindVisuals()
    {
        if (quickSlots == null || quickSlots.Length != InventoryQuickSlotBindingController.SlotCount)
            quickSlots = new ActionSlotHudSlotUI[InventoryQuickSlotBindingController.SlotCount];

        BindSlots("QuickSlotHud", 1, quickSlots);
    }

    private void BindSlots(string groupName, int firstKey, ActionSlotHudSlotUI[] targetSlots)
    {
        Transform group = transform.Find(groupName);
        if (group == null || targetSlots == null)
            return;

        for (int i = 0; i < targetSlots.Length; i++)
        {
            if (targetSlots[i] != null)
                continue;

            Transform slot = group.Find("ActionSlot_" + (firstKey + i));
            targetSlots[i] = slot != null ? slot.GetComponentInChildren<ActionSlotHudSlotUI>(true) : null;
        }
    }
}
