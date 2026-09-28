using UnityEngine;

// Shared authored balance. Catalog ranges choose appearances; instance levels choose stats.
public static class OverburstCombatBalance
{
    public const int ItemBalanceVersion = 1;
    public const float FinalCriticalChance = 65f;
    public const float FinalCriticalDamage = 3f;
    public const float GreatswordWeakDamage = .35f;
    public const float EmptyHeavyDamage = .30f;
    public const float FullHeavyDamage = 2f;

    public static float RoundStat(float value) => (float)System.Math.Round(value, System.MidpointRounding.AwayFromZero);

    public static float GearBase(GearKind kind, int itemLevel)
        => OverburstBalanceTable.Current.GearBase(kind, itemLevel);

    public static GearStat MainStat(GearKind kind)
        => kind == GearKind.Helmet ? GearStat.MaxHealth
            : kind == GearKind.Chest || kind == GearKind.Boots ? GearStat.Armor
            : kind == GearKind.Gloves ? GearStat.CriticalChance
            : kind == GearKind.Necklace ? GearStat.CriticalDamage : GearStat.Attack;

    public static float ReferenceHealth(int level) => Interpolate(level, 0);
    public static float ReferenceArmor(int level) => Interpolate(level, 1);
    public static float ReferenceExpectedHit(int level) => Interpolate(level, 2);
    public static float ReferenceEffectiveHealth(int level)
        => ReferenceHealth(level) * (1f + ReferenceArmor(level) / 100f);

    private static float Interpolate(int level, int stat)
    {
        level = OverburstGrowthRules.ClampLevel(level);
        int low = level < 10 ? 1 : level / 10 * 10;
        int high = level < 10 ? 10 : Mathf.Min(100, low + 10);
        return high == low ? ReferenceNode(low, stat)
            : Mathf.Lerp(ReferenceNode(low, stat), ReferenceNode(high, stat), (level - low) / (float)(high - low));
    }

    private static float ReferenceNode(int level, int stat)
    {
        if (stat == 0) return 100f + OverburstGrowthRules.PlayerHealthBonus(level) + GearBase(GearKind.Helmet, level);
        if (stat == 1) return OverburstGrowthRules.PlayerArmorBonus(level)
            + GearBase(GearKind.Chest, level) + GearBase(GearKind.Boots, level);
        float damage = RoundStat((20f * OverburstGrowthRules.ItemFactor(level)
            + 2f * GearBase(GearKind.Earring, level)) * OverburstGrowthRules.PlayerAttackFactor(level));
        float crit = Mathf.Clamp(.15f + .10f + GearBase(GearKind.Gloves, level) / 100f, 0f, FinalCriticalChance / 100f);
        float critDamage = Mathf.Min(FinalCriticalDamage, 1.6f + GearBase(GearKind.Necklace, level) / 100f);
        return damage * (1f + crit * (critDamage - 1f));
    }
}
