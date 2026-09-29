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

    public static ItemData Roll(EnemyRank rank, int mapLevel, ItemGrade mapGrade = ItemGrade.Common)
    {
        int itemLevel = OverburstGrowthRules.ClampLevel(mapLevel);
        GearItemData[] items = DefinitionsForLevel(itemLevel);
        if (items.Length == 0) return null;
        bool boss = rank != null && rank.GradeType == EnemyGradeType.Boss;
        bool elite = rank != null && rank.GradeType == EnemyGradeType.Elite;
        float chance = boss ? 1f : elite ? .35f : .08f;
        if (Random.value >= Mathf.Min(1f, chance * (1f + MapRunBuffs.Bonus(MapBuffKind.ItemDrop)))) return null;
        float roll = Random.value;
        ItemGrade grade = FlaskLootPolicy.SelectGrade(Mathf.Lerp(roll, 1f,
            MapOptionPolicy.HighGradeRollBias(mapGrade)), mapLevel, boss, elite);
        return new ItemData(items[Random.Range(0, items.Length)],
            itemLevel, grade);
    }
}
