using UnityEngine;

public static class MerchantDisplayPolicy
{
    public static string BuildFallbackInfoText(MerchantDefinition merchant, int reputationLevel, string discountText)
    {
        if (merchant == null)
            return "우호도 Lv.0 기본\n재고 정보 없음\n갱신 Gold 0~0G";

        int minGold;
        int maxGold;
        MerchantReputationService.GetMerchantGoldRange(merchant, out minGold, out maxGold, merchant.MerchantGold, merchant.MerchantGold);

        return "우호도 Lv." + reputationLevel + " " + GetReputationLabel(reputationLevel) + " / 할인 " + discountText
            + "\n재고 " + GetStockSummary(merchant)
            + "\n갱신 Gold " + minGold + "~" + maxGold + "G";
    }

    public static string GetStockSummary(MerchantDefinition merchant)
    {
        if (merchant == null)
            return "정보 없음";

        switch (merchant.Category)
        {
            case ShopCategory.GeneralGoods:
                int goodsMin;
                int goodsMax;
                MerchantReputationService.GetGeneralGoodsRange(merchant, out goodsMin, out goodsMax, 15, 29);
                return "물약 " + goodsMin + "~" + goodsMax + "개";

            case ShopCategory.Weapon:
                return "무기 10~15개";
            default:
                return "기본 재고";
        }
    }

    public static string GetReputationLabel(int level)
    {
        switch (Mathf.Clamp(level, 0, 3))
        {
            case 1:
                return "단골";
            case 2:
                return "신뢰";
            case 3:
                return "VIP";
            default:
                return "기본";
        }
    }

    public static string GetCategoryLabel(MerchantDefinition merchant)
    {
        if (merchant == null)
            return "상인";

        switch (merchant.Category)
        {
            case ShopCategory.GeneralGoods:
                return "잡화";

            case ShopCategory.Weapon:
                return "무기";
            default:
                return "상인";
        }
    }

    public static string GetSaleGradeSummary(MerchantDefinition merchant)
    {
        if (merchant == null)
            return "정보 없음";

        switch (merchant.Category)
        {
            case ShopCategory.GeneralGoods:
                return FormatGradeLabel(ItemGrade.Common) + " ~ " + FormatGradeLabel(ItemGrade.Mythic);

            case ShopCategory.Weapon:
                ItemGrade minGrade = MerchantTemporaryArtifactStockPolicy.GetMinimumDisplayGrade(merchant);
                ItemGrade maxGrade = MerchantTemporaryArtifactStockPolicy.GetMaximumDisplayGrade(merchant);
                string label = FormatGradeLabel(minGrade) + " ~ " + FormatGradeLabel(maxGrade);
                return MerchantTemporaryArtifactStockPolicy.CanDisplayArtifactChance(merchant)
                    ? label + " / " + FormatGradeLabel(ItemGrade.Artifact) + " 10%"
                    : label;
            default:
                return "기본";
        }
    }

    public static string FormatGradeLabel(ItemGrade grade)
    {
        return "<color=" + GradeConfig.GetGradeColorHex(grade) + ">" + ItemTooltipFormatter.GetGradeName(grade) + "</color>";
    }
}
