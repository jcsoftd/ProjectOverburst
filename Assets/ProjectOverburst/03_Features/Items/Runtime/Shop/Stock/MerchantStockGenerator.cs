using UnityEngine;

public abstract class MerchantStockGenerator
{
    public abstract bool CanGenerate(MerchantDefinition definition);
    public abstract MerchantInventory Generate(MerchantDefinition definition, MerchantStockGenerationContext context);

    protected MerchantInventory CreateEmptyWithGold(MerchantDefinition definition, MerchantStockGenerationContext context)
    {
        MerchantInventory inventory = new MerchantInventory();
        inventory.InitializeEmpty(definition != null ? definition.InventoryCapacity : 20);

        int minGold;
        int maxGold;
        int fallbackMin = context != null ? context.MerchantGoldMin : 1000;
        int fallbackMax = context != null ? context.MerchantGoldMax : 2000;
        MerchantReputationService.GetMerchantGoldRange(definition, out minGold, out maxGold, fallbackMin, fallbackMax);
        inventory.TryAddCurrency(CurrencyType.Gold, Random.Range(Mathf.Max(0, minGold), Mathf.Max(minGold, maxGold) + 1));
        return inventory;
    }

    protected void AddRandomStackTotal(MerchantInventory inventory, BaseItemData itemData, ItemGrade grade, int minTotal, int maxTotal)
    {
        if (inventory == null || itemData == null)
            return;

        int count = Random.Range(Mathf.Max(1, minTotal), Mathf.Max(minTotal, maxTotal) + 1);
        ItemData item = CreateStockItem(itemData, grade, count);
        inventory.AddItem(item);
    }

    protected ItemData CreateStockItem(BaseItemData data, ItemGrade grade, int stackCount)
    {
        if (data == null || !ItemGradeAvailabilityPolicy.IsEnabled(grade))
            return null;

        ItemData item = new ItemData(data, 1, grade, Mathf.Max(1, stackCount));
        item.EnsureRuntimeState();
        item.EnsureAcquisitionOrder();
        return item;
    }
}
