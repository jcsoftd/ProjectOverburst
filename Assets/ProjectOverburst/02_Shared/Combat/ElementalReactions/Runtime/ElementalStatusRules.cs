using UnityEngine;

public readonly struct ElementalStatusRule
{
    public readonly WeaponElement Element;
    public readonly int MaxStacks;
    public readonly float Duration;
    public readonly float TickInterval;
    public readonly float TickDamageCoefficient;
    public bool HasDamageTicks { get { return TickInterval > 0f && TickDamageCoefficient > 0f; } }

    public ElementalStatusRule(WeaponElement element, int maxStacks, float duration, float tickInterval, float tickDamageCoefficient)
    {
        Element = element;
        MaxStacks = Mathf.Max(1, maxStacks);
        Duration = Mathf.Max(0f, duration);
        TickInterval = Mathf.Max(0f, tickInterval);
        TickDamageCoefficient = Mathf.Max(0f, tickDamageCoefficient);
    }
}

public static class ElementalStatusRules
{
    public const float WetMoveSlow = 0.10f;
    public const float ChilledSlowPerStack = 0.04f;
    public const float MaxMoveSlow = 0.30f;
    public const float MaxActionSlow = 0.20f;

    private static readonly ElementalStatusRule Burning = new ElementalStatusRule(WeaponElement.Fire, 5, 5f, 1f, 0.02f);
    private static readonly ElementalStatusRule Wet = new ElementalStatusRule(WeaponElement.Water, 1, 6f, 0f, 0f);
    private static readonly ElementalStatusRule Chilled = new ElementalStatusRule(WeaponElement.Ice, 5, 5f, 0f, 0f);
    private static readonly ElementalStatusRule Shocked = new ElementalStatusRule(WeaponElement.Electric, 3, 4f, 1.5f, 0.06f);

    public static bool TryGetRule(WeaponElement element, out ElementalStatusRule rule)
    {
        switch (element)
        {
            case WeaponElement.Fire: rule = Burning; return true;
            case WeaponElement.Water: rule = Wet; return true;
            case WeaponElement.Ice: rule = Chilled; return true;
            case WeaponElement.Electric: rule = Shocked; return true;
            default: rule = default; return false;
        }
    }

    public static float ResolveControlEffectMultiplier(EnemyRank enemyRank)
    {
        return enemyRank != null
            ? ResolveControlEffectMultiplier(enemyRank.Rank)
            : 0.5f; // 등급 누락 저항
    }

    public static float ResolveControlEffectMultiplier(EnemyRankType rank)
    {
        switch (rank)
        {
            case EnemyRankType.Normal: return 1f;
            case EnemyRankType.Elite: return 0.5f;
            default: return 0.5f; // 미식별 저항
        }
    }

    public static void ResolveSpeedMultipliers(
        bool wetActive,
        int chilledStacks,
        float controlEffectMultiplier,
        out float moveSpeedMultiplier,
        out float actionSpeedMultiplier)
    {
        int resolvedChilledStacks = Mathf.Clamp(chilledStacks, 0, Chilled.MaxStacks);
        float chilledSlow = resolvedChilledStacks * ChilledSlowPerStack;
        float rawMoveSlow = Mathf.Min(MaxMoveSlow, (wetActive ? WetMoveSlow : 0f) + chilledSlow);
        float rawActionSlow = Mathf.Min(MaxActionSlow, chilledSlow);
        float resistanceScale = Mathf.Clamp01(controlEffectMultiplier);
        moveSpeedMultiplier = Mathf.Clamp01(1f - rawMoveSlow * resistanceScale);
        actionSpeedMultiplier = Mathf.Clamp01(1f - rawActionSlow * resistanceScale);
    }

    public static float ResolveTickDamage(float lastActualDirectDamage, float tickDamageCoefficient, int stackCount)
    {
        return Mathf.Max(0f, lastActualDirectDamage)
            * Mathf.Max(0f, tickDamageCoefficient)
            * Mathf.Max(0, stackCount);
    }
}
