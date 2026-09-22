using System.Collections.Generic;

public sealed class MerchantStockGenerationContext
{
    public readonly BaseItemData SmallHealPotion;
    public readonly BaseItemData MediumHealPotion;
    public readonly BaseItemData LargeHealPotion;
    public readonly BaseItemData ExtraLargeHealPotion;
    public readonly BaseItemData PermanentHealPotion;
    public readonly BaseItemData MoveSpeedPotion;
    public readonly int GeneralGoodsMinStackTotal;
    public readonly int GeneralGoodsMaxStackTotal;
    public readonly int ComboGemMinStockCount;
    public readonly int ComboGemMaxStockCount;
    public readonly int WeaponMinStockCount;
    public readonly int WeaponMaxStockCount;
    public readonly int MerchantGoldMin;
    public readonly int MerchantGoldMax;
    public readonly List<ComboGemItemData> ComboGemCandidates;
    public readonly List<WeaponItemData> WeaponCandidates;

    public MerchantStockGenerationContext(
        BaseItemData smallHealPotion,
        BaseItemData mediumHealPotion,
        BaseItemData largeHealPotion,
        BaseItemData extraLargeHealPotion,
        BaseItemData permanentHealPotion,
        BaseItemData moveSpeedPotion,
        int generalGoodsMinStackTotal,
        int generalGoodsMaxStackTotal,
        BaseItemData[] comboGemPool,
        int comboGemMinStockCount,
        int comboGemMaxStockCount,
        BaseItemData[] weaponPool,
        int weaponMinStockCount,
        int weaponMaxStockCount,
        int merchantGoldMin,
        int merchantGoldMax)
    {
        SmallHealPotion = smallHealPotion;
        MediumHealPotion = mediumHealPotion;
        LargeHealPotion = largeHealPotion;
        ExtraLargeHealPotion = extraLargeHealPotion;
        PermanentHealPotion = permanentHealPotion;
        MoveSpeedPotion = moveSpeedPotion;
        GeneralGoodsMinStackTotal = generalGoodsMinStackTotal;
        GeneralGoodsMaxStackTotal = generalGoodsMaxStackTotal;
        ComboGemMinStockCount = comboGemMinStockCount;
        ComboGemMaxStockCount = comboGemMaxStockCount;
        WeaponMinStockCount = weaponMinStockCount;
        WeaponMaxStockCount = weaponMaxStockCount;
        MerchantGoldMin = merchantGoldMin;
        MerchantGoldMax = merchantGoldMax;
        ComboGemCandidates = CollectComboGemCandidates(comboGemPool);
        WeaponCandidates = CollectWeaponCandidates(weaponPool);
    }

    private static List<ComboGemItemData> CollectComboGemCandidates(BaseItemData[] pool)
    {
        List<ComboGemItemData> results = new List<ComboGemItemData>();
        if (pool == null)
            return results;

        for (int i = 0; i < pool.Length; i++)
        {
            ComboGemItemData data = pool[i] as ComboGemItemData;
            if (data != null && WeaponContentPolicy.IsAllowedItemData(data) && (int)data.minGrade <= (int)ItemGrade.Uncommon)
                results.Add(data);
        }

        return results;
    }

    private static List<WeaponItemData> CollectWeaponCandidates(BaseItemData[] pool)
    {
        List<WeaponItemData> results = new List<WeaponItemData>();
        if (pool == null)
            return results;

        for (int i = 0; i < pool.Length; i++)
        {
            WeaponItemData data = pool[i] as WeaponItemData;
            if (data != null)
                results.Add(data);
        }

        return results;
    }
}
