using UnityEngine;
using System.Linq;

public static class FlaskLootPolicy
{
    private static FlaskItemData[] catalog;
    private static FlaskItemData[] gameplayCatalog;
    public static FlaskItemData[] GameplayCatalog => gameplayCatalog == null || gameplayCatalog.Length == 0 ? gameplayCatalog = Catalog.Where(x=>x.AvailableForDropsAndShop).ToArray() : gameplayCatalog;
    public static FlaskItemData[] Catalog => catalog == null || catalog.Length == 0 ? catalog = Resources.LoadAll<FlaskItemData>("Items/Flasks") : catalog;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { catalog = null; gameplayCatalog = null; }

    public static ItemData Roll(EnemyRank rank, int mapLevel)
    {
        if (GameplayCatalog.Length == 0) return null;
        bool boss = rank != null && rank.GradeType == EnemyGradeType.Boss;
        bool elite = rank != null && rank.GradeType != EnemyGradeType.Normal;
        float chance = boss ? 1f : elite ? .30f : .04f;
        if (Random.value >= chance) return null;
        ItemGrade grade = SelectGrade(Random.value, mapLevel, boss, elite);
        int itemLevel = OverburstGrowthRules.ClampLevel(mapLevel);
        return new ItemData(GameplayCatalog[Random.Range(0, GameplayCatalog.Length)],
            itemLevel, grade);
    }

    // Later dungeon tiers unlock artifact/mythic; starter-zone farming cannot supply them.
    public static ItemGrade SelectGrade(float roll, int difficulty, bool boss, bool elite)
    {
        float[] weights = boss ? new[] { 0f, 0f, 45f, 35f, 17f, 2.5f, .5f }
            : elite ? new[] { 10f, 30f, 40f, 16f, 3.7f, .28f, .02f }
            : new[] { 45f, 32f, 18f, 4.5f, .49f, .009f, .001f };
        int max = difficulty >= 25 ? 6 : difficulty >= 18 ? 5 : difficulty >= 10 ? 4 : difficulty >= 5 ? 3 : 2;
        float total = 0f; for (int i = 0; i <= max; i++) total += weights[i];
        float pick = Mathf.Clamp01(roll) * total;
        for (int i = 0; i <= max; i++) { pick -= weights[i]; if (pick < 0f) return (ItemGrade)i; }
        return (ItemGrade)max;
    }
}
