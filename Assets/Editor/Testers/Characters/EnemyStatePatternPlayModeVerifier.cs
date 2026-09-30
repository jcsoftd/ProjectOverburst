using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// 2026-10-01: 옛 머록·StageMonster 픽스처와 그 전용 값 검사를 정리했다. 픽스처 없는 AI 계약은 EnemyAiContractVerifier로 옮겼고,
// 남은 Play 검사(상태 순환·피격 반응·그룹 어그로·조향 통합·부하)는 현재 테마 몬스터를 픽스처로 쓴다.
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

    // 2026-10-01: 옛 머록·StageMonster 프리팹(188dcdb 삭제) 대신 현재 테마 몬스터를 픽스처로 쓴다.
    // 이 목록은 검증 픽스처일 뿐 제품 자산을 바꾸지 않는다.
    private const string FixtureMeleePath = "Assets/ProjectOverburst/Resources/Enemies/Themes/Actors/PF_CavernMutants_Ceratoferox.prefab";
    private const string FixtureMediumPath = "Assets/ProjectOverburst/Resources/Enemies/Themes/Actors/PF_CavernMutants_Gasterobrach.prefab";
    private static readonly string[] FixturePrefabPaths = { FixtureMeleePath, FixtureMediumPath };

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


    private static void Begin(bool batchMode)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new System.InvalidOperationException("PlayMode is already active or changing.");
        // 2026-10-01: 빈 씬을 Single로 만들므로 다른 작업의 저장 안 된 씬이 있으면 닫기 전에 멈춘다.
        EditorSceneSafety.RequireNoUnsavedScenes("Verify Enemy State Pattern PlayMode");

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
                    // The fixture's first impact can occur several seconds after range entry.
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
        IReadOnlyList<string> paths = FixturePrefabPaths;
        if (prefabIndex >= paths.Count)
        {
            VerifyLocomotionModes();
            VerifyEnemyCollisionPolicy();
            EnemyAiContractVerifier.RunAll();
            VerifyTacticalDecisions();
            VerifyCombatCoordination();
            VerifyGroupAttackRhythm();
            VerifyWalkableAndApproachContracts();
            VerifyNarrowCorridorCrowdContract();
            VerifyClusterFanOutIntegrationContract();
            VerifyChaseBypassIntegrationContract();
            VerifyFlowFieldChaseIntegrationContract();
            VerifyEncirclementDestinationSpread();
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


    private static void VerifyLocomotionModes()
    {
        string scoutPath = FixtureMeleePath;
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
            first = InstantiateTacticMonster(FixtureMeleePath, Vector3.zero);
            second = InstantiateTacticMonster(FixtureMeleePath, Vector3.zero);
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
                FixtureMeleePath,
                firstPosition);
            second = InstantiateTacticMonster(
                FixtureMeleePath,
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
                    FixtureMeleePath,
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












    private static void VerifyClusterFanOutIntegrationContract()
    {
        string gruntPath = FixtureMeleePath;
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
        string gruntPath = FixtureMeleePath;
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


    private static void VerifyFlowFieldChaseIntegrationContract()
    {
        string gruntPath = FixtureMeleePath;
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



    private static void VerifyInRangeChaseAttackPriority()
    {
        GameObject monster = null;
        Vector3 previousPlayerPosition = playerObject.transform.position;
        try
        {
            playerObject.transform.position = Vector3.zero;
            monster = InstantiateTacticMonster(
                FixtureMeleePath,
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
        string monsterPath = FixtureMeleePath;
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
        Debug.Log("[EnemyStatePatternPlayModeVerifier] Passed three-tendency distance-based combat decisions");
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
                    FixtureMeleePath,
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
                    FixtureMeleePath,
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
        GameObject monster = InstantiateTacticMonster(FixtureMeleePath, Vector3.zero);
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
        GameObject monster = InstantiateTacticMonster(FixtureMeleePath, Vector3.zero);
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
        GameObject monster = InstantiateTacticMonster(FixtureMeleePath, Vector3.zero);
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
        GameObject caller = InstantiateTacticMonster(FixtureMeleePath, Vector3.zero);
        GameObject receiver = InstantiateTacticMonster(FixtureMediumPath, Vector3.right * 3f);
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
        GameObject monster = InstantiateTacticMonster(FixtureMeleePath, Vector3.zero);
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
        GameObject monster = InstantiateTacticMonster(FixtureMediumPath, Vector3.zero);
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
        GameObject monster = InstantiateTacticMonster(FixtureMeleePath, Vector3.zero);
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
        GameObject monster = InstantiateTacticMonster(FixtureMeleePath, Vector3.zero);
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

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FixturePrefabPaths[0]);
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

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FixturePrefabPaths[0]);
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

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FixturePrefabPaths[1]);
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


    private static void MovePlayerAndWait(Vector3 position, VerifyStep nextStep, int frames, float seconds)
    {
        playerObject.transform.position = position;
        Physics.SyncTransforms();
        WaitFor(nextStep, frames, seconds);
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
        IReadOnlyList<string> paths = FixturePrefabPaths;
        return prefabIndex >= 0 && prefabIndex < paths.Count ? paths[prefabIndex] : "<complete>";
    }

    private static void CompleteSuccessfully()
    {
        if (passedPrefabs.Count != FixturePrefabPaths.Length)
        {
            Fail(new System.InvalidOperationException(
                "Expected " + FixturePrefabPaths.Length + " passed prefabs but got " + passedPrefabs.Count));
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
