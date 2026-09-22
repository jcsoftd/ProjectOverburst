using UnityEngine;

public class InventoryItemActionService : MonoBehaviour
{
    private InventorySlotBridge slotBridge;
    private PlayerContext playerContext;
    private PlayerInventory inventory;
    private CombatHealth playerHealth;
    private PlayerBuffController playerBuffController;
    private InventoryQuickSlotBindingController quickSlots;
    private ItemUseCooldownController cooldownController;
    private readonly IItemUseHandler[] itemUseHandlers =
    {
        new MoveSpeedPotionUseHandler(),
        new HealConsumableUseHandler()
    };

    public void Init(InventorySlotBridge bridge, InventoryQuickSlotBindingController quickSlotController)
    {
        slotBridge = bridge;
        quickSlots = quickSlotController;
        ResolveReferences();
    }

    public bool EquipWeapon(SlotUI sourceSlot, int weaponSlotIndex)
    {
        ResolveReferences();
        return slotBridge != null && slotBridge.EquipWeaponFromContextMenu(sourceSlot, weaponSlotIndex);
    }

    public bool UnequipWeaponSlot(int weaponSlotIndex)
    {
        ResolveReferences();

        if (slotBridge == null)
            return false;

        bool unequipped = slotBridge.UnequipWeaponFromContextMenu(weaponSlotIndex, out string failureMessage);
        if (!unequipped && !string.IsNullOrEmpty(failureMessage))
            Debug.LogWarning("[InventoryItemActionService] " + failureMessage, this);

        return unequipped;
    }

    public ItemData GetWeaponSlotItem(int weaponSlotIndex)
    {
        ResolveReferences();
        return slotBridge != null ? slotBridge.GetWeaponSlotItemFromContextMenu(weaponSlotIndex) : null;
    }

    public bool DropItem(SlotUI sourceSlot, Vector2 screenPosition)
    {
        ResolveReferences();
        bool dropped = slotBridge != null && slotBridge.HandleExternalDrop(sourceSlot);
        if (dropped)
            ClearMissingQuickSlotConsumables();

        return dropped;
    }

    public bool CanSplitStack(SlotUI sourceSlot)
    {
        ResolveReferences();
        return inventory != null && sourceSlot != null && inventory.CanSplitStackAt(sourceSlot.SlotIndex);
    }

    public bool SplitStack(SlotUI sourceSlot, int amount)
    {
        ResolveReferences();
        if (inventory == null || sourceSlot == null)
            return false;

        bool split = inventory.SplitStackAt(sourceSlot.SlotIndex, amount);
        if (split)
            slotBridge?.RefreshSlotsFromContextMenu();
        else
            Debug.LogWarning("[InventoryItemActionService] Stack split failed. Check split amount and empty inventory slot.", this);

        return split;
    }

    public bool UseConsumable(SlotUI sourceSlot)
    {
        ResolveReferences();

        ItemData item = sourceSlot != null ? sourceSlot.DisplayItem : null;
        int slotIndex = sourceSlot != null ? sourceSlot.SlotIndex : -1;
        return UseConsumableItem(item, slotIndex, ItemUseSource.InventoryContextMenu);
    }

    public bool UseQuickSlot(int key)
    {
        ResolveReferences();

        if (key >= 4 && key <= 6)
        {
            var flasks = PlayerFlaskController.Current;
            string reason = "물약을 장착해 주세요.";
            bool used = flasks != null && flasks.TryUse(key - 4, out reason);
            if (!used) SpawnPlayerStatusText(reason);
            return used;
        }
        ConsumableItemData consumableData = quickSlots != null ? quickSlots.GetBoundConsumable(key) : null;
        if (consumableData == null)
        {
            ReportConsumableResult("퀵슬롯 " + key + "이 비어 있습니다.", true);
            return false;
        }

        if (inventory == null)
        {
            ReportConsumableResult("Player inventory was not found.", true);
            return false;
        }

        ItemData item = inventory.FindFirstItemByBaseData(consumableData);
        if (item == null)
        {
            ReportConsumableResult(consumableData.itemName + " 보유 수량이 없습니다.", true);
            return false;
        }

        int slotIndex = inventory != null ? inventory.FindFirstMatchingItemIndex(item) : -1;
        return UseConsumableItem(item, slotIndex, ItemUseSource.QuickSlot);
    }

    public ConsumableItemData GetQuickSlotConsumable(int key)
    {
        ResolveReferences();
        return quickSlots != null ? quickSlots.GetBoundConsumable(key) : null;
    }

    public int GetConsumableQuantity(ConsumableItemData consumableData)
    {
        ResolveReferences();
        return inventory != null ? inventory.CountItemsByBaseData(consumableData) : 0;
    }

    public float GetConsumableCooldownRatio(ConsumableItemData consumableData)
    {
        ResolveReferences();
        return cooldownController != null ? cooldownController.GetRemainingRatio(GetCooldownKey(consumableData)) : 0f;
    }

    public float GetConsumableCooldownRemaining(ConsumableItemData consumableData)
    {
        ResolveReferences();
        return cooldownController != null ? cooldownController.GetRemaining(GetCooldownKey(consumableData)) : 0f;
    }

    public bool IsConsumableCoolingDown(ConsumableItemData consumableData)
    {
        ResolveReferences();
        return cooldownController != null && cooldownController.IsCoolingDown(GetCooldownKey(consumableData));
    }

    private bool UseConsumableItem(ItemData item, int inventorySlotIndex, ItemUseSource source)
    {
        if (item == null || item.stackCount <= 0 || item.itemType != "Consumable" || !(item.baseData is ConsumableItemData consumableData))
        {
            ReportConsumableResult("Cannot use this item.", true);
            return false;
        }

        if (inventory == null || !inventory.ContainsItem(item))
        {
            ReportConsumableResult("Consumable is not in inventory.", true);
            return false;
        }

        if (item.baseData is FlaskItemData) { SpawnPlayerStatusText("은신처에서 4~6번 슬롯에 장착해 주세요."); return false; }

        IItemUseHandler handler = FindUseHandler(item);
        if (handler == null)
        {
            ReportConsumableResult("Cannot use this item yet.", true);
            return false;
        }

        ItemUseContext context = new ItemUseContext(
            inventory,
            playerHealth,
            playerBuffController,
            cooldownController,
            quickSlots,
            item,
            inventorySlotIndex,
            source);

        ItemUseResult canUse = handler.CanUse(context);
        if (!canUse.Success)
        {
            ReportItemUseFailure(canUse);
            return false;
        }

        ItemUseResult useResult = handler.Use(context);
        if (!useResult.Success)
        {
            ReportItemUseFailure(useResult);
            return false;
        }

        bool consumeOnUse = consumableData.consumeOnUse;
        if (consumeOnUse && !inventory.ConsumeItem(item, 1))
        {
            ReportConsumableResult("Not enough item quantity.", true);
            return false;
        }

        if (consumeOnUse)
            ClearQuickSlotIfEmpty(consumableData);

        cooldownController?.StartCooldown(context.CooldownKey, context.CooldownDuration);
        slotBridge?.RefreshSlotsFromContextMenu();
        ReportConsumableResult(useResult.Message, false);
        return true;
    }

    public bool BindQuickSlot(int key, ItemData item)
    {
        ResolveReferences();
        if (item?.baseData is FlaskItemData)
        {
            string reason = "물약 장착기를 찾을 수 없습니다.";
            var flasks = PlayerFlaskController.Current;
            bool bound = flasks != null && flasks.TryEquip(key - 4, item, out reason);
            if (!bound) SpawnPlayerStatusText(reason);
            return bound;
        }
        return key == 7 && quickSlots != null && quickSlots.Bind(key, item);
    }

    public string GetQuickSlotLabel(int key)
    {
        ResolveReferences();
        if (key >= 4 && key <= 6) return key + " : " + (PlayerFlaskController.Current?.GetItem(key-4)?.itemName ?? "물약 비어 있음");
        string itemName = quickSlots != null ? quickSlots.GetBoundItemDisplayName(key) : "Empty";
        return key + " : " + itemName;
    }

    public void ShowItemInfo(ItemData item)
    {
        if (item == null)
            return;

        Debug.Log("[InventoryItemActionService] ShowItemInfo reserved: " + item.itemName, this);
    }

    private IItemUseHandler FindUseHandler(ItemData item)
    {
        for (int i = 0; i < itemUseHandlers.Length; i++)
        {
            IItemUseHandler handler = itemUseHandlers[i];
            if (handler != null && handler.CanHandle(item))
                return handler;
        }

        return null;
    }

    private void ReportConsumableResult(string message, bool warning)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        if (warning)
            Debug.LogWarning("[InventoryItemActionService] " + message, this);
        else
            Debug.Log("[InventoryItemActionService] " + message, this);
    }

    private void ReportItemUseFailure(ItemUseResult result)
    {
        ReportConsumableResult(result.Message, true);

        if (result.ShowPlayerStatusText)
            SpawnPlayerStatusText(result.Message);
    }

    private void SpawnPlayerStatusText(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        Vector3 position = playerHealth != null ? playerHealth.transform.position : transform.position;
        DamageNumberSpawner.SpawnStatusText(position, message, new Color(0.95f, 0.88f, 0.58f, 1f));
    }

    private void ResolveReferences()
    {
        if (slotBridge == null)
            slotBridge = GetComponent<InventorySlotBridge>() ?? GetComponentInParent<InventorySlotBridge>();

        if (inventory == null)
            inventory = Object.FindFirstObjectByType<PlayerInventory>();

        if (playerContext == null)
            playerContext = PlayerContext.GetOrCreate();

        CombatHealth leaderHealth = playerContext != null ? playerContext.CurrentActorHealth : null;
        if (leaderHealth != null)
            playerHealth = leaderHealth;

        PlayerBuffController leaderBuffController = playerContext != null ? playerContext.CurrentActorBuffController : null;
        if (leaderBuffController != null)
            playerBuffController = leaderBuffController;

        PlayerMovement player = null;
        if (playerHealth == null || playerBuffController == null)
            player = playerContext != null ? playerContext.CurrentActorMovement : Object.FindFirstObjectByType<PlayerMovement>();

        if (playerHealth == null)
            playerHealth = player != null ? player.GetComponent<CombatHealth>() : null;

        if (playerBuffController == null && player != null)
        {
            playerBuffController = player.GetComponent<PlayerBuffController>();
            if (playerBuffController == null)
                playerBuffController = player.GetComponentInParent<PlayerBuffController>();
            if (playerBuffController == null)
                playerBuffController = player.GetComponentInChildren<PlayerBuffController>(true);
        }

        if (playerBuffController == null && playerHealth != null)
        {
            playerBuffController = playerHealth.GetComponentInParent<PlayerBuffController>();
            if (playerBuffController == null)
                playerBuffController = playerHealth.GetComponentInChildren<PlayerBuffController>(true);
            if (playerBuffController == null)
                playerBuffController = playerHealth.gameObject.AddComponent<PlayerBuffController>();
        }

        if (playerBuffController == null)
            playerBuffController = Object.FindFirstObjectByType<PlayerBuffController>(FindObjectsInactive.Include);

        if (quickSlots == null)
            quickSlots = GetComponent<InventoryQuickSlotBindingController>() ?? GetComponentInParent<InventoryQuickSlotBindingController>();

        if (cooldownController == null)
            cooldownController = GetComponent<ItemUseCooldownController>() ?? GetComponentInParent<ItemUseCooldownController>();

        if (cooldownController == null)
            cooldownController = gameObject.AddComponent<ItemUseCooldownController>();
    }

    private string GetCooldownKey(ConsumableItemData consumableData)
    {
        if (consumableData == null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(consumableData.targetBuffId))
            return consumableData.targetBuffId;

        return consumableData.name;
    }

    private void ClearQuickSlotIfEmpty(ConsumableItemData consumableData)
    {
        if (quickSlots == null || inventory == null || consumableData == null)
            return;

        if (inventory.CountItemsByBaseData(consumableData) <= 0)
            quickSlots.ClearConsumable(consumableData);
    }

    private void ClearMissingQuickSlotConsumables()
    {
        if (quickSlots != null && inventory != null)
            quickSlots.ClearMissingConsumables(inventory);
    }
}
