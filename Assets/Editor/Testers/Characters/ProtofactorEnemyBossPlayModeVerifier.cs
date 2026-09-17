using System;
using System.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ProtofactorEnemyBossPlayModeVerifier
{
    private const string ActiveKey =
        "ProtofactorEnemyBossPlayModeVerifier.Active";
    private const string BatchKey =
        "ProtofactorEnemyBossPlayModeVerifier.Batch";
    private const string ExitCodeKey =
        "ProtofactorEnemyBossPlayModeVerifier.ExitCode";
    private const string PreviousSceneKey =
        "ProtofactorEnemyBossPlayModeVerifier.PreviousScene";
    private static bool previousRunInBackground;

    static ProtofactorEnemyBossPlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    [MenuItem(
        "OVERBURST/Codex/Validation/Verify Protofactor Enemy Boss PlayMode")]
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
            throw new InvalidOperationException(
                "PlayMode가 이미 실행 중이거나 전환 중입니다.");

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid() && activeScene.isDirty)
            throw new InvalidOperationException(
                "저장되지 않은 현재 씬이 있어 검증 씬으로 전환하지 않습니다.");

        SessionState.SetBool(ActiveKey, true);
        SessionState.SetBool(BatchKey, batchMode);
        SessionState.SetInt(ExitCodeKey, 1);
        SessionState.SetString(
            PreviousSceneKey,
            activeScene.IsValid() ? activeScene.path : string.Empty);
        EditorSceneManager.NewScene(
            NewSceneSetup.EmptyScene,
            NewSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(ActiveKey, false))
            return;

        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            previousRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            GameObject runnerObject =
                new GameObject("ProtofactorEnemyBossVerifierRunner");
            ProtofactorEnemyBossVerifierRunner runner =
                runnerObject.AddComponent<ProtofactorEnemyBossVerifierRunner>();
            runner.Begin(CompleteSuccessfully, FailAndExit);
            return;
        }

        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            Application.runInBackground = previousRunInBackground;
            return;
        }

        if (state != PlayModeStateChange.EnteredEditMode)
            return;

        int exitCode = SessionState.GetInt(ExitCodeKey, 1);
        bool batchMode = SessionState.GetBool(BatchKey, false);
        string previousScene =
            SessionState.GetString(PreviousSceneKey, string.Empty);
        ClearSessionState();

        if (!batchMode
            && !string.IsNullOrWhiteSpace(previousScene)
            && System.IO.File.Exists(previousScene))
        {
            EditorSceneManager.OpenScene(
                previousScene,
                OpenSceneMode.Single);
        }

        if (batchMode)
            EditorApplication.Exit(exitCode);
    }

    private static void CompleteSuccessfully()
    {
        Debug.Log(
            "[ProtofactorEnemyBossPlayModeVerifier] PASS"
            + " definition=1 independent=1 phases=3 registry=1"
            + " areaSlam=1 priorityPattern=1 defeatOnce=1"
            + " rewardBeforeClear=1 exitLock=1 bossHud=1"
            + " poolReset=1 reuse=1");
        SessionState.SetInt(ExitCodeKey, 0);
        EditorApplication.ExitPlaymode();
    }

    private static void FailAndExit(Exception exception)
    {
        Debug.LogException(exception);
        SessionState.SetInt(ExitCodeKey, 1);
        if (EditorApplication.isPlaying)
            EditorApplication.ExitPlaymode();
    }

    private static void ClearSessionState()
    {
        SessionState.EraseBool(ActiveKey);
        SessionState.EraseBool(BatchKey);
        SessionState.EraseInt(ExitCodeKey);
        SessionState.EraseString(PreviousSceneKey);
    }
}

public sealed class ProtofactorEnemyBossVerifierRunner : MonoBehaviour
{
    private const string CatalogResourcePath =
        "Enemies/Protofactor/Catalogs/EC_ProtofactorPilot";
    private const string BossHudResourcePath =
        "UI/HUD/PF_EnemyBossHud";

    private Action onSuccess;
    private Action<Exception> onFailure;
    private GameObject fixtureRoot;
    private EnemySpawnService spawnService;
    private EnemyPoolService poolService;

    public void Begin(
        Action completed,
        Action<Exception> failed)
    {
        onSuccess = completed;
        onFailure = failed;
        StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        yield return null;
        IEnumerator verification = RunVerification();
        while (true)
        {
            bool hasNext;
            object current = null;
            try
            {
                hasNext = verification.MoveNext();
                if (hasNext)
                    current = verification.Current;
            }
            catch (Exception exception)
            {
                Cleanup();
                onFailure?.Invoke(exception);
                yield break;
            }

            if (!hasNext)
                break;

            yield return current;
        }

        Cleanup();
        onSuccess?.Invoke();
    }

    private IEnumerator RunVerification()
    {
        EnemyCatalog catalog =
            Resources.Load<EnemyCatalog>(CatalogResourcePath);
            Require(catalog != null, "Protofactor Catalog을 찾지 못했습니다.");
            Require(
                catalog.TryGet(
                    "Ursacetus_Boss",
                    out EnemyDefinition definition),
                "Ursacetus_Boss Definition이 없습니다.");
            Require(
                definition != null
                && definition.IsValid
                && definition.Grade.GradeType == EnemyGradeType.Boss,
                "Ursacetus Boss Definition이 유효하지 않습니다.");
            Require(
                definition.SquadParticipationMode
                == EnemySquadParticipationMode.Independent,
                "Ursacetus는 Independent여야 합니다.");

            BuildServices(catalog);
            DungeonBossEncounterBridge encounterBridge =
                fixtureRoot.AddComponent<DungeonBossEncounterBridge>();
            int rewardRequestCount = 0;
            int roomClearCount = 0;
            bool rewardArrivedBeforeClear = false;
            encounterBridge.RewardRequested += (_, _, _) =>
                rewardRequestCount++;
            encounterBridge.RoomCleared += (_, _) =>
            {
                roomClearCount++;
                rewardArrivedBeforeClear =
                    encounterBridge.IsRewardRequested;
            };

            GameObject hudPrefab =
                Resources.Load<GameObject>(BossHudResourcePath);
            Require(hudPrefab != null, "Boss HUD Prefab을 찾지 못했습니다.");
            GameObject hudObject = Instantiate(
                hudPrefab,
                fixtureRoot.transform);
            EnemyBossHudView bossHud =
                hudObject.GetComponent<EnemyBossHudView>();
            Require(bossHud != null, "Boss HUD View가 없습니다.");

            Transform target = CreatePlayerTarget(
                "BossTarget",
                new Vector3(0f, 0f, 2.2f));
            Transform bystander = CreatePlayerTarget(
                "BossBystander",
                new Vector3(0f, 0f, -2.2f));

            EnemyActor actor = spawnService.Spawn(
                new EnemySpawnRequest(
                    definition,
                    Vector3.zero,
                    Quaternion.identity,
                    target,
                    fixtureRoot,
                    fixtureRoot.transform,
                    fixtureRoot.transform));
            Require(actor != null, "Ursacetus 스폰에 실패했습니다.");
            actor.AI.enabled = false;
            actor.Movement.StopMovement();

            EnemyBossPhaseController boss = actor.BossPhaseController;
            Require(
                boss != null
                && boss.IsEncounterActive
                && !boss.IsDefeated,
                "Boss Encounter가 시작되지 않았습니다.");
            Require(
                boss.BossDefinition != null
                && boss.BossDefinition.IsValid
                && boss.BossDefinition.PhaseCount == 3,
                "Boss 3페이즈 데이터가 유효하지 않습니다.");
            Require(
                EnemyBossEncounterRegistry.Current == boss
                && EnemyBossEncounterRegistry.ActiveCount == 1,
                "Boss Registry 등록이 잘못됐습니다.");
            Require(
                encounterBridge.ActiveBoss == boss
                && encounterBridge.IsBossEncounterActive
                && !encounterBridge.IsExitUnlocked,
                "Boss 시작 시 던전 출구 잠금 상태가 잘못됐습니다.");
            Require(
                bossHud.BoundBoss == boss
                && bossHud.IsVisible
                && Mathf.Approximately(
                    bossHud.DisplayedHealth01,
                    1f),
                "Boss HUD 초기 바인딩이 잘못됐습니다.");
            Require(
                boss.CurrentPhaseIndex == 0
                && actor.AbilityController.AbilitySet.Count == 1,
                "1페이즈 Ability Set이 적용되지 않았습니다.");

            int phaseChangeCount = 0;
            int defeatCount = 0;
            boss.PhaseChanged += (_, _, _) => phaseChangeCount++;
            boss.Defeated += (_, _) => defeatCount++;

            boss.Health.TakeDamage(
                new DamageInfo(
                    boss.Health.MaxHp * 0.4f,
                    boss.transform.position,
                    target.gameObject));
            Require(
                boss.CurrentPhaseIndex == 1
                && actor.AbilityController.AbilitySet.Count == 2,
                "66% 2페이즈 전환이 실패했습니다.");
            Require(
                bossHud.BoundBoss == boss
                && bossHud.DisplayedHealth01 < 0.61f,
                "Boss HUD 체력 갱신이 실패했습니다.");

            IEnumerator phaseTwoAbility =
                StartHighestPriorityAbility(actor, target, 3f);
            while (phaseTwoAbility.MoveNext())
                yield return phaseTwoAbility.Current;
            Require(
                actor.AbilityController.LastCommittedAbility != null
                && actor.AbilityController.LastCommittedAbility.ExecutionMode
                == EnemyAbilityExecutionMode.AreaSlam
                && actor.AbilityController.LastCommittedAbility.Priority == 10,
                "2페이즈 Stomp 우선 패턴이 선택되지 않았습니다.");
            CombatHealth targetHealth = target.GetComponent<CombatHealth>();
            CombatHealth bystanderHealth =
                bystander.GetComponent<CombatHealth>();
            float targetBefore = targetHealth.CurrentHp;
            float bystanderBefore = bystanderHealth.CurrentHp;
            IEnumerator phaseTwoCompletion =
                WaitForAbilityEnd(actor.AbilityController, 8f);
            while (phaseTwoCompletion.MoveNext())
                yield return phaseTwoCompletion.Current;
            Require(
                targetHealth.CurrentHp < targetBefore
                && bystanderHealth.CurrentHp < bystanderBefore,
                "AreaSlam이 반경 안 두 대상에게 실제 피해를 주지 못했습니다.");

            boss.Health.TakeDamage(
                new DamageInfo(
                    boss.Health.MaxHp * 0.4f,
                    boss.transform.position,
                    target.gameObject));
            Require(
                boss.CurrentPhaseIndex == 2
                && actor.AbilityController.AbilitySet.Count == 3,
                "33% 3페이즈 전환이 실패했습니다.");

            IEnumerator phaseThreeAbility =
                StartHighestPriorityAbility(actor, target, 3f);
            while (phaseThreeAbility.MoveNext())
                yield return phaseThreeAbility.Current;
            Require(
                actor.AbilityController.LastCommittedAbility != null
                && actor.AbilityController.LastCommittedAbility.AbilityId
                == "Ursacetus_QuakeSmash"
                && actor.AbilityController.LastCommittedAbility.Priority == 20,
                "3페이즈 QuakeSmash 우선 패턴이 선택되지 않았습니다.");
            actor.AbilityController.Cancel();

            boss.Health.TakeDamage(
                new DamageInfo(
                    boss.Health.MaxHp * 2f,
                    boss.transform.position,
                    target.gameObject));
            Require(
                boss.IsDefeated
                && defeatCount == 1,
                "Boss 사망 이벤트가 정확히 한 번 발생하지 않았습니다.");
            EnemyBossOutcomeController outcome =
                actor.GetComponent<EnemyBossOutcomeController>();
            Require(
                outcome != null
                && outcome.IsOutcomeDispatched
                && rewardRequestCount == 1
                && roomClearCount == 1
                && rewardArrivedBeforeClear
                && encounterBridge.IsRewardRequested
                && encounterBridge.IsRoomCleared
                && encounterBridge.IsExitUnlocked,
                "Boss 보상→방 클리어→출구 해제 순서가 잘못됐습니다.");
            Require(
                !bossHud.IsVisible,
                "Boss 사망 뒤 HUD가 숨겨지지 않았습니다.");

            GameObject portalObject =
                new GameObject("ExitPortalContract");
            portalObject.transform.SetParent(
                fixtureRoot.transform,
                false);
            DungeonPortalExit portal =
                portalObject.AddComponent<DungeonPortalExit>();
            portal.SetInteractionEnabled(false);
            Require(
                !portal.InteractionEnabled
                && !portal.RequestExtract(),
                "잠긴 던전 출구가 귀환 요청을 허용했습니다.");
            boss.Health.TakeDamage(
                new DamageInfo(
                    boss.Health.MaxHp,
                    boss.transform.position,
                    target.gameObject));
            Require(defeatCount == 1, "Boss 사망 이벤트가 중복 발생했습니다.");
            Require(phaseChangeCount == 2, "Boss 페이즈 이벤트 수가 2가 아닙니다.");

            spawnService.Release(actor);
            Require(
                EnemyBossEncounterRegistry.ActiveCount == 0,
                "풀 반환 뒤 Boss Registry가 정리되지 않았습니다.");
            Require(
                poolService.LeasedCount == 0
                && poolService.AvailableCount == 1,
                "Boss 풀 반환 상태가 잘못됐습니다.");

            EnemyActor reused = spawnService.Spawn(
                new EnemySpawnRequest(
                    definition,
                    Vector3.zero,
                    Quaternion.identity,
                    target,
                    fixtureRoot,
                    fixtureRoot.transform,
                    fixtureRoot.transform));
            Require(reused == actor, "Boss가 같은 풀 인스턴스를 재사용하지 않았습니다.");
            Require(
                reused.BossPhaseController.CurrentPhaseIndex == 0
                && !reused.BossPhaseController.IsDefeated
                && EnemyBossEncounterRegistry.Current
                == reused.BossPhaseController,
                "Boss 재대여에서 페이즈·사망·Registry가 초기화되지 않았습니다.");
            Require(
                !reused.GetComponent<EnemyBossOutcomeController>()
                    .IsOutcomeDispatched
                && encounterBridge.IsBossEncounterActive
                && !encounterBridge.IsExitUnlocked
                && bossHud.BoundBoss == reused.BossPhaseController
                && bossHud.IsVisible,
                "Boss 재대여에서 Outcome·Bridge·HUD가 초기화되지 않았습니다.");
            spawnService.Release(reused);
    }

    private IEnumerator StartHighestPriorityAbility(
        EnemyActor actor,
        Transform target,
        float timeout)
    {
        float deadline = Time.realtimeSinceStartup + timeout;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (actor.AbilityController.TryStart(target))
                yield break;
            yield return null;
        }

        throw new InvalidOperationException(
            "Boss Ability를 제한 시간 안에 시작하지 못했습니다.");
    }

    private static IEnumerator WaitForAbilityEnd(
        EnemyAbilityController controller,
        float timeout)
    {
        float deadline = Time.realtimeSinceStartup + timeout;
        while (controller.IsExecuting
            && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        Require(!controller.IsExecuting, "Boss Ability가 제한 시간 안에 끝나지 않았습니다.");
    }

    private void BuildServices(EnemyCatalog catalog)
    {
        fixtureRoot = new GameObject("ProtofactorBossFixture");
        GameObject inactivePool = new GameObject("InactivePool");
        inactivePool.transform.SetParent(fixtureRoot.transform, false);
        inactivePool.SetActive(false);

        GameObject services = new GameObject("EnemySpawnServices");
        services.transform.SetParent(fixtureRoot.transform, false);
        poolService = services.AddComponent<EnemyPoolService>();
        poolService.Configure(inactivePool.transform, 0);
        spawnService = services.AddComponent<EnemySpawnService>();
        spawnService.Configure(catalog, poolService);
        Require(
            spawnService.Validate(out string message),
            message);
    }

    private Transform CreatePlayerTarget(
        string objectName,
        Vector3 position)
    {
        GameObject target = new GameObject(objectName);
        target.transform.SetParent(fixtureRoot.transform, false);
        target.transform.position = position;
        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0)
            target.layer = playerLayer;
        CapsuleCollider collider = target.AddComponent<CapsuleCollider>();
        collider.radius = 0.45f;
        collider.height = 1.8f;
        collider.center = Vector3.up * 0.9f;
        CombatHealth health = target.AddComponent<CombatHealth>();
        health.SetMaxHp(100f, true);
        CombatTarget.EnsureConfigured(target, CombatTeam.PlayerParty);
        return target.transform;
    }

    private void Cleanup()
    {
        EnemyBossEncounterRegistry.Clear();
        if (fixtureRoot != null)
            Destroy(fixtureRoot);
        fixtureRoot = null;
        spawnService = null;
        poolService = null;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
