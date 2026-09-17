public enum ItemUseSource
{
    InventoryContextMenu,
    QuickSlot
}

public sealed class ItemUseContext
{
    public ItemUseContext(
        PlayerInventory inventory,
        CombatHealth playerHealth,
        PlayerBuffController playerBuffController,
        ItemUseCooldownController cooldownController,
        InventoryQuickSlotBindingController quickSlots,
        ItemData item,
        int inventorySlotIndex,
        ItemUseSource source)
    {
        Inventory = inventory;
        PlayerHealth = playerHealth;
        PlayerBuffController = playerBuffController;
        CooldownController = cooldownController;
        QuickSlots = quickSlots;
        Item = item;
        InventorySlotIndex = inventorySlotIndex;
        Source = source;
        ConsumableData = item != null ? item.baseData as ConsumableItemData : null;
        CooldownKey = BuildCooldownKey(ConsumableData);
        CooldownDuration = GetCooldownDuration(ConsumableData);
    }

    public PlayerInventory Inventory { get; }
    public CombatHealth PlayerHealth { get; }
    public PlayerBuffController PlayerBuffController { get; }
    public ItemUseCooldownController CooldownController { get; }
    public InventoryQuickSlotBindingController QuickSlots { get; }
    public ItemData Item { get; }
    public ConsumableItemData ConsumableData { get; }
    public int InventorySlotIndex { get; }
    public ItemUseSource Source { get; }
    public string CooldownKey { get; }
    public float CooldownDuration { get; }

    public bool IsCoolingDown
    {
        get { return CooldownController != null && CooldownController.IsCoolingDown(CooldownKey); }
    }

    public float CooldownRemaining
    {
        get { return CooldownController != null ? CooldownController.GetRemaining(CooldownKey) : 0f; }
    }

    private static string BuildCooldownKey(ConsumableItemData consumableData)
    {
        if (consumableData == null)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(consumableData.targetBuffId))
            return consumableData.targetBuffId;

        return consumableData.name;
    }

    private static float GetCooldownDuration(ConsumableItemData consumableData)
    {
        if (consumableData == null)
            return 0f;

        if (consumableData.cooldown > 0f)
            return consumableData.cooldown;

        return consumableData.consumableType == ConsumableType.SpeedBoost ? 5f : 0f;
    }
}
