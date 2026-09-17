using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using DunGen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class DungeonRunPlayModeVerifier
{
    private const string ActiveKey = "DungeonRunPlayModeVerifier.Active";
    private const string BatchKey = "DungeonRunPlayModeVerifier.Batch";
    private const string ExitCodeKey = "DungeonRunPlayModeVerifier.ExitCode";
    private const string PersistentScenePath =
        "Assets/ProjectOverburst/00_Scenes/PersistentScene.unity";
    private const int VerificationSeed = 27501;
    private const int FailureRecoverySeed = 27502;
    private const int PlayerSweepFirstSeed = 28501;
    private const int PlayerSweepSeedCount = 20;
    private const float ExpectedHideoutCameraYaw = 0f;
    private const float ExpectedDungeonCameraYaw = 135f;
    private const float CameraYawTolerance = 0.25f;
    private const float MinimumSettledGroundOffset = -0.02f;
    private const float MaximumSettledGroundOffset = 0.16f;
    private const float MinimumSpawnSurfaceNormalY = 0.9f;
    private const float SceneTimeoutSeconds = 60f;

    private enum VerifyStep
    {
        WaitForHideout,
        WaitForDungeon,
        VerifyDungeon,
        SweepPlayerSeeds,
        WaitForHideoutReturn,
        WaitForFailureRecovery
    }

    private static VerifyStep step;
    private static int waitUntilFrame;
    private static float timeoutAt;
    private static bool injectFailureOnDungeonLoad;
    private static bool sawFailureState;
    private static DungeonRunFlow sweepRunFlow;
    private static DungeonGenerator sweepGenerator;
    private static IEnumerator playerPlacementRoutine;
    private static int playerSweepIndex;
    private static readonly StringBuilder PlayerSweepReport = new();
    private static readonly HashSet<string> ExpectedStartTileVariants = new();
    private static readonly HashSet<string> ExpectedStairStartTileVariants = new();
    private static readonly HashSet<string> StartTileVariants = new();
    private static readonly HashSet<string> StairStartTileVariants = new();
    private static bool previousRunInBackground;

    static DungeonRunPlayModeVerifier()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    [MenuItem("OVERBURST/Codex/Validation/Verify Dungeon Run PlayMode")]
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

        SessionState.SetBool(ActiveKey, true);
        SessionState.SetBool(BatchKey, batchMode);
        SessionState.SetInt(ExitCodeKey, 1);
        EditorSceneManager.OpenScene(PersistentScenePath, OpenSceneMode.Single);
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
            step = VerifyStep.WaitForHideout;
            Wait(30, SceneTimeoutSeconds);
            EditorApplication.update -= UpdateVerification;
            EditorApplication.update += UpdateVerification;
            return;
        }

        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            Application.runInBackground = previousRunInBackground;
            EditorApplication.update -= UpdateVerification;
            SceneManager.sceneLoaded -= HandleSceneLoaded;
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
            Debug.Log("[DungeonRunPlayModeVerifier] Verification completed.");
        else
            Debug.LogError("[DungeonRunPlayModeVerifier] Verification failed.");
    }

    private static void UpdateVerification()
    {
        if (!EditorApplication.isPlaying || Time.frameCount < waitUntilFrame)
            return;

        try
        {
            if (Time.realtimeSinceStartup > timeoutAt)
            {
                throw new TimeoutException(
                    "Dungeon run verification timed out at " + step + ".");
            }

            PersistentSceneFlow sceneFlow = PersistentSceneFlow.Instance;
            if (sceneFlow == null)
                return;

            switch (step)
            {
                case VerifyStep.WaitForHideout:
                    if (!IsReady(sceneFlow, PersistentSceneFlow.HideoutSceneName))
                        return;

                    VerifyCameraYaw(
                        ExpectedHideoutCameraYaw,
                        PersistentSceneFlow.HideoutSceneName);
                    Require(
                        RunSceneReadinessRegistry.GetState(
                            PersistentSceneFlow.DungeonRunSceneName)
                        == RunSceneReadinessState.None,
                        "Dungeon readiness state was not clean before entry.");
                    sceneFlow.EnterDungeon(
                        DungeonRunEntryRequest.Create(
                            VerificationSeed,
                            PersistentSceneFlow.HideoutSceneName));
                    step = VerifyStep.WaitForDungeon;
                    Wait(1, SceneTimeoutSeconds);
                    break;

                case VerifyStep.WaitForDungeon:
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

                    if (!IsReady(
                            sceneFlow,
                            PersistentSceneFlow.DungeonRunSceneName))
                    {
                        return;
                    }

                    step = VerifyStep.VerifyDungeon;
                    Wait(2, 10f);
                    break;

                case VerifyStep.VerifyDungeon:
                    VerifyCameraYaw(
                        ExpectedDungeonCameraYaw,
                        PersistentSceneFlow.DungeonRunSceneName);
                    sweepRunFlow = VerifyGeneratedRun(sceneFlow);
                    playerSweepIndex = 0;
                    PlayerSweepReport.Clear();
                    ResolveExpectedStartTileVariants(
                        sweepRunFlow.Definition);
                    StartTileVariants.Clear();
                    StairStartTileVariants.Clear();
                    PlayerSweepReport.AppendLine(
                        "[DungeonPlayerPlacementSweep] PASS");
                    BeginNextPlayerSweepSeed();
                    step = VerifyStep.SweepPlayerSeeds;
                    Wait(1, SceneTimeoutSeconds);
                    break;

                case VerifyStep.SweepPlayerSeeds:
                    if (playerPlacementRoutine != null
                        && playerPlacementRoutine.MoveNext())
                    {
                        return;
                    }

                    playerPlacementRoutine = null;
                    VerifyPlayerSweepSeed();
                    playerSweepIndex++;
                    if (playerSweepIndex < PlayerSweepSeedCount)
                    {
                        BeginNextPlayerSweepSeed();
                        Wait(1, SceneTimeoutSeconds);
                        break;
                    }

                    sweepGenerator.GenerateAsynchronously = true;
                    Require(
                        StartTileVariants.SetEquals(
                            ExpectedStartTileVariants),
                        "Start tile coverage mismatch: actual="
                        + string.Join(",", StartTileVariants)
                        + ", expected="
                        + string.Join(",", ExpectedStartTileVariants)
                        + ".");
                    Require(
                        StairStartTileVariants.SetEquals(
                            ExpectedStairStartTileVariants),
                        "Stair-bearing start tile coverage mismatch: "
                        + "actual="
                        + string.Join(",", StairStartTileVariants)
                        + ", expected="
                        + string.Join(",", ExpectedStairStartTileVariants)
                        + ".");
                    PlayerSweepReport.AppendLine(
                        $"SeedCount={PlayerSweepSeedCount}");
                    PlayerSweepReport.AppendLine(
                        "StartTileVariants="
                        + string.Join(",", StartTileVariants));
                    PlayerSweepReport.AppendLine(
                        "StairStartTileVariants="
                        + string.Join(",", StairStartTileVariants));
                    PlayerSweepReport.AppendLine("FailedSeedCount=0");
                    Debug.Log(PlayerSweepReport.ToString().TrimEnd());
                    sceneFlow.SwitchHubScene(
                        PersistentSceneFlow.HideoutSceneName);
                    step = VerifyStep.WaitForHideoutReturn;
                    Wait(1, SceneTimeoutSeconds);
                    break;

                case VerifyStep.WaitForHideoutReturn:
                    if (!IsReady(sceneFlow, PersistentSceneFlow.HideoutSceneName))
                        return;

                    VerifyCameraYaw(
                        ExpectedDungeonCameraYaw,
                        PersistentSceneFlow.HideoutSceneName);
                    VerifyDungeonCleanup();
                    injectFailureOnDungeonLoad = true;
                    sawFailureState = false;
                    SceneManager.sceneLoaded -= HandleSceneLoaded;
                    SceneManager.sceneLoaded += HandleSceneLoaded;
                    sceneFlow.EnterDungeon(
                        DungeonRunEntryRequest.Create(
                            FailureRecoverySeed,
                            PersistentSceneFlow.HideoutSceneName));
                    step = VerifyStep.WaitForFailureRecovery;
                    Wait(1, SceneTimeoutSeconds);
                    break;

                case VerifyStep.WaitForFailureRecovery:
                    if (RunSceneReadinessRegistry.GetState(
                            PersistentSceneFlow.DungeonRunSceneName)
                        == RunSceneReadinessState.Failed)
                    {
                        sawFailureState = true;
                    }

                    if (!IsReady(sceneFlow, PersistentSceneFlow.HideoutSceneName))
                        return;

                    VerifyDungeonCleanup();
                    Require(
                        sawFailureState,
                        "Failure readiness state was not observed.");
                    Require(
                        !DungeonRunLaunchContextHolder.HasRequest,
                        "Failed dungeon request remained after recovery.");
                    Debug.Log(
                        "[DungeonRunPlayModeVerifier] PASS "
                        + $"seed={VerificationSeed} async=1 readiness=1 "
                        + "requestConsumed=1 returnCleanup=1 "
                        + $"playerPlacementSeeds={PlayerSweepSeedCount} "
                        + "cameraYaw=-45/135 lighting=1 roomCullingDisabled=1 "
                        + "failureRecovery=1");
                    Finish(0);
                    break;
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Finish(1);
        }
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!injectFailureOnDungeonLoad
            || scene.name != PersistentSceneFlow.DungeonRunSceneName)
        {
            return;
        }

        injectFailureOnDungeonLoad = false;
        SceneManager.sceneLoaded -= HandleSceneLoaded;

        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            DungeonRunFlow runFlow =
                roots[i].GetComponentInChildren<DungeonRunFlow>(true);
            if (runFlow == null)
                continue;

            runFlow.Configure(
                runFlow.RuntimeDungeon,
                null,
                runFlow.GeneratedRoot);
            return;
        }

        throw new InvalidOperationException(
            "DungeonRunFlow was not found for failure injection.");
    }

    private static DungeonRunFlow VerifyGeneratedRun(
        PersistentSceneFlow sceneFlow)
    {
        Require(
            RunSceneReadinessRegistry.GetState(
                PersistentSceneFlow.DungeonRunSceneName)
            == RunSceneReadinessState.Ready,
            "Dungeon readiness state was not Ready.");
        Require(
            !DungeonRunLaunchContextHolder.HasRequest,
            "Dungeon entry request was not consumed.");

        DungeonRunFlow runFlow =
            UnityEngine.Object.FindFirstObjectByType<DungeonRunFlow>();
        Require(runFlow != null, "DungeonRunFlow was not found.");
        Require(
            runFlow.gameObject.scene.name
            == PersistentSceneFlow.DungeonRunSceneName,
            "DungeonRunFlow belongs to the wrong scene.");
        Require(
            runFlow.ActiveSeed == VerificationSeed,
            $"Requested seed was not retained: {runFlow.ActiveSeed}.");
        Require(
            runFlow.ReturnContext != null
            && runFlow.ReturnContext.TargetSceneName
                == PersistentSceneFlow.HideoutSceneName
            && runFlow.ReturnContext.ReturnPointId == "Default",
            "Dungeon return context was not preserved.");

        RuntimeDungeon runtimeDungeon = runFlow.RuntimeDungeon;
        Require(runtimeDungeon != null, "RuntimeDungeon reference is missing.");
        DungeonGenerator generator = runtimeDungeon.Generator;
        Require(generator != null, "DunGen generator is missing.");
        Require(
            generator.GenerateAsynchronously,
            "Runtime generation did not use async mode.");
        Require(
            generator.Status == GenerationStatus.Complete,
            "DunGen generator did not complete.");
        Require(
            generator.CurrentDungeon != null
            && generator.CurrentDungeon.AllTiles.Count >= 2
            && generator.CurrentDungeon.MainPathTiles.Count >= 2,
            "Generated dungeon content was incomplete.");
        DungeonRunGenerator projectGenerator =
            generator as DungeonRunGenerator;
        Require(
            DungeonEndCapContract.TryValidateFinalOpenDoorwayPass(
                generator.CurrentDungeon,
                projectGenerator,
                runFlow.Definition.EndCapTileSet,
                out string endCapError),
            endCapError);
        Require(
            runFlow.AcceptedLayoutAttempt >= 1
            && runFlow.AcceptedLayoutAttempt
                <= runFlow.Definition.LayoutCandidateCount
            && runFlow.AcceptedLayoutQuality.IsAccepted,
            "Runtime layout quality gate did not accept the dungeon.");
        DungeonLayoutQualityEvaluation layoutQuality =
            DungeonLayoutQualityEvaluator.Evaluate(
                generator.CurrentDungeon,
                runFlow.Definition.MinimumBranchTileCount,
                runFlow.Definition.MaximumPlanarAspectRatio,
                runFlow.Definition.EndCapTileSet);
        Require(
            layoutQuality.IsAccepted,
            "Generated dungeon violates the runtime layout policy: "
            + layoutQuality.RejectionReason);
        Require(
            runFlow.ActiveParameters.DifficultyLevel
                == runFlow.Definition.DefaultDifficultyLevel
            && runFlow.ActiveParameters.RoomCount
                == runFlow.Definition.DefaultRoomCount
            && runFlow.ActiveParameters.ArenaCount
                == runFlow.Definition.DefaultArenaCount
            && runFlow.ActiveParameters.TotalMainPathTileCount
                == runFlow.Definition.DefaultTotalMainPathTileCount,
            "Dungeon run parameters did not resolve from the definition.");
        Require(
            Mathf.Approximately(generator.LengthMultiplier, 1f)
            && generator.CurrentDungeon.MainPathTiles.Count
                == runFlow.ActiveParameters.TotalMainPathTileCount,
            "Runtime dungeon did not match the direct Main Path length policy.");
        for (int tileIndex = 0;
             tileIndex < generator.CurrentDungeon.AllTiles.Count;
             tileIndex++)
        {
            Require(
                generator.CurrentDungeon.AllTiles[tileIndex]
                    .GetComponentInChildren<DungeonPlayRoomAuthoring>(true)
                    == null,
                "Auto-generated PlayRoom tile entered the runtime dungeon.");
        }
        Require(
            sceneFlow.CurrentSubSceneName
                == PersistentSceneFlow.DungeonRunSceneName,
            "Persistent scene flow did not own the dungeon scene.");
        VerifyPlayerPlacement(runFlow, generator.CurrentDungeon);
        VerifyRuntimeLighting(runFlow);
        VerifyRoomCullingDisabled(runFlow);
        return runFlow;
    }

    private static void VerifyRoomCullingDisabled(
        DungeonRunFlow runFlow)
    {
        DungeonRoomVisibilityController controller =
            runFlow.RoomVisibilityController;
        Require(
            controller != null && controller.RoomCulling != null,
            "Dungeon room visibility references are missing.");
        Require(
            !controller.RoomCulling.enabled
            && !controller.RoomCulling.Ready,
            "AdjacentRoomCulling was not disabled at runtime.");
        Require(
            controller.HiddenTileCount == 0
            && controller.VisibilityChangeCount == 0
            && controller.PausedParticleSystemCount == 0
            && controller.PausedAudioSourceCount == 0
            && controller.PausedAnimatorCount == 0,
            "Disabled room culling still changed tile visibility.");
    }

    private static void VerifyRuntimeLighting(DungeonRunFlow runFlow)
    {
        Transform shadowLightTransform = runFlow.transform.Find(
            DungeonRunSceneAuthoringBuilder.ShadowLightObjectName);
        Light shadowLight = shadowLightTransform != null
            ? shadowLightTransform.GetComponent<Light>()
            : null;
        Require(
            shadowLight != null
            && shadowLight.enabled
            && DungeonRunSceneAuthoringBuilder
                .IsShadowLightPolicyCurrent(shadowLight)
            && RenderSettings.sun == shadowLight,
            "Dungeon runtime shadow light policy was not active.");

        PlayerContext partyRuntime = PlayerContext.GetOrCreate();
        Require(
            partyRuntime != null && partyRuntime.CurrentActor != null,
            "Player was not ready for ambient-light verification.");
        {
            const int memberIndex = 0;
            PlayerActorRuntime member = partyRuntime.CurrentActor;
            Transform visualRoot = member != null
                ? member.transform.Find("VisualRoot")
                : null;
            Transform lightTransform = visualRoot != null
                ? visualRoot.Find(
                    PlayerAmbientLightAuthoringBuilder
                        .LightObjectName)
                : null;
            Light playerLight = lightTransform != null
                ? lightTransform.GetComponent<Light>()
                : null;
            Require(
                playerLight != null
                && playerLight.enabled
                && PlayerAmbientLightAuthoringBuilder
                    .IsPolicyCurrent(playerLight),
                $"Player actor {memberIndex} ambient light "
                + "policy mismatch.");
        }
    }

    private static void BeginNextPlayerSweepSeed()
    {
        Require(sweepRunFlow != null, "Player sweep run flow is missing.");
        sweepGenerator = sweepRunFlow.RuntimeDungeon.Generator;
        Require(sweepGenerator != null, "Player sweep generator is missing.");

        int seed = PlayerSweepFirstSeed + playerSweepIndex;
        sweepGenerator.Clear(true);
        sweepGenerator.Seed = seed;
        sweepGenerator.ShouldRandomizeSeed = false;
        sweepGenerator.GenerateAsynchronously = false;
        sweepGenerator.Generate();
        Require(
            sweepGenerator.Status == GenerationStatus.Complete,
            $"Player sweep seed {seed} generation failed: "
            + sweepGenerator.Status);
        Physics.SyncTransforms();

        DungeonPlayerSpawnPlacer placer =
            sweepRunFlow.PlayerSpawnPlacer;
        Require(placer != null, "Player sweep spawn placer is missing.");
        playerPlacementRoutine =
            placer.PlacePlayer(sweepGenerator.CurrentDungeon);
    }

    private static void VerifyPlayerSweepSeed()
    {
        int seed = PlayerSweepFirstSeed + playerSweepIndex;
        VerifyPlayerPlacement(
            sweepRunFlow,
            sweepGenerator.CurrentDungeon);
        int instantiatedTileCount =
            sweepRunFlow.GeneratedRoot
                .GetComponentsInChildren<Tile>(true).Length;
        Require(
            instantiatedTileCount
                == sweepGenerator.CurrentDungeon.AllTiles.Count,
            $"Player sweep seed {seed} tile instance count mismatch: "
                + $"{instantiatedTileCount}/"
                + $"{sweepGenerator.CurrentDungeon.AllTiles.Count}.");

        Tile startTile = sweepGenerator.CurrentDungeon.MainPathTiles[0];
        string startTileName = startTile.name.Replace("(Clone)", string.Empty);
        StartTileVariants.Add(startTileName);
        if (startTile.GetComponentsInChildren<DungeonStairRampProxy>(true)
                .Length > 0)
        {
            StairStartTileVariants.Add(startTileName);
        }

        PlayerSweepReport.AppendLine(
            $"Seed={seed}, Chosen={sweepGenerator.ChosenSeed}, "
            + $"StartTile={startTileName}, "
            + $"Tiles={sweepGenerator.CurrentDungeon.AllTiles.Count}, "
            + $"MainPath="
            + sweepGenerator.CurrentDungeon.MainPathTiles.Count);
    }

    private static void ResolveExpectedStartTileVariants(
        DungeonRunDefinition definition)
    {
        ExpectedStartTileVariants.Clear();
        ExpectedStairStartTileVariants.Clear();
        TileSet startTileSet = null;
        if (definition?.DungeonFlow?.Nodes != null)
        {
            foreach (DunGen.Graph.GraphNode node
                     in definition.DungeonFlow.Nodes)
            {
                if (node.NodeType != DunGen.Graph.NodeType.Start
                    || node.TileSets == null
                    || node.TileSets.Count == 0)
                {
                    continue;
                }

                startTileSet = node.TileSets[0];
                break;
            }
        }

        Require(
            startTileSet?.TileWeights?.Weights != null,
            "Start TileSet is missing.");

        foreach (GameObjectChance weight in startTileSet.TileWeights.Weights)
        {
            GameObject prefab = weight?.Value;
            if (prefab == null)
                continue;

            ExpectedStartTileVariants.Add(prefab.name);
            if (prefab.GetComponentsInChildren<DungeonStairRampProxy>(true)
                    .Length > 0)
            {
                ExpectedStairStartTileVariants.Add(prefab.name);
            }
        }

        Require(
            ExpectedStartTileVariants.Count > 0,
            "Start TileSet contains no valid prefab.");
    }

    private static void VerifyPlayerPlacement(
        DungeonRunFlow runFlow,
        Dungeon dungeon)
    {
        DungeonPlayerSpawnPlacer placer = runFlow.PlayerSpawnPlacer;
        Require(placer != null, "Dungeon player spawn placer is missing.");
        Require(
            placer.PlacementSucceeded,
            "Dungeon player placement did not complete: "
            + placer.FailureMessage);
        Require(
            placer.LastStartAnchor != null,
            "Dungeon start anchor was not retained after placement.");
        Require(
            dungeon != null
            && dungeon.MainPathTiles.Count > 0
            && placer.LastStartAnchor.transform.IsChildOf(
                dungeon.MainPathTiles[0].transform),
            "Dungeon player was not placed from the main-path start tile.");

        PlayerContext partyRuntime = PlayerContext.GetOrCreate();
        Require(
            partyRuntime != null && partyRuntime.CurrentActor != null,
            "Player was not available after dungeon placement.");

        int groundLayer = LayerMask.NameToLayer("Ground");
        Require(groundLayer >= 0, "Ground layer is missing.");
        int groundMask = 1 << groundLayer;
        Tile startTile = dungeon.MainPathTiles[0];
        {
            const int memberIndex = 0;
            PlayerActorRuntime member = partyRuntime.CurrentActor;
            Require(
                member != null,
                $"Player actor {memberIndex} is missing.");
            Vector3 expected = placer.LastSpawnPosition;
            Require(
                Vector3.Distance(member.transform.position, expected) <= 0.8f,
                $"Player actor {memberIndex} is outside its spawn slot.");
            Require(
                TryFindStartTileGroundBelow(
                    member.transform.position,
                    startTile,
                    groundMask,
                    out RaycastHit groundHit),
                $"Player actor {memberIndex} has no start-tile Ground.");
            float actualGroundOffset =
                member.transform.position.y - groundHit.point.y;
            Require(
                actualGroundOffset >= MinimumSettledGroundOffset
                && actualGroundOffset <= MaximumSettledGroundOffset,
                $"Player actor {memberIndex} ground offset mismatch: "
                + $"actual={actualGroundOffset:F3}, expected="
                + $"{MinimumSettledGroundOffset:F2}.."
                + $"{MaximumSettledGroundOffset:F2}.");
            Require(
                !OverlapsStartTileStairRamp(
                    member,
                    startTile,
                    groundMask),
                $"Player actor {memberIndex} overlaps a start-tile "
                + "stair ramp.");
        }
    }

    private static bool TryFindStartTileGroundBelow(
        Vector3 position,
        Tile startTile,
        int groundMask,
        out RaycastHit bestHit)
    {
        RaycastHit[] hits = Physics.RaycastAll(
            position + Vector3.up * 1.5f,
            Vector3.down,
            4f,
            groundMask,
            QueryTriggerInteraction.Ignore);
        bool found = false;
        bestHit = default;
        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider != null
                && hit.collider.transform.IsChildOf(startTile.transform)
                && hit.point.y <= position.y + 0.001f
                && Vector3.Dot(hit.normal, Vector3.up)
                    >= MinimumSpawnSurfaceNormalY
                && (!found || hit.point.y > bestHit.point.y))
            {
                found = true;
                bestHit = hit;
            }
        }

        return found;
    }

    private static bool OverlapsStartTileStairRamp(
        PlayerActorRuntime member,
        Tile startTile,
        int groundMask)
    {
        CharacterController controller =
            member.PlayerKit != null
                ? member.PlayerKit.CharacterController
                : member.GetComponent<CharacterController>();
        Vector3 center;
        float radius;
        float halfSegment;
        if (controller != null)
        {
            center = controller.transform.TransformPoint(controller.center);
            Vector3 scale = controller.transform.lossyScale;
            float horizontalScale = Mathf.Max(
                Mathf.Abs(scale.x),
                Mathf.Abs(scale.z));
            float verticalScale = Mathf.Abs(scale.y);
            radius = controller.radius * horizontalScale;
            float height = Mathf.Max(
                controller.height * verticalScale,
                radius * 2f);
            halfSegment = Mathf.Max(0f, height * 0.5f - radius);
        }
        else
        {
            center = member.transform.position + Vector3.up * 0.8f;
            radius = 0.3f;
            halfSegment = 0.5f;
        }

        Collider[] overlaps = Physics.OverlapCapsule(
            center + Vector3.up * halfSegment,
            center - Vector3.up * halfSegment,
            radius,
            groundMask,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlaps.Length; i++)
        {
            Collider overlap = overlaps[i];
            if (overlap != null
                && overlap.transform.IsChildOf(startTile.transform)
                && overlap.GetComponentInParent<DungeonStairRampProxy>()
                    != null)
            {
                return true;
            }
        }

        return false;
    }

    private static void VerifyDungeonCleanup()
    {
        Require(
            !SceneManager.GetSceneByName(
                PersistentSceneFlow.DungeonRunSceneName).isLoaded,
            "Dungeon scene remained loaded after hub return.");
        Require(
            RunSceneReadinessRegistry.GetState(
                PersistentSceneFlow.DungeonRunSceneName)
            == RunSceneReadinessState.None,
            "Dungeon readiness state remained after scene unload.");
    }

    private static void VerifyCameraYaw(float expectedYaw, string sceneName)
    {
        QuarterViewCamera cameraController = QuarterViewCamera.ActiveInstance;
        Require(
            cameraController != null,
            $"QuarterViewCamera was not active in {sceneName}.");

        float actualYaw = cameraController.transform.eulerAngles.y;
        Require(
            Mathf.Abs(Mathf.DeltaAngle(actualYaw, expectedYaw))
                <= CameraYawTolerance,
            $"Camera Yaw mismatch in {sceneName}: "
            + $"actual={actualYaw:F2}, expected={expectedYaw:F2}.");
    }

    private static bool IsReady(PersistentSceneFlow flow, string sceneName)
    {
        return !flow.IsSwitching && flow.CurrentSubSceneName == sceneName;
    }

    private static void Wait(int frameDelay, float timeoutSeconds)
    {
        waitUntilFrame = Time.frameCount + frameDelay;
        timeoutAt = Time.realtimeSinceStartup + timeoutSeconds;
    }

    private static void Finish(int exitCode)
    {
        SessionState.SetInt(ExitCodeKey, exitCode);
        EditorApplication.update -= UpdateVerification;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        playerPlacementRoutine = null;
        sweepRunFlow = null;
        sweepGenerator = null;
        EditorApplication.ExitPlaymode();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
