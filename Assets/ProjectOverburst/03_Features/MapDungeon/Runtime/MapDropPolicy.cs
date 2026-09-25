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

    public static ItemData Roll(MapItemData definition, AccountContentRegistry registry,
        int dungeonLevel, EnemyThemeTier tier, string runId)
    {
        if (definition == null || registry == null || string.IsNullOrEmpty(runId)
            || Random.value >= Chance(tier)) return null;
        int level = Random.Range(MinimumLevel(dungeonLevel, tier), MaximumLevel(dungeonLevel, tier) + 1);
        ItemGrade grade = ItemGradeAvailabilityPolicy.RollWeightedGrade();
        var item = new ItemData(definition, level, grade) { originRunId = runId };
        item.mapState = new MapInstanceState
        {
            mapContentId = registry.IdFor(definition), monsterThemeId = MapThemeCatalog.RollThemeId(),
            level = level, grade = grade, options = MapOptionPolicy.Roll(grade)
        };
        return item;
    }
}
