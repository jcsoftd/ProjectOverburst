using UnityEngine;

// The parry preview reads the same strike radius and origin used by live damage.
public static class EnemyAttackThreatGeometry
{
    // 2026-09-30 패링 타격감: 강공만 더 넓고 넓은 각도로 친다. 일반 예고 공격은 기존 추가량 유지.
    private const float EliteExtra = 1.10f, StandardExtra = .75f;
    private const float EliteStrongExtra = 1.90f, StandardStrongExtra = 1.40f;
    private const float StrongArcAngle = 150f;

    private enum ThreatTier { None, Standard, Elite }

    private static ThreatTier ResolveTier(EnemyActor actor)
    {
        if (actor == null) return ThreatTier.None;
        EnemyRank rank = actor.GetComponent<EnemyRank>();
        if (rank != null && rank.GradeType == EnemyGradeType.Boss) return ThreatTier.None;
        if (rank != null && (rank.Rank == EnemyRankType.Elite
            || rank.GradeType == EnemyGradeType.GreaterElite))
            return ThreatTier.Elite;
        EnemyMovementReaction reaction = actor.GetComponent<EnemyMovementReaction>();
        return reaction != null && reaction.HitWeightProfile != null
            && reaction.HitWeightProfile.Weight == EnemyHitWeight.Standard
            ? ThreatTier.Standard : ThreatTier.None;
    }

    public static float ResolveRadius(EnemyActor actor, EnemyAbilityDefinition ability)
    {
        if (ability == null) return 0f;
        float radius = ability.HitRadius;
        bool strong = ability.IsTelegraphedStrongAttack;
        switch (ResolveTier(actor))
        {
            case ThreatTier.Elite: return radius + (strong ? EliteStrongExtra : EliteExtra);
            case ThreatTier.Standard: return radius + (strong ? StandardStrongExtra : StandardExtra);
            default: return radius;
        }
    }

    // Damage, parry preview and the ground telegraph must all read this angle.
    public static float ResolveHitAngle(EnemyActor actor, EnemyAbilityDefinition ability)
    {
        if (ability == null) return 0f;
        float angle = ability.HitAngle;
        if (angle >= 359.9f || !ability.IsTelegraphedStrongAttack
            || ResolveTier(actor) == ThreatTier.None) return angle;
        return Mathf.Max(angle, StrongArcAngle);
    }

    // Cheap, physics-free estimate for the parry glint and player cue. The real
    // parry and damage still use WouldHit.
    public static bool IsLikelyThreatTo(EnemyActor actor, EnemyAbilityDefinition ability,
        Vector3 center, Vector3 targetPosition)
    {
        if (actor == null || ability == null) return false;
        Vector3 delta = targetPosition - center; delta.y = 0f;
        float reach = ability.ExecutionMode == EnemyAbilityExecutionMode.Charge
            ? ResolveStartRange(actor, ability) + ResolveRadius(actor, ability)
            : ResolveRadius(actor, ability);
        reach += .6f;
        if (delta.sqrMagnitude > reach * reach) return false;
        float angle = ResolveHitAngle(actor, ability);
        if (angle >= 359.9f || delta.sqrMagnitude < .09f
            || ability.ExecutionMode == EnemyAbilityExecutionMode.AreaSlam) return true;
        Vector3 forward = actor.transform.forward; forward.y = 0f;
        return Vector3.Angle(forward, delta) <= angle * .5f + 15f;
    }

    // Reach affects when a windup may begin; the actual hit uses ResolveRadius.
    // Projectile and boss ranges remain authored values.
    public static float ResolveStartRange(EnemyActor actor, EnemyAbilityDefinition ability)
    {
        if (ability == null) return 0f;
        if (ability.ExecutionMode == EnemyAbilityExecutionMode.Projectile
            || ability.ExecutionMode == EnemyAbilityExecutionMode.DirectTarget)
            return ability.Range;
        float extraRadius = ResolveRadius(actor, ability) - ability.HitRadius;
        return ability.Range + extraRadius * .6f;
    }

    public static bool MatchesUseConditions(EnemyActor actor, EnemyAbilityDefinition ability,
        float distance, float health)
    {
        return ability != null && distance >= ability.MinimumRange
            && distance <= ResolveStartRange(actor, ability)
            && health >= ability.MinimumSelfHealthNormalized
            && health <= ability.MaximumSelfHealthNormalized;
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
