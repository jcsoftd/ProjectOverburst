using UnityEngine;

public static class MeleeAttackStatResolver
{
    public static MeleeAttackRuntimeData Resolve(
        WeaponFinalStats weaponStats,
        MeleeWeaponBaseSettings meleeSettings,
        AttackPhaseData phase,
        float attackDamageMultiplier)
    {
        AttackPatternRuntimeData pattern = phase.ResolvePattern(
            weaponStats.range,
            weaponStats.meleeSlashAngle,
            meleeSettings.hitWidth);
        if (pattern.IsThrust)
            pattern = pattern.WithWidthMultiplier(AttackPatternRuntimeData.ThrustWidthMultiplier);

        AttackImpactData impact = phase.impact;
        AttackGeometryData geometry = phase.geometry;

        return new MeleeAttackRuntimeData(
            pattern,
            Mathf.Max(0f, weaponStats.damage)
                * impact.SafeDamageMultiplier
                * Mathf.Max(0f, attackDamageMultiplier),
            Mathf.Max(0f, weaponStats.knockback) * impact.SafeKnockbackMultiplier,
            Mathf.Max(0f, meleeSettings.hitStunDuration) * impact.SafeHitStunMultiplier,
            Mathf.Max(0.01f, meleeSettings.vfxScaleMultiplier)
                * geometry.SafeVfxScaleMultiplier,
            Mathf.Max(0.01f, weaponStats.meleeAttackRangeScale));
    }
}
