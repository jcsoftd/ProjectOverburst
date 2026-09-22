using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CombatHealth))]
[RequireComponent(typeof(EnemyMovement))]
[RequireComponent(typeof(EnemyMovementReaction))]
[RequireComponent(typeof(EnemySensor))]
[RequireComponent(typeof(EnemyCrowdAgent))]
public sealed class EnemyAIController : MonoBehaviour // 적 상태 조립 및 전환
{
    public const float BaseCombatLoseTargetRange = 30f;

    private const int PatrolDestinationAttempts = 8;
    private const float CombatSeparationRadiusRatio = 0.72f;
    private const float CombatSeparationSpeedMultiplier = 0.55f;
    private const float CombatSeparationStopDistance = 0.05f;
    private const float CombatSeparationMinStep = 0.2f;
    private const float CombatSeparationMaxStep = 0.55f;
    private static readonly HashSet<EnemyAIController> ActiveEnemies = new HashSet<EnemyAIController>();
    private static readonly List<EnemyAIController> SupportSnapshot = new List<EnemyAIController>();
    private static int sessionMonsterDeathCount;

    public static bool DensityApproachSteeringEnabled { get; private set; } = true;
    public static bool ChaseBypassSteeringEnabled { get; private set; } = true;
    public static bool ClusterFanOutSteeringEnabled { get; private set; } = true;

    [Header("References")]
    [SerializeField] private CombatHealth health;
    [SerializeField] private EnemyMovement movement;
    [SerializeField] private EnemyMovementReaction movementReaction;
    [SerializeField] private EnemySensor sensor;
    [SerializeField] private EnemyMeleeAttackController meleeAttack;
    [SerializeField] private EnemyAbilityController abilityController;
    [SerializeField] private EnemyAnimationBridge animationBridge;
    [SerializeField] private EnemyDefenseController defenseController;
    [SerializeField] private EnemyBehaviorProfile behaviorProfile;
    [SerializeField] private EnemyAiPreset squadPursuitPreset;
    [SerializeField] private EnemySquadParticipationMode squadParticipationMode = EnemySquadParticipationMode.SquadMember;
    [SerializeField] private Transform target;

    private GameObject squadEncounterOwner; // 부대 집계 전투 구역
    private Transform squadEncounterAnchor; // 원거리 포위 중심

    [Header("State Distances")]
    [SerializeField] private float detectionRange = 10f;
    [SerializeField] private float loseTargetRange = 14f;
    [SerializeField] private float moveStopDistance = 1.5f;
    [SerializeField] private float attackEnterRange = 1.7f;
    [SerializeField] private float attackExitRange = 2.1f;
    [SerializeField] private float returnArriveDistance = 0.3f;

    [Header("Aggro Retention")]
    [SerializeField, Min(0f)] private float aggroReleaseDelay = 2f;

    [Header("Target Resolution")]
    [SerializeField] private bool findPlayerByTag = true;
    [SerializeField] private string playerTag = "Player";

    [Header("Approach")]
    [SerializeField] private bool useDensityApproachSteering; // 핵심 멀록 근거리 밀도 접근

    [Header("Debug")]
    [SerializeField] private bool logStateChanges;

    private EnemyStateMachine stateMachine;
    private EnemyRoamState roamState;
    private EnemySuspiciousState suspiciousState;
    private EnemyChaseState chaseState;
    private EnemyCombatWaitState combatWaitState;
    private EnemyAttackState attackState;
    private EnemyRepositionState repositionState;
    private EnemyDefendState defendState;
    private EnemyReturnState returnState;
    private EnemyDeadState deadState;
    private EnemyCombatBehavior combatBehavior;
    private Vector3 homePosition;
    private bool initialized;
    private Renderer[] aiLodRenderers;
    private float nextAiTickTime;
    private float currentAiTickInterval;
    private bool aiTickScheduleInitialized;
    private bool forceAiTick;
    private int aiTickCount;
    private int aiSkippedUpdateCount;
    private float nextIdleBreakTime;
    private bool lowHealthRepositionConsumed;
    private Vector3 approachDirection;
    private Vector3 smoothedSeparationDirection;
    private float nextApproachDirectionRefreshTime;
    private int approachDirectionRevision;
    private bool preferSideApproach;
    private bool pendingAlertReaction;
    private bool pendingSupportCall;
    private float nextDodgeLungeTime;
    private float aggroReleaseCandidateSince = -1f;
    private bool sessionDeathReported;
    private EnemyBehaviorProfile runtimeFallbackProfile;
    private EnemyCrowdAgent crowdAgent;
    private readonly List<EnemyCrowdAgent> crowdNeighbors = new List<EnemyCrowdAgent>(16);
    private readonly List<EnemyApproachNeighbor> densityApproachNeighbors = new List<EnemyApproachNeighbor>(16);
    private readonly List<EnemyApproachNeighbor> chaseBypassNeighbors = new List<EnemyApproachNeighbor>(24);
    private int densityApproachTurnSign;
    private int densityApproachRevision;
    private float nextDensityApproachDecisionTime;
    private float densityApproachTurnLockEndTime;
    private bool densityApproachActive;
    private bool? densityApproachRuntimeOverride;
    private int chaseBypassTurnSign;
    private int chaseBypassRevision;
    private float nextChaseBypassDecisionTime;
    private float chaseBypassTurnLockEndTime;
    private float chaseBypassSpeedMultiplier = 1f;
    private bool chaseBypassActive;
    private int clusterFanOutSideSign;
    private float clusterFanOutSpeedMultiplier = 1f;
    private bool clusterFanOutActive;
    private bool squadPursuitMoveActive;
    private EnemyLocomotionMode squadPursuitLocomotion = EnemyLocomotionMode.Walk;
    private float squadPursuitSpeedMultiplier = 1f;
    private bool authoredCombatRangesCaptured;
    private float authoredAttackExitPadding;
    private float authoredMoveStopOffset;

    public string CurrentStateName => stateMachine != null && stateMachine.CurrentState != null
        ? stateMachine.CurrentState.Name
        : "None";
    public string CurrentDebugStateName
    {
        get
        {
            IEnemyState current = stateMachine?.CurrentState;
            if (current == null)
                return "None"; // HP Bar가 AI 초기화보다 먼저 바인딩돼도 안전

            if (ReferenceEquals(current, roamState))
                return "Roam/" + roamState.DebugModeName;
            if (ReferenceEquals(current, suspiciousState))
                return "Suspicious/" + suspiciousState.DebugModeName;
            if (ReferenceEquals(current, chaseState))
                return "Chase/" + chaseState.DebugModeName;
            if (ReferenceEquals(current, repositionState))
                return "Reposition/" + repositionState.DebugModeName;
            return current != null ? current.Name : "None";
        }
    }
    private EnemyTacticalProfile tacticalProfile;
    private EnemyTacticalPositioning tacticalPositioning;
    public EnemyTacticalProfile TacticalProfile => tacticalProfile;
    public bool UsesRangedTactics => tacticalProfile != null && tacticalProfile.UsesRangedPositioning;
    public bool UsesMeleeSquadMovement => !UsesRangedTactics;
    public string TacticalReason => tacticalPositioning != null ? tacticalPositioning.Reason : "Legacy";
    public bool TacticalRetreatUsed => tacticalPositioning != null && tacticalPositioning.RetreatUsed;
    public uint TacticalContextGeneration => tacticalPositioning != null ? tacticalPositioning.ContextGeneration : 0;
    public Vector3 TacticalDestination => tacticalPositioning != null ? tacticalPositioning.Destination : transform.position;
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
            || animationBridge != null && animationBridge.IsBlockingActionActive) return true;
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
            movement.SetFacingDestination(destination, .15f, target.position, EnemyLocomotionMode.Backpedal, 1f);
            tacticalPositioning.CommitRetreat();
        }
        else movement.SetDestination(decision == EnemyTacticalDecision.Navigate ? ResolveChasePlan() : destination,
            .15f, SelectChaseLocomotion(), 1f);
        return true;
    }

    public Transform Target => target;
    public EnemyPartyTargetPhase PartyTargetPhase => EnemyCombatCoordinator.GetPartyTargetPhase(this);
    public int TargetPartyMemberIndex => EnemyCombatCoordinator.GetPartyTargetMemberIndex(this);
    public GameObject SquadEncounterOwner => squadEncounterOwner;
    public Transform SquadEncounterAnchor => squadEncounterAnchor;
    public Vector3 HomePosition => homePosition;
    public EnemyMovement Movement => movement;
    public EnemyMovementReaction MovementReaction => movementReaction;
    public EnemyMeleeAttackController MeleeAttack => meleeAttack;
    public EnemyAbilityController AbilityController => abilityController;
    public EnemyAnimationBridge AnimationBridge => animationBridge;
    public EnemyDefenseController DefenseController => defenseController;
    public EnemyBehaviorProfile BehaviorProfile => ResolveBehaviorProfile();
    public bool CanDefend => BehaviorProfile.HasShield && defenseController != null;
    public float DetectionRange => Mathf.Max(0f, detectionRange);
    public float LoseTargetRange => Mathf.Max(DetectionRange, loseTargetRange);
    public float MoveStopDistance => Mathf.Max(0f, moveStopDistance);
    public float AttackEnterRange => Mathf.Max(0f, abilityController != null && !abilityController.IsExecuting
        ? abilityController.ResolveEngagementRange(target) : attackEnterRange);
    public float AttackExitRange => Mathf.Max(AttackEnterRange, attackExitRange);
    public float ReturnArriveDistance => Mathf.Max(0.01f, returnArriveDistance);
    public float AggroReleaseDelay => Mathf.Max(0f, aggroReleaseDelay);
    public float CombatLoseTargetRange => CurrentCombatLoseTargetRange;
    public float NormalizedHealth => health != null ? health.NormalizedHp : 1f;
    public float DiscoverRange => Mathf.Min(DetectionRange, BehaviorProfile.DiscoverRange);
    public float InvestigateRange => Mathf.Max(DiscoverRange, BehaviorProfile.InvestigateRange);
    public float NoticeRange => Mathf.Max(InvestigateRange, BehaviorProfile.NoticeRange);
    public Vector3 LastHeardPosition => sensor != null ? sensor.LastHeardPosition : transform.position;
    public static int ActiveEnemyCount => ActiveEnemies.Count;
    public static int AliveEnemyCount => CountActiveEnemies(false);
    public static int AggroEnemyCount => CountActiveEnemies(true);
    public static int SessionMonsterDeathCount => sessionMonsterDeathCount;
    public static float CurrentCombatLoseTargetRange => BaseCombatLoseTargetRange;
    public bool IsAggroActive => IsCombatStateName(CurrentStateName);
    public float CurrentAiTickInterval => currentAiTickInterval;
    public int AiTickCount => aiTickCount;
    public int AiSkippedUpdateCount => aiSkippedUpdateCount;
    public bool UsesDensityApproachSteering => DensityApproachSteeringEnabled && ResolveDensityApproachPreference();
    public bool IsDensityApproachActive => densityApproachActive;
    public int CurrentDensityApproachTurnSign => densityApproachTurnSign;
    public bool UsesChaseBypassSteering => ChaseBypassSteeringEnabled && UsesDensityApproachSteering;
    public bool IsChaseBypassActive => chaseBypassActive;
    public bool UsesClusterFanOutSteering => ClusterFanOutSteeringEnabled && UsesDensityApproachSteering;
    public bool IsClusterFanOutActive => clusterFanOutActive;
    public EnemyAiPreset SquadPursuitPreset => ResolveSquadPursuitPreset();
    public EnemySquadParticipationMode SquadParticipationMode => squadParticipationMode;
    public bool UsesSquadPursuit => squadParticipationMode == EnemySquadParticipationMode.SquadMember
        && ResolveSquadPursuitPreset() != null;
    public bool UsesMurlocSquadPursuit => UsesSquadPursuit; // 레거시 검증기 호환 표면
    public int CrowdMovePriority => EnemySquadPursuitRuntimeService.GetMovePriority(this);
    public string SquadPursuitDebugModeName => EnemySquadPursuitRuntimeService.GetDebugModeName(this);
    public float CurrentChaseSpeedMultiplier => squadPursuitMoveActive
        ? squadPursuitSpeedMultiplier
        : clusterFanOutActive
        ? clusterFanOutSpeedMultiplier
        : chaseBypassActive ? chaseBypassSpeedMultiplier : 1f;

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

        return IsCoreMurlocProfile();
    }

    private bool IsCoreMurlocProfile()
    {
        string profileId = behaviorProfile != null ? behaviorProfile.ProfileId : string.Empty;
        switch (profileId)
        {
            case "Murloc_Grunt":
            case "Murloc_Scout":
            case "Murloc_Spearling":
            case "Murloc_Guard":
            case "Murloc_Brute":
            case "Murloc_Warlord":
                return true;
            default:
                return false;
        }
    }

    private EnemyAiPreset ResolveSquadPursuitPreset()
    {
        if (squadParticipationMode != EnemySquadParticipationMode.SquadMember)
            return null;
        if (squadPursuitPreset != null)
            return squadPursuitPreset;
        if (!IsCoreMurlocProfile())
            return null;

        squadPursuitPreset = Resources.Load<EnemyAiPreset>("Enemies/AiPresets/AIP_MurlocSquad");
        return squadPursuitPreset; // 기존 6종만 사용하는 이관 폴백
    }

    private void Awake()
    {
        ResolveReferences();
        CaptureAuthoredCombatRanges();
        homePosition = transform.position;
        approachDirection = ResolveFallbackApproachDirection(GetInstanceID());
        InitializeStateMachine();
        EnableStateDrivenComponents();
    }

    private void OnEnable()
    {
        ActiveEnemies.Add(this);
        EnemyCombatCoordinator.Register(this);
        ResolveReferences();
        InitializeStateMachine();
        EnableStateDrivenComponents();
        homePosition = transform.position;
        lowHealthRepositionConsumed = false;
        nextDodgeLungeTime = 0f;
        sessionDeathReported = false;
        ResetAggroReleaseCandidate();
        ResetDensityApproachPlan();
        ResetAiTickSchedule();
        EnemySquadPursuitRuntimeService.Register(this);
        tacticalPositioning?.Bind();

        if (health != null)
        {
            health.OnDamaged += HandleDamaged;
            health.OnDead += HandleDead;
        }

        if (health != null && health.IsDead)
            ChangeState(deadState);
        else
        {
            PrepareMovementForAliveState();
            ChangeToRoam();
        }
    }

    private void OnDisable()
    {
        tacticalPositioning?.Dispose();
        ActiveEnemies.Remove(this);
        EnemySquadPursuitRuntimeService.Unregister(this);
        EnemyCombatCoordinator.Unregister(this);
        if (health != null)
        {
            health.OnDamaged -= HandleDamaged;
            health.OnDead -= HandleDead;
        }

        stateMachine?.Clear();
        defenseController?.SetDefending(false);
        ResetDensityApproachPlan();
    }

    private void Update()
    {
        if (!initialized)
            return;

        if (health != null && health.IsDead)
        {
            ChangeState(deadState);
            return;
        }

        if (ReferenceEquals(stateMachine.CurrentState, deadState))
        {
            PrepareMovementForAliveState();
            ChangeToRoam();
        }

        if (movementReaction != null && movementReaction.IsStunned)
            return;

        if (!ShouldRunAiTick())
            return;

        if (IsAggroActive)
            RefreshPartyTargetPhase();
        stateMachine.Update();
    }

    public void Configure(Transform playerTarget, Vector3 spawnPosition, float newDetectionRange, float newMoveStopDistance)
    {
        SetTarget(EnemyCombatCoordinator.ResolveCurrentLeaderTarget(playerTarget));
        homePosition = spawnPosition;
        float requestedDetectionRange = Mathf.Max(0f, newDetectionRange);
        if (requestedDetectionRange <= LoseTargetRange)
            detectionRange = requestedDetectionRange;

        RefreshCombatRangesFromAbilities();
        float maximumMoveStopDistance = Mathf.Max(0f, attackEnterRange - 0.2f);
        moveStopDistance = Mathf.Min(Mathf.Max(0f, newMoveStopDistance), maximumMoveStopDistance);
    }

    public void RefreshCombatRangesFromAbilities()
    {
        CaptureAuthoredCombatRanges();
        float resolvedAttackRange = abilityController != null
            ? abilityController.AttackRange
            : meleeAttack != null ? meleeAttack.AttackRange : attackEnterRange;
        attackEnterRange = Mathf.Max(0f, resolvedAttackRange);
        attackExitRange = attackEnterRange + authoredAttackExitPadding;
        float maximumMoveStopDistance = Mathf.Max(0f, attackEnterRange - 0.05f);
        moveStopDistance = Mathf.Min(
            Mathf.Max(0f, attackEnterRange - authoredMoveStopOffset),
            maximumMoveStopDistance);
    }

    public void SetHomePosition(Vector3 position)
    {
        homePosition = position;
    }

    public void SetSquadEncounter(GameObject encounterOwner, Transform encounterAnchor)
    {
        encounterAnchor = EnemyCombatCoordinator.ResolveCurrentLeaderTarget(encounterAnchor);
        if (squadEncounterOwner == encounterOwner && squadEncounterAnchor == encounterAnchor)
            return;

        squadEncounterOwner = encounterOwner; // 타깃과 분리된 전투 구역
        squadEncounterAnchor = encounterAnchor; // 플레이어 기준 포위 중심
        tacticalPositioning?.Bind();
        EnemySquadPursuitRuntimeService.NotifyEncounterBindingChanged(this);
        RequestImmediateAiTick();
    }

    public void SetSquadPursuitProfile(
        EnemyAiPreset preset,
        EnemySquadParticipationMode participationMode)
    {
        bool wasRegistered = isActiveAndEnabled && UsesSquadPursuit;
        if (wasRegistered)
            EnemySquadPursuitRuntimeService.Unregister(this);

        squadPursuitPreset = preset;
        squadParticipationMode = participationMode;

        if (isActiveAndEnabled && UsesSquadPursuit)
            EnemySquadPursuitRuntimeService.Register(this);
        RequestImmediateAiTick();
    }

    public void SetTarget(Transform newTarget)
    {
        if (target != newTarget)
            EnemyCombatCoordinator.Release(this);
        ApplyTarget(newTarget);
        RequestImmediateAiTick();
    }

    public void SetBehaviorProfile(EnemyBehaviorProfile profile)
    {
        behaviorProfile = profile;
        defenseController?.SetProfile(profile);
    }

    public bool TryStartAttack()
    {
        return abilityController != null
            ? abilityController.TryStart(target)
            : meleeAttack != null && meleeAttack.TryStartAttack(target);
    }

    public bool IsAttackInProgress()
    {
        return abilityController != null
            ? abilityController.IsExecuting
            : meleeAttack != null && meleeAttack.IsAttacking;
    }

    internal bool IsCommittedAttackPlaying => abilityController != null && abilityController.UsesCommittedAim
        && (abilityController.IsExecuting || (animationBridge != null && animationBridge.IsBlockingActionActive)
            || (movement != null && movement.IsActionLocked));

    internal float AttackTargetDistance
    {
        get
        {
            if (!IsTargetValid()) return float.PositiveInfinity;
            Vector3 point = abilityController != null ? abilityController.ResolveAimPosition(target) : target.position;
            return Mathf.Sqrt(HorizontalSqrDistance(transform.position, point));
        }
    }

    public void CancelAttack()
    {
        if (abilityController != null)
            abilityController.Cancel();
        else
            meleeAttack?.CancelAttack();
    }

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

    public float TargetDistance => IsTargetValid()
        ? Mathf.Sqrt(HorizontalSqrDistance(transform.position, target.position))
        : float.PositiveInfinity;

    internal void TryPlayIdleBreak()
    {
        if (Time.time < nextIdleBreakTime)
            return;

        EnemyBehaviorProfile profile = BehaviorProfile;
        animationBridge?.PlayIdleBreak();
        nextIdleBreakTime = Time.time + Random.Range(profile.IdleBreakMinInterval, profile.IdleBreakMaxInterval);
    }

    internal void FaceTarget()
    {
        if (IsTargetValid())
        {
            Vector3 point = ReferenceEquals(stateMachine?.CurrentState, combatWaitState) && abilityController != null
                ? abilityController.PrepareAttackAim(target) : target.position;
            movement?.FacePosition(point);
        }
    }

    internal void ChangeToRoam()
    {
        EnemyCombatCoordinator.Release(this);
        ChangeState(roamState);
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
        ClearSquadPursuitMove();
        densityApproachActive = false;
        ClearClusterFanOutActivity(false);
        ClearChaseBypassActivity();
    }

    internal void ChangeToSuspicious() => ChangeState(suspiciousState);
    internal void ChangeToChase() => ChangeState(chaseState);
    internal void ChangeToCombatWait(float delay = 0f)
    {
        if (ReferenceEquals(stateMachine?.CurrentState, combatWaitState))
        {
            combatWaitState.Delay(delay);
            return;
        }

        combatWaitState.PrepareDelay(delay);
        ChangeState(combatWaitState);
    }
    internal void ChangeToAttack()
    {
        // Keep completing the current facing action when no attack can start.
        // Entering Attack on a refusal would charge recovery and a turn cooldown.
        if (abilityController != null && !abilityController.HasAvailableAbility(target))
        {
            // Chase calls this on entering range; CombatWait owns stationary facing.
            // Do not restart its timer while it is already completing that turn.
            if (!ReferenceEquals(stateMachine?.CurrentState, combatWaitState))
                ChangeToCombatWait();
            return;
        }
        if (EnemyCombatCoordinator.TryAcquireAttackTurn(this, target))
            ChangeState(attackState);
        else
            ChangeToCombatWait(BehaviorProfile.AttackWaitDuration * Random.Range(0.85f, 1.15f));
    }
    internal void ChangeToReposition(bool lowHealthBackstep = false)
    {
        repositionState.PrepareBackpedal(lowHealthBackstep);
        ChangeState(repositionState);
    }

    internal bool TryEnterDodgeLunge()
    {
        EnemyBehaviorProfile profile = BehaviorProfile;
        if (profile.RepositionStyle != EnemyRepositionStyle.Dodge
            || Time.time < nextDodgeLungeTime
            || !IsTargetValid())
            return false;

        float distance = TargetDistance;
        if (distance < profile.DodgeLungeMinDistance || distance > profile.DodgeLungeMaxDistance)
            return false;

        nextDodgeLungeTime = Time.time + profile.DodgeLungeCooldown;
        if (Random.value > profile.DodgeLungeChance)
            return false; // 같은 쿨타임 구간에는 다시 굴리지 않아 연속 발동 방지

        repositionState.PrepareDodgeLunge();
        ChangeState(repositionState);
        return true;
    }

    internal bool TryConsumeLowHealthReposition()
    {
        EnemyBehaviorProfile profile = BehaviorProfile;
        float threshold = profile.LowHealthRepositionThreshold;
        if (threshold <= 0f || NormalizedHealth > threshold)
        {
            lowHealthRepositionConsumed = false;
            return false;
        }

        if (lowHealthRepositionConsumed || TargetDistance > AttackExitRange + 1f)
            return false;

        lowHealthRepositionConsumed = true;
        return true;
    }
    internal void ChangeToDefend() => ChangeState(defendState);
    internal void ChangeToReturn()
    {
        tacticalPositioning?.Reset();
        EnemyCombatCoordinator.Release(this);
        ResetAggroReleaseCandidate();
        ChangeState(returnState);
    }

    private void ResetAggroReleaseCandidate()
    {
        aggroReleaseCandidateSince = -1f;
    }

    private void ChangeState(IEnemyState nextState)
    {
        if (!ReferenceEquals(nextState, combatWaitState) && !ReferenceEquals(nextState, attackState))
            abilityController?.ClearPreparedAim();
        stateMachine?.ChangeState(nextState);
    }

    internal void ReleaseAttackTurn()
    {
        EnemyCombatCoordinator.ReleaseAttackTurn(this, BehaviorProfile.AttackTurnCooldown);
    }

    private void HandleDead(CombatHealth source, DamageInfo info)
    {
        if (!sessionDeathReported)
        {
            sessionDeathReported = true;
            sessionMonsterDeathCount++; // 디버그 HUD용 현재 플레이 세션 처치 수
        }

        EnemyCombatCoordinator.Release(this);
        ChangeState(deadState);
    }

    private void HandleDamaged(CombatHealth source, DamageInfo info)
    {
        if (!CombatTeamUtility.IsPlayerActorDamage(info))
            return;

        GameObject playerActor = CombatTeamUtility.ResolvePlayerActorObject(info.source);
        RequestAggro(playerActor != null ? playerActor.transform : target, true);
    }

    private void ResolveReferences()
    {
        if (health == null)
            health = GetComponent<CombatHealth>();
        if (movement == null)
            movement = GetComponent<EnemyMovement>();
        if (movementReaction == null)
            movementReaction = GetComponent<EnemyMovementReaction>();
        if (sensor == null)
            sensor = GetComponent<EnemySensor>();
        if (meleeAttack == null)
            meleeAttack = GetComponent<EnemyMeleeAttackController>();
        if (abilityController == null)
            abilityController = GetComponent<EnemyAbilityController>();
        if (animationBridge == null)
            animationBridge = GetComponent<EnemyAnimationBridge>();
        if (defenseController == null)
            defenseController = GetComponent<EnemyDefenseController>();
        if (crowdAgent == null)
            crowdAgent = GetComponent<EnemyCrowdAgent>();
        if (crowdAgent == null && Application.isPlaying)
            crowdAgent = gameObject.AddComponent<EnemyCrowdAgent>(); // 기존 프리팹 런타임 보완
        if (aiLodRenderers == null || aiLodRenderers.Length == 0)
            aiLodRenderers = GetComponentsInChildren<Renderer>(true); // 가시성 판단 캐시
        defenseController?.SetProfile(behaviorProfile);
    }

    private void CaptureAuthoredCombatRanges()
    {
        if (authoredCombatRangesCaptured)
            return;

        authoredAttackExitPadding = Mathf.Max(0.4f, attackExitRange - attackEnterRange);
        authoredMoveStopOffset = Mathf.Max(0.05f, attackEnterRange - moveStopDistance);
        authoredCombatRangesCaptured = true;
    }

    private bool ShouldRunAiTick()
    {
        float now = Time.time;
        if (aiTickScheduleInitialized && !forceAiTick && now < nextAiTickTime)
        {
            aiSkippedUpdateCount++;
            return false; // 예약 전 프레임은 거리·가시성 조회 생략
        }

        bool hasActiveTarget = target != null && target.gameObject.activeInHierarchy;
        float sqrDistance = ResolveAiLodSqrDistance(hasActiveTarget);
        bool requiresFullRate = forceAiTick || RequiresFullRateAi(sqrDistance, hasActiveTarget);
        bool isVisible = requiresFullRate
            || EnemyAiTickScheduler.IsLikelyVisible(aiLodRenderers, transform.position);
        float interval = EnemyAiTickScheduler.ResolveInterval(
            ActiveEnemies.Count,
            sqrDistance,
            isVisible,
            requiresFullRate);
        currentAiTickInterval = interval;

        if (interval <= 0f || forceAiTick)
        {
            forceAiTick = false;
            aiTickScheduleInitialized = false;
            aiTickCount++;
            return true;
        }

        if (!aiTickScheduleInitialized)
        {
            nextAiTickTime = now + EnemyAiTickScheduler.ResolveStaggerDelay(interval, GetInstanceID());
            aiTickScheduleInitialized = true;
        }

        if (now < nextAiTickTime)
        {
            aiSkippedUpdateCount++;
            return false;
        }

        nextAiTickTime = now + interval;
        aiTickCount++;
        return true;
    }

    private bool RequiresFullRateAi(float sqrDistance, bool hasActiveTarget)
    {
        IEnemyState current = stateMachine != null ? stateMachine.CurrentState : null;
        if (ReferenceEquals(current, attackState)
            || ReferenceEquals(current, repositionState)
            || ReferenceEquals(current, defendState))
        {
            return true;
        }

        return hasActiveTarget
            && sqrDistance <= EnemyAiTickScheduler.FullRateDistance * EnemyAiTickScheduler.FullRateDistance;
    }

    private float ResolveAiLodSqrDistance(bool hasActiveTarget)
    {
        if (hasActiveTarget)
            return HorizontalSqrDistance(transform.position, target.position);

        return EnemyAiTickScheduler.ResolveCameraSqrDistance(transform.position);
    }

    private void ResetAiTickSchedule()
    {
        nextAiTickTime = 0f;
        currentAiTickInterval = 0f;
        aiTickScheduleInitialized = false;
        forceAiTick = false;
        aiTickCount = 0;
        aiSkippedUpdateCount = 0;
    }

    internal void NotifyFacingTurnCompleted() => RequestImmediateAiTick();

    private void RequestImmediateAiTick()
    {
        forceAiTick = true; // 피격·지원 요청은 LOD 대기 없이 반응
        nextAiTickTime = 0f;
        aiTickScheduleInitialized = false;
    }

    private void InitializeStateMachine()
    {
        if (initialized)
            return;

        stateMachine = new EnemyStateMachine();
        roamState = new EnemyRoamState(this);
        suspiciousState = new EnemySuspiciousState(this);
        chaseState = new EnemyChaseState(this);
        combatWaitState = new EnemyCombatWaitState(this);
        attackState = new EnemyAttackState(this);
        repositionState = new EnemyRepositionState(this);
        defendState = new EnemyDefendState(this);
        returnState = new EnemyReturnState(this);
        deadState = new EnemyDeadState(this);
        combatBehavior = new EnemyCombatBehavior(this);
        stateMachine.StateChanged += HandleStateChanged;
        initialized = true;
        EnemyBehaviorProfile profile = BehaviorProfile;
        nextIdleBreakTime = Time.time + Random.Range(profile.IdleBreakMinInterval, profile.IdleBreakMaxInterval);
    }

    private void PrepareChaseAlert(bool callsSupport)
    {
        pendingAlertReaction = true;
        pendingSupportCall = callsSupport;
    }

    private void RefreshPartyTargetPhase()
    {
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

    private void EnableStateDrivenComponents()
    {
        if (movement != null && (health == null || !health.IsDead))
            movement.enabled = true;
    }

    private void PrepareMovementForAliveState()
    {
        if (movement == null)
            return;
        movement.enabled = true;
        movement.StopMovement();
    }

    private void HandleStateChanged(IEnemyState previousState, IEnemyState nextState)
    {
        if (!logStateChanges)
            return;
        string previousName = previousState != null ? previousState.Name : "None";
        string nextName = nextState != null ? nextState.Name : "None";
        Debug.Log("[EnemyAI] " + name + " " + previousName + " -> " + nextName, this);
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
                Mathf.Clamp01(Time.deltaTime * 4f));
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
                Mathf.Clamp01(Time.deltaTime * 4f));

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
                Mathf.Clamp01(Time.deltaTime * 4f));

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
                Mathf.Clamp01(Time.deltaTime * 4f));

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
                Mathf.Clamp01(Time.deltaTime * 4f));

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

    private EnemyBehaviorProfile ResolveBehaviorProfile()
    {
        if (behaviorProfile != null)
            return behaviorProfile;

        if (runtimeFallbackProfile == null)
        {
            runtimeFallbackProfile = ScriptableObject.CreateInstance<EnemyBehaviorProfile>();
            runtimeFallbackProfile.hideFlags = HideFlags.HideAndDontSave;
            runtimeFallbackProfile.Configure(
                "RuntimeFallback",
                EnemyBehaviorTendency.Assault,
                EnemyRepositionStyle.Backpedal,
                0.2f,
                false,
                0.35f,
                0.8f,
                1.2f,
                0.5f,
                0.25f,
                1.5f,
                0.6f,
                false,
                0f,
                0.8f,
                1f,
                120f);
            runtimeFallbackProfile.ConfigureRunApproach(6f);
            runtimeFallbackProfile.ConfigureApproach(1.35f, 15f, 1.5f, 1f);
            runtimeFallbackProfile.ConfigureAttackRhythm(1f, 0.45f);
            runtimeFallbackProfile.ConfigurePeace(0.5f, 4f, 0.65f);
            runtimeFallbackProfile.ConfigureAwareness(14f, 10f, 6f, 150f, 0.7f, 3.5f, 2.5f, 0.55f, 12f);
        }

        return runtimeFallbackProfile;
    }

    private static int CountActiveEnemies(bool aggroOnly)
    {
        int count = 0;
        foreach (EnemyAIController enemy in ActiveEnemies)
        {
            if (enemy == null || !enemy.isActiveAndEnabled || enemy.CurrentStateName == "Dead")
                continue;
            if (aggroOnly && !enemy.IsAggroActive)
                continue;
            count++;
        }
        return count;
    }

    private static bool IsCombatStateName(string stateName)
    {
        return stateName == "Chase"
            || stateName == "CombatWait"
            || stateName == "Attack"
            || stateName == "Reposition"
            || stateName == "Defend";
    }

}
