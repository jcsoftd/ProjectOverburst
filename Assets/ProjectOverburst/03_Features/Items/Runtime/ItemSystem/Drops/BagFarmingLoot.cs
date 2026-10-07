using System;
using System.Linq;
using UnityEngine;
using Overburst.Persistence;

public static class BagFarmingLoot
{
    private static int localGoldCarry;
    private static BagItemData[] bagCatalog;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { localGoldCarry = 0; bagCatalog = null; }

    public static int CombatGoldAmount(int amount, float percent)
    {
        if (percent <= 0) return amount;
        var session = AccountGameplaySession.Current;
        if (session != null)
        {
            try { return session.RollCombatGoldAmount(amount, percent); }
            catch (System.IO.IOException error) { Debug.LogError("가방 골드 보너스를 확정하지 못했습니다: " + error.Message); return amount; }
        }
        int value = BagQuality.ApplyReward(amount, BagQuality.BonusUnits(amount, percent), localGoldCarry, out int carry);
        localGoldCarry = carry;
        return value;
    }
    public static bool Eligible(BaseItemData data)
        => data is ElementGemItemData || data is WeaponItemData || data is GearItemData || data is BagItemData || data is FlaskItemData;

    // The map's existing bias is converted to weights first; the bag then multiplies Rare+ weights.
    public static ItemGrade SelectGrade(float roll, float[] weights, int maximum, float rarePercent, float mapBias = 0)
    {
        maximum = Mathf.Clamp(maximum, 0, (int)ItemGrade.Mythic);
        float original = 0, total = 0, lower = 0;
        for (int i = 0; i <= maximum; i++) original += weights[i];
        if (original <= 0) throw new ArgumentException("Empty farming grade distribution.");
        float bias = Mathf.Clamp(mapBias, 0, .999999f);
        var adjusted = new float[maximum + 1];
        for (int i = 0; i <= maximum; i++)
        {
            float upper = lower + weights[i] / original;
            float mass = Mathf.Max(0, upper - Mathf.Max(lower, bias)) / (1 - bias);
            adjusted[i] = mass * (i >= (int)ItemGrade.Rare ? 1 + Mathf.Max(0, rarePercent) * .01f : 1);
            total += adjusted[i];
            lower = upper;
        }
        float pick = Mathf.Clamp01(roll) * total;
        for (int i = 0; i <= maximum; i++) if ((pick -= adjusted[i]) < 0) return (ItemGrade)i;
        return (ItemGrade)maximum;
    }

    public static bool TryTableGrade(ItemGrade minimum, ItemGrade maximum, bool weighted, float rarePercent, out ItemGrade grade)
    {
        var weights = new[] { .4f, .25f, .15f, .1f, .05f, .03f, .02f };
        int min = Mathf.Min((int)minimum, (int)maximum), max = Mathf.Min((int)ItemGrade.Mythic, Mathf.Max((int)minimum, (int)maximum));
        if (min > max) { grade = default; return false; }
        // Match the previous VTP roll + uniform fallback for grade-restricted authored entries.
        float rejected = 0;
        int allowed = max - min + 1;
        if (weighted) for (int i = 0; i < weights.Length; i++) if (i < min || i > max) rejected += weights[i];
        for (int i = 0; i < weights.Length; i++)
            weights[i] = i < min || i > max ? 0 : weighted ? weights[i] + rejected / allowed : 1;
        grade = SelectGrade(UnityEngine.Random.value, weights, max, rarePercent);
        return true;
    }

    public static ItemData RollBag(EnemyRank rank, int level, ItemGrade mapGrade, float rarePercent)
    {
        var definitions = bagCatalog;
        if (definitions == null)
        {
            var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
            definitions = bagCatalog = registry?.Entries.Select(x => x.asset).OfType<BagItemData>()
                .Where(x => ItemGradeAvailabilityPolicy.IsEnabled(x.defaultGrade)).ToArray();
        }
        if (definitions == null || definitions.Length == 0) return null;
        bool boss = rank != null && rank.GradeType == EnemyGradeType.Boss;
        bool elite = rank != null && rank.GradeType != EnemyGradeType.Normal;
        float chance = CombatDebugSettings.ApplyRunLootChance(boss ? .05f : elite ? .02f : .005f);
        float effectiveChance = Mathf.Min(1, chance * (1 + MapRunBuffs.Bonus(MapBuffKind.ItemDrop)));
        float chanceRoll = UnityEngine.Random.value;
        if (effectiveChance <= 0f || (effectiveChance < 1f && chanceRoll >= effectiveChance)) return null;
        ItemGrade grade = FlaskLootPolicy.SelectGrade(UnityEngine.Random.value, level, boss, elite, rarePercent,
            MapOptionPolicy.HighGradeRollBias(mapGrade));
        var data = definitions.FirstOrDefault(x => x.defaultGrade == grade) ?? definitions[0];
        return new ItemData(data, level, grade);
    }

    public static ItemData Extra(ItemData source, EnemyRank rank, int level, ItemGrade mapGrade, float rarePercent)
    {
        if (!Eligible(source?.baseData)) return null;
        if (source.baseData is ElementGemItemData) return ElementGemLootPolicy.CreateRoll(level,rank!=null?rank.GradeType:EnemyGradeType.Normal,mapGrade,rarePercent);
        ItemGrade grade = FlaskLootPolicy.SelectGrade(UnityEngine.Random.value, level,
            rank != null && rank.GradeType == EnemyGradeType.Boss,
            rank != null && rank.GradeType != EnemyGradeType.Normal, rarePercent, MapOptionPolicy.HighGradeRollBias(mapGrade));
        return new ItemData(source.baseData, level, grade, 1);
    }
}
