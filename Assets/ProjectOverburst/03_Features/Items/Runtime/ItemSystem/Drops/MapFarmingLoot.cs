using System;
using System.Linq;
using Overburst.Persistence;
using UnityEngine;
using Random = UnityEngine.Random;

public enum MapFarmingKind { Weapon, Gear, Gem, Flask, Bag }

// New desert/cave maps share one occurrence and grade contract across all five item sources.
public static class MapFarmingLoot
{
    private static readonly float[][] Chances =
    {
        new[] { .0045f, .009f, .00225f, .0015f, .00075f },
        new[] { .075f, .135f, .0375f, .0375f, .015f },
        new[] { .525f, 1f, .375f, .15f, .075f }
    };
    private static readonly float[] DesertWeights = { 1.4f, 1.15f, .65f, .8f, .8f };
    private static readonly float[] CaveWeights = { .75f, .8f, 1.65f, 1.25f, 1.25f };
    private static readonly float[][] GradeWeights =
    {
        new[] { 50f, 30f, 15f, 4.5f, .5f, 0f, 0f },
        new[] { 40f, 32f, 20f, 7f, 1f, 0f, 0f },
        new[] { 30f, 32f, 25f, 10f, 2.9f, .1f, 0f },
        new[] { 20f, 28f, 32f, 15f, 4.7f, .3f, 0f },
        new[] { 10f, 20f, 37f, 24f, 8f, .95f, .05f },
        new[] { 5f, 12f, 33f, 32f, 15f, 2.8f, .2f },
        new[] { 0f, 5f, 25f, 40f, 24f, 5.5f, .5f }
    };
    private static readonly float[] EliteWeights = { .5f, .8f, 1.1f, 1.3f, 1.5f, 1.5f, 1.5f };
    private static readonly float[] BossWeights = { 0f, 0f, 1f, 1.5f, 2f, 2f, 2f };

    public static bool Applies(MapInstanceState map) => map != null && map.generationVersion > 0
        && (map.terrain == MapTerrainKind.Desert || map.terrain == MapTerrainKind.Cave);

    private static int SourceIndex(EnemyGradeType rank) => rank == EnemyGradeType.Boss ? 2
        : rank == EnemyGradeType.Elite || rank == EnemyGradeType.GreaterElite ? 1 : 0;

    public static float Chance(MapFarmingKind kind, EnemyGradeType rank, MapInstanceState map)
    {
        int source = SourceIndex(rank);
        float[] chances = Chances[source];
        float chance = chances[(int)kind];
        // Boss gear is the one guaranteed item; terrain weighting must not dilute it.
        if (source != 2)
        {
            var weights = map.terrain == MapTerrainKind.Desert ? DesertWeights : CaveWeights;
            float total = 0, weighted = 0;
            for (int i = 0; i < chances.Length; i++) { total += chances[i]; weighted += chances[i] * weights[i]; }
            chance *= weights[(int)kind] * total / weighted;
        }
        return Mathf.Clamp01(chance * (1f + MapOptionPolicy.Value(map, MapOptionPolicy.ExtraDrop)));
    }

    public static ItemGrade MaximumGrade(ItemGrade mapGrade)
        => mapGrade < ItemGrade.Rare ? ItemGrade.Legendary
            : mapGrade < ItemGrade.Legendary ? ItemGrade.Artifact : ItemGrade.Mythic;

    public static ItemGrade SelectGrade(float roll, ItemGrade mapGrade,
        EnemyGradeType rank = EnemyGradeType.Normal, float rarePercent = 0)
    {
        if (float.IsNaN(roll) || float.IsInfinity(roll) || roll < 0 || roll > 1)
            throw new ArgumentOutOfRangeException(nameof(roll));
        var weights = (float[])GradeWeights[Mathf.Clamp((int)mapGrade, 0, 6)].Clone();
        int source = SourceIndex(rank);
        for (int i = 0; i < weights.Length; i++)
            weights[i] *= source == 2 ? BossWeights[i] : source == 1 ? EliteWeights[i] : 1f;
        return BagFarmingLoot.SelectGrade(roll, weights, (int)MaximumGrade(mapGrade), rarePercent);
    }

    public static ItemData Roll(MapFarmingKind kind, EnemyGradeType rank, MapInstanceState map, float rarePercent)
    {
        float chance = Mathf.Clamp01(CombatDebugSettings.ApplyRunLootChance(Chance(kind, rank, map))
            * (1f + MapRunBuffs.Bonus(MapBuffKind.ItemDrop)));
        if (chance <= 0 || (chance < 1 && Random.value >= chance)) return null;
        ItemGrade grade = SelectGrade(Random.value, map.grade, rank, rarePercent);
        return Create(kind, map.level, grade);
    }

    public static ItemData Extra(ItemData source, EnemyGradeType rank, MapInstanceState map, float rarePercent)
    {
        if (!BagFarmingLoot.Eligible(source?.baseData)) return null;
        ItemGrade grade = SelectGrade(Random.value, map.grade, rank, rarePercent);
        if (source.baseData is ElementGemItemData) return Create(MapFarmingKind.Gem, map.level, grade);
        if (source.baseData is BagItemData) return Create(MapFarmingKind.Bag, map.level, grade);
        return new ItemData(source.baseData, map.level, grade, 1);
    }

    private static ItemData Create(MapFarmingKind kind, int level, ItemGrade grade)
    {
        BaseItemData data;
        switch (kind)
        {
            case MapFarmingKind.Weapon: data = Pick(WeaponLootPolicy.DefinitionsForLevel(level)); break;
            case MapFarmingKind.Gear: data = Pick(GearLootPolicy.DefinitionsForLevel(level)); break;
            case MapFarmingKind.Flask: data = Pick(FlaskLootPolicy.GameplayCatalog); break;
            case MapFarmingKind.Gem:
                data = Pick(ElementGemLootPolicy.Catalog.Where(x => x.fixedGrade == grade
                    && ElementGemItemData.IsAllowed(x.element, x.fixedGrade)).ToArray()); break;
            case MapFarmingKind.Bag:
                data = Pick(BagFarmingLoot.Definitions.Where(x => x.defaultGrade == grade).ToArray()); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
        if (data == null) throw new InvalidOperationException("Missing map farming content: " + kind + "/" + level + "/" + grade);
        return new ItemData(data, OverburstGrowthRules.ClampLevel(level), grade, 1);
    }

    private static T Pick<T>(T[] candidates) where T : BaseItemData
        => candidates.Length > 0 ? candidates[Random.Range(0, candidates.Length)] : null;
}
