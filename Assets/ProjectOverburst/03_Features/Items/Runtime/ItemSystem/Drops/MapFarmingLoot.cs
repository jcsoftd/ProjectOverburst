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
        new[] { .009f, .018f, .0045f, .003f, .0015f },
        new[] { .15f, .27f, .075f, .075f, .03f },
        new[] { 1f, 1f, .75f, .30f, .15f }
    };
    private static readonly float[] DesertWeights = { 1.4f, 1.15f, .65f, .8f, .8f };
    private static readonly float[] CaveWeights = { .75f, .8f, 1.65f, 1.25f, 1.25f };
    private static readonly float[][] GradeWeights =
    {
        new[] { 25f, 35f, 28f, 10f, 2f, 0f, 0f },
        new[] { 18f, 32f, 32f, 15f, 3f, 0f, 0f },
        new[] { 12f, 28f, 36f, 18f, 5.5f, 0.5f, 0f },
        new[] { 7f, 21f, 38f, 25f, 8f, 1f, 0f },
        new[] { 3f, 12f, 38f, 32f, 13f, 1.8f, 0.2f },
        new[] { 1f, 7f, 32f, 38f, 18f, 3.5f, 0.5f },
        new[] { 0f, 2f, 20f, 42f, 28f, 7f, 1f }
    };
    // Next-map rarity is tuned independently of farming-item rarity.
    private static readonly float[][] MapGradeWeights =
    {
        new[] { 47f, 30f, 17f, 5.3f, 0.7f, 0f, 0f },
        new[] { 37f, 32f, 21.5f, 8.2f, 1.3f, 0f, 0f },
        new[] { 27f, 32f, 26f, 11.2f, 3.65f, 0.15f, 0f },
        new[] { 17f, 28f, 33f, 16.3f, 5.3f, 0.4f, 0f },
        new[] { 8f, 19f, 38f, 25.2f, 8.6f, 1.13f, 0.07f },
        new[] { 3f, 11f, 34f, 33f, 15.5f, 3.2f, 0.3f },
        new[] { 0f, 3f, 25f, 41f, 24.5f, 5.9f, 0.6f },
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

    public static ItemGrade SelectMapGrade(float roll, ItemGrade mapGrade)
    {
        if (float.IsNaN(roll) || float.IsInfinity(roll) || roll < 0 || roll > 1)
            throw new ArgumentOutOfRangeException(nameof(roll));
        return BagFarmingLoot.SelectGrade(roll, MapGradeWeights[Mathf.Clamp((int)mapGrade, 0, 6)],
            (int)MaximumGrade(mapGrade), 0);
    }

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
        float chance = TesterSupportItemData.ApplyDropChance(CombatDebugSettings.ApplyRunLootChance(Chance(kind, rank, map))
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
            case MapFarmingKind.Flask: data = Pick(FlaskLootPolicy.Catalog); break;
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
