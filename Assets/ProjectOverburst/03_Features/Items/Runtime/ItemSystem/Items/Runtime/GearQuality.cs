using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class GearStatRoll
{
    public GearStat stat;
    public List<WeaponGradeStarType> stars = new List<WeaponGradeStarType>();

    public float Weight
    {
        get
        {
            float result = 0f;
            if (stars == null) return result;
            foreach (WeaponGradeStarType star in stars)
                result += star == WeaponGradeStarType.Red ? -1f
                    : star == WeaponGradeStarType.Yellow ? 2f : star == WeaponGradeStarType.Green ? 1.5f : 1f;
            return result;
        }
    }
}

public static class GearQuality
{
    public const int RowCount = 4;
    public const int MaxStarsPerRow = 6;
    private static readonly GearStat[] Pool =
    {
        GearStat.MaxHealth, GearStat.Armor, GearStat.Attack, GearStat.AttackSpeed,
        GearStat.NormalDamage, GearStat.WeakDamage, GearStat.HeavyDamage,
        GearStat.EliteBossDamage, GearStat.ElementalDamage, GearStat.CriticalDamage
    };

    public static List<GearStatRoll> Roll(GearItemData data, ItemGrade grade, int seed)
    {
        if (data == null || grade < ItemGrade.Common || grade > ItemGrade.Cursed)
            throw new ArgumentException("Invalid gear data or grade.");
        var rng = new System.Random(seed);
        var result = new List<GearStatRoll>(RowCount) { new GearStatRoll { stat = data.MainStat } };
        while (result.Count < RowCount)
            result.Add(new GearStatRoll { stat = SelectSecondary(data.kind, result, rng) });
        int total = WeaponGradeStatRoller.GetMeleePositiveStarCount(grade);
        int guaranteed = GuaranteedMainStars(grade);
        for (int i = 0; i < guaranteed; i++)
            result[0].stars.Add(RollColor(grade, rng));
        for (int i = guaranteed; i < total; i++)
        {
            float sum = 0f;
            float[] weights = new float[RowCount];
            for (int row = 0; row < RowCount; row++)
            {
                int count = result[row].stars.Count;
                weights[row] = count >= MaxStarsPerRow ? 0f : (row == 0 ? 1.2f : 1f) / (1f + count * .65f);
                sum += weights[row];
            }
            float choice = (float)rng.NextDouble() * sum;
            int selected = RowCount - 1;
            for (int row = 0; row < RowCount; row++)
            {
                choice -= weights[row];
                if (choice < 0f) { selected = row; break; }
            }
            result[selected].stars.Add(RollColor(grade, rng));
        }
        int negative = WeaponGradeStatRoller.GetMeleeNegativeStarCount(grade);
        for (int i = 0; i < negative; i++)
        {
            var available = new List<int>(RowCount);
            for (int row = 0; row < RowCount; row++)
                if (result[row].stars.Count < MaxStarsPerRow) available.Add(row);
            result[available[rng.Next(available.Count)]].stars.Add(WeaponGradeStarType.Red);
        }
        return result;
    }

    public static bool IsValid(GearItemData data, ItemGrade grade, List<GearStatRoll> rolls)
    {
        if (data == null || rolls == null || rolls.Count != RowCount
            || grade < ItemGrade.Common || grade > ItemGrade.Cursed
            || rolls[0] == null || rolls[0].stat != data.MainStat
            || rolls[0].stars == null || rolls[0].stars.Count < GuaranteedMainStars(grade)) return false;
        int positive = 0, negative = 0;
        var seen = new HashSet<GearStat>();
        for (int i = 0; i < rolls.Count; i++)
        {
            GearStatRoll row = rolls[i];
            if (row == null || row.stars == null || row.stars.Count > MaxStarsPerRow || !seen.Add(row.stat)) return false;
            if (i > 0 && !IsSecondaryAllowed(data.kind, row.stat, rolls, i)) return false;
            foreach (WeaponGradeStarType star in row.stars)
                if (star == WeaponGradeStarType.Red) negative++;
                else if (star == WeaponGradeStarType.White || star == WeaponGradeStarType.Green
                    || star == WeaponGradeStarType.Yellow) positive++;
                else return false;
        }
        return positive == WeaponGradeStatRoller.GetMeleePositiveStarCount(grade)
            && negative == WeaponGradeStatRoller.GetMeleeNegativeStarCount(grade);
    }

    public static float Value(ItemData item, GearStatRoll row)
    {
        if (item == null || row == null || !(item.baseData is GearItemData data)) return 0f;
        float w = row.Weight;
        float f = OverburstGrowthRules.ItemFactor(item.level);
        if (row.stat == data.MainStat && item.gearRolls != null
            && item.gearRolls.Count > 0 && ReferenceEquals(item.gearRolls[0], row))
        {
            return data.kind == GearKind.Gloves
                ? .75f + .015f * (OverburstGrowthRules.ClampLevel(item.level) - 1) + .15f * w
                : data.MainBaseValue * f * (1f + .08f * w);
        }
        switch (row.stat)
        {
            case GearStat.MaxHealth: return (4f + w) * f;
            case GearStat.Armor: return (2f + .5f * w) * f;
            case GearStat.Attack: return (.25f + .05f * w) * f;
            case GearStat.AttackSpeed: return .25f + .15f * w;
            default: return .5f + .25f * w;
        }
    }

    private static int GuaranteedMainStars(ItemGrade grade)
    {
        switch (grade)
        {
            case ItemGrade.Uncommon: return 1;
            case ItemGrade.Rare: return 2;
            case ItemGrade.Epic:
            case ItemGrade.Legendary: return 3;
            case ItemGrade.Artifact:
            case ItemGrade.Mythic: return 4;
            case ItemGrade.Cursed: return 3;
            default: return 0;
        }
    }

    private static GearStat SelectSecondary(GearKind kind, List<GearStatRoll> chosen, System.Random rng)
    {
        float total = 0f;
        foreach (GearStat stat in Pool)
            if (IsSecondaryAllowed(kind, stat, chosen, chosen.Count)) total += Weight(kind, stat);
        float roll = (float)rng.NextDouble() * total;
        foreach (GearStat stat in Pool)
        {
            if (!IsSecondaryAllowed(kind, stat, chosen, chosen.Count)) continue;
            roll -= Weight(kind, stat);
            if (roll < 0f) return stat;
        }
        throw new InvalidOperationException("No valid gear secondary stat.");
    }

    private static bool IsSecondaryAllowed(GearKind kind, GearStat stat, List<GearStatRoll> chosen, int priorCount)
    {
        if (stat == (kind == GearKind.Helmet || kind == GearKind.Necklace ? GearStat.MaxHealth
            : kind == GearKind.Chest || kind == GearKind.Boots ? GearStat.Armor
            : kind == GearKind.Gloves ? GearStat.CriticalChance : GearStat.Attack)) return false;
        bool targetGroup = stat == GearStat.NormalDamage || stat == GearStat.EliteBossDamage;
        bool attackGroup = stat == GearStat.WeakDamage || stat == GearStat.HeavyDamage || stat == GearStat.ElementalDamage;
        for (int i = 0; i < priorCount; i++)
        {
            GearStat existing = chosen[i].stat;
            if (existing == stat) return false;
            if (targetGroup && (existing == GearStat.NormalDamage || existing == GearStat.EliteBossDamage)) return false;
            if (attackGroup && (existing == GearStat.WeakDamage || existing == GearStat.HeavyDamage
                || existing == GearStat.ElementalDamage)) return false;
        }
        return true;
    }

    private static float Weight(GearKind kind, GearStat stat)
    {
        bool survival = stat == GearStat.MaxHealth || stat == GearStat.Armor;
        bool attackMode = stat == GearStat.WeakDamage || stat == GearStat.HeavyDamage || stat == GearStat.ElementalDamage;
        if (kind == GearKind.Helmet || kind == GearKind.Chest || kind == GearKind.Boots)
            return survival ? 3f : attackMode ? 1.5f : 1f;
        if (kind == GearKind.Gloves)
            return stat == GearStat.AttackSpeed || attackMode ? 3f : survival ? 1f : 2f;
        if (kind == GearKind.Earring)
            return stat == GearStat.CriticalDamage || attackMode ? 3f : survival ? 1f : 2f;
        return attackMode || survival ? 2.5f : 1f;
    }

    private static WeaponGradeStarType RollColor(ItemGrade grade, System.Random rng)
    {
        WeaponGradeStatRoller.GetMeleePositiveStarChances(grade, out float white, out float green, out _);
        double value = rng.NextDouble();
        return value < white ? WeaponGradeStarType.White
            : value < white + green ? WeaponGradeStarType.Green : WeaponGradeStarType.Yellow;
    }
}
