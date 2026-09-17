public sealed class HealConsumableUseHandler : IItemUseHandler
{
    public bool CanHandle(ItemData item)
    {
        ConsumableItemData consumableData = item != null ? item.baseData as ConsumableItemData : null;
        return consumableData != null && consumableData.consumableType == ConsumableType.HealHp;
    }

    public ItemUseResult CanUse(ItemUseContext context)
    {
        if (context == null || context.Item == null || context.ConsumableData == null)
            return ItemUseResult.Fail("Cannot use this item.");

        if (context.Item.stackCount <= 0)
            return ItemUseResult.Fail("수량이 부족합니다.");

        if (context.Inventory == null || !context.Inventory.ContainsItem(context.Item))
            return ItemUseResult.Fail("Consumable is not in inventory.");

        if (context.PlayerHealth == null)
            return ItemUseResult.Fail("Player health component was not found.");

        if (context.PlayerHealth.CurrentHp >= context.PlayerHealth.MaxHp - 0.001f)
            return ItemUseResult.Fail("아이템 사용 불가", true);

        if (ResolveHealAmount(context) <= 0f)
            return ItemUseResult.Fail("아이템 사용 불가", true);

        if (context.IsCoolingDown)
            return ItemUseResult.Fail("아직 사용할 수 없습니다. 쿨타임 " + context.CooldownRemaining.ToString("0.0") + "초");

        return ItemUseResult.Ok(string.Empty);
    }

    public ItemUseResult Use(ItemUseContext context)
    {
        ItemUseResult canUse = CanUse(context);
        if (!canUse.Success)
            return canUse;

        PlayerHealFeedback.ApplyHeal(context.PlayerHealth, ResolveHealAmount(context));
        return ItemUseResult.Ok(GetUseMessage(context.Item, context.ConsumableData, "Consumable used."));
    }

    private float ResolveHealAmount(ItemUseContext context)
    {
        if (context == null || context.PlayerHealth == null || context.ConsumableData == null)
            return 0f;

        float value = context.ConsumableData.effectValue;
        if (value > 0f && value <= 1f)
            return context.PlayerHealth.MaxHp * value;

        return value;
    }

    private string GetUseMessage(ItemData item, ConsumableItemData consumableData, string fallback)
    {
        if (consumableData != null && !string.IsNullOrWhiteSpace(consumableData.useMessage))
            return consumableData.useMessage;

        if (item != null && !string.IsNullOrWhiteSpace(item.itemName))
            return item.itemName + " used.";

        return fallback;
    }
}
