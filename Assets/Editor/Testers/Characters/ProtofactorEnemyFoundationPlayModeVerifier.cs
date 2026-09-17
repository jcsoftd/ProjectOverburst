using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ProtofactorEnemyFoundationPlayModeVerifier
{
    private const string ActiveKey = "ProtofactorEnemyFoundationPlayModeVerifier.Active";
    private const string BatchKey = "ProtofactorEnemyFoundationPlayModeVerifier.Batch";
    private const string ExitCodeKey = "ProtofactorEnemyFoundationPlayModeVerifier.ExitCode";
    private const string PreviousSceneKey = "ProtofactorEnemyFoundationPlayModeVerifier.PreviousScene";
    private const string CatalogResourcePath = "Enemies/Protofactor/Catalogs/EC_ProtofactorPilot";
    private const string DefinitionId = "Ceratoferox_Normal";

    private enum VerifyStep
    {
        None,
        Setup,
        VerifySpawnAndContext,
        VerifyReuseAndAttackCommit,
        WaitForDeathReturn,
        WaitForExternalDisableReturn
    }

    private static VerifyStep step;
    private static int waitUntilFrame;
    private static float timeoutAt;
    private static GameObject fixtureRoot;
    private static Transform target;
    private static EnemyDefinition definition;
    private static EnemySpawnService spawnService;
    private static EnemyPoolService poolService;
    private static EnemyActor actor;

    static ProtofactorEnemyFoundationPlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    [MenuItem("OVERBURST/Codex/Validation/Verify Protofactor Enemy Foundation PlayMode")]
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
            throw new InvalidOperationException("PlayMode가 이미 실행 중이거나 전환 중입니다.");

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid() && activeScene.isDirty)
            throw new InvalidOperationException("저장되지 않은 현재 씬이 있어 빈 검증 씬으로 전환하지 않습니다.");

        SessionState.SetBool(ActiveKey, true);
        SessionState.SetBool(BatchKey, batchMode);
        SessionState.SetInt(ExitCodeKey, 1);
        SessionState.SetString(
            PreviousSceneKey,
            activeScene.IsValid() ? activeScene.path : string.Empty);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(ActiveKey, false))
            return;

        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            step = VerifyStep.Setup;
            WaitFrames(2);
            EditorApplication.update -= UpdateVerification;
            EditorApplication.update += UpdateVerification;
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
        string previousScene = SessionState.GetString(PreviousSceneKey, string.Empty);
        ClearSessionState();

        if (!batchMode
            && !string.IsNullOrWhiteSpace(previousScene)
            && System.IO.File.Exists(previousScene))
        {
            EditorSceneManager.OpenScene(previousScene, OpenSceneMode.Single);
        }

        if (batchMode)
            EditorApplication.Exit(exitCode);
        else if (exitCode == 0)
            Debug.Log("[ProtofactorEnemyFoundationPlayModeVerifier] 검증을 완료했습니다.");
        else
            Debug.LogError("[ProtofactorEnemyFoundationPlayModeVerifier] 검증에 실패했습니다.");
    }

    private static void UpdateVerification()
    {
        if (!EditorApplication.isPlaying || Time.frameCount < waitUntilFrame)
            return;

        try
        {
            switch (step)
            {
                case VerifyStep.Setup:
                    SetupFixture();
                    step = VerifyStep.VerifySpawnAndContext;
                    WaitFrames(2);
                    break;

                case VerifyStep.VerifySpawnAndContext:
                    VerifySpawnContract(actor);
                    ConfigureTransientPresentation(actor);
                    spawnService.Release(actor);
                    VerifyInactiveAndReset(actor, "explicit release");
                    step = VerifyStep.VerifyReuseAndAttackCommit;
                    WaitFrames(1);
                    break;

                case VerifyStep.VerifyReuseAndAttackCommit:
                    VerifyReuseAndAttackCommit();
                    KillAndWaitForPoolReturn();
                    step = VerifyStep.WaitForDeathReturn;
                    waitUntilFrame = Time.frameCount + 1;
                    break;

                case VerifyStep.WaitForDeathReturn:
                    if (!actor.gameObject.activeSelf)
                    {
                        VerifyInactiveAndReset(actor, "death release");
                        actor = Spawn(Vector3.zero, null);
                        actor.gameObject.SetActive(false);
                        step = VerifyStep.WaitForExternalDisableReturn;
                        WaitFrames(3);
                        break;
                    }

                    if (Time.realtimeSinceStartup >= timeoutAt)
                        throw new InvalidOperationException("사망 애니메이션 이후 Actor가 풀로 반환되지 않았습니다.");
                    break;

                case VerifyStep.WaitForExternalDisableReturn:
                    VerifyInactiveAndReset(actor, "external disable");
                    Require(poolService.LeasedCount == 0, "최종 Leased Actor가 남았습니다.");
                    CompleteSuccessfully();
                    break;
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            FailAndExit();
        }
    }

    private static void SetupFixture()
    {
        EnemyCatalog catalog = Resources.Load<EnemyCatalog>(CatalogResourcePath);
        Require(catalog != null, "Protofactor Pilot EnemyCatalog을 찾지 못했습니다.");
        Require(catalog.Validate(out string catalogMessage), catalogMessage);
        Require(catalog.TryGet(DefinitionId, out definition), DefinitionId + " Definition이 없습니다.");

        fixtureRoot = new GameObject("ProtofactorEnemyFoundationFixture");
        GameObject inactivePool = new GameObject("InactivePool");
        inactivePool.transform.SetParent(fixtureRoot.transform, false);
        inactivePool.SetActive(false);

        GameObject serviceObject = new GameObject("EnemySpawnService");
        serviceObject.transform.SetParent(fixtureRoot.transform, false);
        poolService = serviceObject.AddComponent<EnemyPoolService>();
        poolService.Configure(inactivePool.transform, 0);
        spawnService = serviceObject.AddComponent<EnemySpawnService>();
        spawnService.Configure(catalog, poolService);
        Require(spawnService.Validate(out string serviceMessage), serviceMessage);

        GameObject duplicateObject = new GameObject("DuplicateEnemySpawnService");
        duplicateObject.transform.SetParent(fixtureRoot.transform, false);
        EnemySpawnService duplicate = duplicateObject.AddComponent<EnemySpawnService>();
        duplicate.Configure(catalog, poolService);
        Require(EnemySpawnService.Current == spawnService, "중복 SpawnService가 Current를 덮어썼습니다.");
        Require(!duplicate.enabled, "중복 SpawnService가 활성 상태로 남았습니다.");

        GameObject targetObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        targetObject.name = "PlayerTarget";
        targetObject.transform.SetParent(fixtureRoot.transform, false);
        targetObject.transform.position = new Vector3(0f, 0f, 1.25f);
        int playerLayer = LayerMask.NameToLayer("Player");
        Require(playerLayer >= 0, "Player Layer가 없습니다.");
        targetObject.layer = playerLayer;
        CombatHealth targetHealth = targetObject.AddComponent<CombatHealth>();
        targetHealth.SetMaxHp(5000f, true);
        CombatTarget.EnsureConfigured(targetObject, CombatTeam.PlayerParty);
        target = targetObject.transform;

        actor = Spawn(Vector3.zero, null);
    }

    private static EnemyActor Spawn(Vector3 position, Transform spawnTarget)
    {
        EnemySpawnRequest request = new EnemySpawnRequest(
            definition,
            position,
            Quaternion.identity,
            spawnTarget);
        Require(spawnService.TrySpawn(request, out EnemyActor spawned), "Enemy spawn에 실패했습니다.");
        return spawned;
    }

    private static void VerifySpawnContract(EnemyActor spawned)
    {
        Require(spawned != null && spawned.IsLeased, "Spawn Actor의 Lease가 유효하지 않습니다.");
        EnemyRank rank = spawned.GetComponent<EnemyRank>();
        Require(rank != null, "EnemyRank가 없습니다.");
        Require(rank.Rank == EnemyRankType.Normal, "Normal Definition이 legacy Elite로 표시됩니다.");
        Require(rank.GradeType == EnemyGradeType.Normal, "EnemyRank Grade adapter가 Normal이 아닙니다.");
        Require(
            Mathf.Approximately(spawned.AI.AttackEnterRange, spawned.AbilityController.AttackRange),
            "Ability 주입 뒤 AI AttackEnterRange가 갱신되지 않았습니다.");
        Require(
            spawned.AI.MoveStopDistance < spawned.AI.AttackEnterRange,
            "AI MoveStopDistance가 공격 진입 거리보다 작지 않습니다.");
        Require(
            spawned.AI.AttackExitRange > spawned.AI.AttackEnterRange,
            "AI AttackExitRange hysteresis가 없습니다.");

        EnemyAbilitySet abilitySet = definition.AbilitySet;
        Require(abilitySet != null && abilitySet.IsValid, "AbilitySet이 유효하지 않습니다.");
        for (int i = 0; i < abilitySet.Count; i++)
        {
            EnemyAbilityDefinition ability = abilitySet.GetAbility(i);
            Require(ability != null && ability.RequireLineOfSight, "DirectTarget LOS Gate가 꺼져 있습니다.");
        }
    }

    private static void ConfigureTransientPresentation(EnemyActor spawned)
    {
        DropTable transientDropTable = ScriptableObject.CreateInstance<DropTable>();
        GameObject transientHitVfx = new GameObject("TransientHitVfx");
        transientHitVfx.transform.SetParent(fixtureRoot.transform, false);

        EnemyLootDropper dropper = spawned.GetComponent<EnemyLootDropper>();
        CombatVfx vfx = spawned.GetComponent<CombatVfx>();
        Require(dropper != null && vfx != null, "Presentation component가 없습니다.");
        dropper.Configure(transientDropTable, null, target, null);
        vfx.Configure(transientHitVfx, null);

        Require(ReadObjectReference(dropper, "dropTable") == transientDropTable, "DropContext 주입이 실패했습니다.");
        Require(ReadObjectReference(vfx, "hitVfxPrefab") == transientHitVfx, "VFX Context 주입이 실패했습니다.");
    }

    private static void VerifyReuseAndAttackCommit()
    {
        EnemyActor reused = Spawn(Vector3.zero, target);
        Require(ReferenceEquals(actor, reused), "같은 Definition Actor가 재사용되지 않았습니다.");
        actor = reused;
        VerifySpawnContract(actor);
        VerifyPresentationReset(actor);

        actor.AI.enabled = false;
        Require(actor.AbilityController.TryStart(target), "첫 공격 실행을 시작하지 못했습니다.");
        int committedIndex = actor.Melee.LastCommittedAttackIndex;
        Require(committedIndex >= 0, "실행된 공격 후보가 commit되지 않았습니다.");
        Require(!actor.AbilityController.TryStart(target), "진행 중 공격 위에 두 번째 공격이 시작됐습니다.");
        Require(
            actor.Melee.LastCommittedAttackIndex == committedIndex,
            "실패한 공격 시도가 마지막 공격 후보를 변경했습니다.");
        actor.AbilityController.Cancel();
        actor.AI.enabled = true;
    }

    private static void KillAndWaitForPoolReturn()
    {
        actor.Health.TakeDamage(
            new DamageInfo(actor.Health.MaxHp + 1f, actor.transform.position, target.gameObject));
        Require(actor.Health.IsDead, "사망 Fixture가 Actor를 죽이지 못했습니다.");
        float deathLength = definition.AnimationProfile != null
            && definition.AnimationProfile.Death != null
            ? definition.AnimationProfile.Death.length
            : 0f;
        timeoutAt = Time.realtimeSinceStartup + Mathf.Max(4f, deathLength + 2f);
    }

    private static void VerifyInactiveAndReset(EnemyActor released, string label)
    {
        Require(released != null && !released.gameObject.activeSelf, label + ": Actor가 활성 상태입니다.");
        Require(!released.IsLeased, label + ": Lease가 남았습니다.");
        Require(released.Definition == null, label + ": Definition이 남았습니다.");
        Require(released.Identity != null && released.Identity.Definition == null, label + ": Identity가 남았습니다.");
        Require(released.AI.Target == null, label + ": AI Target이 남았습니다.");
        Require(!released.AbilityController.IsExecuting, label + ": 공격 실행이 남았습니다.");
        VerifyPresentationReset(released);
    }

    private static void VerifyPresentationReset(EnemyActor released)
    {
        EnemyLootDropper dropper = released.GetComponent<EnemyLootDropper>();
        CombatVfx vfx = released.GetComponent<CombatVfx>();
        Require(ReadObjectReference(dropper, "dropTable") == null, "DropTable Context가 풀 재사용 후 남았습니다.");
        Require(ReadObjectReference(dropper, "targetInventory") == null, "Inventory Context가 풀 재사용 후 남았습니다.");
        Require(ReadObjectReference(dropper, "player") == null, "Player Context가 풀 재사용 후 남았습니다.");
        Require(ReadObjectReference(vfx, "hitVfxPrefab") == null, "Hit VFX Context가 풀 재사용 후 남았습니다.");
        Require(ReadObjectReference(vfx, "deathVfxPrefab") == null, "Death VFX Context가 풀 재사용 후 남았습니다.");
    }

    private static UnityEngine.Object ReadObjectReference(UnityEngine.Object targetObject, string propertyName)
    {
        SerializedObject serializedObject = new SerializedObject(targetObject);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        Require(property != null, propertyName + " SerializedProperty를 찾지 못했습니다.");
        return property.objectReferenceValue;
    }

    private static void CompleteSuccessfully()
    {
        Debug.Log(
            "[ProtofactorEnemyFoundationPlayModeVerifier] PASS"
            + " contextReset=1 duplicateServiceBlocked=1 gradeAdapter=1 aiRangeRefresh=1"
            + " attackCommit=1 explicitRelease=1 deathRelease=1 externalDisableRelease=1 losGate=1");
        SessionState.SetInt(ExitCodeKey, 0);
        CleanupFixture();
        EditorApplication.update -= UpdateVerification;
        EditorApplication.ExitPlaymode();
    }

    private static void FailAndExit()
    {
        SessionState.SetInt(ExitCodeKey, 1);
        CleanupFixture();
        EditorApplication.update -= UpdateVerification;
        if (EditorApplication.isPlaying)
            EditorApplication.ExitPlaymode();
    }

    private static void CleanupFixture()
    {
        if (fixtureRoot != null)
            UnityEngine.Object.Destroy(fixtureRoot);
        fixtureRoot = null;
        target = null;
        definition = null;
        spawnService = null;
        poolService = null;
        actor = null;
    }

    private static void WaitFrames(int frames)
    {
        waitUntilFrame = Time.frameCount + Mathf.Max(1, frames);
    }

    private static void ClearSessionState()
    {
        SessionState.EraseBool(ActiveKey);
        SessionState.EraseBool(BatchKey);
        SessionState.EraseInt(ExitCodeKey);
        SessionState.EraseString(PreviousSceneKey);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
