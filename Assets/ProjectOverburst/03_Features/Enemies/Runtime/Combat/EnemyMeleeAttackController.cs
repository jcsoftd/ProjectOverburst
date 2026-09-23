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
    private Transform target; // 현재 공격 대상
    private Coroutine attackRoutine; // 진행 중인 공격
    private float nextAttackTime; // 다음 공격 가능 시각
    private int lastAttackIndex = -1; // 직전 공격 모션
    private float statusActionSpeedMultiplier = 1f; // 상태이상 행동 배율
    private float definitionDamageMultiplier = 1f; // 등급·변형 피해 배율
    private float runtimeAttackSpeedMultiplier = 1f; // 등급·변형 공격속도 배율

    public float AttackRange { get { return ResolveMaximumAttackRange(); } }
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
            movementReaction.ReactionStarted -= CancelAttack;
            movementReaction.ReactionStarted += CancelAttack;
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
            movementReaction.ReactionStarted -= CancelAttack;

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

        Vector3 aimPosition = ResolveAimPosition(attackTarget);
        Vector3 delta = aimPosition - transform.position;
        delta.y = 0f;
        return delta.sqrMagnitude <= ability.Range * ability.Range
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
        return ResolveScaledTime(baseCooldown);
    }

    private bool TryStartResolvedAttack(
        EnemyAbilityDefinition ability,
        int selectedIndex,
        bool cooldownOwnedExternally)
    {
        if (ability != null && !ability.IsValid)
            return false;
        float resolvedRange = ability != null ? ability.Range : Mathf.Max(0f, attackRange);
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
        float resolvedCooldown = ability != null ? ability.Cooldown : attackCooldown;
        float resolvedHitDelayBase = ability != null ? ability.HitDelay : hitDelay;
        float resolvedHitNormalizedTime = ability != null ? ability.HitNormalizedTime : hitNormalizedTime;
        float resolvedAttackLockDuration = ability != null ? ability.AttackLockDuration : attackLockDuration;
        float resolvedAnimationDuration = ability != null
            ? ability.AttackAnimationDuration
            : resolvedAttackLockDuration;
        float resolvedRange = ability != null ? ability.Range : attackRange;
        bool keepRangeGate = ability != null
            ? ability.RequireTargetInRangeUntilHit
            : requireTargetInRangeUntilHit;
        if (abilityController != null && abilityController.HasPreparedAim(target)
            && ability != null && ability.ExecutionMode != EnemyAbilityExecutionMode.DirectTarget)
            keepRangeGate = false; // Spatial hit geometry decides whether the committed strike misses.
        bool directTargetExecution = ability != null
            && ability.ExecutionMode == EnemyAbilityExecutionMode.DirectTarget;
        float resolvedAttackSpeed = ResolveAttackSpeedMultiplier();
        if (!cooldownOwnedExternally)
            nextAttackTime = Time.time + ResolveScaledTime(resolvedCooldown, resolvedAttackSpeed);
        if (movement != null)
        {
            float minimumImpactLock =
                resolvedAnimationDuration * (ability != null ? ability.GetHitNormalizedTime(ability.HitCount - 1) : resolvedHitNormalizedTime) + 0.05f;
            movement.ApplyActionLock(
                ResolveScaledTime(
                    Mathf.Max(resolvedAttackLockDuration, minimumImpactLock),
                    resolvedAttackSpeed)); // 실제 접촉 시점 전에 이동 잠금이 풀리지 않음
        }

        if (animationBridge != null)
        {
            animationBridge.SetAttackAnimSpeed(resolvedAttackSpeed); // 공격 속도 적용
            animationBridge.PlayAttack(triggerName); // 선택 공격 재생
        }

        float elapsed = 0f;
        int hitCount = ability != null ? ability.HitCount : 1;
        bool observedAttackAnimation = false;
        for (int impactIndex = 0; impactIndex < hitCount; impactIndex++)
        {
            float impactTime = ability != null ? ability.GetHitNormalizedTime(impactIndex) : resolvedHitNormalizedTime;
            bool stayedInRange = true;
            bool impactReached = false;
            bool useAnimatorTiming = animationBridge != null && animationBridge.HasAnimator;
            float resolvedHitDelay = ResolveScaledTime(impactIndex == 0 ? resolvedHitDelayBase : resolvedAnimationDuration * impactTime, resolvedAttackSpeed); // 애니메이터 미연결 보조 시간
            float attackStateEntryGrace = Mathf.Min(0.35f, Mathf.Max(0.15f, resolvedHitDelay));
            float maximumHitWait = Mathf.Max(
                resolvedHitDelay,
                ResolveScaledTime(resolvedAttackLockDuration, resolvedAttackSpeed) + 0.25f,
                ResolveScaledTime(resolvedAnimationDuration, resolvedAttackSpeed) + 0.15f);
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
                    if (normalizedTime >= Mathf.Clamp01(impactTime))
                    {
                        impactReached = true;
                        break; // 실제 공격 모션 타격 구간
                    }
                }
                else if (!useAnimatorTiming || (!observedAttackAnimation && elapsed >= attackStateEntryGrace))
                {
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
                float resolvedDamage = (ability != null ? ability.Damage : damage) * definitionDamageMultiplier;
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
                    float resolvedRadius = ability != null ? ability.HitRadius : hitRadius;
                    float resolvedAngle = ability != null ? ability.HitAngle : hitAngle;
                    ResolveArcHit(
                        resolvedDamage,
                        resolvedRadius,
                        resolvedAngle,
                        ability);
                }
            }

            if (!stayedInRange) break;
        }

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
        if ((movementReaction != null && movementReaction.IsStunned)
            || (movement != null && movement.IsActionLocked))
            return false;
        if (animationBridge != null && animationBridge.IsBlockingActionActive)
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
        if (movementReaction != null && movementReaction.IsStunned)
            return false;

        return attackPoint != null;
    }

    private bool IsAttackInterrupted()
    {
        if (health != null && health.IsDead)
            return true;
        if (statusActionSpeedMultiplier <= 0f)
            return true;
        if (movementReaction != null && movementReaction.IsStunned)
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
                : transform.forward);
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
        Vector3 impactCenter = isAreaSlam
            ? transform.position
            : attackPoint.position;
        int hitCount = Physics.OverlapSphereNonAlloc(
            impactCenter,
            resolvedRadius,
            hitBuffer,
            targetLayer,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = hitBuffer[i];
            if (hitCollider == null || !IsInFront(hitCollider.transform.position, resolvedAngle))
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
                hitDirection.normalized);
            targetHealth.TakeDamage(info);
        }
    }

    private bool IsInFront(Vector3 targetPosition, float resolvedAngle)
    {
        Vector3 toTarget = targetPosition - transform.position;
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
            attackSpeedMultiplier * runtimeAttackSpeedMultiplier * statusActionSpeedMultiplier);
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
                maximum = Mathf.Max(maximum, ability.Range);
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
        damagedTargets.Clear(); // 타격 대상 정리
        if (movement != null)
            movement.CancelActionLock(); // 피격 반응 우선
    }
}
