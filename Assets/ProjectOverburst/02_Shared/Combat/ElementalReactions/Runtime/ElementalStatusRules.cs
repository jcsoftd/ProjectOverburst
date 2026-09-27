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
    public const float ChilledSlowPerStack = 0.04f;
    public const float MaxMoveSlow = 0.30f;
    public const float MaxActionSlow = 0.20f;

    public static bool TryGetRule(WeaponElement element, out ElementalStatusRule rule)
    {
        if (!OverburstElementRules.IsActive(element))
        {
            rule = default;
            return false;
        }

        OverburstElementTuning tuning = OverburstElementTuning.Current;
        rule = new ElementalStatusRule(element, tuning.maximumStacks, tuning.StatusDuration(element), tuning.TickInterval(element), tuning.TickCoefficient(element));
        return true;
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
        int chilledStacks,
        float controlEffectMultiplier,
        out float moveSpeedMultiplier,
        out float actionSpeedMultiplier)
    {
        int resolvedChilledStacks = Mathf.Clamp(chilledStacks, 0, Mathf.Max(1, OverburstElementTuning.Current.maximumStacks));
        float chilledSlow = resolvedChilledStacks * ChilledSlowPerStack;
        float rawMoveSlow = Mathf.Min(MaxMoveSlow, chilledSlow);
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
