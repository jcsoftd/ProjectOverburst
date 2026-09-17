public readonly struct MeleeAttackRuntimeData
{
    public readonly AttackPatternRuntimeData Pattern;
    public readonly float Damage;
    public readonly float Knockback;
    public readonly float HitStunDuration;
    public readonly float VfxScale;
    public readonly float AttackRangeScale;

    public MeleeAttackRuntimeData(
        AttackPatternRuntimeData pattern,
        float damage,
        float knockback,
        float hitStunDuration,
        float vfxScale,
        float attackRangeScale)
    {
        Pattern = pattern;
        Damage = damage;
        Knockback = knockback;
        HitStunDuration = hitStunDuration;
        VfxScale = vfxScale;
        AttackRangeScale = attackRangeScale;
    }
}
