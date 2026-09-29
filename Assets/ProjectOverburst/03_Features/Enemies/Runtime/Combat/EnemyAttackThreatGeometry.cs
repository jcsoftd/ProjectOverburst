using UnityEngine;

// The parry preview reads the same strike radius and origin used by live damage.
public static class EnemyAttackThreatGeometry
{
    public static float ResolveRadius(EnemyActor actor, EnemyAbilityDefinition ability)
    {
        if (ability == null) return 0f;
        float radius = ability.HitRadius;
        if (actor == null) return radius;
        EnemyRank rank = actor.GetComponent<EnemyRank>();
        if (rank != null && rank.GradeType == EnemyGradeType.Boss) return radius;
        if (rank != null && (rank.Rank == EnemyRankType.Elite
            || rank.GradeType == EnemyGradeType.GreaterElite))
            return radius + .40f;

        EnemyMovementReaction reaction = actor.GetComponent<EnemyMovementReaction>();
        if (reaction != null && reaction.HitWeightProfile != null
            && reaction.HitWeightProfile.Weight == EnemyHitWeight.Standard)
            return radius + .25f;
        return radius;
    }

    public static bool WouldHit(EnemyActor actor, EnemyAbilityDefinition ability,
        CombatTarget target)
    {
        if (actor == null || ability == null || target == null || !target.IsAlive)
            return false;
        if (ability.ExecutionMode == EnemyAbilityExecutionMode.Charge)
            return actor.GetComponent<EnemyThemeSpecialExecutor>()?.WouldChargeHit(ability, target) == true;
        if (ability.ExecutionMode == EnemyAbilityExecutionMode.MeleeArc
            || ability.ExecutionMode == EnemyAbilityExecutionMode.AreaSlam)
            return actor.Melee != null && actor.Melee.WouldAbilityHitTarget(ability, target);
        return false;
    }
}
