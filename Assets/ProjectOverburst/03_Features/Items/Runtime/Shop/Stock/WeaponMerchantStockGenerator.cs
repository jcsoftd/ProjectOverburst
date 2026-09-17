using UnityEngine;

public sealed class WeaponMerchantStockGenerator : MerchantStockGenerator
{
    public override bool CanGenerate(MerchantDefinition definition)
    {
        return definition != null && definition.Category == ShopCategory.Weapon;
    }

    public override MerchantInventory Generate(MerchantDefinition definition, MerchantStockGenerationContext context)
    {
        MerchantInventory inventory = CreateEmptyWithGold(definition, context);
        if (context == null || context.WeaponCandidates == null || context.WeaponCandidates.Count == 0)
            return inventory;

        int count = Random.Range(Mathf.Max(0, context.WeaponMinStockCount), Mathf.Max(context.WeaponMinStockCount, context.WeaponMaxStockCount) + 1);
        for (int i = 0; i < count; i++)
        {
            WeaponItemData data = context.WeaponCandidates[Random.Range(0, context.WeaponCandidates.Count)];
            ItemGrade grade = MerchantTemporaryArtifactStockPolicy.RollElevatedStockGrade(definition, ItemGrade.Common);
            inventory.AddItem(CreateStockItem(data, grade, 1));
        }

        MerchantTemporaryArtifactStockPolicy.TryAddArtifactStock(inventory, definition, context.WeaponCandidates); // 임시 유물 재고 추가
        return inventory;
    }
}
