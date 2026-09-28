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

    // Helmet HP, chest armor, glove crit points, boots armor, earring attack, necklace crit damage points.
    private static readonly float[,] GearTiers = {
        {40,10,1,5,2,5}, {100,16,1.5f,8,3,8}, {180,24,2,12,5,12},
        {280,32,3,16,7,16}, {400,44,4,22,9,20}, {550,56,5,28,12,25},
        {720,70,6,35,15,30}, {920,86,7,43,18,35}, {1150,102,8,51,21,40},
        {1400,120,10,60,24,45}
    };

    public static float RoundStat(float value) => (float)System.Math.Round(value, System.MidpointRounding.AwayFromZero);

    public static float GearBase(GearKind kind, int itemLevel)
        => GearTiers[(OverburstGrowthRules.ClampLevel(itemLevel) - 1) / 10, (int)kind];

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
        float crit = (.15f + .10f + GearBase(GearKind.Gloves, level) / 100f);
        float critDamage = 1.6f + GearBase(GearKind.Necklace, level) / 100f;
        return damage * (1f + crit * (critDamage - 1f));
    }
}
