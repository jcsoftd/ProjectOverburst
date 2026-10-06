using UnityEngine;

public enum EnemyAbilityExecutionMode
{
    MeleeArc,
    DirectTarget,
    Charge,
    AreaSlam,
    Projectile,
    Summon,
    Zone
}

[CreateAssetMenu(menuName = "OVERBURST/Enemies/Ability Definition", fileName = "EAD_EnemyAbility")]
public sealed partial class EnemyAbilityDefinition : ScriptableObject
{
    [SerializeField] private string abilityId;
    [SerializeField] private EnemyWeakAttackExecutionProfile weakAttackExecution;
    public EnemyWeakAttackExecutionProfile WeakAttackExecution => weakAttackExecution;
    public bool HasWeakAttackExecution => weakAttackExecution != null;

    public void ConfigureWeakAttackExecution(EnemyWeakAttackExecutionProfile profile)
    {
        if (profile != null && (!profile.ValidateAuthoring(out _) || IsTelegraphedStrongAttack
            || IsWeakMeleeExecution(ExecutionMode) && (HitCount > 3 || !MatchesWeakContactWindows(profile))))
            throw new System.ArgumentException("V3 약공 프로필 또는 최대 3타 조건이 유효하지 않습니다.");
        weakAttackExecution = profile;
    }

    [SerializeField] private string animatorTrigger = "Attack1";
    [SerializeField] private EnemyAbilityExecutionMode executionMode =
        EnemyAbilityExecutionMode.MeleeArc;
    [SerializeField, Min(0f)] private float damage = 10f;
    [SerializeField, Range(0f, 100f)] private float referencePatternDamagePercent;
    public float ReferencePatternDamagePercent => referencePatternDamagePercent;
    public bool UsesLevelDamageBudget => referencePatternDamagePercent > 0f;
    [SerializeField] private bool telegraphedStrongAttack;
    [SerializeField] private bool telegraphedAttack;
    [SerializeField, Min(.1f)] private float preparationDuration = .55f;
    [SerializeField, Min(.05f)] private float releaseDuration = .14f;
    [SerializeField, Min(.05f)] private float recoveryDuration = .28f;
    public bool IsTelegraphedAttack => telegraphedAttack || telegraphedStrongAttack;
    public bool UsesPacedTimeline => telegraphedAttack;
    private float PreparationEnd => Mathf.Max(.02f, HitNormalizedTime - .12f);
    private float LastHit => GetHitNormalizedTime(HitCount - 1);
    [SerializeField, Min(0f)] private float minimumWarningTime;
    [SerializeField, Min(0f)] private float minimumRecoveryTime;
    [SerializeField] private bool parryable;
    public bool IsTelegraphedStrongAttack => telegraphedStrongAttack;
    public float MinimumWarningTime => telegraphedStrongAttack ? Mathf.Max(0f, minimumWarningTime) : 0f;
    public float MinimumRecoveryTime => Mathf.Max(0f, minimumRecoveryTime);
    // 2026-10-01: 패링 가능 공격은 근접 강공(휘두르기·돌진·내려찍기)뿐이다. 평타와 원거리는 자산 값과 관계없이 패링되지 않는다.
    // 바닥 장판과 머리 위 패링 빛도 근접 강공만 띄운다(EnemyAbilityController).
    public static bool IsMeleeExecution(EnemyAbilityExecutionMode mode) => mode == EnemyAbilityExecutionMode.MeleeArc
        || mode == EnemyAbilityExecutionMode.Charge || mode == EnemyAbilityExecutionMode.AreaSlam;
    public static bool IsWeakMeleeExecution(EnemyAbilityExecutionMode mode) => IsMeleeExecution(mode) || mode == EnemyAbilityExecutionMode.DirectTarget;
    public bool IsMeleeStrongAttack => telegraphedStrongAttack && IsMeleeExecution(executionMode);
    public bool IsParryable => IsMeleeStrongAttack && parryable;
    private float PreparationSeconds(float speed) => Mathf.Max(.42f,
        preparationDuration / Mathf.Max(.01f, speed));
    private float ReleaseSeconds(float speed) => Mathf.Max(.10f,
        releaseDuration / Mathf.Max(.01f, speed));
    // Very long authored tails must not be crushed into a few frames on return.
    private float RecoverySeconds(float speed) => Mathf.Max(MinimumRecoveryTime,
        recoveryDuration / Mathf.Max(.01f, speed),
        UsesPacedTimeline ? AttackAnimationDuration * (1f - LastHit) / 3f : 0f);
    public float ResolvePacedTime(float normalized, float speed)
    {
        normalized = Mathf.Clamp01(normalized);
        if (!UsesPacedTimeline) return AttackAnimationDuration * normalized / Mathf.Max(.01f, speed);
        float prep = PreparationEnd;
        float releaseRate = ReleaseSeconds(speed) / (HitNormalizedTime - prep);
        if (normalized <= prep) return PreparationSeconds(speed) * normalized / prep;
        float time = PreparationSeconds(speed);
        if (normalized <= LastHit) return time + (normalized - prep) * releaseRate;
        return time + (LastHit - prep) * releaseRate
            + (normalized - LastHit) * RecoverySeconds(speed) / Mathf.Max(.01f, 1f - LastHit);
    }
    public float ResolvePhaseAnimationSpeed(float normalized, float speed)
    {
        if (!UsesPacedTimeline) return Mathf.Max(.01f, speed);
        float secondsPerNormalized = normalized < PreparationEnd
            ? PreparationSeconds(speed) / PreparationEnd
            : normalized < LastHit ? ReleaseSeconds(speed) / (HitNormalizedTime - PreparationEnd)
            : RecoverySeconds(speed) / Mathf.Max(.01f, 1f - LastHit);
        return AttackAnimationDuration / secondsPerNormalized;
    }
    public float ResolveWindupDelay(float animationSpeed)
    {
        if (UsesPacedTimeline) return 0f;
        float first = AttackAnimationDuration * HitNormalizedTime / Mathf.Max(.01f, animationSpeed);
        return Mathf.Max(0f, Mathf.Max(.15f, MinimumWarningTime) - first);
    }
    public float ResolveFirstImpactTime(float animationSpeed) => ResolveWindupDelay(animationSpeed)
        + ResolvePacedTime(HitNormalizedTime, animationSpeed);
    public float ResolveLastImpactTime(float animationSpeed) => ResolveWindupDelay(animationSpeed)
        + ResolvePacedTime(LastHit, animationSpeed);
    public float ResolveExecutionDuration(float animationSpeed) => Mathf.Max(
        ResolveWindupDelay(animationSpeed) + ResolvePacedTime(1f, animationSpeed),
        ResolveLastImpactTime(animationSpeed) + MinimumRecoveryTime);
    public float ResolveDamage(int level) => UsesLevelDamageBudget
        ? Mathf.Max(1f, OverburstCombatBalance.RoundStat(OverburstCombatBalance.ReferenceEffectiveHealth(level)
            * referencePatternDamagePercent / (100f * HitCount))) : Damage;
    // V3 weak attacks spend one pattern budget, regardless of contact count.
    // Legacy per-hit ResolveDamage remains unchanged for profiles without V3 execution.
    public bool TryResolveWeakDamageBudget(int level, float definitionMultiplier, out EnemyWeakAttackDamageBudget budget)
    {
        float total = UsesLevelDamageBudget
            ? Mathf.Max(1f, OverburstCombatBalance.RoundStat(OverburstCombatBalance.ReferenceEffectiveHealth(level)
                * referencePatternDamagePercent / 100f)) : Damage;
        return EnemyWeakAttackDamageBudget.TryCreate(total * definitionMultiplier, HitCount, out budget);
    }

    [SerializeField, Min(0f)] private float minimumRange;
    [SerializeField, Min(0f)] private float range = 1.7f;
    [SerializeField, Min(0.01f)] private float hitRadius = 0.8f;
    [SerializeField, Range(0f, 360f)] private float hitAngle = 120f;
    [SerializeField, Min(0f)] private float verticalTolerance = 0.75f;
    [SerializeField] private bool requireLineOfSight;
    [SerializeField, Min(0f)] private float cooldown = 1.25f;
    [SerializeField, Min(0f)] private float hitDelay = 0.45f;
    [SerializeField, Range(0.05f, 0.95f)] private float hitNormalizedTime = 0.45f;
    [SerializeField, Min(0.01f)] private float attackAnimationDuration = 1f;
    [SerializeField, Min(0f)] private float attackLockDuration = 0.9f;
    [SerializeField, Min(0f)] private float weight = 1f;
    [SerializeField] private int priority;
    [SerializeField, Range(0f, 1f)] private float minimumSelfHealthNormalized;
    [SerializeField, Range(0f, 1f)] private float maximumSelfHealthNormalized = 1f;
    [SerializeField] private bool requireTargetInRangeUntilHit = true;
    [SerializeField] private float[] additionalHitNormalizedTimes;
    [SerializeField, Range(1, 2)] private int projectilesPerRelease = 1;
    public int ProjectilesPerRelease => executionMode == EnemyAbilityExecutionMode.Projectile ? Mathf.Clamp(projectilesPerRelease, 1, 2) : 1;
    public int ReleaseCount => 1 + (additionalHitNormalizedTimes != null ? additionalHitNormalizedTimes.Length : 0);
    public int HitCount => ReleaseCount * ProjectilesPerRelease;
    public float GetHitNormalizedTime(int index)
    {
        int release = index / ProjectilesPerRelease;
        return release == 0 ? HitNormalizedTime : additionalHitNormalizedTimes[release - 1];
    }
    public void ConfigureProjectileGrouping(int count)
    {
        if (count < 1 || count > 2 || count > 1 && executionMode != EnemyAbilityExecutionMode.Projectile)
            throw new System.ArgumentException("동시 발사는 투사체 공격의 1~2발만 지원합니다.");
        projectilesPerRelease = count;
    }
    public void ConfigureAdditionalHits(params float[] times)
    {
        float previous = HitNormalizedTime;
        foreach (float time in times)
        {
            if (float.IsNaN(time) || time <= previous || time > .95f)
                throw new System.ArgumentException("추가 타격 시점은 첫 타격 이후 오름차순이어야 합니다.");
            previous = time;
        }
        additionalHitNormalizedTimes = (float[])times.Clone();
    }

    public string AbilityId => abilityId;
    public string AnimatorTrigger => animatorTrigger;
    public EnemyAbilityExecutionMode ExecutionMode => executionMode;
    public float Damage => Mathf.Max(0f, damage);
    public float MinimumRange => Mathf.Clamp(minimumRange, 0f, Range);
    public float Range => Mathf.Max(0f, range);
    public float HitRadius => Mathf.Max(0.01f, hitRadius);
    public float HitAngle => Mathf.Clamp(hitAngle, 0f, 360f);
    public float VerticalTolerance => Mathf.Max(0f, verticalTolerance);
    public bool RequireLineOfSight => requireLineOfSight;
    public float Cooldown => Mathf.Max(0f, cooldown);
    public float HitDelay => Mathf.Max(0f, hitDelay);
    public float HitNormalizedTime => Mathf.Clamp(hitNormalizedTime, 0.05f, 0.95f);
    public float AttackAnimationDuration => Mathf.Max(0.01f, attackAnimationDuration);
    public float AttackLockDuration => Mathf.Max(0f, attackLockDuration);
    public float Weight => Mathf.Max(0f, weight);
    public int Priority => priority;
    public float MinimumSelfHealthNormalized =>
        Mathf.Clamp01(Mathf.Min(minimumSelfHealthNormalized, maximumSelfHealthNormalized));
    public float MaximumSelfHealthNormalized =>
        Mathf.Clamp01(Mathf.Max(minimumSelfHealthNormalized, maximumSelfHealthNormalized));
    public bool RequireTargetInRangeUntilHit => requireTargetInRangeUntilHit;
    public bool IsValid => !string.IsNullOrWhiteSpace(abilityId)
        && !string.IsNullOrWhiteSpace(animatorTrigger)
        && Range > 0f
        && MinimumRange <= Range
        && Weight > 0f
        && (weakAttackExecution == null || weakAttackExecution.IsValid
            && !IsTelegraphedStrongAttack && (!IsWeakMeleeExecution(ExecutionMode)
                || HitCount <= 3 && MatchesWeakContactWindows(weakAttackExecution)));

    private bool MatchesWeakContactWindows(EnemyWeakAttackExecutionProfile profile)
        => profile.MatchesContactWindows(HitCount, GetHitNormalizedTime(0),
            HitCount > 1 ? GetHitNormalizedTime(1) : 0f, HitCount > 2 ? GetHitNormalizedTime(2) : 0f);

    public bool MatchesUseConditions(float distance, float selfHealthNormalized)
    {
        float resolvedDistance = Mathf.Max(0f, distance);
        float resolvedHealth = Mathf.Clamp01(selfHealthNormalized);
        return resolvedDistance >= MinimumRange
            && resolvedDistance <= (HasWeakAttackExecution ? weakAttackExecution.ApproachStartRange : Range)
            && resolvedHealth >= MinimumSelfHealthNormalized
            && resolvedHealth <= MaximumSelfHealthNormalized;
    }

    public void ConfigureUsePolicy(
        float minRange,
        int selectionPriority,
        float minimumHealthNormalized = 0f,
        float maximumHealthNormalized = 1f)
    {
        minimumRange = Mathf.Max(0f, minRange);
        priority = selectionPriority;
        minimumSelfHealthNormalized = Mathf.Clamp01(minimumHealthNormalized);
        maximumSelfHealthNormalized = Mathf.Clamp01(maximumHealthNormalized);
        if (minimumSelfHealthNormalized > maximumSelfHealthNormalized)
        {
            float swap = minimumSelfHealthNormalized;
            minimumSelfHealthNormalized = maximumSelfHealthNormalized;
            maximumSelfHealthNormalized = swap;
        }
    }

    public void Configure(
        string id,
        string trigger,
        float abilityDamage,
        float abilityRange,
        float abilityCooldown,
        float normalizedHitTime,
        float selectionWeight)
    {
        Configure(
            id,
            trigger,
            abilityDamage,
            abilityRange,
            0.8f,
            120f,
            abilityCooldown,
            0.45f,
            normalizedHitTime,
            0.9f,
            selectionWeight,
            true);
    }

    public void Configure(
        string id,
        string trigger,
        float abilityDamage,
        float abilityRange,
        float radius,
        float angle,
        float abilityCooldown,
        float delay,
        float normalizedHitTime,
        float lockDuration,
        float selectionWeight,
        bool keepRangeGate,
        EnemyAbilityExecutionMode mode = EnemyAbilityExecutionMode.MeleeArc,
        float targetVerticalTolerance = 0.75f,
        bool lineOfSightRequired = false,
        float animationDuration = 1f)
    {
        abilityId = id != null ? id.Trim() : string.Empty;
        additionalHitNormalizedTimes = System.Array.Empty<float>();
        animatorTrigger = trigger != null ? trigger.Trim() : string.Empty;
        executionMode = mode;
        damage = Mathf.Max(0f, abilityDamage);
        minimumRange = 0f;
        range = Mathf.Max(0f, abilityRange);
        hitRadius = Mathf.Max(0.01f, radius);
        hitAngle = Mathf.Clamp(angle, 0f, 360f);
        verticalTolerance = Mathf.Max(0f, targetVerticalTolerance);
        requireLineOfSight = lineOfSightRequired;
        cooldown = Mathf.Max(0f, abilityCooldown);
        hitDelay = Mathf.Max(0f, delay);
        hitNormalizedTime = Mathf.Clamp(normalizedHitTime, 0.05f, 0.95f);
        attackAnimationDuration = Mathf.Max(0.01f, animationDuration);
        attackLockDuration = Mathf.Max(0f, lockDuration);
        weight = Mathf.Max(0f, selectionWeight);
        priority = 0;
        minimumSelfHealthNormalized = 0f;
        maximumSelfHealthNormalized = 1f;
        requireTargetInRangeUntilHit = keepRangeGate;
    }
}
