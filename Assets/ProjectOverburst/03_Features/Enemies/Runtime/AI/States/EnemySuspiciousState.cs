using UnityEngine;

public sealed class EnemySuspiciousState : IEnemyState // 약한 소리 확인과 저속 조사 통합
{
    private const float DestinationRefreshInterval = 0.5f;

    private enum SuspiciousMode
    {
        Notice,
        Investigate,
        LookAround
    }

    private readonly EnemyAIController owner;
    private Vector3 progressCheckPosition;
    private Vector3 lookDirection;
    private float lastHeardTime;
    private float modeEndTime;
    private float nextDestinationRefreshTime;
    private float nextProgressCheckTime;
    private float nextLookTime;
    private SuspiciousMode mode;

    public string Name => "Suspicious";
    internal string DebugModeName => mode.ToString(); // 상태 디버그 표시용 하위 모드

    public EnemySuspiciousState(EnemyAIController owner)
    {
        this.owner = owner;
    }

    public void Enter()
    {
        owner.CancelAttack();
        owner.RememberTargetSound();
        lastHeardTime = Time.time;

        if (owner.IsTargetWithin(owner.InvestigateRange))
            StartInvestigate();
        else
            StartNotice();
    }

    public void Update()
    {
        if (!owner.TryResolveTarget())
        {
            owner.ChangeToRoam();
            return;
        }

        if (owner.CanDiscoverTarget())
        {
            owner.DiscoverTarget();
            return;
        }

        if (owner.IsTargetWithin(owner.InvestigateRange))
        {
            owner.RememberTargetSound();
            lastHeardTime = Time.time;
            if (mode == SuspiciousMode.Notice)
            {
                StartInvestigate();
                return;
            }
        }

        if (mode == SuspiciousMode.Notice)
        {
            FaceLastHeardPosition();
            if (Time.time >= modeEndTime)
                owner.ChangeToRoam();
            return;
        }

        if (Time.time - lastHeardTime >= owner.BehaviorProfile.InvestigateMemoryDuration)
        {
            owner.ChangeToReturn();
            return;
        }

        if (mode == SuspiciousMode.LookAround)
        {
            owner.Movement?.FacePosition(owner.transform.position + lookDirection);
            if (Time.time >= modeEndTime)
                StartInvestigate();
            return;
        }

        if (Time.time >= nextLookTime)
        {
            BeginLookAround();
            return;
        }

        if (owner.Movement == null || !owner.Movement.HasDestination)
        {
            BeginLookAround();
            return;
        }

        if (Time.time >= nextDestinationRefreshTime)
            RefreshInvestigateDestination();

        if (Time.time < nextProgressCheckTime)
            return;

        EnemyBehaviorProfile profile = owner.BehaviorProfile;
        Vector3 moved = owner.transform.position - progressCheckPosition;
        moved.y = 0f;
        if (moved.sqrMagnitude < profile.PatrolStuckMinDistance * profile.PatrolStuckMinDistance)
        {
            BeginLookAround();
            return;
        }

        ResetProgressCheck();
    }

    public void Exit()
    {
        owner.Movement?.StopMovement();
    }

    private void StartNotice()
    {
        owner.Movement?.StopMovement();
        owner.BeginNoticeCooldown();
        mode = SuspiciousMode.Notice;
        modeEndTime = Time.time + owner.BehaviorProfile.NoticeDuration;
        FaceLastHeardPosition();
    }

    private void StartInvestigate()
    {
        mode = SuspiciousMode.Investigate;
        nextLookTime = Time.time + Random.Range(1.1f, 1.8f);
        ResetProgressCheck();
        RefreshInvestigateDestination();
    }

    private void RefreshInvestigateDestination()
    {
        owner.Movement?.SetDestination(
            owner.LastHeardPosition,
            0.4f,
            EnemyLocomotionMode.Walk,
            owner.BehaviorProfile.InvestigateSpeedMultiplier);
        nextDestinationRefreshTime = Time.time + DestinationRefreshInterval;
    }

    private void BeginLookAround()
    {
        owner.Movement?.StopMovement();
        Vector3 baseDirection = owner.LastHeardPosition - owner.transform.position;
        baseDirection.y = 0f;
        if (baseDirection.sqrMagnitude <= 0.0001f)
            baseDirection = owner.transform.forward;

        float angle = Random.Range(45f, 100f) * (Random.value < 0.5f ? -1f : 1f);
        lookDirection = Quaternion.Euler(0f, angle, 0f) * baseDirection.normalized;
        modeEndTime = Time.time + Random.Range(0.35f, 0.65f);
        mode = SuspiciousMode.LookAround;
    }

    private void FaceLastHeardPosition()
    {
        owner.Movement?.FacePosition(owner.LastHeardPosition);
    }

    private void ResetProgressCheck()
    {
        progressCheckPosition = owner.transform.position;
        nextProgressCheckTime = Time.time + owner.BehaviorProfile.PatrolStuckCheckDuration;
    }
}
