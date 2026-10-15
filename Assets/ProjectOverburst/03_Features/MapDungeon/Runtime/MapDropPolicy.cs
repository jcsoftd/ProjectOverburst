using Overburst.Persistence;
using UnityEngine;

public static class MapDropPolicy
{
    public static int MinimumLevel(int dungeonLevel, EnemyThemeTier tier)
        => Mathf.Clamp(dungeonLevel - (tier == EnemyThemeTier.Elite ? 2 : 1), 1, 100);

    public static int MaximumLevel(int dungeonLevel, EnemyThemeTier tier)
        => Mathf.Clamp(dungeonLevel + (tier == EnemyThemeTier.Small ? 3
            : tier == EnemyThemeTier.Medium ? 4 : 5), 1, 100);

    // Initial playtest chances. Elite classification wins when size and elite overlap.
    public static float Chance(EnemyThemeTier tier)
        => tier == EnemyThemeTier.Elite ? .10f : tier == EnemyThemeTier.Medium ? .045f : .02f;

    public static float ChanceForMap(EnemyThemeTier tier, MapInstanceState map)
    {
        float chance = tier == EnemyThemeTier.Elite ? .0075f : tier == EnemyThemeTier.Medium ? .00175f : .00075f;
        return Mathf.Clamp01(chance * (1f + MapOptionPolicy.Value(map, MapOptionPolicy.ExtraDrop)));
    }

    public static ItemData Roll(MapItemData definition, AccountContentRegistry registry,
        MapInstanceState map, EnemyThemeTier tier, string runId)
    {
        if (!MapFarmingLoot.Applies(map)) return Roll(definition, registry, map.level, tier, runId);
        float chance = Mathf.Clamp01(ChanceForMap(tier, map) * (1f + MapRunBuffs.Bonus(MapBuffKind.ItemDrop)));
        if (definition == null || registry == null || string.IsNullOrEmpty(runId)
            || chance <= 0 || (chance < 1 && Random.value >= chance)) return null;
        int level = Random.Range(MinimumLevel(map.level, tier), MaximumLevel(map.level, tier) + 1);
        return MapGenerationPolicy.CreateItem(definition, registry, level,
            MapFarmingLoot.SelectMapGrade(Random.value, map.grade), runId);
    }

    public static ItemData Roll(MapItemData definition, AccountContentRegistry registry,
        int dungeonLevel, EnemyThemeTier tier, string runId, bool outdoor = false)
    {
        if (definition == null || registry == null || (!outdoor && string.IsNullOrEmpty(runId))
            || Random.value >= Mathf.Min(1f, Chance(tier) * (1f + MapRunBuffs.Bonus(MapBuffKind.ItemDrop)))) return null;
        int level = Random.Range(MinimumLevel(dungeonLevel, tier), MaximumLevel(dungeonLevel, tier) + 1);
        ItemGrade grade = ItemGradeAvailabilityPolicy.RollWeightedGrade();
        if (outdoor && grade > ItemGrade.Legendary) grade = ItemGrade.Legendary;
        if (outdoor) runId = null;
        return MapGenerationPolicy.CreateItem(definition, registry, level, grade, runId);
    }
}
