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

public static class DungeonEventAreaRegressionValidator
{
    private const int FirstSeed = 27201;
    private const int SeedCount = 20;
    private const string LogPath =
        "Logs/DungeonEventAreaRegression.log";

    [MenuItem(
        "OVERBURST/Codex/Validation/World/Dungeon/"
        + "Run 20 EventArea Seeds")]
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
            File.WriteAllText(
                LogPath,
                report,
                new UTF8Encoding(false));
            Debug.Log(report);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            File.WriteAllText(
                LogPath,
                exception.ToString(),
                new UTF8Encoding(false));
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static string ValidateOrThrow()
    {
        DungeonFlow flow = AssetDatabase.LoadAssetAtPath<DungeonFlow>(
            DungeonContentAuthoringBuilder.FlowOutputPath);
        Require(flow != null, "Project DungeonFlow is missing.");
        DungeonRunDefinition definition =
            AssetDatabase.LoadAssetAtPath<DungeonRunDefinition>(
                DungeonRunSceneAuthoringBuilder.DefinitionPath);
        Require(definition != null, "DungeonRunDefinition is missing.");

        DungeonRunParameters parameters =
            definition.ResolveParameters(0, 0);
        Require(
            parameters.RoomCount == definition.DefaultRoomCount,
            "Default room count resolution mismatch.");
        ValidateDifficultyWeightPolicy(definition);

        Scene previewScene = EditorSceneManager.NewPreviewScene();
        GameObject root = new("DungeonEventAreaRegressionRoot");
        SceneManager.MoveGameObjectToScene(root, previewScene);
        DungeonRunGenerationFlow generationFlow =
            new(flow, parameters);
        DungeonRunGenerator generator =
            new DungeonRunGenerator(
                root,
                definition.EndCapTileSet,
                definition.TopologyRepairCatalog)
        {
            DungeonFlow = generationFlow.Flow,
            LengthMultiplier = 1f,
            ShouldRandomizeSeed = false,
            MaxAttemptCount = definition.MaxAttemptCount,
            GenerateAsynchronously = false,
            PlaceTileTriggers = true,
            TileTriggerLayer = 2
        };
        DungeonArenaTileInjection arenaTileInjection =
            new(
                generator,
                definition.ArenaTileSet,
                parameters.ArenaCount);

        StringBuilder report = new();
        report.AppendLine("[DungeonEventAreaRegressionValidator] PASS");
        report.AppendLine(
            $"Difficulty={parameters.DifficultyLevel}");
        report.AppendLine(
            $"GameplayRoomCount={parameters.RoomCount}");
        report.AppendLine(
            $"SelectedGameplayAreaCount={parameters.RoomCount}");
        report.AppendLine(
            $"TotalMainPath={parameters.TotalMainPathTileCount}");
        report.AppendLine($"ArenaCount={parameters.ArenaCount}");
        report.AppendLine(
            "AreaSizeRange=4x4~8x8");
        report.AppendLine("[Seeds]");

        int minimumTileCount = int.MaxValue;
        int maximumTileCount = int.MinValue;
        int maximumAttempt = 0;
        int qualityRejectedCount = 0;
        int areaRejectedCount = 0;
        int[] difficultyLevels = { 1, 15, 30 };
        Dictionary<int, float> selectedSurfaceTotals =
            difficultyLevels.ToDictionary(level => level, _ => 0f);
        Dictionary<int, int> selectedAreaTotals =
            difficultyLevels.ToDictionary(level => level, _ => 0);
        Dictionary<int, Dictionary<Vector2Int, int>>
            selectedSizeHistograms = difficultyLevels.ToDictionary(
                level => level,
                _ => DungeonEventAreaAuthoringBuilder
                    .SupportedDimensions.ToDictionary(size => size, _ => 0));

        try
        {
            for (int index = 0; index < SeedCount; index++)
            {
                int requestedSeed = FirstSeed + index;
                bool accepted = false;
                string lastError = string.Empty;
                for (int candidateIndex = 0;
                     candidateIndex < definition.LayoutCandidateCount;
                     candidateIndex++)
                {
                    int candidateSeed =
                        DungeonLayoutQualityEvaluator.GetCandidateSeed(
                            requestedSeed,
                            candidateIndex);
                    generator.Seed = candidateSeed;
                    generator.Generate();
                    Require(
                        generator.Status == GenerationStatus.Complete,
                        $"Seed {requestedSeed}: generation status "
                        + generator.Status);

                    Dungeon dungeon = generator.CurrentDungeon;
                    Require(
                        dungeon != null,
                        $"Seed {requestedSeed}: generated dungeon missing.");
                    ValidateDirectLayout(
                        dungeon,
                        generator,
                        definition.ArenaTileSet,
                        definition.EndCapTileSet,
                        parameters,
                        requestedSeed);
                    DungeonLayoutQualityEvaluation quality =
                        DungeonLayoutQualityEvaluator.Evaluate(
                            dungeon,
                            definition.MinimumBranchTileCount,
                            definition.MaximumPlanarAspectRatio,
                            definition.EndCapTileSet);
                    if (!quality.IsAccepted)
                    {
                        qualityRejectedCount++;
                        lastError = quality.RejectionReason;
                        ClearAndValidateNoLeak(
                            generator,
                            root,
                            requestedSeed);
                        continue;
                    }

                    if (!TryResolveDifficultyLayouts(
                            dungeon,
                            definition,
                            parameters.RoomCount,
                            requestedSeed,
                            difficultyLevels,
                            out Dictionary<int, DungeonEventAreaLayout>
                                difficultyLayouts,
                            out lastError))
                    {
                        areaRejectedCount++;
                        ClearAndValidateNoLeak(
                            generator,
                            root,
                            requestedSeed);
                        continue;
                    }

                    for (int difficultyIndex = 0;
                         difficultyIndex < difficultyLevels.Length;
                         difficultyIndex++)
                    {
                        int difficulty = difficultyLevels[difficultyIndex];
                        DungeonRunParameters difficultyParameters = new(
                            difficulty,
                            parameters.RoomCount,
                            parameters.ArenaCount,
                            parameters.TotalMainPathTileCount);
                        DungeonEventAreaLayout difficultyLayout =
                            difficultyLayouts[difficulty];
                        ValidateLayout(
                            dungeon,
                            difficultyLayout,
                            difficultyParameters,
                            requestedSeed);
                        AccumulateSelectedSizes(
                            difficultyLayout,
                            selectedSurfaceTotals,
                            selectedAreaTotals,
                            selectedSizeHistograms);
                    }

                    DungeonEventAreaLayout layout =
                        difficultyLayouts[parameters.DifficultyLevel];
                    string firstSignature = BuildSignature(layout);
                    int firstChosenSeed = generator.ChosenSeed;
                    int firstTileCount = dungeon.AllTiles.Count;
                    int firstBranchCount = dungeon.BranchPathTiles.Count;
                    int firstCandidateCount = layout.Candidates.Count;
                    int firstGameplayCount = layout.GameplayRoomCount;
                    float firstAspect = quality.PlanarAspectRatio;
                    ClearAndValidateNoLeak(
                        generator,
                        root,
                        requestedSeed);

                    generator.Seed = candidateSeed;
                    generator.Generate();
                    Require(
                        generator.Status == GenerationStatus.Complete
                        && generator.ChosenSeed == firstChosenSeed,
                        $"Seed {requestedSeed}: deterministic generation "
                        + "replay failed.");
                    Dungeon dungeonReplay = generator.CurrentDungeon;
                    ValidateDirectLayout(
                        dungeonReplay,
                        generator,
                        definition.ArenaTileSet,
                        definition.EndCapTileSet,
                        parameters,
                        requestedSeed);
                    DungeonLayoutQualityEvaluation replayQuality =
                        DungeonLayoutQualityEvaluator.Evaluate(
                            dungeonReplay,
                            definition.MinimumBranchTileCount,
                            definition.MaximumPlanarAspectRatio,
                            definition.EndCapTileSet);
                    Require(
                        replayQuality.IsAccepted,
                        $"Seed {requestedSeed}: replay layout quality "
                        + "was rejected.");
                    Require(
                        DungeonEventAreaLayoutResolver.TryResolve(
                            dungeonReplay,
                            definition,
                            parameters,
                            requestedSeed,
                            out DungeonEventAreaLayout replay,
                            out string replayError),
                        $"Seed {requestedSeed}: deterministic replay "
                        + $"failed. {replayError}");
                    ValidateLayout(
                        dungeonReplay,
                        replay,
                        parameters,
                        requestedSeed);
                    Require(
                        string.Equals(
                            firstSignature,
                            BuildSignature(replay),
                            StringComparison.Ordinal),
                        $"Seed {requestedSeed}: EventArea selection "
                        + "is not deterministic.");

                    int attempt = candidateIndex + 1;
                    maximumAttempt = Math.Max(maximumAttempt, attempt);
                    minimumTileCount = Math.Min(
                        minimumTileCount,
                        firstTileCount);
                    maximumTileCount = Math.Max(
                        maximumTileCount,
                        firstTileCount);
                    report.AppendLine(
                        $"Requested={requestedSeed}, "
                        + $"Chosen={firstChosenSeed}, "
                        + $"Attempt={attempt}, "
                        + $"Tiles={firstTileCount}, "
                        + $"Branches={firstBranchCount}, "
                        + $"Candidates={firstCandidateCount}, "
                        + $"Gameplay={firstGameplayCount}, "
                        + $"D1={BuildSizeSummary(difficultyLayouts[1])}, "
                        + $"D15={BuildSizeSummary(difficultyLayouts[15])}, "
                        + $"D30={BuildSizeSummary(difficultyLayouts[30])}, "
                        + $"Aspect={firstAspect:F2}");
                    accepted = true;
                    ClearAndValidateNoLeak(
                        generator,
                        root,
                        requestedSeed);
                    break;
                }

                Require(
                    accepted,
                    $"Requested Seed {requestedSeed}: no valid candidate "
                    + $"within {definition.LayoutCandidateCount} attempts. "
                    + lastError);
            }
        }
        finally
        {
            arenaTileInjection.Dispose();
            generator.Clear(false);
            generationFlow.Dispose();
            UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(previewScene);
        }

        report.AppendLine("[Summary]");
        report.AppendLine($"SeedCount={SeedCount}");
        report.AppendLine("FailedSeedCount=0");
        report.AppendLine(
            $"TileCountRange={minimumTileCount}~{maximumTileCount}");
        report.AppendLine($"MaximumLayoutAttempt={maximumAttempt}");
        report.AppendLine(
            $"QualityRejectedCandidateCount={qualityRejectedCount}");
        report.AppendLine(
            $"EventAreaRejectedCandidateCount={areaRejectedCount}");
        report.AppendLine(
            $"DeterministicSelectionCount={SeedCount}");
        report.AppendLine("LeakedTileCount=0");
        report.AppendLine("[SelectedSizeDistribution]");
        float lowAverage = AppendSelectedSizeDistribution(
            report,
            1,
            selectedSurfaceTotals,
            selectedAreaTotals,
            selectedSizeHistograms);
        float middleAverage = AppendSelectedSizeDistribution(
            report,
            15,
            selectedSurfaceTotals,
            selectedAreaTotals,
            selectedSizeHistograms);
        float highAverage = AppendSelectedSizeDistribution(
            report,
            30,
            selectedSurfaceTotals,
            selectedAreaTotals,
            selectedSizeHistograms);
        Require(
            lowAverage < middleAverage
            && middleAverage < highAverage,
            $"난이도별 평균 EventArea 면적 증가 실패: "
            + $"{lowAverage:F2} < {middleAverage:F2} < {highAverage:F2}");
        report.AppendLine("[DifficultyWeightPolicy]");
        AppendWeightRow(report, definition, 1);
        AppendWeightRow(report, definition, 15);
        AppendWeightRow(report, definition, 30);
        return report.ToString().TrimEnd();
    }

    private static bool TryResolveDifficultyLayouts(
        Dungeon dungeon,
        DungeonRunDefinition definition,
        int roomCount,
        int requestedSeed,
        IReadOnlyList<int> difficultyLevels,
        out Dictionary<int, DungeonEventAreaLayout> layouts,
        out string error)
    {
        layouts = new Dictionary<int, DungeonEventAreaLayout>();
        error = string.Empty;
        for (int i = 0; i < difficultyLevels.Count; i++)
        {
            int difficulty = difficultyLevels[i];
            DungeonRunParameters parameters = new(
                difficulty,
                roomCount,
                definition.DefaultArenaCount,
                definition.DefaultTotalMainPathTileCount);
            if (!DungeonEventAreaLayoutResolver.TryResolve(
                    dungeon,
                    definition,
                    parameters,
                    requestedSeed,
                    out DungeonEventAreaLayout layout,
                    out error))
            {
                layouts.Clear();
                return false;
            }

            layouts[difficulty] = layout;
        }

        return true;
    }

    private static void AccumulateSelectedSizes(
        DungeonEventAreaLayout layout,
        IDictionary<int, float> surfaceTotals,
        IDictionary<int, int> areaTotals,
        IDictionary<int, Dictionary<Vector2Int, int>> histograms)
    {
        int difficulty = layout.Parameters.DifficultyLevel;
        for (int i = 0; i < layout.SelectedNodes.Count; i++)
        {
            DungeonEventAreaAuthoring area = layout.SelectedNodes[i].Area;
            surfaceTotals[difficulty] += area.SurfaceArea;
            areaTotals[difficulty]++;
            histograms[difficulty][area.DimensionsMeters]++;
        }
    }

    private static string BuildSizeSummary(DungeonEventAreaLayout layout)
    {
        return string.Join(
            "/",
            layout.SelectedNodes
                .Select(node =>
                    $"{node.Area.DimensionsMeters.x}x"
                    + $"{node.Area.DimensionsMeters.y}")
                .OrderBy(value => value, StringComparer.Ordinal));
    }

    private static float AppendSelectedSizeDistribution(
        StringBuilder report,
        int difficulty,
        IReadOnlyDictionary<int, float> surfaceTotals,
        IReadOnlyDictionary<int, int> areaTotals,
        IReadOnlyDictionary<int, Dictionary<Vector2Int, int>> histograms)
    {
        Require(
            areaTotals[difficulty] > 0,
            $"난이도 {difficulty} 선택 영역 표본 없음");
        float average =
            surfaceTotals[difficulty] / areaTotals[difficulty];
        report.Append(
            $"Difficulty={difficulty}, "
            + $"AverageSurface={average:F3}, Sizes=");
        report.AppendLine(
            string.Join(
                ", ",
                DungeonEventAreaAuthoringBuilder.SupportedDimensions
                    .Select(size =>
                        $"{size.x}x{size.y}:"
                        + histograms[difficulty][size])));
        return average;
    }

    private static void ValidateLayout(
        Dungeon dungeon,
        DungeonEventAreaLayout layout,
        DungeonRunParameters parameters,
        int requestedSeed)
    {
        Require(
            layout != null,
            $"Seed {requestedSeed}: EventArea layout missing.");
        Require(
            layout.GameplayRoomCount == parameters.RoomCount,
            $"Seed {requestedSeed}: gameplay room count "
            + $"{layout.GameplayRoomCount}/{parameters.RoomCount}");
        Require(
            layout.SelectedNodes.Count == parameters.RoomCount,
            $"Seed {requestedSeed}: selected area count "
            + $"{layout.SelectedNodes.Count}/{parameters.RoomCount}");
        Require(
            layout.EntranceTile == dungeon.MainPathTiles[0],
            $"Seed {requestedSeed}: entrance tile mismatch.");
        Require(
            layout.ExitTile
                == dungeon.MainPathTiles[dungeon.MainPathTiles.Count - 1],
            $"Seed {requestedSeed}: exit tile mismatch.");
        Require(
            layout.Candidates.Count >= layout.SelectedNodes.Count,
            $"Seed {requestedSeed}: candidate count "
            + $"{layout.Candidates.Count}/{layout.SelectedNodes.Count}");
        Require(
            layout.DoorwayConnections.Count
            == dungeon.Connections.Count,
            $"Seed {requestedSeed}: doorway connection count mismatch.");

        HashSet<Tile> selectedTiles = new();
        HashSet<DungeonEventAreaAuthoring> selectedAreas = new();
        HashSet<DungeonEventAreaAuthoring> candidates =
            new(layout.Candidates);
        for (int i = 0; i < layout.SelectedNodes.Count; i++)
        {
            DungeonEventAreaNode node = layout.SelectedNodes[i];
            Require(
                node != null && node.Tile != null && node.Area != null,
                $"Seed {requestedSeed}: selected node {i} is invalid.");
            Require(
                selectedTiles.Add(node.Tile),
                $"Seed {requestedSeed}: duplicate selected tile.");
            Require(
                selectedAreas.Add(node.Area),
                $"Seed {requestedSeed}: duplicate selected area.");
            Require(
                candidates.Contains(node.Area),
                $"Seed {requestedSeed}: selected area is not a candidate.");
            Require(
                node.Tile != layout.EntranceTile
                && node.Tile != layout.ExitTile,
                $"Seed {requestedSeed}: entrance/exit selected as gameplay.");
            Require(
                node.Area.Shape == DungeonEventAreaShape.Rectangle
                && DungeonEventAreaAuthoringBuilder
                    .SupportedDimensions.Contains(
                        node.Area.DimensionsMeters),
                $"Seed {requestedSeed}: unsupported area size selected.");
        }

        ValidateGraphReachability(
            dungeon,
            selectedTiles,
            requestedSeed);
    }

    private static void ValidateGraphReachability(
        Dungeon dungeon,
        IReadOnlyCollection<Tile> selectedTiles,
        int requestedSeed)
    {
        Dictionary<Tile, List<Tile>> adjacency = dungeon.AllTiles
            .Where(tile => tile != null)
            .ToDictionary(tile => tile, _ => new List<Tile>());
        for (int i = 0; i < dungeon.Connections.Count; i++)
        {
            DoorwayConnection connection = dungeon.Connections[i];
            Tile a = connection?.A?.Tile;
            Tile b = connection?.B?.Tile;
            Require(
                a != null && b != null,
                $"Seed {requestedSeed}: invalid doorway connection {i}.");
            adjacency[a].Add(b);
            adjacency[b].Add(a);
        }

        Tile entrance = dungeon.MainPathTiles[0];
        HashSet<Tile> visited = new() { entrance };
        Queue<Tile> queue = new();
        queue.Enqueue(entrance);
        while (queue.Count > 0)
        {
            Tile current = queue.Dequeue();
            List<Tile> neighbors = adjacency[current];
            for (int i = 0; i < neighbors.Count; i++)
            {
                if (visited.Add(neighbors[i]))
                    queue.Enqueue(neighbors[i]);
            }
        }

        foreach (Tile selected in selectedTiles)
        {
            Require(
                visited.Contains(selected),
                $"Seed {requestedSeed}: selected tile is unreachable.");
        }
    }

    private static void ValidateDifficultyWeightPolicy(
        DungeonRunDefinition definition)
    {
        Vector2Int minimum = new(4, 4);
        Vector2Int maximum = new(8, 8);
        float lowSmall = definition.GetPlayRoomSizeWeight(minimum, 1);
        float highSmall = definition.GetPlayRoomSizeWeight(
            minimum,
            DungeonRunParameters.MaximumDifficultyLevel);
        float lowLarge = definition.GetPlayRoomSizeWeight(maximum, 1);
        float highLarge = definition.GetPlayRoomSizeWeight(
            maximum,
            DungeonRunParameters.MaximumDifficultyLevel);
        Require(
            Mathf.Approximately(lowSmall, 1f)
            && Mathf.Approximately(highSmall, 1f),
            "4x4 baseline weight must remain 1.");
        Require(
            lowLarge < highLarge,
            "Large-area weight must increase with difficulty.");
        Require(
            lowLarge < lowSmall,
            "Low difficulty must suppress large areas.");
        Require(
            highLarge > highSmall,
            "High difficulty must favor large areas.");
    }

    private static string BuildSignature(
        DungeonEventAreaLayout layout)
    {
        StringBuilder signature = new();
        foreach (DungeonEventAreaNode node in layout.SelectedNodes
                     .OrderBy(node => node.Tile.Placement.PathDepth)
                     .ThenBy(node => node.Tile.Placement.Depth)
                     .ThenBy(node => node.Tile.transform.position.x)
                     .ThenBy(node => node.Tile.transform.position.y)
                     .ThenBy(node => node.Tile.transform.position.z))
        {
            Vector3 tilePosition = node.Tile.transform.position;
            Vector3 areaPosition = node.Area.transform.position;
            signature.Append(node.Tile.name).Append('|')
                .Append(FormatVector(tilePosition)).Append('|')
                .Append(node.Area.AreaId).Append('|')
                .Append(FormatVector(areaPosition)).Append(';');
        }

        return signature.ToString();
    }

    private static string FormatVector(Vector3 value)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0:F3},{1:F3},{2:F3}",
            value.x,
            value.y,
            value.z);
    }

    private static void AppendWeightRow(
        StringBuilder report,
        DungeonRunDefinition definition,
        int difficulty)
    {
        report.Append($"Difficulty={difficulty}");
        for (int width = DungeonEventAreaAuthoring.MinimumDimension;
             width <= DungeonEventAreaAuthoring.MaximumDimension;
             width++)
        {
            for (int depth = width;
                 depth <= DungeonEventAreaAuthoring.MaximumDimension;
                 depth++)
            {
                Vector2Int dimensions = new(width, depth);
                report.Append(
                    $", {width}x{depth}="
                    + $"{definition.GetPlayRoomSizeWeight(dimensions, difficulty):F3}");
            }
        }

        report.AppendLine();
    }

    private static void ClearAndValidateNoLeak(
        DungeonGenerator generator,
        GameObject root,
        int requestedSeed)
    {
        generator.Clear(false);
        Require(
            root.transform.childCount == 0,
            $"Seed {requestedSeed}: generated children leaked after clear.");
        Dungeon dungeon = root.GetComponent<Dungeon>();
        Require(
            dungeon != null
            && dungeon.AllTiles.Count == 0
            && dungeon.MainPathTiles.Count == 0
            && dungeon.BranchPathTiles.Count == 0,
            $"Seed {requestedSeed}: dungeon lists leaked after clear.");
    }

    private static void ValidateDirectLayout(
        Dungeon dungeon,
        DungeonRunGenerator generator,
        TileSet arenaTileSet,
        TileSet endCapTileSet,
        DungeonRunParameters parameters,
        int requestedSeed)
    {
        Require(
            dungeon?.MainPathTiles != null
            && dungeon.MainPathTiles.Count
                == parameters.TotalMainPathTileCount,
            $"Seed {requestedSeed}: direct main-path length mismatch.");
        int placedArenaCount =
            DungeonArenaTileInjection.CountPlacedArenas(
                dungeon,
                arenaTileSet);
        Require(
            placedArenaCount == parameters.ArenaCount
            && DungeonArenaTileInjection.AreAllPlacedArenasOnMainPath(
                dungeon,
                arenaTileSet),
            $"Seed {requestedSeed}: exact Arena count mismatch.");
        Require(
            DungeonEndCapContract.TryValidateFinalOpenDoorwayPass(
                dungeon,
                generator,
                endCapTileSet,
                out string endCapError),
            $"Seed {requestedSeed}: {endCapError}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
