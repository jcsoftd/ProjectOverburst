using UnityEngine;

public readonly struct MeleeDamageRequest
{
    public readonly IDamageable Target;
    public readonly float Damage;
    public readonly AttackImpactData Impact;
    public readonly float HitStunDuration;
    public readonly float CriticalChance;
    public readonly float CriticalDamageMultiplier;
    public readonly Vector3 HitPoint;
    public readonly GameObject Source;
    public readonly Vector3 Direction;
    public readonly float Knockback;
    public readonly bool SuppressDefaultHitVfx;
    public readonly WeaponElement Element;
    public readonly string SourceWeaponRuntimeInstanceId;
    public readonly int SourceAttackSequenceId;
    public readonly int SourceAttackPhaseIndex;
    public readonly PlayerAttackKind AttackKind;

    public MeleeDamageRequest(
        IDamageable target,
        float damage,
        AttackImpactData impact,
        float hitStunDuration,
        float criticalChance,
        float criticalDamageMultiplier,
        Vector3 hitPoint,
        GameObject source,
        Vector3 direction,
        float knockback,
        bool suppressDefaultHitVfx = false,
        WeaponElement element = WeaponElement.None,
        string sourceWeaponRuntimeInstanceId = "",
        int sourceAttackSequenceId = 0,
        PlayerAttackKind attackKind = PlayerAttackKind.Weak,
        int sourceAttackPhaseIndex = 0)
    {
        Target = target;
        Damage = damage;
        Impact = impact;
        HitStunDuration = hitStunDuration;
        CriticalChance = criticalChance;
        CriticalDamageMultiplier = criticalDamageMultiplier;
        HitPoint = hitPoint;
        Source = source;
        Direction = direction;
        Knockback = knockback;
        SuppressDefaultHitVfx = suppressDefaultHitVfx;
        Element = element;
        SourceWeaponRuntimeInstanceId = sourceWeaponRuntimeInstanceId ?? string.Empty;
        SourceAttackSequenceId = sourceAttackSequenceId;
        SourceAttackPhaseIndex = sourceAttackPhaseIndex;
        AttackKind = attackKind;
    }
}

public readonly struct MeleeDamageResult
{
    public readonly CombatHealth TargetHealth;
    public readonly int AttemptedDamage;
    public readonly float ActualDamage;
    public readonly bool IsCritical;

    public MeleeDamageResult(CombatHealth targetHealth, int attemptedDamage, float actualDamage, bool isCritical)
    {
        TargetHealth = targetHealth;
        AttemptedDamage = attemptedDamage;
        ActualDamage = actualDamage;
        IsCritical = isCritical;
    }
}

public static class MeleeDamageResolver
{
    public static MeleeDamageResult Apply(MeleeDamageRequest request)
    {
        if (request.Target == null)
            return default;

        float criticalChance = Mathf.Clamp(request.CriticalChance, 0f, 100f);
        bool isCritical = criticalChance > 0f && Random.value * 100f < criticalChance;
        AttackImpactData impact = request.Impact;
        float damage = Mathf.Max(0f, request.Damage);
        if (isCritical)
            damage *= Mathf.Max(1f, request.CriticalDamageMultiplier);

        int damageAmount = Mathf.Max(1, Mathf.RoundToInt(damage));
        CombatHealth targetHealth = request.Target as CombatHealth;
        float hpBeforeHit = targetHealth != null ? targetHealth.CurrentHp : -1f;

        HitReactionData hitReaction = impact.overrideTargetReaction
            ? new HitReactionData(
                true,
                Mathf.Max(0f, request.HitStunDuration),
                Mathf.Max(0f, impact.knockbackReactionDuration))
            : default;

        DamageInfo info = new DamageInfo(
            damageAmount,
            request.HitPoint,
            request.Source,
            request.Direction,
            Mathf.Max(0f, request.Knockback),
            isCritical,
            impact.triggersOnHitEffects,
            false,
            hitReaction,
            request.SuppressDefaultHitVfx,
            request.Element,
            request.SourceWeaponRuntimeInstanceId,
            ElementalReactionType.None,
            request.SourceAttackSequenceId,
            request.Element == WeaponElement.None ? request.AttackKind
                : request.AttackKind | PlayerAttackKind.Elemental,
            request.SourceAttackPhaseIndex);

        request.Target.TakeDamage(info);
        float actualDamage = targetHealth != null && hpBeforeHit >= 0f
            ? Mathf.Max(0f, hpBeforeHit - targetHealth.CurrentHp)
            : damageAmount;

        return new MeleeDamageResult(targetHealth, damageAmount, actualDamage, isCritical);
    }
}
