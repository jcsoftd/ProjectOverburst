using System.Collections.Generic;
using UnityEngine;

// EnemyAIController partial: 전술 전투·순찰·추격 계획. 필드와 Unity 수명주기는 EnemyAIController.cs에 있다.
public sealed partial class EnemyAIController
{
    public void SetTacticalProfile(EnemyTacticalProfile profile)
    {
        bool registered = isActiveAndEnabled && UsesSquadPursuit;
        if (registered) EnemySquadPursuitRuntimeService.Unregister(this);
        tacticalProfile = profile;
        if (tacticalPositioning == null) tacticalPositioning = new EnemyTacticalPositioning(this);
        tacticalPositioning.Bind();
        if (registered) EnemySquadPursuitRuntimeService.Register(this);
    }

    internal bool TryHandleTacticalCombat()
    {
        if (!UsesRangedTactics || tacticalPositioning == null || !IsTargetValid()) return false;
        if (IsAttackInProgress() || movement != null && (movement.IsActionLocked || movement.IsStatusMovementLocked)
            || animationBridge != null && animationBridge.BlocksAttackStart) return true;
        // Do not replace a committed aim while its turn is still completing.
        if (abilityController.HasPreparedAim(target) && !movement.IsFacingForAttack(abilityController.ResolveAimPosition(target)))
        { movement.StopMovement(); FaceTarget(); return true; }
        var decision = tacticalPositioning.Evaluate(out Vector3 destination);
        if (decision == EnemyTacticalDecision.Legacy) return false;
        if (decision == EnemyTacticalDecision.Hold)
        { movement?.StopMovement(); ChangeToAttack(); return true; }
        if (CurrentStateName != "Chase") { ChangeToChase(); return true; }
        if (decision == EnemyTacticalDecision.Retreat)
        {
            movement.SetFacingDestination(destination, .15f, tacticalPositioning.ObservedAimPosition, EnemyLocomotionMode.Backpedal, 1f);
            tacticalPositioning.CommitRetreat();
        }
        else movement.SetDestination(decision == EnemyTacticalDecision.Navigate ? ResolveChasePlan() : destination,
            .15f, SelectChaseLocomotion(), 1f);
        return true;
    }

    internal bool TryChoosePatrolDestination(out Vector3 destination)
    {
        EnemyBehaviorProfile profile = BehaviorProfile;
        for (int i = 0; i < PatrolDestinationAttempts; i++)
        {
            Vector2 circle = Random.insideUnitCircle;
            if (circle.sqrMagnitude < 0.16f)
                circle = circle.sqrMagnitude > 0f ? circle.normalized * 0.4f : Vector2.right * 0.4f;

            Vector3 candidate = homePosition + new Vector3(circle.x, 0f, circle.y) * profile.PatrolRadius;
            if (movement == null || movement.IsWalkablePosition(candidate))
            {
                destination = candidate;
                return true;
            }
        }

        destination = homePosition;
        return movement == null || movement.IsWalkablePosition(destination);
    }

    internal void ResolveCombatWaitDecision()
    {
        combatBehavior?.Evaluate();
    }

    internal void UpdateCombatWaitSeparation()
    {
        if (UsesRangedTactics) { movement?.StopMovement(); return; }
        if (movement == null)
            return;
        if (target == null || !target.gameObject.activeInHierarchy) { movement.StopMovement(); return; }
        if (!ConsumePlanningTick(ref nextSeparationPlanningTime)) return;

        if (!TryResolveCombatSeparationDestination(out Vector3 destination))
        {
            movement.StopMovement();
            return;
        }

        movement.SetFacingDestination(
            destination,
            CombatSeparationStopDistance,
            target.position,
            EnemyLocomotionMode.Walk,
            CombatSeparationSpeedMultiplier);
    }

    internal EnemyLocomotionMode SelectChaseLocomotion()
    {
        if (squadPursuitMoveActive)
            return squadPursuitLocomotion;

        return TargetDistance >= BehaviorProfile.RunApproachMinDistance
            ? EnemyLocomotionMode.Run
            : EnemyLocomotionMode.Walk;
    }

    internal Vector3 ResolveChaseDestination()
    {
        return ResolveChasePlan();
    }

    internal Vector3 ResolveChasePlan()
    {
        // Arrival, target displacement and a general planning reset cannot bypass observation.
        bool observe = delayedChaseEnabled && ChaseObservationInterval > 0f && target != null;
        if (observe && hasChaseObservation && observedChaseTarget == target && Time.time < nextChaseObservationTime)
        {
            planningReuseCount++;
            return ContinueObservedChaseHeading();
        }
        bool urgent = !hasCachedChasePlan || target == null
            || HorizontalSqrDistance(cachedPlanTargetPosition, target.position) > 0.25f
            || HorizontalSqrDistance(transform.position, cachedChasePlan) < 0.04f;
        if (!urgent && !ConsumePlanningTick(ref nextChasePlanningTime))
        {
            planningReuseCount++;
            return cachedChasePlan;
        }
        if (urgent) nextChasePlanningTime = EnemyAiTickScheduler.NextPlanningTime(Time.time, PlanningInterval, GetInstanceID());
        chasePlanningDeltaTime = lastChasePlanningTime < 0f ? Time.deltaTime
            : Mathf.Max(0f, Time.time - lastChasePlanningTime);
        lastChasePlanningTime = Time.time;
        using (ChasePlanMarker.Auto()) cachedChasePlan = CalculateChasePlan();
        cachedPlanTargetPosition = target != null ? target.position : transform.position;
        hasCachedChasePlan = true;
        planningEvaluationCount++;
        if (observe)
        {
            hasChaseObservation = true;
            observedChaseTarget = target;
            nextChaseObservationTime = Time.time + ChaseObservationInterval;
            Vector3 step = cachedChasePlan - transform.position; step.y = 0f;
            observedChaseHeading = step.sqrMagnitude > .0001f ? step.normalized : Vector3.zero;
            // A short path waypoint must not make the actor stop every 0.35 seconds.
            // Continue that sampled heading only when the sampled target is still safely far away.
            continuesObservedHeading = movement != null && step.magnitude <= EnemyApproachSteering.MaximumShortHorizonDistance + .1f
                && TargetDistance > AttackEnterRange + movement.ActiveMoveSpeed * (ChaseObservationInterval + .2f);
        }
        return cachedChasePlan;
    }

    private Vector3 ContinueObservedChaseHeading()
    {
        if (!continuesObservedHeading || movement == null) return cachedChasePlan;
        Vector3 remaining = cachedChasePlan - transform.position; remaining.y = 0f;
        if (Vector3.Dot(remaining, observedChaseHeading) > .2f) return cachedChasePlan;
        Vector3 step = EnemyApproachSteering.ResolveShortHorizonDestination(transform.position, observedChaseHeading, movement.ActiveMoveSpeed);
        // Movement still checks every physics step against the live walkable area and crowds.
        return movement.IsWalkablePosition(step) ? step : transform.position;
    }

    private void ResetChaseObservation()
    {
        hasChaseObservation = continuesObservedHeading = false;
        observedChaseTarget = null; nextChaseObservationTime = 0f; observedChaseHeading = Vector3.zero;
    }

    private Vector3 CalculateChasePlan()
    {
        if (EnemySquadPursuitRuntimeService.TryResolveMovePlan(
            this,
            out EnemySquadPursuitMovePlan squadPlan))
        {
            squadPursuitMoveActive = true;
            squadPursuitLocomotion = squadPlan.Locomotion;
            squadPursuitSpeedMultiplier = squadPlan.SpeedMultiplier;
            densityApproachActive = false;
            ClearClusterFanOutActivity(false);
            ClearChaseBypassActivity();
            return ResolveSquadPursuitSteeredDestination(squadPlan.Destination);
        }

        ClearSquadPursuitMove();
        float preferredRadius = Mathf.Min(BehaviorProfile.PreferredApproachDistance, Mathf.Max(.2f, AttackEnterRange - .15f));
        Vector3 flowDirection = Vector3.zero;
        Vector3 flowWaypoint = transform.position;
        bool hasFlowDirection = target != null
            && movement != null
            && EnemyFlowFieldService.TryGetDirection(
                target,
                transform.position,
                out flowDirection,
                out flowWaypoint);
        Vector3 navigationDirection = hasFlowDirection
            ? flowDirection
            : target != null ? target.position - transform.position : Vector3.zero;
        if (UsesClusterFanOutSteering)
        {
            ClearChaseBypassActivity();
            if (target != null
                && movement != null
                && crowdAgent != null
                && TryResolveClusterFanOutDestination(navigationDirection, out Vector3 fanOutDestination))
            {
                densityApproachActive = false;
                return fanOutDestination; // 연결 군집 중·뒷열을 양측으로 전개
            }

            ClearClusterFanOutActivity(true);
        }
        else if (UsesChaseBypassSteering
            && target != null
            && movement != null
            && crowdAgent != null
            && TargetDistance <= EnemyChaseBypassSteering.MaximumActivationDistance
            && TryResolveChaseBypassDestination(navigationDirection, out Vector3 bypassDestination))
        {
            densityApproachActive = false;
            return bypassDestination; // 앞줄 정체 시 빈 측면으로 나선형 추월
        }

        if (!UsesClusterFanOutSteering)
            ClearClusterFanOutActivity(false);
        ClearChaseBypassActivity();
        if (UsesDensityApproachSteering
            && target != null
            && movement != null
            && crowdAgent != null
            && TargetDistance <= EnemyApproachSteering.LocalSteeringStartDistance)
        {
            densityApproachActive = true;
            return ResolveDensityApproachDestination(
                preferredRadius,
                navigationDirection); // 근거리는 Flow 방향과 밀도 조향 합성
        }

        densityApproachActive = false;
        if (hasFlowDirection)
        {
            return ResolveFlowFieldApproachDestination(
                flowDirection,
                flowWaypoint,
                preferredRadius); // 장거리는 공유 방향장의 다음 셀만 추적
        }

        return ResolveNaturalApproachDestination(preferredRadius); // 보행 데이터나 유효 경로가 없을 때 폴백
    }

    internal void BeginChaseApproach(bool preferSide, bool forceRefresh = false)
    {
        if (preferSideApproach != preferSide)
            forceRefresh = true;
        preferSideApproach = preferSide;
        if (forceRefresh || Time.time >= nextApproachDirectionRefreshTime)
            RefreshApproachDirection();
    }

    internal void RefreshChaseApproachDirection()
    {
        hasCachedChasePlan = false;
        if (clusterFanOutActive)
        {
            EnemyClusterFanOutService.Invalidate(target); // 같은 측면을 유지한 채 군집 외피만 갱신
            ClearClusterFanOutActivity(false);
            return;
        }

        if (chaseBypassActive)
        {
            ReleaseChaseBypassTurnLock(); // 우회 중 막힘은 반대 측면 재평가 허용
            return;
        }

        if (densityApproachActive)
        {
            ReleaseDensityApproachTurnLock(); // 막힘 시 고정 방향을 버리고 즉시 재평가
            return;
        }

        RefreshApproachDirection();
    }

    internal void EndChaseApproach()
    {
        ResetChaseObservation();
        hasCachedChasePlan = false;
        ClearSquadPursuitMove();
        densityApproachActive = false;
        ClearClusterFanOutActivity(false);
        ClearChaseBypassActivity();
    }
}
