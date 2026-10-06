using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CombatHealth))]
[RequireComponent(typeof(EnemyMovement))]
[RequireComponent(typeof(EnemyMovementReaction))]
[RequireComponent(typeof(EnemySensor))]
[RequireComponent(typeof(EnemyCrowdAgent))]
public sealed partial class EnemyAIController : MonoBehaviour // 적 상태 조립 및 전환
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
    [SerializeField, Min(0f)] private float chaseObservationInterval = 0.65f; // target observation, independent of CPU LOD
    internal float ChaseObservationInterval => delayedChaseEnabled ? Mathf.Max(0f, chaseObservationInterval) : 0f;
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
    private float nextChasePlanningTime, nextSeparationPlanningTime, nextTargetPlanningTime;
    private bool hasCachedChasePlan;
    private bool hasChaseObservation, continuesObservedHeading, delayedChaseEnabled;
    private Transform observedChaseTarget;
    private float nextChaseObservationTime;
    private Vector3 observedChaseHeading;
    private float lastChasePlanningTime = -1f, chasePlanningDeltaTime;
    private Vector3 cachedChasePlan, cachedPlanTargetPosition;
    private int planningEvaluationCount, planningReuseCount;
    public int PlanningEvaluationCount => planningEvaluationCount;
    public int PlanningReuseCount => planningReuseCount;
    private static readonly Unity.Profiling.ProfilerMarker ChasePlanMarker = new Unity.Profiling.ProfilerMarker("Overburst.AI.ChasePlan");
    private static readonly Unity.Profiling.ProfilerMarker TargetPlanMarker = new Unity.Profiling.ProfilerMarker("Overburst.AI.TargetPlan");
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
    public int CrowdMovePriority => EnemySquadPursuitRuntimeService.GetMovePriority(this);
    public string SquadPursuitDebugModeName => EnemySquadPursuitRuntimeService.GetDebugModeName(this);
    public float CurrentChaseSpeedMultiplier => squadPursuitMoveActive
        ? squadPursuitSpeedMultiplier
        : clusterFanOutActive
        ? clusterFanOutSpeedMultiplier
        : chaseBypassActive ? chaseBypassSpeedMultiplier : 1f;

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

        if (movementReaction != null && movementReaction.BlocksAttack)
        {
            TryStartStrongThroughHit();
            return;
        }

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

    // A started attack owns its animation tail for every locomotion profile.
    // Aim style only chooses where it strikes; it cannot shorten a missed swing.
    internal bool IsCommittedAttackPlaying => IsAttackInProgress()
        || (animationBridge != null && animationBridge.BlocksAttackStart)
        || (movement != null && movement.IsActionLocked);

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
        var projectile = GetComponent<EnemyThemeSpecialExecutor>();
        bool completedWeakAction = projectile != null && projectile.BeginCompletedWeakActionCleanup();
        try
        {
            if (abilityController != null)
                abilityController.Cancel();
            else
                meleeAttack?.CancelAttack();
        }
        finally
        {
            if (completedWeakAction) projectile.EndCompletedWeakActionCleanup();
        }
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

    private float PlanningInterval => EnemyAiTickScheduler.ResolvePlanningInterval(
        ActiveEnemies.Count, ResolveAiLodSqrDistance(target != null && target.gameObject.activeInHierarchy));

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
