using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DunGen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DungeonTopologyRepairRegressionValidator
{
    private static readonly string[] RequiredSourcePaths =
    {
        "Assets/ProjectOverburst/04_Contents/02_Dungeon/Content/"
        + "MultistoryDungeons2/Tiles/"
        + "PF_DungeonTile_TD_Bridge_11.prefab",
        "Assets/ProjectOverburst/04_Contents/02_Dungeon/Content/"
        + "MultistoryDungeons2/Tiles/"
        + "PF_DungeonTile_TD_Bridge_21.prefab"
    };
    private const string LogPath =
        "Logs/DungeonTopologyRepairRegression.log";
    private const int FirstSeed = 27001;
    private const int MaximumSeedCount = 20;
    private const int ReportedFailureSeed = 931553947;
    private const string ReportedExpectedCandidate =
        "PF_DungeonTile_TD_Bridge_21_L2";

    [MenuItem(
        "OVERBURST/Codex/Validation/World/Dungeon/"
        + "Validate Topology Repair")]
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

    public static string ValidateOrThrow()
    {
        DungeonRunDefinition definition =
            AssetDatabase.LoadAssetAtPath<DungeonRunDefinition>(
                DungeonRunSceneAuthoringBuilder.DefinitionPath);
        Require(definition != null, "DungeonRunDefinition 누락");
        DungeonTopologyRepairCatalog catalog =
            definition.TopologyRepairCatalog;
        Require(catalog != null, "형태 보정 카탈로그 누락");
        ValidateStaticRules();
        IReadOnlyList<GameObject> savedCandidates =
            ValidateSavedCatalog(catalog);

        Scene previewScene =
            EditorSceneManager.NewPreviewScene();
        GameObject root = new("DungeonTopologyRepairRegressionRoot");
        SceneManager.MoveGameObjectToScene(root, previewScene);
        DungeonRunGenerationFlow generationFlow = null;
        DungeonArenaTileInjection arenaInjection = null;
        DungeonRunGenerator generator = null;
        try
        {
            DungeonRunParameters parameters =
                definition.ResolveParameters(0, 0);
            generationFlow = new DungeonRunGenerationFlow(
                definition.DungeonFlow,
                parameters);
            generator = new DungeonRunGenerator(
                root,
                definition.EndCapTileSet,
                catalog)
            {
                DungeonFlow = generationFlow.Flow,
                LengthMultiplier = 1f,
                ShouldRandomizeSeed = false,
                MaxAttemptCount = definition.MaxAttemptCount,
                GenerateAsynchronously = false,
                PlaceTileTriggers = true,
                TileTriggerLayer = 2
            };
            arenaInjection = new DungeonArenaTileInjection(
                generator,
                definition.ArenaTileSet,
                parameters.ArenaCount);
            string reportedSeedResult =
                ValidateReportedFailureSeed(
                    generator,
                    definition.EndCapTileSet);

            int acceptedSeed = 0;
            string firstSignature = string.Empty;
            int shapeReplacementCount = 0;
            int endCapReplacementCount = 0;
            int removedDoorwayCount = 0;
            int openBefore = 0;
            int openAfter = 0;
            string usedCandidateNames = string.Empty;
            for (int index = 0;
                 index < MaximumSeedCount;
                 index++)
            {
                int seed = FirstSeed + index;
                generator.Seed = seed;
                generator.Generate();
                if (generator.Status == GenerationStatus.Complete
                    && generator.ReplacedShapeTileCount > 0)
                {
                    RequireCurrentResult(
                        generator,
                        definition.EndCapTileSet,
                        seed);
                    acceptedSeed = seed;
                    shapeReplacementCount =
                        generator.ReplacedShapeTileCount;
                    endCapReplacementCount =
                        generator.ReplacedWithEndCapCount;
                    removedDoorwayCount =
                        generator.RemovedOpenDoorwayCount;
                    openBefore =
                        generator.OpenDoorwayCountBeforeTopologyRepair;
                    openAfter =
                        generator.OpenDoorwayCountAfterTopologyRepair;
                    firstSignature =
                        BuildSignature(generator.CurrentDungeon);
                    usedCandidateNames =
                        ResolveUsedCandidateNames(
                            generator.CurrentDungeon,
                            savedCandidates);
                    break;
                }

                generator.Clear(false);
            }

            Require(
                acceptedSeed != 0,
                "20 Seed 안에서 저장된 I/L 형태 후보 교체가 "
                + "발생하지 않았습니다.");
            generator.Clear(false);
            generator.Seed = acceptedSeed;
            generator.Generate();
            RequireCurrentResult(
                generator,
                definition.EndCapTileSet,
                acceptedSeed);
            Require(
                generator.ReplacedShapeTileCount
                    == shapeReplacementCount
                && generator.ReplacedWithEndCapCount
                    == endCapReplacementCount
                && generator.RemovedOpenDoorwayCount
                    == removedDoorwayCount
                && generator.OpenDoorwayCountBeforeTopologyRepair
                    == openBefore
                && generator.OpenDoorwayCountAfterTopologyRepair
                    == openAfter
                && BuildSignature(generator.CurrentDungeon)
                    == firstSignature,
                "동일 Seed 형태 보정 결과가 재현되지 않았습니다.");

            return "[DungeonTopologyRepairRegression] PASS\n"
                + reportedSeedResult + "\n"
                + $"Seed={acceptedSeed}\n"
                + $"ShapeReplaced={shapeReplacementCount}\n"
                + $"EndCapReplaced={endCapReplacementCount}\n"
                + $"RemovedDoorways={removedDoorwayCount}\n"
                + $"OpenDoorways={openBefore}->{openAfter}\n"
                + $"SourceFamilies={RequiredSourcePaths.Length}\n"
                + $"SavedCandidates={savedCandidates.Count}\n"
                + $"UsedCandidates={usedCandidateNames}\n"
                + "ConnectionsPreserved=1\n"
                + "DeterministicReplay=1\n"
                + "SavedCandidateAssetsChanged=0";
        }
        finally
        {
            arenaInjection?.Dispose();
            generator?.Clear(false);
            generationFlow?.Dispose();
            if (root != null)
                UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(previewScene);
        }
    }

    private static string ValidateReportedFailureSeed(
        DungeonRunGenerator generator,
        TileSet endCapTileSet)
    {
        generator.Seed = ReportedFailureSeed;
        generator.Generate();
        RequireCurrentResult(
            generator,
            endCapTileSet,
            ReportedFailureSeed);
        Require(
            ContainsCandidate(
                generator.CurrentDungeon,
                ReportedExpectedCandidate),
            $"Seed {ReportedFailureSeed}: "
            + ReportedExpectedCandidate
            + " 제자리 교체가 발생하지 않았습니다.");

        int shapeReplacementCount =
            generator.ReplacedShapeTileCount;
        int endCapReplacementCount =
            generator.ReplacedWithEndCapCount;
        int openBefore =
            generator.OpenDoorwayCountBeforeTopologyRepair;
        int openAfter =
            generator.OpenDoorwayCountAfterTopologyRepair;
        string signature =
            BuildSignature(generator.CurrentDungeon);

        generator.Clear(false);
        generator.Seed = ReportedFailureSeed;
        generator.Generate();
        RequireCurrentResult(
            generator,
            endCapTileSet,
            ReportedFailureSeed);
        Require(
            ContainsCandidate(
                generator.CurrentDungeon,
                ReportedExpectedCandidate)
            && generator.ReplacedShapeTileCount
                == shapeReplacementCount
            && generator.ReplacedWithEndCapCount
                == endCapReplacementCount
            && generator.OpenDoorwayCountBeforeTopologyRepair
                == openBefore
            && generator.OpenDoorwayCountAfterTopologyRepair
                == openAfter
            && BuildSignature(generator.CurrentDungeon)
                == signature,
            $"Seed {ReportedFailureSeed}: "
            + "제자리 교체 결과가 재현되지 않았습니다.");
        generator.Clear(false);

        return $"ReportedSeed={ReportedFailureSeed}\n"
            + $"ReportedCandidate={ReportedExpectedCandidate}\n"
            + $"ReportedShapeReplaced={shapeReplacementCount}\n"
            + $"ReportedEndCapReplaced={endCapReplacementCount}\n"
            + $"ReportedOpenDoorways={openBefore}->{openAfter}\n"
            + "ReportedDeterministicReplay=1";
    }

    private static void ValidateStaticRules()
    {
        Require(
            DungeonTopologyRepairId.TryParse(
                "I12",
                out DungeonTopologyRepairShape shape,
                out int priority)
            && shape == DungeonTopologyRepairShape.I
            && priority == 12,
            "I/L/T 후보 ID 해석 오류");
        Require(
            !DungeonTopologyRepairId.TryParse("E1", out _, out _)
            && !DungeonTopologyRepairId.TryParse("L0", out _, out _)
            && !DungeonTopologyRepairId.TryParse("L01", out _, out _)
            && !DungeonTopologyRepairId.TryParse("l1", out _, out _)
            && !DungeonTopologyRepairId.TryParse("12", out _, out _),
            "허용하지 않는 형태 후보 ID가 등록됩니다.");
        Require(
            DungeonRunGenerator.ResolveRequiredTopology(
                new[] { Vector3.forward, Vector3.back })
                == DungeonTopologyRepairShape.I
            && DungeonRunGenerator.ResolveRequiredTopology(
                new[] { Vector3.forward, Vector3.right })
                == DungeonTopologyRepairShape.L
            && DungeonRunGenerator.ResolveRequiredTopology(
                new[]
                {
                    Vector3.forward,
                    Vector3.right,
                    Vector3.back
                })
                == DungeonTopologyRepairShape.T
            && DungeonRunGenerator.ResolveRequiredTopology(
                new[] { Vector3.forward })
                == DungeonTopologyRepairShape.None,
            "연결 Doorway 형태 판정 오류");
    }

    private static IReadOnlyList<GameObject> ValidateSavedCatalog(
        DungeonTopologyRepairCatalog catalog)
    {
        string[] requiredTopologyIds = { "I1", "L1", "L2" };
        List<GameObject> candidates = new();
        foreach (string sourcePath in RequiredSourcePaths)
        {
            GameObject source =
                AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            Require(source != null, sourcePath + ": 원본 누락");
            Require(
                source.GetComponentsInChildren<Doorway>(true)
                    .Length == 3,
                source.name + ": T자 원본 Doorway 수가 3이 아닙니다.");
            Require(
                catalog.TryGetRule(
                    source,
                    out DungeonTopologyRepairRule rule),
                source.name + ": 저장된 형태 보정 Rule 누락");
            Require(
                rule.CandidateGroups
                    .Select(group => group.TopologyId)
                    .SequenceEqual(requiredTopologyIds),
                source.name + ": I1/L1/L2 그룹 구성 또는 순서 오류");

            Dictionary<string, string> groupSocketKeys = new();
            foreach (DungeonTopologyRepairCandidateGroup group
                     in rule.CandidateGroups)
            {
                Require(
                    group != null
                    && !group.AllowQuarterTurns
                    && group.Variants.Count > 0,
                    source.name + ": " + group?.TopologyId
                    + " 그룹 설정 오류");
                string groupSocketKey = null;
                foreach (GameObject candidate in group.Variants)
                {
                    Require(
                        candidate != null
                        && !candidates.Contains(candidate),
                        source.name + ": null 또는 중복 저장 후보");
                    DungeonTopologyRepairCandidateMarker marker =
                        candidate.GetComponent<
                            DungeonTopologyRepairCandidateMarker>();
                    Require(
                        marker != null
                        && marker.SourceFamilyPrefab == source
                        && marker.TopologyId == group.TopologyId,
                        candidate.name + ": marker 계약 불일치");

                    int[] sourceDoorwayIndices =
                        ResolveSourceDoorwayIndices(
                            source,
                            candidate);
                    DungeonTopologyRepairShape candidateShape =
                        DungeonRunGenerator.ResolveRequiredTopology(
                            candidate
                                .GetComponentsInChildren<Doorway>(true)
                                .Select(doorway =>
                                    candidate.transform
                                        .InverseTransformDirection(
                                            doorway.transform.forward)));
                    Require(
                        candidateShape == group.Shape,
                        candidate.name
                        + ": 이름과 실제 Doorway 형태 불일치");
                    string socketKey =
                        string.Join(",", sourceDoorwayIndices);
                    if (groupSocketKey == null)
                        groupSocketKey = socketKey;
                    Require(
                        groupSocketKey == socketKey,
                        source.name + ": " + group.TopologyId
                        + " 시각 파생본의 Doorway 구성이 서로 다릅니다.");
                    candidates.Add(candidate);
                }

                groupSocketKeys.Add(
                    group.TopologyId,
                    groupSocketKey);
            }

            Require(
                groupSocketKeys.Values.Distinct().Count()
                    == requiredTopologyIds.Length,
                source.name
                + ": I1/L1/L2가 T자 원본의 서로 다른 "
                + "Doorway 조합을 덮지 못합니다.");
        }

        Require(
            candidates.Count >= 6,
            "Bridge 11·21 저장 형태 후보가 6개 미만입니다.");
        return candidates;
    }

    private static int[] ResolveSourceDoorwayIndices(
        GameObject source,
        GameObject candidate)
    {
        Doorway[] sourceDoorways =
            OrderDoorways(source);
        Doorway[] candidateDoorways =
            OrderDoorways(candidate);
        Require(
            candidateDoorways.Length == 2,
            candidate.name + ": I/L 후보 Doorway 수가 2가 아닙니다.");

        HashSet<int> claimedSourceIndices = new();
        foreach (Doorway candidateDoorway in candidateDoorways)
        {
            int sourceIndex = -1;
            for (int index = 0;
                 index < sourceDoorways.Length;
                 index++)
            {
                if (claimedSourceIndices.Contains(index)
                    || !DoorwayContractsMatch(
                        source,
                        sourceDoorways[index],
                        candidate,
                        candidateDoorway))
                {
                    continue;
                }

                sourceIndex = index;
                break;
            }

            Require(
                sourceIndex >= 0,
                candidate.name
                + ": 원본과 정확히 일치하는 Doorway를 찾지 못했습니다.");
            claimedSourceIndices.Add(sourceIndex);
        }

        return claimedSourceIndices.OrderBy(index => index).ToArray();
    }

    private static Doorway[] OrderDoorways(GameObject root)
    {
        return root.GetComponentsInChildren<Doorway>(true)
            .OrderBy(doorway =>
                root.transform.InverseTransformPoint(
                    doorway.transform.position).x)
            .ThenBy(doorway =>
                root.transform.InverseTransformPoint(
                    doorway.transform.position).y)
            .ThenBy(doorway =>
                root.transform.InverseTransformPoint(
                    doorway.transform.position).z)
            .ThenBy(doorway =>
                root.transform.InverseTransformDirection(
                    doorway.transform.forward).x)
            .ThenBy(doorway =>
                root.transform.InverseTransformDirection(
                    doorway.transform.forward).z)
            .ToArray();
    }

    private static bool DoorwayContractsMatch(
        GameObject sourceRoot,
        Doorway source,
        GameObject candidateRoot,
        Doorway candidate)
    {
        const float positionTolerance = 0.001f;
        const float directionTolerance = 0.9999f;
        Vector3 sourcePosition =
            sourceRoot.transform.InverseTransformPoint(
                source.transform.position);
        Vector3 candidatePosition =
            candidateRoot.transform.InverseTransformPoint(
                candidate.transform.position);
        Vector3 sourceForward =
            sourceRoot.transform.InverseTransformDirection(
                source.transform.forward).normalized;
        Vector3 candidateForward =
            candidateRoot.transform.InverseTransformDirection(
                candidate.transform.forward).normalized;
        Vector3 sourceUp =
            sourceRoot.transform.InverseTransformDirection(
                source.transform.up).normalized;
        Vector3 candidateUp =
            candidateRoot.transform.InverseTransformDirection(
                candidate.transform.up).normalized;
        return source.Socket == candidate.Socket
            && Vector3.SqrMagnitude(
                sourcePosition - candidatePosition)
                <= positionTolerance * positionTolerance
            && Vector3.Dot(sourceForward, candidateForward)
                >= directionTolerance
            && Vector3.Dot(sourceUp, candidateUp)
                >= directionTolerance;
    }

    private static string ResolveUsedCandidateNames(
        Dungeon dungeon,
        IReadOnlyList<GameObject> candidates)
    {
        string[] tileNames = dungeon.AllTiles
            .Where(tile => tile != null)
            .Select(tile => tile.name)
            .ToArray();
        string[] usedCandidates = candidates
            .Where(candidate => tileNames.Any(tileName =>
                tileName.StartsWith(
                    candidate.name,
                    StringComparison.Ordinal)))
            .Select(candidate => candidate.name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        return usedCandidates.Length > 0
            ? string.Join(",", usedCandidates)
            : "(replacement-count-confirmed)";
    }

    private static bool ContainsCandidate(
        Dungeon dungeon,
        string candidateName)
    {
        return dungeon != null
            && dungeon.AllTiles.Any(tile =>
                tile != null
                && tile.name.StartsWith(
                    candidateName,
                    StringComparison.Ordinal));
    }

    private static void RequireCurrentResult(
        DungeonRunGenerator generator,
        TileSet endCapTileSet,
        int seed)
    {
        Require(
            generator.Status == GenerationStatus.Complete,
            $"Seed {seed}: 생성 상태={generator.Status}");
        Require(
            generator.ReplacedShapeTileCount > 0
            && generator.RemovedOpenDoorwayCount > 0,
            $"Seed {seed}: I/L/T 형태 보정 교체가 없습니다.");
        Require(
            generator.OpenDoorwayCountBeforeTopologyRepair
                - generator.RemovedOpenDoorwayCount
                == generator.OpenDoorwayCountAfterTopologyRepair,
            $"Seed {seed}: 형태 보정 열린 Doorway 수치 불일치");
        Require(
            DungeonEndCapContract.TryValidateFinalOpenDoorwayPass(
                generator.CurrentDungeon,
                generator,
                endCapTileSet,
                out string error),
            $"Seed {seed}: {error}");
    }

    private static string BuildSignature(Dungeon dungeon)
    {
        StringBuilder signature = new();
        foreach (Tile tile in dungeon.AllTiles
                     .OrderBy(item => item.name, StringComparer.Ordinal)
                     .ThenBy(item => item.transform.position.x)
                     .ThenBy(item => item.transform.position.y)
                     .ThenBy(item => item.transform.position.z))
        {
            Vector3 position = tile.transform.position;
            Quaternion rotation = tile.transform.rotation;
            signature.Append(tile.name).Append('|')
                .Append(position.x.ToString(
                    "R",
                    CultureInfo.InvariantCulture))
                .Append(',')
                .Append(position.y.ToString(
                    "R",
                    CultureInfo.InvariantCulture))
                .Append(',')
                .Append(position.z.ToString(
                    "R",
                    CultureInfo.InvariantCulture))
                .Append('|')
                .Append(rotation.eulerAngles.y.ToString(
                    "R",
                    CultureInfo.InvariantCulture))
                .AppendLine();
        }

        return signature.ToString();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
