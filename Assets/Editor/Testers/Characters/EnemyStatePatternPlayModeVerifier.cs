using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class EnemyStatePatternPlayModeVerifier
{
    private const string ActiveKey = "EnemyStatePatternPlayModeVerifier.Active";
    private const string BatchKey = "EnemyStatePatternPlayModeVerifier.Batch";
    private const string ExitCodeKey = "EnemyStatePatternPlayModeVerifier.ExitCode";

    private enum VerifyStep
    {
        None,
        SetupMonster,
        CheckRoam,
        CheckChase,
        CheckAttack,
        CheckChaseAfterAttack,
        CheckChaseWithoutHomeLeash,
        CheckReturn,
        CheckReturnReaggro,
        CheckReturnAgain,
        CheckHomeRoam,
        CheckDead,
        CheckHitStunEnd,
        CheckKnockbackPunch,
        CheckKnockbackEnd,
        CheckHitAnimationStarted,
        CheckHitAnimationRestarted,
        CheckGroupRoam,
        CheckGroupAggro,
        CheckGroupAggroRetention,
        CheckAreaCleared,
        SetupStress40,
        CheckStress40,
        SetupStress100,
        CheckStress100,
        SetupStress200,
        CheckStress200
    }

    private static readonly List<string> passedPrefabs = new List<string>();
    private static VerifyStep step;
    private static int prefabIndex;
    private static int waitUntilFrame;
    private static float waitUntilTime;
    private static GameObject playerObject;
    private static CombatHealth playerHealth;
    private static GameObject monsterObject;
    private static EnemyAIController monsterAI;
    private static EnemyMovement monsterMovement;
    private static EnemyMovementReaction monsterMovementReaction;
    private static EnemyMeleeAttackController monsterAttack;
    private static CombatHealth monsterHealth;
    private static float playerHpBeforeAttack;
    private static float attackDamageDeadline;
    private static Rigidbody reactionBody;
    private static Vector3 reactionStartPosition;
    private static Vector3 reactionPunchPosition;
    private static EnemyAnimationBridge reactionAnimationBridge;
    private static Animator reactionAnimator;
    private static float hitNormalizedBeforeRestart;
    private static GameObject groupAreaObject;
    private static GameObject groupMonsterA;
    private static GameObject groupMonsterB;
    private static EnemyAIController groupAiA;
    private static EnemyAIController groupAiB;
    private static CombatHealth groupHealthA;
    private static CombatHealth groupHealthB;
    private static EnemyBehaviorProfile verificationBehaviorProfile;
    private static readonly List<GameObject> stressMonsters = new List<GameObject>(200);
    private static readonly List<EnemyAIController> stressAgents = new List<EnemyAIController>(200);
    private static GameObject stressCameraObject;
    private static float stressStartTime;
    private static int stressStartTickCount;
    private static int stressStartSkippedCount;

    static EnemyStatePatternPlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    [MenuItem("OVERBURST/Codex/Validation/Verify Enemy State Pattern PlayMode")]
    public static void RunFromMenu()
    {
        Begin(false);
    }

    public static void RunOnceFromCommandLine()
    {
        Begin(true);
    }

    public static void VerifySquadPursuitPlannerPureContract()
    {
        VerifySquadPursuitPlannerContract();
    }

    private static void Begin(bool batchMode)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new System.InvalidOperationException("PlayMode is already active or changing.");

        SessionState.SetBool(ActiveKey, true);
        SessionState.SetBool(BatchKey, batchMode);
        SessionState.SetInt(ExitCodeKey, 1);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(ActiveKey, false))
            return;

        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            BeginPlayModeVerification();
            return;
        }

        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= UpdateVerification;
            return;
        }

        if (state != PlayModeStateChange.EnteredEditMode)
            return;

        int exitCode = SessionState.GetInt(ExitCodeKey, 1);
        bool batchMode = SessionState.GetBool(BatchKey, false);
        SessionState.EraseBool(ActiveKey);
        SessionState.EraseBool(BatchKey);
        SessionState.EraseInt(ExitCodeKey);

        if (batchMode)
            EditorApplication.Exit(exitCode);
        else if (exitCode == 0)
            Debug.Log("[EnemyStatePatternPlayModeVerifier] Verification completed successfully.");
        else
            Debug.LogError("[EnemyStatePatternPlayModeVerifier] Verification failed.");
    }

    private static void BeginPlayModeVerification()
    {
        try
        {
            passedPrefabs.Clear();
            prefabIndex = 0;
            CreatePlayer();
            step = VerifyStep.SetupMonster;
            waitUntilFrame = Time.frameCount;
            waitUntilTime = Time.time;
            EditorApplication.update -= UpdateVerification;
            EditorApplication.update += UpdateVerification;
        }
        catch (System.Exception exception)
        {
            Fail(exception);
        }
    }

    private static void CreatePlayer()
    {
        playerObject = new GameObject("EnemyAI_VerificationPlayer");
        playerObject.tag = "Player";
        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0)
            playerObject.layer = playerLayer;

        CapsuleCollider collider = playerObject.AddComponent<CapsuleCollider>();
        collider.radius = 0.4f;
        collider.height = 2f;
        collider.center = new Vector3(0f, 1f, 0f);

        playerHealth = playerObject.AddComponent<CombatHealth>();
        CombatTarget.EnsureConfigured(playerObject, CombatTeam.PlayerParty);
        playerObject.transform.position = new Vector3(0f, 0f, 20f);
    }

    private static void UpdateVerification()
    {
        if (!EditorApplication.isPlaying)
            return;
        if (Time.frameCount < waitUntilFrame || Time.time < waitUntilTime)
            return;

        try
        {
            switch (step)
            {
                case VerifyStep.SetupMonster:
                    SetupMonster();
                    break;
                case VerifyStep.CheckRoam:
                    CheckState("Roam");
                    MovePlayerAndWait(new Vector3(0f, 0f, 5f), VerifyStep.CheckChase, 3, 1.6f);
                    break;
                case VerifyStep.CheckChase:
                    CheckState("Chase");
                    RequireMovingLocomotion(monsterMovement, CurrentPath() + " Chase");
                    monsterObject.transform.position = Vector3.zero;
                    playerHealth.ResetHealth();
                    playerHpBeforeAttack = playerHealth.CurrentHp;
                    // Existing Fishman first impact can occur around 5 seconds after range entry.
                    // Wait for actual damage with a bounded deadline, without changing AI or animation.
                    attackDamageDeadline = Time.time + 8f;
                    MovePlayerAndWait(new Vector3(0f, 0f, 1.6f), VerifyStep.CheckAttack, 3, 0.05f);
                    break;
                case VerifyStep.CheckAttack:
                    CheckStateAny("CombatWait", "Attack", "Reposition", "Defend");
                    if (playerHealth.CurrentHp >= playerHpBeforeAttack)
                    {
                        if (Time.time < attackDamageDeadline)
                            return;
                        throw new System.InvalidOperationException(CurrentPath() + " attack did not damage the player within 8 seconds; state=" + monsterAI.CurrentDebugStateName);
                    }
                    MovePlayerAndWait(new Vector3(0f, 0f, 5f), VerifyStep.CheckChaseAfterAttack, 4, 0.8f);
                    break;
                case VerifyStep.CheckChaseAfterAttack:
                    CheckState("Chase");
                    monsterObject.transform.position = new Vector3(0f, 0f, 30f);
                    Physics.SyncTransforms();
                    MovePlayerAndWait(new Vector3(0f, 0f, 35f), VerifyStep.CheckChaseWithoutHomeLeash, 4, 0.5f);
                    break;
                case VerifyStep.CheckChaseWithoutHomeLeash:
                    CheckState("Chase");
                    MovePlayerAndWait(
                        new Vector3(0f, 0f, 100f),
                        VerifyStep.CheckReturn,
                        8,
                        monsterAI.AggroReleaseDelay + 0.75f);
                    break;
                case VerifyStep.CheckReturn:
                    CheckState("Return");
                    RequireMovingLocomotion(monsterMovement, CurrentPath() + " Return");
                    playerObject.transform.position = monsterObject.transform.position + Vector3.forward * 2f;
                    Physics.SyncTransforms();
                    WaitFor(VerifyStep.CheckReturnReaggro, 4, 0.15f);
                    break;
                case VerifyStep.CheckReturnReaggro:
                    CheckState("Chase");
                    MovePlayerAndWait(
                        new Vector3(0f, 0f, 100f),
                        VerifyStep.CheckReturnAgain,
                        8,
                        monsterAI.AggroReleaseDelay + 0.75f);
                    break;
                case VerifyStep.CheckReturnAgain:
                    CheckState("Return");
                    monsterObject.transform.position = monsterAI.HomePosition;
                    Physics.SyncTransforms();
                    WaitFor(VerifyStep.CheckHomeRoam, 4, 0.1f);
                    break;
                case VerifyStep.CheckHomeRoam:
                    CheckState("Roam");
                    DamageInfo lethalDamage = new DamageInfo(
                        monsterHealth.MaxHp + 9999f,
                        monsterObject.transform.position,
                        playerObject,
                        Vector3.forward);
                    monsterHealth.TakeDamage(lethalDamage);
                    WaitFor(VerifyStep.CheckDead, 4, 0.1f);
                    break;
                case VerifyStep.CheckDead:
                    CheckState("Dead");
                    if (monsterMovement.enabled)
                        throw new System.InvalidOperationException(CurrentPath() + " movement remained enabled after death");
                    passedPrefabs.Add(CurrentPath());
                    Debug.Log("[EnemyStatePatternPlayModeVerifier] Passed " + CurrentPath());
                    prefabIndex++;
                    step = VerifyStep.SetupMonster;
                    waitUntilFrame = Time.frameCount + 1;
                    waitUntilTime = Time.time;
                    break;
                case VerifyStep.CheckHitStunEnd:
                    if (monsterMovementReaction.IsStunned)
                        throw new System.InvalidOperationException("Hit stun did not end");

                    reactionStartPosition = monsterObject.transform.position;
                    monsterMovementReaction.ApplyKnockback(Vector3.back, 1f);
                    monsterMovementReaction.ExtendKnockbackReaction(0.4f);
                    WaitFor(VerifyStep.CheckKnockbackPunch, 1, 0.16f);
                    break;
                case VerifyStep.CheckKnockbackPunch:
                    float punchDistance = Vector3.Distance(reactionStartPosition, monsterObject.transform.position);
                    if (punchDistance < 0.08f)
                        throw new System.InvalidOperationException("Controlled knockback moved too little: " + punchDistance);
                    if (!monsterMovementReaction.IsKnockbackActive)
                        throw new System.InvalidOperationException("Knockback reaction ended before the stun duration");

                    reactionPunchPosition = monsterObject.transform.position;
                    WaitFor(VerifyStep.CheckKnockbackEnd, 1, 0.3f);
                    break;
                case VerifyStep.CheckKnockbackEnd:
                    if (monsterMovementReaction.IsKnockbackActive)
                        throw new System.InvalidOperationException("Knockback reaction did not end");
                    if (reactionBody != null && reactionBody.linearVelocity.sqrMagnitude > 0.0001f)
                        throw new System.InvalidOperationException("Rigidbody retained velocity after controlled knockback");

                    float slideDistance = Vector3.Distance(reactionPunchPosition, monsterObject.transform.position);
                    if (slideDistance > 0.03f)
                        throw new System.InvalidOperationException("Monster kept sliding after knockback punch: " + slideDistance);

                    Debug.Log("[EnemyStatePatternPlayModeVerifier] Passed hit-stun attack cancel and controlled knockback");
                    SetupRepeatedHitAnimationVerification();
                    break;
                case VerifyStep.CheckHitAnimationStarted:
                    if (reactionAnimator == null || Mathf.Abs(reactionAnimator.speed - 2.5f) > 0.01f)
                        throw new System.InvalidOperationException("Hit animation speed was not boosted to 2.5x");
                    if (!TryGetHitAnimationNormalizedTime(reactionAnimator, out hitNormalizedBeforeRestart))
                        throw new System.InvalidOperationException("Get_hit animation did not start");

                    reactionAnimationBridge.PlayHit();
                    WaitFor(VerifyStep.CheckHitAnimationRestarted, 1, 0.02f);
                    break;
                case VerifyStep.CheckHitAnimationRestarted:
                    if (!TryGetHitAnimationNormalizedTime(reactionAnimator, out float restartedNormalizedTime))
                        throw new System.InvalidOperationException("Get_hit animation was not active after repeated hit");
                    if (restartedNormalizedTime >= hitNormalizedBeforeRestart)
                        throw new System.InvalidOperationException(
                            "Repeated hit did not restart Get_hit: before=" + hitNormalizedBeforeRestart + ", after=" + restartedNormalizedTime);

                    Debug.Log("[EnemyStatePatternPlayModeVerifier] Passed 2.5x repeated hit animation restart");
                    SetupGroupAggroVerification();
                    break;
                case VerifyStep.CheckGroupRoam:
                    CheckGroupState(groupAiA, "Roam", "group A");
                    CheckGroupState(groupAiB, "Roam", "group B");
                    playerObject.transform.position = new Vector3(0f, 0f, 12f);
                    Physics.SyncTransforms();
                    groupHealthA.TakeDamage(new DamageInfo(1f, groupMonsterA.transform.position, playerObject, Vector3.forward));
                    WaitFor(VerifyStep.CheckGroupAggro, 4, 0.45f);
                    break;
                case VerifyStep.CheckGroupAggro:
                    CheckGroupStateAny(groupAiA, "group A", "Chase", "CombatWait", "Attack", "Reposition", "Defend");
                    CheckGroupStateAny(groupAiB, "group B", "Chase", "CombatWait", "Attack", "Reposition", "Defend");
                    groupMonsterA.transform.position = playerObject.transform.position + Vector3.back * 40f;
                    groupMonsterB.transform.position = playerObject.transform.position + Vector3.back * 15f;
                    Physics.SyncTransforms();
                    if (!EnemySquadPursuitRuntimeService.HasNearbyEngagedGroupMember(
                            groupAiA,
                            groupAiA.CombatLoseTargetRange))
                    {
                        throw new System.InvalidOperationException(
                            "Group aggro retention signal was not resolved before distance release");
                    }
                    WaitFor(
                        VerifyStep.CheckGroupAggroRetention,
                        8,
                        groupAiA.AggroReleaseDelay + 0.75f);
                    break;
                case VerifyStep.CheckGroupAggroRetention:
                    CheckGroupStateAny(groupAiA, "distant group A", "Chase", "CombatWait", "Attack", "Reposition", "Defend");
                    CheckGroupStateAny(groupAiB, "engaged group B", "Chase", "CombatWait", "Attack", "Reposition", "Defend");
                    groupHealthA.TakeDamage(new DamageInfo(groupHealthA.MaxHp + 9999f, groupMonsterA.transform.position, playerObject, Vector3.forward));
                    groupHealthB.TakeDamage(new DamageInfo(groupHealthB.MaxHp + 9999f, groupMonsterB.transform.position, playerObject, Vector3.forward));
                    WaitFor(VerifyStep.CheckAreaCleared, 4, 0.1f);
                    break;
                case VerifyStep.CheckAreaCleared:
                    if (groupHealthA == null
                        || groupHealthB == null
                        || !groupHealthA.IsDead
                        || !groupHealthB.IsDead)
                    {
                        throw new System.InvalidOperationException(
                            "Enemy encounter group did not finish after "
                            + "all registered enemies died");
                    }
                    Debug.Log(
                        "[EnemyStatePatternPlayModeVerifier] Passed Return "
                        + "reaggro and encounter group retention");
                    VerifyHideoutDebugSpawnerToggle();
                    CleanupBeforeStressVerification();
                    WaitFor(VerifyStep.SetupStress40, 2, 0.05f);
                    break;
                case VerifyStep.SetupStress40:
                    SetupAiStressCase(40);
                    WaitFor(VerifyStep.CheckStress40, 4, 1f);
                    break;
                case VerifyStep.CheckStress40:
                    CheckAiStressCase(40, 0f);
                    CleanupStressMonsters();
                    WaitFor(VerifyStep.SetupStress100, 2, 0.05f);
                    break;
                case VerifyStep.SetupStress100:
                    SetupAiStressCase(100);
                    WaitFor(VerifyStep.CheckStress100, 4, 1f);
                    break;
                case VerifyStep.CheckStress100:
                    CheckAiStressCase(100, 0.1f);
                    CleanupStressMonsters();
                    WaitFor(VerifyStep.SetupStress200, 2, 0.05f);
                    break;
                case VerifyStep.SetupStress200:
                    SetupAiStressCase(200);
                    WaitFor(VerifyStep.CheckStress200, 4, 1f);
                    break;
                case VerifyStep.CheckStress200:
                    CheckAiStressCase(200, 0.25f);
                    CleanupStressMonsters();
                    CompleteSuccessfully();
                    break;
            }
        }
        catch (System.Exception exception)
        {
            Fail(exception);
        }
    }

    private static void SetupMonster()
    {
        IReadOnlyList<string> paths = EnemyStatePatternPrefabFormalizer.TargetPrefabPaths;
        if (prefabIndex >= paths.Count)
        {
            VerifyShieldDefense();
            VerifyLocomotionModes();
            VerifyKnockbackReductionProfiles();
            VerifyCrowdWeightProfiles();
            VerifyEnemyCollisionPolicy();
            VerifyAiTickPolicy();
            VerifyRunUsesInPlaceClip();
            VerifyBehaviorProfileMigration();
            VerifyTacticalDecisions();
            VerifyCombatCoordination();
            VerifyGroupAttackRhythm();
            VerifyWalkableAndApproachContracts();
            VerifyNarrowCorridorCrowdContract();
            VerifyReservationFreeApproachSteeringCalculator();
            VerifySquadPursuitPlannerContract();
            VerifyClusterFanOutSteeringCalculator();
            VerifyClusterFanOutIntegrationContract();
            VerifyChaseBypassSteeringCalculator();
            VerifyChaseBypassIntegrationContract();
            VerifyFlowFieldCalculatorContract();
            VerifySharedFlowFieldServiceContract();
            VerifyFlowFieldChaseIntegrationContract();
            VerifyDensityApproachCoreRosterContract();
            VerifyEncirclementDestinationSpread();
            VerifyLegacySurroundRemovalContract();
            VerifyInRangeChaseAttackPriority();
            VerifyEnemyStateDebugLabel();
            SetupHitReactionVerification();
            return;
        }

        if (monsterObject != null)
            Object.DestroyImmediate(monsterObject);

        playerHealth.ResetHealth();
        playerObject.transform.position = new Vector3(0f, 0f, 20f);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CurrentPath());
        if (prefab == null)
            throw new System.InvalidOperationException("Prefab missing: " + CurrentPath());

        monsterObject = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
        monsterObject.name = prefab.name + "_PlayModeVerification";

        PrepareBody(monsterObject);

        monsterAI = RequireComponent<EnemyAIController>(monsterObject);
        bool expectedDensityApproach = EnemyStatePatternPrefabFormalizer.UsesDensityApproachByDefault(prefab.name);
        if (monsterAI.UsesDensityApproachSteering != expectedDensityApproach)
        {
            throw new System.InvalidOperationException(
                CurrentPath() + " density approach mismatch expected=" + expectedDensityApproach
                + " actual=" + monsterAI.UsesDensityApproachSteering);
        }
        ApplyVerificationBehavior(monsterAI);
        monsterMovement = RequireComponent<EnemyMovement>(monsterObject);
        monsterMovementReaction = RequireComponent<EnemyMovementReaction>(monsterObject);
        monsterAttack = RequireComponent<EnemyMeleeAttackController>(monsterObject);
        monsterHealth = RequireComponent<CombatHealth>(monsterObject);
        monsterAI.SetHomePosition(Vector3.zero);
        monsterAI.SetTarget(playerObject.transform);
        Physics.SyncTransforms();
        WaitFor(VerifyStep.CheckRoam, 4, 0.1f);
    }

    private static void VerifyShieldDefense()
    {
        const string guardPath = "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Guard.prefab";
        GameObject guardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(guardPath);
        if (guardPrefab == null)
            throw new System.InvalidOperationException("Guard defense verification prefab is missing");

        GameObject guard = Object.Instantiate(guardPrefab, Vector3.zero, Quaternion.identity);
        try
        {
            PrepareBody(guard);
            EnemyDefenseController defense = RequireComponent<EnemyDefenseController>(guard);
            EnemyMovementReaction reaction = RequireComponent<EnemyMovementReaction>(guard);
            CombatHealth health = RequireComponent<CombatHealth>(guard);
            float hpBefore = health.CurrentHp;

            playerObject.transform.position = guard.transform.position + guard.transform.forward * 1.5f;
            defense.SetDefending(true);
            health.TakeDamage(new DamageInfo(100f, guard.transform.position, playerObject, guard.transform.forward, 5f));

            float appliedDamage = hpBefore - health.CurrentHp;
            if (Mathf.Abs(appliedDamage - 35f) > 0.05f)
                throw new System.InvalidOperationException("Guard shield expected damage=35 actual=" + appliedDamage);
            if (reaction.IsStunned)
                throw new System.InvalidOperationException("Guard shield block incorrectly applied hit-stun or knockback");

            Debug.Log("[EnemyStatePatternPlayModeVerifier] Passed Guard shield damage and reaction block");
        }
        finally
        {
            Object.DestroyImmediate(guard);
        }
    }

    private static void VerifyLocomotionModes()
    {
        const string scoutPath = "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Scout.prefab";
        GameObject scoutPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(scoutPath);
        if (scoutPrefab == null)
            throw new System.InvalidOperationException("Scout locomotion verification prefab is missing");

        GameObject scout = Object.Instantiate(scoutPrefab, Vector3.zero, Quaternion.identity);
        PrepareBody(scout);
        try
        {
            EnemyAIController ai = RequireComponent<EnemyAIController>(scout);
            EnemyMovement movement = RequireComponent<EnemyMovement>(scout);
            EnemyMotor motor = RequireComponent<EnemyMotor>(scout);
            EnemyMovementReaction reaction = RequireComponent<EnemyMovementReaction>(scout);
            EnemyAnimationBridge animationBridge = RequireComponent<EnemyAnimationBridge>(scout);
            ai.enabled = false;
            movement.CancelActionLock();
            reaction.ResetReaction();

            System.Reflection.MethodInfo fixedUpdate = typeof(EnemyMovement).GetMethod(
                "FixedUpdate",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (fixedUpdate == null)
                throw new System.InvalidOperationException("EnemyMovement.FixedUpdate reflection hook missing");

            movement.StopMovement();
            if (!motor.IsPositionHeld)
                throw new System.InvalidOperationException("Stationary enemy must hold XZ position against crowd pushing");

            movement.SetDestination(Vector3.forward * 10f, 0.1f, EnemyLocomotionMode.Walk);
            float walkSpeed = movement.ActiveMoveSpeed;
            Vector3 movementStart = scout.transform.position;
            fixedUpdate.Invoke(movement, null);
            if (motor.IsPositionHeld)
                throw new System.InvalidOperationException("Moving enemy did not release its XZ position hold");
            Vector3 movementDelta = scout.transform.position - movementStart;
            movementDelta.y = 0f;
            if (movementDelta.sqrMagnitude <= 0.000001f)
                throw new System.InvalidOperationException("Walk command produced no actual movement");
            RequireMovingLocomotion(movement, "Scout actual Walk movement");
            movement.SetDestination(Vector3.forward * 10f, 0.1f, EnemyLocomotionMode.Run);
            float runSpeed = movement.ActiveMoveSpeed;
            movement.SetFacingDestination(Vector3.right * 3f, 0.1f, Vector3.forward, EnemyLocomotionMode.Dodge);
            float dodgeSpeed = movement.ActiveMoveSpeed;

            if (runSpeed < walkSpeed * EnemyMovementProfile.MinimumRunSpeedMultiplier)
                throw new System.InvalidOperationException(
                    "Run speed is below the minimum multiplier. walk=" + walkSpeed + " run=" + runSpeed);
            if (dodgeSpeed <= walkSpeed)
                throw new System.InvalidOperationException("Dodge movement must be faster than walk. walk=" + walkSpeed + " dodge=" + dodgeSpeed);
            if (movement.MovementAnimationAmount < 0.9f)
                throw new System.InvalidOperationException("Moving Dodge must retain Walk locomotion fallback after its trigger ends");

            movement.SetDestination(Vector3.forward * 10f, 0.1f, EnemyLocomotionMode.Run);
            reaction.ApplyHitStun(1f);
            fixedUpdate.Invoke(movement, null);
            if (!movement.HasDestination
                || movement.LocomotionMode != EnemyLocomotionMode.Run
                || movement.MovementAnimationAmount < 1.9f)
            {
                throw new System.InvalidOperationException(
                    "Hit-stun erased the pending Run command. mode=" + movement.LocomotionMode
                    + " amount=" + movement.MovementAnimationAmount);
            }
            reaction.ResetReaction();

            movement.SetFacingDestination(
                Vector3.back * 3f,
                0.1f,
                Vector3.forward,
                EnemyLocomotionMode.Backpedal);
            movement.ApplyActionLock(1f);
            fixedUpdate.Invoke(movement, null);
            if (!movement.HasDestination
                || movement.LocomotionMode != EnemyLocomotionMode.Backpedal
                || movement.MovementAnimationAmount > -0.9f)
            {
                throw new System.InvalidOperationException(
                    "Action lock erased the pending Backpedal command. mode=" + movement.LocomotionMode
                    + " amount=" + movement.MovementAnimationAmount);
            }
            movement.CancelActionLock();

            movement.SetFacingDestination(
                Vector3.right * 3f,
                0.1f,
                Vector3.forward,
                EnemyLocomotionMode.Dodge);
            reaction.ApplyKnockback(Vector3.back, 1f);
            fixedUpdate.Invoke(movement, null);
            if (!movement.HasDestination
                || movement.LocomotionMode != EnemyLocomotionMode.Dodge
                || movement.MovementAnimationAmount < 0.9f)
            {
                throw new System.InvalidOperationException(
                    "Knockback erased the pending Dodge command. mode=" + movement.LocomotionMode
                    + " amount=" + movement.MovementAnimationAmount);
            }
            reaction.ResetReaction();

            movement.SetProfile(null);
            movement.SetDestination(Vector3.forward * 10f, 0.1f, EnemyLocomotionMode.Run);
            if (movement.ActiveMoveSpeed < EnemyMovementProfile.MinimumRunSpeedMultiplier)
                throw new System.InvalidOperationException("Fallback Run speed contract is missing. run=" + movement.ActiveMoveSpeed);

            animationBridge.PlayTaunt();
            if (animationBridge.AllowsMovement(EnemyLocomotionMode.Walk))
                throw new System.InvalidOperationException("Taunt must block locomotion until its Animator state exits");

            if (!animationBridge.PlayDodge()
                || !animationBridge.AllowsMovement(EnemyLocomotionMode.Dodge)
                || animationBridge.AllowsMovement(EnemyLocomotionMode.Walk))
            {
                throw new System.InvalidOperationException("Dodge movement gate must allow only the active Dodge command");
            }

            Debug.Log("[EnemyStatePatternPlayModeVerifier] Passed Walk/Run/Dodge speed contract walk="
                + walkSpeed.ToString("0.00") + " run=" + runSpeed.ToString("0.00") + " dodge=" + dodgeSpeed.ToString("0.00"));
            Debug.Log("[EnemyStatePatternPlayModeVerifier] Passed locomotion command preservation during hit-stun, action lock and knockback");
        }
        finally
        {
            Object.DestroyImmediate(scout);
        }
    }

    private static void VerifyKnockbackReductionProfiles()
    {
        VerifyKnockbackReduction("Murloc_Grunt", 0f);
        VerifyKnockbackReduction("Murloc_Scout", 0f);
        VerifyKnockbackReduction("Murloc_Spearling", 5f);
        VerifyKnockbackReduction("Murloc_Guard", 10f);
        VerifyKnockbackReduction("Murloc_Brute", 15f);
        VerifyKnockbackReduction("Murloc_Warlord", 20f);
        Debug.Log("[EnemyStatePatternPlayModeVerifier] Passed role-based knockback reduction and elite resistance");
    }

    private static void VerifyCrowdWeightProfiles()
    {
        VerifyCrowdWeight("Default", 1f);
        VerifyCrowdWeight("Murloc_Grunt", 1f);
        VerifyCrowdWeight("Murloc_Scout", 0.9f);
        VerifyCrowdWeight("Murloc_Spearling", 1.05f);
        VerifyCrowdWeight("Murloc_Guard", 1.2f);
        VerifyCrowdWeight("Murloc_Brute", 1.4f);
        VerifyCrowdWeight("Murloc_Warlord", 1.55f);
        // Authored 0.64-scale roster preserved from VTP; see monster content master reference.
        VerifyCrowdBodyRadius("Murloc_Scout", 0.33792f);
        VerifyCrowdBodyRadius("Murloc_Grunt", 0.384f);
        VerifyCrowdBodyRadius("Murloc_Spearling", 0.39168f);
        VerifyCrowdBodyRadius("Murloc_Guard", 0.43008f);
        VerifyCrowdBodyRadius("Murloc_Brute", 0.48f);
        VerifyCrowdBodyRadius("Murloc_Warlord", 0.4992f);
        VerifyEliteCrowdWeight("Murloc_Brute", 1.54f);
        VerifyEliteCrowdWeight("Murloc_Warlord", 1.705f);
        VerifySeparationProfile("Murloc_Grunt", 1.5f, 1f);
        VerifySeparationProfile("Murloc_Scout", 1.55f, 1.25f);
        VerifySeparationProfile("Murloc_Spearling", 1.65f, 1.2f);
        VerifySeparationProfile("Murloc_Guard", 1.7f, 1f);
        VerifySeparationProfile("Murloc_Brute", 1.85f, 0.75f);
        VerifySeparationProfile("Murloc_Warlord", 2f, 0.9f);
        Debug.Log("[EnemyStatePatternPlayModeVerifier] Passed tuned crowd weights, body radii, elite multiplier and Separation profiles");
    }

    private static void VerifyCrowdWeight(string id, float expectedWeight)
    {
        string path = "Assets/ProjectOverburst/Resources/Enemies/MovementProfiles/EMP_" + id + ".asset";
        EnemyMovementProfile profile = AssetDatabase.LoadAssetAtPath<EnemyMovementProfile>(path);
        if (profile == null)
            throw new System.InvalidOperationException("Crowd weight profile is missing: " + path);
        if (Mathf.Abs(profile.CrowdWeight - expectedWeight) > 0.01f)
        {
            throw new System.InvalidOperationException(
                id + " crowd weight mismatch expected=" + expectedWeight + " actual=" + profile.CrowdWeight);
        }
    }

    private static void VerifyCrowdBodyRadius(string id, float expectedRadius)
    {
        string path = "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_" + id + ".prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
            throw new System.InvalidOperationException("Crowd radius prefab is missing: " + path);

        float actualRadius = EnemyCrowdAgent.EstimateBodyRadius(prefab);
        if (Mathf.Abs(actualRadius - expectedRadius) > 0.005f)
        {
            throw new System.InvalidOperationException(
                id + " crowd radius mismatch expected=" + expectedRadius + " actual=" + actualRadius);
        }
    }

    private static void VerifyEliteCrowdWeight(string id, float expectedWeight)
    {
        string path = "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_" + id + ".prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        GameObject instance = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
        try
        {
            EnemyCrowdAgent agent = RequireComponent<EnemyCrowdAgent>(instance);
            if (Mathf.Abs(agent.EffectiveCrowdWeight - expectedWeight) > 0.01f)
            {
                throw new System.InvalidOperationException(
                    id + " elite crowd weight mismatch expected=" + expectedWeight + " actual=" + agent.EffectiveCrowdWeight);
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    private static void VerifySeparationProfile(string id, float expectedRadius, float expectedWeight)
    {
        string path = "Assets/ProjectOverburst/Resources/Enemies/BehaviorProfiles/EBP_" + id + ".asset";
        EnemyBehaviorProfile profile = AssetDatabase.LoadAssetAtPath<EnemyBehaviorProfile>(path);
        if (profile == null)
            throw new System.InvalidOperationException("Separation profile is missing: " + path);
        if (Mathf.Abs(profile.SeparationRadius - expectedRadius) > 0.01f
            || Mathf.Abs(profile.SeparationWeight - expectedWeight) > 0.01f)
        {
            throw new System.InvalidOperationException(
                id + " Separation mismatch radius=" + profile.SeparationRadius + " weight=" + profile.SeparationWeight);
        }
    }

    private static void VerifyEnemyCollisionPolicy()
    {
        int enemyLayer = LayerMask.NameToLayer("Enemy");
        if (enemyLayer < 0 || !EnemyCrowdService.IsEnemySelfCollisionDisabled)
            throw new System.InvalidOperationException("Enemy self-collision was not disabled by EnemyCrowdService");

        string[] preservedLayers = { "Default", "Ground", "Player" };
        for (int i = 0; i < preservedLayers.Length; i++)
        {
            int layer = LayerMask.NameToLayer(preservedLayers[i]);
            if (layer >= 0 && Physics.GetIgnoreLayerCollision(enemyLayer, layer))
            {
                throw new System.InvalidOperationException(
                    "Enemy collision must remain enabled for layer=" + preservedLayers[i]);
            }
        }

        Debug.Log("[EnemyStatePatternPlayModeVerifier] Passed Enemy self-collision off and Ground/Wall/Player collision preservation");
    }

    private static void VerifyAiTickPolicy()
    {
        float farSqr = 80f * 80f;
        if (EnemyAiTickScheduler.ResolveInterval(40, farSqr, false, false) != 0f)
            throw new System.InvalidOperationException("40-enemy AI policy must remain full rate");
        if (Mathf.Abs(EnemyAiTickScheduler.ResolveInterval(100, farSqr, false, false) - 0.1f) > 0.001f)
            throw new System.InvalidOperationException("100-enemy hidden AI policy must use 0.1s ticks");
        if (Mathf.Abs(EnemyAiTickScheduler.ResolveInterval(200, farSqr, false, false) - 0.25f) > 0.001f)
            throw new System.InvalidOperationException("200-enemy hidden AI policy must use 0.25s ticks");
        if (Mathf.Abs(EnemyAiTickScheduler.ResolveInterval(201, farSqr, false, false) - 0.5f) > 0.001f)
            throw new System.InvalidOperationException("200+ very-far hidden AI policy must use 0.5s ticks");
        if (EnemyAiTickScheduler.ResolveInterval(200, 10f * 10f, false, false) != 0f)
            throw new System.InvalidOperationException("near combat AI must remain full rate");
        if (EnemyAiTickScheduler.ResolveInterval(200, farSqr, false, true) != 0f)
            throw new System.InvalidOperationException("urgent AI event must bypass tick LOD");

        HashSet<int> staggerSlots = new HashSet<int>();
        for (int i = 0; i < 200; i++)
        {
            float delay = EnemyAiTickScheduler.ResolveStaggerDelay(0.25f, i + 1);
            staggerSlots.Add(Mathf.FloorToInt(delay / 0.01f));
        }
        if (staggerSlots.Count < 12)
            throw new System.InvalidOperationException("AI tick staggering produced too few time slots");

        Debug.Log("[EnemyStatePatternPlayModeVerifier] Passed 40/100/200 AI tick LOD policy and stable staggering");
    }

    private static void VerifyKnockbackReduction(string id, float expectedPercent)
    {
        string path = "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_" + id + ".prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
            throw new System.InvalidOperationException("Knockback verification prefab is missing: " + path);

        GameObject instance = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
        try
        {
            EnemyMovementReaction reaction = RequireComponent<EnemyMovementReaction>(instance);
            reaction.ResolveReferences();
            if (Mathf.Abs(reaction.KnockbackReductionPercent - expectedPercent) > 0.01f)
            {
                throw new System.InvalidOperationException(
                    id + " knockback reduction mismatch. expected=" + expectedPercent
                    + " actual=" + reaction.KnockbackReductionPercent);
            }

            float expectedDistance = 1f * (1f - expectedPercent * 0.01f);
            float actualDistance = reaction.ResolveKnockbackDistance(10f);
            if (Mathf.Abs(actualDistance - expectedDistance) > 0.001f)
            {
                throw new System.InvalidOperationException(
                    id + " knockback distance mismatch. expected=" + expectedDistance
                    + " actual=" + actualDistance);
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    private static void VerifyWalkableAndApproachContracts()
    {
        bool[,] cells = new bool[3, 3];
        for (int x = 0; x < 3; x++)
        {
            for (int z = 0; z < 3; z++)
                cells[x, z] = true;
        }

        RunWalkableArea walkableArea =
            new RunWalkableArea(cells, 3, 3, -1.5f, -1.5f, 1f);
        RunWalkableContext.SetCurrent(walkableArea);
        GameObject first = null;
        GameObject second = null;
        Vector3 previousPlayerPosition = playerObject.transform.position;
        try
        {
            playerObject.transform.position = Vector3.forward * 0.5f;
            first = InstantiateTacticMonster("Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Grunt.prefab", Vector3.zero);
            second = InstantiateTacticMonster("Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Grunt.prefab", Vector3.zero);
            EnemyMovement movement = RequireComponent<EnemyMovement>(first);
            if (!movement.TryResolveWalkableDestination(Vector3.right * 20f, out Vector3 resolved)
                || !walkableArea.IsWalkable(resolved))
                throw new System.InvalidOperationException("Enemy destination was not resolved inside the walkable area");

            EnemyAIController firstAi = RequireComponent<EnemyAIController>(first);
            EnemyAIController secondAi = RequireComponent<EnemyAIController>(second);
            EnemyMovement secondMovement = RequireComponent<EnemyMovement>(second);
            EnemyCrowdAgent firstAgent = RequireComponent<EnemyCrowdAgent>(first);
            EnemyCrowdAgent secondAgent = RequireComponent<EnemyCrowdAgent>(second);
            float testPenetration = 0.04f;
            Vector3 secondStationaryPosition = Vector3.right
                * (firstAgent.BodyRadius + secondAgent.BodyRadius - testPenetration);
            secondMovement.StopMovement(); // 정지 개체 보정 조건 고정
            second.transform.position = secondStationaryPosition;
            Physics.SyncTransforms();

            if (!movement.TryResolveCrowdPosition(Vector3.zero, out Vector3 firstHardPosition)
                || !secondMovement.TryResolveCrowdPosition(secondStationaryPosition, out Vector3 secondHardPosition)
                || (firstHardPosition - secondHardPosition).sqrMagnitude <= 0.01f)
            {
                throw new System.InvalidOperationException("Hard Overlap did not split stationary overlap candidates");
            }
            float firstCorrection = firstHardPosition.magnitude;
            if (firstCorrection < testPenetration - 0.005f)
                throw new System.InvalidOperationException("Moving enemy did not take full correction around a stationary enemy");
            if (firstCorrection > 0.081f || (secondHardPosition - secondStationaryPosition).magnitude > 0.081f)
                throw new System.InvalidOperationException("Hard Overlap exceeded the 0.08m FixedUpdate correction limit");

            Vector3 deepOverlapPosition = Vector3.right
                * (firstAgent.BodyRadius + secondAgent.BodyRadius - 0.2f);
            second.transform.position = deepOverlapPosition;
            secondAgent.enabled = false;
            secondAgent.enabled = true;
            Physics.SyncTransforms();
            if (!movement.TryResolveCrowdPosition(Vector3.zero, out Vector3 cappedHardPosition)
                || cappedHardPosition.magnitude < 0.075f
                || cappedHardPosition.magnitude > 0.081f)
            {
                throw new System.InvalidOperationException(
                    "Hard Overlap 0.08m cap mismatch actual=" + cappedHardPosition.magnitude);
            }

            if (!EnemyCrowdService.TryFindSpawnPosition(Vector3.zero, 2f, 0.5f, 12, out Vector3 crowdSpawn)
                || !walkableArea.IsWalkable(crowdSpawn)
                || crowdSpawn.sqrMagnitude <= 0.01f)
            {
                throw new System.InvalidOperationException("Crowd-aware spawn position was not separated inside walkable area");
            }

            first.transform.position = Vector3.zero;
            second.transform.position = Vector3.zero;
            secondAgent.enabled = false;
            secondAgent.enabled = true; // 다음 군집 검증 전에 FixedUpdate 스냅샷 갱신
            Physics.SyncTransforms();

            firstAi.RequestAggro(playerObject.transform);
            secondAi.RequestAggro(playerObject.transform);
            System.Reflection.MethodInfo resolveApproach = typeof(EnemyAIController).GetMethod(
                "ResolveChaseDestination",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null,
                System.Type.EmptyTypes,
                null);
            if (resolveApproach == null)
                throw new System.InvalidOperationException("EnemyAIController.ResolveChaseDestination reflection hook missing");

            System.Reflection.BindingFlags privateInstance =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            System.Reflection.FieldInfo approachDirectionField = typeof(EnemyAIController).GetField("approachDirection", privateInstance);
            System.Reflection.FieldInfo smoothedSeparationField = typeof(EnemyAIController).GetField("smoothedSeparationDirection", privateInstance);
            System.Reflection.FieldInfo approachRefreshField = typeof(EnemyAIController).GetField("nextApproachDirectionRefreshTime", privateInstance);
            if (approachDirectionField == null
                || smoothedSeparationField == null
                || approachRefreshField == null)
                throw new System.InvalidOperationException("EnemyAIController approach verification fields missing");

            Vector3 commonApproachDirection = Vector3.back;
            approachDirectionField.SetValue(firstAi, commonApproachDirection);
            approachDirectionField.SetValue(secondAi, commonApproachDirection);
            smoothedSeparationField.SetValue(firstAi, Vector3.zero);
            smoothedSeparationField.SetValue(secondAi, Vector3.zero);
            approachRefreshField.SetValue(firstAi, Time.time + 10f);
            approachRefreshField.SetValue(secondAi, Time.time + 10f);
            Vector3 firstDestination = (Vector3)resolveApproach.Invoke(firstAi, null);
            Vector3 secondDestination = (Vector3)resolveApproach.Invoke(secondAi, null);
            if ((firstDestination - secondDestination).sqrMagnitude <= 0.0001f)
                throw new System.InvalidOperationException("Density steering did not split overlapping approach destinations");

            System.Reflection.MethodInfo resolveCombatSeparation = typeof(EnemyAIController).GetMethod(
                "TryResolveCombatSeparationDestination",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (resolveCombatSeparation == null)
                throw new System.InvalidOperationException("EnemyAIController combat Separation hook missing");

            object[] firstArgs = { Vector3.zero };
            object[] secondArgs = { Vector3.zero };
            bool firstSeparated = (bool)resolveCombatSeparation.Invoke(firstAi, firstArgs);
            bool secondSeparated = (bool)resolveCombatSeparation.Invoke(secondAi, secondArgs);
            Vector3 firstCombatDestination = (Vector3)firstArgs[0];
            Vector3 secondCombatDestination = (Vector3)secondArgs[0];
            if (!firstSeparated
                || !secondSeparated
                || (firstCombatDestination - secondCombatDestination).sqrMagnitude <= 0.01f)
            {
                throw new System.InvalidOperationException("CombatWait Separation did not release overlapping enemies");
            }

            Debug.Log("[EnemyStatePatternPlayModeVerifier] Passed Chase density steering and CombatWait Separation destinations");
        }
        finally
        {
            RunWalkableContext.Clear();
            playerObject.transform.position = previousPlayerPosition;
            if (first != null)
                Object.DestroyImmediate(first);
            if (second != null)
                Object.DestroyImmediate(second);
        }
    }

    private static void VerifyNarrowCorridorCrowdContract()
    {
        bool[,] cells = new bool[3, 5];
        for (int z = 0; z < 5; z++)
            cells[1, z] = true;

        RunWalkableArea corridor =
            new RunWalkableArea(cells, 3, 5, -1.5f, -2.5f, 1f);
        RunWalkableContext.SetCurrent(corridor);
        GameObject first = null;
        GameObject second = null;
        try
        {
            Vector3 firstPosition = new Vector3(0.45f, 0f, 0f);
            Vector3 secondPosition = new Vector3(-0.7f, 0f, 0f);
            first = InstantiateTacticMonster(
                "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Grunt.prefab",
                firstPosition);
            second = InstantiateTacticMonster(
                "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Grunt.prefab",
                secondPosition);

            EnemyMovement firstMovement = RequireComponent<EnemyMovement>(first);
            EnemyMovement secondMovement = RequireComponent<EnemyMovement>(second);
            secondMovement.StopMovement();
            if (!firstMovement.TryResolveCrowdPosition(firstPosition, out Vector3 resolved)
                || !corridor.IsWalkable(resolved)
                || resolved.x >= 0.5f
                || Mathf.Abs(resolved.z) <= 0.001f
                || Vector3.Distance(firstPosition, resolved) > 0.081f)
            {
                throw new System.InvalidOperationException(
                    "Narrow corridor tangent correction failed resolved=" + resolved);
            }

            Debug.Log("[EnemyStatePatternPlayModeVerifier] Passed narrow corridor walkable tangent correction");
        }
        finally
        {
            RunWalkableContext.Clear();
            if (first != null)
                Object.DestroyImmediate(first);
            if (second != null)
                Object.DestroyImmediate(second);
        }
    }

    private static void VerifyEncirclementDestinationSpread()
    {
        const int enemyCount = 12;
        const int directionBins = 12;
        List<GameObject> monsters = new List<GameObject>(enemyCount);
        HashSet<int> occupiedBins = new HashSet<int>();
        Vector3 previousPlayerPosition = playerObject.transform.position;
        try
        {
            playerObject.transform.position = Vector3.zero;
            System.Reflection.MethodInfo resolveApproach = typeof(EnemyAIController).GetMethod(
                "ResolveChaseDestination",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (resolveApproach == null)
                throw new System.InvalidOperationException("EnemyAIController.ResolveChaseDestination reflection hook missing");

            for (int i = 0; i < enemyCount; i++)
            {
                float angle = i * Mathf.PI * 2f / enemyCount;
                Vector3 position = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 2.5f;
                GameObject monster = InstantiateTacticMonster(
                    "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Grunt.prefab",
                    position);
                EnemyAIController ai = RequireComponent<EnemyAIController>(monster);
                ai.SetTarget(playerObject.transform);
                monsters.Add(monster);
            }

            for (int i = 0; i < monsters.Count; i++)
            {
                EnemyAIController ai = RequireComponent<EnemyAIController>(monsters[i]);
                Vector3 destination = (Vector3)resolveApproach.Invoke(ai, null);
                Vector3 offset = destination - playerObject.transform.position;
                float normalizedAngle = Mathf.Atan2(offset.z, offset.x) + Mathf.PI;
                int bin = Mathf.FloorToInt(normalizedAngle / (Mathf.PI * 2f) * directionBins) % directionBins;
                occupiedBins.Add(bin);
            }

            if (occupiedBins.Count < 8)
                throw new System.InvalidOperationException("Encirclement destinations used too few directions=" + occupiedBins.Count);

            Debug.Log("[EnemyStatePatternPlayModeVerifier] Passed 12-enemy encirclement destination spread bins=" + occupiedBins.Count);
        }
        finally
        {
            playerObject.transform.position = previousPlayerPosition;
            for (int i = 0; i < monsters.Count; i++)
            {
                if (monsters[i] != null)
                    Object.DestroyImmediate(monsters[i]);
            }
        }
    }

    private static void VerifyReservationFreeApproachSteeringCalculator()
    {
        List<EnemyApproachNeighbor> neighbors = new List<EnemyApproachNeighbor>(4);
        EnemyApproachSteeringResult farResult = EnemyApproachSteering.Resolve(
            new EnemyApproachSteeringInput(
                new Vector3(0f, 0f, 8f),
                Vector3.zero,
                Vector3.back,
                Vector3.zero,
                1.7f,
                0.6f,
                101),
            neighbors);
        if (farResult.LocalBlend > 0.0001f || Vector3.Dot(farResult.Direction, Vector3.back) < 0.999f)
        {
            throw new System.InvalidOperationException(
                "Far approach did not preserve navigation direction blend=" + farResult.LocalBlend
                + " direction=" + farResult.Direction);
        }

        EnemyApproachSteeringResult closeResult = EnemyApproachSteering.Resolve(
            new EnemyApproachSteeringInput(
                new Vector3(0f, 0f, 1f),
                Vector3.zero,
                Vector3.back,
                Vector3.zero,
                1.7f,
                0.6f,
                102),
            neighbors);
        if (Vector3.Dot(closeResult.RadialCorrection, Vector3.forward) < 0.8f
            || Vector3.Dot(closeResult.Direction, Vector3.forward) < 0.5f)
        {
            throw new System.InvalidOperationException(
                "Too-close approach did not steer outward radial=" + closeResult.RadialCorrection
                + " direction=" + closeResult.Direction);
        }

        neighbors.Add(new EnemyApproachNeighbor(new Vector3(0.45f, 0f, 1.6f), 0.6f));
        neighbors.Add(new EnemyApproachNeighbor(new Vector3(0.7f, 0f, 2f), 0.8f));
        EnemyApproachSteeringInput densityInput = new EnemyApproachSteeringInput(
            new Vector3(0f, 0f, 2.2f),
            Vector3.zero,
            Vector3.back,
            Vector3.zero,
            1.7f,
            0.6f,
            103);
        EnemyApproachSteeringResult leftCrowded = EnemyApproachSteering.Resolve(densityInput, neighbors);
        if (leftCrowded.LeftDensity <= leftCrowded.RightDensity
            || leftCrowded.TurnSign != -1
            || Vector3.Dot(leftCrowded.Direction, Vector3.left) < 0.2f)
        {
            throw new System.InvalidOperationException(
                "Left density did not produce a right tangent left=" + leftCrowded.LeftDensity
                + " right=" + leftCrowded.RightDensity
                + " sign=" + leftCrowded.TurnSign
                + " direction=" + leftCrowded.Direction);
        }

        neighbors.Clear();
        neighbors.Add(new EnemyApproachNeighbor(new Vector3(-0.45f, 0f, 1.6f), 0.6f));
        neighbors.Add(new EnemyApproachNeighbor(new Vector3(-0.7f, 0f, 2f), 0.8f));
        EnemyApproachSteeringResult rightCrowded = EnemyApproachSteering.Resolve(densityInput, neighbors);
        if (rightCrowded.RightDensity <= rightCrowded.LeftDensity
            || rightCrowded.TurnSign != 1
            || Vector3.Dot(rightCrowded.Direction, Vector3.right) < 0.2f)
        {
            throw new System.InvalidOperationException(
                "Right density did not produce a left tangent left=" + rightCrowded.LeftDensity
                + " right=" + rightCrowded.RightDensity
                + " sign=" + rightCrowded.TurnSign
                + " direction=" + rightCrowded.Direction);
        }

        if (EnemyApproachSteering.ResolveTurnSign(1, 4f, 0f, false, 104) != 1)
            throw new System.InvalidOperationException("Turn lock did not preserve the current side");
        if (EnemyApproachSteering.ResolveTurnSign(1, 1f, 0.8f, true, 104) != 1)
            throw new System.InvalidOperationException("Turn hysteresis switched on less than 25 percent improvement");
        if (EnemyApproachSteering.ResolveTurnSign(1, 1f, 0.7f, true, 104) != -1)
            throw new System.InvalidOperationException("Turn hysteresis did not switch on more than 25 percent improvement");
        int stableTurn = EnemyApproachSteering.ResolveStableTurnSign(105);
        if (EnemyApproachSteering.ResolveTurnSign(0, 1f, 1f, true, 105) != stableTurn)
            throw new System.InvalidOperationException("Equal density did not use the stable turn side");

        EnemyApproachSteeringResult noSeparation = EnemyApproachSteering.Resolve(
            new EnemyApproachSteeringInput(
                new Vector3(0f, 0f, 4f),
                Vector3.zero,
                Vector3.back,
                Vector3.right * 0.25f,
                1.7f,
                0.6f,
                106,
                separationWeight: 0f),
            null);
        EnemyApproachSteeringResult weightedSeparation = EnemyApproachSteering.Resolve(
            new EnemyApproachSteeringInput(
                new Vector3(0f, 0f, 4f),
                Vector3.zero,
                Vector3.back,
                Vector3.right * 0.25f,
                1.7f,
                0.6f,
                106,
                separationWeight: 2f),
            null);
        if (Mathf.Abs(noSeparation.Direction.x) > 0.001f
            || Vector3.Dot(weightedSeparation.Direction, Vector3.right) < 0.2f)
        {
            throw new System.InvalidOperationException(
                "Separation pressure or role weight was discarded zero=" + noSeparation.Direction
                + " weighted=" + weightedSeparation.Direction);
        }

        if (Mathf.Abs(EnemyApproachSteering.ResolveNeighborQueryRadius(0.3f) - 2f) > 0.0001f
            || Mathf.Abs(EnemyApproachSteering.ResolveNeighborQueryRadius(0.8f) - 3.2f) > 0.0001f)
        {
            throw new System.InvalidOperationException("Body-radius neighbor query contract changed");
        }

        Vector3 origin = new Vector3(2f, 3f, 4f);
        Vector3 minimumDestination = EnemyApproachSteering.ResolveShortHorizonDestination(
            origin,
            new Vector3(10f, 5f, 0f),
            0f);
        Vector3 maximumDestination = EnemyApproachSteering.ResolveShortHorizonDestination(
            origin,
            Vector3.right,
            10f);
        Vector3 stoppedDestination = EnemyApproachSteering.ResolveShortHorizonDestination(
            origin,
            Vector3.zero,
            10f);
        if (Mathf.Abs(Vector3.Distance(origin, minimumDestination)
                - EnemyApproachSteering.MinimumShortHorizonDistance) > 0.0001f
            || Mathf.Abs(Vector3.Distance(origin, maximumDestination)
                - EnemyApproachSteering.MaximumShortHorizonDistance) > 0.0001f
            || Mathf.Abs(minimumDestination.y - origin.y) > 0.0001f
            || stoppedDestination != origin)
        {
            throw new System.InvalidOperationException(
                "Short-horizon destination contract failed min=" + minimumDestination
                + " max=" + maximumDestination
                + " stopped=" + stoppedDestination);
        }

        Debug.Log(
            "[EnemyStatePatternPlayModeVerifier] Passed reservation-free approach steering density, radius, hysteresis and short horizon");
    }

    private static void VerifyFlowFieldCalculatorContract()
    {
        const int width = 12;
        const int height = 7;
        const int wallX = 5;
        const int gapZ = 6;
        bool[,] cells = CreateFlowFieldCells(width, height, wallX, gapZ);
        RunWalkableArea area =
            new RunWalkableArea(cells, width, height, 0f, 0f, 1f);
        EnemyFlowField field = new EnemyFlowField(area, 10, 3);
        int firstAdvance = field.Advance(3);
        if (firstAdvance != 3 || field.IsComplete)
            throw new System.InvalidOperationException("Flow Field incremental build budget was ignored");

        int guard = width * height + 1;
        while (!field.IsComplete && guard-- > 0)
        {
            if (field.Advance(7) > 7)
                throw new System.InvalidOperationException("Flow Field exceeded one incremental build budget");
        }
        if (!field.IsComplete || guard <= 0 || field.ProcessedCellCount > width * height)
            throw new System.InvalidOperationException("Flow Field did not complete inside the cell count bound");

        Vector3 current = ResolveCellCenter(area, 1, 3);
        if (!field.TryGetDirection(current, out Vector3 firstDirection, out Vector3 firstWaypoint)
            || firstDirection.z <= 0.1f
            || !area.IsWalkable(firstWaypoint))
        {
            throw new System.InvalidOperationException(
                "Flow Field did not route toward the wall gap direction=" + firstDirection
                + " waypoint=" + firstWaypoint);
        }

        bool reachedTarget = false;
        for (int step = 0; step < width * height; step++)
        {
            if (area.TryGetCell(current, out int currentX, out int currentZ)
                && currentX == field.TargetX
                && currentZ == field.TargetZ)
            {
                reachedTarget = true;
                break;
            }

            if (!field.TryGetDirection(current, out _, out Vector3 waypoint)
                || !area.IsWalkable(waypoint))
            {
                throw new System.InvalidOperationException("Flow Field path left the walkable cells step=" + step);
            }
            current = waypoint;
        }
        if (!reachedTarget)
            throw new System.InvalidOperationException("Flow Field path did not reach its target cell");

        bool[,] blockedCells = CreateFlowFieldCells(width, height, wallX, -1);
        RunWalkableArea blockedArea = new RunWalkableArea(
            blockedCells,
            width,
            height,
            0f,
            0f,
            1f);
        EnemyFlowField blockedField = new EnemyFlowField(blockedArea, 10, 3);
        blockedField.Advance(width * height);
        if (blockedField.TryGetDirection(ResolveCellCenter(blockedArea, 1, 3), out _, out _))
            throw new System.InvalidOperationException("Disconnected Flow Field returned a false route");

        if (!field.Rebuild(10, 4))
            throw new System.InvalidOperationException("Flow Field target-cell rebuild failed");
        field.Advance(width * height);
        if (!field.TryGetIntegrationCost(10, 4, out int targetCost) || targetCost != 0)
            throw new System.InvalidOperationException("Flow Field target-cell rebuild retained stale integration data");

        Debug.Log(
            "[EnemyStatePatternPlayModeVerifier] Passed incremental Flow Field detour, corner safety, unreachable and rebuild contracts");
    }

    private static void VerifyChaseBypassSteeringCalculator()
    {
        Vector3 agentPosition = new Vector3(0f, 0f, 8f);
        List<EnemyApproachNeighbor> blockers = new List<EnemyApproachNeighbor>
        {
            new EnemyApproachNeighbor(new Vector3(0f, 0f, 6.5f), 0.6f),
            new EnemyApproachNeighbor(new Vector3(0f, 0f, 5f), 0.6f)
        };
        EnemyChaseBypassInput input = new EnemyChaseBypassInput(
            agentPosition,
            Vector3.zero,
            Vector3.back,
            Vector3.zero,
            0.6f,
            201);
        EnemyChaseBypassResult result = EnemyChaseBypassSteering.Resolve(input, blockers);
        if (!result.IsActive
            || result.ForwardBlockerCount != 2
            || Mathf.Abs(result.Direction.x) < 0.5f
            || Vector3.Dot(result.Direction, Vector3.back) <= 0.1f
            || result.SpeedMultiplier < EnemyChaseBypassSteering.MinimumSpeedMultiplier
            || result.SpeedMultiplier > EnemyChaseBypassSteering.MaximumSpeedMultiplier)
        {
            throw new System.InvalidOperationException(
                "Chase bypass did not produce a fast spiral direction blockers=" + result.ForwardBlockerCount
                + " direction=" + result.Direction
                + " speed=" + result.SpeedMultiplier);
        }

        blockers.Clear();
        blockers.Add(new EnemyApproachNeighbor(new Vector3(0.5f, 0f, 6.5f), 0.6f));
        blockers.Add(new EnemyApproachNeighbor(new Vector3(0.8f, 0f, 5f), 0.6f));
        EnemyChaseBypassResult leftCrowded = EnemyChaseBypassSteering.Resolve(input, blockers);
        if (!leftCrowded.IsActive
            || leftCrowded.LeftDensity <= leftCrowded.RightDensity
            || leftCrowded.TurnSign != -1
            || Vector3.Dot(leftCrowded.Direction, Vector3.left) < 0.5f)
        {
            throw new System.InvalidOperationException(
                "Left queue did not bypass toward the open right side left=" + leftCrowded.LeftDensity
                + " right=" + leftCrowded.RightDensity
                + " sign=" + leftCrowded.TurnSign
                + " direction=" + leftCrowded.Direction);
        }

        blockers.RemoveAt(1);
        EnemyChaseBypassResult oneBlocker = EnemyChaseBypassSteering.Resolve(input, blockers);
        EnemyChaseBypassResult farResult = EnemyChaseBypassSteering.Resolve(
            new EnemyChaseBypassInput(
                new Vector3(0f, 0f, 12f),
                Vector3.zero,
                Vector3.back,
                Vector3.zero,
                0.6f,
                202),
            new List<EnemyApproachNeighbor>
            {
                new EnemyApproachNeighbor(new Vector3(0f, 0f, 10.5f), 0.6f),
                new EnemyApproachNeighbor(new Vector3(0f, 0f, 9f), 0.6f)
            });
        blockers.Add(new EnemyApproachNeighbor(new Vector3(0.8f, 0f, 5f), 0.6f));
        EnemyChaseBypassResult detourResult = EnemyChaseBypassSteering.Resolve(
            new EnemyChaseBypassInput(
                agentPosition,
                Vector3.zero,
                Vector3.right,
                Vector3.zero,
                0.6f,
                203),
            blockers);
        if (oneBlocker.IsActive || farResult.IsActive || detourResult.IsActive)
        {
            throw new System.InvalidOperationException(
                "Chase bypass ignored activation guards one=" + oneBlocker.IsActive
                + " far=" + farResult.IsActive
                + " detour=" + detourResult.IsActive);
        }

        Debug.Log(
            "[EnemyStatePatternPlayModeVerifier] Passed Chase forward blockage, spiral bypass, side choice and speed boost contracts");
    }

    private static void VerifyClusterFanOutSteeringCalculator()
    {
        EnemyClusterFanOutMemberData rearMember = new EnemyClusterFanOutMemberData(
            50,
            new Vector3(0f, 0f, 6f),
            Vector3.back,
            Vector3.left,
            -2f,
            0.4f,
            0.9f,
            1.2f,
            4.9f,
            1f,
            1);
        EnemyClusterFanOutResult rearResult = EnemyClusterFanOutSteering.Resolve(
            new EnemyClusterFanOutInput(
                rearMember,
                Vector3.back,
                Vector3.zero,
                0,
                301));
        if (!rearResult.IsActive
            || Vector3.Dot(rearResult.Direction, Vector3.back) <= 0.1f
            || Vector3.Dot(rearResult.Direction, Vector3.left) <= 0.5f
            || rearResult.SpeedMultiplier < EnemyClusterFanOutSteering.MinimumSpeedMultiplier
            || rearResult.SpeedMultiplier > EnemyClusterFanOutSteering.MaximumSpeedMultiplier)
        {
            throw new System.InvalidOperationException(
                "Cluster rear row did not fan out direction=" + rearResult.Direction
                + " speed=" + rearResult.SpeedMultiplier);
        }

        EnemyClusterFanOutResult lockedSideResult = EnemyClusterFanOutSteering.Resolve(
            new EnemyClusterFanOutInput(
                rearMember,
                Vector3.back,
                Vector3.zero,
                -1,
                301));
        if (!lockedSideResult.IsActive
            || lockedSideResult.SideSign != -1
            || Vector3.Dot(lockedSideResult.Direction, Vector3.right) <= 0.5f)
        {
            throw new System.InvalidOperationException(
                "Cluster fan-out did not preserve its assigned side sign="
                + lockedSideResult.SideSign
                + " direction=" + lockedSideResult.Direction);
        }

        EnemyClusterFanOutMemberData frontMember = new EnemyClusterFanOutMemberData(
            50,
            Vector3.zero,
            Vector3.back,
            Vector3.left,
            2f,
            -0.4f,
            0.1f,
            1.2f,
            4.9f,
            1f,
            -1);
        EnemyClusterFanOutResult frontResult = EnemyClusterFanOutSteering.Resolve(
            new EnemyClusterFanOutInput(
                frontMember,
                Vector3.back,
                Vector3.zero,
                0,
                302));
        EnemyClusterFanOutMemberData smallMember = new EnemyClusterFanOutMemberData(
            7,
            Vector3.zero,
            Vector3.back,
            Vector3.left,
            -1f,
            0.2f,
            0.9f,
            0.5f,
            2f,
            1f,
            1);
        EnemyClusterFanOutResult smallResult = EnemyClusterFanOutSteering.Resolve(
            new EnemyClusterFanOutInput(
                smallMember,
                Vector3.back,
                Vector3.zero,
                0,
                303));
        if (frontResult.IsActive || smallResult.IsActive)
        {
            throw new System.InvalidOperationException(
                "Cluster fan-out ignored front-row or minimum-size guards front="
                + frontResult.IsActive
                + " small=" + smallResult.IsActive);
        }

        Debug.Log(
            "[EnemyStatePatternPlayModeVerifier] Passed cluster front hold, rear fan-out, side lock and speed contracts");
    }

    private static void VerifySquadPursuitPlannerContract()
    {
        VerifyMaximumFirstSquadSizes(4, 8, 0);
        VerifyMaximumFirstSquadSizes(4, 8, 3);
        VerifyMaximumFirstSquadSizes(4, 8, 4, 4);
        VerifyMaximumFirstSquadSizes(4, 8, 7, 7);
        VerifyMaximumFirstSquadSizes(4, 8, 8, 8);
        VerifyMaximumFirstSquadSizes(4, 8, 9, 8);
        VerifyMaximumFirstSquadSizes(4, 8, 11, 8);
        VerifyMaximumFirstSquadSizes(4, 8, 12, 8, 4);
        VerifyMaximumFirstSquadSizes(4, 8, 15, 8, 7);
        VerifyMaximumFirstSquadSizes(4, 8, 16, 8, 8);
        VerifyMaximumFirstSquadSizes(4, 8, 20, 8, 8, 4);
        VerifyMaximumFirstSquadSizes(4, 8, 41, 8, 8, 8, 8, 8);

        VerifyMaximumFirstSquadSizes(8, 12, 0);
        VerifyMaximumFirstSquadSizes(8, 12, 7);
        VerifyMaximumFirstSquadSizes(8, 12, 8, 8);
        VerifyMaximumFirstSquadSizes(8, 12, 11, 11);
        VerifyMaximumFirstSquadSizes(8, 12, 12, 12);
        VerifyMaximumFirstSquadSizes(8, 12, 13, 12);
        VerifyMaximumFirstSquadSizes(8, 12, 19, 12);
        VerifyMaximumFirstSquadSizes(8, 12, 20, 12, 8);
        VerifyMaximumFirstSquadSizes(8, 12, 23, 12, 11);
        VerifyMaximumFirstSquadSizes(8, 12, 24, 12, 12);
        VerifyMaximumFirstSquadSizes(8, 12, 25, 12, 12);
        VerifyMaximumFirstSquadSizes(8, 12, 41, 12, 12, 12);

        VerifyMaximumFirstSquadRange(4, 8);
        VerifyMaximumFirstSquadRange(8, 12);

        Vector3 player = Vector3.zero;
        Vector3 directOutward = Vector3.right;
        Vector3 changedOutward = EnemySquadPursuitPlanner.ResolveDirectOutward(
            player,
            Vector3.forward * 12f);
        if (Vector3.Dot(changedOutward, Vector3.forward) < 0.99f
            || Mathf.Abs(Vector3.Dot(changedOutward, directOutward)) > 0.01f)
        {
            throw new System.InvalidOperationException(
                "Squad pursuit direct axis did not follow the current nearest squad center");
        }
        List<EnemySquadPursuitSlot> slots = new List<EnemySquadPursuitSlot>(8);
        EnemySquadPursuitPlanner.BuildSlots(
            player,
            directOutward,
            EnemySquadPursuitPlanner.DefaultSlotRadius,
            slots);
        EnemySquadPursuitSlot directSlot = FindSlotByKind(slots, EnemySquadPursuitSlotKind.Direct);
        EnemySquadPursuitSlot rightSlot = FindSlotByKind(slots, EnemySquadPursuitSlotKind.RightBypass);
        EnemySquadPursuitSlot rearSlot = FindSlotByKind(slots, EnemySquadPursuitSlotKind.Rear);
        if (slots.Count != 8
            || Vector3.Dot(directSlot.OutwardDirection, directOutward) < 0.99f
            || Mathf.Abs(Vector3.Dot(rightSlot.OutwardDirection, directOutward)) > 0.01f
            || Vector3.Dot(rearSlot.OutwardDirection, directOutward) > -0.99f)
        {
            throw new System.InvalidOperationException(
                "Squad pursuit slots did not preserve direct, side and rear axes");
        }
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].Index != i
                || slots[i].PointIndex != i
                || Mathf.Abs(slots[i].Radius - EnemySquadPursuitPlanner.DefaultSlotRadius) > 0.0001f)
            {
                throw new System.InvalidOperationException(
                    "Squad pursuit physical point order or uniform radius changed index="
                    + i + " radius=" + slots[i].Radius);
            }
        }

        List<EnemySquadPursuitSlot> changedSlots = new List<EnemySquadPursuitSlot>(8);
        EnemySquadPursuitPlanner.BuildSlots(
            player,
            changedOutward,
            EnemySquadPursuitPlanner.DefaultSlotRadius,
            changedSlots);
        EnemySquadPursuitSlot changedDirectSlot = FindSlotByKind(
            changedSlots,
            EnemySquadPursuitSlotKind.Direct);
        if (changedSlots.Count != 8
            || changedDirectSlot.PointIndex == directSlot.PointIndex
            || Vector3.Dot(changedDirectSlot.OutwardDirection, changedOutward) < 0.99f)
        {
            throw new System.InvalidOperationException(
                "Squad pursuit direct role did not move to the nearest fixed point");
        }
        for (int pointIndex = 0; pointIndex < 8; pointIndex++)
        {
            EnemySquadPursuitSlot before = FindSlotAtPoint(slots, pointIndex);
            EnemySquadPursuitSlot after = FindSlotAtPoint(changedSlots, pointIndex);
            if ((before.Position - after.Position).sqrMagnitude > 0.0001f)
            {
                throw new System.InvalidOperationException(
                    "Squad pursuit fixed point moved while its role changed point=P" + pointIndex);
            }
        }
        for (int directPointIndex = 0; directPointIndex < 8; directPointIndex++)
        {
            Vector3 axis = Quaternion.AngleAxis(directPointIndex * 45f, Vector3.up) * Vector3.right;
            EnemySquadPursuitPlanner.BuildSlots(
                player,
                axis,
                EnemySquadPursuitPlanner.DefaultSlotRadius,
                changedSlots);
            changedDirectSlot = FindSlotByKind(changedSlots, EnemySquadPursuitSlotKind.Direct);
            if (changedDirectSlot.PointIndex != directPointIndex)
            {
                throw new System.InvalidOperationException(
                    "Squad pursuit direct role selected the wrong fixed point expected=P"
                    + directPointIndex + " actual=P" + changedDirectSlot.PointIndex);
            }
            for (int pointIndex = 0; pointIndex < changedSlots.Count; pointIndex++)
            {
                EnemySquadPursuitSlot fixedPoint = FindSlotAtPoint(slots, pointIndex);
                EnemySquadPursuitSlot remappedPoint = FindSlotAtPoint(changedSlots, pointIndex);
                EnemySquadPursuitSlotKind expectedKind = (EnemySquadPursuitSlotKind)(
                    (pointIndex - directPointIndex + 8) % 8);
                if ((fixedPoint.Position - remappedPoint.Position).sqrMagnitude > 0.0001f
                    || remappedPoint.Kind != expectedKind)
                {
                    throw new System.InvalidOperationException(
                        "Squad pursuit 8-direction role sweep changed a fixed point or role mapping axis=P"
                        + directPointIndex + " point=P" + pointIndex);
                }
            }
        }

        EnemySquadPursuitPlanner.BuildSlots(
            player,
            directOutward,
            EnemySquadPursuitPlanner.DefaultSlotRadius,
            slots);
        List<int> balancedSlots = new List<int>(7);
        EnemySquadPursuitPlanner.BuildBalancedSlotIndices(slots, 5, balancedSlots);
        if (balancedSlots.Count != 5
            || !balancedSlots.Contains(FindSlotByKind(slots, EnemySquadPursuitSlotKind.Direct).Index)
            || balancedSlots.Contains(FindSlotByKind(slots, EnemySquadPursuitSlotKind.Rear).Index)
            || ResolveMaximumSelectedPointGap(balancedSlots) > 2)
        {
            throw new System.InvalidOperationException(
                "Squad pursuit balanced subset did not distribute five slots around the player");
        }
        EnemySquadPursuitPlanner.BuildBalancedSlotIndices(slots, 7, balancedSlots);
        if (balancedSlots.Count != 7
            || balancedSlots.Contains(FindSlotByKind(slots, EnemySquadPursuitSlotKind.Rear).Index))
        {
            throw new System.InvalidOperationException(
                "Squad pursuit far assignment must use every non-rear slot and exclude rear center");
        }
        Vector3 rearClaimInside = player
            + Quaternion.AngleAxis(29f, Vector3.up) * rearSlot.OutwardDirection * 13f;
        Vector3 rearClaimOutside = player
            + Quaternion.AngleAxis(31f, Vector3.up) * rearSlot.OutwardDirection * 13f;
        Vector3 rearReleaseInside = player
            + Quaternion.AngleAxis(44f, Vector3.up) * rearSlot.OutwardDirection * 13f;
        if (EnemySquadPursuitPlanner.DefaultRearSlotClaimAngle
                >= EnemySquadPursuitPlanner.DefaultRearSlotReleaseAngle
            || !EnemySquadPursuitPlanner.IsWithinSlotAngularSector(
                player,
                rearClaimInside,
                rearSlot.OutwardDirection,
                EnemySquadPursuitPlanner.DefaultRearSlotClaimAngle)
            || EnemySquadPursuitPlanner.IsWithinSlotAngularSector(
                player,
                rearClaimOutside,
                rearSlot.OutwardDirection,
                EnemySquadPursuitPlanner.DefaultRearSlotClaimAngle)
            || !EnemySquadPursuitPlanner.IsWithinSlotAngularSector(
                player,
                rearReleaseInside,
                rearSlot.OutwardDirection,
                EnemySquadPursuitPlanner.DefaultRearSlotReleaseAngle))
        {
            throw new System.InvalidOperationException(
                "Squad conditional rear-orbit sector or release hysteresis contract failed");
        }
        EnemySquadPursuitPlanner.BuildBalancedSlotIndices(slots, 1, balancedSlots);
        if (balancedSlots.Count != 1
            || balancedSlots[0] != FindSlotByKind(slots, EnemySquadPursuitSlotKind.Direct).Index
            || EnemySquadPursuitPlanner.DefaultNearReleaseDistance
                >= EnemySquadPursuitPlanner.DefaultSlotRadius
            || EnemySquadPursuitPlanner.DefaultSlotRadius
                >= EnemySquadPursuitPlanner.DefaultDirectCommitRadius
            || EnemySquadPursuitPlanner.DefaultDirectCommitRadius
                >= EnemySquadPursuitPlanner.DefaultFarActivationDistance)
        {
            throw new System.InvalidOperationException(
                "Squad pursuit direct priority or near/slot/commit/far distance ordering changed");
        }

        Vector3 sideSquadCenter = new Vector3(15f, 0f, 1f);
        EnemySquadPursuitSlot sideSlot = FindSlotByKind(slots, EnemySquadPursuitSlotKind.RightBypass);
        EnemySquadPursuitRoute sideRoute = EnemySquadPursuitPlanner.BuildRoute(
            sideSquadCenter,
            player,
            directOutward,
            sideSlot,
            slots);
        Vector3 rightDirection = new Vector3(directOutward.z, 0f, -directOutward.x);
        Vector3 previousPoint = sideSquadCenter;
        Vector3 previousSegment = Vector3.zero;
        float accumulatedTurn = 0f;
        for (int waypointIndex = 0; waypointIndex < sideRoute.WaypointCount; waypointIndex++)
        {
            Vector3 waypoint = sideRoute.GetWaypoint(waypointIndex);
            Vector3 segment = waypoint - previousPoint;
            segment.y = 0f;
            if (segment.sqrMagnitude <= 0.0001f
                || (waypointIndex > 0 && Vector3.Dot(previousSegment.normalized, segment.normalized) <= 0.25f))
            {
                throw new System.InvalidOperationException(
                    "Squad side pursuit curve contained a zero or sharp reverse segment index="
                    + waypointIndex);
            }
            if (waypointIndex > 0)
                accumulatedTurn += Mathf.Abs(Vector3.Cross(previousSegment.normalized, segment.normalized).y);
            previousSegment = segment;
            previousPoint = waypoint;
        }
        if (sideRoute.WaypointCount != 5
            || (sideRoute.GetWaypoint(4) - sideSlot.Position).sqrMagnitude > 0.0001f
            || Vector3.Dot(sideRoute.GetWaypoint(2) - player, rightDirection) <= 0f
            || accumulatedTurn <= 0.1f)
        {
            throw new System.InvalidOperationException(
                "Squad right pursuit route did not form a smooth right-side quadratic curve");
        }

        EnemySquadPursuitSlot leftSlot = FindSlotByKind(slots, EnemySquadPursuitSlotKind.LeftBypass);
        EnemySquadPursuitRoute leftRoute = EnemySquadPursuitPlanner.BuildRoute(
            new Vector3(15f, 0f, -1f),
            player,
            directOutward,
            leftSlot,
            slots);
        if (leftRoute.WaypointCount != 5
            || Vector3.Dot(leftRoute.GetWaypoint(2) - player, rightDirection) >= 0f
            || (leftRoute.GetWaypoint(4) - leftSlot.Position).sqrMagnitude > 0.0001f)
        {
            throw new System.InvalidOperationException(
                "Squad left pursuit route did not form a smooth left-side quadratic curve");
        }

        EnemySquadPursuitSlot frontDiagonalSlot = FindSlotByKind(
            slots,
            EnemySquadPursuitSlotKind.FrontRightDiagonal);
        EnemySquadPursuitSlot rearDiagonalSlot = FindSlotByKind(
            slots,
            EnemySquadPursuitSlotKind.RearRightDiagonal);
        EnemySquadPursuitSlot frontLeftDiagonalSlot = FindSlotByKind(
            slots,
            EnemySquadPursuitSlotKind.FrontLeftDiagonal);
        EnemySquadPursuitSlot rearLeftDiagonalSlot = FindSlotByKind(
            slots,
            EnemySquadPursuitSlotKind.RearLeftDiagonal);
        EnemySquadPursuitRoute frontDiagonalRoute = EnemySquadPursuitPlanner.BuildRoute(
            sideSquadCenter,
            player,
            directOutward,
            frontDiagonalSlot,
            slots);
        EnemySquadPursuitRoute rearDiagonalRoute = EnemySquadPursuitPlanner.BuildRoute(
            sideSquadCenter,
            player,
            directOutward,
            rearDiagonalSlot,
            slots);
        EnemySquadPursuitRoute frontLeftDiagonalRoute = EnemySquadPursuitPlanner.BuildRoute(
            sideSquadCenter,
            player,
            directOutward,
            frontLeftDiagonalSlot,
            slots);
        EnemySquadPursuitRoute rearLeftDiagonalRoute = EnemySquadPursuitPlanner.BuildRoute(
            sideSquadCenter,
            player,
            directOutward,
            rearLeftDiagonalSlot,
            slots);
        float frontDiagonalBulge = MeasureRouteBulge(
            sideSquadCenter,
            frontDiagonalSlot.Position,
            frontDiagonalRoute);
        float sideBypassBulge = MeasureRouteBulge(
            sideSquadCenter,
            sideSlot.Position,
            sideRoute);
        EnemySquadPursuitRoute compactSideRoute = EnemySquadPursuitPlanner.BuildRoute(
            sideSquadCenter,
            player,
            directOutward,
            sideSlot,
            slots,
            0.8f);
        float compactSideBulge = MeasureRouteBulge(
            sideSquadCenter,
            sideSlot.Position,
            compactSideRoute);
        float rearDiagonalBulge = MeasureRouteBulge(
            sideSquadCenter,
            rearDiagonalSlot.Position,
            rearDiagonalRoute);
        if (frontDiagonalRoute.WaypointCount != 5
            || rearDiagonalRoute.WaypointCount != 5
            || frontLeftDiagonalRoute.WaypointCount != 5
            || rearLeftDiagonalRoute.WaypointCount != 5
            || frontDiagonalBulge >= sideBypassBulge
            || sideBypassBulge >= rearDiagonalBulge
            || compactSideBulge >= sideBypassBulge
            || Vector3.Dot(frontLeftDiagonalRoute.GetWaypoint(2) - player, rightDirection) >= 0f
            || Vector3.Dot(rearLeftDiagonalRoute.GetWaypoint(2) - player, rightDirection) >= 0f)
        {
            throw new System.InvalidOperationException(
                "Squad pursuit curve tiers must remain small front-diagonal, large side and largest rear-diagonal");
        }

        EnemySquadPursuitRoute rearRoute = EnemySquadPursuitPlanner.BuildRoute(
            new Vector3(15f, 0f, -2f),
            player,
            directOutward,
            FindSlotByKind(slots, EnemySquadPursuitSlotKind.Rear),
            slots);
        int[] expectedPriority = { 0, 2, 6, 1, 7, 3, 5, 4 };
        HashSet<int> preferredSlots = new HashSet<int>();
        HashSet<Color32> fixedRoleColors = new HashSet<Color32>();
        for (int order = 0; order < expectedPriority.Length; order++)
        {
            int actual = EnemySquadPursuitPlanner.GetPreferredSlotIndex(order);
            preferredSlots.Add(actual);
            fixedRoleColors.Add((Color32)EnemySquadPursuitSimulatorWindow.ResolveSlotRoleColor(
                (EnemySquadPursuitSlotKind)actual));
            if (actual != expectedPriority[order])
            {
                throw new System.InvalidOperationException(
                    "Squad pursuit slot priority changed order=" + order
                    + " expected=" + expectedPriority[order] + " actual=" + actual);
            }
        }

        List<Vector3> distancePrioritySquads = new List<Vector3>
        {
            new Vector3(1f, 0f, 0f),
            new Vector3(-2f, 0f, 0f)
        };
        List<Vector3> distancePrioritySlots = new List<Vector3>
        {
            Vector3.zero,
            new Vector3(10f, 0f, 0f)
        };
        List<int> squadIndexBySlot = new List<int>();
        EnemySquadPursuitPlanner.BuildMinimumDistanceAssignment(
            distancePrioritySquads,
            distancePrioritySlots,
            squadIndexBySlot);
        if (squadIndexBySlot.Count != 2
            || squadIndexBySlot[0] != 1
            || squadIndexBySlot[1] != 0)
        {
            throw new System.InvalidOperationException(
                "Squad pursuit slots must use minimum-total-distance pairing instead of slot-first greedy pairing");
        }

        distancePrioritySquads.Clear();
        distancePrioritySquads.Add(new Vector3(9f, 0f, 0f));
        distancePrioritySlots.Clear();
        distancePrioritySlots.Add(new Vector3(-10f, 0f, 0f));
        distancePrioritySlots.Add(new Vector3(10f, 0f, 0f));
        EnemySquadPursuitPlanner.BuildMinimumDistanceAssignment(
            distancePrioritySquads,
            distancePrioritySlots,
            squadIndexBySlot);
        if (squadIndexBySlot.Count != 2
            || squadIndexBySlot[0] != -1
            || squadIndexBySlot[1] != 0)
        {
            throw new System.InvalidOperationException(
                "Squad pursuit must leave a farther priority slot vacant when a nearer slot is available");
        }

        distancePrioritySquads[0] = Vector3.zero;
        distancePrioritySlots[0] = Vector3.left;
        distancePrioritySlots[1] = Vector3.right;
        EnemySquadPursuitPlanner.BuildMinimumDistanceAssignment(
            distancePrioritySquads,
            distancePrioritySlots,
            squadIndexBySlot);
        if (squadIndexBySlot[0] != 0 || squadIndexBySlot[1] != -1)
        {
            throw new System.InvalidOperationException(
                "Squad pursuit role priority must break an exact distance tie deterministically");
        }

        if (rearRoute.WaypointCount != 1
            || (rearRoute.GetWaypoint(0) - rearSlot.Position).sqrMagnitude > 0.0001f
            || preferredSlots.Count != 8
            || fixedRoleColors.Count != 8
            || !preferredSlots.Contains(0)
            || !preferredSlots.Contains(2)
            || !preferredSlots.Contains(6)
            || !preferredSlots.Contains(4))
        {
            throw new System.InvalidOperationException(
                "Squad pursuit direct rear route, slot priority or fixed role color contract failed");
        }

        Vector3 reserveDirectionSum = Vector3.zero;
        int reserveQuadrants = 0;
        bool[] occupiedQuadrants = new bool[4];
        for (int squadId = 1; squadId <= 16; squadId++)
        {
            Vector3 reserveDirection = EnemySquadPursuitPlanner.ResolveReserveOrbitDirection(401, squadId);
            reserveDirectionSum += reserveDirection;
            int quadrant = reserveDirection.x >= 0f
                ? reserveDirection.z >= 0f ? 0 : 1
                : reserveDirection.z < 0f ? 2 : 3;
            if (!occupiedQuadrants[quadrant])
            {
                occupiedQuadrants[quadrant] = true;
                reserveQuadrants++;
            }
            float reserveRadius = EnemySquadPursuitPlanner.ResolveReserveOrbitRadius(
                EnemySquadPursuitPlanner.DefaultReserveOrbitRadius,
                squadId);
            if (reserveRadius < EnemySquadPursuitPlanner.DefaultFarActivationDistance + 2f)
            {
                throw new System.InvalidOperationException(
                    "Squad reserve orbit entered the active slot ring squad=" + squadId);
            }
        }

        float oppositeRadius = EnemySquadPursuitPlanner.ResolveReserveOrbitRadius(
            EnemySquadPursuitPlanner.DefaultReserveOrbitRadius,
            2);
        Vector3 clockwiseDestination = EnemySquadPursuitPlanner.ResolveReserveOrbitDestination(
            player,
            player + Vector3.right * oppositeRadius,
            Vector3.left,
            oppositeRadius,
            2);
        Vector3 counterClockwiseDestination = EnemySquadPursuitPlanner.ResolveReserveOrbitDestination(
            player,
            player + Vector3.right * oppositeRadius,
            Vector3.left,
            oppositeRadius,
            3);
        Vector3 clockwiseDirection = (clockwiseDestination - player).normalized;
        Vector3 counterClockwiseDirection = (counterClockwiseDestination - player).normalized;
        Vector3 reserveStartDirection = EnemySquadPursuitPlanner.ResolveReserveOrbitDirection(401, 2, 0f);
        Vector3 reserveQuarterTurnDirection = EnemySquadPursuitPlanner.ResolveReserveOrbitDirection(401, 2, 90f);
        float clockwiseProjectedClearance = Vector3.Dot(
            clockwiseDestination - player,
            Vector3.right);
        float counterClockwiseProjectedClearance = Vector3.Dot(
            counterClockwiseDestination - player,
            Vector3.right);
        if (reserveQuadrants != 4
            || reserveDirectionSum.magnitude >= 2f
            || clockwiseProjectedClearance < oppositeRadius - 0.001f
            || counterClockwiseProjectedClearance < oppositeRadius - 0.001f
            || Vector3.Dot(clockwiseDirection, Vector3.right) <= 0.8f
            || Vector3.Dot(counterClockwiseDirection, Vector3.right) <= 0.8f
            || clockwiseDestination.z >= 0f
            || counterClockwiseDestination.z <= 0f
            || Mathf.Abs(Vector3.Dot(reserveStartDirection, reserveQuarterTurnDirection)) > 0.001f
            || Mathf.Abs(reserveQuarterTurnDirection.magnitude - 1f) > 0.001f)
        {
            throw new System.InvalidOperationException(
                "Squad reserve orbit did not distribute, rotate or preserve player clearance");
        }

        Vector3 formationDestination = new Vector3(4f, 0f, -2f);
        Vector3 formationOffset = new Vector3(1.5f, 0f, 0.5f);
        Vector3 arrivedMemberPosition = formationDestination + formationOffset * 0.52f;
        if (!EnemySquadPursuitSimulatorWindow.IsMemberAtFormationDestination(
                arrivedMemberPosition,
                formationDestination,
                formationOffset,
                0.52f,
                0.2f)
            || EnemySquadPursuitSimulatorWindow.IsMemberAtFormationDestination(
                arrivedMemberPosition + Vector3.right,
                formationDestination,
                formationOffset,
                0.52f,
                0.2f))
        {
            throw new System.InvalidOperationException(
                "Squad pursuit member-first formation arrival contract failed");
        }

        if (!EnemySquadPursuitSimulatorWindow.IsOutsidePlayerDistance(
                Vector3.right * 11.1f,
                Vector3.zero,
                11f)
            || EnemySquadPursuitSimulatorWindow.IsOutsidePlayerDistance(
                Vector3.right * 11f,
                Vector3.zero,
                11f))
        {
            throw new System.InvalidOperationException(
                "Squad Rush Far-area release boundary contract failed");
        }

        Vector3 forwardPreserved = EnemySquadPursuitSimulatorWindow.PreserveMinimumForwardProgress(
            Vector3.left,
            Vector3.right * 0.8f + Vector3.forward,
            0.2f);
        if (Vector3.Dot(forwardPreserved, Vector3.left) < 0.199f
            || forwardPreserved.magnitude > 1.001f)
        {
            throw new System.InvalidOperationException(
                "Squad pursuit separation cancelled the guaranteed forward progress");
        }

        EnemySquadPursuitRoute translatedSideRoute = EnemySquadPursuitPlanner.TranslateRoute(
            sideRoute,
            new Vector3(2f, 9f, -3f));
        if (EnemySquadPursuitPlanner.ShouldRefreshRoute(0.99f, 1f, 1f, false)
            || !EnemySquadPursuitPlanner.ShouldRefreshRoute(1f, 1f, 1f, false)
            || EnemySquadPursuitPlanner.ShouldRefreshRoute(1f, 0.99f, 1f, false)
            || EnemySquadPursuitPlanner.ShouldRefreshRoute(1f, 1f, 1f, true)
            || EnemySquadPursuitPlanner.ShouldRefreshFullReformation(11.99f, 9.99f, 10f, 0f)
            || !EnemySquadPursuitPlanner.ShouldRefreshFullReformation(0f, 10f, 10f, 0f)
            || EnemySquadPursuitPlanner.ShouldRefreshFullReformation(12f, 4.99f, 10f, 0f)
            || !EnemySquadPursuitPlanner.ShouldRefreshFullReformation(12f, 5f, 10f, 0f)
            || Vector3.Distance(
                translatedSideRoute.GetWaypoint(0),
                sideRoute.GetWaypoint(0) + new Vector3(2f, 0f, -3f)) > 0.001f)
        {
            throw new System.InvalidOperationException(
                "Squad route translation, limited rebuild, or full reformation contract failed");
        }

        Vector3 cohesionDestination = EnemySquadPursuitPlanner.ResolveCohesionDestination(
            new Vector3(0f, 2f, 3f),
            Vector3.zero,
            Vector3.zero,
            Vector3.right * 10f,
            0.52f);
        Vector3 cohesionDeadZoneDestination = EnemySquadPursuitPlanner.ResolveCohesionDestination(
            new Vector3(0f, 2f, 0.5f),
            Vector3.zero,
            Vector3.zero,
            Vector3.right * 10f,
            0.52f);
        float trailingBoost = EnemySquadPursuitPlanner.ResolveCohesionSpeedMultiplier(
            Vector3.left * 5f,
            Vector3.zero,
            Vector3.right * 10f);
        float lateralBoost = EnemySquadPursuitPlanner.ResolveCohesionSpeedMultiplier(
            Vector3.forward * 5f,
            Vector3.zero,
            Vector3.right * 10f);
        if (cohesionDestination.x <= 0f
            || cohesionDestination.z >= 3f
            || !Mathf.Approximately(cohesionDestination.y, 2f)
            || Vector3.Distance(cohesionDeadZoneDestination, new Vector3(10f, 2f, 0f)) > 0.001f
            || trailingBoost < 1.119f
            || trailingBoost > 1.121f
            || !Mathf.Approximately(lateralBoost, 1f))
        {
            throw new System.InvalidOperationException(
                "Squad moving cohesion steering or catch-up speed contract failed");
        }

        if (EnemySquadPursuitSimulatorWindow.ResolveMovePriorityValue(true, false, false) != 3
            || EnemySquadPursuitSimulatorWindow.ResolveMovePriorityValue(false, true, false) != 2
            || EnemySquadPursuitSimulatorWindow.ResolveMovePriorityValue(false, false, true) != 1
            || EnemySquadPursuitSimulatorWindow.ResolveMovePriorityValue(false, false, false) != 0
            || !EnemySquadPursuitSimulatorWindow.ShouldUseRemnantPattern(false, 3, 10, 0.4f)
            || EnemySquadPursuitSimulatorWindow.ShouldUseRemnantPattern(false, 4, 10, 0.4f)
            || !EnemySquadPursuitSimulatorWindow.ShouldUseRemnantPattern(true, 10, 10, 0.4f)
            || EnemySquadPursuitSimulatorWindow.ShouldUseRemnantPattern(true, 0, 10, 0.4f))
        {
            throw new System.InvalidOperationException(
                "Squad Near/Remnant priority or permanent slot-revocation contract failed");
        }

        float priorityOwnerShare = EnemySquadPursuitSimulatorWindow.ResolvePriorityCorrectionShare(
            1f,
            1f,
            2,
            0);
        float reserveShare = EnemySquadPursuitSimulatorWindow.ResolvePriorityCorrectionShare(
            1f,
            1f,
            0,
            2);
        float weightedShare = EnemySquadPursuitSimulatorWindow.ResolvePriorityCorrectionShare(
            1f,
            2f,
            0,
            0);
        Vector3 stablePairA = EnemySquadPursuitSimulatorWindow.ResolveStablePairDirection(17, 29);
        Vector3 stablePairB = EnemySquadPursuitSimulatorWindow.ResolveStablePairDirection(29, 17);
        if (Mathf.Abs(priorityOwnerShare - 0.1f) > 0.0001f
            || Mathf.Abs(reserveShare - 0.9f) > 0.0001f
            || Mathf.Abs(weightedShare - 2f / 3f) > 0.0001f
            || Mathf.Abs(EnemySquadPursuitSimulatorWindow.ResolvePrioritySeparationScale(2, 0) - 0.15f) > 0.0001f
            || Mathf.Abs(EnemySquadPursuitSimulatorWindow.ResolvePrioritySeparationScale(0, 2) - 1.35f) > 0.0001f
            || (stablePairA + stablePairB).sqrMagnitude > 0.0001f)
        {
            throw new System.InvalidOperationException(
                "Squad pursuit priority Hard Overlap or stable pair-direction contract failed");
        }

        EnemyCrowdPriorityBody highPriorityBody = new EnemyCrowdPriorityBody
        {
            StableId = 101,
            DesiredPosition = Vector3.zero,
            ResolvedPosition = Vector3.zero,
            BodyRadius = 0.5f,
            CrowdWeight = 1f,
            MovePriority = 2,
            CanMove = true
        };
        EnemyCrowdPriorityBody reserveBody = new EnemyCrowdPriorityBody
        {
            StableId = 202,
            DesiredPosition = Vector3.right * 0.8f,
            ResolvedPosition = Vector3.right * 0.8f,
            BodyRadius = 0.5f,
            CrowdWeight = 1f,
            MovePriority = 0,
            CanMove = true
        };
        if (!EnemyCrowdPrioritySolver.TryCalculatePair(
                highPriorityBody,
                reserveBody,
                out EnemyCrowdPairCorrection centralPair)
            || Mathf.Abs(centralPair.CorrectionA.magnitude - 0.02f) > 0.0001f
            || Mathf.Abs(centralPair.CorrectionB.magnitude - 0.18f) > 0.0001f
            || centralPair.YieldCorrectionA.sqrMagnitude > 0.000001f
            || centralPair.YieldCorrectionB.sqrMagnitude <= 0.000001f)
        {
            throw new System.InvalidOperationException(
                "Central crowd solver bidirectional 10:90 or Reserve Yield contract failed");
        }

        if (!EnemyCrowdPrioritySolver.TryCalculatePair(
                reserveBody,
                highPriorityBody,
                out EnemyCrowdPairCorrection reversedCentralPair)
            || (reversedCentralPair.CorrectionA - centralPair.CorrectionB).sqrMagnitude > 0.000001f
            || (reversedCentralPair.CorrectionB - centralPair.CorrectionA).sqrMagnitude > 0.000001f)
        {
            throw new System.InvalidOperationException(
                "Central crowd solver pair-order independence contract failed");
        }

        EnemyCrowdPriorityBody lockedBody = highPriorityBody;
        lockedBody.CanMove = false;
        if (!EnemyCrowdPrioritySolver.TryCalculatePair(
                lockedBody,
                reserveBody,
                out EnemyCrowdPairCorrection lockedPair)
            || lockedPair.CorrectionA.sqrMagnitude > 0.000001f
            || Mathf.Abs(lockedPair.CorrectionB.magnitude - 0.2f) > 0.0001f)
        {
            throw new System.InvalidOperationException(
                "Central crowd solver position-lock contract failed");
        }

        EnemyCrowdPriorityBody forcedBody = highPriorityBody;
        forcedBody.IsForcedMotion = true;
        if (!EnemyCrowdPrioritySolver.TryCalculatePair(
                forcedBody,
                reserveBody,
                out EnemyCrowdPairCorrection forcedPair)
            || forcedPair.CorrectionA.sqrMagnitude > 0.000001f
            || Mathf.Abs(forcedPair.CorrectionB.magnitude - 0.2f) > 0.0001f)
        {
            throw new System.InvalidOperationException(
                "Central crowd solver forced-motion authority contract failed");
        }

        EnemyCrowdPriorityBody cappedBody = highPriorityBody;
        Vector3 cappedCorrection = EnemyCrowdPrioritySolver.ApplyAccumulatedCorrection(
            ref cappedBody,
            Vector3.left,
            EnemyCrowdService.MaximumCentralCorrection);
        if (Mathf.Abs(cappedCorrection.magnitude - EnemyCrowdService.MaximumCentralCorrection) > 0.0001f)
        {
            throw new System.InvalidOperationException(
                "Central crowd solver per-FixedUpdate correction cap failed");
        }

        Debug.Log(
            "[EnemyStatePatternPlayModeVerifier] Passed maximum-first squad formation (4~8, 8~12, 0~200), uniform physical slots, conditional rear Reserve, route translation, full reformation, central priority pair solver, member-first Rush, all-outside release and reserve contracts");
    }

    private static void VerifyMaximumFirstSquadSizes(
        int minimum,
        int maximum,
        int monsterCount,
        params int[] expected)
    {
        List<int> sizes = new List<int>();
        EnemySquadPursuitPlanner.BuildMaximumFirstSquadSizes(
            monsterCount,
            minimum,
            maximum,
            sizes);
        if (sizes.Count != expected.Length)
        {
            throw new System.InvalidOperationException(
                "Maximum-first squad count mismatch range=" + minimum + "~" + maximum
                + " monsters=" + monsterCount + " actual=" + sizes.Count
                + " expected=" + expected.Length);
        }

        for (int i = 0; i < expected.Length; i++)
        {
            if (sizes[i] != expected[i])
            {
                throw new System.InvalidOperationException(
                    "Maximum-first squad size mismatch range=" + minimum + "~" + maximum
                    + " monsters=" + monsterCount + " index=" + i
                    + " actual=" + sizes[i] + " expected=" + expected[i]);
            }
        }
    }

    private static void VerifyMaximumFirstSquadRange(int minimum, int maximum)
    {
        List<int> sizes = new List<int>();
        for (int monsterCount = 0; monsterCount <= 200; monsterCount++)
        {
            EnemySquadPursuitPlanner.BuildMaximumFirstSquadSizes(
                monsterCount,
                minimum,
                maximum,
                sizes);
            int assignedCount = 0;
            for (int i = 0; i < sizes.Count; i++)
            {
                if (sizes[i] < minimum
                    || sizes[i] > maximum
                    || i < sizes.Count - 1 && sizes[i] != maximum)
                {
                    throw new System.InvalidOperationException(
                        "Maximum-first range sweep produced invalid size range="
                        + minimum + "~" + maximum + " monsters=" + monsterCount
                        + " index=" + i + " size=" + sizes[i]);
                }
                assignedCount += sizes[i];
            }

            int remainder = monsterCount - assignedCount;
            int rawRemainder = monsterCount % maximum;
            int expectedAssigned = monsterCount / maximum * maximum
                + (rawRemainder >= minimum ? rawRemainder : 0);
            if (assignedCount != expectedAssigned || remainder < 0 || remainder >= minimum)
            {
                throw new System.InvalidOperationException(
                    "Maximum-first range sweep assignment mismatch range="
                    + minimum + "~" + maximum + " monsters=" + monsterCount
                    + " assigned=" + assignedCount + " expected=" + expectedAssigned
                    + " remainder=" + remainder);
            }
        }
    }

    private static float MeasureRouteBulge(
        Vector3 start,
        Vector3 end,
        EnemySquadPursuitRoute route)
    {
        if (route.WaypointCount < 3)
            return 0f;

        Vector3 linearPoint = Vector3.Lerp(start, end, 0.6f);
        Vector3 delta = route.GetWaypoint(2) - linearPoint;
        delta.y = 0f;
        return delta.magnitude;
    }

    private static EnemySquadPursuitSlot FindSlotAtPoint(
        List<EnemySquadPursuitSlot> slots,
        int pointIndex)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].PointIndex == pointIndex)
                return slots[i];
        }

        throw new System.InvalidOperationException(
            "Squad pursuit fixed point mapping is missing point=P" + pointIndex);
    }

    private static EnemySquadPursuitSlot FindSlotByKind(
        List<EnemySquadPursuitSlot> slots,
        EnemySquadPursuitSlotKind kind)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].Kind == kind)
                return slots[i];
        }

        throw new System.InvalidOperationException(
            "Squad pursuit role mapping is missing kind=" + kind);
    }

    private static int ResolveMaximumSelectedPointGap(List<int> selectedPointIndices)
    {
        bool[] selected = new bool[8];
        for (int i = 0; i < selectedPointIndices.Count; i++)
        {
            int pointIndex = selectedPointIndices[i];
            if (pointIndex >= 0 && pointIndex < selected.Length)
                selected[pointIndex] = true;
        }

        int first = -1;
        int previous = -1;
        int maximumGap = 0;
        for (int pointIndex = 0; pointIndex < selected.Length; pointIndex++)
        {
            if (!selected[pointIndex])
                continue;
            if (first < 0)
                first = pointIndex;
            if (previous >= 0)
                maximumGap = Mathf.Max(maximumGap, pointIndex - previous);
            previous = pointIndex;
        }

        if (first >= 0 && previous >= 0)
            maximumGap = Mathf.Max(maximumGap, first + 8 - previous);
        return maximumGap;
    }

    private static void VerifyClusterFanOutIntegrationContract()
    {
        const string gruntPath = "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Grunt.prefab";
        List<GameObject> monsters = new List<GameObject>(12);
        Vector3 previousPlayerPosition = playerObject.transform.position;
        try
        {
            EnemyAIController.SetDensityApproachSteeringEnabled(true);
            EnemyAIController.SetChaseBypassSteeringEnabled(true);
            EnemyAIController.SetClusterFanOutSteeringEnabled(true);
            EnemyClusterFanOutService.ClearCache();
            playerObject.transform.position = Vector3.zero;

            EnemyAIController rearAi = null;
            EnemyMovement rearMovement = null;
            EnemyAIController frontAi = null;
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    Vector3 position = new Vector3(
                        -0.8f + column * 0.8f,
                        0f,
                        9f - row * 1.2f);
                    GameObject monster = InstantiateTacticMonster(gruntPath, position);
                    monsters.Add(monster);
                    EnemyAIController ai = RequireComponent<EnemyAIController>(monster);
                    ai.SetHomePosition(position);
                    ai.SetTarget(playerObject.transform);
                    if (row == 0 && column == 0)
                    {
                        rearAi = ai;
                        rearMovement = RequireComponent<EnemyMovement>(monster);
                    }
                    if (row == 3 && column == 1)
                        frontAi = ai;
                }
            }

            Physics.SyncTransforms();
            EnemyClusterFanOutService.ClearCache();
            System.Reflection.MethodInfo resolveApproach = typeof(EnemyAIController).GetMethod(
                "ResolveChaseDestination",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            System.Reflection.MethodInfo changeToChase = typeof(EnemyAIController).GetMethod(
                "ChangeToChase",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (rearAi == null
                || rearMovement == null
                || frontAi == null
                || resolveApproach == null
                || changeToChase == null)
            {
                throw new System.InvalidOperationException("Cluster fan-out integration hook missing");
            }

            Vector3 rearDestination = (Vector3)resolveApproach.Invoke(rearAi, null);
            Vector3 rearDelta = rearDestination - rearAi.transform.position;
            rearDelta.y = 0f;
            resolveApproach.Invoke(frontAi, null);
            if (!rearAi.UsesClusterFanOutSteering
                || !rearAi.IsClusterFanOutActive
                || rearAi.IsChaseBypassActive
                || Mathf.Abs(rearDelta.x) < 0.25f
                || rearAi.CurrentChaseSpeedMultiplier
                    < EnemyClusterFanOutSteering.MinimumSpeedMultiplier
                || frontAi.IsClusterFanOutActive)
            {
                throw new System.InvalidOperationException(
                    "Cluster integration did not separate rear and front rows rearActive="
                    + rearAi.IsClusterFanOutActive
                    + " frontActive=" + frontAi.IsClusterFanOutActive
                    + " rearDelta=" + rearDelta
                    + " speed=" + rearAi.CurrentChaseSpeedMultiplier);
            }

            changeToChase.Invoke(rearAi, null);
            float baseMoveSpeed = rearMovement.MoveSpeed;
            if (rearMovement.LocomotionMode == EnemyLocomotionMode.Run && rearMovement.Profile != null)
                baseMoveSpeed *= rearMovement.Profile.RunSpeedMultiplier;
            if (!rearAi.IsClusterFanOutActive
                || rearAi.CurrentDebugStateName != "Chase/FanOut"
                || !rearMovement.HasDestination
                || rearMovement.ActiveMoveSpeed
                    < baseMoveSpeed * EnemyClusterFanOutSteering.MinimumSpeedMultiplier - 0.001f)
            {
                throw new System.InvalidOperationException(
                    "Cluster fan-out did not drive locomotion and debug mode state="
                    + rearAi.CurrentDebugStateName
                    + " base=" + baseMoveSpeed
                    + " speed=" + rearMovement.ActiveMoveSpeed);
            }

            EnemyAIController.SetClusterFanOutSteeringEnabled(false);
            Vector3 rollbackDestination = (Vector3)resolveApproach.Invoke(rearAi, null);
            if (rearAi.UsesClusterFanOutSteering
                || rearAi.IsClusterFanOutActive
                || !rearAi.UsesChaseBypassSteering
                || !rearAi.IsChaseBypassActive
                || rollbackDestination == rearAi.transform.position)
            {
                throw new System.InvalidOperationException(
                    "Cluster fan-out rollback did not restore individual Chase/Bypass fanOut="
                    + rearAi.IsClusterFanOutActive
                    + " bypass=" + rearAi.IsChaseBypassActive);
            }

            EnemyAIController.SetClusterFanOutSteeringEnabled(true);
            EnemyClusterFanOutService.ClearCache();
            resolveApproach.Invoke(rearAi, null);
            if (!rearAi.IsClusterFanOutActive || rearAi.IsChaseBypassActive)
            {
                throw new System.InvalidOperationException(
                    "Cluster fan-out did not restore after global re-enable");
            }

            Debug.Log(
                "[EnemyStatePatternPlayModeVerifier] Passed 12-agent Chase/FanOut integration, front hold, speed and Bypass rollback");
        }
        finally
        {
            EnemyAIController.SetClusterFanOutSteeringEnabled(true);
            EnemyAIController.SetChaseBypassSteeringEnabled(true);
            EnemyAIController.SetDensityApproachSteeringEnabled(true);
            EnemyClusterFanOutService.ClearCache();
            playerObject.transform.position = previousPlayerPosition;
            for (int i = 0; i < monsters.Count; i++)
            {
                if (monsters[i] != null)
                    Object.DestroyImmediate(monsters[i]);
            }
        }
    }

    private static void VerifyChaseBypassIntegrationContract()
    {
        const string gruntPath = "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Grunt.prefab";
        List<GameObject> monsters = new List<GameObject>(3);
        Vector3 previousPlayerPosition = playerObject.transform.position;
        try
        {
            EnemyAIController.SetDensityApproachSteeringEnabled(true);
            EnemyAIController.SetChaseBypassSteeringEnabled(true);
            EnemyAIController.SetClusterFanOutSteeringEnabled(false);
            playerObject.transform.position = Vector3.zero;
            Vector3[] positions =
            {
                new Vector3(0f, 0f, 8f),
                new Vector3(0f, 0f, 6.5f),
                new Vector3(0f, 0f, 5f)
            };
            EnemyAIController rearAi = null;
            EnemyMovement rearMovement = null;
            for (int i = 0; i < positions.Length; i++)
            {
                GameObject monster = InstantiateTacticMonster(gruntPath, positions[i]);
                monsters.Add(monster);
                EnemyAIController ai = RequireComponent<EnemyAIController>(monster);
                ai.SetHomePosition(positions[i]);
                ai.SetTarget(playerObject.transform);
                if (i == 0)
                {
                    rearAi = ai;
                    rearMovement = RequireComponent<EnemyMovement>(monster);
                }
            }

            Physics.SyncTransforms();
            System.Reflection.MethodInfo resolveApproach = typeof(EnemyAIController).GetMethod(
                "ResolveChaseDestination",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            System.Reflection.MethodInfo changeToChase = typeof(EnemyAIController).GetMethod(
                "ChangeToChase",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (rearAi == null || rearMovement == null || resolveApproach == null || changeToChase == null)
                throw new System.InvalidOperationException("Chase bypass integration hook missing");

            Vector3 bypassDestination = (Vector3)resolveApproach.Invoke(rearAi, null);
            Vector3 bypassDelta = bypassDestination - rearAi.transform.position;
            bypassDelta.y = 0f;
            if (!rearAi.UsesChaseBypassSteering
                || !rearAi.IsChaseBypassActive
                || Mathf.Abs(bypassDelta.x) < 0.3f
                || rearAi.CurrentChaseSpeedMultiplier <= 1f)
            {
                throw new System.InvalidOperationException(
                    "Rear Chase did not enter bypass active=" + rearAi.IsChaseBypassActive
                    + " delta=" + bypassDelta
                    + " speed=" + rearAi.CurrentChaseSpeedMultiplier);
            }

            EnemyAIController.SetChaseBypassSteeringEnabled(false);
            resolveApproach.Invoke(rearAi, null);
            if (rearAi.UsesChaseBypassSteering
                || rearAi.IsChaseBypassActive
                || !Mathf.Approximately(rearAi.CurrentChaseSpeedMultiplier, 1f))
            {
                throw new System.InvalidOperationException("Chase bypass global rollback did not clear the live plan");
            }

            EnemyAIController.SetChaseBypassSteeringEnabled(true);
            changeToChase.Invoke(rearAi, null);
            float baseMoveSpeed = rearMovement.MoveSpeed;
            if (rearMovement.LocomotionMode == EnemyLocomotionMode.Run && rearMovement.Profile != null)
                baseMoveSpeed *= rearMovement.Profile.RunSpeedMultiplier;
            if (!rearAi.IsChaseBypassActive
                || rearAi.CurrentDebugStateName != "Chase/Bypass"
                || !rearMovement.HasDestination
                || rearMovement.ActiveMoveSpeed
                    < baseMoveSpeed * EnemyChaseBypassSteering.MinimumSpeedMultiplier - 0.001f)
            {
                throw new System.InvalidOperationException(
                    "Chase bypass did not restore locomotion and debug mode state=" + rearAi.CurrentDebugStateName
                    + " base=" + baseMoveSpeed
                    + " speed=" + rearMovement.ActiveMoveSpeed);
            }

            Debug.Log(
                "[EnemyStatePatternPlayModeVerifier] Passed rear queue Chase/Bypass integration, speed and global rollback");
        }
        finally
        {
            EnemyAIController.SetClusterFanOutSteeringEnabled(true);
            EnemyAIController.SetChaseBypassSteeringEnabled(true);
            EnemyAIController.SetDensityApproachSteeringEnabled(true);
            playerObject.transform.position = previousPlayerPosition;
            for (int i = 0; i < monsters.Count; i++)
            {
                if (monsters[i] != null)
                    Object.DestroyImmediate(monsters[i]);
            }
        }
    }

    private static void VerifySharedFlowFieldServiceContract()
    {
        GameObject firstTarget = null;
        GameObject secondTarget = null;
        try
        {
            bool[,] cells = CreateFlowFieldCells(6, 4, -1, -1);
            RunWalkableArea firstArea =
                new RunWalkableArea(cells, 6, 4, 0f, 0f, 1f);
            int revisionBefore = RunWalkableContext.Revision;
            RunWalkableContext.SetCurrent(firstArea);
            if (RunWalkableContext.Revision == revisionBefore)
                throw new System.InvalidOperationException("Walkable context revision did not advance");

            EnemyFlowFieldService.SetEnabled(true);
            EnemyFlowFieldService.ClearCache();
            firstTarget = new GameObject("FlowFieldSharedTargetA");
            firstTarget.transform.position = ResolveCellCenter(firstArea, 4, 1);
            Vector3 firstAgent = ResolveCellCenter(firstArea, 1, 1);
            if (!EnemyFlowFieldService.TryGetDirection(
                    firstTarget.transform,
                    firstAgent,
                    out _,
                    out _)
                || EnemyFlowFieldService.CachedTargetCount != 1
                || EnemyFlowFieldService.BuildCount != 1)
            {
                throw new System.InvalidOperationException("Shared Flow Field did not create one target cache");
            }

            if (!EnemyFlowFieldService.TryGetDirection(
                    firstTarget.transform,
                    ResolveCellCenter(firstArea, 1, 2),
                    out _,
                    out _)
                || EnemyFlowFieldService.BuildCount != 1
                || EnemyFlowFieldService.CacheHitCount <= 0)
            {
                throw new System.InvalidOperationException("Shared Flow Field was rebuilt per enemy request");
            }

            firstTarget.transform.position = ResolveCellCenter(firstArea, 4, 2);
            if (!EnemyFlowFieldService.TryGetDirection(firstTarget.transform, firstAgent, out _, out _)
                || EnemyFlowFieldService.BuildCount != 2
                || EnemyFlowFieldService.CachedTargetCount != 1)
            {
                throw new System.InvalidOperationException("Shared Flow Field target-cell rebuild contract failed");
            }

            RunWalkableArea secondArea = new RunWalkableArea(
                cells,
                6,
                4,
                10f,
                0f,
                1f);
            RunWalkableContext.SetCurrent(secondArea);
            firstTarget.transform.position = ResolveCellCenter(secondArea, 4, 2);
            Vector3 secondAreaAgent = ResolveCellCenter(secondArea, 1, 2);
            if (!EnemyFlowFieldService.TryGetDirection(firstTarget.transform, secondAreaAgent, out _, out _)
                || EnemyFlowFieldService.BuildCount != 3
                || EnemyFlowFieldService.CachedTargetCount != 1)
            {
                throw new System.InvalidOperationException("Shared Flow Field map revision invalidation failed");
            }

            secondTarget = new GameObject("FlowFieldSharedTargetB");
            secondTarget.transform.position = ResolveCellCenter(secondArea, 4, 1);
            if (!EnemyFlowFieldService.TryGetDirection(secondTarget.transform, secondAreaAgent, out _, out _)
                || EnemyFlowFieldService.BuildCount != 4
                || EnemyFlowFieldService.CachedTargetCount != 2
                || EnemyFlowFieldService.BuiltCellCountThisFrame > EnemyFlowFieldService.MaximumBuildCellsPerFrame)
            {
                throw new System.InvalidOperationException("Shared Flow Field multi-target cache or frame budget failed");
            }

            EnemyFlowFieldService.SetEnabled(false);
            if (EnemyFlowFieldService.TryGetDirection(firstTarget.transform, secondAreaAgent, out _, out _)
                || EnemyFlowFieldService.CachedTargetCount != 0)
            {
                throw new System.InvalidOperationException("Shared Flow Field rollback switch did not clear and disable caches");
            }

            Debug.Log(
                "[EnemyStatePatternPlayModeVerifier] Passed shared Flow Field cache, target/map rebuild, multi-target and rollback contracts");
        }
        finally
        {
            EnemyFlowFieldService.SetEnabled(true);
            EnemyFlowFieldService.ClearCache();
            RunWalkableContext.Clear();
            if (firstTarget != null)
                Object.DestroyImmediate(firstTarget);
            if (secondTarget != null)
                Object.DestroyImmediate(secondTarget);
        }
    }

    private static void VerifyFlowFieldChaseIntegrationContract()
    {
        const string gruntPath = "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Grunt.prefab";
        const int width = 12;
        const int height = 7;
        GameObject longMonster = null;
        GameObject nearMonster = null;
        Vector3 previousPlayerPosition = playerObject.transform.position;
        try
        {
            bool[,] cells = CreateFlowFieldCells(width, height, 5, 6);
            RunWalkableArea area =
                new RunWalkableArea(
                    cells,
                    width,
                    height,
                    0f,
                    0f,
                    1f);
            RunWalkableContext.SetCurrent(area);
            EnemyFlowFieldService.SetEnabled(true);
            EnemyFlowFieldService.ClearCache();
            playerObject.transform.position = ResolveCellCenter(area, 10, 3);

            System.Reflection.MethodInfo resolveApproach = typeof(EnemyAIController).GetMethod(
                "ResolveChaseDestination",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (resolveApproach == null)
                throw new System.InvalidOperationException("Flow Field Chase reflection hook missing");

            Vector3 longPosition = ResolveCellCenter(area, 1, 3);
            longMonster = InstantiateTacticMonster(gruntPath, longPosition);
            EnemyAIController longAi = RequireComponent<EnemyAIController>(longMonster);
            longAi.SetTarget(playerObject.transform);
            Vector3 flowDestination = (Vector3)resolveApproach.Invoke(longAi, null);
            Vector3 flowDelta = flowDestination - longPosition;
            flowDelta.y = 0f;
            if (longAi.IsDensityApproachActive
                || flowDelta.magnitude < EnemyApproachSteering.MinimumShortHorizonDistance - 0.001f
                || flowDelta.magnitude > EnemyApproachSteering.MaximumShortHorizonDistance + 0.001f
                || flowDelta.z <= 0.05f
                || !area.IsWalkable(flowDestination))
            {
                throw new System.InvalidOperationException(
                    "Long Chase did not follow the shared Flow Field detour delta=" + flowDelta);
            }

            EnemyFlowFieldService.SetEnabled(false);
            Vector3 fallbackDestination = (Vector3)resolveApproach.Invoke(longAi, null);
            Vector3 fallbackDelta = fallbackDestination - longMonster.transform.position;
            fallbackDelta.y = 0f;
            if (fallbackDelta.magnitude <= EnemyApproachSteering.MaximumShortHorizonDistance)
                throw new System.InvalidOperationException("Flow Field OFF did not restore natural long approach");

            EnemyFlowFieldService.SetEnabled(true);
            EnemyFlowFieldService.ClearCache();
            Vector3 nearPosition = ResolveCellCenter(area, 4, 3);
            nearMonster = InstantiateTacticMonster(gruntPath, nearPosition);
            EnemyAIController nearAi = RequireComponent<EnemyAIController>(nearMonster);
            nearAi.SetTarget(playerObject.transform);
            Vector3 nearDestination = (Vector3)resolveApproach.Invoke(nearAi, null);
            Vector3 nearDelta = nearDestination - nearPosition;
            nearDelta.y = 0f;
            if (!nearAi.IsDensityApproachActive
                || nearDelta.magnitude < EnemyApproachSteering.MinimumShortHorizonDistance - 0.001f
                || nearDelta.magnitude > EnemyApproachSteering.MaximumShortHorizonDistance + 0.001f
                || nearDelta.z <= 0.05f)
            {
                throw new System.InvalidOperationException(
                    "Near Chase did not blend Flow Field direction into density steering delta=" + nearDelta);
            }

            Debug.Log(
                "[EnemyStatePatternPlayModeVerifier] Passed Flow Field long Chase, natural fallback and near density blend");
        }
        finally
        {
            EnemyFlowFieldService.SetEnabled(true);
            EnemyFlowFieldService.ClearCache();
            RunWalkableContext.Clear();
            playerObject.transform.position = previousPlayerPosition;
            if (longMonster != null)
                Object.DestroyImmediate(longMonster);
            if (nearMonster != null)
                Object.DestroyImmediate(nearMonster);
        }
    }

    private static bool[,] CreateFlowFieldCells(int width, int height, int wallX, int gapZ)
    {
        bool[,] cells = new bool[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
                cells[x, z] = wallX < 0 || x != wallX || z == gapZ;
        }
        return cells;
    }

    private static Vector3 ResolveCellCenter(
        RunWalkableArea area,
        int x,
        int z,
        float y = 0f)
    {
        Vector2 center = area.GetCellCenter(x, z);
        return new Vector3(center.x, y, center.y);
    }

    private static void VerifyDensityApproachCoreRosterContract()
    {
        List<GameObject> monsters = new List<GameObject>();
        Vector3 previousPlayerPosition = playerObject.transform.position;
        try
        {
            EnemyAIController.SetDensityApproachSteeringEnabled(true);
            playerObject.transform.position = Vector3.zero;

            System.Reflection.MethodInfo resolveApproach = typeof(EnemyAIController).GetMethod(
                "ResolveChaseDestination",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            System.Reflection.MethodInfo changeToChase = typeof(EnemyAIController).GetMethod(
                "ChangeToChase",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            System.Reflection.MethodInfo fixedUpdate = typeof(EnemyMovement).GetMethod(
                "FixedUpdate",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (resolveApproach == null || changeToChase == null || fixedUpdate == null)
                throw new System.InvalidOperationException("Density Chase reflection hook missing");

            EnemyAIController primaryAi = null;
            EnemyMovement primaryMovement = null;
            GameObject primaryMonster = null;
            IReadOnlyList<string> allPaths = EnemyStatePatternPrefabFormalizer.TargetPrefabPaths;
            const int firstCorePathIndex = 2;
            int corePathCount = allPaths.Count - firstCorePathIndex;
            for (int i = 0; i < corePathCount; i++)
            {
                string corePath = allPaths[firstCorePathIndex + i];
                float angle = i * Mathf.PI * 2f / corePathCount;
                Vector3 position = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 4f;
                GameObject monster = InstantiateTacticMonster(corePath, position);
                monsters.Add(monster);

                EnemyAIController ai = RequireComponent<EnemyAIController>(monster);
                ai.SetHomePosition(position);
                ai.SetTarget(playerObject.transform);
                if (!ai.UsesDensityApproachSteering)
                    throw new System.InvalidOperationException("Core Murloc density approach is disabled: " + corePath);

                Vector3 destination = (Vector3)resolveApproach.Invoke(ai, null);
                Vector3 delta = destination - position;
                delta.y = 0f;
                if (!ai.IsDensityApproachActive
                    || delta.magnitude < EnemyApproachSteering.MinimumShortHorizonDistance - 0.001f
                    || delta.magnitude > EnemyApproachSteering.MaximumShortHorizonDistance + 0.001f)
                {
                    throw new System.InvalidOperationException(
                        "Core Murloc did not use reservation-free short steering path=" + corePath
                        + " active=" + ai.IsDensityApproachActive
                        + " distance=" + delta.magnitude);
                }

                if (i == 0)
                {
                    primaryMonster = monster;
                    primaryAi = ai;
                    primaryMovement = RequireComponent<EnemyMovement>(monster);
                }
            }

            string[] optOutPaths =
            {
                "Assets/ProjectOverburst/Resources/Enemies/PF_StageMonster.prefab",
                "Assets/ProjectOverburst/Resources/Enemies/PF_StageMonster_FishmanTest.prefab"
            };
            for (int i = 0; i < optOutPaths.Length; i++)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(optOutPaths[i]);
                EnemyAIController ai = prefab != null ? prefab.GetComponent<EnemyAIController>() : null;
                if (ai == null || ai.UsesDensityApproachSteering)
                    throw new System.InvalidOperationException("Non-core density opt-out mismatch: " + optOutPaths[i]);
            }

            primaryAi.SetDensityApproachSteeringOptIn(false);
            Vector3 individualOffDestination = (Vector3)resolveApproach.Invoke(primaryAi, null);
            Vector3 individualOffDelta = individualOffDestination - primaryMonster.transform.position;
            individualOffDelta.y = 0f;
            if (primaryAi.UsesDensityApproachSteering
                || primaryAi.IsDensityApproachActive
                || individualOffDelta.magnitude <= EnemyApproachSteering.MaximumShortHorizonDistance)
            {
                throw new System.InvalidOperationException("Core Murloc individual density OFF did not use natural approach");
            }

            primaryAi.SetDensityApproachSteeringOptIn(true);
            resolveApproach.Invoke(primaryAi, null);
            if (!primaryAi.UsesDensityApproachSteering || !primaryAi.IsDensityApproachActive)
                throw new System.InvalidOperationException("Core Murloc individual density ON did not restore steering");

            int lockedTurnSign = primaryAi.CurrentDensityApproachTurnSign;
            resolveApproach.Invoke(primaryAi, null);
            if (lockedTurnSign == 0 || primaryAi.CurrentDensityApproachTurnSign != lockedTurnSign)
                throw new System.InvalidOperationException("Density Chase turn lock changed inside one decision interval");

            EnemyAIController.SetDensityApproachSteeringEnabled(false);
            Vector3 rollbackDestination = (Vector3)resolveApproach.Invoke(primaryAi, null);
            Vector3 rollbackDelta = rollbackDestination - primaryMonster.transform.position;
            rollbackDelta.y = 0f;
            if (primaryAi.UsesDensityApproachSteering
                || primaryAi.IsDensityApproachActive
                || rollbackDelta.magnitude <= EnemyApproachSteering.MaximumShortHorizonDistance)
            {
                throw new System.InvalidOperationException(
                    "Density global rollback did not restore reservation-free natural approach active="
                    + primaryAi.IsDensityApproachActive
                    + " distance=" + rollbackDelta.magnitude);
            }

            EnemyAIController.SetDensityApproachSteeringEnabled(true);
            resolveApproach.Invoke(primaryAi, null);
            if (!primaryAi.IsDensityApproachActive)
                throw new System.InvalidOperationException("Density steering did not resume after restoring the global switch");

            changeToChase.Invoke(primaryAi, null);
            EnemyLocomotionMode expectedMode = primaryAi.TargetDistance >= primaryAi.BehaviorProfile.RunApproachMinDistance
                ? EnemyLocomotionMode.Run
                : EnemyLocomotionMode.Walk;
            RequireMovingLocomotion(primaryMovement, "Density Chase");
            if (primaryMovement.LocomotionMode != expectedMode || primaryMovement.ActiveMoveSpeed <= 0f)
            {
                throw new System.InvalidOperationException(
                    "Density Chase movement mode or animation speed contract failed expected=" + expectedMode
                    + " actual=" + primaryMovement.LocomotionMode
                    + " speed=" + primaryMovement.ActiveMoveSpeed);
            }

            Vector3 movementStart = primaryMonster.transform.position;
            fixedUpdate.Invoke(primaryMovement, null);
            Vector3 moved = primaryMonster.transform.position - movementStart;
            moved.y = 0f;
            if (moved.sqrMagnitude <= 0.000001f)
                throw new System.InvalidOperationException("Density Chase locomotion command produced no movement");
            RequireMovingLocomotion(primaryMovement, "Density Chase actual movement");

            primaryMonster.transform.position = Vector3.forward * 1.5f;
            primaryAi.SetHomePosition(primaryMonster.transform.position);
            Physics.SyncTransforms();
            InvokeAIUpdate(primaryAi);
            if (primaryAi.CurrentStateName != "Attack" && primaryAi.CurrentStateName != "CombatWait")
            {
                throw new System.InvalidOperationException(
                    "In-range density Chase did not prioritize Attack state=" + primaryAi.CurrentStateName);
            }
            if (primaryMovement.HasDestination)
                throw new System.InvalidOperationException("In-range density Chase retained its short destination");

            Debug.Log(
                "[EnemyStatePatternPlayModeVerifier] Passed core Murlocs=" + corePathCount
                + " density steering, individual/global natural rollback, locomotion and Attack priority");
        }
        finally
        {
            EnemyAIController.SetDensityApproachSteeringEnabled(true);
            playerObject.transform.position = previousPlayerPosition;
            for (int i = 0; i < monsters.Count; i++)
            {
                if (monsters[i] != null)
                    Object.DestroyImmediate(monsters[i]);
            }
        }
    }

    private static void VerifyLegacySurroundRemovalContract()
    {
        const string plannerPath = "Assets/ProjectOverburst/03_Features/Enemies/Runtime/AI/EnemySurroundRingPlanner.cs";
        if (AssetDatabase.LoadAssetAtPath<MonoScript>(plannerPath) != null)
            throw new System.InvalidOperationException("Legacy surrounding planner asset still exists");

        string[] removedMembers =
        {
            "AdaptiveSurroundingRingsEnabled",
            "useAdaptiveSurroundingRings",
            "UsesAdaptiveSurroundingRings",
            "surroundDirection",
            "surroundRingRadius",
            "currentSurroundRingIndex",
            "currentSurroundInnerCapacity",
            "CurrentSurroundRingIndex",
            "CurrentSurroundInnerCapacity",
            "CurrentSurroundDestination",
            "SetAdaptiveSurroundingRingsEnabled",
            "ClearSurroundPlan"
        };
        System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.DeclaredOnly;
        System.Type controllerType = typeof(EnemyAIController);
        for (int i = 0; i < removedMembers.Length; i++)
        {
            if (controllerType.GetMember(removedMembers[i], flags).Length > 0)
                throw new System.InvalidOperationException("Legacy surrounding member still exists: " + removedMembers[i]);
        }

        Debug.Log("[EnemyStatePatternPlayModeVerifier] Passed legacy surrounding planner, fields and toggle removal");
    }

    private static void VerifyInRangeChaseAttackPriority()
    {
        GameObject monster = null;
        Vector3 previousPlayerPosition = playerObject.transform.position;
        try
        {
            playerObject.transform.position = Vector3.zero;
            monster = InstantiateTacticMonster(
                "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Grunt.prefab",
                Vector3.forward * 1.5f);
            EnemyAIController ai = RequireComponent<EnemyAIController>(monster);
            EnemyMovement movement = RequireComponent<EnemyMovement>(monster);
            ai.SetHomePosition(monster.transform.position);
            ai.SetTarget(playerObject.transform);

            System.Reflection.MethodInfo changeToChase = typeof(EnemyAIController).GetMethod(
                "ChangeToChase",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (changeToChase == null)
                throw new System.InvalidOperationException("EnemyAIController.ChangeToChase reflection hook missing");

            changeToChase.Invoke(ai, null);
            if (movement.HasDestination)
                throw new System.InvalidOperationException("In-range Chase created a destination before its Attack check");
            InvokeAIUpdate(ai);
            if (ai.CurrentStateName != "Attack" && ai.CurrentStateName != "CombatWait")
            {
                throw new System.InvalidOperationException(
                    "In-range Chase kept moving instead of entering combat. state=" + ai.CurrentStateName);
            }
            if (movement.HasDestination)
                throw new System.InvalidOperationException("In-range Chase retained its approach destination");

            Debug.Log("[EnemyStatePatternPlayModeVerifier] Passed in-range Chase attack priority");
        }
        finally
        {
            playerObject.transform.position = previousPlayerPosition;
            if (monster != null)
                Object.DestroyImmediate(monster);
        }
    }

    private static void VerifyEnemyStateDebugLabel()
    {
        const string monsterPath = "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Grunt.prefab";
        const string hpBarPath = "Assets/ProjectOverburst/Resources/UI/World/MonsterHpBars/PF_EnemyHpBar_Normal.prefab";
        GameObject monster = null;
        GameObject viewObject = null;
        try
        {
            CombatDebugSettings.SetEnemyAiStateDebug(true);
            monster = InstantiateTacticMonster(monsterPath, Vector3.zero);
            EnemyAIController ai = RequireComponent<EnemyAIController>(monster);
            CombatHealth health = RequireComponent<CombatHealth>(monster);
            GameObject viewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(hpBarPath);
            if (viewPrefab == null)
                throw new System.InvalidOperationException("Enemy HP bar debug label prefab is missing");

            viewObject = Object.Instantiate(viewPrefab);
            EnemyHpBarView view = RequireComponent<EnemyHpBarView>(viewObject);
            view.Bind(health);
            view.SetProjectionVisible(true);

            Transform labelTransform = view.transform.Find("StateDebugText");
            TextMeshProUGUI label = labelTransform != null
                ? labelTransform.GetComponent<TextMeshProUGUI>()
                : null;
            CanvasGroup canvasGroup = view.GetComponent<CanvasGroup>();
            if (label == null || !label.gameObject.activeSelf)
                throw new System.InvalidOperationException("Enemy HP bar state debug label was not created");
            if (label.text != ai.CurrentDebugStateName || !label.text.StartsWith("Roam/"))
            {
                throw new System.InvalidOperationException(
                    "Enemy HP bar state debug label mismatch expected=" + ai.CurrentDebugStateName
                    + " actual=" + label.text);
            }
            Color expectedRoamColor = new Color(0.78f, 0.82f, 0.86f, 1f);
            if (!Mathf.Approximately(label.color.r, expectedRoamColor.r)
                || !Mathf.Approximately(label.color.g, expectedRoamColor.g)
                || !Mathf.Approximately(label.color.b, expectedRoamColor.b)
                || !Mathf.Approximately(label.color.a, expectedRoamColor.a))
                throw new System.InvalidOperationException("Enemy HP bar Roam debug color mismatch actual=" + label.color);
            if (canvasGroup == null || canvasGroup.alpha < 0.99f)
                throw new System.InvalidOperationException("Enemy state debug label was hidden with the undamaged HP bar");

            CombatDebugSettings.SetEnemyAiStateDebug(false);
            if (label.gameObject.activeSelf)
                throw new System.InvalidOperationException("Enemy state debug label ignored the global OFF setting");
            if (canvasGroup.alpha > 0.01f)
                throw new System.InvalidOperationException("Undamaged HP bar remained visible after AI state debug OFF");

            CombatDebugSettings.SetEnemyAiStateDebug(true);
            if (!label.gameObject.activeSelf || canvasGroup.alpha < 0.99f)
                throw new System.InvalidOperationException("Enemy state debug label did not return after global ON");

            Debug.Log("[EnemyStatePatternPlayModeVerifier] Passed overhead AI state debug label, state color and global toggle=" + label.text);
        }
        finally
        {
            CombatDebugSettings.SetEnemyAiStateDebug(true);
            if (viewObject != null)
                Object.DestroyImmediate(viewObject);
            if (monster != null)
                Object.DestroyImmediate(monster);
        }
    }

    private static void VerifyBehaviorProfileMigration()
    {
        VerifyBehaviorProfile("Default", EnemyBehaviorTendency.Assault, EnemyRepositionStyle.Backpedal, 6f, false);
        VerifyBehaviorProfile("Murloc_Grunt", EnemyBehaviorTendency.Assault, EnemyRepositionStyle.Dodge, 6f, false);
        VerifyBehaviorProfile("Murloc_Scout", EnemyBehaviorTendency.Disruptor, EnemyRepositionStyle.Dodge, 4f, false);
        VerifyBehaviorProfile("Murloc_Spearling", EnemyBehaviorTendency.Disruptor, EnemyRepositionStyle.Backpedal, 7f, false);
        VerifyBehaviorProfile("Murloc_Guard", EnemyBehaviorTendency.Defender, EnemyRepositionStyle.Backpedal, 8f, true);
        VerifyBehaviorProfile("Murloc_Brute", EnemyBehaviorTendency.Assault, EnemyRepositionStyle.Dodge, 7f, false);
        VerifyBehaviorProfile("Murloc_Warlord", EnemyBehaviorTendency.Defender, EnemyRepositionStyle.Backpedal, 7f, true);
        Debug.Log("[EnemyStatePatternPlayModeVerifier] Passed seven behavior profiles and three-tendency migration");
    }

    private static void VerifyBehaviorProfile(
        string id,
        EnemyBehaviorTendency tendency,
        EnemyRepositionStyle repositionStyle,
        float runApproachMinDistance,
        bool hasShield)
    {
        string path = "Assets/ProjectOverburst/Resources/Enemies/BehaviorProfiles/EBP_" + id + ".asset";
        EnemyBehaviorProfile profile = AssetDatabase.LoadAssetAtPath<EnemyBehaviorProfile>(path);
        if (profile == null)
            throw new System.InvalidOperationException("Behavior profile is missing: " + path);
        if (profile.ProfileId != id
            || profile.Tendency != tendency
            || profile.RepositionStyle != repositionStyle
            || profile.HasShield != hasShield
            || !Mathf.Approximately(profile.RunApproachMinDistance, runApproachMinDistance))
        {
            throw new System.InvalidOperationException("Behavior profile migration mismatch: " + path);
        }
    }

    private static void VerifyTacticalDecisions()
    {
        if (!Mathf.Approximately(
                EnemyAIController.ResolveCombatLoseTargetRange(0),
                EnemyAIController.BaseCombatLoseTargetRange)
            || !Mathf.Approximately(
                EnemyAIController.ResolveCombatLoseTargetRange(999),
                EnemyAIController.BaseCombatLoseTargetRange))
        {
            throw new System.InvalidOperationException("Fixed 30m aggro range contract mismatch");
        }
        if (!Mathf.Approximately(
                EnemySquadPursuitSimulatorWindow.ResolveAggroReleaseDistance(30f),
                30f)
            || EnemySquadPursuitSimulatorWindow.ShouldReleaseAggro(31f * 31f, 30f, 1.99f, 2f)
            || !EnemySquadPursuitSimulatorWindow.ShouldReleaseAggro(31f * 31f, 30f, 2f, 2f)
            || EnemySquadPursuitSimulatorWindow.ShouldReleaseAggro(29f * 29f, 30f, 3f, 2f))
        {
            throw new System.InvalidOperationException("Squad simulator aggro retention contract mismatch");
        }
        if (!EnemySquadPursuitSimulatorWindow.CanDiscoverPlayer(10f * 10f, 10f)
            || EnemySquadPursuitSimulatorWindow.CanDiscoverPlayer(10.01f * 10.01f, 10f))
        {
            throw new System.InvalidOperationException("Squad simulator distance-only aggro acquisition contract mismatch");
        }
        VerifyPartyTargetPhasePolicy();
        Rect simulatorView = new Rect(0f, 0f, 800f, 600f);
        Vector3 simulatorCenter = new Vector3(7f, 0f, -4f);
        Vector3 simulatorWorld = new Vector3(18f, 0f, 13f);
        float simulatorScale = EnemySquadPursuitSimulatorWindow.ResolveViewScale(simulatorView, 50f, 2f);
        Vector2 simulatorCanvas = EnemySquadPursuitSimulatorWindow.WorldToCanvasPoint(
            simulatorView,
            simulatorWorld,
            simulatorCenter,
            simulatorScale);
        Vector3 simulatorRoundTrip = EnemySquadPursuitSimulatorWindow.CanvasToWorldPoint(
            simulatorView,
            simulatorCanvas,
            simulatorCenter,
            simulatorScale);
        if ((simulatorRoundTrip - simulatorWorld).sqrMagnitude > 0.0001f)
            throw new System.InvalidOperationException("Squad simulator zoom and pan transform contract mismatch");

        VerifyRoamPatrolMode();
        VerifyAwarenessDecisions();
        VerifyDistanceBasedChaseDecision();
        VerifyDisruptorSideApproach();
        VerifyLowHealthRepositionDecision();
        VerifyDodgeDecision();
        Debug.Log("[EnemyStatePatternPlayModeVerifier] Passed three-tendency distance-based combat decisions");
    }

    private static void VerifyPartyTargetPhasePolicy()
    {
        const float enemyRadius = 0.4f;
        const float memberRadius = 0.5f;
        const float memberEngageRange = 2.5f;
        float engageCenterRadius = EnemyCombatCoordinator.ResolveMemberEngageCenterRadius(
            enemyRadius,
            memberRadius,
            memberEngageRange);
        if (!Mathf.Approximately(engageCenterRadius, 3.4f)
            || !EnemyCombatCoordinator.IsInsideMemberEngageRange(
                Vector3.zero,
                enemyRadius,
                Vector3.right * engageCenterRadius,
                memberRadius,
                memberEngageRange)
            || EnemyCombatCoordinator.IsInsideMemberEngageRange(
                Vector3.zero,
                enemyRadius,
                Vector3.right * (engageCenterRadius + 0.001f),
                memberRadius,
                memberEngageRange))
        {
            throw new System.InvalidOperationException(
                "MemberEngage orange-zone center radius does not match the runtime surface-distance boundary");
        }

        var farCandidates = new List<EnemyPartyTargetCandidate>
        {
            new EnemyPartyTargetCandidate(0, true, false, false, 7f),
            new EnemyPartyTargetCandidate(1, true, false, false, 8f),
            new EnemyPartyTargetCandidate(2, true, false, false, 9f)
        };
        EnemyPartyTargetDecision farDecision = EnemyCombatCoordinator.ResolvePartyTargetPhase(
            new EnemyPartyTargetDecision(EnemyPartyTargetPhase.None, -1),
            0,
            farCandidates);
        RequirePartyTargetDecision(
            farDecision,
            EnemyPartyTargetPhase.LeaderApproach,
            0,
            "Far party candidates must all approach P1");

        var nearP2Candidates = new List<EnemyPartyTargetCandidate>
        {
            new EnemyPartyTargetCandidate(0, true, false, false, 7f),
            new EnemyPartyTargetCandidate(1, true, true, false, 0.25f),
            new EnemyPartyTargetCandidate(2, true, false, false, 8f)
        };
        EnemyPartyTargetDecision nearP2Decision = EnemyCombatCoordinator.ResolvePartyTargetPhase(
            farDecision,
            0,
            nearP2Candidates);
        RequirePartyTargetDecision(
            nearP2Decision,
            EnemyPartyTargetPhase.MemberEngaged,
            1,
            "Only the enemy inside P2 engage range may lock P2");

        var nearDirectAttackerCandidates = new List<EnemyPartyTargetCandidate>
        {
            new EnemyPartyTargetCandidate(0, true, true, false, 0.1f),
            new EnemyPartyTargetCandidate(1, true, true, true, 0.8f),
            new EnemyPartyTargetCandidate(2, true, true, false, 0.4f)
        };
        EnemyPartyTargetDecision nearDirectAttackerDecision = EnemyCombatCoordinator.ResolvePartyTargetPhase(
            farDecision,
            0,
            nearDirectAttackerCandidates);
        RequirePartyTargetDecision(
            nearDirectAttackerDecision,
            EnemyPartyTargetPhase.MemberEngaged,
            1,
            "An in-range direct attacker must beat closer non-attacker candidates");

        var nearestSurfaceCandidates = new List<EnemyPartyTargetCandidate>
        {
            new EnemyPartyTargetCandidate(0, true, true, false, 1.2f),
            new EnemyPartyTargetCandidate(1, true, true, false, 0.3f),
            new EnemyPartyTargetCandidate(2, true, true, false, 0.8f)
        };
        EnemyPartyTargetDecision nearestSurfaceDecision = EnemyCombatCoordinator.ResolvePartyTargetPhase(
            farDecision,
            0,
            nearestSurfaceCandidates);
        RequirePartyTargetDecision(
            nearestSurfaceDecision,
            EnemyPartyTargetPhase.MemberEngaged,
            1,
            "Without a direct attacker the nearest surface-distance candidate must win");

        var equalDistanceCandidates = new List<EnemyPartyTargetCandidate>
        {
            new EnemyPartyTargetCandidate(2, true, true, false, 0.5f),
            new EnemyPartyTargetCandidate(1, true, true, false, 0.5f),
            new EnemyPartyTargetCandidate(0, true, false, false, 7f)
        };
        EnemyPartyTargetDecision equalDistanceDecision = EnemyCombatCoordinator.ResolvePartyTargetPhase(
            farDecision,
            0,
            equalDistanceCandidates);
        RequirePartyTargetDecision(
            equalDistanceDecision,
            EnemyPartyTargetPhase.MemberEngaged,
            1,
            "Equal surface-distance candidates must use the lowest MemberIndex");

        var farDirectAttackerCandidates = new List<EnemyPartyTargetCandidate>
        {
            new EnemyPartyTargetCandidate(0, true, false, false, 7f),
            new EnemyPartyTargetCandidate(1, true, false, true, 8f),
            new EnemyPartyTargetCandidate(2, true, false, false, 9f)
        };
        EnemyPartyTargetDecision farDirectAttackerDecision = EnemyCombatCoordinator.ResolvePartyTargetPhase(
            farDecision,
            0,
            farDirectAttackerCandidates);
        RequirePartyTargetDecision(
            farDirectAttackerDecision,
            EnemyPartyTargetPhase.LeaderApproach,
            0,
            "A direct attacker outside engage range must not replace P1");

        EnemyPartyTargetDecision invalidLock = new EnemyPartyTargetDecision(
            EnemyPartyTargetPhase.MemberEngaged,
            1);
        var fallbackNearCandidates = new List<EnemyPartyTargetCandidate>
        {
            new EnemyPartyTargetCandidate(0, true, false, false, 7f),
            new EnemyPartyTargetCandidate(1, false, false, false, float.PositiveInfinity),
            new EnemyPartyTargetCandidate(2, true, true, false, 0.4f)
        };
        EnemyPartyTargetDecision nearFallbackDecision = EnemyCombatCoordinator.ResolvePartyTargetPhase(
            invalidLock,
            0,
            fallbackNearCandidates);
        RequirePartyTargetDecision(
            nearFallbackDecision,
            EnemyPartyTargetPhase.MemberEngaged,
            2,
            "An invalid lock must prefer another nearby party member");

        var invalidNoNearCandidates = new List<EnemyPartyTargetCandidate>
        {
            new EnemyPartyTargetCandidate(0, true, false, false, 7f),
            new EnemyPartyTargetCandidate(1, false, false, false, float.PositiveInfinity),
            new EnemyPartyTargetCandidate(2, true, false, false, 9f)
        };
        EnemyPartyTargetDecision leaderFallbackDecision = EnemyCombatCoordinator.ResolvePartyTargetPhase(
            invalidLock,
            2,
            invalidNoNearCandidates);
        RequirePartyTargetDecision(
            leaderFallbackDecision,
            EnemyPartyTargetPhase.LeaderApproach,
            2,
            "An invalid lock without a nearby candidate must use the current leader");

        EnemyPartyTargetDecision reboundLeaderDecision = EnemyCombatCoordinator.ResolvePartyTargetPhase(
            farDecision,
            2,
            farCandidates);
        RequirePartyTargetDecision(
            reboundLeaderDecision,
            EnemyPartyTargetPhase.LeaderApproach,
            2,
            "Leader change must rebind LeaderApproach");

        EnemyPartyTargetDecision preservedLockDecision = EnemyCombatCoordinator.ResolvePartyTargetPhase(
            nearP2Decision,
            2,
            nearP2Candidates);
        RequirePartyTargetDecision(
            preservedLockDecision,
            EnemyPartyTargetPhase.MemberEngaged,
            1,
            "Leader change must preserve a valid MemberEngaged lock");

        Debug.Log(
            "[EnemyStatePatternPlayModeVerifier] Passed party target direct-attacker, surface-distance and MemberIndex priority");
    }

    private static void RequirePartyTargetDecision(
        EnemyPartyTargetDecision decision,
        EnemyPartyTargetPhase expectedPhase,
        int expectedMemberIndex,
        string message)
    {
        if (decision.Phase != expectedPhase || decision.TargetMemberIndex != expectedMemberIndex)
        {
            throw new System.InvalidOperationException(
                message
                + ". phase=" + decision.Phase
                + " member=" + decision.TargetMemberIndex);
        }
    }

    private static void VerifyCombatCoordination()
    {
        const int enemyCount = 10;
        PlayerContext context = PlayerContext.GetOrCreate();
        if (context == null)
            throw new System.InvalidOperationException("PlayerContext is required");
        PlayerActorRuntime previousActor = context.CurrentActor;
        GameObject partyLeader = null;
        GameObject nonActorService = null;
        List<GameObject> monsters = new List<GameObject>(enemyCount);
        try
        {
            partyLeader = CreateVerificationPlayerActor("EnemyAI_VerificationPlayer", 0, Vector3.zero);
            context.Bind(partyLeader.GetComponent<PlayerActorRuntime>());
            Physics.SyncTransforms();
            System.Reflection.MethodInfo refreshPartyTargetPhase = typeof(EnemyAIController).GetMethod(
                "RefreshPartyTargetPhase",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (refreshPartyTargetPhase == null)
                throw new System.InvalidOperationException("EnemyAIController.RefreshPartyTargetPhase reflection hook missing");

            nonActorService = new GameObject("EnemyAI_VerificationNonActorService");
            nonActorService.transform.position = Vector3.forward * 0.1f;
            nonActorService.AddComponent<CapsuleCollider>();
            nonActorService.AddComponent<CombatHealth>();
            CombatTarget.EnsureConfigured(nonActorService, CombatTeam.PlayerParty);

            for (int i = 0; i < enemyCount; i++)
            {
                float angle = i * Mathf.PI * 2f / enemyCount;
                Vector3 position = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 8f;
                GameObject monster = InstantiateTacticMonster(
                    "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Grunt.prefab",
                    position);
                monsters.Add(monster);

                EnemyAIController ai = RequireComponent<EnemyAIController>(monster);
                ai.RequestAggro(partyLeader.transform);
                RequireRuntimePartyTarget(
                    ai,
                    partyLeader.transform,
                    EnemyPartyTargetPhase.LeaderApproach,
                    "Far enemy did not start from P1 LeaderApproach index=" + i);
            }

            EnemyAIController farDamagedAi = RequireComponent<EnemyAIController>(monsters[enemyCount / 2]);
            CombatHealth farDamagedHealth = RequireComponent<CombatHealth>(farDamagedAi.gameObject);
            farDamagedHealth.TakeDamage(new DamageInfo(
                1f,
                farDamagedAi.transform.position,
                partyLeader,
                Vector3.forward));
            RequireRuntimePartyTarget(
                farDamagedAi,
                partyLeader.transform,
                EnemyPartyTargetPhase.LeaderApproach,
                "A far direct attacker replaced the current leader");

            EnemyAIController damagedAi = RequireComponent<EnemyAIController>(monsters[0]);
            partyLeader.transform.position = damagedAi.transform.position + Vector3.right * 0.25f;
            Physics.SyncTransforms();
            CombatHealth damagedHealth = RequireComponent<CombatHealth>(damagedAi.gameObject);
            damagedHealth.TakeDamage(new DamageInfo(
                1f,
                damagedAi.transform.position,
                partyLeader,
                Vector3.forward));
            RequireRuntimePartyTarget(
                damagedAi,
                partyLeader.transform,
                EnemyPartyTargetPhase.MemberEngaged,
                "The nearby player was not acquired as an individual engagement");

            CombatHealth playerHealth = RequireComponent<CombatHealth>(partyLeader);
            playerHealth.TakeDamage(new DamageInfo(playerHealth.MaxHp + 1f,
                partyLeader.transform.position, null, Vector3.forward));
            refreshPartyTargetPhase.Invoke(damagedAi, null);
            if (EnemyCombatCoordinator.GetPartyTargetPhase(damagedAi) == EnemyPartyTargetPhase.MemberEngaged)
                throw new System.InvalidOperationException("Dead player retained its engagement lock");
            playerHealth.ResetHealth();
            partyLeader.transform.position = Vector3.zero;
            Physics.SyncTransforms();
            foreach (GameObject monster in monsters)
            {
                EnemyAIController ai = RequireComponent<EnemyAIController>(monster);
                ai.RequestAggro(partyLeader.transform);
                RequireRuntimePartyTarget(ai, partyLeader.transform,
                    EnemyPartyTargetPhase.LeaderApproach, "Reset player did not restore the approach target");
            }

            System.Reflection.MethodInfo resolveApproach = typeof(EnemyAIController).GetMethod(
                "ResolveChaseDestination",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (resolveApproach == null)
                throw new System.InvalidOperationException("EnemyAIController.ResolveChaseDestination reflection hook missing");
            System.Reflection.FieldInfo nextDirectionRefresh = typeof(EnemyAIController).GetField(
                "nextApproachDirectionRefreshTime",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (nextDirectionRefresh == null)
                throw new System.InvalidOperationException("EnemyAIController approach direction timer reflection hook missing");

            List<Vector3> destinations = new List<Vector3>(enemyCount);
            for (int i = 0; i < monsters.Count; i++)
            {
                EnemyAIController ai = RequireComponent<EnemyAIController>(monsters[i]);
                ai.SetTarget(partyLeader.transform);
                Vector3 destination = (Vector3)resolveApproach.Invoke(ai, null);
                float radius = Vector3.Distance(partyLeader.transform.position, destination);
                float expectedRadius = ai.BehaviorProfile.PreferredApproachDistance;
                if (ai.IsDensityApproachActive
                    || Mathf.Abs(radius - expectedRadius) > 0.01f)
                {
                    throw new System.InvalidOperationException(
                        "Long approach did not use the reservation-free natural path active="
                        + ai.IsDensityApproachActive
                        + " expectedRadius=" + expectedRadius
                        + " actualRadius=" + radius);
                }
                float remainingDirectionTime = (float)nextDirectionRefresh.GetValue(ai) - Time.time;
                if (remainingDirectionTime < ai.BehaviorProfile.ApproachDirectionMinDuration - 0.05f
                    || remainingDirectionTime > ai.BehaviorProfile.ApproachDirectionMaxDuration + 0.05f)
                {
                    throw new System.InvalidOperationException(
                        "Approach direction was not held for the configured duration. remaining=" + remainingDirectionTime);
                }

                for (int j = 0; j < destinations.Count; j++)
                {
                    if ((destinations[j] - destination).sqrMagnitude <= 0.0001f)
                        throw new System.InvalidOperationException("Natural approach produced an overlapping destination");
                }
                destinations.Add(destination);
            }

            partyLeader.transform.rotation = Quaternion.Euler(0f, 137f, 0f);
            for (int i = 0; i < monsters.Count; i++)
            {
                EnemyAIController ai = RequireComponent<EnemyAIController>(monsters[i]);
                Vector3 rotatedTargetDestination = (Vector3)resolveApproach.Invoke(ai, null);
                if ((destinations[i] - rotatedTargetDestination).sqrMagnitude > 0.0001f)
                    throw new System.InvalidOperationException("World approach direction changed when only the target rotated");
            }

            Debug.Log(
                "[EnemyStatePatternPlayModeVerifier] Passed player aggro, local engagement, death-reset lock cleanup and reservation-free long approach count="
                + destinations.Count);
        }
        finally
        {
            for (int i = 0; i < monsters.Count; i++)
            {
                if (monsters[i] != null)
                    Object.DestroyImmediate(monsters[i]);
            }
            if (nonActorService != null)
                Object.DestroyImmediate(nonActorService);
            context.Bind(previousActor);
            if (partyLeader != null)
                Object.DestroyImmediate(partyLeader);
        }
    }

    private static GameObject CreateVerificationPlayerActor(string objectName, int memberIndex, Vector3 position)
    {
        GameObject actor = new GameObject(objectName);
        actor.transform.position = position;
        CapsuleCollider collider = actor.AddComponent<CapsuleCollider>();
        collider.radius = 0.4f;
        collider.height = 2f;
        collider.center = new Vector3(0f, 1f, 0f);
        actor.AddComponent<CombatHealth>();
        PlayerActorRuntime actorRuntime = actor.AddComponent<PlayerActorRuntime>();
        actorRuntime.Initialize(memberIndex, objectName, "P" + (memberIndex + 1));
        CombatTarget.EnsureConfigured(actor, CombatTeam.PlayerParty);
        return actor;
    }



    private static void RequireRuntimePartyTarget(
        EnemyAIController ai,
        Transform expectedTarget,
        EnemyPartyTargetPhase expectedPhase,
        string message)
    {
        EnemyPartyTargetPhase actualPhase = EnemyCombatCoordinator.GetPartyTargetPhase(ai);
        if (ai == null || ai.Target != expectedTarget || actualPhase != expectedPhase)
        {
            throw new System.InvalidOperationException(
                message
                + ". target=" + (ai != null && ai.Target != null ? ai.Target.name : "null")
                + " phase=" + actualPhase);
        }
    }

    private static void VerifyGroupAttackRhythm()
    {
        const int enemyCount = 5;
        GameObject targetActor = null;
        EnemyBehaviorProfile profile = null;
        List<GameObject> monsters = new List<GameObject>(enemyCount);
        try
        {
            targetActor = new GameObject("EnemyAI_AttackRhythmTarget");
            CapsuleCollider targetCollider = targetActor.AddComponent<CapsuleCollider>();
            targetCollider.radius = 0.4f;
            targetCollider.height = 2f;
            targetCollider.center = new Vector3(0f, 1f, 0f);
            targetActor.AddComponent<CombatHealth>();
            targetActor.AddComponent<PlayerActorRuntime>();
            CombatTarget.EnsureConfigured(targetActor, CombatTeam.PlayerParty);

            profile = CreateTacticProfile(EnemyBehaviorTendency.Assault, EnemyRepositionStyle.Backpedal, 0.5f);
            profile.ConfigureAttackRhythm(1f, 0.4f);
            System.Reflection.MethodInfo evaluate = typeof(EnemyAIController).GetMethod(
                "ResolveCombatWaitDecision",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            System.Reflection.MethodInfo changeToCombatWait = typeof(EnemyAIController).GetMethod(
                "ChangeToCombatWait",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (evaluate == null || changeToCombatWait == null)
                throw new System.InvalidOperationException("Attack rhythm state reflection hooks missing");

            int attackCount = 0;
            int waitCount = 0;
            EnemyAIController firstAttacker = null;
            for (int i = 0; i < enemyCount; i++)
            {
                float angle = i * Mathf.PI * 2f / enemyCount;
                Vector3 position = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 1.5f;
                GameObject monster = InstantiateTacticMonster(
                    "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Grunt.prefab",
                    position);
                monsters.Add(monster);

                EnemyAIController ai = RequireComponent<EnemyAIController>(monster);
                ai.SetBehaviorProfile(profile);
                ai.SetHomePosition(position);
                ai.SetTarget(targetActor.transform);
                evaluate.Invoke(ai, null);
                if (ai.CurrentStateName == "Attack")
                {
                    attackCount++;
                    firstAttacker = ai;
                }
                else if (ai.CurrentStateName == "CombatWait")
                {
                    waitCount++;
                }
                else
                {
                    throw new System.InvalidOperationException("Close-range group selected unexpected state=" + ai.CurrentStateName);
                }
            }

            if (attackCount != enemyCount || waitCount != 0 || firstAttacker == null)
                throw new System.InvalidOperationException("Immediate unlimited attack entry mismatch. attack=" + attackCount + " wait=" + waitCount);

            changeToCombatWait.Invoke(firstAttacker, new object[] { 0f });
            evaluate.Invoke(firstAttacker, null);
            if (firstAttacker.CurrentStateName != "CombatWait")
                throw new System.InvalidOperationException("Finished attacker ignored its reentry cooldown");

            Debug.Log("[EnemyStatePatternPlayModeVerifier] Passed immediate unlimited attack entry and individual reentry cooldown");
        }
        finally
        {
            for (int i = 0; i < monsters.Count; i++)
            {
                if (monsters[i] != null)
                    Object.DestroyImmediate(monsters[i]);
            }
            if (profile != null)
                Object.DestroyImmediate(profile);
            if (targetActor != null)
                Object.DestroyImmediate(targetActor);
        }
    }

    private static void VerifyRoamPatrolMode()
    {
        GameObject monster = InstantiateTacticMonster("Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Scout.prefab", Vector3.zero);
        EnemyBehaviorProfile profile = CreateTacticProfile(EnemyBehaviorTendency.Disruptor, EnemyRepositionStyle.Dodge, 1f);
        profile.ConfigurePeace(1f, 5f, 0.7f);
        try
        {
            EnemyAIController ai = RequireComponent<EnemyAIController>(monster);
            EnemyMovement movement = RequireComponent<EnemyMovement>(monster);
            playerObject.transform.position = Vector3.forward * 20f;

            ai.enabled = false;
            ai.SetBehaviorProfile(profile);
            ai.SetHomePosition(ai.transform.position);
            ai.SetTarget(playerObject.transform);
            ai.enabled = true;

            if (ai.CurrentStateName != "Roam")
                throw new System.InvalidOperationException("Patrol chance 100% did not keep the Roam state. state=" + ai.CurrentStateName);
            if (!movement.HasDestination || movement.LocomotionMode != EnemyLocomotionMode.Walk)
                throw new System.InvalidOperationException("Patrol did not start Walk movement. mode=" + movement.LocomotionMode);

            float expectedSpeed = movement.MoveSpeed * profile.PatrolSpeedMultiplier;
            if (Mathf.Abs(movement.ActiveMoveSpeed - expectedSpeed) > 0.01f)
                throw new System.InvalidOperationException(
                    "Patrol speed multiplier mismatch. expected=" + expectedSpeed + " actual=" + movement.ActiveMoveSpeed);

            Debug.Log("[EnemyStatePatternPlayModeVerifier] Passed Roam idle/patrol modes and slow movement contract");
        }
        finally
        {
            playerObject.transform.rotation = Quaternion.identity;
            Object.DestroyImmediate(profile);
            Object.DestroyImmediate(monster);
        }
    }

    private static void VerifyAwarenessDecisions()
    {
        VerifyOutsideDetectionRangeRemainsRoam();
        VerifyInsideDetectionRangeStartsChase();
        VerifyOcclusionDoesNotBlockDistanceAggro();
        VerifyDiscoverySupportCall();
        Debug.Log("[EnemyStatePatternPlayModeVerifier] Passed distance-only aggro and support contracts");
    }

    private static void VerifyOutsideDetectionRangeRemainsRoam()
    {
        GameObject monster = InstantiateTacticMonster("Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Grunt.prefab", Vector3.zero);
        EnemyBehaviorProfile profile = CreateAwarenessProfile(0.55f, 12f);
        try
        {
            EnemyAIController ai = PrepareAwarenessMonster(monster, profile, Vector3.forward * 12f);
            InvokeRoamAwareness(ai);
            if (ai.CurrentStateName != "Roam")
                throw new System.InvalidOperationException("Target outside detection range must remain Roam. state=" + ai.CurrentStateName);
        }
        finally
        {
            Object.DestroyImmediate(profile);
            Object.DestroyImmediate(monster);
        }
    }

    private static void VerifyInsideDetectionRangeStartsChase()
    {
        GameObject monster = InstantiateTacticMonster("Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Grunt.prefab", Vector3.zero);
        EnemyBehaviorProfile profile = CreateAwarenessProfile(0.55f, 12f);
        try
        {
            EnemyAIController ai = PrepareAwarenessMonster(monster, profile, Vector3.forward * 8f);
            InvokeRoamAwareness(ai);
            if (ai.CurrentStateName != "Chase")
                throw new System.InvalidOperationException("Target inside detection range must enter Chase. state=" + ai.CurrentStateName);
        }
        finally
        {
            Object.DestroyImmediate(profile);
            Object.DestroyImmediate(monster);
        }
    }

    private static void VerifyDiscoverySupportCall()
    {
        GameObject caller = InstantiateTacticMonster("Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Grunt.prefab", Vector3.zero);
        GameObject receiver = InstantiateTacticMonster("Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Guard.prefab", Vector3.right * 3f);
        EnemyBehaviorProfile callerProfile = CreateAwarenessProfile(0.55f, 12f);
        EnemyBehaviorProfile receiverProfile = CreateAwarenessProfile(0.5f, 12f);
        try
        {
            Vector3 playerPosition = Vector3.forward * 4f;
            EnemyAIController callerAi = PrepareAwarenessMonster(caller, callerProfile, playerPosition);
            EnemyAIController receiverAi = PrepareAwarenessMonster(receiver, receiverProfile, playerPosition);
            InvokeRoamAwareness(callerAi);

            if (callerAi.CurrentStateName != "Chase")
                throw new System.InvalidOperationException("Direct discovery did not select Chase alert mode. state=" + callerAi.CurrentStateName);
            if (receiverAi.CurrentStateName != "Chase")
                throw new System.InvalidOperationException(
                    "Support receiver did not enter Chase immediately. state=" + receiverAi.CurrentStateName);
        }
        finally
        {
            Object.DestroyImmediate(callerProfile);
            Object.DestroyImmediate(receiverProfile);
            Object.DestroyImmediate(caller);
            Object.DestroyImmediate(receiver);
        }
    }

    private static void VerifyOcclusionDoesNotBlockDistanceAggro()
    {
        GameObject monster = InstantiateTacticMonster("Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Grunt.prefab", Vector3.zero);
        GameObject obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        EnemyBehaviorProfile profile = CreateAwarenessProfile(0.55f, 12f);
        try
        {
            obstacle.name = "EnemyAI_AwarenessOccluder";
            obstacle.transform.position = new Vector3(0f, 1f, 2f);
            obstacle.transform.localScale = new Vector3(2f, 2f, 0.5f);
            EnemyAIController ai = PrepareAwarenessMonster(monster, profile, Vector3.forward * 4f);
            Physics.SyncTransforms();
            InvokeRoamAwareness(ai);
            if (ai.CurrentStateName != "Chase")
                throw new System.InvalidOperationException(
                    "Occluded target inside detection range must enter Chase. state=" + ai.CurrentStateName);
        }
        finally
        {
            Object.DestroyImmediate(profile);
            Object.DestroyImmediate(obstacle);
            Object.DestroyImmediate(monster);
        }
    }

    private static EnemyBehaviorProfile CreateAwarenessProfile(float investigateSpeedMultiplier, float supportRange)
    {
        EnemyBehaviorProfile profile = CreateTacticProfile(EnemyBehaviorTendency.Assault, EnemyRepositionStyle.Backpedal, 0.5f);
        profile.ConfigurePeace(0f, 4f, 0.65f);
        profile.ConfigureAwareness(14f, 10f, 6f, 360f, 0.5f, 3f, 2.5f, investigateSpeedMultiplier, supportRange);
        return profile;
    }

    private static EnemyAIController PrepareAwarenessMonster(
        GameObject monster,
        EnemyBehaviorProfile profile,
        Vector3 playerPosition)
    {
        EnemyAIController ai = RequireComponent<EnemyAIController>(monster);
        ai.enabled = false;
        ai.SetBehaviorProfile(profile);
        ai.SetTarget(playerObject.transform);
        monster.transform.rotation = Quaternion.identity;
        playerObject.transform.position = playerPosition;
        Physics.SyncTransforms();
        ai.enabled = true;
        ai.SetHomePosition(monster.transform.position);
        return ai;
    }

    private static void InvokeRoamAwareness(EnemyAIController ai)
    {
        System.Reflection.MethodInfo evaluate = typeof(EnemyAIController).GetMethod(
            "TryEvaluateRoamAwareness",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (evaluate == null)
            throw new System.InvalidOperationException("EnemyAIController.TryEvaluateRoamAwareness reflection hook missing");
        evaluate.Invoke(ai, null);
    }

    private static void VerifyRunUsesInPlaceClip()
    {
        const string controllerPath = "Assets/ProjectOverburst/03_Features/Enemies/Animations/Fishman/AC_Enemy_Fishman.controller";
        const string inPlaceWalkPath = "Assets/ProjectOverburst/03_Features/Enemies/Animations/Fishman/InPlace/Fishman_Walk_InPlace.anim";
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        AnimationClip inPlaceWalk = AssetDatabase.LoadAssetAtPath<AnimationClip>(inPlaceWalkPath);
        if (controller == null || inPlaceWalk == null)
            throw new System.InvalidOperationException("Fishman InPlace locomotion assets are missing");

        VerifyInPlaceTranslationCurves(inPlaceWalk);
        bool runFound = false;
        AnimatorControllerLayer[] layers = controller.layers;
        for (int layerIndex = 0; layerIndex < layers.Length; layerIndex++)
        {
            ChildAnimatorState[] states = layers[layerIndex].stateMachine.states;
            for (int stateIndex = 0; stateIndex < states.Length; stateIndex++)
            {
                if (states[stateIndex].state == null
                    || states[stateIndex].state.name != "Locomotion"
                    || !(states[stateIndex].state.motion is BlendTree blendTree))
                    continue;

                ChildMotion[] children = blendTree.children;
                for (int childIndex = 0; childIndex < children.Length; childIndex++)
                {
                    if (!Mathf.Approximately(children[childIndex].threshold, 2f))
                        continue;

                    runFound = true;
                    if (children[childIndex].motion != inPlaceWalk)
                        throw new System.InvalidOperationException("Run must reuse the verified Fishman InPlace Walk clip");
                }
            }
        }

        if (!runFound)
            throw new System.InvalidOperationException("Fishman Locomotion Run threshold is missing");

        Debug.Log("[EnemyStatePatternPlayModeVerifier] Passed Run InPlace clip contract");
    }

    private static void VerifyInPlaceTranslationCurves(AnimationClip clip)
    {
        EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
        for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
        {
            EditorCurveBinding binding = bindings[bindingIndex];
            if (!IsRootXZTranslation(binding))
                continue;

            AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null || curve.length <= 1)
                continue;

            float minimum = float.PositiveInfinity;
            float maximum = float.NegativeInfinity;
            for (int keyIndex = 0; keyIndex < curve.length; keyIndex++)
            {
                minimum = Mathf.Min(minimum, curve.keys[keyIndex].value);
                maximum = Mathf.Max(maximum, curve.keys[keyIndex].value);
            }

            if (maximum - minimum > 0.001f)
                throw new System.InvalidOperationException(
                    "Fishman InPlace clip contains rollback translation. path=" + binding.path + " property=" + binding.propertyName);
        }
    }

    private static bool IsRootXZTranslation(EditorCurveBinding binding)
    {
        string propertyName = binding.propertyName;
        bool isXZ = propertyName.EndsWith(".x", System.StringComparison.Ordinal)
            || propertyName.EndsWith(".z", System.StringComparison.Ordinal);
        if (!isXZ)
            return false;
        if (propertyName.StartsWith("RootT.", System.StringComparison.Ordinal)
            || propertyName.StartsWith("MotionT.", System.StringComparison.Ordinal))
            return true;
        if (propertyName != "m_LocalPosition.x" && propertyName != "m_LocalPosition.z")
            return false;

        string normalizedPath = (binding.path ?? string.Empty).Replace("\\", "/").ToLowerInvariant();
        int slashIndex = normalizedPath.LastIndexOf('/');
        string leaf = slashIndex >= 0 ? normalizedPath.Substring(slashIndex + 1) : normalizedPath;
        return string.IsNullOrEmpty(leaf)
            || leaf == "rig"
            || leaf == "root"
            || leaf == "armature"
            || leaf == "hips"
            || leaf.Contains("root")
            || leaf.Contains("hip");
    }

    private static void VerifyDistanceBasedChaseDecision()
    {
        GameObject monster = InstantiateTacticMonster("Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Brute.prefab", Vector3.zero);
        EnemyBehaviorProfile profile = CreateTacticProfile(EnemyBehaviorTendency.Assault, EnemyRepositionStyle.Backpedal, 0.5f);
        profile.ConfigureRunApproach(4f);
        try
        {
            EnemyAIController ai = RequireComponent<EnemyAIController>(monster);
            EnemyMovement movement = RequireComponent<EnemyMovement>(monster);
            playerObject.transform.position = Vector3.forward * 8f;
            PrepareTacticDecision(ai, profile);
            if (ai.CurrentStateName != "Chase" || movement.LocomotionMode != EnemyLocomotionMode.Run)
                throw new System.InvalidOperationException("Far chase did not select Run. state=" + ai.CurrentStateName + " mode=" + movement.LocomotionMode);

            playerObject.transform.position = Vector3.forward * 3f;
            Physics.SyncTransforms();
            InvokeAIUpdate(ai);
            if (movement.LocomotionMode != EnemyLocomotionMode.Walk)
                throw new System.InvalidOperationException("Near chase did not return from Run to Walk. mode=" + movement.LocomotionMode);
        }
        finally
        {
            Object.DestroyImmediate(profile);
            Object.DestroyImmediate(monster);
        }
    }

    private static void VerifyDisruptorSideApproach()
    {
        GameObject monster = InstantiateTacticMonster("Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Scout.prefab", Vector3.zero);
        EnemyBehaviorProfile profile = CreateTacticProfile(EnemyBehaviorTendency.Disruptor, EnemyRepositionStyle.Backpedal, 0.5f);
        profile.ConfigureRunApproach(10f);
        profile.ConfigureApproach(1.35f, 60f, 1.5f, 1f);
        try
        {
            EnemyAIController ai = RequireComponent<EnemyAIController>(monster);
            EnemyMovement movement = RequireComponent<EnemyMovement>(monster);
            playerObject.transform.position = Vector3.forward * 4f;
            PrepareTacticDecision(ai, profile);
            if (ai.CurrentStateName != "Chase" || movement.LocomotionMode != EnemyLocomotionMode.Walk)
                throw new System.InvalidOperationException("Disruptor did not start side-biased Walk approach. state=" + ai.CurrentStateName);

            System.Reflection.MethodInfo resolveApproach = typeof(EnemyAIController).GetMethod(
                "ResolveChaseDestination",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (resolveApproach == null)
                throw new System.InvalidOperationException("EnemyAIController.ResolveChaseDestination reflection hook missing");

            Vector3 beforeTargetRotation = (Vector3)resolveApproach.Invoke(ai, null);
            Vector3 approachOffset = beforeTargetRotation - playerObject.transform.position;
            if (Mathf.Abs(approachOffset.x) < 0.25f)
                throw new System.InvalidOperationException("Disruptor approach did not include a side offset");

            playerObject.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            Vector3 afterTargetRotation = (Vector3)resolveApproach.Invoke(ai, null);
            if ((beforeTargetRotation - afterTargetRotation).sqrMagnitude > 0.0001f)
                throw new System.InvalidOperationException("World side approach changed when only the player rotated");
        }
        finally
        {
            playerObject.transform.rotation = Quaternion.identity;
            Object.DestroyImmediate(profile);
            Object.DestroyImmediate(monster);
        }
    }

    private static void VerifyLowHealthRepositionDecision()
    {
        GameObject monster = InstantiateTacticMonster("Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Spearling.prefab", Vector3.zero);
        EnemyBehaviorProfile profile = CreateTacticProfile(
            EnemyBehaviorTendency.Disruptor,
            EnemyRepositionStyle.Backpedal,
            0.5f,
            0.5f);
        try
        {
            EnemyAIController ai = RequireComponent<EnemyAIController>(monster);
            EnemyMovement movement = RequireComponent<EnemyMovement>(monster);
            CombatHealth health = RequireComponent<CombatHealth>(monster);
            health.TakeDamage(new DamageInfo(health.MaxHp * 0.75f, monster.transform.position, null, Vector3.back));
            playerObject.transform.position = Vector3.forward * 1.5f;
            PrepareTacticDecision(ai, profile);
            if (ai.CurrentStateName != "Reposition" || movement.LocomotionMode != EnemyLocomotionMode.Backpedal)
                throw new System.InvalidOperationException(
                    "Low-health CombatWait decision did not select Reposition backpedal. state=" + ai.CurrentStateName + " mode=" + movement.LocomotionMode);
        }
        finally
        {
            Object.DestroyImmediate(profile);
            Object.DestroyImmediate(monster);
        }
    }

    private static void VerifyDodgeDecision()
    {
        VerifyDodgeDecision(
            "Murloc_Grunt",
            "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Grunt.prefab");
        VerifyDodgeDecision(
            "Murloc_Scout",
            "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Scout.prefab");
        VerifyDodgeDecision(
            "Murloc_Brute",
            "Assets/ProjectOverburst/Resources/Enemies/Murloc/PF_StageMonster_Murloc_Brute.prefab");
    }

    private static void VerifyDodgeDecision(string id, string prefabPath)
    {
        GameObject monster = InstantiateTacticMonster(prefabPath, Vector3.zero);
        EnemyBehaviorProfile sourceProfile = AssetDatabase.LoadAssetAtPath<EnemyBehaviorProfile>(
            "Assets/ProjectOverburst/Resources/Enemies/BehaviorProfiles/EBP_" + id + ".asset");
        if (sourceProfile == null)
            throw new System.InvalidOperationException("Dodge verification profile missing: " + id);
        EnemyBehaviorProfile profile = Object.Instantiate(sourceProfile);
        profile.ConfigureDodgeLunge(
            sourceProfile.DodgeLungeMinDistance,
            sourceProfile.DodgeLungeMaxDistance,
            sourceProfile.DodgeLungeDistance,
            sourceProfile.DodgeLungeDuration,
            sourceProfile.DodgeLungeCooldown,
            1f,
            sourceProfile.DodgeVisualHeight);

        try
        {
            EnemyAIController ai = RequireComponent<EnemyAIController>(monster);
            EnemyMovement movement = RequireComponent<EnemyMovement>(monster);
            float triggerDistance = (profile.DodgeLungeMinDistance + profile.DodgeLungeMaxDistance) * 0.5f;
            playerObject.transform.position = Vector3.forward * triggerDistance;
            PrepareTacticDecision(ai, profile);
            InvokeAIUpdate(ai);
            if (ai.CurrentStateName != "Reposition" || movement.LocomotionMode != EnemyLocomotionMode.Dodge)
                throw new System.InvalidOperationException(id + " did not select forward Dodge lunge. state=" + ai.CurrentStateName + " mode=" + movement.LocomotionMode);
            if (movement.ActiveMoveSpeed <= movement.MoveSpeed || movement.ActiveMoveSpeed > 2.5f)
                throw new System.InvalidOperationException(id + " Dodge speed is outside the readable range. walk=" + movement.MoveSpeed + " dodge=" + movement.ActiveMoveSpeed);
        }
        finally
        {
            Object.DestroyImmediate(profile);
            Object.DestroyImmediate(monster);
        }
    }

    private static GameObject InstantiateTacticMonster(string path, Vector3 position)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
            throw new System.InvalidOperationException("Tactic verification prefab missing: " + path);

        GameObject monster = Object.Instantiate(prefab, position, Quaternion.identity);
        PrepareBody(monster);
        return monster;
    }

    private static EnemyBehaviorProfile CreateTacticProfile(
        EnemyBehaviorTendency tendency,
        EnemyRepositionStyle repositionStyle,
        float preferredMinDistance,
        float lowHealthThreshold = 0f)
    {
        EnemyBehaviorProfile profile = ScriptableObject.CreateInstance<EnemyBehaviorProfile>();
        profile.Configure(
            "TacticVerification",
            tendency,
            repositionStyle,
            0f,
            false,
            0.1f,
            preferredMinDistance,
            1.5f,
            0.5f,
            lowHealthThreshold,
            2f,
            0.7f,
            false,
            0f,
            0.5f,
            1f,
            120f);
        profile.ConfigureRunApproach(6f);
        profile.ConfigureApproach(1.35f, 15f, 1.5f, 1f);
        profile.ConfigureAttackRhythm(1f, 0.45f);
        return profile;
    }

    private static void PrepareTacticDecision(EnemyAIController ai, EnemyBehaviorProfile profile)
    {
        ai.SetBehaviorProfile(profile);
        ai.SetHomePosition(ai.transform.position);
        ai.SetTarget(playerObject.transform);
        System.Reflection.MethodInfo evaluate = typeof(EnemyAIController).GetMethod(
            "ResolveCombatWaitDecision",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (evaluate == null)
            throw new System.InvalidOperationException("EnemyAIController.ResolveCombatWaitDecision reflection hook missing");
        evaluate.Invoke(ai, null);
        if (ai.CurrentStateName != "Chase")
            return;

        InvokeAIUpdate(ai);
    }

    private static void InvokeAIUpdate(EnemyAIController ai)
    {
        System.Reflection.MethodInfo update = typeof(EnemyAIController).GetMethod(
            "Update",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (update == null)
            throw new System.InvalidOperationException("EnemyAIController.Update reflection hook missing");
        update.Invoke(ai, null);
    }

    private static void SetupHitReactionVerification()
    {
        if (monsterObject != null)
            Object.DestroyImmediate(monsterObject);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyStatePatternPrefabFormalizer.TargetPrefabPaths[0]);
        if (prefab == null)
            throw new System.InvalidOperationException("Hit reaction verification prefab is missing");

        playerObject.transform.position = new Vector3(0f, 0f, 1.5f);
        monsterObject = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
        PrepareBody(monsterObject);
        Physics.SyncTransforms();

        monsterAI = RequireComponent<EnemyAIController>(monsterObject);
        monsterMovement = RequireComponent<EnemyMovement>(monsterObject);
        monsterMovementReaction = RequireComponent<EnemyMovementReaction>(monsterObject);
        monsterAttack = RequireComponent<EnemyMeleeAttackController>(monsterObject);
        reactionBody = RequireComponent<Rigidbody>(monsterObject);
        monsterAI.enabled = false; // 반응 자체만 독립 검증

        if (!monsterAttack.TryStartAttack(playerObject.transform))
            throw new System.InvalidOperationException("Could not start attack for hit-stun cancellation verification");
        if (!monsterAttack.IsAttacking)
            throw new System.InvalidOperationException("Attack routine was not active before hit stun");

        monsterMovementReaction.ApplyHitStun(0.25f);
        if (monsterAttack.IsAttacking)
            throw new System.InvalidOperationException("Hit stun did not cancel the active attack");

        WaitFor(VerifyStep.CheckHitStunEnd, 1, 0.3f);
    }

    private static void SetupGroupAggroVerification()
    {
        if (monsterObject != null)
            Object.DestroyImmediate(monsterObject);

        playerHealth.ResetHealth();
        playerObject.transform.position = new Vector3(0f, 0f, 20f);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyStatePatternPrefabFormalizer.TargetPrefabPaths[2]);
        if (prefab == null)
            throw new System.InvalidOperationException("Group aggro verification prefab is missing");

        groupMonsterA = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
        groupMonsterB = Object.Instantiate(prefab, new Vector3(0f, 0f, -2f), Quaternion.identity);
        PrepareBody(groupMonsterA);
        PrepareBody(groupMonsterB);

        groupAiA = RequireComponent<EnemyAIController>(groupMonsterA);
        groupAiB = RequireComponent<EnemyAIController>(groupMonsterB);
        groupHealthA = RequireComponent<CombatHealth>(groupMonsterA);
        groupHealthB = RequireComponent<CombatHealth>(groupMonsterB);
        groupAiA.SetHomePosition(groupMonsterA.transform.position);
        groupAiB.SetHomePosition(groupMonsterB.transform.position);
        groupAiA.SetTarget(playerObject.transform);
        groupAiB.SetTarget(playerObject.transform);

        groupAreaObject = new GameObject("EnemyAI_VerificationCombatArea");
        groupAiA.SetSquadEncounter(
            groupAreaObject,
            playerObject.transform);
        groupAiB.SetSquadEncounter(
            groupAreaObject,
            playerObject.transform);
        Physics.SyncTransforms();
        WaitFor(VerifyStep.CheckGroupRoam, 4, 0.1f);
    }

    private static void SetupRepeatedHitAnimationVerification()
    {
        if (monsterObject != null)
            Object.DestroyImmediate(monsterObject);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyStatePatternPrefabFormalizer.TargetPrefabPaths[1]);
        if (prefab == null)
            throw new System.InvalidOperationException("Repeated hit animation verification prefab is missing");

        monsterObject = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
        PrepareBody(monsterObject);
        EnemyAIController ai = RequireComponent<EnemyAIController>(monsterObject);
        ai.enabled = false;
        reactionAnimationBridge = RequireComponent<EnemyAnimationBridge>(monsterObject);
        reactionAnimator = monsterObject.GetComponentInChildren<Animator>(true);
        if (reactionAnimator == null)
            throw new System.InvalidOperationException("Repeated hit animation verification Animator is missing");

        reactionAnimationBridge.PlayHit();
        WaitFor(VerifyStep.CheckHitAnimationStarted, 2, 0.08f);
    }

    private static bool TryGetHitAnimationNormalizedTime(Animator animator, out float normalizedTime)
    {
        normalizedTime = 0f;
        if (animator == null)
            return false;

        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
        if (currentState.IsName("Get_hit") || currentState.IsName("Base Layer.Get_hit"))
        {
            normalizedTime = currentState.normalizedTime;
            return true;
        }

        if (!animator.IsInTransition(0))
            return false;

        AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(0);
        if (!nextState.IsName("Get_hit") && !nextState.IsName("Base Layer.Get_hit"))
            return false;

        normalizedTime = nextState.normalizedTime;
        return true;
    }

    private static void VerifyHideoutDebugSpawnerToggle()
    {
        if (groupMonsterA != null)
            Object.DestroyImmediate(groupMonsterA);
        if (groupMonsterB != null)
            Object.DestroyImmediate(groupMonsterB);
        if (groupAreaObject != null)
            Object.DestroyImmediate(groupAreaObject);

        playerHealth.ResetHealth();
        playerObject.transform.position = Vector3.zero;

        GameObject spawnerObject = new GameObject("EnemyAI_VerificationHideoutDebugSpawner");
        HideoutMonsterSpawnDebugController spawnPoint = spawnerObject.AddComponent<HideoutMonsterSpawnDebugController>();
        GameObject enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyStatePatternPrefabFormalizer.TargetPrefabPaths[0]);
        SerializedObject serializedSpawner = new SerializedObject(spawnPoint);
        SerializedProperty spawnConfig = serializedSpawner.FindProperty("spawnConfig");
        MurlocSpawnPackConfigBuilder.ConfigureSharedSpawnConfig(spawnConfig);
        VerifyConfiguredSpawnPacks(spawnConfig, "Hideout");
        spawnConfig.FindPropertyRelative("detectionRange").floatValue = 10f;
        spawnConfig.FindPropertyRelative("stopDistance").floatValue = 1.5f;
        serializedSpawner.FindProperty("spawnRadius").floatValue = 20f;
        serializedSpawner.ApplyModifiedPropertiesWithoutUndo();

        CombatDebugSettings.SetHideoutMonsterSpawn(false);
        if (spawnPoint.TrySpawnPackNow() != 0)
            throw new System.InvalidOperationException("Hideout debug spawner created monsters while toggle was off");

        CombatDebugSettings.SetHideoutMonsterSpawn(true);
        int firstSpawned = spawnPoint.TrySpawnPackNow();
        if (firstSpawned <= 0)
            throw new System.InvalidOperationException("Hideout debug spawner did not create a configured spawn pack");

        int firstChildCount = spawnerObject.transform.childCount;
        int secondSpawned = spawnPoint.TrySpawnPackNow();
        if (secondSpawned <= 0 || spawnerObject.transform.childCount <= firstChildCount)
            throw new System.InvalidOperationException("Hideout debug spawner did not continue without a spawn cap");

        int childCount = spawnerObject.transform.childCount;
        CombatDebugSettings.SetHideoutMonsterSpawn(false);
        if (spawnPoint.TrySpawnPackNow() != 0 || spawnerObject.transform.childCount != childCount)
            throw new System.InvalidOperationException("Hideout debug spawner did not stop cleanly");

        for (int i = 0; i < spawnerObject.transform.childCount; i++)
        {
            GameObject spawnedMonster = spawnerObject.transform.GetChild(i).gameObject;
            RequireComponent<EnemyAIController>(spawnedMonster);
            RequireComponent<EnemySensor>(spawnedMonster);
            RequireComponent<EnemyMovement>(spawnedMonster);
            RequireComponent<EnemyMeleeAttackController>(spawnedMonster);
            RequireComponent<EnemyLootDropper>(spawnedMonster);
        }
        Object.DestroyImmediate(spawnerObject);
        Debug.Log("[EnemyStatePatternPlayModeVerifier] Passed Hideout five-pack unlimited spawn toggle on/off");
    }

    private static void MovePlayerAndWait(Vector3 position, VerifyStep nextStep, int frames, float seconds)
    {
        playerObject.transform.position = position;
        Physics.SyncTransforms();
        WaitFor(nextStep, frames, seconds);
    }

    private static void VerifyConfiguredSpawnPacks(SerializedProperty spawnConfig, string context)
    {
        if (spawnConfig == null)
            throw new System.InvalidOperationException(context + " spawn config is missing");

        SerializedProperty enemyPrefabs = spawnConfig.FindPropertyRelative("enemyPrefabs");
        if (enemyPrefabs == null || !enemyPrefabs.isArray || enemyPrefabs.arraySize != 6)
            throw new System.InvalidOperationException(context + " expected six core murloc prefabs");

        string[] expectedPackIds =
        {
            "Murloc_BasicMob",
            "Murloc_ScoutPack",
            "Murloc_ShieldLine",
            "Murloc_BruteRaid",
            "Murloc_WarlordEscort"
        };
        SerializedProperty packs = spawnConfig.FindPropertyRelative("spawnPacks");
        if (packs == null || !packs.isArray || packs.arraySize != expectedPackIds.Length)
            throw new System.InvalidOperationException(context + " expected five spawn packs");

        for (int packIndex = 0; packIndex < packs.arraySize; packIndex++)
        {
            SerializedProperty pack = packs.GetArrayElementAtIndex(packIndex);
            string packId = pack.FindPropertyRelative("packId")?.stringValue;
            SerializedProperty entries = pack.FindPropertyRelative("entries");
            if (packId != expectedPackIds[packIndex]
                || entries == null
                || !entries.isArray
                || entries.arraySize <= 0)
            {
                throw new System.InvalidOperationException(context + " spawn pack contract mismatch: " + packIndex);
            }

            for (int entryIndex = 0; entryIndex < entries.arraySize; entryIndex++)
            {
                SerializedProperty prefab = entries.GetArrayElementAtIndex(entryIndex).FindPropertyRelative("prefab");
                if (prefab == null || prefab.objectReferenceValue == null)
                    throw new System.InvalidOperationException(context + " spawn pack prefab is missing: " + packId);
            }
        }

        if (spawnConfig.FindPropertyRelative("respawnWaveCount")?.intValue != 6
            || !Mathf.Approximately(spawnConfig.FindPropertyRelative("respawnWaveInterval")?.floatValue ?? 0f, 5f))
        {
            throw new System.InvalidOperationException(context + " respawn sequence contract mismatch");
        }
    }

    private static void WaitFor(VerifyStep nextStep, int frames, float seconds)
    {
        step = nextStep;
        waitUntilFrame = Time.frameCount + Mathf.Max(1, frames);
        waitUntilTime = Time.time + Mathf.Max(0f, seconds);
    }

    private static void CheckState(string expected)
    {
        string actual = monsterAI != null ? monsterAI.CurrentStateName : "MissingAI";
        if (actual != expected)
            throw new System.InvalidOperationException(CurrentPath() + " expected state=" + expected + " actual=" + actual);
    }

    private static void RequireMovingLocomotion(EnemyMovement movement, string context)
    {
        if (movement == null
            || !movement.HasDestination
            || movement.LocomotionMode == EnemyLocomotionMode.Idle
            || Mathf.Abs(movement.MovementAnimationAmount) < 0.1f)
        {
            string mode = movement != null ? movement.LocomotionMode.ToString() : "Missing";
            float amount = movement != null ? movement.MovementAnimationAmount : 0f;
            throw new System.InvalidOperationException(
                context + " moved without locomotion. mode=" + mode + " amount=" + amount);
        }
    }

    private static void CheckStateAny(params string[] expectedStates)
    {
        string actual = monsterAI != null ? monsterAI.CurrentStateName : "MissingAI";
        for (int i = 0; i < expectedStates.Length; i++)
        {
            if (actual == expectedStates[i])
                return;
        }

        throw new System.InvalidOperationException(CurrentPath() + " expected one of=" + string.Join(",", expectedStates) + " actual=" + actual);
    }

    private static void CheckGroupState(EnemyAIController ai, string expected, string label)
    {
        string actual = ai != null ? ai.CurrentStateName : "MissingAI";
        if (actual != expected)
            throw new System.InvalidOperationException(label + " expected state=" + expected + " actual=" + actual);
    }

    private static void CheckGroupStateAny(EnemyAIController ai, string label, params string[] expectedStates)
    {
        string actual = ai != null ? ai.CurrentStateName : "MissingAI";
        for (int i = 0; i < expectedStates.Length; i++)
        {
            if (actual == expectedStates[i])
                return;
        }

        throw new System.InvalidOperationException(label + " expected one of=" + string.Join(",", expectedStates) + " actual=" + actual);
    }

    private static void PrepareBody(GameObject target)
    {
        Rigidbody body = target.GetComponent<Rigidbody>();
        if (body == null)
            return;

        body.useGravity = false;
        body.isKinematic = true;
    }

    private static void CleanupBeforeStressVerification()
    {
        EnemyAIController[] existing = Object.FindObjectsByType<EnemyAIController>(FindObjectsSortMode.None);
        for (int i = 0; i < existing.Length; i++)
        {
            if (existing[i] != null)
                Object.DestroyImmediate(existing[i].gameObject);
        }

        if (groupAreaObject != null)
            Object.DestroyImmediate(groupAreaObject);

        if (stressCameraObject == null)
        {
            stressCameraObject = new GameObject("EnemyAI_StressCamera");
            stressCameraObject.tag = "MainCamera";
            Camera camera = stressCameraObject.AddComponent<Camera>();
            camera.enabled = true;
            stressCameraObject.transform.position = new Vector3(0f, 8f, 0f);
            stressCameraObject.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
        }
    }

    private static void SetupAiStressCase(int count)
    {
        CleanupStressMonsters();
        stressMonsters.Capacity = Mathf.Max(stressMonsters.Capacity, count);
        stressAgents.Capacity = Mathf.Max(stressAgents.Capacity, count);

        for (int i = 0; i < count; i++)
        {
            GameObject monster = new GameObject("EnemyAI_Stress_" + count + "_" + i);
            monster.SetActive(false);
            Vector3 position = new Vector3(80f + (i % 20) * 1.5f, 0f, (i / 20) * 1.5f);
            monster.transform.position = position;
            EnemyAIController ai = monster.AddComponent<EnemyAIController>();
            Rigidbody body = monster.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.useGravity = false;
                body.isKinematic = true;
            }

            monster.SetActive(true);
            ai.Configure(playerObject.transform, position, 10f, 1.5f);
            EnemyMovement movement = monster.GetComponent<EnemyMovement>();
            if (movement != null)
                movement.enabled = false; // AI 판단 부하만 측정
            stressMonsters.Add(monster);
            stressAgents.Add(ai);
        }

        stressStartTime = Time.time;
        stressStartTickCount = SumStressTicks();
        stressStartSkippedCount = SumStressSkippedUpdates();
    }

    private static void CheckAiStressCase(int expectedCount, float expectedInterval)
    {
        if (stressAgents.Count != expectedCount)
            throw new System.InvalidOperationException("AI stress agent count mismatch expected=" + expectedCount + " actual=" + stressAgents.Count);
        if (EnemyAIController.ActiveEnemyCount != expectedCount)
            throw new System.InvalidOperationException("Active enemy count mismatch expected=" + expectedCount + " actual=" + EnemyAIController.ActiveEnemyCount);
        if (EnemyCrowdService.RegisteredCount != expectedCount)
            throw new System.InvalidOperationException("Crowd registration count mismatch expected=" + expectedCount + " actual=" + EnemyCrowdService.RegisteredCount);

        int tickCount = SumStressTicks() - stressStartTickCount;
        int skippedCount = SumStressSkippedUpdates() - stressStartSkippedCount;
        float elapsed = Mathf.Max(0.01f, Time.time - stressStartTime);
        if (tickCount <= 0)
            throw new System.InvalidOperationException(expectedCount + "-enemy stress produced no AI ticks");

        for (int i = 0; i < stressAgents.Count; i++)
        {
            EnemyAIController ai = stressAgents[i];
            if (ai == null)
                throw new System.InvalidOperationException(expectedCount + "-enemy stress lost an AI agent");
            if (Mathf.Abs(ai.CurrentAiTickInterval - expectedInterval) > 0.001f)
            {
                throw new System.InvalidOperationException(
                    expectedCount + "-enemy AI interval mismatch expected=" + expectedInterval + " actual=" + ai.CurrentAiTickInterval);
            }
        }

        if (expectedInterval <= 0f && skippedCount != 0)
            throw new System.InvalidOperationException("40-enemy full-rate stress unexpectedly skipped AI updates");
        if (expectedInterval > 0f && skippedCount <= tickCount)
            throw new System.InvalidOperationException(expectedCount + "-enemy stress did not reduce enough AI updates");

        Debug.Log(
            "[EnemyStatePatternPlayModeVerifier] Passed AI stress count=" + expectedCount
            + " interval=" + expectedInterval.ToString("F2")
            + " elapsed=" + elapsed.ToString("F2")
            + " ticks=" + tickCount
            + " skipped=" + skippedCount);
    }

    private static int SumStressTicks()
    {
        int sum = 0;
        for (int i = 0; i < stressAgents.Count; i++)
        {
            if (stressAgents[i] != null)
                sum += stressAgents[i].AiTickCount;
        }
        return sum;
    }

    private static int SumStressSkippedUpdates()
    {
        int sum = 0;
        for (int i = 0; i < stressAgents.Count; i++)
        {
            if (stressAgents[i] != null)
                sum += stressAgents[i].AiSkippedUpdateCount;
        }
        return sum;
    }

    private static void CleanupStressMonsters()
    {
        for (int i = 0; i < stressMonsters.Count; i++)
        {
            if (stressMonsters[i] != null)
                Object.DestroyImmediate(stressMonsters[i]);
        }
        stressMonsters.Clear();
        stressAgents.Clear();
    }

    private static void ApplyVerificationBehavior(EnemyAIController ai)
    {
        if (ai == null)
            return;

        if (verificationBehaviorProfile == null)
        {
            verificationBehaviorProfile = ScriptableObject.CreateInstance<EnemyBehaviorProfile>();
            verificationBehaviorProfile.hideFlags = HideFlags.HideAndDontSave;
            verificationBehaviorProfile.Configure(
                "PlayModeVerification",
                EnemyBehaviorTendency.Assault,
                EnemyRepositionStyle.Backpedal,
                0.05f,
                false,
                0.12f,
                0f,
                1f,
                0.4f,
                0f,
                1.5f,
                0.6f,
                false,
                0f,
                0.8f,
                1f,
                120f);
            verificationBehaviorProfile.ConfigurePeace(0f, 4f, 0.65f);
            verificationBehaviorProfile.ConfigureAwareness(14f, 10f, 6f, 180f, 0.5f, 3f, 2.5f, 0.55f, 12f);
            verificationBehaviorProfile.ConfigureRunApproach(6f);
            verificationBehaviorProfile.ConfigureApproach(1.35f, 15f, 1.5f, 1f);
            verificationBehaviorProfile.ConfigureAttackRhythm(1f, 0.45f);
        }

        bool wasEnabled = ai.enabled;
        if (wasEnabled)
            ai.enabled = false;
        ai.SetBehaviorProfile(verificationBehaviorProfile);
        if (wasEnabled)
            ai.enabled = true;
    }

    private static T RequireComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        if (component == null)
            throw new System.InvalidOperationException(CurrentPath() + " missing " + typeof(T).Name);
        return component;
    }

    private static string CurrentPath()
    {
        IReadOnlyList<string> paths = EnemyStatePatternPrefabFormalizer.TargetPrefabPaths;
        return prefabIndex >= 0 && prefabIndex < paths.Count ? paths[prefabIndex] : "<complete>";
    }

    private static void CompleteSuccessfully()
    {
        if (passedPrefabs.Count != EnemyStatePatternPrefabFormalizer.TargetPrefabPaths.Count)
        {
            Fail(new System.InvalidOperationException(
                "Expected " + EnemyStatePatternPrefabFormalizer.TargetPrefabPaths.Count + " passed prefabs but got " + passedPrefabs.Count));
            return;
        }

        Debug.Log("[EnemyStatePatternPlayModeVerifier] Passed all prefabs=" + passedPrefabs.Count);
        SessionState.SetInt(ExitCodeKey, 0);
        EditorApplication.update -= UpdateVerification;
        EditorApplication.ExitPlaymode();
    }

    private static void Fail(System.Exception exception)
    {
        Debug.LogException(exception);
        SessionState.SetInt(ExitCodeKey, 1);
        EditorApplication.update -= UpdateVerification;
        if (EditorApplication.isPlaying)
            EditorApplication.ExitPlaymode();
        else if (SessionState.GetBool(BatchKey, false))
            EditorApplication.Exit(1);
    }
}
