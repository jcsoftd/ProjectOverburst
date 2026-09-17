using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DunGen;
using UnityEditor;
using UnityEngine;

public sealed class DungeonTopologyRepairCandidateState
{
    internal DungeonTopologyRepairCandidateState(
        string sourceFamilyName,
        DungeonTopologyRepairCandidateGroup group)
    {
        SourceFamilyName = sourceFamilyName;
        TopologyId = group.TopologyId;
        Shape = group.Shape;
        Priority = group.Priority;
        VariantCount = group.Variants.Count;
        AllowQuarterTurns = group.AllowQuarterTurns;
    }

    public string SourceFamilyName { get; }
    public string TopologyId { get; }
    public DungeonTopologyRepairShape Shape { get; }
    public int Priority { get; }
    public int VariantCount { get; }
    public bool AllowQuarterTurns { get; set; }
}

public static class DungeonTopologyRepairAuthoringUtility
{
    public const string CatalogPath =
        "Assets/ProjectOverburst/04_Contents/02_Dungeon/Data/Definitions/"
        + "DTRC_MultistoryDungeon_Pilot.asset";

    private const string Bridge21SourcePath =
        "Assets/ProjectOverburst/04_Contents/02_Dungeon/Content/"
        + "MultistoryDungeons2/Tiles/"
        + "PF_DungeonTile_TD_Bridge_21.prefab";
    private const string Bridge21L1Path =
        "Assets/ProjectOverburst/04_Contents/02_Dungeon/Content/"
        + "MultistoryDungeons2/Tiles/"
        + "PF_DungeonTile_TD_Bridge_21_L1.prefab";
    private const string Bridge21L2Path =
        "Assets/ProjectOverburst/04_Contents/02_Dungeon/Content/"
        + "MultistoryDungeons2/Tiles/"
        + "PF_DungeonTile_TD_Bridge_21_L2.prefab";
    private const string LogPath =
        "Logs/DungeonTopologyRepairAuthoring.log";
    private const string CandidateRefreshLogPath =
        "Logs/DungeonTopologyRepairCandidateRefresh.log";
    private const string GeneratedRootName = "__DungeonAuthoring";
    private const int CandidateAuthoringVersion = 1;

    [MenuItem(
        "OVERBURST/Codex/Setup/World/Dungeon/"
        + "Create Bridge 21 L Repair Candidates")]
    public static void CreateBridge21CandidatesFromMenu()
    {
        Debug.Log(CreateBridge21Candidates());
    }

    [MenuItem(
        "OVERBURST/Tools/World/Dungeon/"
        + "Register Selected Topology Repair Candidates")]
    public static void RegisterSelectedCandidatesFromMenu()
    {
        Debug.Log(RegisterSelectedCandidates());
    }

    [MenuItem(
        "OVERBURST/Tools/World/Dungeon/"
        + "Register Selected Topology Repair Candidates",
        true)]
    private static bool CanRegisterSelectedCandidates()
    {
        return Selection.objects
            .OfType<GameObject>()
            .Count(prefab =>
                !string.IsNullOrEmpty(
                    AssetDatabase.GetAssetPath(prefab)))
            >= 2;
    }

    [MenuItem(
        "OVERBURST/Tools/World/Dungeon/"
        + "Refresh Registered Topology Candidate Walkability")]
    public static void RefreshRegisteredCandidateWalkabilityFromMenu()
    {
        Debug.Log(RefreshRegisteredCandidateWalkability());
    }

    public static void
        RefreshRegisteredCandidateWalkabilityFromCommandLine()
    {
        Directory.CreateDirectory("Logs");
        try
        {
            string report =
                RefreshRegisteredCandidateWalkability();
            File.WriteAllText(CandidateRefreshLogPath, report);
            Debug.Log(report);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            File.WriteAllText(
                CandidateRefreshLogPath,
                exception.ToString());
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static void RunBridge21CandidatesFromCommandLine()
    {
        Directory.CreateDirectory("Logs");
        try
        {
            string report = CreateBridge21Candidates();
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

    public static string RefreshRegisteredCandidateWalkability()
    {
        DungeonTopologyRepairCatalog catalog =
            LoadRequiredAsset<DungeonTopologyRepairCatalog>(
                CatalogPath);
        List<string> reports = new();
        HashSet<GameObject> refreshedCandidates = new();
        foreach (DungeonTopologyRepairRule rule in catalog.Rules)
        {
            Require(
                rule?.SourceFamilyPrefab != null,
                "형태 보정 Rule 원본 누락");
            foreach (DungeonTopologyRepairCandidateGroup group
                     in rule.CandidateGroups)
            {
                Require(
                    group != null,
                    rule.SourceFamilyName
                    + ": null 형태 후보 그룹");
                foreach (GameObject candidate in group.Variants)
                {
                    Require(
                        candidate != null,
                        rule.SourceFamilyName
                        + ": null 형태 후보");
                    if (!refreshedCandidates.Add(candidate))
                        continue;

                    GameObject refreshed = ConfigureCandidate(
                        AssetDatabase.GetAssetPath(candidate),
                        rule.SourceFamilyPrefab,
                        group.TopologyId);
                    DungeonTileAuthoringMarker marker =
                        refreshed.GetComponent<
                            DungeonTileAuthoringMarker>();
                    Require(
                        marker != null,
                        refreshed.name
                        + ": DungeonTileAuthoringMarker 누락");
                    reports.Add(
                        refreshed.name
                        + $": Ground={marker.WalkableSurfaceCount}, "
                        + $"Ramp={marker.StairRampCount}");
                }
            }
        }

        Require(
            refreshedCandidates.Count > 0,
            "갱신할 등록 형태 후보가 없습니다.");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(
            ImportAssetOptions.ForceSynchronousImport);
        return "[DungeonTopologyRepairCandidateRefresh] PASS\n"
            + $"CandidateCount={refreshedCandidates.Count}\n"
            + string.Join("\n", reports);
    }

    public static string CreateBridge21Candidates()
    {
        GameObject source = LoadRequiredAsset<GameObject>(
            Bridge21SourcePath);
        Require(
            source.GetComponent<Tile>() != null,
            "Bridge_21 루트 DunGen Tile 누락");
        Require(
            source.GetComponentsInChildren<Doorway>(true).Length == 3,
            "Bridge_21은 T자 기준 Doorway 3개여야 합니다.");

        bool createdL1 = CopyCandidateIfMissing(
            Bridge21SourcePath,
            Bridge21L1Path);
        bool createdL2 = CopyCandidateIfMissing(
            Bridge21SourcePath,
            Bridge21L2Path);
        source = LoadRequiredAsset<GameObject>(
            Bridge21SourcePath); // 복제 임포트 뒤 최신 AssetDatabase 참조를 다시 받는다.
        GameObject l1 = ConfigureCandidate(
            Bridge21L1Path,
            source,
            "L1");
        GameObject l2 = ConfigureCandidate(
            Bridge21L2Path,
            source,
            "L2");

        DungeonTopologyRepairCatalog catalog =
            LoadOrCreateCatalog();
        DungeonTopologyRepairRule rule =
            catalog.GetOrCreateRule(source);
        rule.UpsertGroup("L1", false, new[] { l1 });
        rule.UpsertGroup("L2", false, new[] { l2 });
        EditorUtility.SetDirty(catalog);

        DungeonRunDefinition definition =
            LoadRequiredAsset<DungeonRunDefinition>(
                DungeonRunSceneAuthoringBuilder.DefinitionPath);
        definition.ConfigureTopologyRepairs(catalog);
        EditorUtility.SetDirty(definition);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(
            ImportAssetOptions.ForceSynchronousImport);

        string tileSetReport =
            DungeonTileFamilyWeightUtility
                .SynchronizePreservingFamilyWeights(false);
        ValidateCandidate(l1, source, "L1");
        ValidateCandidate(l2, source, "L2");
        Require(
            !IsInActiveTileSets(l1)
            && !IsInActiveTileSets(l2),
            "형태 보정 후보가 일반 활성 TileSet에 포함됐습니다.");

        AssetDatabase.SaveAssets();
        return "[DungeonTopologyRepairAuthoring] PASS\n"
            + $"Bridge21Doorways=3\n"
            + $"L1={(createdL1 ? "Created" : "Preserved")}\n"
            + $"L2={(createdL2 ? "Created" : "Preserved")}\n"
            + "Priority=L1->L2\n"
            + "QuarterTurns=Disabled\n"
            + "ActiveTileSetExcluded=1\n"
            + tileSetReport;
    }

    public static IReadOnlyList<DungeonTopologyRepairCandidateState>
        LoadCandidateStates()
    {
        DungeonTopologyRepairCatalog catalog =
            LoadRequiredAsset<DungeonTopologyRepairCatalog>(
                CatalogPath);
        return catalog.Rules
            .Where(rule => rule != null)
            .SelectMany(rule => rule.CandidateGroups
                .Where(group => group != null)
                .Select(group =>
                    new DungeonTopologyRepairCandidateState(
                        rule.SourceFamilyName,
                        group)))
            .OrderBy(
                state => state.SourceFamilyName,
                StringComparer.Ordinal)
            .ThenBy(
                state => state.Shape)
            .ThenBy(state => state.Priority)
            .ToArray();
    }

    public static string ApplyCandidateStates(
        IReadOnlyList<DungeonTopologyRepairCandidateState> states)
    {
        Require(states != null, "형태 보정 후보 목록이 없습니다.");
        DungeonTopologyRepairCatalog catalog =
            LoadRequiredAsset<DungeonTopologyRepairCatalog>(
                CatalogPath);
        Dictionary<string, DungeonTopologyRepairCandidateState>
            stateByKey = states.ToDictionary(
                state => BuildStateKey(
                    state.SourceFamilyName,
                    state.TopologyId),
                state => state,
                StringComparer.Ordinal);
        int appliedCount = 0;
        for (int ruleIndex = 0;
             ruleIndex < catalog.Rules.Count;
             ruleIndex++)
        {
            DungeonTopologyRepairRule rule =
                catalog.Rules[ruleIndex];
            for (int groupIndex = 0;
                 groupIndex < rule.CandidateGroups.Count;
                 groupIndex++)
            {
                DungeonTopologyRepairCandidateGroup group =
                    rule.CandidateGroups[groupIndex];
                string key = BuildStateKey(
                    rule.SourceFamilyName,
                    group.TopologyId);
                Require(
                    stateByKey.TryGetValue(
                        key,
                        out DungeonTopologyRepairCandidateState state),
                    "형태 보정 후보 누락: " + key);
                group.Configure(
                    group.TopologyId,
                    state.AllowQuarterTurns,
                    group.Variants);
                appliedCount++;
            }
        }

        Require(
            appliedCount == states.Count,
            $"형태 보정 후보 개수 불일치: "
            + $"{appliedCount}/{states.Count}");
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        return $"형태 보정 후보 설정 적용 완료: {appliedCount}개";
    }

    public static string RegisterSelectedCandidates()
    {
        List<GameObject> selectedPrefabs = Selection.objects
            .OfType<GameObject>()
            .Where(prefab =>
                AssetDatabase.GetAssetPath(prefab)
                    .EndsWith(
                        ".prefab",
                        StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();
        Require(
            selectedPrefabs.Count >= 2,
            "Project 창에서 원본 기본본과 형태 후보를 함께 선택하세요.");

        HashSet<GameObject> activePrefabs =
            LoadActiveTileSetPrefabs();
        List<GameObject> sourceCandidates =
            selectedPrefabs
                .Where(prefab =>
                    activePrefabs.Contains(prefab)
                    && !HasTopologyCandidateSuffix(prefab.name)
                    && prefab.name
                        == DungeonTileVisualFamilyName.Resolve(
                            prefab.name))
                .ToList();
        Require(
            sourceCandidates.Count == 1,
            "선택 항목에는 활성 기본본이 정확히 "
            + "1개 있어야 합니다.");

        GameObject source = sourceCandidates[0];
        Require(
            source.GetComponentsInChildren<Doorway>(true).Length >= 2,
            source.name + ": Doorway 2개 이상인 원본만 등록할 수 있습니다.");
        string candidatePrefix = source.name + "_";
        List<GameObject> repairCandidates =
            selectedPrefabs
                .Where(prefab => prefab != source)
                .ToList();
        Dictionary<string, List<GameObject>>
            variantsByTopology = new(StringComparer.Ordinal);
        for (int i = 0; i < repairCandidates.Count; i++)
        {
            GameObject candidate = repairCandidates[i];
            Require(
                PrefabUtility.GetPrefabAssetType(candidate)
                    == PrefabAssetType.Regular,
                candidate.name
                + ": 독립 Regular Prefab만 등록할 수 있습니다.");
            string topologyFamilyName =
                DungeonTileVisualFamilyName.Resolve(candidate.name);
            Require(
                topologyFamilyName.StartsWith(
                    candidatePrefix,
                    StringComparison.Ordinal),
                candidate.name
                + ": 원본 계열 뒤에 _L1, _T1, _I1 같은 "
                + "형태 ID가 필요합니다.");
            string topologyId =
                topologyFamilyName.Substring(
                    candidatePrefix.Length);
            Require(
                DungeonTopologyRepairId.TryParse(
                    topologyId,
                    out _,
                    out _),
                candidate.name
                + ": 형태 ID는 I1, L1, T1 형식이어야 합니다. "
                + "E 후보는 등록하지 않습니다.");
            if (!variantsByTopology.TryGetValue(
                    topologyId,
                    out List<GameObject> variants))
            {
                variants = new List<GameObject>();
                variantsByTopology.Add(topologyId, variants);
            }

            variants.Add(
                ConfigureCandidate(
                    AssetDatabase.GetAssetPath(candidate),
                    source,
                    topologyId));
        }

        DungeonTopologyRepairCatalog catalog =
            LoadOrCreateCatalog();
        DungeonTopologyRepairRule rule =
            catalog.GetOrCreateRule(source);
        foreach (KeyValuePair<string, List<GameObject>> pair
                 in variantsByTopology)
        {
            rule.UpsertGroup(
                pair.Key,
                false,
                pair.Value);
        }

        EditorUtility.SetDirty(catalog);
        DungeonRunDefinition definition =
            LoadRequiredAsset<DungeonRunDefinition>(
                DungeonRunSceneAuthoringBuilder.DefinitionPath);
        definition.ConfigureTopologyRepairs(catalog);
        EditorUtility.SetDirty(definition);
        AssetDatabase.SaveAssets();
        string tileSetReport =
            DungeonTileFamilyWeightUtility
                .SynchronizePreservingFamilyWeights(false);
        return "[DungeonTopologyRepairRegister] PASS\n"
            + $"Source={source.name}\n"
            + "Groups="
            + string.Join(
                ",",
                variantsByTopology
                    .OrderBy(pair => pair.Key)
                    .Select(pair =>
                        $"{pair.Key}:{pair.Value.Count}"))
            + "\n"
            + tileSetReport;
    }

    private static bool HasTopologyCandidateSuffix(string prefabName)
    {
        string topologyFamilyName =
            DungeonTileVisualFamilyName.Resolve(prefabName);
        int separatorIndex =
            topologyFamilyName.LastIndexOf('_');
        if (separatorIndex <= 0
            || separatorIndex >= topologyFamilyName.Length - 1)
        {
            return false;
        }

        string topologyId =
            topologyFamilyName.Substring(separatorIndex + 1);
        return DungeonTopologyRepairId.TryParse(
            topologyId,
            out _,
            out _);
    }

    private static bool CopyCandidateIfMissing(
        string sourcePath,
        string candidatePath)
    {
        GameObject existing =
            AssetDatabase.LoadAssetAtPath<GameObject>(candidatePath);
        if (existing != null)
            return false; // 사용자 편집본은 재실행해도 덮어쓰지 않는다.

        Require(
            AssetDatabase.CopyAsset(sourcePath, candidatePath),
            "형태 보정 후보 복제 실패: " + candidatePath);
        AssetDatabase.ImportAsset(
            candidatePath,
            ImportAssetOptions.ForceSynchronousImport);
        DungeonContentAuthoringBuilder.NormalizeProjectPrefabIdentity(
            candidatePath,
            Path.GetFileNameWithoutExtension(sourcePath));
        return true;
    }

    private static GameObject ConfigureCandidate(
        string candidatePath,
        GameObject source,
        string topologyId)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(candidatePath);
        try
        {
            root.name = Path.GetFileNameWithoutExtension(candidatePath);
            int missingScriptCount =
                GameObjectUtility
                    .GetMonoBehavioursWithMissingScriptCount(root);
            Require(
                missingScriptCount == 0,
                candidatePath
                + ": Missing Script 수="
                + missingScriptCount);
            RebuildCandidateWalkability(root);
            DungeonTopologyRepairCandidateMarker marker =
                root.GetComponent<DungeonTopologyRepairCandidateMarker>();
            if (marker == null)
            {
                marker = root.AddComponent<
                    DungeonTopologyRepairCandidateMarker>();
            }

            marker.Configure(
                source,
                topologyId,
                CandidateAuthoringVersion);
            EditorUtility.SetDirty(marker);
            PrefabUtility.SaveAsPrefabAsset(
                root,
                candidatePath,
                out bool saved);
            Require(saved, "형태 보정 후보 저장 실패: " + candidatePath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return LoadRequiredAsset<GameObject>(candidatePath);
    }

    private static void RebuildCandidateWalkability(
        GameObject root)
    {
        Transform generatedRoot =
            root.transform.Find(GeneratedRootName);
        Require(
            generatedRoot != null,
            root.name + ": " + GeneratedRootName + " 누락");
        Transform previousRampRoot =
            generatedRoot.Find("StairRamps");
        if (previousRampRoot != null)
        {
            UnityEngine.Object.DestroyImmediate(
                previousRampRoot.gameObject);
        }

        GameObject rampRootObject = new("StairRamps");
        rampRootObject.transform.SetParent(generatedRoot, false);
        int groundLayer = LayerMask.NameToLayer("Ground");
        Require(
            groundLayer >= 0,
            "Ground 레이어를 찾지 못했습니다.");
        float minimumWalkableHeight =
            DungeonWalkableHeightUtility.ResolveMinimumHeight(root);
        DungeonStairRampBuilder.BuildResult rampResult =
            DungeonStairRampBuilder.Rebuild(
                root,
                rampRootObject.transform,
                groundLayer,
                minimumWalkableHeight);

        DungeonTileAuthoringMarker authoringMarker =
            root.GetComponent<DungeonTileAuthoringMarker>();
        Require(
            authoringMarker != null,
            root.name + ": DungeonTileAuthoringMarker 누락");
        int walkableSurfaceCount = root
            .GetComponentsInChildren<Collider>(true)
            .Count(collider =>
                collider.gameObject.layer == groundLayer
                && !collider.transform.IsChildOf(
                    generatedRoot));
        authoringMarker.Configure(
            authoringMarker.SourcePrefab,
            authoringMarker.AuthoringVersion,
            authoringMarker.IsExitTile,
            walkableSurfaceCount,
            rampResult.RampCount,
            authoringMarker.IsDirectSourceTile,
            authoringMarker.IsStandaloneProjectTile);
        EditorUtility.SetDirty(authoringMarker);
    }

    private static DungeonTopologyRepairCatalog LoadOrCreateCatalog()
    {
        DungeonTopologyRepairCatalog catalog =
            AssetDatabase.LoadAssetAtPath<
                DungeonTopologyRepairCatalog>(CatalogPath);
        if (catalog != null)
            return catalog;

        catalog = ScriptableObject.CreateInstance<
            DungeonTopologyRepairCatalog>();
        AssetDatabase.CreateAsset(catalog, CatalogPath);
        AssetDatabase.ImportAsset(
            CatalogPath,
            ImportAssetOptions.ForceSynchronousImport);
        return LoadRequiredAsset<DungeonTopologyRepairCatalog>(
            CatalogPath);
    }

    private static void ValidateCandidate(
        GameObject candidate,
        GameObject source,
        string topologyId)
    {
        Require(candidate != null, topologyId + " 후보 누락");
        Require(candidate != source, topologyId + "가 원본과 같습니다.");
        Require(
            PrefabUtility.GetPrefabAssetType(candidate)
                == PrefabAssetType.Regular,
            topologyId + "가 독립 Regular Prefab이 아닙니다.");
        Require(
            candidate.GetComponent<Tile>() != null,
            topologyId + " 루트 DunGen Tile 누락");
        DungeonTopologyRepairCandidateMarker marker =
            candidate.GetComponent<
                DungeonTopologyRepairCandidateMarker>();
        Require(
            marker != null
            && marker.SourceFamilyPrefab != null
            && AssetDatabase.GetAssetPath(
                marker.SourceFamilyPrefab)
                == AssetDatabase.GetAssetPath(source)
            && marker.TopologyId == topologyId,
            topologyId + " 형태 보정 marker 불일치");
    }

    private static bool IsInActiveTileSets(GameObject candidate)
    {
        string[] paths =
        {
            DungeonContentAuthoringBuilder.RegularTileSetOutputPath,
            DungeonContentAuthoringBuilder.ArenaTileSetOutputPath,
            DungeonContentAuthoringBuilder.EndCapTileSetOutputPath,
            DungeonContentAuthoringBuilder.StartTileSetOutputPath,
            DungeonContentAuthoringBuilder.ExitTileSetOutputPath
        };
        for (int i = 0; i < paths.Length; i++)
        {
            TileSet tileSet = LoadRequiredAsset<TileSet>(paths[i]);
            if (tileSet.TileWeights.Weights.Exists(weight =>
                    weight?.Value == candidate))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildStateKey(
        string sourceFamilyName,
        string topologyId)
    {
        return (sourceFamilyName?.Trim() ?? string.Empty)
            + "|"
            + (topologyId?.Trim() ?? string.Empty);
    }

    private static HashSet<GameObject> LoadActiveTileSetPrefabs()
    {
        string[] paths =
        {
            DungeonContentAuthoringBuilder.RegularTileSetOutputPath,
            DungeonContentAuthoringBuilder.ArenaTileSetOutputPath,
            DungeonContentAuthoringBuilder.EndCapTileSetOutputPath,
            DungeonContentAuthoringBuilder.StartTileSetOutputPath,
            DungeonContentAuthoringBuilder.ExitTileSetOutputPath
        };
        HashSet<GameObject> prefabs = new();
        for (int i = 0; i < paths.Length; i++)
        {
            TileSet tileSet = LoadRequiredAsset<TileSet>(paths[i]);
            foreach (GameObjectChance weight
                     in tileSet.TileWeights.Weights)
            {
                if (weight?.Value != null)
                    prefabs.Add(weight.Value);
            }
        }

        return prefabs;
    }

    private static T LoadRequiredAsset<T>(string path)
        where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        Require(asset != null, "필수 에셋 누락: " + path);
        return asset;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
