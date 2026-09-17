using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using DunGen;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

[InitializeOnLoad]
public static class DungeonVisibilityPerformancePlayModeVerifier
{
    private const string ActiveKey =
        "DungeonVisibilityPerformanceVerifier.Active";
    private const string BatchKey =
        "DungeonVisibilityPerformanceVerifier.Batch";
    private const string ExitCodeKey =
        "DungeonVisibilityPerformanceVerifier.ExitCode";
    private const string PersistentScenePath =
        "Assets/ProjectOverburst/00_Scenes/PersistentScene.unity";
    private const string ReportPath =
        "Logs/DungeonPhase5VisibilityPerformance.log";
    private const string RenderedReportPath =
        "Logs/DungeonPhase5VisibilityPerformance_Rendered.log";
    private const int EntrySeed = 33501;
    private const int SamplesPerScenario = 5;
    private const int MaximumCandidatesPerScenario = 60;
    private const int StableFrameCount = 5;
    private const int ProfileFrameCount = 45;
    private const float StepTimeoutSeconds = 120f;

    private enum VerifyState
    {
        WaitForHideout,
        WaitForDungeon,
        WaitForCulling,
        VerifyStationary,
        WaitForClear,
        GenerateCandidate,
        ProfileFrames
    }

    private enum StationaryContinuation
    {
        Initial,
        Traversal,
        ContextRebind,
        ScenarioSample
    }

    private enum ScenarioKind
    {
        ShortMainPath,
        LongMainPath,
        BranchIncluded
    }

    private static readonly List<GenerationSample>[] ScenarioSamples =
    {
        new(),
        new(),
        new()
    };

    private static readonly FrameTiming[] FrameTimings =
        new FrameTiming[1];
    private static readonly StringBuilder Report = new();

    private static VerifyState state;
    private static StationaryContinuation pendingContinuation;
    private static int waitUntilFrame;
    private static float timeoutAt;
    private static DungeonRunFlow runFlow;
    private static DungeonGenerator generator;
    private static DungeonRoomVisibilityController visibilityController;
    private static AdjacentRoomCulling roomCulling;
    private static PlayerContext playerContext;
    private static Dungeon activeDungeon;
    private static int stableFramesRemaining;
    private static int stationaryVisibilityRevision;
    private static int stationaryVisibilitySignature;
    private static int traversalTileIndex;
    private static int traversalTileLimit;
    private static int scenarioIndex;
    private static int acceptedScenarioSamples;
    private static int candidateCount;
    private static int currentCandidateSeed;
    private static int currentChosenSeed;
    private static double currentGenerationWallMilliseconds;
    private static double currentGenerationStatsMilliseconds;
    private static int cleanupCheckCount;
    private static int peakGeneratedObjectCount;
    private static int peakRuntimeMaterialCount;
    private static FrameCounterSet frameCounters;
    private static readonly MetricSeries MainThreadMilliseconds = new();
    private static readonly MetricSeries RenderThreadMilliseconds = new();
    private static readonly MetricSeries GpuFrameMilliseconds = new();
    private static readonly MetricSeries GcAllocatedBytes = new();
    private static readonly MetricSeries SetPassCalls = new();
    private static readonly MetricSeries DrawCalls = new();
    private static int profileFramesCaptured;
    private static int pendingEditorExitCode;
    private static int pendingEditorExitTicks;

    static DungeonVisibilityPerformancePlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -=
            HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged +=
            HandlePlayModeStateChanged;
    }

    [MenuItem(
        "OVERBURST/Codex/Validation/World/Dungeon/"
        + "Verify Phase 5 Visibility and Performance")]
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
                "PlayMode is already active or changing.");
        }

        ResetState();
        SessionState.SetBool(ActiveKey, true);
        SessionState.SetBool(BatchKey, batchMode);
        SessionState.SetInt(ExitCodeKey, 1);
        EditorSceneManager.OpenScene(
            PersistentScenePath,
            OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    private static void ResetState()
    {
        EditorApplication.update -= ExitEditorWhenSettled;
        Report.Clear();
        for (int i = 0; i < ScenarioSamples.Length; i++)
            ScenarioSamples[i].Clear();
        runFlow = null;
        generator = null;
        visibilityController = null;
        roomCulling = null;
        playerContext = null;
        activeDungeon = null;
        cleanupCheckCount = 0;
        peakGeneratedObjectCount = 0;
        peakRuntimeMaterialCount = 0;
        frameCounters?.Dispose();
        frameCounters = null;
        MainThreadMilliseconds.Reset();
        RenderThreadMilliseconds.Reset();
        GpuFrameMilliseconds.Reset();
        GcAllocatedBytes.Reset();
        SetPassCalls.Reset();
        DrawCalls.Reset();
        profileFramesCaptured = 0;
    }

    private static void HandlePlayModeStateChanged(
        PlayModeStateChange change)
    {
        if (!SessionState.GetBool(ActiveKey, false))
            return;

        if (change == PlayModeStateChange.EnteredPlayMode)
        {
            state = VerifyState.WaitForHideout;
            Wait(30);
            EditorApplication.update -= UpdateVerification;
            EditorApplication.update += UpdateVerification;
            return;
        }

        if (change == PlayModeStateChange.ExitingPlayMode)
        {
            EditorApplication.update -= UpdateVerification;
            return;
        }

        if (change != PlayModeStateChange.EnteredEditMode)
            return;

        int exitCode = SessionState.GetInt(ExitCodeKey, 1);
        bool batchMode = SessionState.GetBool(BatchKey, false);
        SessionState.EraseBool(ActiveKey);
        SessionState.EraseBool(BatchKey);
        SessionState.EraseInt(ExitCodeKey);

        if (batchMode)
        {
            pendingEditorExitCode = exitCode;
            pendingEditorExitTicks = 10;
            EditorApplication.update -= ExitEditorWhenSettled;
            EditorApplication.update += ExitEditorWhenSettled;
        }
        else if (exitCode == 0)
            Debug.Log(
                "[DungeonVisibilityPerformanceVerifier] 완료");
        else
            Debug.LogError(
                "[DungeonVisibilityPerformanceVerifier] 실패");
    }

    private static void ExitEditorWhenSettled()
    {
        if (EditorApplication.isCompiling
            || EditorApplication.isUpdating)
        {
            return;
        }

        pendingEditorExitTicks--;
        if (pendingEditorExitTicks > 0)
            return;

        EditorApplication.update -= ExitEditorWhenSettled;
        EditorApplication.Exit(pendingEditorExitCode);
    }

    private static void UpdateVerification()
    {
        if (!EditorApplication.isPlaying
            || Time.frameCount < waitUntilFrame)
        {
            return;
        }

        try
        {
            if (Time.realtimeSinceStartup > timeoutAt)
            {
                throw new TimeoutException(
                    "Phase 5 verification timed out at " + state);
            }

            switch (state)
            {
                case VerifyState.WaitForHideout:
                    WaitForHideout();
                    break;
                case VerifyState.WaitForDungeon:
                    WaitForDungeon();
                    break;
                case VerifyState.WaitForCulling:
                    BeginStationaryVerification();
                    break;
                case VerifyState.VerifyStationary:
                    VerifyStationaryFrame();
                    break;
                case VerifyState.WaitForClear:
                    VerifyClearAndPrepareNextGeneration();
                    break;
                case VerifyState.GenerateCandidate:
                    GenerateCandidate();
                    break;
                case VerifyState.ProfileFrames:
                    CaptureProfileFrame();
                    break;
            }
        }
        catch (Exception exception)
        {
            Report.AppendLine("[FAILED]");
            Report.AppendLine(exception.ToString());
            WriteReport();
            Debug.LogException(exception);
            Finish(1);
        }
    }

    private static void WaitForHideout()
    {
        PersistentSceneFlow sceneFlow = PersistentSceneFlow.Instance;
        if (sceneFlow == null
            || sceneFlow.IsSwitching
            || sceneFlow.CurrentSubSceneName
                != PersistentSceneFlow.HideoutSceneName)
        {
            return;
        }

        sceneFlow.EnterDungeon(
            DungeonRunEntryRequest.Create(
                EntrySeed,
                PersistentSceneFlow.HideoutSceneName,
                "DungeonPortal"));
        state = VerifyState.WaitForDungeon;
        Wait(1);
    }

    private static void WaitForDungeon()
    {
        RunSceneReadinessState readiness =
            RunSceneReadinessRegistry.GetState(
                PersistentSceneFlow.DungeonRunSceneName);
        if (readiness == RunSceneReadinessState.Failed)
        {
            throw new InvalidOperationException(
                "Dungeon readiness failed: "
                + RunSceneReadinessRegistry.GetMessage(
                    PersistentSceneFlow.DungeonRunSceneName));
        }

        PersistentSceneFlow sceneFlow = PersistentSceneFlow.Instance;
        if (sceneFlow == null
            || sceneFlow.IsSwitching
            || readiness != RunSceneReadinessState.Ready
            || sceneFlow.CurrentSubSceneName
                != PersistentSceneFlow.DungeonRunSceneName)
        {
            return;
        }

        CaptureRuntimeReferences();
        if (!roomCulling.enabled)
        {
            Require(!roomCulling.Ready
                && visibilityController.HiddenTileCount == 0
                && visibilityController.VisibilityChangeCount == 0
                && visibilityController.PausedParticleSystemCount == 0
                && visibilityController.PausedAudioSourceCount == 0
                && visibilityController.PausedAnimatorCount == 0,
                "Disabled room culling still changed generated content.");
            Require(visibilityController.LeaderTarget == playerContext.CurrentActor.transform,
                "Disabled culling controller lost its player binding.");
            playerContext.Bind(playerContext.CurrentActor);
            visibilityController.RefreshBindings();
            Require(visibilityController.LeaderTarget == playerContext.CurrentActor.transform,
                "Idempotent player context bind changed the visibility target.");
            Report.AppendLine("CurrentPolicy=RoomCullingDisabled");
            Report.AppendLine("DisabledVisibilityContract=PASS");
            Report.AppendLine("CullingTraversalAndPerformance=NOT_RUN (feature disabled)");
            WriteReport();
            Debug.Log("[DungeonVisibilityPerformanceVerifier] Disabled visibility/player binding PASS; culling performance NOT_RUN because the authored feature is disabled.");
            Finish(0);
            return;
        }
        VerifyCoreContract();
        pendingContinuation = StationaryContinuation.Initial;
        state = VerifyState.WaitForCulling;
        Wait(4);
    }

    private static void CaptureRuntimeReferences()
    {
        runFlow =
            UnityEngine.Object.FindFirstObjectByType<DungeonRunFlow>();
        Require(runFlow != null, "DungeonRunFlow 누락");
        generator = runFlow.RuntimeDungeon?.Generator;
        Require(generator != null, "DungeonGenerator 누락");
        visibilityController = runFlow.RoomVisibilityController;
        Require(
            visibilityController != null,
            "DungeonRoomVisibilityController 누락");
        roomCulling = visibilityController.RoomCulling;
        Require(roomCulling != null, "AdjacentRoomCulling 누락");
        playerContext = PlayerContext.GetOrCreate();
        Require(
            playerContext != null && playerContext.CurrentActor != null,
            "플레이어 누락");
        activeDungeon = generator.CurrentDungeon;
        Require(activeDungeon != null, "생성 Dungeon 누락");
    }

    private static void VerifyCoreContract()
    {
        Require(
            runFlow.ActiveSeed == EntrySeed,
            $"초기 요청 Seed 전달 불일치: "
            + $"Active={runFlow.ActiveSeed}, Expected={EntrySeed}");
        Require(generator.Status == GenerationStatus.Complete,
            "초기 Dungeon 생성 미완료");
        Require(roomCulling.Ready,
            "AdjacentRoomCulling 준비 미완료");
        Require(roomCulling.AdjacentTileDepth == 2,
            "AdjacentTileDepth가 2가 아님");
        Require(!roomCulling.CullBehindClosedDoors,
            "문 뒤 조기 컬링이 활성화됨");
        Require(
            roomCulling.TargetOverride
                == playerContext.CurrentActor.transform,
            "컬링 TargetOverride가 현재 리더가 아님");
        Require(
            visibilityController.LeaderTarget
                == playerContext.CurrentActor.transform,
            "가시성 컨트롤러 리더 동기화 실패");
    }

    private static void BeginStationaryVerification()
    {
        VerifyCoreContract();
        VerifyCullingContract();
        stationaryVisibilitySignature =
            BuildVisibilitySignature(activeDungeon);
        stationaryVisibilityRevision =
            visibilityController.VisibilityChangeCount;
        stableFramesRemaining = StableFrameCount;
        state = VerifyState.VerifyStationary;
        Wait(1);
    }

    private static void VerifyStationaryFrame()
    {
        Require(
            BuildVisibilitySignature(activeDungeon)
                == stationaryVisibilitySignature,
            "정지 상태에서 방 가시성 집합이 변경됨");
        Require(
            visibilityController.VisibilityChangeCount
                == stationaryVisibilityRevision,
            "정지 상태에서 반복 가시성 이벤트가 발생함");

        stableFramesRemaining--;
        if (stableFramesRemaining > 0)
        {
            Wait(1);
            return;
        }

        ContinueAfterStationaryVerification();
    }

    private static void ContinueAfterStationaryVerification()
    {
        switch (pendingContinuation)
        {
            case StationaryContinuation.Initial:
                traversalTileIndex = 1;
                traversalTileLimit = Mathf.Min(
                    3,
                    activeDungeon.MainPathTiles.Count - 1);
                if (traversalTileLimit <= 0)
                {
                    BeginContextRebindVerification();
                    return;
                }

                MovePlayerToTile(
                    activeDungeon.MainPathTiles[traversalTileIndex]);
                pendingContinuation =
                    StationaryContinuation.Traversal;
                state = VerifyState.WaitForCulling;
                Wait(4);
                break;

            case StationaryContinuation.Traversal:
                traversalTileIndex++;
                if (traversalTileIndex <= traversalTileLimit)
                {
                    MovePlayerToTile(
                        activeDungeon.MainPathTiles[
                            traversalTileIndex]);
                    state = VerifyState.WaitForCulling;
                    Wait(4);
                    return;
                }

                BeginContextRebindVerification();
                break;

            case StationaryContinuation.ContextRebind:
                Require(
                    roomCulling.TargetOverride
                        == playerContext.CurrentActor.transform,
                    "동일 플레이어 재연결 뒤 컬링 대상 동기화 실패");
                Report.AppendLine(
                    "[InitialVisibility] PASS");
                Report.AppendLine(
                    $"RequestedSeed={EntrySeed}, "
                    + $"ChosenSeed={generator.ChosenSeed}, "
                    + $"TraversedMainPathTiles={traversalTileLimit}, "
                    + $"StableFramesPerStop={StableFrameCount}");
                scenarioIndex = 0;
                acceptedScenarioSamples = 0;
                candidateCount = 0;
                BeginCleanup();
                break;

            case StationaryContinuation.ScenarioSample:
                RecordScenarioSample();
                acceptedScenarioSamples++;
                if (scenarioIndex == ScenarioSamples.Length - 1
                    && acceptedScenarioSamples
                        >= SamplesPerScenario)
                {
                    BeginFrameProfiling();
                    return;
                }

                BeginCleanup();
                break;
        }
    }

    private static void BeginContextRebindVerification()
    {
        PlayerActorRuntime actor = playerContext.CurrentActor;
        Require(actor != null, "Player context is empty");
        playerContext.Bind(actor);
        visibilityController.RefreshBindings();
        pendingContinuation = StationaryContinuation.ContextRebind;
        state = VerifyState.WaitForCulling;
        Wait(4);
    }

    private static void BeginCleanup()
    {
        generator.Clear(true);
        state = VerifyState.WaitForClear;
        Wait(3);
    }

    private static void VerifyClearAndPrepareNextGeneration()
    {
        Require(
            runFlow.GeneratedRoot
                .GetComponentsInChildren<Tile>(true).Length == 0,
            "반복 생성 정리 뒤 Tile 인스턴스 잔존");
        Require(
            runFlow.GeneratedRoot.transform.childCount == 0,
            "반복 생성 정리 뒤 생성 Root 자식 잔존");
        Require(
            visibilityController.HiddenTileCount == 0
            && visibilityController.PausedParticleSystemCount == 0
            && visibilityController.PausedAudioSourceCount == 0
            && visibilityController.PausedAnimatorCount == 0,
            "반복 생성 정리 뒤 가시성 상태 잔존");
        Require(
            CountRuntimeMaterialReferences(runFlow.GeneratedRoot) == 0,
            "반복 생성 정리 뒤 runtime Material 참조 잔존");
        cleanupCheckCount++;

        if (acceptedScenarioSamples >= SamplesPerScenario)
        {
            scenarioIndex++;
            acceptedScenarioSamples = 0;
            candidateCount = 0;
        }

        Require(
            scenarioIndex < ScenarioSamples.Length,
            "프로파일 시나리오 인덱스 초과");
        state = VerifyState.GenerateCandidate;
        Wait(1);
    }

    private static void GenerateCandidate()
    {
        candidateCount++;
        Require(
            candidateCount <= MaximumCandidatesPerScenario,
            GetScenarioName((ScenarioKind)scenarioIndex)
            + " 시나리오의 유효 Seed 확보 실패");

        ScenarioKind scenario = (ScenarioKind)scenarioIndex;
        currentCandidateSeed =
            GetScenarioFirstSeed(scenario) + candidateCount - 1;
        generator.LengthMultiplier =
            scenario == ScenarioKind.LongMainPath ? 1.5f : 1f;
        generator.Seed = currentCandidateSeed;
        generator.ShouldRandomizeSeed = false;
        generator.GenerateAsynchronously = false;

        Stopwatch stopwatch = Stopwatch.StartNew();
        generator.Generate();
        stopwatch.Stop();
        Require(
            generator.Status == GenerationStatus.Complete,
            $"Seed {currentCandidateSeed} 생성 실패: "
            + generator.Status);

        activeDungeon = generator.CurrentDungeon;
        Require(activeDungeon != null, "생성 결과 Dungeon 누락");
        currentGenerationWallMilliseconds =
            stopwatch.Elapsed.TotalMilliseconds;
        currentGenerationStatsMilliseconds =
            generator.GenerationStats.TotalTime;
        currentChosenSeed = generator.ChosenSeed;

        if (!AcceptScenarioResult(scenario, activeDungeon))
        {
            BeginCleanup();
            return;
        }

        MovePlayerToTile(activeDungeon.MainPathTiles[0]);
        pendingContinuation =
            StationaryContinuation.ScenarioSample;
        state = VerifyState.WaitForCulling;
        Wait(4);
    }

    private static bool AcceptScenarioResult(
        ScenarioKind scenario,
        Dungeon dungeon)
    {
        int mainPathCount = dungeon.MainPathTiles.Count;
        int branchCount = dungeon.BranchPathTiles.Count;
        return scenario switch
        {
            ScenarioKind.ShortMainPath =>
                mainPathCount is >= 6 and <= 8,
            ScenarioKind.LongMainPath =>
                mainPathCount is >= 10 and <= 12,
            ScenarioKind.BranchIncluded =>
                mainPathCount is >= 6 and <= 8
                && branchCount > 0,
            _ => false
        };
    }

    private static void RecordScenarioSample()
    {
        ScenarioKind scenario = (ScenarioKind)scenarioIndex;
        int generatedObjectCount =
            runFlow.GeneratedRoot
                .GetComponentsInChildren<Transform>(true).Length - 1;
        int runtimeMaterialCount =
            CountRuntimeMaterialReferences(runFlow.GeneratedRoot);
        peakGeneratedObjectCount = Mathf.Max(
            peakGeneratedObjectCount,
            generatedObjectCount);
        peakRuntimeMaterialCount = Mathf.Max(
            peakRuntimeMaterialCount,
            runtimeMaterialCount);

        Require(
            runFlow.GeneratedRoot
                .GetComponentsInChildren<Tile>(true).Length
                == activeDungeon.AllTiles.Count,
            "생성 Root Tile 수와 Dungeon 목록 불일치");
        Require(
            visibilityController.HiddenTileCount
                <= activeDungeon.AllTiles.Count,
            "숨은 Tile 상태 수가 현재 Dungeon 수를 초과함");

        ScenarioSamples[scenarioIndex].Add(
            new GenerationSample(
                currentCandidateSeed,
                currentChosenSeed,
                activeDungeon.MainPathTiles.Count,
                activeDungeon.BranchPathTiles.Count,
                activeDungeon.AllTiles.Count,
                currentGenerationWallMilliseconds,
                currentGenerationStatsMilliseconds,
                visibilityController.HiddenTileCount,
                generatedObjectCount,
                runtimeMaterialCount));
    }

    private static void BeginFrameProfiling()
    {
        frameCounters?.Dispose();
        frameCounters = new FrameCounterSet();
        MainThreadMilliseconds.Reset();
        RenderThreadMilliseconds.Reset();
        GpuFrameMilliseconds.Reset();
        GcAllocatedBytes.Reset();
        SetPassCalls.Reset();
        DrawCalls.Reset();
        profileFramesCaptured = 0;
        FrameTimingManager.CaptureFrameTimings();
        state = VerifyState.ProfileFrames;
        Wait(5);
    }

    private static void CaptureProfileFrame()
    {
        MainThreadMilliseconds.Add(
            frameCounters.MainThreadMilliseconds);
        RenderThreadMilliseconds.Add(
            frameCounters.RenderThreadMilliseconds);
        GcAllocatedBytes.Add(frameCounters.GcAllocatedBytes);
        SetPassCalls.Add(frameCounters.SetPassCalls);
        DrawCalls.Add(frameCounters.DrawCalls);

        uint timingCount = FrameTimingManager.GetLatestTimings(
            1,
            FrameTimings);
        if (timingCount > 0
            && FrameTimings[0].gpuFrameTime > 0d)
        {
            GpuFrameMilliseconds.Add(
                FrameTimings[0].gpuFrameTime);
        }

        FrameTimingManager.CaptureFrameTimings();
        profileFramesCaptured++;
        if (profileFramesCaptured < ProfileFrameCount)
        {
            Wait(1);
            return;
        }

        BuildSuccessReport();
        WriteReport();
        Debug.Log(Report.ToString().TrimEnd());
        Finish(0);
    }

    private static void MovePlayerToTile(Tile tile)
    {
        Require(tile != null, "이동 대상 Tile 누락");
        DungeonRoomAnchor[] anchors =
            tile.GetComponentsInChildren<DungeonRoomAnchor>(true);
        DungeonRoomAnchor anchor = null;
        for (int i = 0; i < anchors.Length; i++)
        {
            if (anchors[i] != null
                && anchors[i].Supports(DungeonRoomAnchorRole.Spawn))
            {
                anchor = anchors[i];
                break;
            }
        }

        Require(anchor != null, tile.name + ": Spawn Anchor 누락");
        ActorTeleportUtility.TeleportSafely(
            playerContext.CurrentActor.transform,
            anchor.transform.position,
            anchor.transform.rotation);
        visibilityController.RefreshBindings();
        Physics.SyncTransforms();
    }

    private static void VerifyCullingContract()
    {
        Require(activeDungeon != null, "검증 Dungeon 누락");
        Tile currentTile = FindCurrentTile(
            activeDungeon,
            playerContext.CurrentActor.transform.position);
        Require(currentTile != null,
            "현재 리더가 어떤 Dungeon Tile에도 속하지 않음");

        HashSet<Tile> expectedVisible =
            BuildExpectedVisibleTiles(currentTile, 2);
        int actualVisibleCount = 0;
        int hiddenRendererEnabledCount = 0;
        int hiddenLightEnabledCount = 0;
        int hiddenPlayingParticleCount = 0;
        int hiddenPlayingAudioCount = 0;

        for (int tileIndex = 0;
             tileIndex < activeDungeon.AllTiles.Count;
             tileIndex++)
        {
            Tile tile = activeDungeon.AllTiles[tileIndex];
            bool expected = expectedVisible.Contains(tile);
            bool actual = roomCulling.IsTileVisible(tile);
            Require(
                expected == actual,
                $"{tile.name}: 예상 가시성={expected}, 실제={actual}");

            if (actual)
            {
                actualVisibleCount++;
                Require(
                    !visibilityController.IsTileManagedAsHidden(tile),
                    tile.name + ": 보이는 Tile이 숨김 상태로 관리됨");
                continue;
            }

            Require(
                visibilityController.IsTileManagedAsHidden(tile),
                tile.name + ": 숨은 Tile ambient 상태 미관리");

            Renderer[] renderers =
                tile.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer != null
                    && renderer.enabled
                    && renderer.gameObject.activeInHierarchy
                    && renderer.GetComponentInParent<Door>() == null)
                {
                    hiddenRendererEnabledCount++;
                }
            }

            Light[] lights =
                tile.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null
                    && lights[i].enabled
                    && lights[i].gameObject.activeInHierarchy)
                {
                    hiddenLightEnabledCount++;
                }
            }

            ParticleSystem[] particles =
                tile.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i] != null
                    && particles[i].gameObject.activeInHierarchy
                    && particles[i].isPlaying)
                {
                    hiddenPlayingParticleCount++;
                }
            }

            AudioSource[] sources =
                tile.GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < sources.Length; i++)
            {
                if (sources[i] != null
                    && sources[i].isActiveAndEnabled
                    && sources[i].isPlaying)
                {
                    hiddenPlayingAudioCount++;
                }
            }
        }

        Require(
            actualVisibleCount == expectedVisible.Count,
            "Depth 2 가시 방 수 불일치");
        Require(
            actualVisibleCount < activeDungeon.AllTiles.Count,
            "모든 방이 보이는 상태라 컬링 검증 불가");
        Require(
            hiddenRendererEnabledCount == 0,
            "숨은 방 Renderer 활성 잔존="
            + hiddenRendererEnabledCount);
        Require(
            hiddenLightEnabledCount == 0,
            "숨은 방 Light 활성 잔존="
            + hiddenLightEnabledCount);
        Require(
            hiddenPlayingParticleCount == 0,
            "숨은 방 Particle 재생 잔존="
            + hiddenPlayingParticleCount);
        Require(
            hiddenPlayingAudioCount == 0,
            "숨은 방 Audio 재생 잔존="
            + hiddenPlayingAudioCount);
    }

    private static Tile FindCurrentTile(
        Dungeon dungeon,
        Vector3 position)
    {
        for (int i = 0; i < dungeon.AllTiles.Count; i++)
        {
            Tile tile = dungeon.AllTiles[i];
            if (tile != null && tile.Bounds.Contains(position))
                return tile;
        }

        return null;
    }

    private static HashSet<Tile> BuildExpectedVisibleTiles(
        Tile start,
        int depth)
    {
        List<Tile> visible = new() { start };
        int processStart = 0;
        for (int currentDepth = 0;
             currentDepth < depth;
             currentDepth++)
        {
            int processEnd = visible.Count;
            for (int tileIndex = processStart;
                 tileIndex < processEnd;
                 tileIndex++)
            {
                Tile tile = visible[tileIndex];
                for (int doorwayIndex = 0;
                     doorwayIndex < tile.UsedDoorways.Count;
                     doorwayIndex++)
                {
                    Doorway doorway =
                        tile.UsedDoorways[doorwayIndex];
                    Tile adjacent =
                        doorway?.ConnectedDoorway?.Tile;
                    if (adjacent != null
                        && !visible.Contains(adjacent))
                    {
                        visible.Add(adjacent);
                    }
                }
            }

            processStart = processEnd;
        }

        return new HashSet<Tile>(visible);
    }

    private static int BuildVisibilitySignature(Dungeon dungeon)
    {
        unchecked
        {
            int hash = 17;
            for (int i = 0; i < dungeon.AllTiles.Count; i++)
            {
                Tile tile = dungeon.AllTiles[i];
                hash = hash * 31
                    + (tile != null ? tile.GetInstanceID() : 0);
                hash = hash * 31
                    + (tile != null
                        && roomCulling.IsTileVisible(tile)
                            ? 1
                            : 0);
            }

            return hash;
        }
    }

    private static int CountRuntimeMaterialReferences(GameObject root)
    {
        HashSet<int> materialIds = new();
        Renderer[] renderers =
            root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Material[] materials = renderers[i].sharedMaterials;
            for (int materialIndex = 0;
                 materialIndex < materials.Length;
                 materialIndex++)
            {
                Material material = materials[materialIndex];
                if (material != null
                    && !EditorUtility.IsPersistent(material))
                {
                    materialIds.Add(material.GetInstanceID());
                }
            }
        }

        return materialIds.Count;
    }

    private static int GetScenarioFirstSeed(ScenarioKind scenario)
    {
        return scenario switch
        {
            ScenarioKind.ShortMainPath => 34001,
            ScenarioKind.LongMainPath => 35001,
            ScenarioKind.BranchIncluded => 36001,
            _ => 37001
        };
    }

    private static string GetScenarioName(ScenarioKind scenario)
    {
        return scenario switch
        {
            ScenarioKind.ShortMainPath => "MainPath_6_8",
            ScenarioKind.LongMainPath => "MainPath_10_12",
            ScenarioKind.BranchIncluded => "Branch_Included",
            _ => scenario.ToString()
        };
    }

    private static void BuildSuccessReport()
    {
        Report.Insert(
            0,
            "[DungeonVisibilityPerformancePlayModeVerifier] PASS\n");
        for (int i = 0; i < ScenarioSamples.Length; i++)
        {
            ScenarioKind scenario = (ScenarioKind)i;
            List<GenerationSample> samples = ScenarioSamples[i];
            Report.AppendLine();
            Report.AppendLine("[" + GetScenarioName(scenario) + "]");
            for (int sampleIndex = 0;
                 sampleIndex < samples.Count;
                 sampleIndex++)
            {
                Report.AppendLine(samples[sampleIndex].Format());
            }

            Report.AppendLine(
                "GenerationWallMs="
                + MetricSeries.Format(
                    ExtractGenerationValues(samples, true)));
            Report.AppendLine(
                "GenerationStatsMs="
                + MetricSeries.Format(
                    ExtractGenerationValues(samples, false)));
        }

        Report.AppendLine();
        Report.AppendLine("[StableFrameProfile]");
        Report.AppendLine(
            $"FrameCount={profileFramesCaptured}");
        Report.AppendLine(
            "MainThreadMs=" + MainThreadMilliseconds.Format());
        Report.AppendLine(
            "RenderThreadMs=" + RenderThreadMilliseconds.Format());
        Report.AppendLine(
            "GpuFrameMs=" + GpuFrameMilliseconds.Format());
        Report.AppendLine(
            "GCAllocatedBytes=" + GcAllocatedBytes.Format());
        Report.AppendLine(
            "SetPassCalls=" + SetPassCalls.Format());
        Report.AppendLine(
            "DrawCalls=" + DrawCalls.Format());
        Report.AppendLine(frameCounters.FormatValidity());
        Report.AppendLine();
        Report.AppendLine("[Cleanup]");
        Report.AppendLine($"CleanupCheckCount={cleanupCheckCount}");
        Report.AppendLine(
            $"PeakGeneratedObjectCount={peakGeneratedObjectCount}");
        Report.AppendLine(
            $"PeakRuntimeMaterialReferenceCount="
            + peakRuntimeMaterialCount);
        Report.AppendLine("LeakedTileCountAfterClear=0");
        Report.AppendLine("LeakedRuntimeMaterialReferenceAfterClear=0");
        Report.AppendLine("AdjacentTileDepth=2");
        Report.AppendLine("StationaryVisibilityFlicker=0");
        Report.AppendLine("TargetHardwarePass=Deferred");
    }

    private static List<double> ExtractGenerationValues(
        List<GenerationSample> samples,
        bool wallTime)
    {
        List<double> values = new(samples.Count);
        for (int i = 0; i < samples.Count; i++)
        {
            values.Add(
                wallTime
                    ? samples[i].WallMilliseconds
                    : samples[i].StatsMilliseconds);
        }

        return values;
    }

    private static void WriteReport()
    {
        Directory.CreateDirectory("Logs");
        File.WriteAllText(ReportPath, Report.ToString());
        if (GpuFrameMilliseconds.Count > 0
            && Report.ToString().StartsWith(
                "[DungeonVisibilityPerformancePlayModeVerifier] PASS",
                StringComparison.Ordinal))
        {
            File.WriteAllText(
                RenderedReportPath,
                Report.ToString());
        }
    }

    private static void Finish(int exitCode)
    {
        if (generator != null)
        {
            generator.LengthMultiplier = 1f;
            generator.GenerateAsynchronously = true;
        }

        frameCounters?.Dispose();
        frameCounters = null;
        SessionState.SetInt(ExitCodeKey, exitCode);
        EditorApplication.update -= UpdateVerification;
        EditorApplication.ExitPlaymode();
    }

    private static void Wait(int frameDelay)
    {
        waitUntilFrame = Time.frameCount + frameDelay;
        timeoutAt =
            Time.realtimeSinceStartup + StepTimeoutSeconds;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private readonly struct GenerationSample
    {
        public GenerationSample(
            int seed,
            int chosenSeed,
            int mainPathCount,
            int branchCount,
            int tileCount,
            double wallMilliseconds,
            double statsMilliseconds,
            int hiddenTileCount,
            int generatedObjectCount,
            int runtimeMaterialCount)
        {
            Seed = seed;
            ChosenSeed = chosenSeed;
            MainPathCount = mainPathCount;
            BranchCount = branchCount;
            TileCount = tileCount;
            WallMilliseconds = wallMilliseconds;
            StatsMilliseconds = statsMilliseconds;
            HiddenTileCount = hiddenTileCount;
            GeneratedObjectCount = generatedObjectCount;
            RuntimeMaterialCount = runtimeMaterialCount;
        }

        public int Seed { get; }
        public int ChosenSeed { get; }
        public int MainPathCount { get; }
        public int BranchCount { get; }
        public int TileCount { get; }
        public double WallMilliseconds { get; }
        public double StatsMilliseconds { get; }
        public int HiddenTileCount { get; }
        public int GeneratedObjectCount { get; }
        public int RuntimeMaterialCount { get; }

        public string Format()
        {
            return $"RequestedSeed={Seed}, ChosenSeed={ChosenSeed}, "
                + $"Main={MainPathCount}, "
                + $"Branch={BranchCount}, Tiles={TileCount}, "
                + $"WallMs={WallMilliseconds:F3}, "
                + $"StatsMs={StatsMilliseconds:F3}, "
                + $"Hidden={HiddenTileCount}, "
                + $"Objects={GeneratedObjectCount}, "
                + $"RuntimeMaterials={RuntimeMaterialCount}";
        }
    }

    private sealed class MetricSeries
    {
        private readonly List<double> values =
            new(ProfileFrameCount);

        public int Count => values.Count;

        public void Reset()
        {
            values.Clear();
        }

        public void Add(double value)
        {
            if (value < 0d
                || double.IsNaN(value)
                || double.IsInfinity(value))
            {
                return;
            }

            values.Add(value);
        }

        public string Format()
        {
            return Format(values);
        }

        public static string Format(IReadOnlyList<double> source)
        {
            if (source == null || source.Count == 0)
                return "Unavailable";

            List<double> sorted = new(source.Count);
            double total = 0d;
            double maximum = 0d;
            for (int i = 0; i < source.Count; i++)
            {
                double value = source[i];
                sorted.Add(value);
                total += value;
                maximum = Math.Max(maximum, value);
            }

            sorted.Sort();
            int p95Index = Mathf.Clamp(
                Mathf.CeilToInt(sorted.Count * 0.95f) - 1,
                0,
                sorted.Count - 1);
            return $"Mean={total / source.Count:F3}, "
                + $"P95={sorted[p95Index]:F3}, "
                + $"Max={maximum:F3}";
        }
    }

    private sealed class FrameCounterSet : IDisposable
    {
        private readonly CounterProbe mainThread =
            new(ProfilerCategory.Internal, "Main Thread", true);
        private readonly CounterProbe renderThread =
            new(ProfilerCategory.Internal, "Render Thread", true);
        private readonly CounterProbe gcAllocated =
            new(ProfilerCategory.Memory, "GC Allocated In Frame");
        private readonly CounterProbe setPassCalls =
            new(ProfilerCategory.Render, "SetPass Calls Count");
        private readonly CounterProbe drawCalls =
            new(ProfilerCategory.Render, "Draw Calls Count");

        public double MainThreadMilliseconds =>
            mainThread.ReadMilliseconds();
        public double RenderThreadMilliseconds =>
            renderThread.ReadMilliseconds();
        public long GcAllocatedBytes => gcAllocated.ReadValue();
        public long SetPassCalls => setPassCalls.ReadValue();
        public long DrawCalls => drawCalls.ReadValue();

        public string FormatValidity()
        {
            return "[ProfilerCounters]\n"
                + $"{mainThread}\n{renderThread}\n"
                + $"{gcAllocated}\n{setPassCalls}\n{drawCalls}";
        }

        public void Dispose()
        {
            mainThread.Dispose();
            renderThread.Dispose();
            gcAllocated.Dispose();
            setPassCalls.Dispose();
            drawCalls.Dispose();
        }
    }

    private sealed class CounterProbe : IDisposable
    {
        private ProfilerRecorder recorder;
        private readonly bool nanoseconds;

        public CounterProbe(
            ProfilerCategory category,
            string counterName,
            bool nanoseconds = false)
        {
            CategoryName = category.ToString();
            CounterName = counterName;
            this.nanoseconds = nanoseconds;
            try
            {
                recorder = ProfilerRecorder.StartNew(
                    category,
                    counterName,
                    1);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[DungeonVisibilityPerformanceVerifier] "
                    + $"카운터 시작 실패: {category}/{counterName} - "
                    + exception.Message);
            }
        }

        public string CategoryName { get; }
        public string CounterName { get; }

        public long ReadValue()
        {
            return recorder.Valid ? recorder.LastValue : 0L;
        }

        public double ReadMilliseconds()
        {
            return nanoseconds
                ? ReadValue() / 1_000_000d
                : ReadValue();
        }

        public void Dispose()
        {
            recorder.Dispose();
        }

        public override string ToString()
        {
            return $"{CategoryName}/{CounterName}: "
                + $"Valid={recorder.Valid}";
        }
    }
}
