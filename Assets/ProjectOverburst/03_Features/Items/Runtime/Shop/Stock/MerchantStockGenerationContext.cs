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
    public readonly int WeaponMinStockCount;
    public readonly int WeaponMaxStockCount;
    public readonly int MerchantGoldMin;
    public readonly int MerchantGoldMax;
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
        WeaponMinStockCount = weaponMinStockCount;
        WeaponMaxStockCount = weaponMaxStockCount;
        MerchantGoldMin = merchantGoldMin;
        MerchantGoldMax = merchantGoldMax;
        WeaponCandidates = CollectWeaponCandidates(weaponPool);
    }

    private static List<WeaponItemData> CollectWeaponCandidates(BaseItemData[] pool)
    {
        var results = new List<WeaponItemData>();
        var seen = new HashSet<WeaponItemData>();
        if (pool != null)
            for (int i = 0; i < pool.Length; i++)
                AddWeaponCandidate(pool[i] as WeaponItemData, results, seen);

        // 정식 무기 카탈로그의 완성 자산을 기존 판매 경로에 함께 등록한다.
        WeaponLevelCatalog catalog = WeaponLevelCatalog.Current;
        if (catalog != null && catalog.entries != null)
            for (int i = 0; i < catalog.entries.Length; i++)
                AddWeaponCandidate(catalog.entries[i].weapon, results, seen);

        return results;
    }

    private static void AddWeaponCandidate(WeaponItemData data, List<WeaponItemData> results,
        HashSet<WeaponItemData> seen)
    {
        if (data == null || !WeaponContentPolicy.IsActiveWeapon(data)
            || data.icon == null || data.weaponRootPrefab == null || data.worldPickupPrefab == null
            || !seen.Add(data))
            return;

        results.Add(data);
    }
}
