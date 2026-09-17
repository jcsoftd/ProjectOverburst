using UnityEngine;

public static class ElementalReactionRules
{
    public const float VaporizeRadius = 2.5f;
    public const float VaporizeDamageCoefficient = 0.45f;
    public const float FractureDamageCoefficient = 0.3f;
    public const float PlasmaDamageCoefficient = 0.3f;
    public const float PlasmaRadius = 3f;
    public const float PlasmaKnockback = 2.5f;
    public const float FreezeDuration = 3f;
    public const float ShatterDamageCoefficient = 0.4f;
    public const float ChainRadius = 4f;
    public const int ChainMaximumTargetCount = 5;
    public const float ColdChargeDuration = 6f;
    public const float ColdChargeDamageCoefficient = 0.05f;
    public const float ColdChargeRadius = 2.5f;
    public const float ColdChargeKnockback = 2f;
    public const float ChainFallbackDamageMultiplier = 0.5f;
    public const float CircularAreaMinimumDamageMultiplier = 0.3f;
    public static readonly float[] ChainDamageCoefficients = { 1f, 0.2f, 0.15f, 0.1f, 0.05f };

    public static float ResolveVaporizeDamage(float actualDirectDamage)
    {
        return Mathf.Max(0f, actualDirectDamage) * VaporizeDamageCoefficient;
    }

    public static float ResolvePlasmaDamage(float storedDamage)
    {
        return Mathf.Max(0f, storedDamage) * PlasmaDamageCoefficient;
    }

    public static float ResolveFractureDamage(float actualDirectDamage)
    {
        return Mathf.Max(0f, actualDirectDamage) * FractureDamageCoefficient;
    }

    public static float ResolveShatterDamage(float triggerActualDamage)
    {
        return Mathf.Max(0f, triggerActualDamage) * ShatterDamageCoefficient;
    }

    public static float ResolveColdChargeDamage(float triggerActualDamage)
    {
        return Mathf.Max(0f, triggerActualDamage) * ColdChargeDamageCoefficient;
    }

    public static float ResolveCircularAreaDamage(
        float baseDamage,
        Vector3 center,
        Vector3 targetCenter,
        float radius)
    {
        float damage = Mathf.Max(0f, baseDamage);
        if (damage <= 0f || radius <= 0f)
            return damage;

        float radiusSquared = radius * radius;
        float distanceSquared = (targetCenter - center).sqrMagnitude;
        float multiplier = ResolveCircularAreaDamageMultiplier(distanceSquared, radiusSquared);
        return damage * multiplier;
    }

    public static float ResolveCircularAreaDamageMultiplier(float distanceSquared, float radiusSquared)
    {
        if (radiusSquared <= 0f)
            return 1f;

        float normalizedDistanceSquared = Mathf.Clamp01(distanceSquared / radiusSquared);
        return Mathf.Lerp(1f, CircularAreaMinimumDamageMultiplier, normalizedDistanceSquared);
    }
}

public static class ElementalReactionResolver
{
    public static bool TryResolve(
        ElementalBasicStatusSet previousStatuses,
        WeaponElement incomingElement,
        out ElementalReactionType reactionType)
    {
        bool vaporize = (incomingElement == WeaponElement.Fire && previousStatuses.Wet)
            || (incomingElement == WeaponElement.Water && previousStatuses.Burning);
        if (vaporize)
        {
            reactionType = ElementalReactionType.Vaporize; // 임시 최우선 순위
            return true;
        }

        bool thermalFracture = (incomingElement == WeaponElement.Ice && previousStatuses.Burning)
            || (incomingElement == WeaponElement.Fire && previousStatuses.Chilled);
        if (thermalFracture)
        {
            reactionType = ElementalReactionType.ThermalFracture;
            return true;
        }

        bool plasma = (incomingElement == WeaponElement.Electric && previousStatuses.Burning)
            || (incomingElement == WeaponElement.Fire && previousStatuses.Shocked);
        if (plasma)
        {
            reactionType = ElementalReactionType.Plasma;
            return true;
        }

        bool freeze = (incomingElement == WeaponElement.Ice && previousStatuses.Wet)
            || (incomingElement == WeaponElement.Water && previousStatuses.Chilled);
        if (freeze)
        {
            reactionType = ElementalReactionType.Freeze;
            return true;
        }

        bool chainElectricity = (incomingElement == WeaponElement.Water && previousStatuses.Shocked)
            || (incomingElement == WeaponElement.Electric && previousStatuses.Wet);
        if (chainElectricity)
        {
            reactionType = ElementalReactionType.ChainElectricity;
            return true;
        }

        bool coldCharge = (incomingElement == WeaponElement.Ice && previousStatuses.Shocked)
            || (incomingElement == WeaponElement.Electric && previousStatuses.Chilled);
        reactionType = coldCharge ? ElementalReactionType.ColdCharge : ElementalReactionType.None;
        return coldCharge;
    }
}
