using UnityEngine;

public sealed class EnemyRepositionState : IEnemyState // 후퇴와 전진 도약 이동 통합
{
    private enum RepositionMode
    {
        Backpedal,
        DodgeLunge
    }

    private readonly EnemyAIController owner;
    private RepositionMode pendingMode;
    private RepositionMode activeMode;
    private float actionStartTime;
    private float actionDuration;
    private float endTime;
    private bool pendingLowHealthBackstep;
    private bool dodgeAnimationTracked;
    private Transform visualRoot;
    private float visualBaseLocalY;

    public string Name { get { return "Reposition"; } }
    internal string DebugModeName => activeMode.ToString(); // 상태 디버그 표시용 하위 모드

    public EnemyRepositionState(EnemyAIController owner)
    {
        this.owner = owner;
    }

    public void PrepareBackpedal(bool lowHealthBackstep)
    {
        pendingMode = RepositionMode.Backpedal;
        pendingLowHealthBackstep = lowHealthBackstep;
    }

    public void PrepareDodgeLunge()
    {
        pendingMode = RepositionMode.DodgeLunge;
        pendingLowHealthBackstep = false;
    }

    public void Enter()
    {
        if (!owner.TryResolveTarget())
        {
            owner.ChangeToReturn();
            return;
        }

        EnemyBehaviorProfile profile = owner.BehaviorProfile;
        activeMode = pendingMode;
        pendingMode = RepositionMode.Backpedal;
        bool lowHealthBackstep = pendingLowHealthBackstep;
        pendingLowHealthBackstep = false;
        dodgeAnimationTracked = false;

        if (activeMode == RepositionMode.DodgeLunge)
            StartDodgeLunge(profile);
        else
            StartBackpedal(profile, lowHealthBackstep);
    }

    public void Update()
    {
        if (owner.ShouldReturnFromCombat())
        {
            owner.ChangeToReturn();
            return;
        }

        if (activeMode == RepositionMode.DodgeLunge)
            UpdateDodgeVisual();

        if (Time.time >= endTime
            || (dodgeAnimationTracked && owner.AnimationBridge != null && !owner.AnimationBridge.IsBlockingActionActive))
            owner.ChangeToCombatWait(owner.BehaviorProfile.RecoveryDuration);
    }

    public void Exit()
    {
        dodgeAnimationTracked = false;
        ResetDodgeVisual();
        owner.Movement?.StopMovement();
    }

    private void StartBackpedal(EnemyBehaviorProfile profile, bool lowHealthBackstep)
    {
        Vector3 away = owner.transform.position - owner.Target.position;
        away.y = 0f;
        if (away.sqrMagnitude <= 0.0001f)
            away = -owner.transform.forward;

        float moveDistance = lowHealthBackstep ? profile.LowHealthRepositionDistance : profile.RepositionDistance;
        Vector3 destination = owner.transform.position + away.normalized * moveDistance;
        owner.Movement?.SetFacingDestination(
            destination,
            0.1f,
            owner.Target.position,
            EnemyLocomotionMode.Backpedal);

        actionDuration = lowHealthBackstep ? profile.LowHealthRepositionDuration : profile.RepositionDuration;
        actionStartTime = Time.time;
        endTime = actionStartTime + actionDuration;
    }

    private void StartDodgeLunge(EnemyBehaviorProfile profile)
    {
        Vector3 towardTarget = owner.Target.position - owner.transform.position;
        towardTarget.y = 0f;
        float targetDistance = towardTarget.magnitude;
        if (targetDistance <= 0.0001f || owner.Movement == null)
        {
            owner.ChangeToCombatWait();
            return;
        }

        float landingDistance = Mathf.Max(0.35f, owner.AttackEnterRange * 0.75f);
        float travelDistance = Mathf.Min(
            profile.DodgeLungeDistance,
            Mathf.Max(0f, targetDistance - landingDistance));
        if (travelDistance <= 0.1f)
        {
            owner.ChangeToCombatWait();
            return;
        }

        actionDuration = profile.DodgeLungeDuration;
        actionStartTime = Time.time;
        endTime = actionStartTime + actionDuration;
        Vector3 destination = owner.transform.position + towardTarget.normalized * travelDistance;
        float baseDodgeSpeed = owner.Movement.MoveSpeed
            * (owner.Movement.Profile != null
                ? owner.Movement.Profile.DodgeSpeedMultiplier
                : EnemyMovementProfile.DefaultDodgeSpeedMultiplier);
        float requiredSpeed = travelDistance / actionDuration;
        float speedMultiplier = requiredSpeed / Mathf.Max(EnemyMovementProfile.MinimumMoveSpeed, baseDodgeSpeed);

        owner.Movement.FacePosition(owner.Target.position);
        owner.Movement.SetDestination(
            destination,
            0.05f,
            EnemyLocomotionMode.Dodge,
            speedMultiplier);
        if (!owner.Movement.HasDestination)
        {
            owner.ChangeToCombatWait();
            return;
        }

        CacheVisualRoot();
        dodgeAnimationTracked = owner.AnimationBridge != null && owner.AnimationBridge.PlayDodge();
    }

    private void CacheVisualRoot()
    {
        visualRoot = owner.transform.Find("VisualRoot");
        if (visualRoot != null)
            visualBaseLocalY = visualRoot.localPosition.y;
    }

    private void UpdateDodgeVisual()
    {
        if (visualRoot == null)
            return;

        float progress = Mathf.Clamp01((Time.time - actionStartTime) / Mathf.Max(0.1f, actionDuration));
        float arc = 4f * progress * (1f - progress) * owner.BehaviorProfile.DodgeVisualHeight;
        Vector3 localPosition = visualRoot.localPosition;
        localPosition.y = visualBaseLocalY + arc;
        visualRoot.localPosition = localPosition; // 충돌체는 지면에 두고 비주얼 Y만 도약
    }

    private void ResetDodgeVisual()
    {
        if (visualRoot != null)
        {
            Vector3 localPosition = visualRoot.localPosition;
            localPosition.y = visualBaseLocalY;
            visualRoot.localPosition = localPosition;
        }

        visualRoot = null;
        visualBaseLocalY = 0f;
    }
}
