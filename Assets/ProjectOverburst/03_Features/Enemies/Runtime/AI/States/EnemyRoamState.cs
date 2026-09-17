using UnityEngine;

public sealed class EnemyRoamState : IEnemyState // 평시 대기와 순찰 통합
{
    private enum RoamMode
    {
        Idle,
        Patrol,
        ObstacleTurn
    }

    private readonly EnemyAIController owner;
    private Vector3 progressCheckPosition;
    private Vector3 turnDirection;
    private float modeEndTime;
    private float nextProgressCheckTime;
    private RoamMode mode;

    public string Name => "Roam";
    internal string DebugModeName => mode.ToString(); // 상태 디버그 표시용 하위 모드

    public EnemyRoamState(EnemyAIController owner)
    {
        this.owner = owner;
    }

    public void Enter()
    {
        owner.CancelAttack();
        SelectNextMode();
    }

    public void Update()
    {
        if (owner.TryEvaluateRoamAwareness())
            return;

        if (mode == RoamMode.Idle)
        {
            owner.TryPlayIdleBreak();
            if (Time.time >= modeEndTime)
                SelectNextMode();
            return;
        }

        if (mode == RoamMode.ObstacleTurn)
        {
            owner.Movement?.FacePosition(owner.transform.position + turnDirection);
            if (Time.time >= modeEndTime && !TryStartPatrol())
                StartIdle();
            return;
        }

        if (owner.Movement == null || !owner.Movement.HasDestination)
        {
            SelectNextMode();
            return;
        }

        if (Time.time < nextProgressCheckTime)
            return;

        EnemyBehaviorProfile profile = owner.BehaviorProfile;
        Vector3 moved = owner.transform.position - progressCheckPosition;
        moved.y = 0f;
        if (moved.sqrMagnitude < profile.PatrolStuckMinDistance * profile.PatrolStuckMinDistance)
        {
            BeginObstacleTurn();
            return;
        }

        ResetProgressCheck();
    }

    public void Exit()
    {
        owner.Movement?.StopMovement();
    }

    private void SelectNextMode()
    {
        if (Random.value < owner.BehaviorProfile.PatrolChance && TryStartPatrol())
            return;

        StartIdle();
    }

    private void StartIdle()
    {
        owner.Movement?.StopMovement();
        EnemyBehaviorProfile profile = owner.BehaviorProfile;
        mode = RoamMode.Idle;
        modeEndTime = Time.time + Random.Range(profile.IdleDurationMin, profile.IdleDurationMax);
    }

    private bool TryStartPatrol()
    {
        if (!owner.TryChoosePatrolDestination(out Vector3 destination))
            return false;

        EnemyBehaviorProfile profile = owner.BehaviorProfile;
        owner.Movement?.SetDestination(
            destination,
            0.15f,
            EnemyLocomotionMode.Walk,
            profile.PatrolSpeedMultiplier);
        if (owner.Movement == null || !owner.Movement.HasDestination)
            return false;

        mode = RoamMode.Patrol;
        ResetProgressCheck();
        return true;
    }

    private void BeginObstacleTurn()
    {
        owner.Movement?.StopMovement();
        float angle = Random.Range(60f, 160f) * (Random.value < 0.5f ? -1f : 1f);
        turnDirection = Quaternion.Euler(0f, angle, 0f) * owner.transform.forward;
        modeEndTime = Time.time + Random.Range(0.3f, 0.55f);
        mode = RoamMode.ObstacleTurn;
    }

    private void ResetProgressCheck()
    {
        progressCheckPosition = owner.transform.position;
        nextProgressCheckTime = Time.time + owner.BehaviorProfile.PatrolStuckCheckDuration;
    }
}
