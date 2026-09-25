using UnityEngine;

public static class GearLootPolicy
{
    private static GearItemData[] catalog;
    private static GearItemData[] Catalog => catalog == null
        ? catalog = Resources.LoadAll<GearItemData>("Items/Gear") : catalog;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => catalog = null;

    public static ItemData Roll(EnemyRank rank, int mapLevel)
    {
        GearItemData[] items = Catalog;
        if (items == null || items.Length == 0) return null;
        bool boss = rank != null && rank.GradeType == EnemyGradeType.Boss;
        bool elite = rank != null && rank.GradeType == EnemyGradeType.Elite;
        float chance = boss ? 1f : elite ? .35f : .08f;
        if (Random.value >= chance) return null;
        ItemGrade grade = FlaskLootPolicy.SelectGrade(Random.value, mapLevel, boss, elite);
        int itemLevel = OverburstGrowthRules.ClampLevel(mapLevel);
        return new ItemData(items[Random.Range(0, items.Length)],
            itemLevel, grade);
    }
}
