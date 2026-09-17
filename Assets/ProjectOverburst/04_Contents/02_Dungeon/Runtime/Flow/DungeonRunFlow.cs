using System;
using System.Collections;
using System.Collections.Generic;
using DunGen;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class DungeonRunFlow : MonoBehaviour
{
    public event Action<DungeonPortalExit> ExitPortalCreated;

    [SerializeField] private RuntimeDungeon runtimeDungeon;
    [SerializeField] private DungeonRunDefinition definition;
    [SerializeField] private GameObject generatedRoot;
    [UnityEngine.Serialization.FormerlySerializedAs("partySpawnPlacer"), SerializeField] private DungeonPlayerSpawnPlacer playerSpawnPlacer;
    [SerializeField] private GameObject exitPortalPrefab;
    [SerializeField] private DungeonRunDebugReturnInput debugReturnInput;
    [SerializeField]
    private DungeonRoomVisibilityController roomVisibilityController;

    private DungeonGenerator generator;
    private DungeonArenaTileInjection arenaTileInjection;
    private DungeonRunGenerationFlow generationFlow;
    private DungeonRunEntryRequest request;
    private DungeonPortalExit activeExitPortal;
    private bool terminalStatusReceived;
    private GenerationStatus terminalStatus;
    private bool shuttingDown;
    private bool returnRequested;

    public int ActiveSeed { get; private set; }
    public DungeonRunParameters ActiveParameters { get; private set; }
    public RunSceneReturnContext ReturnContext { get; private set; }
    public RuntimeDungeon RuntimeDungeon => runtimeDungeon;
    public DungeonRunDefinition Definition => definition;
    public GameObject GeneratedRoot => generatedRoot;
    public DungeonPlayerSpawnPlacer PlayerSpawnPlacer => playerSpawnPlacer;
    public GameObject ExitPortalPrefab => exitPortalPrefab;
    public DungeonPortalExit ActiveExitPortal => activeExitPortal;
    public DungeonRunDebugReturnInput DebugReturnInput =>
        debugReturnInput;
    public DungeonRoomVisibilityController RoomVisibilityController =>
        roomVisibilityController;
    public int AcceptedLayoutAttempt { get; private set; }
    public DungeonLayoutQualityEvaluation AcceptedLayoutQuality
    {
        get;
        private set;
    }

    public void Configure(
        RuntimeDungeon dungeon,
        DungeonRunDefinition runDefinition,
        GameObject root,
        DungeonPlayerSpawnPlacer spawnPlacer = null,
        GameObject configuredExitPortalPrefab = null,
        DungeonRunDebugReturnInput configuredDebugReturnInput = null,
        DungeonRoomVisibilityController
            configuredRoomVisibilityController = null)
    {
        runtimeDungeon = dungeon;
        definition = runDefinition;
        generatedRoot = root;
        playerSpawnPlacer = spawnPlacer != null
            ? spawnPlacer
            : GetComponent<DungeonPlayerSpawnPlacer>();
        if (configuredExitPortalPrefab != null)
            exitPortalPrefab = configuredExitPortalPrefab;
        if (configuredDebugReturnInput != null)
            debugReturnInput = configuredDebugReturnInput;
        if (configuredRoomVisibilityController != null)
        {
            roomVisibilityController =
                configuredRoomVisibilityController;
        }
        if (debugReturnInput != null)
            debugReturnInput.Configure(this);
        if (roomVisibilityController != null)
            roomVisibilityController.RefreshBindings();
    }

    private IEnumerator Start()
    {
        string sceneName = gameObject.scene.name;
        RunSceneReadinessRegistry.Begin(sceneName);

        if (!TryPrepareGeneration(out string error))
        {
            Fail(sceneName, error);
            yield break;
        }

        float startTime = Time.realtimeSinceStartup;
        bool acceptedLayout = false;
        DungeonLayoutQualityEvaluation lastQuality = default;
        string lastGenerationError = string.Empty;
        for (int candidateIndex = 0;
             candidateIndex < definition.LayoutCandidateCount;
             candidateIndex++)
        {
            terminalStatusReceived = false;
            terminalStatus = GenerationStatus.NotStarted;
            int candidateSeed =
                DungeonLayoutQualityEvaluator.GetCandidateSeed(
                    ActiveSeed,
                    candidateIndex);
            generator.Seed = candidateSeed;
            runtimeDungeon.Generate();

            while (!terminalStatusReceived && generator.IsGenerating)
            {
                if (Time.realtimeSinceStartup - startTime
                    > definition.GenerationTimeoutSeconds)
                {
                    generator.Cancel();
                    Fail(
                        sceneName,
                        $"생성 제한 시간 초과: "
                        + $"{definition.GenerationTimeoutSeconds:F1}초");
                    yield break;
                }

                yield return null;
            }

            if (!terminalStatusReceived)
                terminalStatus = generator.Status;

            if (terminalStatus != GenerationStatus.Complete)
            {
                lastGenerationError =
                    "DunGen 생성 실패: " + terminalStatus;
                Debug.LogWarning(
                    $"[DungeonRun] Generation candidate failed. "
                    + $"RequestedSeed={ActiveSeed}, "
                    + $"CandidateSeed={candidateSeed}, "
                    + $"Attempt={candidateIndex + 1}/"
                    + $"{definition.LayoutCandidateCount}, "
                    + $"Status={terminalStatus}");
                generator.Clear(false);
                yield return null;
                continue;
            }

            Physics.SyncTransforms();
            yield return null;

            if (!TryValidateGeneratedDungeon(
                    out string validationError))
            {
                lastGenerationError = validationError;
                Debug.LogWarning(
                    $"[DungeonRun] Structurally invalid candidate rejected. "
                    + $"RequestedSeed={ActiveSeed}, "
                    + $"CandidateSeed={generator.ChosenSeed}, "
                    + $"Attempt={candidateIndex + 1}/"
                    + $"{definition.LayoutCandidateCount}, "
                    + $"Reason={validationError}");
                generator.Clear(false);
                yield return null;
                continue;
            }

            lastQuality = DungeonLayoutQualityEvaluator.Evaluate(
                generator.CurrentDungeon,
                definition.MinimumBranchTileCount,
                definition.MaximumPlanarAspectRatio,
                definition.EndCapTileSet);
            if (lastQuality.IsAccepted)
            {
                acceptedLayout = true;
                AcceptedLayoutAttempt = candidateIndex + 1;
                AcceptedLayoutQuality = lastQuality;
                break;
            }

            Debug.LogWarning(
                $"[DungeonRun] Layout rejected. "
                + $"RequestedSeed={ActiveSeed}, "
                + $"CandidateSeed={generator.ChosenSeed}, "
                + $"Attempt={candidateIndex + 1}/"
                + $"{definition.LayoutCandidateCount}, "
                + $"Reason={lastQuality.RejectionReason}");
            generator.Clear(false);
            yield return null;
        }

        if (!acceptedLayout)
        {
            Fail(
                sceneName,
                $"레이아웃 품질 기준 미달: "
                + $"{definition.LayoutCandidateCount}개 후보, "
                + (string.IsNullOrWhiteSpace(lastGenerationError)
                    ? lastQuality.RejectionReason
                    : lastGenerationError));
            yield break;
        }

        yield return playerSpawnPlacer.PlacePlayer(generator.CurrentDungeon);
        if (!playerSpawnPlacer.PlacementSucceeded)
        {
            generator.Clear(true);
            Fail(
                sceneName,
                "Party placement failed: "
                + playerSpawnPlacer.FailureMessage);
            yield break;
        }

        if (!TryCreateExitPortal(out string exitPortalError))
        {
            generator.Clear(true);
            Fail(sceneName, exitPortalError);
            yield break;
        }

        roomVisibilityController?.RefreshGeneratedContent(
            generator.CurrentDungeon);
        RunSceneReadinessRegistry.MarkReady(sceneName);
        DungeonRunGenerator projectGenerator =
            generator as DungeonRunGenerator;
        Debug.Log(
            $"[DungeonRun] Ready. RequestedSeed={ActiveSeed}, "
            + $"ChosenSeed={generator.ChosenSeed}, "
            + $"LayoutAttempt={AcceptedLayoutAttempt}/"
            + $"{definition.LayoutCandidateCount}, "
            + $"Branches={AcceptedLayoutQuality.BranchTileCount}, "
            + $"PlanarAspect="
            + $"{AcceptedLayoutQuality.PlanarAspectRatio:F2}, "
            + $"TotalMainPath="
            + $"{ActiveParameters.TotalMainPathTileCount}, "
            + $"Arena={ActiveParameters.ArenaCount}, "
            + "EndCap="
            + DungeonEndCapContract.CountPlacedEndCaps(
                generator.CurrentDungeon,
                definition.EndCapTileSet)
            + ", "
            + "OpenDoorways="
            + (projectGenerator != null
                ? $"{projectGenerator.OpenDoorwayCountBeforeEndCaps}"
                    + "->"
                    + $"{projectGenerator.OpenDoorwayCountAfterEndCaps}"
                : "Unavailable")
            + ", "
            + "LengthMode=Direct, "
            + "Mode=SupplierDunGen, "
            + $"Tiles={generator.CurrentDungeon.AllTiles.Count}, "
            + $"MainPath={generator.CurrentDungeon.MainPathTiles.Count}");
    }

    private bool TryPrepareGeneration(out string error)
    {
        error = string.Empty;
        if (runtimeDungeon == null)
        {
            error = "RuntimeDungeon 참조 누락";
            return false;
        }

        if (definition == null || definition.DungeonFlow == null)
        {
            error = "DungeonRunDefinition 또는 DungeonFlow 참조 누락";
            return false;
        }

        if (generatedRoot == null)
        {
            error = "GeneratedDungeon Root 참조 누락";
            return false;
        }

        if (roomVisibilityController == null)
        {
            error =
                "DungeonRoomVisibilityController 참조 누락";
            return false;
        }

        if (playerSpawnPlacer == null)
        {
            playerSpawnPlacer = GetComponent<DungeonPlayerSpawnPlacer>();
            if (playerSpawnPlacer == null)
            {
                error = "DungeonPlayerSpawnPlacer reference is missing.";
                return false;
            }
        }

        if (!DungeonRunLaunchContextHolder.TryConsume(out request))
        {
            request = DungeonRunEntryRequest.Create(
                definition.DefaultSeed,
                PersistentSceneFlow.DefaultHubSceneName);
        }

        string sourceSceneName =
            PersistentSceneFlow.IsHubSceneName(request.SourceSceneName)
                ? request.SourceSceneName
                : PersistentSceneFlow.DefaultHubSceneName;
        ReturnContext = RunSceneReturnContext.CreateHubTransfer(
            sourceSceneName,
            request.ReturnPointId);
        ActiveSeed = request.RunSeed;
        ActiveParameters = definition.ResolveParameters(
            request.DifficultyLevel,
            request.RoomCount,
            request.ArenaCount,
            request.TotalMainPathTileCount);
        if (ActiveParameters.ArenaCount > 0
            && definition.ArenaTileSet == null)
        {
            error = "Arena 개수가 1개 이상이지만 Arena TileSet 참조가 없습니다.";
            return false;
        }
        if (definition.EndCapTileSet == null)
        {
            error = "EndCap TileSet 참조가 없습니다.";
            return false;
        }

        runtimeDungeon.GenerateOnStart = false;
        runtimeDungeon.Root = generatedRoot;
        generator = DungeonRunGenerator.CreateFrom(
            runtimeDungeon.Generator,
            generatedRoot,
            definition.EndCapTileSet,
            definition.TopologyRepairCatalog);
        runtimeDungeon.Generator = generator;
        generator.Root = generatedRoot;
        generationFlow?.Dispose();
        generationFlow = new DungeonRunGenerationFlow(
            definition.DungeonFlow,
            ActiveParameters);
        generator.DungeonFlow = generationFlow.Flow;
        generator.Seed = ActiveSeed;
        generator.ShouldRandomizeSeed = false;
        generator.MaxAttemptCount = definition.MaxAttemptCount;
        generator.LengthMultiplier = 1f;
        generator.GenerateAsynchronously = true;
        generator.MaxAsyncFrameMilliseconds =
            definition.MaxAsyncFrameMilliseconds;
        generator.PlaceTileTriggers = true;
        generator.TileTriggerLayer = 2;
        arenaTileInjection?.Dispose();
        arenaTileInjection = new DungeonArenaTileInjection(
            generator,
            definition.ArenaTileSet,
            ActiveParameters.ArenaCount);
        generator.OnGenerationStatusChanged += HandleGenerationStatusChanged;
        roomVisibilityController.RefreshBindings();
        return true;
    }

    public bool RequestReturnToSourceHub(bool extractSuccess)
    {
        if (returnRequested)
            return false;

        PersistentSceneFlow sceneFlow =
            PersistentSceneFlow.Instance;
        if (sceneFlow == null || sceneFlow.IsSwitching)
            return false;

        string targetSceneName = ReturnContext != null
            ? ReturnContext.TargetSceneName
            : PersistentSceneFlow.DefaultHubSceneName;
        string returnPointId = ReturnContext != null
            ? ReturnContext.ReturnPointId
            : "Default";
        RunSceneReturnContext context = extractSuccess
            ? RunSceneReturnContext.CreateExtractSuccess(
                targetSceneName,
                returnPointId)
            : RunSceneReturnContext.CreateHubTransfer(
                targetSceneName,
                returnPointId);

        returnRequested = true;
        sceneFlow.ReturnToHub(context);
        return true;
    }

    private bool TryValidateGeneratedDungeon(out string error)
    {
        error = string.Empty;
        Dungeon dungeon = generator.CurrentDungeon;
        if (dungeon == null)
        {
            error = "생성 완료 뒤 Dungeon 인스턴스 누락";
            return false;
        }

        if (dungeon.AllTiles == null || dungeon.AllTiles.Count < 2)
        {
            error = "생성 타일 수 부족";
            return false;
        }

        if (dungeon.MainPathTiles == null
            || dungeon.MainPathTiles.Count < 2)
        {
            error = "MainPath 타일 수 부족";
            return false;
        }

        HashSet<int> tileIds = new();
        for (int i = 0; i < dungeon.AllTiles.Count; i++)
        {
            Tile tile = dungeon.AllTiles[i];
            if (tile == null || !tileIds.Add(tile.GetInstanceID()))
            {
                error = "null 또는 중복 타일 인스턴스 감지";
                return false;
            }
        }

        Tile first = dungeon.MainPathTiles[0];
        Tile last = dungeon.MainPathTiles[dungeon.MainPathTiles.Count - 1];
        if (first == null || last == null || first == last)
        {
            error = "MainPath 시작·마지막 타일 판정 오류";
            return false;
        }

        if (!HasRequiredAnchor(first, DungeonRoomAnchorRole.Start)
            || !HasRequiredAnchor(last, DungeonRoomAnchorRole.Exit))
        {
            error = "MainPath 시작 또는 출구 Anchor 누락";
            return false;
        }

        if (dungeon.Connections == null
            || dungeon.Connections.Count < dungeon.MainPathTiles.Count - 1)
        {
            error = "MainPath 연결 수 부족";
            return false;
        }

        int placedArenaCount =
            DungeonArenaTileInjection.CountPlacedArenas(
                dungeon,
                definition.ArenaTileSet);
        if (placedArenaCount != ActiveParameters.ArenaCount)
        {
            error =
                $"Arena 타일 수 불일치: "
                + $"{placedArenaCount}/{ActiveParameters.ArenaCount}";
            return false;
        }

        if (!DungeonArenaTileInjection.AreAllPlacedArenasOnMainPath(
                dungeon,
                definition.ArenaTileSet))
        {
            error = "Arena 타일이 Main Path 밖에 배치되었습니다.";
            return false;
        }

        if (!DungeonEndCapContract.TryValidateFinalOpenDoorwayPass(
                dungeon,
                generator as DungeonRunGenerator,
                definition.EndCapTileSet,
                out string endCapError))
        {
            error = endCapError;
            return false;
        }

        if (dungeon.MainPathTiles.Count
            != ActiveParameters.TotalMainPathTileCount)
        {
            error =
                $"Main Path 총길이 불일치: "
                + $"{dungeon.MainPathTiles.Count}/"
                + $"{ActiveParameters.TotalMainPathTileCount}";
            return false;
        }

        return true;
    }

    private static bool HasRequiredAnchor(
        Tile tile,
        DungeonRoomAnchorRole role)
    {
        DungeonRoomAnchor[] anchors =
            tile.GetComponentsInChildren<DungeonRoomAnchor>(true);
        for (int i = 0; i < anchors.Length; i++)
        {
            DungeonRoomAnchor anchor = anchors[i];
            if (anchor != null
                && anchor.Supports(DungeonRoomAnchorRole.Spawn)
                && anchor.Supports(role))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryCreateExitPortal(out string error)
    {
        error = string.Empty;
        if (exitPortalPrefab == null)
        {
            error = "Dungeon exit portal prefab reference is missing.";
            return false;
        }

        Dungeon dungeon = generator.CurrentDungeon;
        Tile exitTile = dungeon.MainPathTiles[
            dungeon.MainPathTiles.Count - 1];
        DungeonRoomAnchor[] anchors =
            exitTile.GetComponentsInChildren<DungeonRoomAnchor>(true);
        DungeonRoomAnchor exitAnchor = null;
        for (int i = 0; i < anchors.Length; i++)
        {
            DungeonRoomAnchor anchor = anchors[i];
            if (anchor != null
                && anchor.Supports(DungeonRoomAnchorRole.Spawn)
                && anchor.Supports(DungeonRoomAnchorRole.Exit))
            {
                exitAnchor = anchor;
                break;
            }
        }

        if (exitAnchor == null)
        {
            error = "Main-path exit anchor is missing.";
            return false;
        }

        if (activeExitPortal != null)
            Destroy(activeExitPortal.gameObject);

        GameObject exitPortalObject = Instantiate(
            exitPortalPrefab,
            exitAnchor.transform.position,
            exitAnchor.transform.rotation,
            exitTile.transform);
        activeExitPortal =
            exitPortalObject.GetComponent<DungeonPortalExit>();
        if (activeExitPortal == null)
        {
            Destroy(exitPortalObject);
            error =
                "Dungeon exit portal prefab component is missing.";
            return false;
        }
        activeExitPortal.name = "DungeonPortal_Exit_Runtime";
        activeExitPortal.Bind(this);
        ExitPortalCreated?.Invoke(activeExitPortal);
        return true;
    }

    private void HandleGenerationStatusChanged(
        DungeonGenerator changedGenerator,
        GenerationStatus status)
    {
        if (changedGenerator != generator)
            return;

        if (status != GenerationStatus.Complete
            && status != GenerationStatus.Failed)
        {
            return;
        }

        terminalStatus = status;
        terminalStatusReceived = true;
    }

    private static void Fail(string sceneName, string message)
    {
        RunSceneReadinessRegistry.MarkFailed(sceneName, message);
        Debug.LogError("[DungeonRun] " + message);
    }

    private void OnDestroy()
    {
        shuttingDown = true;
        if (generator != null)
        {
            generator.OnGenerationStatusChanged -=
                HandleGenerationStatusChanged;
            if (generator.IsGenerating)
                generator.Cancel();
        }
        arenaTileInjection?.Dispose();
        arenaTileInjection = null;
        generationFlow?.Dispose();
        generationFlow = null;
    }

    private void OnDisable()
    {
        if (shuttingDown || generator == null)
            return;

        if (generator.IsGenerating)
            generator.Cancel();
    }
}
