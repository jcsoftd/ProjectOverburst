using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CombatHealth))]
public class EnemyMeleeAttackController : MonoBehaviour // 적 근접 공격 실행
{
    [SerializeField] private EnemyMovement movement; // 공격 중 이동 잠금
    [SerializeField] private EnemyMovementReaction movementReaction; // 피격 중 공격 취소
    [SerializeField] private EnemyAnimationBridge animationBridge; // 공격 애니메이션 연결
    [SerializeField] private Transform attackPoint; // 공격 판정 중심
    [SerializeField] private EnemyAbilitySet abilitySet; // Definition 기반 공격 후보
    [SerializeField] private string[] attackTriggers = { "Attack1", "Attack2", "Attack3" }; // 공격 트리거 목록
    [SerializeField] private LayerMask targetLayer; // 공격 대상 레이어
    [SerializeField] private float attackRange = 1.7f; // 공격 시작 거리
    [SerializeField] private float hitRadius = 0.8f; // 타격 반경
    [SerializeField] private float hitAngle = 120f; // 전방 타격 각도
    [SerializeField] private float damage = 10f; // 기본 피해량
    [SerializeField] private float attackCooldown = 1.25f; // 공격 재사용 시간
    [SerializeField] private float hitDelay = 0.45f; // 시간 기반 타격 지연
    [SerializeField, Range(0.05f, 0.95f)] private float hitNormalizedTime = 0.45f; // 애니메이션 타격 시점
    [SerializeField] private float attackLockDuration = 0.9f; // 공격 이동 잠금 시간
    [SerializeField] private float attackSpeedMultiplier = 1f; // 공격 속도 배율
    [SerializeField] private bool requireTargetInRangeUntilHit = true; // 타격 시점 거리 재확인
    [SerializeField] private bool avoidSameAttackTwice = true; // 동일 모션 연속 방지

    private readonly Collider[] hitBuffer = new Collider[16];
    private readonly RaycastHit[] lineOfSightBuffer = new RaycastHit[8];
    private readonly HashSet<CombatHealth> damagedTargets = new HashSet<CombatHealth>();
    private CombatHealth health; // 사망 및 피격 확인
    private CombatTarget combatTarget; // 직접 타격 소유자
    private EnemyActor actor;
    private Transform target; // 현재 공격 대상
    private Coroutine attackRoutine; // 진행 중인 공격
    private float nextAttackTime; // 다음 공격 가능 시각
    private int lastAttackIndex = -1; // 직전 공격 모션
    private int attackSequenceId;
    private int attackPhaseIndex;
    private float statusActionSpeedMultiplier = 1f; // 상태이상 행동 배율
    private float definitionDamageMultiplier = 1f; // 등급·변형 피해 배율
    private float runtimeAttackSpeedMultiplier = 1f; // 등급·변형 공격속도 배율
    private readonly EnemyAttackClock weakAttackClock = new EnemyAttackClock();
    private EnemyWeakAttackExecutionProfile activeWeakExecution;
    private string weakClockTrigger;
    private EnemyWeakAttackMotionDriver weakMotionDriver;
    public EnemyWeakAttackExecutionProfile ActiveWeakExecution => activeWeakExecution;
    public float WeakAttackNormalizedTime => weakAttackClock.NormalizedTime;
    public bool HasEnteredWeakAttack => activeWeakExecution != null && weakAttackClock.HasEntered;

    private void LateUpdate()
    {
        if (activeWeakExecution == null || weakAttackClock.IsTerminal || animationBridge == null) return;
        bool present = animationBridge.TryGetAttackMotionTime(weakClockTrigger, activeWeakExecution.RuntimeClip, out float progress);
        weakAttackClock.Observe(Time.frameCount, Time.deltaTime, present, progress);
    }

    public float AttackRange { get { return ResolveMaximumAttackRange(); } }
    public Transform AttackPoint => attackPoint != null ? attackPoint : transform;
    // Preview only: run the same collider, facing, height and sight checks as ResolveArcHit.
    public bool WouldAbilityHitTarget(EnemyAbilityDefinition ability, CombatTarget target)
    {
        if (ability == null || target == null || attackPoint == null
            || !CombatTargetFilter.CanDamage(combatTarget, target)) return false;
        Vector3 center = EnemyAttackThreatGeometry.ResolveImpactCenter(actor, ability, attackPoint.position);
        int count = Physics.OverlapSphereNonAlloc(center,
            EnemyAttackThreatGeometry.ResolveRadius(actor, ability),
            hitBuffer, targetLayer, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider collider = hitBuffer[i];
            if (collider == null || CombatTarget.Resolve(collider) != target
                || !IsInFront(collider.transform.position,
                    EnemyAttackThreatGeometry.ResolveHitAngle(actor, ability),
                    EnemyAttackThreatGeometry.ResolveFacingOrigin(actor, ability, center))
                || EnemyAttackThreatGeometry.IsInsideSectorInset(actor, ability, center, collider.transform.position)) continue;
            CombatTargetVolume volume = target.CurrentVolume;
            if (Mathf.Abs(center.y - volume.Center.y) > volume.HalfHeight + ability.VerticalTolerance)
                continue;
            if (ability.RequireLineOfSight
                && !HasDirectLineOfSight(target, volume.Center, center)) continue;
            return true;
        }
        return false;
    }
    public bool IsAttacking { get { return attackRoutine != null; } }
    public bool IsCooldownReady { get { return Time.time >= nextAttackTime; } }
    public float StatusActionSpeedMultiplier { get { return statusActionSpeedMultiplier; } }
    public int LastCommittedAttackIndex { get { return lastAttackIndex; } }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        statusActionSpeedMultiplier = 1f;

        if (movementReaction != null)
        {
            movementReaction.ReactionStarted -= HandleReactionStarted;
            movementReaction.ReactionStarted += HandleReactionStarted;
        }

        if (health != null)
        {
            if (GetComponent<EnemyHitResponseCoordinator>() == null)
                health.OnDamaged += HandleDamaged;
            health.OnDead += HandleDead;
        }
    }

    private void OnDisable()
    {
        statusActionSpeedMultiplier = 1f;

        if (movementReaction != null)
            movementReaction.ReactionStarted -= HandleReactionStarted;

        if (health != null)
        {
            health.OnDamaged -= HandleDamaged;
            health.OnDead -= HandleDead;
        }

        if (attackRoutine != null)
        {
            StopCoroutine(attackRoutine);
            attackRoutine = null;
        }
        weakMotionDriver?.End();
        activeWeakExecution = null;
        weakAttackClock.Cancel();
        movement?.ClearAttackDisplacement();
    }

    public bool TryStartAttack(Transform attackTarget)
    {
        if (attackTarget != null)
            target = attackTarget;

        ResolveTarget();
        if (!CanBeginAttackAttempt())
            return false;

        EnemyAbilityDefinition ability = PickConfiguredAbility(out int selectedIndex);
        if (abilitySet != null && abilitySet.IsValid && ability == null)
            return false;
        return TryStartResolvedAttack(ability, selectedIndex, false);
    }

    private void HandleReactionStarted()
    {
        if (movementReaction == null || movementReaction.BlocksAttack) CancelAttack();
    }

    public bool CanStartAbility(
        EnemyAbilityDefinition ability,
        Transform attackTarget)
    {
        if (ability == null
            || !ability.IsValid
            || (ability.ExecutionMode != EnemyAbilityExecutionMode.MeleeArc
                && ability.ExecutionMode != EnemyAbilityExecutionMode.DirectTarget
                && ability.ExecutionMode != EnemyAbilityExecutionMode.AreaSlam)
            || !CanBeginAttackAttempt(attackTarget))
        {
            return false;
        }

        if (ability.HasWeakAttackExecution && (weakMotionDriver == null || !weakMotionDriver.CanUse(ability.WeakAttackExecution))) return false;
        if (ability.HasWeakAttackExecution && (animationBridge == null
            || !animationBridge.CanPlayAttackMotion(ability.AnimatorTrigger, ability.WeakAttackExecution.RuntimeClip))) return false;
        Vector3 aimPosition = ResolveAimPosition(attackTarget);
        Vector3 delta = aimPosition - transform.position;
        delta.y = 0f;
        float startRange = EnemyAttackThreatGeometry.ResolveStartRange(actor, ability);
        return delta.sqrMagnitude <= startRange * startRange
            && (!ability.HasWeakAttackExecution || EnemyAttackThreatGeometry.MatchesUseConditions(actor, ability,
                delta.magnitude, health != null ? health.NormalizedHp : 1f))
            && (movement == null || movement.IsFacingForAttack(aimPosition));
    }

    public bool TryStartAbility(
        Transform attackTarget,
        EnemyAbilityDefinition ability,
        int abilityIndex)
    {
        if (attackTarget != null)
            target = attackTarget;
        return TryStartResolvedAttack(ability, abilityIndex, true);
    }

    public float ResolveAbilityCooldown(float baseCooldown)
    {
        return Mathf.Max(0f, baseCooldown);
    }

    public float AbilityAnimationSpeed => ResolveAttackSpeedMultiplier();
    public float ResolveAbilityAnimationTime(float duration) => ResolveScaledTime(duration);

    private bool TryStartResolvedAttack(
        EnemyAbilityDefinition ability,
        int selectedIndex,
        bool cooldownOwnedExternally)
    {
        if (ability != null && (!ability.IsValid || ability.HasWeakAttackExecution && !CanStartAbility(ability, target)))
            return false;
        float resolvedRange = ability != null ? EnemyAttackThreatGeometry.ResolveStartRange(actor, ability) : Mathf.Max(0f, attackRange);
        if (!CanStartAttack(resolvedRange))
            return false;

        Vector3 aimPosition = ResolveAimPosition(target);
        if (movement != null && !movement.IsFacingForAttack(aimPosition))
        {
            movement.FacePosition(aimPosition);
            return false;
        }

        FaceTargetOnce();
        string triggerName = ability != null
            ? ability.AnimatorTrigger
            : PickAttackTrigger(out selectedIndex);
        if (string.IsNullOrEmpty(triggerName))
        {
            Debug.LogWarning("[EnemyMeleeAttackController] Attack trigger list is empty.", this);
            return false;
        }

        CombatTarget committedTarget = ability != null
            && ability.ExecutionMode == EnemyAbilityExecutionMode.DirectTarget
            ? ResolveCombatTarget(target)
            : null;
        if (ability != null
            && ability.ExecutionMode == EnemyAbilityExecutionMode.DirectTarget
            && !CombatTargetFilter.CanDamage(combatTarget, committedTarget))
        {
            return false;
        }

        movementReaction?.PrepareForAttack();
        attackSequenceId = EnemyAttackSequence.Next();
        attackRoutine = StartCoroutine(
            AttackRoutine(
                triggerName,
                ability,
                committedTarget,
                cooldownOwnedExternally));
        if (attackRoutine == null)
            return false;

        lastAttackIndex = selectedIndex;
        return true;
    }

    public void Configure(EnemyAbilitySet configuredAbilitySet, float damageMultiplier = 1f)
    {
        abilitySet = configuredAbilitySet;
        definitionDamageMultiplier = float.IsNaN(damageMultiplier) || float.IsInfinity(damageMultiplier)
            ? 1f
            : Mathf.Max(0f, damageMultiplier);
        lastAttackIndex = -1;
        nextAttackTime = 0f;
    }

    public void SetRuntimeAttackSpeedMultiplier(float multiplier)
    {
        runtimeAttackSpeedMultiplier = float.IsNaN(multiplier) || float.IsInfinity(multiplier)
            ? 1f
            : Mathf.Max(0.01f, multiplier);
    }

    public void ClearTarget()
    {
        target = null;
    }

    public void SetStatusActionSpeedMultiplier(float speedMultiplier)
    {
        statusActionSpeedMultiplier = float.IsNaN(speedMultiplier) || float.IsInfinity(speedMultiplier)
            ? 1f
            : Mathf.Clamp01(speedMultiplier);
        if (statusActionSpeedMultiplier <= 0f)
            CancelAttack(); // 빙결 순간 진행 공격과 예약 타격 취소
    }

    private IEnumerator AttackRoutine(
        string triggerName,
        EnemyAbilityDefinition ability,
        CombatTarget committedTarget,
        bool cooldownOwnedExternally)
    {
        if (ability != null && ability.HasWeakAttackExecution)
        {
            yield return WeakAttackRoutine(triggerName, ability, committedTarget, cooldownOwnedExternally);
            yield break;
        }
        float resolvedCooldown = ability != null ? ability.Cooldown : attackCooldown;
        float resolvedHitDelayBase = ability != null ? ability.HitDelay : hitDelay;
        float resolvedHitNormalizedTime = ability != null ? ability.HitNormalizedTime : hitNormalizedTime;
        float resolvedAttackLockDuration = ability != null ? ability.AttackLockDuration : attackLockDuration;
        float resolvedAnimationDuration = ability != null
            ? ability.AttackAnimationDuration
            : resolvedAttackLockDuration;
        float resolvedRange = ability != null ? EnemyAttackThreatGeometry.ResolveStartRange(actor, ability) : attackRange;
        bool keepRangeGate = ability != null
            ? ability.RequireTargetInRangeUntilHit
            : requireTargetInRangeUntilHit;
        if (ability != null && ability.IsTelegraphedAttack
            && ability.ExecutionMode != EnemyAbilityExecutionMode.DirectTarget) keepRangeGate = false;
        if (abilityController != null && abilityController.HasPreparedAim(target)
            && ability != null && ability.ExecutionMode != EnemyAbilityExecutionMode.DirectTarget)
            keepRangeGate = false; // Spatial hit geometry decides whether the committed strike misses.
        bool directTargetExecution = ability != null
            && ability.ExecutionMode == EnemyAbilityExecutionMode.DirectTarget;
        float resolvedAttackSpeed = ResolveAttackSpeedMultiplier();
        float startedAt = Time.time;
        float windup = ability != null ? ability.ResolveWindupDelay(resolvedAttackSpeed) : 0f;
        float executionDuration = ability != null ? ability.ResolveExecutionDuration(resolvedAttackSpeed)
            : resolvedAnimationDuration / resolvedAttackSpeed;
        if (!cooldownOwnedExternally)
            nextAttackTime = Time.time + Mathf.Max(resolvedCooldown, executionDuration);
        if (movement != null)
        {
            float minimumImpactLock =
                resolvedAnimationDuration * (ability != null ? ability.GetHitNormalizedTime(ability.HitCount - 1) : resolvedHitNormalizedTime) + 0.05f;
            movement.ApplyActionLock(ability != null && ability.UsesPacedTimeline ? executionDuration
                : Mathf.Max(executionDuration, windup + ResolveScaledTime(
                    Mathf.Max(resolvedAttackLockDuration, minimumImpactLock), resolvedAttackSpeed)));
        }

        while (Time.time < startedAt + windup)
        {
            if (IsAttackInterrupted()) { attackRoutine = null; yield break; }
            yield return null;
        }

        if (animationBridge != null)
        {
            animationBridge.SetAttackAnimSpeed(ability != null
                ? ability.ResolvePhaseAnimationSpeed(0f, resolvedAttackSpeed) : resolvedAttackSpeed);
            animationBridge.PlayAttack(triggerName); // 선택 공격 재생
        }

        float elapsed = 0f;
        int hitCount = ability != null ? ability.HitCount : 1;
        bool observedAttackAnimation = false;
        bool strongReleasePlayed = false;
        for (int impactIndex = 0; impactIndex < hitCount; impactIndex++)
        {
            float impactTime = ability != null ? ability.GetHitNormalizedTime(impactIndex) : resolvedHitNormalizedTime;
            bool stayedInRange = true;
            bool impactReached = false;
            bool useAnimatorTiming = animationBridge != null && animationBridge.HasAnimator;
            float resolvedHitDelay = ability != null && ability.UsesPacedTimeline
                ? ability.ResolvePacedTime(impactTime, resolvedAttackSpeed)
                : ResolveScaledTime(impactIndex == 0 ? resolvedHitDelayBase : resolvedAnimationDuration * impactTime, resolvedAttackSpeed);
            float attackStateEntryGrace = Mathf.Min(0.35f, Mathf.Max(0.15f, resolvedHitDelay));
            float maximumHitWait = Mathf.Max(
                resolvedHitDelay,
                ResolveScaledTime(resolvedAttackLockDuration, resolvedAttackSpeed) + 0.25f,
                ResolveScaledTime(resolvedAnimationDuration, resolvedAttackSpeed) + 0.15f);
            if (ability != null && ability.UsesPacedTimeline) maximumHitWait = executionDuration + .5f;
            while (elapsed < maximumHitWait)
            {
                if (IsAttackInterrupted())
                {
                    attackRoutine = null;
                    yield break; // 피격 공격 취소
                }

                if (keepRangeGate
                    && !directTargetExecution
                    && !IsTargetWithinAttackRange(resolvedRange))
                {
                    stayedInRange = false; // 사거리 이탈
                    break;
                }

                if (useAnimatorTiming
                    && animationBridge.TryGetAttackNormalizedTime(triggerName, out float normalizedTime))
                {
                    observedAttackAnimation = true;
                    if (!strongReleasePlayed && impactIndex == 0
                        && ability != null && ability.IsTelegraphedStrongAttack
                        && normalizedTime >= impactTime - .055f)
                    {
                        strongReleasePlayed = true;
                        CombatActionSfxService.PlayEnemyStrongRelease(transform.position);
                    }
                    if (ability != null) animationBridge.SetAttackAnimSpeed(
                        ability.ResolvePhaseAnimationSpeed(normalizedTime, resolvedAttackSpeed));
                    if (normalizedTime >= Mathf.Clamp01(impactTime))
                    {
                        impactReached = true;
                        break; // 실제 공격 모션 타격 구간
                    }
                }
                else if (useAnimatorTiming && ability != null && ability.UsesPacedTimeline
                    && !observedAttackAnimation && elapsed >= attackStateEntryGrace)
                {
                    stayedInRange = false;
                    break; // 예고형 공격은 연결된 모션에 진입하지 못하면 피해도 취소한다.
                }
                else if (!useAnimatorTiming || (!observedAttackAnimation && elapsed >= attackStateEntryGrace))
                {
                    if (!strongReleasePlayed && impactIndex == 0
                        && ability != null && ability.IsTelegraphedStrongAttack
                        && elapsed >= resolvedHitDelay - .08f)
                    {
                        strongReleasePlayed = true;
                        CombatActionSfxService.PlayEnemyStrongRelease(transform.position);
                    }
                    if (elapsed >= resolvedHitDelay)
                    {
                        impactReached = true;
                        break; // 애니메이션 미연결 시간 판정
                    }
                }
                else if (observedAttackAnimation)
                {
                    stayedInRange = false;
                    break; // 타격 전 공격 상태 종료
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            if (impactReached && stayedInRange && !IsAttackInterrupted() && CanResolveHit())
            {
                abilityController?.NotifyAbilityImpact(ability, impactIndex);
                attackPhaseIndex = impactIndex;
                if (ability != null && ability.IsTelegraphedStrongAttack && impactIndex == 0)
                {
                    Vector3 impactOrigin = ability.ExecutionMode == EnemyAbilityExecutionMode.AreaSlam
                        ? transform.position : attackPoint.position;
                    CombatActionSfxService.PlayEnemyGroundImpact(impactOrigin);
                    EnemyStrongAttackImpactVfx.Play(impactOrigin);
                }
                int level = GetComponent<EnemyRank>()?.Level ?? 1;
                float resolvedDamage = (ability != null ? ability.ResolveDamage(level) : damage) * definitionDamageMultiplier;
                if (directTargetExecution)
                {
                    ResolveDirectTargetHit(
                        committedTarget,
                        ability,
                        resolvedDamage,
                        keepRangeGate);
                }
                else
                {
                    float resolvedRadius = ability != null
                        ? EnemyAttackThreatGeometry.ResolveRadius(actor, ability)
                        : hitRadius;
                    float resolvedAngle = ability != null
                        ? EnemyAttackThreatGeometry.ResolveHitAngle(actor, ability) : hitAngle;
                    ResolveArcHit(
                        resolvedDamage,
                        resolvedRadius,
                        resolvedAngle,
                        ability);
                }
            }

            if (!stayedInRange) break;
        }

        float recoveryEnd = Mathf.Max(startedAt + executionDuration,
            Time.time + (ability != null ? ability.MinimumRecoveryTime : 0f));
        while (Time.time < recoveryEnd && !IsAttackInterrupted())
        {
            if (ability != null && animationBridge != null
                && animationBridge.TryGetAttackNormalizedTime(triggerName, out float progress))
                animationBridge.SetAttackAnimSpeed(ability.ResolvePhaseAnimationSpeed(progress, resolvedAttackSpeed));
            yield return null;
        }
        attackRoutine = null;
    }

    private IEnumerator WeakAttackRoutine(string triggerName, EnemyAbilityDefinition ability,
        CombatTarget committedTarget, bool cooldownOwnedExternally)
    {
        activeWeakExecution = ability.WeakAttackExecution;
        weakClockTrigger = triggerName;
        Vector3 initialDelta = target != null ? ResolveAimPosition(target) - transform.position : Vector3.zero; initialDelta.y = 0f;
        weakMotionDriver?.Begin(activeWeakExecution, initialDelta.magnitude, transform.rotation);
        bool previous = animationBridge.TryGetAttackMotionTime(triggerName, activeWeakExecution.RuntimeClip, out float previousTime);
        weakAttackClock.Begin(Time.frameCount, previous, previousTime);
        float speed = ResolveAttackSpeedMultiplier();
        if (!cooldownOwnedExternally) nextAttackTime = Time.time + Mathf.Max(ability.Cooldown, ability.ResolveExecutionDuration(speed));
        movement?.ApplyActionLock(.2f);
        animationBridge.SetAttackAnimSpeed(ability.ResolvePhaseAnimationSpeed(0f, speed));
        animationBridge.PlayAttack(triggerName);
        int nextImpact = 0;
        float lastStrikeAt = Time.time;
        // Always yield once: the previous state's same-frame sample cannot be
        // mistaken for this execution, and the coroutine handle is established.
        yield return null;
        while (!IsAttackInterrupted())
        {
            if (weakAttackClock.State == EnemyAttackClock.Phase.Failed || weakAttackClock.State == EnemyAttackClock.Phase.Cancelled) break;
            movement?.ApplyActionLock(.2f);
            if (weakAttackClock.HasEntered)
            {
                animationBridge.SetAttackAnimSpeed(ability.ResolvePhaseAnimationSpeed(weakAttackClock.NormalizedTime, ResolveAttackSpeedMultiplier()));
                while (nextImpact < ability.HitCount && weakAttackClock.NormalizedTime >= ability.GetHitNormalizedTime(nextImpact))
                {
                    if (IsAttackInterrupted()) break;
                    abilityController?.NotifyAbilityImpact(ability, nextImpact);
                    attackPhaseIndex = nextImpact++;
                    lastStrikeAt = Time.time;
                    if (!CanResolveHit()) continue;
                    int level = GetComponent<EnemyRank>()?.Level ?? 1;
                    float resolvedDamage = ability.ResolveDamage(level) * definitionDamageMultiplier;
                    if (ability.ExecutionMode == EnemyAbilityExecutionMode.DirectTarget)
                        ResolveDirectTargetHit(committedTarget, ability, resolvedDamage, true);
                    else ResolveArcHit(resolvedDamage, EnemyAttackThreatGeometry.ResolveRadius(actor, ability),
                        EnemyAttackThreatGeometry.ResolveHitAngle(actor, ability), ability);
                    // A missed strike does not cancel later strikes. GOAL C will
                    // consume these clock crossings in the physics event queue.
                }
            }
            if (weakAttackClock.IsTerminal) break;
            yield return null;
        }
        bool completed = weakAttackClock.State == EnemyAttackClock.Phase.Completed && !IsAttackInterrupted();
        if (completed)
        {
            float recoveryEnd = Mathf.Max(Time.time, lastStrikeAt + ability.MinimumRecoveryTime);
            while (Time.time < recoveryEnd && !IsAttackInterrupted())
            { movement?.ApplyActionLock(.2f); yield return null; }
        }
        weakMotionDriver?.End();
        activeWeakExecution = null;
        weakAttackClock.Cancel();
        movement?.ClearAttackDisplacement();
        movement?.CancelActionLock();
        attackRoutine = null;
    }

    private bool CanStartAttack(float resolvedRange)
    {
        if (!CanBeginAttackAttempt())
            return false;

        Vector3 delta = ResolveAimPosition(target) - transform.position;
        delta.y = 0f;
        return delta.sqrMagnitude <= resolvedRange * resolvedRange;
    }

    private bool CanBeginAttackAttempt(Transform candidateTarget = null)
    {
        if (health != null && health.IsDead)
            return false;
        if (statusActionSpeedMultiplier <= 0f)
            return false;
        if (attackRoutine != null || Time.time < nextAttackTime)
            return false;
        if ((movementReaction != null && movementReaction.BlocksAttack)
            || (movement != null && movement.IsActionLocked))
            return false;
        if (animationBridge != null && animationBridge.BlocksAttackStart)
            return false; // 이전 공격 모션이 끝나기 전 새 타격 예약 금지

        if (candidateTarget == null && target == null)
            return false;

        return true;
    }

    private bool IsTargetWithinAttackRange(float resolvedRange)
    {
        if (target == null)
            return false;

        Vector3 delta = target.position - transform.position;
        delta.y = 0f;
        return delta.sqrMagnitude <= resolvedRange * resolvedRange;
    }

    private bool CanResolveHit()
    {
        if (health != null && health.IsDead)
            return false;
        if (statusActionSpeedMultiplier <= 0f)
            return false;
        if (movementReaction != null && movementReaction.BlocksAttack)
            return false;

        return attackPoint != null;
    }

    private bool IsAttackInterrupted()
    {
        if (health != null && health.IsDead)
            return true;
        if (statusActionSpeedMultiplier <= 0f)
            return true;
        if (movementReaction != null && movementReaction.BlocksAttack)
            return true;

        return false;
    }

    private void ResolveDirectTargetHit(
        CombatTarget committedTarget,
        EnemyAbilityDefinition ability,
        float resolvedDamage,
        bool keepRangeGate)
    {
        if (ability == null
            || !CombatTargetFilter.CanDamage(combatTarget, committedTarget))
        {
            return;
        }

        CombatTargetVolume targetVolume = committedTarget.CurrentVolume;
        if (keepRangeGate
            && !IsTargetWithinAttackRange(committedTarget.transform, ability.Range))
        {
            return;
        }

        if (!IsInFront(targetVolume.Center, ability.HitAngle))
            return;

        float verticalDistance =
            Mathf.Abs(attackPoint.position.y - targetVolume.Center.y);
        if (verticalDistance > targetVolume.HalfHeight + ability.VerticalTolerance)
            return;

        if (ability.RequireLineOfSight
            && !HasDirectLineOfSight(committedTarget, targetVolume.Center))
        {
            return;
        }

        CombatHealth targetHealth = committedTarget.DamageReceiver;
        if (targetHealth == null || targetHealth.IsDead)
            return;

        Vector3 hitDirection = targetVolume.Center - transform.position;
        hitDirection.y = 0f;
        DamageInfo info = new DamageInfo(
            resolvedDamage,
            targetVolume.Center,
            gameObject,
            hitDirection.sqrMagnitude > 0.0001f
                ? hitDirection.normalized
                : transform.forward, sourceAttackSequenceId: attackSequenceId,
            sourceAttackPhaseIndex: attackPhaseIndex, enemyAbility: ability);
        targetHealth.TakeDamage(info); // 선택한 단일 대상에게 한 번만 직접 피해
    }

    private void ResolveArcHit(
        float resolvedDamage,
        float resolvedRadius,
        float resolvedAngle,
        EnemyAbilityDefinition ability)
    {
        damagedTargets.Clear();
        bool isAreaSlam = ability != null
            && ability.ExecutionMode == EnemyAbilityExecutionMode.AreaSlam;
        Vector3 impactCenter = EnemyAttackThreatGeometry.ResolveImpactCenter(actor, ability, attackPoint.position);
        int hitCount = Physics.OverlapSphereNonAlloc(
            impactCenter,
            resolvedRadius,
            hitBuffer,
            targetLayer,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            if (IsAttackInterrupted()) break; // A parried hit cancels remaining targets in this impact too.
            Collider hitCollider = hitBuffer[i];
            if (hitCollider == null || !IsInFront(hitCollider.transform.position, resolvedAngle,
                    ability != null ? EnemyAttackThreatGeometry.ResolveFacingOrigin(actor, ability, impactCenter) : transform.position)
                || EnemyAttackThreatGeometry.IsInsideSectorInset(actor, ability, impactCenter, hitCollider.transform.position))
                continue;

            CombatTarget hitTarget = CombatTarget.Resolve(hitCollider);
            if (hitTarget != null
                && !CombatTargetFilter.CanDamage(combatTarget, hitTarget))
            {
                continue;
            }

            CombatHealth targetHealth = hitTarget != null
                ? hitTarget.DamageReceiver
                : hitCollider.GetComponentInParent<CombatHealth>();
            if (targetHealth == null
                || targetHealth == health
                || targetHealth.IsDead
                || damagedTargets.Contains(targetHealth))
            {
                continue;
            }

            Vector3 targetCenter = hitTarget != null
                ? hitTarget.CurrentVolume.Center
                : hitCollider.bounds.center;
            if (ability != null)
            {
                float verticalDistance =
                    Mathf.Abs(impactCenter.y - targetCenter.y);
                float targetHalfHeight = hitTarget != null
                    ? hitTarget.CurrentVolume.HalfHeight
                    : hitCollider.bounds.extents.y;
                if (verticalDistance
                    > targetHalfHeight + ability.VerticalTolerance)
                {
                    continue;
                }

                if (ability.RequireLineOfSight
                    && hitTarget != null
                    && !HasDirectLineOfSight(
                        hitTarget,
                        targetCenter,
                        impactCenter))
                {
                    continue;
                }
            }

            if (!damagedTargets.Add(targetHealth))
                continue;

            Vector3 hitDirection = targetHealth.transform.position - transform.position;
            hitDirection.y = 0f;
            DamageInfo info = new DamageInfo(
                resolvedDamage,
                hitCollider.ClosestPoint(impactCenter),
                gameObject,
                hitDirection.normalized, sourceAttackSequenceId: attackSequenceId,
                sourceAttackPhaseIndex: attackPhaseIndex, enemyAbility: ability);
            targetHealth.TakeDamage(info);
        }
    }

    private bool IsInFront(Vector3 targetPosition, float resolvedAngle, Vector3? origin = null)
    {
        Vector3 toTarget = targetPosition - (origin ?? transform.position);
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude <= 0.0001f)
            return true;

        float halfAngle = Mathf.Clamp(resolvedAngle, 0f, 360f) * 0.5f;
        float dot = Vector3.Dot(transform.forward, toTarget.normalized);
        return dot >= Mathf.Cos(halfAngle * Mathf.Deg2Rad);
    }

    private bool HasDirectLineOfSight(
        CombatTarget committedTarget,
        Vector3 targetCenter)
    {
        Vector3 origin = attackPoint != null
            ? attackPoint.position
            : transform.position;
        return HasDirectLineOfSight(
            committedTarget,
            targetCenter,
            origin);
    }

    private bool HasDirectLineOfSight(
        CombatTarget committedTarget,
        Vector3 targetCenter,
        Vector3 origin)
    {
        Vector3 delta = targetCenter - origin;
        float distance = delta.magnitude;
        if (distance <= 0.0001f)
            return true;

        int hitCount = Physics.RaycastNonAlloc(
            origin,
            delta / distance,
            lineOfSightBuffer,
            distance,
            ~0,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = lineOfSightBuffer[i].collider;
            if (hitCollider == null)
                continue;

            CombatTarget hitTarget = CombatTarget.Resolve(hitCollider);
            if (hitTarget == combatTarget
                || hitTarget == committedTarget
                || hitTarget != null)
            {
                continue; // 캐릭터 Collider는 시야 장애물로 취급하지 않음
            }

            return false;
        }

        return true;
    }

    private string PickAttackTrigger(out int selectedIndex)
    {
        selectedIndex = -1;
        if (attackTriggers == null || attackTriggers.Length == 0)
            return null;

        if (attackTriggers.Length == 1 || !avoidSameAttackTwice)
        {
            selectedIndex = Random.Range(0, attackTriggers.Length);
            return attackTriggers[selectedIndex];
        }

        selectedIndex = Random.Range(0, attackTriggers.Length);
        if (selectedIndex == lastAttackIndex)
            selectedIndex = (selectedIndex + Random.Range(1, attackTriggers.Length)) % attackTriggers.Length;

        return attackTriggers[selectedIndex];
    }

    private EnemyAbilityDefinition PickConfiguredAbility(out int selectedIndex)
    {
        selectedIndex = -1;
        if (abilitySet == null || !abilitySet.IsValid || target == null)
            return null;

        Vector3 delta = target.position - transform.position;
        delta.y = 0f;
        float distanceSqr = delta.sqrMagnitude;
        int usableCount = 0;
        float usableWeight = 0f;
        for (int i = 0; i < abilitySet.Count; i++)
        {
            EnemyAbilityDefinition candidate = abilitySet.GetAbility(i);
            if (candidate == null || !candidate.IsValid || distanceSqr > candidate.Range * candidate.Range)
                continue;
            usableCount++;
            if (!avoidSameAttackTwice || i != lastAttackIndex)
                usableWeight += candidate.Weight;
        }
        if (usableCount <= 0)
            return null;

        bool excludeLast = avoidSameAttackTwice && usableCount > 1 && usableWeight > 0f;
        if (!excludeLast)
        {
            usableWeight = 0f;
            for (int i = 0; i < abilitySet.Count; i++)
            {
                EnemyAbilityDefinition candidate = abilitySet.GetAbility(i);
                if (candidate != null
                    && candidate.IsValid
                    && distanceSqr <= candidate.Range * candidate.Range)
                {
                    usableWeight += candidate.Weight;
                }
            }
        }

        float cursor = Random.value * usableWeight;
        for (int i = 0; i < abilitySet.Count; i++)
        {
            EnemyAbilityDefinition candidate = abilitySet.GetAbility(i);
            if (candidate == null
                || !candidate.IsValid
                || distanceSqr > candidate.Range * candidate.Range
                || (excludeLast && i == lastAttackIndex))
            {
                continue;
            }

            cursor -= candidate.Weight;
            if (cursor > 0f)
                continue;
            selectedIndex = i;
            return candidate;
        }

        return null;
    }

    private void FaceTargetOnce()
    {
        if (movement != null && movement.Profile != null && movement.Profile.HasTurnAnimation)
            return; // 테마 몬스터는 회전 동작을 완료한 방향으로 공격하며 순간 정렬하지 않는다.
        if (target == null)
            return;

        Vector3 direction = target.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up); // 공격 방향 정렬
    }

    private EnemyAbilityController abilityController;
    private Vector3 ResolveAimPosition(Transform aimTarget)
    {
        if (abilityController == null) abilityController = GetComponent<EnemyAbilityController>();
        return abilityController != null ? abilityController.ResolveAimPosition(aimTarget) : aimTarget.position;
    }

    private void ResolveReferences()
    {
        if (weakMotionDriver == null) weakMotionDriver = GetComponent<EnemyWeakAttackMotionDriver>();
        if (actor == null)
            actor = GetComponent<EnemyActor>();
        if (health == null)
            health = GetComponent<CombatHealth>();
        if (combatTarget == null)
            combatTarget = GetComponent<CombatTarget>();
        if (movement == null)
            movement = GetComponent<EnemyMovement>();
        if (movementReaction == null)
            movementReaction = GetComponent<EnemyMovementReaction>();
        if (animationBridge == null)
            animationBridge = GetComponent<EnemyAnimationBridge>();
        if (attackPoint == null)
        {
            Transform foundPoint = transform.Find("AttackPoint");
            attackPoint = foundPoint != null ? foundPoint : transform;
        }
        if (targetLayer.value == 0)
        {
            int playerLayer = LayerMask.NameToLayer("Player");
            targetLayer = playerLayer >= 0 ? 1 << playerLayer : ~0;
        }
    }

    private float ResolveAttackSpeedMultiplier()
    {
        return Mathf.Max(
            0.01f,
            Mathf.Min(OverburstBalanceTable.Current.EnemyAttackCap, attackSpeedMultiplier * runtimeAttackSpeedMultiplier) * statusActionSpeedMultiplier);
    }

    private float ResolveMaximumAttackRange()
    {
        if (abilitySet == null || !abilitySet.IsValid)
            return Mathf.Max(0f, attackRange);

        float maximum = 0f;
        for (int i = 0; i < abilitySet.Count; i++)
        {
            EnemyAbilityDefinition ability = abilitySet.GetAbility(i);
            if (ability != null && ability.IsValid)
                maximum = Mathf.Max(maximum, EnemyAttackThreatGeometry.ResolveStartRange(actor, ability));
        }
        return maximum;
    }

    private float ResolveScaledTime(float baseTime)
    {
        return Mathf.Max(0f, baseTime) / ResolveAttackSpeedMultiplier();
    }

    private static float ResolveScaledTime(float baseTime, float resolvedAttackSpeed)
    {
        return Mathf.Max(0f, baseTime) / Mathf.Max(0.01f, resolvedAttackSpeed);
    }

    private void ResolveTarget()
    {
        if (target != null)
            return;

        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        target = playerObject != null ? playerObject.transform : null;
    }

    private static CombatTarget ResolveCombatTarget(Transform candidate)
    {
        if (candidate == null)
            return null;

        CombatTarget resolved = candidate.GetComponent<CombatTarget>();
        if (resolved == null)
            resolved = candidate.GetComponentInParent<CombatTarget>();
        if (resolved == null)
            resolved = candidate.GetComponentInChildren<CombatTarget>(true);
        return resolved;
    }

    private bool IsTargetWithinAttackRange(
        Transform candidate,
        float resolvedRange)
    {
        if (candidate == null)
            return false;

        Vector3 delta = candidate.position - transform.position;
        delta.y = 0f;
        return delta.sqrMagnitude <= resolvedRange * resolvedRange;
    }

    private void HandleDead(CombatHealth source, DamageInfo info)
    {
        CancelAttack();
    }

    private void HandleDamaged(CombatHealth source, DamageInfo info)
    {
        if (info.isDamageOverTime || !info.triggersOnHitEffects)
            return; // 직접 피격만 공격 취소
        if (source == null || source.IsDead)
            return;

        CancelAttack();
    }

    public void CancelAttack()
    {
        if (attackRoutine != null)
            StopCoroutine(attackRoutine);

        attackRoutine = null;
        weakMotionDriver?.End();
        activeWeakExecution = null;
        weakAttackClock.Cancel();
        movement?.ClearAttackDisplacement();
        damagedTargets.Clear(); // 타격 대상 정리
        if (movement != null)
            movement.CancelActionLock(); // 피격 반응 우선
    }
}
