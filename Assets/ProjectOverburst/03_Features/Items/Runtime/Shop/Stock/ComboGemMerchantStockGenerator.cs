using UnityEngine;

public sealed class ComboGemMerchantStockGenerator : MerchantStockGenerator
{
    public override bool CanGenerate(MerchantDefinition definition)
    {
        return definition != null && definition.Category == ShopCategory.ComboGem;
    }

    public override MerchantInventory Generate(MerchantDefinition definition, MerchantStockGenerationContext context)
    {
        MerchantInventory inventory = CreateEmptyWithGold(definition, context);
        if (context == null || context.ComboGemCandidates == null || context.ComboGemCandidates.Count == 0)
            return inventory;

        int minCount;
        int maxCount;
        MerchantReputationService.GetComboGemStockRange(definition, out minCount, out maxCount, context.ComboGemMinStockCount, context.ComboGemMaxStockCount);
        int count = Random.Range(Mathf.Max(0, minCount), Mathf.Max(minCount, maxCount) + 1);
        for (int i = 0; i < count; i++)
        {
            ComboGemItemData data = context.ComboGemCandidates[Random.Range(0, context.ComboGemCandidates.Count)];
            ItemGrade grade = MerchantTemporaryArtifactStockPolicy.RollElevatedStockGrade(definition, ItemGrade.Common);
            if ((int)grade < (int)data.minGrade)
                grade = data.minGrade;

            inventory.AddItem(CreateStockItem(data, grade, 1));
        }

        MerchantTemporaryArtifactStockPolicy.TryAddArtifactStock(inventory, definition, context.ComboGemCandidates); // 임시 유물 재고 추가
        return inventory;
    }
}
