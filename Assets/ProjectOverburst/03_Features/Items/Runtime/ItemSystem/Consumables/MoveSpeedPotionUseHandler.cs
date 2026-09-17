using UnityEngine;

public sealed class MoveSpeedPotionUseHandler : IItemUseHandler
{
    private const string DefaultBuffId = "move_speed_potion_30";

    public bool CanHandle(ItemData item)
    {
        ConsumableItemData consumableData = item != null ? item.baseData as ConsumableItemData : null;
        return consumableData != null && consumableData.consumableType == ConsumableType.SpeedBoost;
    }

    public ItemUseResult CanUse(ItemUseContext context)
    {
        if (context == null || context.Item == null || context.ConsumableData == null)
            return ItemUseResult.Fail("Cannot use this item.");

        if (context.Item.stackCount <= 0)
            return ItemUseResult.Fail("수량이 부족합니다.");

        if (context.Inventory == null || !context.Inventory.ContainsItem(context.Item))
            return ItemUseResult.Fail("Consumable is not in inventory.");

        if (context.PlayerBuffController == null)
            return ItemUseResult.Fail("Player buff controller was not found.");

        if (context.IsCoolingDown)
            return ItemUseResult.Fail("아직 사용할 수 없습니다. 쿨타임 " + context.CooldownRemaining.ToString("0.0") + "초");

        return ItemUseResult.Ok(string.Empty);
    }

    public ItemUseResult Use(ItemUseContext context)
    {
        ItemUseResult canUse = CanUse(context);
        if (!canUse.Success)
            return canUse;

        bool alreadyActive = context.PlayerBuffController.HasBuff(GetBuffId(context.ConsumableData));
        context.PlayerBuffController.ApplyBuff(CreateBuff(context.Item, context.ConsumableData));
        return ItemUseResult.Ok(alreadyActive
            ? "Move speed effect duration refreshed to 60 seconds."
            : GetUseMessage(context.Item, context.ConsumableData, "Move speed potion used: +30% move speed for 60 seconds."));
    }

    private BuffDefinition CreateBuff(ItemData item, ConsumableItemData consumableData)
    {
        float duration = consumableData.duration > 0f ? consumableData.duration : 60f;
        float multiplier = consumableData.moveSpeedMultiplier > 0f ? consumableData.moveSpeedMultiplier : consumableData.effectValue;
        if (multiplier <= 0f)
            multiplier = 1.3f;

        return new BuffDefinition
        {
            buffId = GetBuffId(consumableData),
            displayName = !string.IsNullOrWhiteSpace(item.itemName) ? item.itemName : "Move Speed",
            duration = duration,
            tickInterval = duration,
            initialHealPercent = 0f,
            healPercentPerTick = 0f,
            moveSpeedMultiplier = multiplier,
            isDebuff = false,
            iconPointsDown = false,
            icon = consumableData.icon,
            baseIndicatorColor = new Color(0.12f, 0.22f, 0.32f, 0.85f),
            indicatorColor = new Color(0.2f, 0.75f, 1f, 1f)
        };
    }

    private string GetBuffId(ConsumableItemData consumableData)
    {
        if (consumableData != null && !string.IsNullOrWhiteSpace(consumableData.targetBuffId))
            return consumableData.targetBuffId;

        return DefaultBuffId;
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
