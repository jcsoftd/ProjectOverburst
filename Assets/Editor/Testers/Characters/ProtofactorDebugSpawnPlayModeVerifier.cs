using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class ProtofactorDebugSpawnPlayModeVerifier
{
    private const string ActiveKey =
        "ProtofactorDebugSpawnPlayModeVerifier.Active";
    private const string BatchKey =
        "ProtofactorDebugSpawnPlayModeVerifier.Batch";
    private const string ExitCodeKey =
        "ProtofactorDebugSpawnPlayModeVerifier.ExitCode";
    private const string PreviousSceneKey =
        "ProtofactorDebugSpawnPlayModeVerifier.PreviousScene";
    private const int HideoutSpawnCount = 4;
    private const int MassSpawnCount = 6;

    private enum VerifyStep
    {
        None,
        Setup,
        VerifyHideoutSpawn,
        WaitForMassSpawn,
        VerifyRelease
    }

    private static VerifyStep step;
    private static int waitUntilFrame;
    private static float timeoutAt;
    private static GameObject fixtureRoot;
    private static HideoutMonsterSpawnDebugController hideoutController;
    private static EnemySpawnService spawnService;
    private static int baselineSquadCount;
    private static int baselineCrowdCount;
    private static int finalCeratoferoxCount;
    private static int finalRapaxCount;
    private static int finalGobblerCount;

    static ProtofactorDebugSpawnPlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    [MenuItem("OVERBURST/Codex/Validation/Verify Protofactor Debug Spawns PlayMode")]
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
        {
            throw new InvalidOperationException(
                "PlayMode가 이미 실행 중이거나 전환 중입니다.");
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid() && activeScene.isDirty)
        {
            throw new InvalidOperationException(
                "현재 씬에 저장되지 않은 변경이 있어 빈 검증 씬으로 전환하지 않습니다.");
        }

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
        string previousScene =
            SessionState.GetString(PreviousSceneKey, string.Empty);
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
            Debug.Log("[ProtofactorDebugSpawnPlayModeVerifier] 검증을 완료했습니다.");
        else
            Debug.LogError("[ProtofactorDebugSpawnPlayModeVerifier] 검증에 실패했습니다.");
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
                    step = VerifyStep.VerifyHideoutSpawn;
                    WaitFrames(2);
                    break;

                case VerifyStep.VerifyHideoutSpawn:
                    VerifyActiveRoster(HideoutSpawnCount, false);
                    Require(
                        EnemyMassSpawnDebugService.RequestSpawn(
                            EnemyMassSpawnDebugPattern.Circle,
                            MassSpawnCount),
                        "대량 소환 요청이 거부되었습니다.");
                    step = VerifyStep.WaitForMassSpawn;
                    waitUntilFrame = Time.frameCount + 1;
                    timeoutAt = Time.realtimeSinceStartup + 8f;
                    break;

                case VerifyStep.WaitForMassSpawn:
                    if (CountActiveActors() == HideoutSpawnCount + MassSpawnCount)
                    {
                        VerifyActiveRoster(
                            HideoutSpawnCount + MassSpawnCount,
                            true);
                        ReleaseAllActors();
                        step = VerifyStep.VerifyRelease;
                        WaitFrames(2);
                        break;
                    }

                    if (Time.realtimeSinceStartup >= timeoutAt)
                    {
                        throw new InvalidOperationException(
                            "대량 소환 6마리가 제한 시간 안에 생성되지 않았습니다. active="
                            + CountActiveActors());
                    }
                    break;

                case VerifyStep.VerifyRelease:
                    Require(
                        spawnService != null
                        && spawnService.Pool != null
                        && spawnService.Pool.LeasedCount == 0,
                        "검증 종료 시 반환되지 않은 신규 몬스터가 있습니다.");
                    Require(
                        EnemySquadPursuitRuntimeService.GetRuntimeStats()
                            .RegisteredAgentCount == baselineSquadCount,
                        "반환 뒤 부대 추적 등록 수가 복구되지 않았습니다.");
                    Require(
                        EnemyCrowdService.RegisteredCount == baselineCrowdCount,
                        "반환 뒤 군집 등록 수가 복구되지 않았습니다.");
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
        baselineSquadCount =
            EnemySquadPursuitRuntimeService.GetRuntimeStats().RegisteredAgentCount;
        baselineCrowdCount = EnemyCrowdService.RegisteredCount;
        fixtureRoot = new GameObject("ProtofactorDebugSpawnFixture");

        GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        player.name = "Player";
        player.tag = "Player";
        player.transform.SetParent(fixtureRoot.transform, false);
        int playerLayer = LayerMask.NameToLayer("Player");
        Require(playerLayer >= 0, "Player Layer를 찾지 못했습니다.");
        player.layer = playerLayer;
        CombatHealth playerHealth = player.AddComponent<CombatHealth>();
        playerHealth.SetMaxHp(10000f, true);
        Require(
            CombatTarget.EnsureConfigured(player, CombatTeam.PlayerParty) != null,
            "Player CombatTarget 조립에 실패했습니다.");

        GameObject controllerObject =
            new GameObject("HideoutMonsterSpawnDebugController");
        controllerObject.transform.SetParent(fixtureRoot.transform, false);
        hideoutController =
            controllerObject.AddComponent<HideoutMonsterSpawnDebugController>();

        CombatDebugSettings.SetHideoutMonsterSpawn(true);
        int spawned = hideoutController.TrySpawnPackNow();
        CombatDebugSettings.SetHideoutMonsterSpawn(false);
        Require(
            spawned == HideoutSpawnCount,
            "하이드아웃 1회 소환 수 불일치: " + spawned);

        spawnService = EnemySpawnService.Current;
        Require(spawnService != null, "디버그 EnemySpawnService가 생성되지 않았습니다.");
        Require(
            spawnService.Catalog != null
            && spawnService.Catalog.name == "EC_ProtofactorPilot",
            "디버그 SpawnService가 Protofactor Catalog를 사용하지 않습니다.");
        VerifyRandomRosterSelection();
    }

    private static void VerifyActiveRoster(
        int expectedTotal,
        bool requireAllDefinitions)
    {
        EnemyActor[] actors = FindActiveActors();
        int ceratoferoxCount = 0;
        int rapaxCount = 0;
        int gobblerCount = 0;
        for (int i = 0; i < actors.Length; i++)
        {
            EnemyActor actor = actors[i];
            Require(actor != null && actor.IsLeased, "활성 Actor의 풀 Lease가 없습니다.");
            Require(
                actor.Definition != null && actor.Definition.IsValid,
                "활성 Actor의 Definition이 유효하지 않습니다.");
            Require(
                actor.name.IndexOf("Murloc", StringComparison.OrdinalIgnoreCase) < 0,
                "활성 디버그 소환 목록에 Murloc이 포함됐습니다: " + actor.name);

            string definitionId = actor.Definition.EnemyId;
            if (definitionId == EnemyDebugSpawnRuntimeContext.CeratoferoxDefinitionId)
                ceratoferoxCount++;
            else if (definitionId == EnemyDebugSpawnRuntimeContext.RapaxDefinitionId)
                rapaxCount++;
            else if (definitionId == EnemyDebugSpawnRuntimeContext.GobblerDefinitionId)
                gobblerCount++;
            else
                throw new InvalidOperationException(
                    "허용되지 않은 디버그 Definition이 생성됐습니다: " + definitionId);
        }

        Require(
            actors.Length == expectedTotal,
            "활성 신규 몬스터 수 불일치: "
            + actors.Length
            + "/"
            + expectedTotal);
        Require(
            !requireAllDefinitions
            || (ceratoferoxCount > 0 && rapaxCount > 0 && gobblerCount > 0),
            "활성 디버그 로스터에 일반 3종이 모두 포함되지 않았습니다: Ceratoferox="
            + ceratoferoxCount
            + " Rapax="
            + rapaxCount
            + " Gobbler="
            + gobblerCount);
        Require(
            spawnService.Pool.LeasedCount == expectedTotal,
            "Pool Lease 수가 활성 Actor 수와 다릅니다.");
        finalCeratoferoxCount = ceratoferoxCount;
        finalRapaxCount = rapaxCount;
        finalGobblerCount = gobblerCount;
    }

    private static void VerifyRandomRosterSelection()
    {
        var random = new System.Random(20260728);
        bool foundCeratoferox = false;
        bool foundRapax = false;
        bool foundGobbler = false;
        for (int i = 0; i < 64; i++)
        {
            string definitionId =
                EnemyDebugSpawnRuntimeContext.GetRandomDefinitionId(random);
            if (definitionId == EnemyDebugSpawnRuntimeContext.CeratoferoxDefinitionId)
                foundCeratoferox = true;
            else if (definitionId == EnemyDebugSpawnRuntimeContext.RapaxDefinitionId)
                foundRapax = true;
            else if (definitionId == EnemyDebugSpawnRuntimeContext.GobblerDefinitionId)
                foundGobbler = true;
            else
                throw new InvalidOperationException(
                    "무작위 디버그 로스터가 허용되지 않은 Definition을 반환했습니다: "
                    + definitionId);
        }

        Require(
            foundCeratoferox && foundRapax && foundGobbler,
            "무작위 디버그 로스터 선택에서 일반 3종을 모두 확인하지 못했습니다.");
    }

    private static void ReleaseAllActors()
    {
        EnemyActor[] actors = FindActiveActors();
        for (int i = 0; i < actors.Length; i++)
            spawnService.Release(actors[i]);
    }

    private static int CountActiveActors()
    {
        return FindActiveActors().Length;
    }

    private static EnemyActor[] FindActiveActors()
    {
        return UnityEngine.Object.FindObjectsByType<EnemyActor>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
    }

    private static void CompleteSuccessfully()
    {
        Debug.Log(
            "[ProtofactorDebugSpawnPlayModeVerifier] PASS "
            + "hideoutRandom=4 massAlternating=6 Ceratoferox="
            + finalCeratoferoxCount
            + " Rapax="
            + finalRapaxCount
            + " Gobbler="
            + finalGobblerCount
            + " "
            + "murlocActive=0 poolRelease=10");
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
        CombatDebugSettings.SetHideoutMonsterSpawn(false);
        if (spawnService != null)
            ReleaseAllActors();
        if (fixtureRoot != null)
            UnityEngine.Object.Destroy(fixtureRoot);
        fixtureRoot = null;
        hideoutController = null;
        spawnService = null;
        finalCeratoferoxCount = 0;
        finalRapaxCount = 0;
        finalGobblerCount = 0;
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
