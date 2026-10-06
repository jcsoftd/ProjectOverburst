using UnityEngine;

// The parry preview reads the same strike radius and origin used by live damage.
public static partial class EnemyAttackThreatGeometry
{
    // 2026-09-30 패링 타격감: 강공만 더 넓고 넓은 각도로 친다. 일반 예고 공격은 기존 추가량 유지.
    private const float EliteExtra = 1.10f, StandardExtra = .75f;
    // 2026-09-30 강공 사거리 2차: 판정 반경 추가분 중형 1.40→1.90, 정예 1.90→2.50.
    private const float EliteStrongExtra = 2.50f, StandardStrongExtra = 1.90f;
    private const float StrongArcAngle = 150f;

    public const float ChargeHalfWidth = .4f;
    private static EnemyTelegraphVisualLibrary standardLibrary;
    public static bool UsesStandardAttackAreas
    {
        get
        {
            if (standardLibrary == null) standardLibrary = Resources.Load<EnemyTelegraphVisualLibrary>("Enemies/Balance/EnemyTelegraphVisualLibrary");
            return standardLibrary != null && standardLibrary.UseProceduralIndicator;
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStandardLibrary() => standardLibrary = null;

    // A short curved near edge replaces the acute apex. The real hit and preview
    // use the same inset; the legacy toggle restores the former filled sector.
    public static float ResolveSectorInnerRadius(EnemyActor actor, EnemyAbilityDefinition ability)
    {
        if (!UsesStandardAttackAreas || ability == null || !ability.IsMeleeStrongAttack
            || ability.ExecutionMode != EnemyAbilityExecutionMode.MeleeArc || ResolveHitAngle(actor, ability) >= 359.9f) return 0f;
        float radius = ResolveRadius(actor, ability);
        var body = actor != null ? actor.GetComponent<CombatTarget>() : null;
        float clearance = radius * .18f;
        if (body != null)
        {
            Vector3 offset = body.CurrentVolume.Center - actor.transform.position; offset.y = 0f;
            clearance = Mathf.Max(clearance, body.CurrentVolume.Radius + offset.magnitude + .08f);
        }
        return Mathf.Min(clearance, Mathf.Max(0f, radius - .15f));
    }
    public static bool IsInsideSectorInset(EnemyActor actor, EnemyAbilityDefinition ability, Vector3 center, Vector3 point)
    {
        float inner = ResolveSectorInnerRadius(actor, ability);
        Vector3 delta = point - center; delta.y = 0f;
        return inner > 0f && delta.sqrMagnitude < inner * inner;
    }
    public static Vector3 ResolveFacingOrigin(EnemyActor actor, EnemyAbilityDefinition ability, Vector3 impactCenter)
        => UsesStandardAttackAreas && ability != null && ability.IsMeleeStrongAttack ? impactCenter : actor.transform.position;

    public static Vector3 ResolveImpactCenter(EnemyActor actor, EnemyAbilityDefinition ability, Vector3 attackPoint)
    {
        if (actor == null || ability == null) return attackPoint;
        if (ability.HasWeakAttackExecution && ability.WeakAttackExecution.HasContactGeometry) return actor.transform.position;
        if (ability.ExecutionMode == EnemyAbilityExecutionMode.AreaSlam
            || UsesStandardAttackAreas && ability.IsMeleeStrongAttack && ResolveHitAngle(actor, ability) < 359.9f)
            return actor.transform.position;
        return attackPoint;
    }

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
        if (ability.HasWeakAttackExecution && ability.WeakAttackExecution.HasContactGeometry)
            return ability.WeakAttackExecution.MaximumContactPlanarReach;
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
        if (ability.HasWeakAttackExecution && ability.WeakAttackExecution.HasContactGeometry) return 360f;
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
    // 2026-09-30 강공 발동 거리: 중형·정예 근접·범위 강공은 판정 반경이 넓은데 발동은 너무 붙어서 했다.
    // 발동 거리를 더 늘리되, 선 채로 맞을 수 있게 판정 반경(근접은 공격점 여유 포함) 안으로 제한한다.
    private const float StandardStrongStartBonus = 1.0f, EliteStrongStartBonus = 1.2f; // 2차: 중형 .5→1.0, 정예 .7→1.2
    private const float MeleeArcStartCapMargin = .4f, AreaSlamStartCapMargin = .25f;

    public static float ResolveStartRange(EnemyActor actor, EnemyAbilityDefinition ability)
    {
        if (ability == null) return 0f;
        if (ability.WeakAttackExecution != null && ability.WeakAttackExecution.RequiresBlockedApproach
            && (actor == null || actor.GetComponent<EnemyBlockedApproachProjectileExecutor>()?.HasFallbackPermission(ability) != true)) return 0f;
        if (ability.HasWeakAttackExecution) return ability.WeakAttackExecution.ApproachStartRange;
        if (ability.ExecutionMode == EnemyAbilityExecutionMode.Projectile
            || ability.ExecutionMode == EnemyAbilityExecutionMode.DirectTarget)
            return ability.Range;
        float resolvedRadius = ResolveRadius(actor, ability);
        float start = ability.Range + (resolvedRadius - ability.HitRadius) * .6f;
        bool strongStrike = ability.IsTelegraphedStrongAttack
            && (ability.ExecutionMode == EnemyAbilityExecutionMode.MeleeArc
                || ability.ExecutionMode == EnemyAbilityExecutionMode.AreaSlam);
        ThreatTier tier = strongStrike ? ResolveTier(actor) : ThreatTier.None;
        if (tier == ThreatTier.None) return start;
        float bonus = tier == ThreatTier.Elite ? EliteStrongStartBonus : StandardStrongStartBonus;
        float cap = resolvedRadius
            + (ability.ExecutionMode == EnemyAbilityExecutionMode.MeleeArc ? MeleeArcStartCapMargin : AreaSlamStartCapMargin);
        return Mathf.Max(start, Mathf.Min(start + bonus, cap));
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
        var materialExecutor = actor.GetComponent<EnemyBossMaterialExecutor>();
        if (materialExecutor != null && materialExecutor.Supports(ability)) return materialExecutor.WouldHit(ability, target);
        if (ability.ExecutionMode == EnemyAbilityExecutionMode.Charge)
            return actor.GetComponent<EnemyThemeSpecialExecutor>()?.WouldChargeHit(ability, target) == true;
        if (ability.ExecutionMode == EnemyAbilityExecutionMode.MeleeArc
            || ability.ExecutionMode == EnemyAbilityExecutionMode.AreaSlam)
            return actor.Melee != null && actor.Melee.WouldAbilityHitTarget(ability, target);
        return false;
    }
}
