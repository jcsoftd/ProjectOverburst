using System;
using System.Collections.Generic;
using UnityEngine;

public enum FlaskStat { Primary, Secondary, Duration, Efficiency, Capacity }

[Serializable]
public sealed class FlaskStatRoll
{
    public FlaskStat stat;
    public List<WeaponGradeStarType> stars = new List<WeaponGradeStarType>();
    public float Weight
    {
        get
        {
            float result = 0f;
            if (stars == null) return result;
            foreach (WeaponGradeStarType star in stars)
                result += star == WeaponGradeStarType.Yellow ? 2f : star == WeaponGradeStarType.Green ? 1.5f : 1f;
            return result;
        }
    }
}

[Serializable]
public sealed class FlaskInstanceState
{
    public int seed;
    public ItemGrade rolledGrade;
    public MeleeStarDistributionProfile profile;
    public List<FlaskStatRoll> rolls = new List<FlaskStatRoll>();
    public float charge;
    public bool chargeInitialized;
    public int equippedSlot = -1;

    public float Weight(FlaskStat stat)
    {
        if (rolls != null)
            foreach (FlaskStatRoll row in rolls)
                if (row != null && row.stat == stat) return row.Weight;
        return 0f;
    }
}

public readonly struct FlaskStats
{
    public readonly float primary, secondary, duration, cost, capacity;
    public FlaskStats(float p, float s, float d, float c, float max)
    { primary = p; secondary = s; duration = d; cost = c; capacity = max; }

    public static FlaskStats Calculate(FlaskItemData data, FlaskInstanceState state)
    {
        if (data == null) return default;
        float p = state != null ? state.Weight(FlaskStat.Primary) : 0f;
        float s = state != null ? state.Weight(FlaskStat.Secondary) : 0f;
        float d = state != null ? state.Weight(FlaskStat.Duration) : 0f;
        float e = state != null ? state.Weight(FlaskStat.Efficiency) : 0f;
        float c = state != null ? state.Weight(FlaskStat.Capacity) : 0f;
        return new FlaskStats(
            Mathf.Max(0f, data.primaryValue) * (1f + .08f * Mathf.Clamp(p, 0f, 12f)),
            Mathf.Max(0f, data.secondaryValue) * (1f + .08f * Mathf.Clamp(s, 0f, 12f)),
            Mathf.Max(.1f, data.duration) * (1f + .05f * Mathf.Clamp(d, 0f, 12f)),
            Mathf.Max(1f, data.chargeCost) * (1f - .02f * Mathf.Clamp(e, 0f, 12f)),
            Mathf.Max(data.chargeCost, data.chargeCapacity) + 5f * Mathf.Clamp(c, 0f, 12f));
    }
}

public static class FlaskGradeRoller
{
    public const int RowCount = 5;
    public const int MaxStarsPerRow = 6;
    private static readonly WeaponGradeStatType[] weaponMapping =
    {
        WeaponGradeStatType.Damage, WeaponGradeStatType.AttackSpeed, WeaponGradeStatType.AttackRange,
        WeaponGradeStatType.CritChance, WeaponGradeStatType.CritDamage
    };

    public static FlaskInstanceState Roll(ItemGrade grade, int seed)
    {
        if (grade < ItemGrade.Common || grade > ItemGrade.Mythic)
            throw new ArgumentOutOfRangeException(nameof(grade), "물약은 일반~신화 등급만 생성합니다.");
        var rng = new System.Random(seed);
        double profileRoll = rng.NextDouble();
        var state = new FlaskInstanceState { seed = seed, rolledGrade = grade,
            profile = profileRoll < .30 ? MeleeStarDistributionProfile.Focused
                : profileRoll < .75 ? MeleeStarDistributionProfile.DualCore : MeleeStarDistributionProfile.Spread };
        int total = WeaponGradeStatRoller.GetMeleePositiveStarCount(grade);
        int assigned = 0;
        for (int i = 0; i < RowCount; i++)
        {
            var row = new FlaskStatRoll { stat = (FlaskStat)i };
            state.rolls.Add(row);
            int guaranteed = WeaponGradeStatRoller.GetGuaranteedMeleePositiveStarCount(grade, weaponMapping[i]);
            for (int n = 0; n < guaranteed; n++) { row.stars.Add(RollColor(grade, rng)); assigned++; }
        }
        int[] priority = { 0, 1, 2, 3, 4 };
        for (int i = priority.Length - 1; i > 0; i--)
        { int j = rng.Next(i + 1); int value = priority[j]; priority[j] = priority[i]; priority[i] = value; }
        float[] weights = new float[RowCount];
        while (assigned < total)
        {
            float sum = 0f;
            for (int i = 0; i < RowCount; i++)
            {
                int count = state.rolls[i].stars.Count;
                float weight = 1f;
                if (state.profile == MeleeStarDistributionProfile.Focused)
                    weight = i == priority[0] ? 8f : i == priority[1] ? 1.8f : .6f;
                else if (state.profile == MeleeStarDistributionProfile.DualCore)
                    weight = i == priority[0] || i == priority[1] ? 4.5f : 1f;
                else weight = 1f / (1f + count * .65f);
                weights[i] = count >= MaxStarsPerRow ? 0f : weight;
                sum += weights[i];
            }
            if (sum <= 0f) throw new InvalidOperationException("물약 별 배분 공간이 부족합니다.");
            double choice = rng.NextDouble() * sum;
            int selected = RowCount - 1;
            for (int i = 0; i < RowCount; i++)
            { choice -= weights[i]; if (choice < 0d) { selected = i; break; } }
            state.rolls[selected].stars.Add(RollColor(grade, rng));
            assigned++;
        }
        return state;
    }

    public static bool IsValid(FlaskInstanceState state, ItemGrade grade)
    {
        if (state == null || state.rolledGrade != grade || grade < ItemGrade.Common || grade > ItemGrade.Mythic
            || state.rolls == null || state.rolls.Count != RowCount) return false;
        int total = 0;
        for (int i = 0; i < RowCount; i++)
        {
            FlaskStatRoll row = state.rolls[i];
            if (row == null || row.stat != (FlaskStat)i || row.stars == null || row.stars.Count > MaxStarsPerRow
                || row.stars.Count < WeaponGradeStatRoller.GetGuaranteedMeleePositiveStarCount(grade, weaponMapping[i])) return false;
            foreach (WeaponGradeStarType star in row.stars)
                if (star != WeaponGradeStarType.White && star != WeaponGradeStarType.Green && star != WeaponGradeStarType.Yellow) return false;
            total += row.stars.Count;
        }
        return total == WeaponGradeStatRoller.GetMeleePositiveStarCount(grade);
    }

    private static WeaponGradeStarType RollColor(ItemGrade grade, System.Random rng)
    {
        WeaponGradeStatRoller.GetMeleePositiveStarChances(grade, out float white, out float green, out _);
        double r = rng.NextDouble();
        return r < white ? WeaponGradeStarType.White : r < white + green ? WeaponGradeStarType.Green : WeaponGradeStarType.Yellow;
    }
}
