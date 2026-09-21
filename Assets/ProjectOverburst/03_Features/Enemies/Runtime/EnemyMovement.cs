using System.Collections.Generic;
using UnityEngine;

public enum EnemyLocomotionMode
{
    Idle,
    Walk,
    Run,
    Backpedal,
    Dodge
}

[RequireComponent(typeof(CombatHealth))]
[RequireComponent(typeof(EnemyMotor))]
[RequireComponent(typeof(EnemyMovementReaction))]
[RequireComponent(typeof(EnemyLocomotionAnimator))]
[RequireComponent(typeof(EnemyCrowdAgent))]
public sealed class EnemyMovement : MonoBehaviour // AI 이동 명령과 이동 잠금 조정
{
    private const float MaxHardOverlapCorrection = 0.08f;

    [SerializeField] private EnemyMovementProfile profile; // 이동 수치 단일 원본
    [SerializeField] private float runtimeSpeedMultiplier = 1f; // Definition 등급·변형 배율
    [SerializeField] private CombatHealth health; // 사망 확인
    [SerializeField] private EnemyMotor motor; // 실제 이동
    [SerializeField] private EnemyMovementReaction reaction; // 피격 이동
    [SerializeField] private EnemyLocomotionAnimator locomotionAnimator; // 이동 애니메이션
    [SerializeField] private EnemyCrowdAgent crowdAgent; // 군집 몸 반경과 체급

    private Vector3 destination; // AI 목적지
    private float destinationStopDistance; // 목적지 정지 거리
    private float actionLockEndTime; // 공격 이동 잠금 종료
    private bool hasDestination; // 목적지 보유
    private bool hasFacingPosition; // 이동 방향과 별도 시선 위치 보유
    private Vector3 facingPosition; // 시선 고정 위치
    private float movementAnimationAmount = 1f; // 전진 1, 후진 -1
    private float commandSpeedMultiplier = 1f; // AI 이동 명령 배율
    private float statusMoveSpeedMultiplier = 1f; // 상태이상 이동 배율
    private float earthZoneMoveSpeedMultiplier = 1f; // 진흙 장판 전용 이동 배율
    private Vector3 pendingAreaDisplacement; // 자기장 pulse의 다음 FixedUpdate 이동 요청
    private Vector3 pendingAttackDisplacement;

    public bool RequestAttackDisplacement(Vector3 displacement)
    {
        if (!isActiveAndEnabled || !IsActionLocked || IsStatusMovementLocked || health == null || health.IsDead
            || (reaction != null && (reaction.IsHitStunActive || reaction.IsKnockbackActive))) return false;
        displacement.y = 0;
        pendingAttackDisplacement = Vector3.ClampMagnitude(displacement, .35f);
        return true;
    }

    public void ClearAttackDisplacement() { pendingAttackDisplacement = Vector3.zero; }
    private EnemyLocomotionMode locomotionMode = EnemyLocomotionMode.Idle; // 현재 이동 모드
    private readonly List<EnemyCrowdAgent> crowdNeighbors = new List<EnemyCrowdAgent>(16);

    public EnemyMovementProfile Profile { get { return profile; } }
    public float MoveSpeed
    {
        get
        {
            float baseSpeed = profile != null ? profile.MoveSpeed : EnemyMovementProfile.MinimumMoveSpeed;
            return baseSpeed * Mathf.Max(0.01f, runtimeSpeedMultiplier);
        }
    }
    public bool HasDestination { get { return hasDestination; } }
    public bool IsActionLocked { get { return Time.time < actionLockEndTime; } }
    public EnemyLocomotionMode LocomotionMode { get { return locomotionMode; } }
    public float StatusMoveSpeedMultiplier { get { return statusMoveSpeedMultiplier; } }
    public bool IsStatusMovementLocked { get { return statusMoveSpeedMultiplier <= 0f; } }
    public float ActiveMoveSpeed { get { return ResolveMoveSpeed(locomotionMode) * commandSpeedMultiplier * statusMoveSpeedMultiplier * earthZoneMoveSpeedMultiplier; } }
    public float MovementAnimationAmount { get { return movementAnimationAmount; } }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ClearAttackDisplacement();
        ResolveReferences();
        SetStatusMoveSpeedMultiplier(1f);
        SetEarthZoneMoveSpeedMultiplier(1f);
        pendingAreaDisplacement = Vector3.zero;
        hasDestination = false;
        actionLockEndTime = 0f;
        reaction?.ResetReaction();
        ClearMovementCommand();
        StopLocomotionOutput();
    }

    private void OnDisable()
    {
        ClearAttackDisplacement();
        SetStatusMoveSpeedMultiplier(1f);
        SetEarthZoneMoveSpeedMultiplier(1f);
        pendingAreaDisplacement = Vector3.zero;
    }

    private void FixedUpdate()
    {
        Vector3 attackDisplacement = pendingAttackDisplacement;
        pendingAttackDisplacement = Vector3.zero;
        if (health != null && health.IsDead)
        {
            ClearMotorAndAnimation();
            return;
        }

        if (IsStatusMovementLocked)
        {
            PauseMotorAndAnimation(); // 빙결 중 위치·회전·애니메이션 고정
            return;
        }

        if (reaction != null && reaction.IsKnockbackActive)
        {
            reaction.TickFixed();
            StopLocomotionOutput(); // 넉백 중 기존 이동 명령 보존
            return;
        }

        if ((reaction != null && reaction.IsHitStunActive) || IsActionLocked)
        {
            PauseMotorAndAnimation(); // 일시 정지 후 기존 이동 재개
            if (IsActionLocked && (reaction == null || !reaction.IsHitStunActive)
                && attackDisplacement.sqrMagnitude > .000001f && motor != null
                && TryResolveCrowdPosition(transform.position + attackDisplacement, false, out Vector3 attackPosition))
                motor.MoveToPosition(attackPosition);
            return;
        }

        if (locomotionAnimator != null && !locomotionAnimator.AllowsMovement(locomotionMode))
        {
            PauseMotorAndAnimation(); // 정지형 단발 애니메이션이 끝날 때까지 목적지만 보존
            return;
        }

        ApplyPendingAreaDisplacement();
        UpdateDestinationMovement();
    }

    public void SetProfile(EnemyMovementProfile movementProfile)
    {
        profile = movementProfile;
    }

    public void SetRuntimeSpeedMultiplier(float multiplier)
    {
        runtimeSpeedMultiplier = float.IsNaN(multiplier) || float.IsInfinity(multiplier)
            ? 1f
            : Mathf.Max(0.01f, multiplier);
    }

    public void SetStatusMoveSpeedMultiplier(float speedMultiplier)
    {
        statusMoveSpeedMultiplier = float.IsNaN(speedMultiplier) || float.IsInfinity(speedMultiplier)
            ? 1f
            : Mathf.Clamp01(speedMultiplier);

        bool frozen = IsStatusMovementLocked;
        motor?.SetFrozen(frozen);
        locomotionAnimator?.SetFrozen(frozen);
        if (frozen)
            PauseMotorAndAnimation(); // 상태 적용 프레임에 즉시 Idle 전환
    }

    public void SetEarthZoneMoveSpeedMultiplier(float speedMultiplier)
    {
        earthZoneMoveSpeedMultiplier = float.IsNaN(speedMultiplier) || float.IsInfinity(speedMultiplier)
            ? 1f
            : Mathf.Clamp01(speedMultiplier);
    }

    public bool RequestAreaDisplacement(Vector3 displacement)
    {
        displacement.y = 0f;
        if (!isActiveAndEnabled
            || IsStatusMovementLocked
            || health == null
            || health.IsDead
            || (profile != null && profile.KnockbackReductionPercent >= 100f)
            || displacement.sqrMagnitude <= 0.000001f)
        {
            return false;
        }

        pendingAreaDisplacement += Vector3.ClampMagnitude(displacement, 0.25f);
        pendingAreaDisplacement = Vector3.ClampMagnitude(pendingAreaDisplacement, 0.25f);
        return true;
    }

    private void ApplyPendingAreaDisplacement()
    {
        Vector3 displacement = pendingAreaDisplacement;
        pendingAreaDisplacement = Vector3.zero;
        if (displacement.sqrMagnitude <= 0.000001f || motor == null)
            return;

        Vector3 candidate = transform.position + displacement;
        if (TryResolveCrowdPosition(candidate, false, out Vector3 resolved))
            motor.MoveToPosition(resolved);
    }

    public void SetDestination(Vector3 worldPosition, float stoppingDistance)
    {
        SetDestination(worldPosition, stoppingDistance, EnemyLocomotionMode.Walk);
    }

    public void SetDestination(Vector3 worldPosition, float stoppingDistance, EnemyLocomotionMode mode)
    {
        SetDestination(worldPosition, stoppingDistance, mode, 1f);
    }

    public void SetDestination(Vector3 worldPosition, float stoppingDistance, EnemyLocomotionMode mode, float speedMultiplier)
    {
        if (!TryResolveWalkableDestination(worldPosition, out destination))
        {
            StopMovement();
            return;
        }

        destinationStopDistance = Mathf.Max(0f, stoppingDistance);
        hasDestination = true;
        hasFacingPosition = false;
        locomotionMode = ResolveMovingMode(mode);
        movementAnimationAmount = ResolveAnimationAmount(locomotionMode);
        commandSpeedMultiplier = Mathf.Clamp(speedMultiplier, 0.1f, 4f);
    }

    public void SetFacingDestination(Vector3 worldPosition, float stoppingDistance, Vector3 worldFacingPosition, float animationAmount)
    {
        EnemyLocomotionMode mode = animationAmount < -0.1f ? EnemyLocomotionMode.Backpedal : EnemyLocomotionMode.Walk;
        SetFacingDestination(worldPosition, stoppingDistance, worldFacingPosition, mode);
    }

    public void SetFacingDestination(Vector3 worldPosition, float stoppingDistance, Vector3 worldFacingPosition, EnemyLocomotionMode mode)
    {
        SetFacingDestination(worldPosition, stoppingDistance, worldFacingPosition, mode, 1f);
    }

    public void SetFacingDestination(
        Vector3 worldPosition,
        float stoppingDistance,
        Vector3 worldFacingPosition,
        EnemyLocomotionMode mode,
        float speedMultiplier)
    {
        if (!TryResolveWalkableDestination(worldPosition, out destination))
        {
            StopMovement();
            return;
        }

        destinationStopDistance = Mathf.Max(0f, stoppingDistance);
        facingPosition = worldFacingPosition;
        locomotionMode = ResolveMovingMode(mode);
        movementAnimationAmount = ResolveAnimationAmount(locomotionMode);
        commandSpeedMultiplier = Mathf.Clamp(speedMultiplier, 0.1f, 4f);
        hasDestination = true;
        hasFacingPosition = true;
    }

    public void StopMovement()
    {
        hasDestination = false;
        hasFacingPosition = false;
        ClearMovementCommand();
        if (reaction == null || !reaction.IsKnockbackActive)
            motor?.HoldPosition();
        StopLocomotionOutput();
    }

    public void ApplyActionLock(float duration)
    {
        if (health != null && health.IsDead)
            return;

        float resolvedDuration = Mathf.Max(0f, duration);
        if (resolvedDuration <= 0f)
            return;

        actionLockEndTime = Mathf.Max(actionLockEndTime, Time.time + resolvedDuration);
        PauseMotorAndAnimation();
    }

    public void FacePosition(Vector3 worldPosition)
    {
        if (IsStatusMovementLocked)
            return;

        Vector3 direction = worldPosition - transform.position;
        float turnSpeed = profile != null ? profile.TurnSpeed : 360f;
        motor?.Face(direction, turnSpeed);
    }

    public bool IsWalkablePosition(Vector3 worldPosition)
    {
        RunWalkableArea walkableArea = RunWalkableContext.Current;
        return walkableArea == null || walkableArea.IsWalkable(worldPosition); // 비스테이지에서는 기존 직선 이동 유지
    }

    public bool TryResolveWalkableDestination(Vector3 worldPosition, out Vector3 resolvedPosition)
    {
        resolvedPosition = worldPosition;
        RunWalkableArea walkableArea = RunWalkableContext.Current;
        if (walkableArea == null || walkableArea.IsWalkable(worldPosition))
            return true;

        if (!walkableArea.TryFindNearestWalkable(new Vector2(worldPosition.x, worldPosition.z), out Vector2 nearest))
            return false;

        resolvedPosition.x = nearest.x;
        resolvedPosition.z = nearest.y;
        return true;
    }

    public void CancelActionLock()
    {
        actionLockEndTime = 0f;
    }

    public void StopForDeath()
    {
        hasDestination = false;
        actionLockEndTime = 0f;
        reaction?.ResetReaction();
        ClearMotorAndAnimation();
        enabled = false;
    }

    public void ResolveReferences()
    {
        if (health == null)
            health = GetComponent<CombatHealth>();
        if (motor == null)
            motor = GetComponent<EnemyMotor>();
        if (reaction == null)
            reaction = GetComponent<EnemyMovementReaction>();
        if (locomotionAnimator == null)
            locomotionAnimator = GetComponent<EnemyLocomotionAnimator>();
        if (crowdAgent == null)
            crowdAgent = GetComponent<EnemyCrowdAgent>();

        motor?.ResolveReferences();
        reaction?.ResolveReferences();
        locomotionAnimator?.ResolveReferences();
    }

    public bool TryResolveCrowdPosition(Vector3 candidate, out Vector3 resolvedPosition)
    {
        return TryResolveCrowdPosition(candidate, true, out resolvedPosition);
    }

    public bool TryResolveCrowdPosition(Vector3 candidate, bool allowTangent, out Vector3 resolvedPosition)
    {
        resolvedPosition = candidate;
        if (crowdAgent == null || !crowdAgent.IsCrowdActive)
            return true;

        float ownRadius = crowdAgent.BodyRadius;
        EnemyCrowdService.CollectPotentialOverlaps(candidate, ownRadius, crowdNeighbors);
        Vector3 correction = Vector3.zero;
        bool hasOverlap = false;
        EnemyCrowdAgent fallbackNeighbor = null;
        float strongestCorrection = 0f;

        for (int i = 0; i < crowdNeighbors.Count; i++)
        {
            EnemyCrowdAgent other = crowdNeighbors[i];
            if (other == null || other == crowdAgent || !other.IsCrowdActive)
                continue;

            Vector3 delta = candidate - other.SnapshotPosition;
            delta.y = 0f;
            float minimumDistance = ownRadius + other.BodyRadius;
            float sqrDistance = delta.sqrMagnitude;
            if (sqrDistance >= minimumDistance * minimumDistance)
                continue;

            float distance = Mathf.Sqrt(sqrDistance);
            float penetration = minimumDistance - distance;
            Vector3 direction = distance > 0.0001f
                ? delta / distance
                : EnemyCrowdService.ResolvePairDirection(crowdAgent, other);
            float ownShare = ResolveOwnCorrectionShare(crowdAgent, other);
            float correctionAmount = penetration * ownShare;
            correction += direction * correctionAmount;
            if (fallbackNeighbor == null || correctionAmount > strongestCorrection)
            {
                fallbackNeighbor = other;
                strongestCorrection = correctionAmount;
            }
            hasOverlap = true;
        }

        if (!hasOverlap)
            return true;
        if (correction.sqrMagnitude <= 0.000001f)
        {
            if (fallbackNeighbor == null || strongestCorrection <= 0f)
                return true;
            correction = EnemyCrowdService.ResolvePairDirection(crowdAgent, fallbackNeighbor)
                * strongestCorrection; // 대칭 압력 합이 0이어도 안정적으로 분리
        }

        correction = Vector3.ClampMagnitude(correction, MaxHardOverlapCorrection);
        Vector3 corrected = candidate + correction;
        if (IsWalkablePosition(corrected))
        {
            resolvedPosition = corrected;
            return true;
        }

        float candidatePenetration = EnemyCrowdService.CalculatePenetration(
            candidate,
            ownRadius,
            crowdAgent,
            crowdNeighbors);
        if (allowTangent && TryResolveTangentCandidate(candidate, correction, candidatePenetration, out resolvedPosition))
            return true;

        Vector3 currentPosition = motor != null ? motor.Position : transform.position;
        float currentPenetration = EnemyCrowdService.CalculatePenetration(
            currentPosition,
            ownRadius,
            crowdAgent,
            crowdNeighbors);
        if (IsWalkablePosition(currentPosition) && currentPenetration <= candidatePenetration)
        {
            resolvedPosition = currentPosition;
            return false;
        }

        resolvedPosition = candidate; // 이미 겹친 상태라면 더 적게 겹치는 전진은 허용
        return true;
    }

    private void UpdateDestinationMovement()
    {
        if (!hasDestination)
        {
            ClearMotorAndAnimation();
            return;
        }

        Vector3 delta = destination - transform.position;
        delta.y = 0f;
        if (delta.sqrMagnitude <= destinationStopDistance * destinationStopDistance)
        {
            hasDestination = false;
            hasFacingPosition = false;
            ClearMotorAndAnimation();
            return;
        }

        float moveSpeed = ActiveMoveSpeed;
        float turnSpeed = profile != null ? profile.TurnSpeed : 360f;
        Vector3 currentPosition = motor != null ? motor.Position : transform.position;
        Vector3 nextPosition = currentPosition + delta.normalized * moveSpeed * Time.fixedDeltaTime;
        if (!IsWalkablePosition(nextPosition))
        {
            StopMovement(); // AI 이동으로 KillOnExit 경계를 넘지 않음
            return;
        }

        Vector3 facingDirection = hasFacingPosition ? facingPosition - transform.position : delta;
        if (EnemyCrowdService.SubmitMovementIntent(
            this,
            crowdAgent,
            currentPosition,
            nextPosition,
            turnSpeed,
            facingDirection,
            moveSpeed))
        {
            return; // 모든 개체의 후보가 모인 뒤 중앙 드라이버가 한 번만 이동
        }

        bool canAdvance = TryResolveCrowdPosition(nextPosition, true, out Vector3 resolvedPosition);
        if (!canAdvance && (resolvedPosition - currentPosition).sqrMagnitude <= 0.000001f)
        {
            motor?.HoldPosition();
            StopLocomotionOutput(); // 군집에 막힌 동안 제자리 발놀림 방지
            return;
        }

        motor?.MoveToPosition(resolvedPosition, turnSpeed, facingDirection);
        locomotionAnimator?.SetMovement(movementAnimationAmount, moveSpeed, ResolveAnimationReferenceSpeed());
    }

    internal Vector3 ResolveCentralCrowdCandidate(
        Vector3 desiredPosition,
        Vector3 resolvedPosition,
        EnemyCrowdAgent agent)
    {
        if (IsWalkablePosition(resolvedPosition))
            return resolvedPosition;

        Vector3 correction = resolvedPosition - desiredPosition;
        correction.y = 0f;
        float correctionDistance = correction.magnitude;
        if (correctionDistance <= 0.0001f)
            return desiredPosition;

        Vector3 tangent = new Vector3(-correction.z, 0f, correction.x).normalized;
        if (agent != null && (agent.GetInstanceID() & 1) != 0)
            tangent = -tangent;
        for (int i = 0; i < 2; i++)
        {
            Vector3 tangentCandidate = desiredPosition + tangent * correctionDistance;
            if (IsWalkablePosition(tangentCandidate))
                return tangentCandidate;
            tangent = -tangent;
        }

        if (agent != null && agent.TryGetPriorityYieldDirection(out Vector3 yieldDirection))
        {
            Vector3 yieldCandidate = desiredPosition + yieldDirection * correctionDistance;
            if (IsWalkablePosition(yieldCandidate))
                return yieldCandidate;
        }

        return desiredPosition; // 벽 밖으로 밀지 않고 원래 보행 가능 이동만 유지
    }

    internal void ApplyCentralCrowdMovement(
        Vector3 currentPosition,
        Vector3 resolvedPosition,
        float turnSpeed,
        Vector3 facingDirection,
        float commandedMoveSpeed)
    {
        if ((health != null && health.IsDead)
            || IsStatusMovementLocked
            || reaction != null && (reaction.IsKnockbackActive || reaction.IsHitStunActive)
            || IsActionLocked
            || locomotionAnimator != null && !locomotionAnimator.AllowsMovement(locomotionMode))
        {
            PauseMotorAndAnimation();
            return;
        }

        Vector3 actualMovement = resolvedPosition - currentPosition;
        actualMovement.y = 0f;
        if (actualMovement.sqrMagnitude <= 0.000001f)
        {
            motor?.HoldPosition();
            StopLocomotionOutput();
            return;
        }

        motor?.MoveToPosition(resolvedPosition, turnSpeed, facingDirection);
        float actualMoveSpeed = actualMovement.magnitude / Mathf.Max(0.0001f, Time.fixedDeltaTime);
        locomotionAnimator?.SetMovement(
            movementAnimationAmount,
            Mathf.Max(commandedMoveSpeed, actualMoveSpeed),
            ResolveAnimationReferenceSpeed());
    }

    private bool TryResolveTangentCandidate(
        Vector3 candidate,
        Vector3 correction,
        float candidatePenetration,
        out Vector3 resolvedPosition)
    {
        resolvedPosition = candidate;
        float correctionDistance = correction.magnitude;
        if (correctionDistance <= 0.0001f)
            return false;

        Vector3 tangent = new Vector3(-correction.z, 0f, correction.x).normalized;
        if ((crowdAgent.GetInstanceID() & 1) != 0)
            tangent = -tangent;

        float ownRadius = crowdAgent.BodyRadius;
        for (int i = 0; i < 2; i++)
        {
            Vector3 tangentCandidate = candidate + tangent * correctionDistance;
            if (IsWalkablePosition(tangentCandidate))
            {
                float penetration = EnemyCrowdService.CalculatePenetration(
                    tangentCandidate,
                    ownRadius,
                    crowdAgent,
                    crowdNeighbors);
                if (penetration < candidatePenetration)
                {
                    resolvedPosition = tangentCandidate;
                    return true;
                }
            }

            tangent = -tangent;
        }

        return false;
    }

    private static float ResolveOwnCorrectionShare(EnemyCrowdAgent own, EnemyCrowdAgent other)
    {
        if (own.IsPositionLocked)
            return 0f;
        if (other.IsPositionLocked)
            return 1f;
        if (own.MovePriority > other.MovePriority)
            return 0.1f; // 슬롯 이동권 보유자는 외곽 대기 개체를 밀어내며 통과
        if (own.MovePriority < other.MovePriority)
            return 0.9f;
        if (!other.IsActivelyMoving)
            return 1f; // 정지 개체는 스스로 보정하지 않으므로 이동 개체가 전량 양보

        float ownWeight = own.EffectiveCrowdWeight;
        float otherWeight = other.EffectiveCrowdWeight;
        return EnemyCrowdPrioritySolver.ResolvePriorityCorrectionShare(
            ownWeight,
            otherWeight,
            own.MovePriority,
            other.MovePriority);
    }

    private void PauseMotorAndAnimation()
    {
        motor?.HoldPosition();
        StopLocomotionOutput();
    }

    private void ClearMotorAndAnimation()
    {
        motor?.HoldPosition();
        ClearMovementCommand();
        StopLocomotionOutput();
    }

    private void ClearMovementCommand()
    {
        locomotionMode = EnemyLocomotionMode.Idle;
        movementAnimationAmount = 0f;
        commandSpeedMultiplier = 1f;
    }

    private void StopLocomotionOutput()
    {
        locomotionAnimator?.Stop(MoveSpeed, ResolveAnimationReferenceSpeed());
    }

    private float ResolveMoveSpeed(EnemyLocomotionMode mode)
    {
        float multiplier = 1f;
        if (mode == EnemyLocomotionMode.Run)
            multiplier = profile != null ? profile.RunSpeedMultiplier : EnemyMovementProfile.DefaultRunSpeedMultiplier;
        else if (mode == EnemyLocomotionMode.Dodge)
            multiplier = profile != null ? profile.DodgeSpeedMultiplier : EnemyMovementProfile.DefaultDodgeSpeedMultiplier;

        return MoveSpeed * multiplier;
    }

    private static EnemyLocomotionMode ResolveMovingMode(EnemyLocomotionMode mode)
    {
        return mode == EnemyLocomotionMode.Idle ? EnemyLocomotionMode.Walk : mode;
    }

    private static float ResolveAnimationAmount(EnemyLocomotionMode mode)
    {
        if (mode == EnemyLocomotionMode.Backpedal)
            return -1f;
        if (mode == EnemyLocomotionMode.Run)
            return 2f;
        if (mode == EnemyLocomotionMode.Dodge)
            return 1f; // Dodge 트리거 종료 후 남은 이동은 Walk로 이어짐
        return 1f;
    }

    private float ResolveAnimationReferenceSpeed()
    {
        return profile != null
            ? profile.GetAnimationReferenceSpeed(locomotionMode)
            : EnemyMovementProfile.MinimumMoveSpeed;
    }
}
