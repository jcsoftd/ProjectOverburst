using UnityEngine;

public class ActionSlotHudUI : MonoBehaviour
{
    [SerializeField] private PlayerEquipment playerEquipment;
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private InventoryQuickSlotBindingController quickSlotBindingController;
    [SerializeField] private InventoryItemActionService actionService;
    [SerializeField] private ActionSlotHudSlotUI[] weaponSlots = new ActionSlotHudSlotUI[1];
    [SerializeField] private ActionSlotHudSlotUI[] consumableSlots = new ActionSlotHudSlotUI[4];

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
        HandleConsumableHotkeys();
        Refresh();
    }

    public void Refresh()
    {
        RefreshWeaponSlots();
        RefreshConsumableSlots();
    }

    private void RefreshWeaponSlots()
    {
        BindVisuals();

        for (int i = 0; i < weaponSlots.Length; i++)
        {
            ActionSlotHudSlotUI slot = weaponSlots[i];
            if (slot == null)
                continue;

            slot.SetKeyNumber(0);
            ItemData item = playerEquipment != null ? playerEquipment.GetWeaponSlotItem(0) : null;
            bool active = playerEquipment != null && playerEquipment.IsActiveWeaponSlot(0);
            slot.SetItem(item, 0, active, false);
            slot.SetCooldown(0f);
        }
    }

    private void RefreshConsumableSlots()
    {
        BindVisuals();
        if (quickSlotBindingController != null && playerInventory != null)
            quickSlotBindingController.ClearMissingConsumables(playerInventory);

        for (int i = 0; i < consumableSlots.Length; i++)
        {
            int key = i + 4;
            ActionSlotHudSlotUI slot = consumableSlots[i];
            if (slot == null)
                continue;

            slot.SetKeyNumber(key);
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

    private void HandleConsumableHotkeys()
    {
        // GOAL A2: 숫자열/numpad 4~7 직접 읽기 대신 Gameplay QuickSlot4~7을 사용한다.
        // 기존 감각(이번 프레임 눌림, 4>5>6>7 우선 1개 실행)을 else-if로 보존.
        PlayerInputFacade facade = PlayerInputFacade.Current;
        if (facade == null || GameplayInputBlocker.IsGameplayInputBlocked || actionService == null)
            return;

        if (facade.QuickSlot4PressedThisFrame)
            actionService.UseQuickSlot(4);
        else if (facade.QuickSlot5PressedThisFrame)
            actionService.UseQuickSlot(5);
        else if (facade.QuickSlot6PressedThisFrame)
            actionService.UseQuickSlot(6);
        else if (facade.QuickSlot7PressedThisFrame)
            actionService.UseQuickSlot(7);
    }

    private void ResolveReferences()
    {
        PlayerContext context = PlayerContext.GetOrCreate();
        if (context != null)
        {
            playerEquipment = context.CurrentActorEquipment;
            playerInventory = context.CurrentActorInventory;
        }

        if (quickSlotBindingController == null)
            quickSlotBindingController = FindFirstObjectByType<InventoryQuickSlotBindingController>(FindObjectsInactive.Include);

        if (actionService == null)
            actionService = FindFirstObjectByType<InventoryItemActionService>(FindObjectsInactive.Include);
    }



    private void BindVisuals()
    {
        if (weaponSlots == null || weaponSlots.Length != 1)
            weaponSlots = new ActionSlotHudSlotUI[1];

        if (consumableSlots == null || consumableSlots.Length != 4)
            consumableSlots = new ActionSlotHudSlotUI[4];

        BindSlots("WeaponSlotHud", 1, weaponSlots);
        BindSlots("ConsumableSlotHud", 4, consumableSlots);
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
            targetSlots[i] = slot != null ? slot.GetComponent<ActionSlotHudSlotUI>() : null;
        }
    }
}
