using System.Collections.Generic;
using UnityEngine;

public static class GearLootPolicy
{
    private static GearItemData[][] catalogByLevel;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => catalogByLevel = null;

    public static GearItemData[] DefinitionsForLevel(int level)
    {
        if (catalogByLevel == null)
        {
            var lists = new List<GearItemData>[101];
            for (int i = 1; i <= 100; i++) lists[i] = new List<GearItemData>();
            foreach (GearItemData data in Resources.LoadAll<GearItemData>("Items/Gear"))
            {
                if (data == null) continue;
                for (int i = 1; i <= 100; i++)
                    if (data.AppearsAtLevel(i)) lists[i].Add(data);
            }
            catalogByLevel = new GearItemData[101][];
            for (int i = 1; i <= 100; i++) catalogByLevel[i] = lists[i].ToArray();
        }
        return catalogByLevel[OverburstGrowthRules.ClampLevel(level)];
    }

    public static ItemData Roll(EnemyRank rank, int mapLevel, ItemGrade mapGrade = ItemGrade.Common, float rareGradePercent = 0)
        => Roll(rank != null ? rank.GradeType : EnemyGradeType.Normal, mapLevel, mapGrade, rareGradePercent);

    // 등급 값만 받는 굴림. 디버그 창의 드롭 모의가 EnemyRank 없이 같은 규칙을 쓴다(90C 7.5). 난수 순서는 위와 같다.
    public static ItemData Roll(EnemyGradeType gradeType, int mapLevel, ItemGrade mapGrade = ItemGrade.Common, float rareGradePercent = 0)
    {
        int itemLevel = OverburstGrowthRules.ClampLevel(mapLevel);
        GearItemData[] items = DefinitionsForLevel(itemLevel);
        if (items.Length == 0) return null;
        bool boss = gradeType == EnemyGradeType.Boss;
        bool elite = gradeType == EnemyGradeType.Elite;
        float chance = CombatDebugSettings.ApplyRunLootChance(boss ? 1f : elite ? .15f : .03f);
        float effectiveChance = Mathf.Min(1f, chance * (1f + MapRunBuffs.Bonus(MapBuffKind.ItemDrop)));
        float chanceRoll = Random.value;
        if (effectiveChance <= 0f || (effectiveChance < 1f && chanceRoll >= effectiveChance)) return null;
        float roll = Random.value;
        ItemGrade grade = FlaskLootPolicy.SelectGrade(roll, mapLevel, boss, elite, rareGradePercent, MapOptionPolicy.HighGradeRollBias(mapGrade));
        return new ItemData(items[Random.Range(0, items.Length)],
            itemLevel, grade);
    }
}
