using UnityEngine;

public static class FlaskCombatModifiers
{
    public static float Bonus(GameObject actor, FlaskEffect effect)
    {
        var flasks = actor != null ? actor.GetComponentInParent<PlayerFlaskController>() : null;
        return flasks != null && flasks.isActiveAndEnabled ? flasks.Bonus(effect) : 0f;
    }

    // Snapshot at attack commit. Inventory comparisons retain the weapon's own stats.
    public static WeaponFinalStats Apply(WeaponFinalStats stats, GameObject actor)
    {
        stats.damage *= 1f + Bonus(actor, FlaskEffect.DirectDamage);
        stats.meleeAttackSpeedMultiplier = Mathf.Min(WeaponGradeStatRoller.MaximumMeleeAttackSpeedMultiplier,
            stats.meleeAttackSpeedMultiplier + Bonus(actor, FlaskEffect.AttackSpeed));
        float previous = Mathf.Max(.01f, stats.meleeAttackRangeScale);
        stats.meleeAttackRangeScale = Mathf.Min(WeaponGradeStatRoller.MaximumMeleeAttackRangeMultiplier,
            previous + Bonus(actor, FlaskEffect.AttackRadius));
        stats.range *= stats.meleeAttackRangeScale / previous;
        stats.critChance = Mathf.Min(WeaponGradeStatRoller.MaximumMeleeCriticalChance,
            stats.critChance + Bonus(actor, FlaskEffect.CritChance) * 100f);
        stats.critDamageMultiplier = Mathf.Min(WeaponGradeStatRoller.MaximumMeleeCriticalDamageMultiplier,
            stats.critDamageMultiplier + Bonus(actor, FlaskEffect.CritDamage));
        stats.knockback *= 1f + Bonus(actor, FlaskEffect.OutgoingImpact);
        return stats;
    }

    public static void Incoming(CombatHealth target, ref DamageInfo info, ref float damage)
    {
        var flasks = target.GetComponent<PlayerFlaskController>();
        if (flasks == null || !flasks.isActiveAndEnabled) return;
        float reduction = flasks.Bonus(info.isDamageOverTime ? FlaskEffect.DotDamageReduction : FlaskEffect.DirectDamageReduction);
        damage *= 1f - Mathf.Clamp(reduction, 0f, .6f);
        info.damage = damage;
        info.knockback *= 1f - Mathf.Clamp(flasks.Bonus(FlaskEffect.IncomingImpactReduction), 0f, .7f);
    }

    public static void ConfirmedHit(CombatHealth target, DamageInfo info, float actualDamage)
    {
        if (actualDamage <= 0f || info.source == null || target.GetComponent<TrainingDummy>() != null) return;
        var sourceTeam = info.source.GetComponentInParent<CombatTarget>();
        var targetTeam = target.GetComponent<CombatTarget>();
        if (sourceTeam == null || targetTeam == null || sourceTeam.Team == targetTeam.Team) return;
        var attacker = info.source.GetComponentInParent<PlayerFlaskController>();
        var defender = target.GetComponent<PlayerFlaskController>();
        if (target.CurrentHp <= 0f)
        {
            var rank = target.GetComponent<EnemyRank>();
            attacker?.GrantBonus(rank != null && rank.GradeType != EnemyGradeType.Normal ? 5f : 1f);
        }
        if (info.isDamageOverTime || !info.triggersOnHitEffects) return;
        if (sourceTeam.GetComponent<TrainingDummy>() != null) return;
        attacker?.ReportCombat();
        defender?.ReportCombat();
    }
}
