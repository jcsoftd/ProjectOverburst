using System.Collections.Generic;
using UnityEngine;

/// <summary>지도 레벨에 맞는 완성 무기를 몬스터의 자연 드랍에 연결한다.</summary>
public static class WeaponLootPolicy
{
    private static WeaponItemData[][] catalogByLevel;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => catalogByLevel = null;

    public static WeaponItemData[] DefinitionsForLevel(int level)
    {
        if (catalogByLevel == null)
        {
            catalogByLevel = new WeaponItemData[101][];
            WeaponLevelCatalog catalog = WeaponLevelCatalog.Current;
            for (int itemLevel = 1; itemLevel <= 100; itemLevel++)
            {
                var candidates = new List<WeaponItemData>();
                var seen = new HashSet<WeaponItemData>();
                if (catalog != null)
                    foreach (WeaponItemData data in catalog.Candidates(itemLevel))
                        if (data.icon != null && data.weaponRootPrefab != null
                            && data.worldPickupPrefab != null && seen.Add(data))
                            candidates.Add(data);
                catalogByLevel[itemLevel] = candidates.ToArray();
            }
        }
        return catalogByLevel[OverburstGrowthRules.ClampLevel(level)];
    }

    public static ItemData Roll(EnemyRank rank, int mapLevel, ItemGrade mapGrade = ItemGrade.Common, float rareGradePercent = 0)
        => Roll(rank != null ? rank.GradeType : EnemyGradeType.Normal, mapLevel, mapGrade, rareGradePercent);

    public static ItemData Roll(EnemyGradeType gradeType, int mapLevel, ItemGrade mapGrade = ItemGrade.Common, float rareGradePercent = 0)
    {
        int itemLevel = OverburstGrowthRules.ClampLevel(mapLevel);
        WeaponItemData[] items = DefinitionsForLevel(itemLevel);
        if (items.Length == 0) return null;
        bool boss = gradeType == EnemyGradeType.Boss;
        bool elite = gradeType == EnemyGradeType.Elite || gradeType == EnemyGradeType.GreaterElite;
        float chance = CombatDebugSettings.ApplyRunLootChance(boss ? .35f : elite ? .08f : .015f);
        float effectiveChance = Mathf.Min(1f, chance * (1f + MapRunBuffs.Bonus(MapBuffKind.ItemDrop)));
        float chanceRoll = Random.value;
        if (effectiveChance <= 0f || (effectiveChance < 1f && chanceRoll >= effectiveChance)) return null;
        ItemGrade grade = FlaskLootPolicy.SelectGrade(Random.value, itemLevel, boss, elite,
            rareGradePercent, MapOptionPolicy.HighGradeRollBias(mapGrade));
        return new ItemData(items[Random.Range(0, items.Length)], itemLevel, grade);
    }
}
