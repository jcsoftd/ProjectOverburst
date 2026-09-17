using System.Collections.Generic;
using UnityEngine;

public enum EnemyPartyTargetPhase
{
    None,
    LeaderApproach,
    MemberEngaged
}

public readonly struct EnemyPartyTargetCandidate
{
    public EnemyPartyTargetCandidate(
        int memberIndex,
        bool isValid,
        bool canEngage,
        bool isDirectAttacker,
        float surfaceDistance)
    {
        MemberIndex = memberIndex;
        IsValid = isValid;
        CanEngage = canEngage;
        IsDirectAttacker = isDirectAttacker;
        SurfaceDistance = Mathf.Max(0f, surfaceDistance);
    }

    public int MemberIndex { get; }
    public bool IsValid { get; }
    public bool CanEngage { get; }
    public bool IsDirectAttacker { get; }
    public float SurfaceDistance { get; }
}

public readonly struct EnemyPartyTargetDecision
{
    public EnemyPartyTargetDecision(EnemyPartyTargetPhase phase, int targetMemberIndex)
    {
        Phase = phase;
        TargetMemberIndex = targetMemberIndex;
    }

    public EnemyPartyTargetPhase Phase { get; }
    public int TargetMemberIndex { get; }
}

public static class EnemyPartyTargetPolicy
{
    public static EnemyPartyTargetDecision Resolve(
        EnemyPartyTargetDecision current,
        int leaderMemberIndex,
        IReadOnlyList<EnemyPartyTargetCandidate> candidates)
    {
        if (current.Phase == EnemyPartyTargetPhase.MemberEngaged
            && IsValidMember(current.TargetMemberIndex, candidates))
        {
            return current; // 근접 잠금은 대상이 유효한 동안 유지
        }

        bool found = false;
        EnemyPartyTargetCandidate best = default;
        int count = candidates != null ? candidates.Count : 0;
        for (int i = 0; i < count; i++)
        {
            EnemyPartyTargetCandidate candidate = candidates[i];
            if (!candidate.IsValid || !candidate.CanEngage)
                continue;
            if (!found || IsPreferred(candidate, best))
            {
                found = true;
                best = candidate;
            }
        }

        if (found)
        {
            return new EnemyPartyTargetDecision(
                EnemyPartyTargetPhase.MemberEngaged,
                best.MemberIndex);
        }

        return leaderMemberIndex >= 0
            ? new EnemyPartyTargetDecision(EnemyPartyTargetPhase.LeaderApproach, leaderMemberIndex)
            : new EnemyPartyTargetDecision(EnemyPartyTargetPhase.None, -1);
    }

    private static bool IsValidMember(
        int memberIndex,
        IReadOnlyList<EnemyPartyTargetCandidate> candidates)
    {
        int count = candidates != null ? candidates.Count : 0;
        for (int i = 0; i < count; i++)
        {
            EnemyPartyTargetCandidate candidate = candidates[i];
            if (candidate.MemberIndex == memberIndex)
                return candidate.IsValid;
        }

        return false;
    }

    private static bool IsPreferred(
        EnemyPartyTargetCandidate candidate,
        EnemyPartyTargetCandidate best)
    {
        if (candidate.IsDirectAttacker != best.IsDirectAttacker)
            return candidate.IsDirectAttacker;

        int distanceCompare = candidate.SurfaceDistance.CompareTo(best.SurfaceDistance);
        return distanceCompare < 0
            || distanceCompare == 0 && candidate.MemberIndex < best.MemberIndex;
    }
}

public static class EnemyCombatCoordinator // 어그로 타깃과 공격 차례 관리
{
    private sealed class TargetAssignment
    {
        public CombatTarget Target;
        public EnemyPartyTargetPhase Phase;
        public int MemberIndex = -1;
    }

    private const int FixedPathHitCapacity = 32;

    private static readonly Dictionary<EnemyAIController, TargetAssignment> TargetAssignments =
        new Dictionary<EnemyAIController, TargetAssignment>();
    private static readonly Dictionary<EnemyAIController, CombatTarget> AttackReservations =
        new Dictionary<EnemyAIController, CombatTarget>();
    private static readonly Dictionary<EnemyAIController, float> AttackReadyTimes =
        new Dictionary<EnemyAIController, float>();
    private static readonly HashSet<EnemyAIController> RegisteredEnemies =
        new HashSet<EnemyAIController>();
    private static readonly List<EnemyAIController> EnemySnapshot = new List<EnemyAIController>(256);
    private static readonly List<EnemyAIController> EnemyCleanup = new List<EnemyAIController>(32);
    private static readonly List<EnemyAIController> AttackReservationCleanup = new List<EnemyAIController>(32);
    private static readonly List<EnemyPartyTargetCandidate> PartyCandidates =
        new List<EnemyPartyTargetCandidate>(1);
    private static readonly RaycastHit[] FixedPathHits = new RaycastHit[FixedPathHitCapacity];

    private static PlayerContext subscribedLeaderContext;
    private static Transform currentLeaderTransform;
    private static int currentLeaderMemberIndex = -1;
    private static int lastCleanupFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        if (subscribedLeaderContext != null)
            subscribedLeaderContext.CurrentActorChanged -= HandleLeaderChanged;

        subscribedLeaderContext = null;
        currentLeaderTransform = null;
        currentLeaderMemberIndex = -1;
        lastCleanupFrame = -1;
        TargetAssignments.Clear();
        AttackReservations.Clear();
        AttackReadyTimes.Clear();
        RegisteredEnemies.Clear();
        EnemySnapshot.Clear();
        EnemyCleanup.Clear();
        AttackReservationCleanup.Clear();
        PartyCandidates.Clear();
    }

    public static void Register(EnemyAIController enemy)
    {
        if (enemy == null)
            return;

        RegisteredEnemies.Add(enemy);
        EnsureLeaderSubscription();
    }

    public static void Unregister(EnemyAIController enemy)
    {
        if (enemy == null)
            return;

        RegisteredEnemies.Remove(enemy);
        Release(enemy);
    }

    public static EnemyPartyTargetDecision ResolvePartyTargetPhase(
        EnemyPartyTargetDecision current,
        int leaderMemberIndex,
        IReadOnlyList<EnemyPartyTargetCandidate> candidates)
    {
        return EnemyPartyTargetPolicy.Resolve(current, leaderMemberIndex, candidates);
    }

    public static float ResolveHorizontalSurfaceDistance(
        Vector3 leftCenter,
        float leftRadius,
        Vector3 rightCenter,
        float rightRadius)
    {
        Vector3 delta = rightCenter - leftCenter;
        delta.y = 0f;
        return Mathf.Max(0f, delta.magnitude - Mathf.Max(0f, leftRadius) - Mathf.Max(0f, rightRadius));
    }

    public static float ResolveMemberEngageCenterRadius(
        float enemyRadius,
        float memberRadius,
        float memberEngageRange)
    {
        return Mathf.Max(0f, enemyRadius)
            + Mathf.Max(0f, memberRadius)
            + Mathf.Max(0f, memberEngageRange);
    }

    public static bool IsInsideMemberEngageRange(
        Vector3 enemyCenter,
        float enemyRadius,
        Vector3 memberCenter,
        float memberRadius,
        float memberEngageRange)
    {
        return ResolveHorizontalSurfaceDistance(
                enemyCenter,
                enemyRadius,
                memberCenter,
                memberRadius)
            <= Mathf.Max(0f, memberEngageRange);
    }

    public static EnemyPartyTargetPhase GetPartyTargetPhase(EnemyAIController enemy)
    {
        CleanupInvalidEntries();
        return enemy != null && TargetAssignments.TryGetValue(enemy, out TargetAssignment assignment)
            ? assignment.Phase
            : EnemyPartyTargetPhase.None;
    }

    public static int GetPartyTargetMemberIndex(EnemyAIController enemy)
    {
        CleanupInvalidEntries();
        return enemy != null && TargetAssignments.TryGetValue(enemy, out TargetAssignment assignment)
            ? assignment.MemberIndex
            : -1;
    }

    public static Transform ResolveCurrentLeaderTarget(Transform fallbackTarget = null)
    {
        EnsureLeaderSubscription();
        CombatTarget leaderTarget = ResolveLeaderCombatTarget();
        return leaderTarget != null ? leaderTarget.transform : fallbackTarget;
    }

    public static bool TryStartLeaderApproach(
        EnemyAIController enemy,
        Transform legacyFallback,
        out Transform assignedTarget)
    {
        assignedTarget = null;
        if (enemy == null)
            return false;

        CleanupInvalidEntries();
        EnsureLeaderSubscription();
        if (TryRetainMemberEngaged(enemy, out assignedTarget))
            return true;

        CombatTarget leaderTarget = ResolveLeaderCombatTarget();
        if (leaderTarget != null)
        {
            Assign(enemy, leaderTarget, EnemyPartyTargetPhase.LeaderApproach, currentLeaderMemberIndex);
            assignedTarget = leaderTarget.transform;
            return true;
        }

        CombatTarget fallbackCombatTarget = ResolvePlayerPartyTarget(legacyFallback);
        if (fallbackCombatTarget != null)
        {
            Assign(enemy, fallbackCombatTarget, EnemyPartyTargetPhase.None, ResolveMemberIndex(fallbackCombatTarget));
            assignedTarget = fallbackCombatTarget.transform;
            return true;
        }

        if (legacyFallback == null || !legacyFallback.gameObject.activeInHierarchy)
            return false;

        TargetAssignments.Remove(enemy);
        assignedTarget = legacyFallback;
        return true;
    }

    public static bool TryRefreshLocalEngagement(
        EnemyAIController enemy,
        Transform directAttacker,
        out Transform assignedTarget)
    {
        assignedTarget = null;
        if (enemy == null)
            return false;

        CleanupInvalidEntries();
        EnsureLeaderSubscription();
        PlayerContext runtime = PlayerContext.GetOrCreate();
        CombatTarget leaderTarget = ResolveLeaderCombatTarget();
        if (runtime == null || leaderTarget == null)
            return TryStartLeaderApproach(enemy, directAttacker != null ? directAttacker : enemy.Target, out assignedTarget);

        CombatTarget directAttackerTarget = ResolvePlayerPartyTarget(directAttacker);
        BuildPartyCandidates(enemy, runtime, directAttackerTarget);
        EnemyPartyTargetDecision current = ResolveCurrentDecision(enemy);
        EnemyPartyTargetDecision decision = ResolvePartyTargetPhase(
            current,
            currentLeaderMemberIndex,
            PartyCandidates);
        PartyCandidates.Clear();

        CombatTarget decisionTarget = decision.Phase == EnemyPartyTargetPhase.LeaderApproach
            ? leaderTarget
            : ResolveMemberCombatTarget(runtime, decision.TargetMemberIndex);
        if (decisionTarget == null)
        {
            Assign(enemy, leaderTarget, EnemyPartyTargetPhase.LeaderApproach, currentLeaderMemberIndex);
            assignedTarget = leaderTarget.transform;
            return true;
        }

        Assign(enemy, decisionTarget, decision.Phase, decision.TargetMemberIndex);
        assignedTarget = decisionTarget.transform;
        return true;
    }

    public static bool TryMaintainTarget(EnemyAIController enemy, out Transform assignedTarget)
    {
        assignedTarget = null;
        if (enemy == null)
            return false;

        CleanupInvalidEntries();
        EnsureLeaderSubscription();
        if (TryRetainMemberEngaged(enemy, out assignedTarget))
            return true;

        return TryRefreshLocalEngagement(enemy, null, out assignedTarget);
    }

    public static bool TryRebindLeaderApproach(EnemyAIController enemy, out Transform assignedTarget)
    {
        assignedTarget = null;
        if (enemy == null
            || !TargetAssignments.TryGetValue(enemy, out TargetAssignment assignment)
            || assignment.Phase != EnemyPartyTargetPhase.LeaderApproach)
        {
            return false;
        }

        CombatTarget leaderTarget = ResolveLeaderCombatTarget();
        if (leaderTarget == null)
            return false;

        Assign(enemy, leaderTarget, EnemyPartyTargetPhase.LeaderApproach, currentLeaderMemberIndex);
        assignedTarget = leaderTarget.transform;
        return true;
    }

    public static bool HasPartyDetectionStimulus(EnemyAIController enemy, float range)
    {
        if (enemy == null)
            return false;
        CombatTarget candidate = ResolveLeaderCombatTarget();
        float resolvedRange = Mathf.Max(0f, range);
        return candidate != null
            && HorizontalSqrDistance(enemy.transform.position, candidate.transform.position) <= resolvedRange * resolvedRange;
    }

    public static bool TryAcquireAttackTurn(EnemyAIController enemy, Transform combatTarget)
    {
        if (enemy == null || combatTarget == null || !combatTarget.gameObject.activeInHierarchy)
            return false;

        CleanupInvalidEntries();
        CombatTarget target = combatTarget.GetComponentInParent<CombatTarget>();
        if (target == null)
            return true; // 구형 대상은 기존 단독 공격 허용
        if (!target.IsAlive)
            return false;

        if (AttackReservations.TryGetValue(enemy, out CombatTarget existing))
        {
            if (existing == target)
                return true;
            AttackReservations.Remove(enemy);
        }

        if (AttackReadyTimes.TryGetValue(enemy, out float readyTime) && Time.time < readyTime)
            return false;
        AttackReservations[enemy] = target;
        return true;
    }

    public static void ReleaseAttackTurn(EnemyAIController enemy, float reentryCooldown)
    {
        if (enemy == null || !AttackReservations.Remove(enemy))
            return;

        AttackReadyTimes[enemy] = Time.time + Mathf.Max(0f, reentryCooldown);
    }

    public static void Release(EnemyAIController enemy)
    {
        if (enemy == null)
            return;

        TargetAssignments.Remove(enemy);
        AttackReservations.Remove(enemy);
        AttackReadyTimes.Remove(enemy);
    }

    private static void BuildPartyCandidates(
        EnemyAIController enemy,
        PlayerContext runtime,
        CombatTarget directAttacker)
    {
        PartyCandidates.Clear();
        CombatTarget enemyTarget = ResolveCombatTarget(enemy.gameObject);
        CombatTargetVolume enemyVolume = enemyTarget != null
            ? enemyTarget.CurrentVolume
            : new CombatTargetVolume(enemy.transform.position, 0f, 0f);
        EnemyBehaviorProfile profile = enemy.BehaviorProfile;
        float engageRange = profile.MemberEngageRange;
        float verticalTolerance = profile.MemberEngageVerticalTolerance;

        PlayerActorRuntime member = runtime != null ? runtime.CurrentActor : null;
        if (member != null)
        {
            int memberIndex = member.ActorIndex;
            CombatTarget memberTarget = ResolveCombatTarget(member != null ? member.gameObject : null);
            bool isValid = IsRealPlayerPartyActor(memberTarget);
            float surfaceDistance = float.PositiveInfinity;
            bool canEngage = false;
            if (isValid)
            {
                CombatTargetVolume memberVolume = memberTarget.CurrentVolume;
                surfaceDistance = ResolveHorizontalSurfaceDistance(
                    enemyVolume.Center,
                    enemyVolume.Radius,
                    memberVolume.Center,
                    memberVolume.Radius);
                bool verticalAllowed = Mathf.Abs(enemyVolume.Center.y - memberVolume.Center.y) <= verticalTolerance;
                canEngage = IsInsideMemberEngageRange(
                        enemyVolume.Center,
                        enemyVolume.Radius,
                        memberVolume.Center,
                        memberVolume.Radius,
                        engageRange)
                    && verticalAllowed
                    && HasReachableFixedColliderPath(enemy, memberTarget, enemyVolume.Center, memberVolume.Center);
            }

            PartyCandidates.Add(new EnemyPartyTargetCandidate(
                member != null ? member.ActorIndex : memberIndex,
                isValid,
                canEngage,
                isValid && memberTarget == directAttacker,
                surfaceDistance));
        }
    }

    private static bool HasReachableFixedColliderPath(
        EnemyAIController enemy,
        CombatTarget memberTarget,
        Vector3 origin,
        Vector3 destination)
    {
        Vector3 direction = destination - origin;
        float distance = direction.magnitude;
        if (distance <= 0.001f)
            return true;

        int hitCount = Physics.RaycastNonAlloc(
            origin,
            direction / distance,
            FixedPathHits,
            distance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            Collider collider = FixedPathHits[i].collider;
            if (collider == null || collider.isTrigger || FixedPathHits[i].distance <= 0.001f)
                continue;
            Transform hitTransform = collider.transform;
            if (hitTransform == enemy.transform
                || hitTransform.IsChildOf(enemy.transform)
                || hitTransform == memberTarget.transform
                || hitTransform.IsChildOf(memberTarget.transform))
            {
                continue;
            }

            CombatTarget actorTarget = collider.GetComponentInParent<CombatTarget>();
            if (actorTarget != null
                || CombatTeamUtility.ResolvePlayerActorObject(collider.gameObject) != null
                || collider.GetComponentInParent<EnemyAIController>() != null
                || collider.GetComponentInParent<EnemyMovement>() != null)
            {
                continue; // 플레이어·몬스터·장착물은 고정 벽으로 보지 않음
            }

            Rigidbody attachedBody = collider.attachedRigidbody;
            if (attachedBody != null && !attachedBody.isKinematic)
                continue; // 동적 Rigidbody는 일시 장애물이므로 잠금 판정에서 제외

            return false;
        }

        return true;
    }

    private static EnemyPartyTargetDecision ResolveCurrentDecision(EnemyAIController enemy)
    {
        return enemy != null && TargetAssignments.TryGetValue(enemy, out TargetAssignment assignment)
            ? new EnemyPartyTargetDecision(assignment.Phase, assignment.MemberIndex)
            : new EnemyPartyTargetDecision(EnemyPartyTargetPhase.None, -1);
    }

    private static bool TryRetainMemberEngaged(EnemyAIController enemy, out Transform target)
    {
        target = null;
        if (!TargetAssignments.TryGetValue(enemy, out TargetAssignment assignment)
            || assignment.Phase != EnemyPartyTargetPhase.MemberEngaged
            || !IsRealPlayerPartyActor(assignment.Target))
        {
            return false;
        }

        PlayerContext runtime = PlayerContext.GetOrCreate();
        if (runtime != null
            && ResolveMemberCombatTarget(runtime, assignment.MemberIndex) != assignment.Target)
        {
            return false;
        }

        target = assignment.Target.transform;
        return true;
    }

    private static void Assign(
        EnemyAIController enemy,
        CombatTarget target,
        EnemyPartyTargetPhase phase,
        int memberIndex)
    {
        if (!TargetAssignments.TryGetValue(enemy, out TargetAssignment assignment))
        {
            assignment = new TargetAssignment();
            TargetAssignments.Add(enemy, assignment);
        }

        assignment.Target = target;
        assignment.Phase = phase;
        assignment.MemberIndex = memberIndex;
        // 공격 예약은 Target 배정이 아니라 진행 중인 공격 수명주기에서 해제한다.
    }

    private static CombatTarget ResolveLeaderCombatTarget()
    {
        EnsureLeaderSubscription();
        PlayerActorRuntime leader = subscribedLeaderContext != null
            ? subscribedLeaderContext.CurrentActor
            : null;
        if (leader != null && leader.transform != currentLeaderTransform)
            HandleLeaderChanged(leader); // 비활성 구간에 놓친 이벤트도 실제 Transform 차이로 한 번만 보정
        CombatTarget target = ResolveCombatTarget(leader != null ? leader.gameObject : null);
        if (!IsRealPlayerPartyActor(target))
            return null;

        currentLeaderMemberIndex = leader.ActorIndex;
        return target;
    }

    private static CombatTarget ResolveMemberCombatTarget(PlayerContext runtime, int memberIndex)
    {
        PlayerActorRuntime actor = runtime != null ? runtime.CurrentActor : null;
        if (actor == null || actor.ActorIndex != memberIndex)
            return null;
        CombatTarget target = ResolveCombatTarget(actor.gameObject);
        return IsRealPlayerPartyActor(target) ? target : null;
    }

    private static CombatTarget ResolvePlayerPartyTarget(Transform candidate)
    {
        if (candidate == null)
            return null;

        CombatTarget target = candidate.GetComponentInParent<CombatTarget>();
        return IsRealPlayerPartyActor(target) ? target : null;
    }

    private static CombatTarget ResolveCombatTarget(GameObject actor)
    {
        if (actor == null)
            return null;

        CombatTarget target = actor.GetComponent<CombatTarget>();
        if (target == null)
            target = actor.GetComponentInParent<CombatTarget>();
        if (target == null)
            target = actor.GetComponentInChildren<CombatTarget>(true);
        return target;
    }

    private static bool IsRealPlayerPartyActor(CombatTarget target)
    {
        return target != null
            && target.IsAlive
            && target.Team == CombatTeam.PlayerParty
            && CombatTeamUtility.ResolvePlayerActorObject(target.gameObject) != null;
    }

    private static int ResolveMemberIndex(CombatTarget target)
    {
        PlayerActorRuntime member = target != null ? target.GetComponentInParent<PlayerActorRuntime>() : null;
        return member != null ? member.ActorIndex : -1;
    }

    private static void EnsureLeaderSubscription()
    {
        PlayerContext context = PlayerContext.GetOrCreate();
        if (subscribedLeaderContext == context)
            return;

        if (subscribedLeaderContext != null)
            subscribedLeaderContext.CurrentActorChanged -= HandleLeaderChanged;
        subscribedLeaderContext = context;
        if (subscribedLeaderContext == null)
            return;

        subscribedLeaderContext.CurrentActorChanged += HandleLeaderChanged;
        HandleLeaderChanged(subscribedLeaderContext.CurrentActor);
    }

    private static void HandleLeaderChanged(PlayerActorRuntime leader)
    {
        Transform nextLeader = leader != null ? leader.transform : null;
        if (nextLeader == currentLeaderTransform)
            return; // 같은 리더 인덱스의 중복 이벤트는 재설정하지 않음

        Transform previousLeader = currentLeaderTransform;
        currentLeaderTransform = nextLeader;
        currentLeaderMemberIndex = leader != null ? leader.ActorIndex : -1;

        EnemySnapshot.Clear();
        EnemySnapshot.AddRange(RegisteredEnemies);
        for (int i = 0; i < EnemySnapshot.Count; i++)
        {
            EnemyAIController enemy = EnemySnapshot[i];
            if (enemy != null && enemy.isActiveAndEnabled)
                enemy.HandlePartyLeaderChanged(previousLeader, nextLeader);
        }
        EnemySnapshot.Clear();
    }

    private static void CleanupInvalidEntries()
    {
        int frame = Time.frameCount;
        if (lastCleanupFrame == frame)
            return;
        lastCleanupFrame = frame;

        EnemyCleanup.Clear();
        AttackReservationCleanup.Clear();
        foreach (KeyValuePair<EnemyAIController, TargetAssignment> pair in TargetAssignments)
        {
            if (pair.Key == null || !pair.Key.isActiveAndEnabled || pair.Value == null)
                EnemyCleanup.Add(pair.Key);
        }

        foreach (KeyValuePair<EnemyAIController, CombatTarget> pair in AttackReservations)
        {
            if (pair.Key == null || !pair.Key.isActiveAndEnabled)
            {
                if (!EnemyCleanup.Contains(pair.Key))
                    EnemyCleanup.Add(pair.Key);
            }
            else if (pair.Value == null || !pair.Value.IsAlive)
            {
                AttackReservationCleanup.Add(pair.Key);
            }
        }

        foreach (KeyValuePair<EnemyAIController, float> pair in AttackReadyTimes)
        {
            if (pair.Key == null || !pair.Key.isActiveAndEnabled)
            {
                if (!EnemyCleanup.Contains(pair.Key))
                    EnemyCleanup.Add(pair.Key);
            }
        }

        for (int i = 0; i < EnemyCleanup.Count; i++)
        {
            EnemyAIController enemy = EnemyCleanup[i];
            TargetAssignments.Remove(enemy);
            AttackReservations.Remove(enemy);
            AttackReadyTimes.Remove(enemy);
            RegisteredEnemies.Remove(enemy);
        }
        EnemyCleanup.Clear();

        for (int i = 0; i < AttackReservationCleanup.Count; i++)
            AttackReservations.Remove(AttackReservationCleanup[i]);
        AttackReservationCleanup.Clear();
    }

    private static float HorizontalSqrDistance(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        delta.y = 0f;
        return delta.sqrMagnitude;
    }
}
