using System.Collections.Generic;
using UnityEngine;

public static class MerchantTemporaryArtifactStockPolicy
{
    public const int RequiredReputationLevel = 3;
    private const float ArtifactStockChance = 0.10f;

    public static bool TryAddArtifactStock(MerchantInventory inventory, MerchantDefinition merchant, IList<WeaponItemData> candidates)
    {
        if (inventory == null || !IsSupportedMerchant(merchant) || merchant.Category != ShopCategory.Weapon || candidates == null || candidates.Count == 0)
            return false;

        if (Random.value > ArtifactStockChance)
            return false;

        WeaponItemData data = candidates[Random.Range(0, candidates.Count)];
        ItemData item = CreateStockItem(data, ItemGrade.Artifact);
        return item != null && inventory.AddItem(item);
    }

    public static bool CanAddMerchantOffer(MerchantDefinition merchant, ItemData item, out string message)
    {
        if (IsRestrictedArtifactStockItem(merchant, item) && MerchantReputationService.GetLevel(merchant) < RequiredReputationLevel)
        {
            message = "우호도 레벨이 " + RequiredReputationLevel + " 이상만 구매 가능합니다.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    public static ItemGrade RollElevatedStockGrade(MerchantDefinition merchant, ItemGrade minGrade)
    {
        ItemGrade grade = RollElevatedStockGrade(MerchantReputationService.GetLevel(merchant));
        return (int)grade < (int)minGrade ? minGrade : grade;
    }

    public static ItemGrade GetMinimumDisplayGrade(MerchantDefinition merchant)
    {
        int level = MerchantReputationService.GetLevel(merchant);
        if (level >= 3)
            return ItemGrade.Rare;

        if (level >= 2)
            return ItemGrade.Uncommon;

        return ItemGrade.Common;
    }

    public static ItemGrade GetMaximumDisplayGrade(MerchantDefinition merchant)
    {
        int level = MerchantReputationService.GetLevel(merchant);
        if (level >= 2)
            return ItemGrade.Legendary;

        if (level >= 1)
            return ItemGrade.Epic;

        return ItemGrade.Rare;
    }

    public static bool CanDisplayArtifactChance(MerchantDefinition merchant)
    {
        return IsSupportedMerchant(merchant);
    }

    private static ItemGrade RollElevatedStockGrade(int reputationLevel)
    {
        float roll = Random.value;
        switch (Mathf.Clamp(reputationLevel, 0, 3))
        {
            case 1:
                if (roll < 0.20f) return ItemGrade.Common;
                if (roll < 0.65f) return ItemGrade.Uncommon;
                if (roll < 0.90f) return ItemGrade.Rare;
                return ItemGrade.Epic;
            case 2:
                if (roll < 0.40f) return ItemGrade.Uncommon;
                if (roll < 0.75f) return ItemGrade.Rare;
                if (roll < 0.95f) return ItemGrade.Epic;
                return ItemGrade.Legendary;
            case 3:
                if (roll < 0.35f) return ItemGrade.Rare;
                if (roll < 0.75f) return ItemGrade.Epic;
                return ItemGrade.Legendary;
            default:
                if (roll < 0.50f) return ItemGrade.Common;
                if (roll < 0.85f) return ItemGrade.Uncommon;
                return ItemGrade.Rare;
        }
    }

    private static bool IsSupportedMerchant(MerchantDefinition merchant)
    {
        return merchant != null && merchant.Category == ShopCategory.Weapon;
    }

    private static bool IsRestrictedArtifactStockItem(MerchantDefinition merchant, ItemData item)
    {
        if (!IsSupportedMerchant(merchant) || item == null || item.grade != ItemGrade.Artifact)
            return false;

        return merchant.Category == ShopCategory.Weapon && item.baseData is WeaponItemData;
    }

    private static ItemData CreateStockItem(BaseItemData data, ItemGrade grade)
    {
        if (data == null || !ItemGradeAvailabilityPolicy.IsEnabled(grade))
            return null;

        ItemData item = new ItemData(data, 1, grade, 1);
        item.EnsureRuntimeState();
        item.EnsureAcquisitionOrder();
        return item;
    }
}
