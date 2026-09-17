using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CombatHealth))]
public sealed class EnemyCrowdAgent : MonoBehaviour // 군집 조회용 개체 데이터와 등록 생명주기
{
    private const float FallbackBodyRadius = 0.5f;
    private const float EliteCrowdWeightMultiplier = 1.1f;
    internal const float YieldActivationDistance = EnemyCrowdPrioritySolver.YieldActivationDistance;
    private const float YieldDirectionHoldDuration = EnemyCrowdPrioritySolver.YieldDirectionHoldDuration;
    private const float YieldDirectionLockDuration = EnemyCrowdPrioritySolver.YieldDirectionLockDuration;
    private const float YieldReversePressureMultiplier = EnemyCrowdPrioritySolver.YieldReversePressureMultiplier;

    [SerializeField] private CombatHealth health; // 사망 등록 해제
    [SerializeField] private CapsuleCollider bodyCollider; // 몸 반경 원본

    private EnemyAIController controller;
    private EnemyMovement movement;
    private EnemyMovementReaction movementReaction;
    private EnemyRank rank;
    private Vector3 snapshotPosition;
    private Vector3 priorityYieldDirection;
    private float priorityYieldUntilTime;
    private float priorityYieldDirectionLockUntilTime;
    private float priorityYieldPressure;
    private bool subscribed;

    public EnemyAIController Controller
    {
        get
        {
            if (controller == null)
                controller = GetComponent<EnemyAIController>(); // 런타임 조립 순서 보완
            return controller;
        }
    }
    public Vector3 SnapshotPosition => snapshotPosition;
    public bool IsCrowdActive => isActiveAndEnabled && (health == null || !health.IsDead);
    public float BodyRadius => bodyCollider != null
        ? Mathf.Max(bodyCollider.bounds.extents.x, bodyCollider.bounds.extents.z)
        : FallbackBodyRadius;
    public float CrowdWeight => movement != null && movement.Profile != null
        ? movement.Profile.CrowdWeight
        : 1f;
    public int MovePriority => Controller != null ? Controller.CrowdMovePriority : 0;
    public bool IsPriorityYieldEligible => Controller != null
        && EnemySquadPursuitRuntimeService.IsReserveMovement(Controller);
    public bool IsPositionLocked
    {
        get
        {
            string stateName = Controller != null ? Controller.CurrentStateName : string.Empty;
            return stateName == "Attack" || stateName == "Defend";
        }
    }
    public float EffectiveCrowdWeight
    {
        get
        {
            float weight = CrowdWeight;
            if (rank != null && rank.Rank == EnemyRankType.Elite)
                weight *= EliteCrowdWeightMultiplier; // 엘리트 체급 10% 추가
            if (IsPositionLocked)
                return weight * 100f;
            if (movement != null && movement.LocomotionMode == EnemyLocomotionMode.Dodge)
                return weight * 1.75f;

            string stateName = Controller != null ? Controller.CurrentStateName : string.Empty;
            if (stateName == "Reposition")
                return weight * 1.35f;
            if (stateName == "CombatWait")
                return weight * 0.85f;
            return weight;
        }
    }
    public bool IsActivelyMoving
    {
        get
        {
            if (IsPositionLocked)
                return false;
            if (movementReaction != null && movementReaction.IsKnockbackActive)
                return true;
            if (movementReaction != null && movementReaction.IsHitStunActive)
                return false;
            return movement != null
                && movement.HasDestination
                && !movement.IsActionLocked
                && movement.LocomotionMode != EnemyLocomotionMode.Idle;
        }
    }

    private void Awake()
    {
        ResolveReferences();
        snapshotPosition = transform.position;
    }

    private void OnEnable()
    {
        ResolveReferences();
        ResetPriorityYield();
        SubscribeHealth();
        EnemyCrowdService.Register(this); // 활성 군집 등록
    }

    private void OnDisable()
    {
        EnemyCrowdService.Unregister(this); // 비활성 군집 해제
        ResetPriorityYield();
        UnsubscribeHealth();
    }

    internal void CaptureSnapshot()
    {
        snapshotPosition = transform.position; // FixedUpdate 공유 위치
    }

    internal bool TryGetPriorityYieldDirection(out Vector3 direction)
    {
        if (Time.fixedTime <= priorityYieldUntilTime
            && priorityYieldDirection.sqrMagnitude > 0.0001f)
        {
            direction = priorityYieldDirection;
            return true;
        }

        direction = Vector3.zero;
        return false;
    }

    internal void RegisterPriorityYield(Vector3 correction, float pressure)
    {
        correction.y = 0f;
        float distance = correction.magnitude;
        if (!IsPriorityYieldEligible || distance < YieldActivationDistance)
            return;

        Vector3 direction = correction / distance;
        float now = Time.fixedTime;
        bool hasActiveDirection = now <= priorityYieldUntilTime
            && priorityYieldDirection.sqrMagnitude > 0.0001f;
        if (!hasActiveDirection)
        {
            priorityYieldDirection = direction;
            priorityYieldPressure = Mathf.Max(distance, pressure);
            priorityYieldDirectionLockUntilTime = now + YieldDirectionLockDuration;
            priorityYieldUntilTime = now + YieldDirectionHoldDuration;
            return;
        }

        float alignment = Vector3.Dot(priorityYieldDirection, direction);
        float resolvedPressure = Mathf.Max(distance, pressure);
        if (alignment < 0f)
        {
            if (now < priorityYieldDirectionLockUntilTime
                || resolvedPressure < priorityYieldPressure * YieldReversePressureMultiplier)
            {
                priorityYieldUntilTime = now + YieldDirectionHoldDuration;
                return;
            }

            priorityYieldDirection = direction;
            priorityYieldDirectionLockUntilTime = now + YieldDirectionLockDuration;
        }
        else
        {
            priorityYieldDirection = Vector3.Lerp(priorityYieldDirection, direction, 0.35f).normalized;
        }

        priorityYieldPressure = Mathf.Max(priorityYieldPressure * 0.8f, resolvedPressure);
        priorityYieldUntilTime = now + YieldDirectionHoldDuration;
    }

    internal void ResetPriorityYield()
    {
        priorityYieldDirection = Vector3.zero;
        priorityYieldUntilTime = 0f;
        priorityYieldDirectionLockUntilTime = 0f;
        priorityYieldPressure = 0f;
    }

    private void ResolveReferences()
    {
        if (health == null)
            health = GetComponent<CombatHealth>();
        if (bodyCollider == null)
            bodyCollider = GetComponent<CapsuleCollider>();
        if (bodyCollider == null)
            bodyCollider = GetComponentInChildren<CapsuleCollider>();
        if (controller == null)
            controller = GetComponent<EnemyAIController>();
        if (movement == null)
            movement = GetComponent<EnemyMovement>();
        if (movementReaction == null)
            movementReaction = GetComponent<EnemyMovementReaction>();
        if (rank == null)
            rank = GetComponent<EnemyRank>();
    }

    public static float EstimateBodyRadius(GameObject root)
    {
        if (root == null)
            return FallbackBodyRadius;

        CapsuleCollider capsule = root.GetComponent<CapsuleCollider>();
        if (capsule == null)
            capsule = root.GetComponentInChildren<CapsuleCollider>(true);
        if (capsule == null)
            return FallbackBodyRadius;

        Vector3 scale = capsule.transform.lossyScale;
        float horizontalScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        return Mathf.Max(0.05f, capsule.radius * horizontalScale);
    }

    private void SubscribeHealth()
    {
        if (subscribed || health == null)
            return;

        health.OnDead += HandleDead;
        subscribed = true;
    }

    private void UnsubscribeHealth()
    {
        if (!subscribed || health == null)
            return;

        health.OnDead -= HandleDead;
        subscribed = false;
    }

    private void HandleDead(CombatHealth source, DamageInfo info)
    {
        ResetPriorityYield();
        EnemyCrowdService.Unregister(this); // 시체는 이웃 조회에서 즉시 제외
    }
}
