public sealed class GeneralGoodsMerchantStockGenerator : MerchantStockGenerator
{
    public override bool CanGenerate(MerchantDefinition definition)
    {
        return definition != null && definition.Category == ShopCategory.GeneralGoods;
    }

    public override MerchantInventory Generate(MerchantDefinition definition, MerchantStockGenerationContext context)
    {
        MerchantInventory inventory = CreateEmptyWithGold(definition, context);
        if (context == null)
            return inventory;

        int minTotal;
        int maxTotal;
        MerchantReputationService.GetGeneralGoodsRange(definition, out minTotal, out maxTotal, context.GeneralGoodsMinStackTotal, context.GeneralGoodsMaxStackTotal);
        AddRandomGeneralGoodsStack(inventory, context.SmallHealPotion, minTotal, maxTotal);
        AddRandomGeneralGoodsStack(inventory, context.MediumHealPotion, minTotal, maxTotal);
        AddRandomGeneralGoodsStack(inventory, context.LargeHealPotion, minTotal, maxTotal);
        AddRandomGeneralGoodsStack(inventory, context.ExtraLargeHealPotion, minTotal, maxTotal);
        foreach (var flask in FlaskLootPolicy.GameplayCatalog) AddFixedGeneralGoodsStack(inventory, flask, 1);
        AddRandomGeneralGoodsStack(inventory, context.MoveSpeedPotion, minTotal, maxTotal);
        return inventory;
    }

    private void AddRandomGeneralGoodsStack(MerchantInventory inventory, BaseItemData itemData, int minTotal, int maxTotal)
    {
        AddRandomStackTotal(inventory, itemData, ResolveStockGrade(itemData), minTotal, maxTotal);
    }

    private void AddFixedGeneralGoodsStack(MerchantInventory inventory, BaseItemData itemData, int count)
    {
        if (inventory == null || itemData == null || count <= 0)
            return;

        inventory.AddItem(CreateStockItem(itemData, ResolveStockGrade(itemData), count));
    }

    private static ItemGrade ResolveStockGrade(BaseItemData itemData)
    {
        ConsumableItemData consumableData = itemData as ConsumableItemData;
        return consumableData != null ? consumableData.defaultGrade : ItemGrade.Common;
    }
}
