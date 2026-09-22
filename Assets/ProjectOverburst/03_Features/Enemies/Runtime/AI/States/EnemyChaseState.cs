using UnityEngine;

public sealed class EnemyChaseState : IEnemyState // 발견 반응과 거리 기반 접근 통합
{
    private const float ProgressCheckInterval = 0.75f;
    private const float MinimumProgressDistance = 0.08f;

    private enum ChaseMode
    {
        Alerting,
        Approach
    }

    private readonly EnemyAIController owner;
    private ChaseMode mode;
    private float alertEndTime;
    private float nextProgressCheckTime;
    private Vector3 lastProgressPosition;

    public string Name => "Chase";
    internal string DebugModeName => mode == ChaseMode.Approach
        && !string.IsNullOrEmpty(owner.SquadPursuitDebugModeName)
        ? owner.SquadPursuitDebugModeName
        : mode == ChaseMode.Approach && owner.IsClusterFanOutActive
        ? "FanOut"
        : mode == ChaseMode.Approach && owner.IsChaseBypassActive
            ? "Bypass"
            : mode.ToString(); // 상태 디버그 표시용 하위 모드

    public EnemyChaseState(EnemyAIController owner)
    {
        this.owner = owner;
    }

    public void Enter()
    {
        owner.Movement?.StopMovement();
        owner.CancelAttack();

        if (owner.ConsumePendingAlertReaction(out bool callsSupport))
        {
            mode = ChaseMode.Alerting;
            alertEndTime = Time.time + owner.BehaviorProfile.AlertDuration;
            if (callsSupport || owner.BehaviorProfile.PlayTauntOnAlert)
                owner.AnimationBridge?.PlayTaunt();
            if (callsSupport)
                owner.BroadcastSupportCall();
            return;
        }

        BeginApproach();
    }

    public void Update()
    {
        if (owner.ShouldReturnFromCombat())
        {
            owner.ChangeToReturn();
            return;
        }

        if (mode == ChaseMode.Alerting)
        {
            owner.FaceTarget();
            if (Time.time < alertEndTime)
                return;

            BeginApproach();
        }

        if (owner.TryHandleTacticalCombat()) return;

        if (owner.IsTargetWithin(owner.AttackEnterRange))
        {
            owner.ChangeToAttack(); // 공격 가능 거리에서는 자리 찾기보다 공격을 우선
            return;
        }

        if (owner.TryEnterDodgeLunge())
            return;

        UpdateApproach(owner.ResolveChasePlan());
    }

    public void Exit()
    {
        owner.EndChaseApproach();
        owner.Movement?.StopMovement();
    }

    private void BeginApproach()
    {
        mode = ChaseMode.Approach;
        if (owner.UsesRangedTactics) return;
        if (owner.IsTargetWithin(owner.AttackEnterRange))
        {
            owner.Movement?.StopMovement(); // 사거리 내 진입은 조향 목적지를 만들지 않음
            lastProgressPosition = owner.transform.position;
            nextProgressCheckTime = Time.time + ProgressCheckInterval;
            return;
        }

        bool preferSide = owner.BehaviorProfile.Tendency == EnemyBehaviorTendency.Disruptor;
        owner.BeginChaseApproach(preferSide);
        lastProgressPosition = owner.transform.position;
        nextProgressCheckTime = Time.time + ProgressCheckInterval;
        UpdateApproach(owner.ResolveChasePlan());
    }

    private void UpdateApproach(Vector3 chaseDestination)
    {
        owner.Movement?.SetDestination(
            chaseDestination,
            0.15f,
            owner.SelectChaseLocomotion(),
            owner.CurrentChaseSpeedMultiplier);

        if (Time.time < nextProgressCheckTime || owner.Movement == null)
            return;

        Vector3 delta = owner.transform.position - lastProgressPosition;
        delta.y = 0f;
        if (owner.Movement.HasDestination
            && delta.sqrMagnitude < MinimumProgressDistance * MinimumProgressDistance)
        {
            owner.RefreshChaseApproachDirection();
        }

        lastProgressPosition = owner.transform.position;
        nextProgressCheckTime = Time.time + ProgressCheckInterval;
    }
}
