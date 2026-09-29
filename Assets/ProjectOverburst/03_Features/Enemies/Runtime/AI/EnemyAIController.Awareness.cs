using System.Collections.Generic;
using UnityEngine;

// EnemyAIController partial: 대상 탐지·어그로·지원 호출. 필드와 Unity 수명주기는 EnemyAIController.cs에 있다.
public sealed partial class EnemyAIController
{
    public void RequestAggro(Transform aggroTarget)
    {
        RequestAggro(aggroTarget, false);
    }

    private void RequestAggro(Transform aggroTarget, bool evaluateDirectAttacker)
    {
        if (health != null && health.IsDead)
            return;

        ResetAggroReleaseCandidate(); // 피격·외부 요청은 어그로 유지 시간을 새로 시작
        RequestImmediateAiTick();

        Transform preferredTarget = aggroTarget != null ? aggroTarget : target;
        Transform assignedTarget;
        bool assigned = evaluateDirectAttacker
            ? EnemyCombatCoordinator.TryRefreshLocalEngagement(this, preferredTarget, out assignedTarget)
            : EnemyCombatCoordinator.TryStartLeaderApproach(this, preferredTarget, out assignedTarget);
        if (assigned)
        {
            ApplyTarget(assignedTarget);
        }
        if (!TryResolveTarget())
            return;

        pendingSupportCall = false;
        IEnemyState current = stateMachine?.CurrentState;
        if (ReferenceEquals(current, roamState)
            || ReferenceEquals(current, suspiciousState)
            || ReferenceEquals(current, returnState))
        {
            PrepareChaseAlert(false);
            ChangeToChase();
        }
        else if (current == null)
        {
            ChangeToChase();
        }
    }

    public bool TryResolveTarget()
    {
        if (IsTargetValid())
            return true;

        EnemyPartyTargetPhase phase = EnemyCombatCoordinator.GetPartyTargetPhase(this);
        if ((IsAggroActive || phase != EnemyPartyTargetPhase.None)
            && EnemyCombatCoordinator.TryMaintainTarget(this, out Transform maintainedTarget))
        {
            ApplyTarget(maintainedTarget);
            return IsTargetValid();
        }

        EnemyCombatCoordinator.Release(this);
        ApplyTarget(null);
        if (!findPlayerByTag || string.IsNullOrWhiteSpace(playerTag))
            return false;

        GameObject playerObject = GameObject.FindGameObjectWithTag(playerTag);
        Transform fallbackTarget = EnemyCombatCoordinator.ResolveCurrentLeaderTarget(
            playerObject != null ? playerObject.transform : null);
        ApplyTarget(fallbackTarget);
        return IsTargetValid();
    }

    public bool IsTargetValid()
    {
        if (target == null || !target.gameObject.activeInHierarchy)
            return false;

        CombatTarget combatTarget = target.GetComponentInParent<CombatTarget>();
        return combatTarget == null || combatTarget.IsAlive;
    }

    public bool IsTargetWithin(float range)
    {
        return IsTargetValid()
            && HorizontalSqrDistance(transform.position, target.position) <= range * range;
    }

    public bool IsTargetBeyond(float range)
    {
        return !IsTargetValid() || HorizontalSqrDistance(transform.position, target.position) > range * range;
    }

    internal bool ShouldReturnFromCombat()
    {
        if (!TryResolveTarget())
        {
            if (TryMaintainGroupAggro())
                return false;
            ResetAggroReleaseCandidate();
            return true; // 유효한 파티 타깃을 다시 찾지 못한 경우만 즉시 복귀
        }

        if (!IsTargetBeyond(CombatLoseTargetRange))
        {
            ResetAggroReleaseCandidate();
            return false; // 생성 위치와 무관하게 타깃이 전투권 안이면 유지
        }

        if (TryMaintainGroupAggro())
            return false;

        if (AggroReleaseDelay <= 0f)
            return true;
        if (aggroReleaseCandidateSince < 0f)
        {
            aggroReleaseCandidateSince = Time.time;
            return false;
        }

        return Time.time - aggroReleaseCandidateSince >= AggroReleaseDelay;
    }

    public bool IsAtHome()
    {
        return HorizontalSqrDistance(transform.position, homePosition) <= ReturnArriveDistance * ReturnArriveDistance;
    }

    internal bool TryEvaluateRoamAwareness()
    {
        return TryEvaluateDistanceAwareness();
    }

    internal bool TryEvaluateReturnAwareness()
    {
        return TryEvaluateDistanceAwareness();
    }

    private bool TryEvaluateDistanceAwareness()
    {
        if (EnemyCombatCoordinator.HasPartyDetectionStimulus(this, DetectionRange))
        {
            DiscoverTarget();
            return IsAggroActive;
        }

        if (!TryResolveTarget())
            return false;

        if (!CanDiscoverTarget())
            return false;

        DiscoverTarget();
        return true;
    }

    internal bool TryRejoinEngagedGroup()
    {
        if (!TryMaintainGroupAggro())
            return false;

        pendingAlertReaction = false;
        pendingSupportCall = false;
        ChangeToChase(); // 동료 전투 합류는 발견 연출 없이 즉시 추적
        return true;
    }

    internal bool CanDiscoverTarget()
    {
        return EnemyCombatCoordinator.HasPartyDetectionStimulus(this, DetectionRange)
            || IsTargetWithin(DetectionRange);
    }

    internal void RememberTargetSound()
    {
        if (IsTargetValid())
            sensor?.RememberSound(target);
    }

    internal void BeginNoticeCooldown()
    {
        sensor?.BeginNoticeCooldown(BehaviorProfile.NoticeCooldown);
    }

    internal void DiscoverTarget()
    {
        if (EnemyCombatCoordinator.TryStartLeaderApproach(this, target, out Transform assignedTarget))
            ApplyTarget(assignedTarget);
        else if (!TryResolveTarget())
            return;
        if (!IsTargetValid())
            return;

        ResetAggroReleaseCandidate();
        PrepareChaseAlert(true);
        ChangeToChase();
    }

    private bool TryMaintainGroupAggro()
    {
        if (!EnemySquadPursuitRuntimeService.HasNearbyEngagedGroupMember(
                this,
                CombatLoseTargetRange))
        {
            return false;
        }

        if (EnemyCombatCoordinator.TryStartLeaderApproach(this, target, out Transform assignedTarget))
            ApplyTarget(assignedTarget); // 동료의 근접 잠금 Target은 복사하지 않음
        ResetAggroReleaseCandidate();
        return IsTargetValid();
    }

    internal bool ConsumePendingAlertReaction(out bool callsSupport)
    {
        bool result = pendingAlertReaction;
        callsSupport = pendingSupportCall;
        pendingAlertReaction = false;
        pendingSupportCall = false;
        return result;
    }

    internal void BroadcastSupportCall()
    {
        if (!IsTargetValid())
            return;

        float radius = BehaviorProfile.SupportCallRange;
        if (radius <= 0f)
            return;

        float radiusSqr = radius * radius;
        SupportSnapshot.Clear();
        SupportSnapshot.AddRange(ActiveEnemies);
        for (int i = 0; i < SupportSnapshot.Count; i++)
        {
            EnemyAIController receiver = SupportSnapshot[i];
            if (receiver == null || receiver == this || !receiver.isActiveAndEnabled)
                continue;

            Vector3 delta = receiver.transform.position - transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude <= radiusSqr)
                receiver.ReceiveSupportAlert();
        }
        SupportSnapshot.Clear();
    }

    internal void ReceiveSupportAlert()
    {
        if (health != null && health.IsDead)
            return;

        ResetAggroReleaseCandidate();
        RequestImmediateAiTick();

        if (EnemyCombatCoordinator.TryStartLeaderApproach(this, target, out Transform assignedTarget))
            ApplyTarget(assignedTarget);
        if (!TryResolveTarget())
            return;

        pendingAlertReaction = false;
        pendingSupportCall = false;
        ChangeToChase();
    }

    private void PrepareChaseAlert(bool callsSupport)
    {
        pendingAlertReaction = true;
        pendingSupportCall = callsSupport;
    }

    private void RefreshPartyTargetPhase()
    {
        if (IsTargetValid() && !ConsumePlanningTick(ref nextTargetPlanningTime)) return;
        using (TargetPlanMarker.Auto())
            if (EnemyCombatCoordinator.TryMaintainTarget(this, out Transform assignedTarget))
                ApplyTarget(assignedTarget);
    }

    internal void HandlePartyLeaderChanged(Transform previousLeader, Transform nextLeader)
    {
        if (nextLeader == null)
            return;

        bool anchorChanged = squadEncounterOwner != null
            && squadEncounterAnchor != null
            && (squadEncounterAnchor == previousLeader || IsPlayerPartyTransform(squadEncounterAnchor));
        if (anchorChanged)
        {
            squadEncounterAnchor = nextLeader;
            EnemySquadPursuitRuntimeService.NotifyEncounterBindingChanged(this);
        }

        bool targetChanged = EnemyCombatCoordinator.TryRebindLeaderApproach(this, out Transform assignedTarget);
        if (targetChanged)
            ApplyTarget(assignedTarget);
        if (anchorChanged || targetChanged)
            RequestImmediateAiTick();
    }

    private void ApplyTarget(Transform newTarget)
    {
        if (target != newTarget)
        {
            ResetPlanningSchedule();
            tacticalPositioning?.Reset();
            nextApproachDirectionRefreshTime = 0f;
            smoothedSeparationDirection = Vector3.zero;
            ResetAggroReleaseCandidate();
            ResetDensityApproachPlan();
        }
        target = newTarget;
    }

    private static bool IsPlayerPartyTransform(Transform candidate)
    {
        CombatTarget combatTarget = candidate != null
            ? candidate.GetComponentInParent<CombatTarget>()
            : null;
        return combatTarget != null && combatTarget.Team == CombatTeam.PlayerParty;
    }
}
