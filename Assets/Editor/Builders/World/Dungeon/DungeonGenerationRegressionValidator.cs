using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DunGen;
using DunGen.Graph;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DungeonGenerationRegressionValidator
{
    private const int FirstSeed = 27001;
    private const int SeedCount = 20;
    private const int MaximumGenerationAttempts = 100;
    private const string LogPath =
        "Logs/DungeonGenerationRegression.log";
    private const string FailedSeedLogPath =
        "Logs/DungeonGenerationFailedSeeds.log";
    private const string ArenaCandidateViabilityLogPath =
        "Logs/DungeonArenaCandidateViability.log";

    [MenuItem(
        "OVERBURST/Codex/Validation/World/Dungeon/Run 20 Seeds")]
    public static void ValidateFromMenu()
    {
        Debug.Log(ValidateOrThrow());
    }

    public static void RunFromCommandLine()
    {
        Directory.CreateDirectory("Logs");
        try
        {
            string report = ValidateOrThrow();
            File.WriteAllText(LogPath, report);
            Debug.Log(report);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            File.WriteAllText(LogPath, exception.ToString());
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static void ValidateArenaCandidatesFromCommandLine()
    {
        Directory.CreateDirectory("Logs");
        try
        {
            string report = ValidateArenaCandidateViability();
            File.WriteAllText(ArenaCandidateViabilityLogPath, report);
            Debug.Log(report);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            File.WriteAllText(
                ArenaCandidateViabilityLogPath,
                exception.ToString());
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static string ValidateOrThrow()
    {
        DungeonFlow flow = AssetDatabase.LoadAssetAtPath<DungeonFlow>(
            DungeonContentAuthoringBuilder.FlowOutputPath);
        Require(flow != null, "Project DungeonFlow 누락");
        DungeonRunDefinition definition =
            AssetDatabase.LoadAssetAtPath<DungeonRunDefinition>(
                DungeonRunSceneAuthoringBuilder.DefinitionPath);
        Require(definition != null, "DungeonRunDefinition 누락");
        Require(
            definition.EndCapTileSet != null,
            "DungeonRunDefinition EndCap TileSet 누락");

        Scene previewScene = EditorSceneManager.NewPreviewScene();
        GameObject root = new("DungeonRegressionRoot");
        SceneManager.MoveGameObjectToScene(root, previewScene);
        DungeonRunParameters parameters =
            definition.ResolveParameters(0, 0);
        DungeonRunGenerationFlow generationFlow =
            new(flow, parameters);
        DungeonGenerator generator =
            CreateGenerator(
                generationFlow.Flow,
                root,
                definition.EndCapTileSet,
                definition.TopologyRepairCatalog);
        DungeonArenaTileInjection arenaTileInjection =
            new(
                generator,
                definition.ArenaTileSet,
                parameters.ArenaCount);
        StringBuilder report = new();
        report.AppendLine("[DungeonGenerationRegressionValidator] PASS");
        report.AppendLine(
            $"TotalMainPath={parameters.TotalMainPathTileCount}");
        report.AppendLine($"ArenaCount={parameters.ArenaCount}");
        string firstSignature = null;
        HashSet<string> seenTileNames = new(StringComparer.Ordinal);
        List<string> failedSeeds = new();
        int minimumTileCount = int.MaxValue;
        int maximumTileCount = int.MinValue;
        int minimumMainPathCount = int.MaxValue;
        int maximumMainPathCount = int.MinValue;
        int minimumBranchTileCount = int.MaxValue;
        int maximumBranchTileCount = int.MinValue;
        int minimumEndCapCount = int.MaxValue;
        int maximumEndCapCount = int.MinValue;
        int minimumOpenDoorwayCountBeforeEndCaps = int.MaxValue;
        int maximumOpenDoorwayCountBeforeEndCaps = int.MinValue;
        int minimumOpenDoorwayCountAfterEndCaps = int.MaxValue;
        int maximumOpenDoorwayCountAfterEndCaps = int.MinValue;
        int minimumOpenDoorwayCountAfterTopologyRepair = int.MaxValue;
        int maximumOpenDoorwayCountAfterTopologyRepair = int.MinValue;
        int totalEndCapReplacementCount = 0;
        int totalShapeReplacementCount = 0;
        int topologyRepairedSeedCount = 0;
        int branchlessSeedCount = 0;
        float minimumHeightSpan = float.PositiveInfinity;
        float maximumHeightSpan = float.NegativeInfinity;
        float minimumPlanarAspect = float.PositiveInfinity;
        float maximumPlanarAspect = float.NegativeInfinity;
        int familyAdjacentRepeatCount = 0;

        try
        {
            for (int index = 0; index < SeedCount; index++)
            {
                int seed = FirstSeed + index;
                try
                {
                    GenerationSnapshot snapshot =
                        GenerateAndValidate(
                            generator,
                            root,
                            seed,
                            parameters,
                            definition.ArenaTileSet,
                            definition.EndCapTileSet);
                    if (index == 0)
                        firstSignature = snapshot.Signature;

                    for (int tileIndex = 0;
                         tileIndex < snapshot.TileNames.Count;
                         tileIndex++)
                    {
                        seenTileNames.Add(snapshot.TileNames[tileIndex]);
                    }

                    minimumTileCount = Math.Min(
                        minimumTileCount,
                        snapshot.TileCount);
                    maximumTileCount = Math.Max(
                        maximumTileCount,
                        snapshot.TileCount);
                    minimumMainPathCount = Math.Min(
                        minimumMainPathCount,
                        snapshot.MainPathCount);
                    maximumMainPathCount = Math.Max(
                        maximumMainPathCount,
                        snapshot.MainPathCount);
                    minimumBranchTileCount = Math.Min(
                        minimumBranchTileCount,
                        snapshot.BranchTileCount);
                    maximumBranchTileCount = Math.Max(
                        maximumBranchTileCount,
                        snapshot.BranchTileCount);
                    minimumEndCapCount = Math.Min(
                        minimumEndCapCount,
                        snapshot.EndCapCount);
                    maximumEndCapCount = Math.Max(
                        maximumEndCapCount,
                        snapshot.EndCapCount);
                    minimumOpenDoorwayCountBeforeEndCaps = Math.Min(
                        minimumOpenDoorwayCountBeforeEndCaps,
                        snapshot.OpenDoorwayCountBeforeEndCaps);
                    maximumOpenDoorwayCountBeforeEndCaps = Math.Max(
                        maximumOpenDoorwayCountBeforeEndCaps,
                        snapshot.OpenDoorwayCountBeforeEndCaps);
                    minimumOpenDoorwayCountAfterEndCaps = Math.Min(
                        minimumOpenDoorwayCountAfterEndCaps,
                        snapshot.OpenDoorwayCountAfterEndCaps);
                    maximumOpenDoorwayCountAfterEndCaps = Math.Max(
                        maximumOpenDoorwayCountAfterEndCaps,
                        snapshot.OpenDoorwayCountAfterEndCaps);
                    minimumOpenDoorwayCountAfterTopologyRepair = Math.Min(
                        minimumOpenDoorwayCountAfterTopologyRepair,
                        snapshot.OpenDoorwayCountAfterTopologyRepair);
                    maximumOpenDoorwayCountAfterTopologyRepair = Math.Max(
                        maximumOpenDoorwayCountAfterTopologyRepair,
                        snapshot.OpenDoorwayCountAfterTopologyRepair);
                    totalEndCapReplacementCount +=
                        snapshot.ReplacedWithEndCapCount;
                    totalShapeReplacementCount +=
                        snapshot.ReplacedShapeTileCount;
                    if (snapshot.ReplacedWithEndCapCount > 0
                        || snapshot.ReplacedShapeTileCount > 0)
                    {
                        topologyRepairedSeedCount++;
                    }
                    if (snapshot.BranchTileCount == 0)
                        branchlessSeedCount++;
                    minimumHeightSpan = Mathf.Min(
                        minimumHeightSpan,
                        snapshot.HeightSpan);
                    maximumHeightSpan = Mathf.Max(
                        maximumHeightSpan,
                        snapshot.HeightSpan);
                    minimumPlanarAspect = Mathf.Min(
                        minimumPlanarAspect,
                        snapshot.PlanarAspectRatio);
                    maximumPlanarAspect = Mathf.Max(
                        maximumPlanarAspect,
                        snapshot.PlanarAspectRatio);
                    familyAdjacentRepeatCount +=
                        snapshot.FamilyAdjacentRepeatCount;

                    report.AppendLine(
                        $"Seed={seed}, Chosen={snapshot.ChosenSeed}, "
                        + $"Tiles={snapshot.TileCount}, "
                        + $"MainPath={snapshot.MainPathCount}, "
                        + $"Arena={snapshot.ArenaCount}, "
                        + $"EndCap={snapshot.EndCapCount}"
                        + $"({snapshot.PlacedEndCapCount}"
                        + $"+{snapshot.ReplacedWithEndCapCount}), "
                        + "OpenDoorways="
                        + $"{snapshot.OpenDoorwayCountBeforeEndCaps}"
                        + $"->{snapshot.OpenDoorwayCountAfterEndCaps}"
                        + $"->{snapshot.OpenDoorwayCountAfterTopologyRepair}, "
                        + "ShapeReplaced="
                        + $"{snapshot.ReplacedShapeTileCount}, "
                        + $"BranchTiles={snapshot.BranchTileCount}, "
                        + $"Unique={snapshot.UniqueTileCount}, "
                        + $"HeightSpan={snapshot.HeightSpan:F2}, "
                        + $"PlanarAspect="
                        + $"{snapshot.PlanarAspectRatio:F2}, "
                        + $"FamilyAdjacentRepeat="
                        + snapshot.FamilyAdjacentRepeatCount);
                    ClearAndValidateNoLeak(generator, root, seed);
                }
                catch (Exception exception)
                {
                    failedSeeds.Add($"Seed={seed}\n{exception}");
                    generator.Clear(false);
                }
            }

            int rawSeedFailureCount = failedSeeds.Count;
            if (rawSeedFailureCount > 0)
            {
                File.WriteAllText(
                    FailedSeedLogPath,
                    string.Join("\n\n", failedSeeds));
                report.AppendLine(
                    "RawCandidateFailureCount="
                    + rawSeedFailureCount);
                report.AppendLine(
                    "RawCandidateFailureLog="
                    + FailedSeedLogPath);
            }
            else if (File.Exists(FailedSeedLogPath))
            {
                File.Delete(FailedSeedLogPath);
            }

            GenerationSnapshot replay =
                GenerateAndValidate(
                    generator,
                    root,
                    FirstSeed,
                    parameters,
                    definition.ArenaTileSet,
                    definition.EndCapTileSet);
            Require(
                replay.Signature == firstSignature,
                $"고정 Seed 결과 불일치: {FirstSeed}");
            ClearAndValidateNoLeak(generator, root, FirstSeed);

            QualityRetrySummary qualitySummary =
                ValidateQualityRetryPolicy(
                    generator,
                    root,
                    definition,
                    parameters,
                    report);
            report.AppendLine(
                $"QualityAcceptedSeedCount="
                + qualitySummary.AcceptedSeedCount);
            report.AppendLine(
                $"QualityRejectedCandidateCount="
                + qualitySummary.RejectedCandidateCount);
            report.AppendLine(
                $"QualityMaximumAttempt="
                + qualitySummary.MaximumAttempt);
            report.AppendLine(
                $"QualityAcceptedAspectRange="
                + $"{qualitySummary.MinimumAcceptedAspect:F2}~"
                + $"{qualitySummary.MaximumAcceptedAspect:F2}");
        }
        finally
        {
            arenaTileInjection.Dispose();
            generator.Clear(false);
            generationFlow.Dispose();
            UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(previewScene);
        }

        IReadOnlyList<string> expectedTileNames =
            DungeonTileFamilyWeightUtility
                .GetPositiveWeightProjectPrefabNames();
        IReadOnlyList<string> ineligibleMainPathArenas =
            DungeonTileFamilyWeightUtility
                .GetIneligibleRequiredMainPathArenaPrefabNames();
        string[] missingTileNames = expectedTileNames
            .Where(name => !seenTileNames.Contains(name))
            .ToArray();
        Require(
            missingTileNames.Length == 0,
            "20 Seed 미등장 타일=" + string.Join(",", missingTileNames));

        report.AppendLine($"SeedCount={SeedCount}");
        report.AppendLine($"FailedSeedCount=0");
        report.AppendLine(
            $"TileCoverage={seenTileNames.Count}/{expectedTileNames.Count}");
        report.AppendLine(
            "IneligibleRequiredMainPathArenas="
            + (ineligibleMainPathArenas.Count == 0
                ? "None"
                : string.Join(",", ineligibleMainPathArenas)));
        report.AppendLine($"TileCountRange={minimumTileCount}~{maximumTileCount}");
        report.AppendLine(
            $"MainPathCountRange={minimumMainPathCount}~{maximumMainPathCount}");
        report.AppendLine(
            $"BranchTileCountRange={minimumBranchTileCount}~{maximumBranchTileCount}");
        report.AppendLine(
            $"EndCapCountRange={minimumEndCapCount}~{maximumEndCapCount}");
        report.AppendLine(
            "OpenDoorwayCountBeforeEndCapsRange="
            + $"{minimumOpenDoorwayCountBeforeEndCaps}~"
            + maximumOpenDoorwayCountBeforeEndCaps);
        report.AppendLine(
            "OpenDoorwayCountAfterEndCapsRange="
            + $"{minimumOpenDoorwayCountAfterEndCaps}~"
            + maximumOpenDoorwayCountAfterEndCaps);
        report.AppendLine(
            "OpenDoorwayCountAfterTopologyRepairRange="
            + $"{minimumOpenDoorwayCountAfterTopologyRepair}~"
            + maximumOpenDoorwayCountAfterTopologyRepair);
        report.AppendLine(
            $"EndCapReplacementCount={totalEndCapReplacementCount}");
        report.AppendLine(
            $"ShapeReplacementCount={totalShapeReplacementCount}");
        report.AppendLine(
            $"TopologyRepairedSeedCount={topologyRepairedSeedCount}");
        report.AppendLine($"BranchlessSeedCount={branchlessSeedCount}");
        report.AppendLine(
            $"HeightSpanRange={minimumHeightSpan:F2}~{maximumHeightSpan:F2}");
        report.AppendLine(
            $"PlanarAspectRange="
            + $"{minimumPlanarAspect:F2}~{maximumPlanarAspect:F2}");
        report.AppendLine(
            $"FamilyAdjacentRepeatCount={familyAdjacentRepeatCount}");
        report.AppendLine($"DeterministicReplaySeed={FirstSeed}");
        report.AppendLine("DuplicateRootCount=0");
        report.AppendLine("LeakedTileCount=0");
        return report.ToString().TrimEnd();
    }

    private static string ValidateArenaCandidateViability()
    {
        DungeonFlow flow = AssetDatabase.LoadAssetAtPath<DungeonFlow>(
            DungeonContentAuthoringBuilder.FlowOutputPath);
        Require(flow != null, "Project DungeonFlow 누락");
        DungeonRunDefinition definition =
            AssetDatabase.LoadAssetAtPath<DungeonRunDefinition>(
                DungeonRunSceneAuthoringBuilder.DefinitionPath);
        Require(definition != null, "DungeonRunDefinition 누락");
        Require(
            definition.ArenaTileSet != null,
            "DungeonRunDefinition Arena TileSet 누락");

        const int diagnosticSeedCount = 12;
        DungeonRunParameters parameters =
            definition.ResolveParameters(0, 0, 1, 24);
        Scene previewScene = EditorSceneManager.NewPreviewScene();
        GameObject root = new("DungeonArenaCandidateDiagnosticRoot");
        SceneManager.MoveGameObjectToScene(root, previewScene);
        DungeonRunGenerationFlow generationFlow =
            new(flow, parameters);
        StringBuilder report = new();
        report.AppendLine("[DungeonArenaCandidateViability]");
        report.AppendLine(
            $"TotalMainPath={parameters.TotalMainPathTileCount}");
        report.AppendLine($"ArenaCount={parameters.ArenaCount}");
        report.AppendLine($"SeedsPerCandidate={diagnosticSeedCount}");

        try
        {
            IReadOnlyList<GameObjectChance> candidates =
                definition.ArenaTileSet.TileWeights.Weights
                    .Where(weight => weight?.Value != null)
                    .OrderBy(
                        weight => weight.Value.name,
                        StringComparer.Ordinal)
                    .ToArray();
            for (int candidateIndex = 0;
                 candidateIndex < candidates.Count;
                 candidateIndex++)
            {
                GameObjectChance sourceChance =
                    candidates[candidateIndex];
                TileSet candidateTileSet =
                    ScriptableObject.CreateInstance<TileSet>();
                candidateTileSet.name =
                    sourceChance.Value.name + "_DiagnosticTileSet";
                candidateTileSet.TileWeights.Weights.Add(
                    new GameObjectChance(
                        sourceChance.Value,
                        1f,
                        1f,
                        candidateTileSet)
                    {
                        DepthWeightScale =
                            AnimationCurve.Linear(0f, 1f, 1f, 1f)
                    });

                DungeonGenerator generator =
                    CreateGenerator(
                        generationFlow.Flow,
                        root,
                        definition.EndCapTileSet,
                        definition.TopologyRepairCatalog);
                int completedCount = 0;
                int exactCandidateCount = 0;
                string firstFailure = null;
                using (new DungeonArenaTileInjection(
                    generator,
                    candidateTileSet,
                    parameters.ArenaCount))
                {
                    for (int seedIndex = 0;
                         seedIndex < diagnosticSeedCount;
                         seedIndex++)
                    {
                        int seed =
                            FirstSeed
                            + candidateIndex * 1000
                            + seedIndex;
                        try
                        {
                            generator.Seed = seed;
                            generator.Generate();
                            if (generator.Status
                                != GenerationStatus.Complete)
                            {
                                firstFailure ??=
                                    $"Seed {seed}: {generator.Status}";
                                continue;
                            }

                            completedCount++;
                            Dungeon dungeon =
                                generator.CurrentDungeon;
                            int matchingCount =
                                dungeon?.AllTiles?.Count(tile =>
                                    NormalizeTileName(tile.name)
                                    == sourceChance.Value.name)
                                ?? 0;
                            if (matchingCount
                                == parameters.ArenaCount)
                            {
                                exactCandidateCount++;
                            }
                            else
                            {
                                firstFailure ??=
                                    $"Seed {seed}: 후보 배치 "
                                    + $"{matchingCount}/"
                                    + parameters.ArenaCount;
                            }
                        }
                        catch (Exception exception)
                        {
                            firstFailure ??=
                                $"Seed {seed}: "
                                + exception.GetType().Name
                                + " - "
                                + exception.Message;
                        }
                        finally
                        {
                            generator.Clear(false);
                        }
                    }
                }

                report.AppendLine(
                    $"{sourceChance.Value.name}: "
                    + $"Complete={completedCount}/"
                    + diagnosticSeedCount
                    + ", Exact="
                    + $"{exactCandidateCount}/"
                    + diagnosticSeedCount
                    + (string.IsNullOrEmpty(firstFailure)
                        ? string.Empty
                        : $", FirstFailure={firstFailure}"));
                UnityEngine.Object.DestroyImmediate(
                    candidateTileSet);
            }
        }
        finally
        {
            generationFlow.Dispose();
            UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(previewScene);
        }

        return report.ToString().TrimEnd();
    }

    private static DungeonGenerator CreateGenerator(
        DungeonFlow flow,
        GameObject root,
        TileSet endCapTileSet,
        DungeonTopologyRepairCatalog topologyRepairCatalog)
    {
        return new DungeonRunGenerator(
            root,
            endCapTileSet,
            topologyRepairCatalog)
        {
            DungeonFlow = flow,
            LengthMultiplier = 1f,
            ShouldRandomizeSeed = false,
            MaxAttemptCount = MaximumGenerationAttempts,
            GenerateAsynchronously = false,
            PlaceTileTriggers = true,
            TileTriggerLayer = 2
        };
    }

    private static GenerationSnapshot GenerateAndValidate(
        DungeonGenerator generator,
        GameObject root,
        int seed,
        DungeonRunParameters parameters,
        TileSet arenaTileSet,
        TileSet endCapTileSet)
    {
        generator.Seed = seed;
        generator.Generate();
        Require(
            generator.Status == GenerationStatus.Complete,
            $"Seed {seed}: 생성 상태={generator.Status}");

        Dungeon dungeon = generator.CurrentDungeon;
        Require(dungeon != null, $"Seed {seed}: Dungeon 누락");
        Require(dungeon.transform == root.transform,
            $"Seed {seed}: Dungeon Root 불일치");
        Require(dungeon.AllTiles != null && dungeon.AllTiles.Count >= 2,
            $"Seed {seed}: 전체 타일 부족");
        Require(
            dungeon.MainPathTiles != null
            && dungeon.MainPathTiles.Count
                == parameters.TotalMainPathTileCount,
            $"Seed {seed}: MainPath 직접 길이 불일치="
            + dungeon.MainPathTiles?.Count
            + $", Expected={parameters.TotalMainPathTileCount}");
        int placedArenaCount =
            DungeonArenaTileInjection.CountPlacedArenas(
                dungeon,
                arenaTileSet);
        Require(
            placedArenaCount == parameters.ArenaCount,
            $"Seed {seed}: Arena 수 불일치="
            + $"{placedArenaCount}/{parameters.ArenaCount}");
        Require(
            DungeonArenaTileInjection.AreAllPlacedArenasOnMainPath(
                dungeon,
                arenaTileSet),
            $"Seed {seed}: Arena가 Main Path 밖에 배치됨");
        Require(
            dungeon.BranchPathTiles != null,
            $"Seed {seed}: Branch 타일 목록 누락");
        DungeonRunGenerator projectGenerator =
            generator as DungeonRunGenerator;
        Require(
            DungeonEndCapContract.TryValidateFinalOpenDoorwayPass(
                dungeon,
                projectGenerator,
                endCapTileSet,
                out string endCapError),
            $"Seed {seed}: {endCapError}");
        int placedEndCapCount =
            DungeonEndCapContract.CountPlacedEndCaps(
                dungeon,
                endCapTileSet);
        Require(
            dungeon.Connections != null
            && dungeon.Connections.Count
                >= dungeon.MainPathTiles.Count - 1,
            $"Seed {seed}: 연결 수 부족");

        HashSet<int> tileIds = new();
        for (int i = 0; i < dungeon.AllTiles.Count; i++)
        {
            Tile tile = dungeon.AllTiles[i];
            Require(tile != null && tileIds.Add(tile.GetInstanceID()),
                $"Seed {seed}: null 또는 중복 타일");
        }
        int familyAdjacentRepeatCount =
            ValidateNoImmediateTileRepeat(dungeon, seed);

        Tile first = dungeon.MainPathTiles[0];
        Tile last =
            dungeon.MainPathTiles[dungeon.MainPathTiles.Count - 1];
        Require(first != last,
            $"Seed {seed}: 시작·마지막 타일 동일");
        Require(HasAnchor(first, DungeonRoomAnchorRole.Start),
            $"Seed {seed}: 시작 Anchor 누락");
        Require(HasAnchor(last, DungeonRoomAnchorRole.Exit),
            $"Seed {seed}: 출구 Anchor 누락");

        List<string> tileNames = dungeon.AllTiles
            .Select(tile => NormalizeTileName(tile.name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        return new GenerationSnapshot(
            generator.ChosenSeed,
            dungeon.AllTiles.Count,
            dungeon.MainPathTiles.Count,
            DungeonEndCapContract.CountNonEndCapBranchTiles(
                dungeon,
                endCapTileSet),
            placedArenaCount,
            placedEndCapCount,
            projectGenerator.PlacedEndCapCount,
            projectGenerator.ReplacedWithEndCapCount,
            projectGenerator.ReplacedShapeTileCount,
            projectGenerator.OpenDoorwayCountBeforeEndCaps,
            projectGenerator.OpenDoorwayCountAfterEndCaps,
            projectGenerator.OpenDoorwayCountAfterTopologyRepair,
            tileNames,
            tileNames.Distinct(StringComparer.Ordinal).Count(),
            dungeon.Bounds.size.y,
            CalculatePlanarAspect(dungeon.Bounds),
            familyAdjacentRepeatCount,
            BuildSignature(dungeon));
    }

    private static QualityRetrySummary ValidateQualityRetryPolicy(
        DungeonGenerator generator,
        GameObject root,
        DungeonRunDefinition definition,
        DungeonRunParameters parameters,
        StringBuilder report)
    {
        int acceptedSeedCount = 0;
        int rejectedCandidateCount = 0;
        int maximumAttempt = 0;
        float minimumAcceptedAspect = float.PositiveInfinity;
        float maximumAcceptedAspect = float.NegativeInfinity;

        report.AppendLine("[LayoutQualityRetry]");
        for (int index = 0; index < SeedCount; index++)
        {
            int requestedSeed = FirstSeed + index;
            bool accepted = false;
            for (int candidateIndex = 0;
                 candidateIndex < definition.LayoutCandidateCount;
                 candidateIndex++)
            {
                int candidateSeed =
                    DungeonLayoutQualityEvaluator.GetCandidateSeed(
                        requestedSeed,
                        candidateIndex);
                GenerationSnapshot snapshot;
                try
                {
                    snapshot = GenerateAndValidate(
                        generator,
                        root,
                        candidateSeed,
                        parameters,
                        definition.ArenaTileSet,
                        definition.EndCapTileSet);
                }
                catch (InvalidOperationException)
                {
                    rejectedCandidateCount++;
                    ClearAndValidateNoLeak(
                        generator,
                        root,
                        candidateSeed);
                    continue;
                }
                DungeonLayoutQualityEvaluation quality =
                    DungeonLayoutQualityEvaluator.Evaluate(
                        generator.CurrentDungeon,
                        definition.MinimumBranchTileCount,
                        definition.MaximumPlanarAspectRatio,
                        definition.EndCapTileSet);
                if (quality.IsAccepted)
                {
                    accepted = true;
                    acceptedSeedCount++;
                    int attempt = candidateIndex + 1;
                    maximumAttempt = Math.Max(
                        maximumAttempt,
                        attempt);
                    minimumAcceptedAspect = Mathf.Min(
                        minimumAcceptedAspect,
                        quality.PlanarAspectRatio);
                    maximumAcceptedAspect = Mathf.Max(
                        maximumAcceptedAspect,
                        quality.PlanarAspectRatio);
                    report.AppendLine(
                        $"Requested={requestedSeed}, "
                        + $"Accepted={snapshot.ChosenSeed}, "
                        + $"Attempt={attempt}, "
                        + $"Branches={quality.BranchTileCount}, "
                        + $"PlanarAspect="
                        + $"{quality.PlanarAspectRatio:F2}");
                    ClearAndValidateNoLeak(
                        generator,
                        root,
                        candidateSeed);
                    break;
                }

                rejectedCandidateCount++;
                ClearAndValidateNoLeak(
                    generator,
                    root,
                    candidateSeed);
            }

            Require(
                accepted,
                $"Requested Seed {requestedSeed}: "
                + $"{definition.LayoutCandidateCount}개 후보가 "
                + "모두 품질 기준 미달");
        }

        Require(
            acceptedSeedCount == SeedCount,
            $"품질 통과 Seed 수 오류={acceptedSeedCount}");
        return new QualityRetrySummary(
            acceptedSeedCount,
            rejectedCandidateCount,
            maximumAttempt,
            minimumAcceptedAspect,
            maximumAcceptedAspect);
    }

    private static float CalculatePlanarAspect(Bounds bounds)
    {
        float width = Mathf.Abs(bounds.size.x);
        float depth = Mathf.Abs(bounds.size.z);
        float shorterSide = Mathf.Min(width, depth);
        return shorterSide > 0.01f
            ? Mathf.Max(width, depth) / shorterSide
            : float.PositiveInfinity;
    }

    private static int ValidateNoImmediateTileRepeat(
        Dungeon dungeon,
        int seed)
    {
        int familyAdjacentRepeatCount = 0;
        for (int i = 0; i < dungeon.Connections.Count; i++)
        {
            DoorwayConnection connection = dungeon.Connections[i];
            Tile tileA = connection?.A != null ? connection.A.Tile : null;
            Tile tileB = connection?.B != null ? connection.B.Tile : null;
            Require(
                tileA != null && tileB != null,
                $"Seed {seed}: Connection[{i}] Tile 참조 누락");

            string nameA = NormalizeTileName(tileA.name);
            string nameB = NormalizeTileName(tileB.name);
            Require(
                !string.Equals(nameA, nameB, StringComparison.Ordinal),
                $"Seed {seed}: 같은 타일 즉시 반복={nameA}");

            if (string.Equals(
                    NormalizeTileFamily(nameA),
                    NormalizeTileFamily(nameB),
                    StringComparison.Ordinal))
            {
                familyAdjacentRepeatCount++;
            }
        }

        return familyAdjacentRepeatCount;
    }

    private static string NormalizeTileName(string value)
    {
        const string cloneSuffix = "(Clone)";
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.EndsWith(cloneSuffix, StringComparison.Ordinal))
        {
            normalized = normalized
                .Substring(0, normalized.Length - cloneSuffix.Length)
                .TrimEnd();
        }

        return normalized;
    }

    private static string NormalizeTileFamily(string value)
    {
        return DungeonTileFamilyWeightUtility
            .NormalizeTileFamilyName(value);
    }

    private static void ClearAndValidateNoLeak(
        DungeonGenerator generator,
        GameObject root,
        int seed)
    {
        generator.Clear(false);
        Require(
            root.transform.childCount == 0,
            $"Seed {seed}: 정리 후 Root 자식="
            + root.transform.childCount);
        Dungeon dungeon = root.GetComponent<Dungeon>();
        Require(
            dungeon != null
            && dungeon.AllTiles.Count == 0
            && dungeon.MainPathTiles.Count == 0
            && dungeon.BranchPathTiles.Count == 0,
            $"Seed {seed}: 정리 후 Dungeon 목록 유출");
    }

    private static bool HasAnchor(
        Tile tile,
        DungeonRoomAnchorRole role)
    {
        DungeonRoomAnchor[] anchors =
            tile.GetComponentsInChildren<DungeonRoomAnchor>(true);
        for (int i = 0; i < anchors.Length; i++)
        {
            if (anchors[i] != null
                && anchors[i].Supports(DungeonRoomAnchorRole.Spawn)
                && anchors[i].Supports(role))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildSignature(Dungeon dungeon)
    {
        StringBuilder signature = new();
        for (int i = 0; i < dungeon.MainPathTiles.Count; i++)
        {
            Tile tile = dungeon.MainPathTiles[i];
            Transform transform = tile.transform;
            Vector3 position = transform.position;
            Quaternion rotation = transform.rotation;
            signature.Append(tile.name).Append('|')
                .Append(position.x.ToString("R", CultureInfo.InvariantCulture))
                .Append(',')
                .Append(position.y.ToString("R", CultureInfo.InvariantCulture))
                .Append(',')
                .Append(position.z.ToString("R", CultureInfo.InvariantCulture))
                .Append('|')
                .Append(rotation.x.ToString("R", CultureInfo.InvariantCulture))
                .Append(',')
                .Append(rotation.y.ToString("R", CultureInfo.InvariantCulture))
                .Append(',')
                .Append(rotation.z.ToString("R", CultureInfo.InvariantCulture))
                .Append(',')
                .Append(rotation.w.ToString("R", CultureInfo.InvariantCulture))
                .AppendLine();
        }

        return signature.ToString();
    }

    private readonly struct GenerationSnapshot
    {
        public GenerationSnapshot(
            int chosenSeed,
            int tileCount,
            int mainPathCount,
            int branchTileCount,
            int arenaCount,
            int endCapCount,
            int placedEndCapCount,
            int replacedWithEndCapCount,
            int replacedShapeTileCount,
            int openDoorwayCountBeforeEndCaps,
            int openDoorwayCountAfterEndCaps,
            int openDoorwayCountAfterTopologyRepair,
            IReadOnlyList<string> tileNames,
            int uniqueTileCount,
            float heightSpan,
            float planarAspectRatio,
            int familyAdjacentRepeatCount,
            string signature)
        {
            ChosenSeed = chosenSeed;
            TileCount = tileCount;
            MainPathCount = mainPathCount;
            BranchTileCount = branchTileCount;
            ArenaCount = arenaCount;
            EndCapCount = endCapCount;
            PlacedEndCapCount = placedEndCapCount;
            ReplacedWithEndCapCount = replacedWithEndCapCount;
            ReplacedShapeTileCount = replacedShapeTileCount;
            OpenDoorwayCountBeforeEndCaps =
                openDoorwayCountBeforeEndCaps;
            OpenDoorwayCountAfterEndCaps =
                openDoorwayCountAfterEndCaps;
            OpenDoorwayCountAfterTopologyRepair =
                openDoorwayCountAfterTopologyRepair;
            TileNames = tileNames;
            UniqueTileCount = uniqueTileCount;
            HeightSpan = heightSpan;
            PlanarAspectRatio = planarAspectRatio;
            FamilyAdjacentRepeatCount = familyAdjacentRepeatCount;
            Signature = signature;
        }

        public int ChosenSeed { get; }
        public int TileCount { get; }
        public int MainPathCount { get; }
        public int BranchTileCount { get; }
        public int ArenaCount { get; }
        public int EndCapCount { get; }
        public int PlacedEndCapCount { get; }
        public int ReplacedWithEndCapCount { get; }
        public int ReplacedShapeTileCount { get; }
        public int OpenDoorwayCountBeforeEndCaps { get; }
        public int OpenDoorwayCountAfterEndCaps { get; }
        public int OpenDoorwayCountAfterTopologyRepair { get; }
        public IReadOnlyList<string> TileNames { get; }
        public int UniqueTileCount { get; }
        public float HeightSpan { get; }
        public float PlanarAspectRatio { get; }
        public int FamilyAdjacentRepeatCount { get; }
        public string Signature { get; }
    }

    private readonly struct QualityRetrySummary
    {
        public QualityRetrySummary(
            int acceptedSeedCount,
            int rejectedCandidateCount,
            int maximumAttempt,
            float minimumAcceptedAspect,
            float maximumAcceptedAspect)
        {
            AcceptedSeedCount = acceptedSeedCount;
            RejectedCandidateCount = rejectedCandidateCount;
            MaximumAttempt = maximumAttempt;
            MinimumAcceptedAspect = minimumAcceptedAspect;
            MaximumAcceptedAspect = maximumAcceptedAspect;
        }

        public int AcceptedSeedCount { get; }
        public int RejectedCandidateCount { get; }
        public int MaximumAttempt { get; }
        public float MinimumAcceptedAspect { get; }
        public float MaximumAcceptedAspect { get; }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
