using System;
using System.Collections.Generic;
using UnityEngine;

public enum BagStat
{
    InventorySlots, GoldMagnetRadius, ItemPickupDistance, KillExperience,
    CombatGold, ExtraItemDrop, RareGradeWeight
}

[Serializable]
public sealed class BagStatRoll
{
    public BagStat stat;
    public List<WeaponGradeStarType> stars = new List<WeaponGradeStarType>();
    public float Weight
    {
        get
        {
            float value = 0f;
            if (stars != null) foreach (var star in stars)
                value += star == WeaponGradeStarType.Red ? -1f : star == WeaponGradeStarType.Yellow ? 2f
                    : star == WeaponGradeStarType.Green ? 1.5f : 1f;
            return value;
        }
    }
}

[Serializable]
public sealed class BagInstanceState
{
    public int version;
    public int seed;
    public List<BagStatRoll> rows = new List<BagStatRoll>();
}

public static class BagQuality
{
    public const int Version = 1;
    public const int InventoryCapacity = 48;
    public const int RowCount = 4;
    public const int MaxStarsPerRow = 6;
    public const int RewardScale = 10000;
    private static readonly int[] SlotBases = { 5, 6, 7, 8, 9, 11, 13, 15, 17, 19 };

    public static int Seed(string identity)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in identity ?? "") { hash ^= c; hash *= 16777619; }
            return (int)(hash ^ 0x42414701u);
        }
    }

    // Cursed is accepted only to preserve legacy saved identities; new loot excludes it.
    public static BagInstanceState Roll(ItemGrade grade, int seed, bool excludeExperience = false)
    {
        if (!Enum.IsDefined(typeof(ItemGrade), grade)) throw new ArgumentOutOfRangeException(nameof(grade));
        var rng = new System.Random(seed);
        var result = new BagInstanceState { version = Version, seed = seed };
        result.rows.Add(new BagStatRoll { stat = BagStat.InventorySlots });
        var pool = new List<BagStat>();
        for (int i = 1; i <= (int)BagStat.RareGradeWeight; i++)
            if (!excludeExperience || (BagStat)i != BagStat.KillExperience) pool.Add((BagStat)i);
        while (result.rows.Count < RowCount)
        {
            int selected = rng.Next(pool.Count);
            result.rows.Add(new BagStatRoll { stat = pool[selected] });
            pool.RemoveAt(selected);
        }
        int guaranteed = MainMinimum(grade);
        for (int i = 0; i < guaranteed; i++) result.rows[0].stars.Add(Color(grade, rng));
        int total = WeaponGradeStatRoller.GetMeleePositiveStarCount(grade);
        for (int i = guaranteed; i < total; i++)
        {
            var weights = new float[RowCount];
            float sum = 0;
            for (int r = 0; r < RowCount; r++)
            {
                int count = result.rows[r].stars.Count;
                weights[r] = count >= MaxStarsPerRow ? 0 : (r == 0 ? 1.2f : 1f) / (1f + .65f * count);
                sum += weights[r];
            }
            float choice = (float)rng.NextDouble() * sum;
            for (int r = 0; r < RowCount; r++)
                if ((choice -= weights[r]) < 0) { result.rows[r].stars.Add(Color(grade, rng)); break; }
        }
        for (int i = 0; i < WeaponGradeStatRoller.GetMeleeNegativeStarCount(grade); i++)
        {
            var available = new List<int>();
            for (int r = 0; r < RowCount; r++) if (result.rows[r].stars.Count < MaxStarsPerRow) available.Add(r);
            result.rows[available[rng.Next(available.Count)]].stars.Add(WeaponGradeStarType.Red);
        }
        return result;
    }

    public static bool IsValid(BagInstanceState state, ItemGrade grade)
    {
        if (state == null || state.version != Version || !Enum.IsDefined(typeof(ItemGrade), grade)
            || state.rows == null || state.rows.Count != RowCount || state.rows[0] == null
            || state.rows[0].stat != BagStat.InventorySlots) return false;
        int positive = 0, negative = 0, mainPositive = 0;
        var seen = new HashSet<BagStat>();
        for (int i = 0; i < RowCount; i++)
        {
            var row = state.rows[i];
            if (row == null || !Enum.IsDefined(typeof(BagStat), row.stat) || !seen.Add(row.stat)
                || row.stars == null || row.stars.Count > MaxStarsPerRow) return false;
            foreach (var star in row.stars)
                if (star == WeaponGradeStarType.Red) negative++;
                else if (star == WeaponGradeStarType.White || star == WeaponGradeStarType.Green || star == WeaponGradeStarType.Yellow)
                { positive++; if (i == 0) mainPositive++; }
                else return false;
        }
        return mainPositive >= MainMinimum(grade)
            && positive == WeaponGradeStatRoller.GetMeleePositiveStarCount(grade)
            && negative == WeaponGradeStatRoller.GetMeleeNegativeStarCount(grade);
    }

    public static int BaseSlots(int level) => SlotBases[(OverburstGrowthRules.ClampLevel(level) - 1) / 10];
    public static int AdditionalSlots(int level, BagInstanceState state)
        => BaseSlots(level) + Mathf.Max(0, Mathf.FloorToInt(state?.rows?[0]?.Weight ?? 0));
    public static int AdditionalSlots(ItemData item)
        => item?.baseData is BagItemData ? AdditionalSlots(item.level, item.bagState) : 0;
    public static float Value(int level, BagStatRoll row, bool baseOnly = false)
    {
        if (row == null) return 0;
        float w = baseOnly ? 0 : row.Weight;
        switch (row.stat)
        {
            case BagStat.InventorySlots: return BaseSlots(level) + Mathf.Max(0, Mathf.FloorToInt(w));
            case BagStat.GoldMagnetRadius: return Mathf.Max(0, 20 + 5 * w);
            case BagStat.ItemPickupDistance: return Mathf.Max(0, 10 + 2.5f * w);
            case BagStat.KillExperience: return Mathf.Max(0, 2 + .5f * w);
            case BagStat.CombatGold: return Mathf.Max(0, 3 + w);
            case BagStat.ExtraItemDrop: return Mathf.Max(0, 1 + .5f * w);
            case BagStat.RareGradeWeight: return Mathf.Max(0, 2 + .5f * w);
            default: return 0;
        }
    }
    public static float EquippedBonus(BagStat stat)
    {
        float sum = 0;
        foreach (var item in PlayerAccountInventoryService.Loadout.Bags)
            if (item?.baseData is BagItemData && IsValid(item.bagState, item.grade))
                foreach (var row in item.bagState.rows) if (row.stat == stat) sum += Value(item.level, row);
        return sum;
    }
    public static long BonusUnits(int amount, float percent)
        => checked((long)amount * Mathf.RoundToInt(Mathf.Max(0, percent) * 100));
    public static int ApplyReward(int amount, long bonusUnits, int carry, out int nextCarry)
    {
        long units = checked(bonusUnits + carry);
        nextCarry = (int)(units % RewardScale);
        return checked(amount + (int)(units / RewardScale));
    }
    private static int MainMinimum(ItemGrade grade)
        => grade == ItemGrade.Common ? 0 : grade == ItemGrade.Uncommon ? 1 : grade == ItemGrade.Rare ? 2
            : grade == ItemGrade.Artifact || grade == ItemGrade.Mythic ? 4 : 3;
    private static WeaponGradeStarType Color(ItemGrade grade, System.Random rng)
    {
        WeaponGradeStatRoller.GetMeleePositiveStarChances(grade, out float white, out float green, out _);
        double value = rng.NextDouble();
        return value < white ? WeaponGradeStarType.White : value < white + green ? WeaponGradeStarType.Green : WeaponGradeStarType.Yellow;
    }
}
