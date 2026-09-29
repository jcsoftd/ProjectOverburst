using System.Collections.Generic;
using UnityEngine;

// EnemyAIController partial: 접근 조향 스위치와 접근·분리 목적지 계산. 필드와 Unity 수명주기는 EnemyAIController.cs에 있다.
public sealed partial class EnemyAIController
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetApproachRuntimeFlags()
    {
        DensityApproachSteeringEnabled = true;
        ChaseBypassSteeringEnabled = true;
        ClusterFanOutSteeringEnabled = true;
        sessionMonsterDeathCount = 0;
    }

    public static float ResolveCombatLoseTargetRange(int _)
    {
        return BaseCombatLoseTargetRange;
    }

    public static void SetDensityApproachSteeringEnabled(bool enabled)
    {
        if (DensityApproachSteeringEnabled == enabled)
            return;

        DensityApproachSteeringEnabled = enabled;
        foreach (EnemyAIController enemy in ActiveEnemies)
        {
            if (enemy == null)
                continue;

            enemy.ResetDensityApproachPlan();
            enemy.RequestImmediateAiTick();
        }
    }

    public static void SetChaseBypassSteeringEnabled(bool enabled)
    {
        if (ChaseBypassSteeringEnabled == enabled)
            return;

        ChaseBypassSteeringEnabled = enabled;
        foreach (EnemyAIController enemy in ActiveEnemies)
        {
            if (enemy == null)
                continue;

            enemy.ResetChaseBypassPlan();
            enemy.RequestImmediateAiTick();
        }
    }

    public static void SetClusterFanOutSteeringEnabled(bool enabled)
    {
        if (ClusterFanOutSteeringEnabled == enabled)
            return;

        ClusterFanOutSteeringEnabled = enabled;
        EnemyClusterFanOutService.ClearCache();
        foreach (EnemyAIController enemy in ActiveEnemies)
        {
            if (enemy == null)
                continue;

            enemy.ResetClusterFanOutPlan();
            enemy.ResetChaseBypassPlan();
            enemy.RequestImmediateAiTick();
        }
    }

    public void SetDensityApproachSteeringOptIn(bool enabled)
    {
        if (ResolveDensityApproachPreference() == enabled)
            return;

        densityApproachRuntimeOverride = enabled;
        ResetDensityApproachPlan();
        RequestImmediateAiTick();
    }

    private bool ResolveDensityApproachPreference()
    {
        if (densityApproachRuntimeOverride.HasValue)
            return densityApproachRuntimeOverride.Value;
        if (useDensityApproachSteering)
            return true;

        return false;
    }

    private EnemyAiPreset ResolveSquadPursuitPreset()
    {
        if (squadParticipationMode != EnemySquadParticipationMode.SquadMember)
            return null;
        return squadPursuitPreset;
    }

    private void ResetDensityApproachPlan()
    {
        densityApproachNeighbors.Clear();
        densityApproachTurnSign = 0;
        densityApproachRevision = 0;
        nextDensityApproachDecisionTime = 0f;
        densityApproachTurnLockEndTime = 0f;
        densityApproachActive = false;
        ResetChaseBypassPlan();
        ResetClusterFanOutPlan();
    }

    private void ReleaseDensityApproachTurnLock()
    {
        densityApproachTurnSign = 0;
        densityApproachRevision++;
        nextDensityApproachDecisionTime = 0f;
        densityApproachTurnLockEndTime = 0f;
    }

    private void ResetChaseBypassPlan()
    {
        chaseBypassNeighbors.Clear();
        chaseBypassTurnSign = 0;
        chaseBypassRevision = 0;
        nextChaseBypassDecisionTime = 0f;
        chaseBypassTurnLockEndTime = 0f;
        chaseBypassSpeedMultiplier = 1f;
        chaseBypassActive = false;
    }

    private void ReleaseChaseBypassTurnLock()
    {
        chaseBypassTurnSign = 0;
        chaseBypassRevision++;
        nextChaseBypassDecisionTime = 0f;
        chaseBypassTurnLockEndTime = 0f;
    }

    private void ClearChaseBypassActivity()
    {
        chaseBypassActive = false;
        chaseBypassSpeedMultiplier = 1f;
    }

    private void ResetClusterFanOutPlan()
    {
        clusterFanOutSideSign = 0;
        clusterFanOutSpeedMultiplier = 1f;
        clusterFanOutActive = false;
    }

    private void ClearClusterFanOutActivity(bool seedDensityDirection)
    {
        if (seedDensityDirection
            && clusterFanOutActive
            && clusterFanOutSideSign != 0
            && densityApproachTurnSign == 0)
        {
            densityApproachTurnSign = -clusterFanOutSideSign; // Fan-Out 측면을 근거리 조향까지 유지
        }

        clusterFanOutSpeedMultiplier = 1f;
        clusterFanOutActive = false;
    }

    private static float HorizontalSqrDistance(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        delta.y = 0f;
        return delta.sqrMagnitude;
    }

    private Vector3 ResolveDensityApproachDestination(
        float preferredRadius,
        Vector3 navigationDirection)
    {
        EnemyBehaviorProfile profile = BehaviorProfile;
        float bodyRadius = crowdAgent.BodyRadius;
        float queryRadius = EnemyApproachSteering.ResolveNeighborQueryRadius(bodyRadius);
        Vector3 separation = CollectDensityApproachInputs(queryRadius, profile.SeparationRadius);
        if (smoothedSeparationDirection.sqrMagnitude <= 0.0001f)
        {
            smoothedSeparationDirection = separation;
        }
        else
        {
            smoothedSeparationDirection = Vector3.Lerp(
                smoothedSeparationDirection,
                separation,
                Mathf.Clamp01(chasePlanningDeltaTime * 4f));
        }

        float now = Time.time;
        bool decisionDue = densityApproachTurnSign == 0 || now >= nextDensityApproachDecisionTime;
        bool allowTurnSwitch = decisionDue && now >= densityApproachTurnLockEndTime;
        EnemyApproachSteeringResult result = EnemyApproachSteering.Resolve(
            new EnemyApproachSteeringInput(
                transform.position,
                target.position,
                navigationDirection,
                smoothedSeparationDirection,
                preferredRadius,
                bodyRadius,
                unchecked(GetInstanceID() + densityApproachRevision),
                densityApproachTurnSign,
                allowTurnSwitch,
                queryRadius,
                profile.SeparationWeight),
            densityApproachNeighbors);

        int previousTurnSign = densityApproachTurnSign;
        densityApproachTurnSign = result.TurnSign;
        if (decisionDue)
            nextDensityApproachDecisionTime = now + EnemyApproachSteering.TurnDecisionInterval;
        if (previousTurnSign == 0 || previousTurnSign != densityApproachTurnSign)
            densityApproachTurnLockEndTime = now + EnemyApproachSteering.TurnLockDuration;

        return EnemyApproachSteering.ResolveShortHorizonDestination(
            transform.position,
            result.Direction,
            movement.ActiveMoveSpeed);
    }

    private bool TryResolveChaseBypassDestination(
        Vector3 navigationDirection,
        out Vector3 destination)
    {
        destination = transform.position;
        EnemyBehaviorProfile profile = BehaviorProfile;
        float bodyRadius = crowdAgent.BodyRadius;
        Vector3 separation = CollectChaseBypassInputs(profile.SeparationRadius);
        if (smoothedSeparationDirection.sqrMagnitude <= 0.0001f)
            smoothedSeparationDirection = separation;
        else
            smoothedSeparationDirection = Vector3.Lerp(
                smoothedSeparationDirection,
                separation,
                Mathf.Clamp01(chasePlanningDeltaTime * 4f));

        float now = Time.time;
        bool decisionDue = chaseBypassTurnSign == 0 || now >= nextChaseBypassDecisionTime;
        bool allowTurnSwitch = decisionDue && now >= chaseBypassTurnLockEndTime;
        EnemyChaseBypassResult result = EnemyChaseBypassSteering.Resolve(
            new EnemyChaseBypassInput(
                transform.position,
                target.position,
                navigationDirection,
                smoothedSeparationDirection,
                bodyRadius,
                unchecked(GetInstanceID() + chaseBypassRevision),
                chaseBypassTurnSign,
                allowTurnSwitch),
            chaseBypassNeighbors);

        int previousTurnSign = chaseBypassTurnSign;
        chaseBypassTurnSign = result.TurnSign;
        if (decisionDue)
            nextChaseBypassDecisionTime = now + EnemyChaseBypassSteering.TurnDecisionInterval;
        if (result.IsActive && (previousTurnSign == 0 || previousTurnSign != chaseBypassTurnSign))
            chaseBypassTurnLockEndTime = now + EnemyChaseBypassSteering.TurnLockDuration;
        if (!result.IsActive)
            return false;

        Vector3 candidate = EnemyApproachSteering.ResolveShortHorizonDestination(
            transform.position,
            result.Direction,
            movement.ActiveMoveSpeed * result.SpeedMultiplier);
        if (!movement.IsWalkablePosition(candidate))
        {
            ReleaseChaseBypassTurnLock();
            return false; // 벽·통로에서는 기존 Flow 방향을 우선
        }

        chaseBypassActive = true;
        chaseBypassSpeedMultiplier = result.SpeedMultiplier;
        destination = candidate;
        return true;
    }

    private bool TryResolveClusterFanOutDestination(
        Vector3 navigationDirection,
        out Vector3 destination)
    {
        destination = transform.position;
        if (!EnemyClusterFanOutService.TryGetMemberData(
                crowdAgent,
                target,
                out EnemyClusterFanOutMemberData member))
        {
            return false;
        }

        Vector3 separation = ResolveSeparationDirection();
        if (smoothedSeparationDirection.sqrMagnitude <= 0.0001f)
            smoothedSeparationDirection = separation;
        else
            smoothedSeparationDirection = Vector3.Lerp(
                smoothedSeparationDirection,
                separation,
                Mathf.Clamp01(chasePlanningDeltaTime * 4f));

        EnemyClusterFanOutResult result = EnemyClusterFanOutSteering.Resolve(
            new EnemyClusterFanOutInput(
                member,
                navigationDirection,
                smoothedSeparationDirection,
                clusterFanOutSideSign,
                GetInstanceID()));
        if (!result.IsActive)
            return false;

        Vector3 candidate = EnemyApproachSteering.ResolveShortHorizonDestination(
            transform.position,
            result.Direction,
            movement.ActiveMoveSpeed * result.SpeedMultiplier);
        if (!movement.IsWalkablePosition(candidate))
            return false; // 좁은 통로와 벽에서는 Flow 방향 유지

        clusterFanOutSideSign = result.SideSign;
        clusterFanOutSpeedMultiplier = result.SpeedMultiplier;
        clusterFanOutActive = true;
        destination = candidate;
        return true;
    }

    private Vector3 CollectChaseBypassInputs(float separationRadius)
    {
        chaseBypassNeighbors.Clear();
        Vector3 separation = Vector3.zero;
        float resolvedSeparationRadius = Mathf.Max(0.01f, separationRadius);
        float separationRadiusSqr = resolvedSeparationRadius * resolvedSeparationRadius;
        float queryRadius = Mathf.Max(
            EnemyChaseBypassSteering.NeighborQueryRadius,
            resolvedSeparationRadius);
        EnemyCrowdService.CollectNeighbors(transform.position, queryRadius, crowdNeighbors);
        for (int i = 0; i < crowdNeighbors.Count; i++)
        {
            EnemyCrowdAgent other = crowdNeighbors[i];
            if (other == null || other == crowdAgent || !other.IsCrowdActive)
                continue;

            EnemyAIController otherController = other.Controller;
            if (otherController == null
                || otherController == this
                || !otherController.isActiveAndEnabled
                || otherController.Target != target)
            {
                continue;
            }

            Vector3 delta = transform.position - other.SnapshotPosition;
            delta.y = 0f;
            float sqrDistance = delta.sqrMagnitude;
            if (sqrDistance <= EnemyChaseBypassSteering.NeighborQueryRadius
                    * EnemyChaseBypassSteering.NeighborQueryRadius)
            {
                chaseBypassNeighbors.Add(
                    new EnemyApproachNeighbor(other.SnapshotPosition, other.BodyRadius));
            }

            if (sqrDistance >= separationRadiusSqr)
                continue;
            if (sqrDistance <= 0.0001f)
            {
                separation += ResolvePairSeparationDirection(otherController);
                continue;
            }

            float distance = Mathf.Sqrt(sqrDistance);
            separation += delta / distance * (1f - distance / resolvedSeparationRadius);
        }

        return Vector3.ClampMagnitude(separation, 1f);
    }

    private Vector3 ResolveFlowFieldApproachDestination(
        Vector3 flowDirection,
        Vector3 flowWaypoint,
        float fallbackRadius)
    {
        Vector3 separation = ResolveSeparationDirection();
        if (smoothedSeparationDirection.sqrMagnitude <= 0.0001f)
            smoothedSeparationDirection = separation;
        else
            smoothedSeparationDirection = Vector3.Lerp(
                smoothedSeparationDirection,
                separation,
                Mathf.Clamp01(chasePlanningDeltaTime * 4f));

        Vector3 direction = flowDirection
            + smoothedSeparationDirection * BehaviorProfile.SeparationWeight;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            direction = flowDirection;

        Vector3 destination = EnemyApproachSteering.ResolveShortHorizonDestination(
            transform.position,
            direction,
            movement.ActiveMoveSpeed);
        if (movement.IsWalkablePosition(destination))
            return destination;

        flowWaypoint.y = transform.position.y;
        return movement.IsWalkablePosition(flowWaypoint)
            ? flowWaypoint
            : ResolveNaturalApproachDestination(fallbackRadius); // 비정상 셀은 기존 접근 폴백
    }

    private Vector3 CollectDensityApproachInputs(float queryRadius, float separationRadius)
    {
        densityApproachNeighbors.Clear();
        Vector3 separation = Vector3.zero;
        float resolvedSeparationRadius = Mathf.Max(0.01f, separationRadius);
        float separationRadiusSqr = resolvedSeparationRadius * resolvedSeparationRadius;
        EnemyCrowdService.CollectNeighbors(transform.position, queryRadius, crowdNeighbors);
        for (int i = 0; i < crowdNeighbors.Count; i++)
        {
            EnemyCrowdAgent other = crowdNeighbors[i];
            if (other == null || other == crowdAgent || !other.IsCrowdActive)
                continue;

            densityApproachNeighbors.Add(
                new EnemyApproachNeighbor(other.SnapshotPosition, other.BodyRadius)); // 미래 목적지는 읽지 않음

            EnemyAIController otherController = other.Controller;
            if (otherController == null
                || otherController == this
                || !otherController.isActiveAndEnabled
                || otherController.Target != target)
            {
                continue;
            }

            Vector3 delta = transform.position - other.SnapshotPosition;
            delta.y = 0f;
            float sqrDistance = delta.sqrMagnitude;
            if (sqrDistance >= separationRadiusSqr)
                continue;

            if (sqrDistance <= 0.0001f)
            {
                separation += ResolvePairSeparationDirection(otherController);
                continue;
            }

            float distance = Mathf.Sqrt(sqrDistance);
            separation += delta / distance * (1f - distance / resolvedSeparationRadius);
        }

        return Vector3.ClampMagnitude(separation, 1f);
    }

    private Vector3 ResolveNaturalApproachDestination(float radius)
    {
        if (!IsTargetValid())
            return transform.position;

        if (Time.time >= nextApproachDirectionRefreshTime)
            RefreshApproachDirection();

        Vector3 separation = ResolveSeparationDirection();
        if (smoothedSeparationDirection.sqrMagnitude <= 0.0001f)
            smoothedSeparationDirection = separation;
        else
            smoothedSeparationDirection = Vector3.Lerp(
                smoothedSeparationDirection,
                separation,
                Mathf.Clamp01(chasePlanningDeltaTime * 4f));

        Vector3 direction = approachDirection
            + smoothedSeparationDirection * BehaviorProfile.SeparationWeight;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
            direction = approachDirection;

        return target.position + direction.normalized * Mathf.Max(0.5f, radius);
    }

    private Vector3 ResolveSquadPursuitSteeredDestination(Vector3 destination)
    {
        if (movement == null || crowdAgent == null)
            return destination;

        Vector3 seek = destination - transform.position;
        seek.y = 0f;
        if (seek.sqrMagnitude <= 0.0001f)
            return destination;
        seek.Normalize();

        float radius = Mathf.Max(0.1f, BehaviorProfile.SeparationRadius);
        EnemyCrowdService.CollectNeighbors(transform.position, radius, crowdNeighbors);
        Vector3 separation = Vector3.zero;
        int ownPriority = CrowdMovePriority;
        for (int i = 0; i < crowdNeighbors.Count; i++)
        {
            EnemyCrowdAgent other = crowdNeighbors[i];
            if (other == null || other == crowdAgent || !other.IsCrowdActive)
                continue;

            Vector3 delta = transform.position - other.SnapshotPosition;
            delta.y = 0f;
            float desiredDistance = Mathf.Max(radius, crowdAgent.BodyRadius + other.BodyRadius);
            float sqrDistance = delta.sqrMagnitude;
            if (sqrDistance <= 0.0001f || sqrDistance >= desiredDistance * desiredDistance)
                continue;

            float distance = Mathf.Sqrt(sqrDistance);
            int otherPriority = other.MovePriority;
            float priorityScale = ownPriority > otherPriority
                ? 0.15f
                : ownPriority < otherPriority ? 1.35f : 1f;
            separation += delta / distance
                * (1f - distance / desiredDistance)
                * priorityScale;
        }

        Vector3 combined = Vector3.ClampMagnitude(
            seek + Vector3.ClampMagnitude(separation, 1f) * BehaviorProfile.SeparationWeight,
            1f);
        float forward = Vector3.Dot(combined, seek);
        if (forward < 0.2f)
        {
            Vector3 lateral = combined - seek * forward;
            combined = (seek * 0.2f + Vector3.ClampMagnitude(lateral, 0.98f)).normalized;
        }

        Vector3 candidate = EnemyApproachSteering.ResolveShortHorizonDestination(
            transform.position,
            combined,
            movement.ActiveMoveSpeed);
        candidate.y = transform.position.y;
        return movement.IsWalkablePosition(candidate) ? candidate : destination;
    }

    private void ClearSquadPursuitMove()
    {
        squadPursuitMoveActive = false;
        squadPursuitLocomotion = EnemyLocomotionMode.Walk;
        squadPursuitSpeedMultiplier = 1f;
    }

    private void RefreshApproachDirection()
    {
        if (!IsTargetValid())
            return;

        Vector3 radial = transform.position - target.position;
        radial.y = 0f;
        if (radial.sqrMagnitude <= 0.0001f)
            radial = ResolveFallbackApproachDirection(GetInstanceID() + approachDirectionRevision);
        else
            radial.Normalize();

        EnemyBehaviorProfile profile = BehaviorProfile;
        float sideAngle = preferSideApproach
            ? Mathf.Max(45f, profile.ApproachSideAngle)
            : profile.ApproachSideAngle;
        float sign = ((GetInstanceID() + approachDirectionRevision) & 1) == 0 ? -1f : 1f;
        approachDirection = Quaternion.Euler(0f, sideAngle * sign, 0f) * radial;
        approachDirectionRevision++;
        nextApproachDirectionRefreshTime = Time.time + Random.Range(
            profile.ApproachDirectionMinDuration,
            profile.ApproachDirectionMaxDuration);
    }

    private Vector3 ResolveSeparationDirection()
    {
        float radius = BehaviorProfile.SeparationRadius;
        float radiusSqr = radius * radius;
        Vector3 separation = Vector3.zero;
        EnemyCrowdService.CollectNeighbors(transform.position, radius, crowdNeighbors);
        for (int i = 0; i < crowdNeighbors.Count; i++)
        {
            EnemyAIController other = crowdNeighbors[i].Controller;
            if (other == null || other == this || !other.isActiveAndEnabled || other.Target != target)
                continue;

            Vector3 delta = transform.position - other.transform.position;
            delta.y = 0f;
            float sqrDistance = delta.sqrMagnitude;
            if (sqrDistance >= radiusSqr)
                continue;

            if (sqrDistance <= 0.0001f)
            {
                separation += ResolvePairSeparationDirection(other);
                continue;
            }

            float distance = Mathf.Sqrt(sqrDistance);
            separation += delta / distance * (1f - distance / radius);
        }

        return Vector3.ClampMagnitude(separation, 1f);
    }

    private bool TryResolveCombatSeparationDestination(out Vector3 destination)
    {
        destination = transform.position;
        if (!IsTargetValid())
            return false;

        EnemyBehaviorProfile profile = BehaviorProfile;
        float activationRadius = Mathf.Max(0.5f, profile.SeparationRadius * CombatSeparationRadiusRatio);
        float activationRadiusSqr = activationRadius * activationRadius;
        Vector3 separation = Vector3.zero;
        float strongestPressure = 0f;
        int neighborCount = 0;

        EnemyCrowdService.CollectNeighbors(transform.position, activationRadius, crowdNeighbors);
        for (int i = 0; i < crowdNeighbors.Count; i++)
        {
            EnemyAIController other = crowdNeighbors[i].Controller;
            if (other == null
                || other == this
                || !other.isActiveAndEnabled
                || other.Target != target
                || (other.health != null && other.health.IsDead))
            {
                continue;
            }

            Vector3 delta = transform.position - other.transform.position;
            delta.y = 0f;
            float sqrDistance = delta.sqrMagnitude;
            if (sqrDistance >= activationRadiusSqr)
                continue;

            float pressure;
            Vector3 direction;
            if (sqrDistance <= 0.0001f)
            {
                pressure = 1f;
                direction = ResolvePairSeparationDirection(other);
            }
            else
            {
                float distance = Mathf.Sqrt(sqrDistance);
                pressure = 1f - distance / activationRadius;
                direction = delta / distance;
            }

            separation += direction * Mathf.Lerp(0.35f, 1f, pressure);
            strongestPressure = Mathf.Max(strongestPressure, pressure);
            neighborCount++;
        }

        if (neighborCount == 0)
            return false;
        if (separation.sqrMagnitude <= 0.0001f)
            separation = ResolveFallbackApproachDirection(GetInstanceID()); // 대칭 군집 합이 0이어도 분리

        float weightScale = Mathf.Lerp(0.8f, 1.1f, profile.SeparationWeight * 0.5f);
        float step = Mathf.Lerp(CombatSeparationMinStep, CombatSeparationMaxStep, strongestPressure) * weightScale;
        Vector3 candidate = transform.position + separation.normalized * step;

        Vector3 targetOffset = candidate - target.position;
        targetOffset.y = 0f;
        float maxCombatRadius = Mathf.Max(0.75f, AttackEnterRange - 0.1f);
        if (targetOffset.sqrMagnitude > maxCombatRadius * maxCombatRadius)
            candidate = target.position + targetOffset.normalized * maxCombatRadius;

        return movement.TryResolveWalkableDestination(candidate, out destination);
    }

    private Vector3 ResolvePairSeparationDirection(EnemyAIController other)
    {
        int ownId = GetInstanceID();
        int otherId = other != null ? other.GetInstanceID() : 0;
        int lowerId = Mathf.Min(ownId, otherId);
        int upperId = Mathf.Max(ownId, otherId);
        Vector3 direction = ResolveFallbackApproachDirection(unchecked(lowerId * 397 ^ upperId));
        return ownId <= otherId ? direction : -direction;
    }

    private static Vector3 ResolveFallbackApproachDirection(int instanceId)
    {
        uint hash = unchecked((uint)instanceId);
        hash ^= hash >> 16;
        hash *= 0x7feb352d;
        hash ^= hash >> 15;
        hash *= 0x846ca68b;
        hash ^= hash >> 16;
        float radians = (hash % 3600u) * 0.1f * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians));
    }
}
