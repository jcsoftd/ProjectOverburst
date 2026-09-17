using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DunGen;
using UnityEditor;
using UnityEngine;

public sealed class DungeonTileFamilyWeightState
{
    private readonly List<GameObject> variants;

    internal DungeonTileFamilyWeightState(
        string familyName,
        DungeonTileRole role,
        IEnumerable<GameObject> variants,
        float mainPathWeight,
        float branchPathWeight)
    {
        FamilyName = familyName;
        Role = role;
        this.variants = variants.ToList();
        MainPathWeight = mainPathWeight;
        BranchPathWeight = branchPathWeight;
    }

    public string FamilyName { get; }
    public DungeonTileRole Role { get; }
    public IReadOnlyList<GameObject> Variants => variants;
    public int VariantCount => variants.Count;
    public int MinimumDoorwayCount => variants.Count == 0
        ? 0
        : variants.Min(variant =>
            variant.GetComponentsInChildren<Doorway>(true).Length);
    public bool SupportsRequiredMainPathArena =>
        Role != DungeonTileRole.Arena || MinimumDoorwayCount >= 2;
    public float MainPathWeight { get; set; }
    public float BranchPathWeight { get; set; }
}

public static class DungeonTileFamilyWeightUtility
{
    private const string ProjectTilePrefix = "PF_DungeonTile_";
    private const string ArenaSplitLogPath =
        "Logs/DungeonArenaTileSetMigration.log";

    [MenuItem(
        "OVERBURST/Codex/Setup/World/Dungeon/"
        + "Synchronize Dungeon Role TileSets")]
    public static void SynchronizeArenaSplitFromMenu()
    {
        Debug.Log(SynchronizeArenaSplitAndDefinition());
    }

    public static void SynchronizeArenaSplitFromCommandLine()
    {
        Directory.CreateDirectory("Logs");
        try
        {
            string report = SynchronizeArenaSplitAndDefinition();
            File.WriteAllText(ArenaSplitLogPath, report);
            Debug.Log(report);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            File.WriteAllText(
                ArenaSplitLogPath,
                exception.ToString());
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static string SynchronizeArenaSplitAndDefinition()
    {
        MigrateEndCapPrefabNames();
        if (AssetDatabase.LoadMainAssetAtPath(
                DungeonContentAuthoringBuilder.ArenaTileSetOutputPath)
            == null)
        {
            Require(
                AssetDatabase.CopyAsset(
                    DungeonContentAuthoringBuilder
                        .SourceRegularTileSetPath,
                    DungeonContentAuthoringBuilder
                        .ArenaTileSetOutputPath),
                "Arena TileSet 생성 실패");
            AssetDatabase.ImportAsset(
                DungeonContentAuthoringBuilder.ArenaTileSetOutputPath,
                ImportAssetOptions.ForceSynchronousImport);
        }
        if (AssetDatabase.LoadMainAssetAtPath(
                DungeonContentAuthoringBuilder.EndCapTileSetOutputPath)
            == null)
        {
            Require(
                AssetDatabase.CopyAsset(
                    DungeonContentAuthoringBuilder
                        .SourceRegularTileSetPath,
                    DungeonContentAuthoringBuilder
                        .EndCapTileSetOutputPath),
                "EndCap TileSet 생성 실패");
            AssetDatabase.ImportAsset(
                DungeonContentAuthoringBuilder.EndCapTileSetOutputPath,
                ImportAssetOptions.ForceSynchronousImport);
        }

        string weightReport =
            SynchronizePreservingFamilyWeights(false);
        TileSet arenas = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.ArenaTileSetOutputPath);
        TileSet endCaps = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.EndCapTileSetOutputPath);
        TileSet regular = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.RegularTileSetOutputPath);
        DungeonArchetype sourceArchetype =
            LoadRequiredAsset<DungeonArchetype>(
                DungeonContentAuthoringBuilder.SourceArchetypePath);
        DungeonArchetype archetype =
            LoadRequiredAsset<DungeonArchetype>(
                DungeonContentAuthoringBuilder.ArchetypeOutputPath);
        archetype.TileSets = new List<TileSet> { regular };
        archetype.BranchCapTileSets = new List<TileSet>();
        archetype.BranchCapType = sourceArchetype.BranchCapType;
        archetype.BranchingDepth = new IntRange(
            sourceArchetype.BranchingDepth.Min,
            sourceArchetype.BranchingDepth.Max);
        EditorUtility.SetDirty(archetype);
        DungeonRunDefinition definition =
            LoadRequiredAsset<DungeonRunDefinition>(
                DungeonRunSceneAuthoringBuilder.DefinitionPath);
        definition.ConfigureArenaTiles(arenas, 4, 24);
        definition.ConfigureEndCapTiles(endCaps);
        EditorUtility.SetDirty(definition);
        AssetDatabase.SaveAssets();

        string validation = ValidateOrThrow();
        return "[DungeonArenaTileSetMigration] PASS\n"
            + weightReport + "\n"
            + validation + "\n"
            + "Bridge=24\n"
            + "Arena=8\n"
            + "EndCap=1\n"
            + "EndCapMode=FinalOpenDoorwayPass\n"
            + $"BranchingDepth={archetype.BranchingDepth.Min}"
            + $"~{archetype.BranchingDepth.Max}\n"
            + "DefaultTotalMainPath=24\n"
            + "DefaultArenaCount=4";
    }

    public static IReadOnlyList<string> GetProjectTileNames()
    {
        return DiscoverFamilies()
            .SelectMany(family => family.Variants)
            .Select(prefab => RemoveProjectTilePrefix(prefab.name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<string>
        GetPositiveWeightProjectPrefabNames()
    {
        TileSet regular = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.RegularTileSetOutputPath);
        TileSet arenas = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.ArenaTileSetOutputPath);
        TileSet endCaps = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.EndCapTileSetOutputPath);
        TileSet starts = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.StartTileSetOutputPath);
        TileSet exits = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.ExitTileSetOutputPath);

        IEnumerable<GameObjectChance> activeBridgeWeights =
            regular.TileWeights.Weights.Where(weight =>
                weight?.Value != null
                && (weight.MainPathWeight > 0f
                    || weight.BranchPathWeight > 0f));
        IEnumerable<GameObjectChance> activeArenaWeights =
            arenas.TileWeights.Weights.Where(weight =>
                weight?.Value != null
                && weight.MainPathWeight > 0f
                && SupportsRequiredMainPathArena(weight.Value));
        IEnumerable<GameObjectChance> activeEndCapWeights =
            endCaps.TileWeights.Weights.Where(weight =>
                weight?.Value != null
                && weight.BranchPathWeight > 0f);
        IEnumerable<GameObjectChance> activeEndpointWeights =
            starts.TileWeights.Weights
                .Concat(exits.TileWeights.Weights)
                .Where(weight =>
                    weight?.Value != null
                    && weight.MainPathWeight > 0f);

        return activeBridgeWeights
            .Concat(activeArenaWeights)
            .Concat(activeEndCapWeights)
            .Concat(activeEndpointWeights)
            .Select(weight => weight.Value.name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<string>
        GetIneligibleRequiredMainPathArenaPrefabNames()
    {
        TileSet arenas = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.ArenaTileSetOutputPath);
        return arenas.TileWeights.Weights
            .Where(weight =>
                weight?.Value != null
                && !SupportsRequiredMainPathArena(weight.Value))
            .Select(weight => weight.Value.name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    public static bool SupportsRequiredMainPathArena(
        GameObject prefab)
    {
        return prefab != null
            && prefab.GetComponentsInChildren<Doorway>(true).Length >= 2;
    }

    public static IReadOnlyList<DungeonTileFamilyWeightState>
        LoadFamilyWeights()
    {
        List<TileFamily> families = DiscoverFamilies();
        TileSet regular = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.RegularTileSetOutputPath);
        TileSet arenas = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.ArenaTileSetOutputPath);
        TileSet endCaps = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.EndCapTileSetOutputPath);
        TileSet starts = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.StartTileSetOutputPath);
        TileSet exits = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.ExitTileSetOutputPath);
        TileSet sourceRegular = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.SourceRegularTileSetPath);
        TileSet sourceExits = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.SourceExitTileSetPath);

        List<DungeonTileFamilyWeightState> states = new();
        for (int i = 0; i < families.Count; i++)
        {
            TileFamily family = families[i];
            TileSet target = ResolveTargetTileSet(
                family.Role,
                regular,
                arenas,
                endCaps,
                starts,
                exits);
            TileSet source =
                family.Role == DungeonTileRole.Bridge
                || family.Role == DungeonTileRole.Arena
                || family.Role == DungeonTileRole.EndCap
                    ? sourceRegular
                    : sourceExits;
            List<GameObjectChance> currentWeights = target
                .TileWeights.Weights
                .Where(weight =>
                    weight?.Value != null
                    && family.Variants.Contains(weight.Value))
                .ToList();
            if (currentWeights.Count == 0
                && family.Role == DungeonTileRole.Arena)
            {
                // Arena TileSet 분리 전 일반 TileSet에 저장한 가중치를 보존한다.
                currentWeights = regular.TileWeights.Weights
                    .Where(weight =>
                        weight?.Value != null
                        && family.Variants.Contains(weight.Value))
                    .ToList();
            }
            else if (currentWeights.Count == 0
                && family.Role == DungeonTileRole.EndCap)
            {
                // Arena에서 EndCap으로 역할을 바꾸기 전 가중치를 보존한다.
                currentWeights = arenas.TileWeights.Weights
                    .Concat(regular.TileWeights.Weights)
                    .Where(weight =>
                        weight?.Value != null
                        && family.Variants.Contains(weight.Value))
                    .ToList();
            }

            float mainPathWeight;
            float branchPathWeight;
            if (currentWeights.Count > 0)
            {
                mainPathWeight = currentWeights.Sum(
                    weight => Mathf.Max(0f, weight.MainPathWeight));
                branchPathWeight = currentWeights.Sum(
                    weight => Mathf.Max(0f, weight.BranchPathWeight));

                if (target.TileWeights.Weights.Any(
                        weight => weight?.Value == null))
                {
                    GameObjectChance template =
                        FindSourceTemplate(source, family);
                    if (template != null)
                    {
                        mainPathWeight = Mathf.Max(
                            mainPathWeight,
                            Mathf.Max(
                                0f,
                                template.MainPathWeight));
                        branchPathWeight = Mathf.Max(
                            branchPathWeight,
                            Mathf.Max(
                                0f,
                                template.BranchPathWeight));
                    }
                }
            }
            else
            {
                GameObjectChance template =
                    FindSourceTemplate(source, family);
                mainPathWeight = template != null
                    ? Mathf.Max(0f, template.MainPathWeight)
                    : 1f;
                branchPathWeight = template != null
                    ? Mathf.Max(0f, template.BranchPathWeight)
                    : 1f;
            }

            states.Add(
                new DungeonTileFamilyWeightState(
                    family.FamilyName,
                    family.Role,
                    family.Variants,
                    mainPathWeight,
                    branchPathWeight));
        }

        return states;
    }

    public static string SynchronizePreservingFamilyWeights(
        bool saveAssets)
    {
        return ApplyFamilyWeights(LoadFamilyWeights(), saveAssets);
    }

    public static string ApplyFamilyWeights(
        IReadOnlyList<DungeonTileFamilyWeightState> states,
        bool saveAssets)
    {
        Require(states != null, "타일 계열 가중치 목록이 없습니다.");
        List<TileFamily> families = DiscoverFamilies();
        Dictionary<string, DungeonTileFamilyWeightState> stateByFamily =
            states.ToDictionary(
                state => BuildRoleKey(
                    state.FamilyName,
                    state.Role),
                state => state,
                StringComparer.Ordinal);

        Require(
            stateByFamily.Count == families.Count,
            "타일 계열 목록이 변경됐습니다. 목록을 새로고침하세요.");

        TileSet regular = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.RegularTileSetOutputPath);
        TileSet arenas = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.ArenaTileSetOutputPath);
        TileSet endCaps = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.EndCapTileSetOutputPath);
        TileSet starts = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.StartTileSetOutputPath);
        TileSet exits = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.ExitTileSetOutputPath);
        TileSet sourceRegular = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.SourceRegularTileSetPath);
        TileSet sourceExits = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.SourceExitTileSetPath);

        ConfigureTileSet(
            regular,
            sourceRegular,
            families.Where(family =>
                family.Role == DungeonTileRole.Bridge),
            stateByFamily);
        ConfigureTileSet(
            arenas,
            sourceRegular,
            families.Where(family =>
                family.Role == DungeonTileRole.Arena),
            stateByFamily);
        ConfigureTileSet(
            endCaps,
            sourceRegular,
            families.Where(family =>
                family.Role == DungeonTileRole.EndCap),
            stateByFamily);
        ConfigureTileSet(
            starts,
            sourceExits,
            families.Where(family =>
                family.Role == DungeonTileRole.Start),
            stateByFamily);
        ConfigureTileSet(
            exits,
            sourceExits,
            families.Where(family =>
                family.Role == DungeonTileRole.Exit),
            stateByFamily);

        if (saveAssets)
            AssetDatabase.SaveAssets();

        int variantCount = families.Sum(family => family.Variants.Count);
        return "타일 계열 가중치 적용 완료: "
            + $"계열 {families.Count}, 후보 {variantCount}";
    }

    public static string ValidateOrThrow()
    {
        List<TileFamily> families = DiscoverFamilies();
        TileSet regular = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.RegularTileSetOutputPath);
        TileSet arenas = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.ArenaTileSetOutputPath);
        TileSet endCaps = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.EndCapTileSetOutputPath);
        TileSet starts = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.StartTileSetOutputPath);
        TileSet exits = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.ExitTileSetOutputPath);

        ValidateTileSet(
            regular,
            families.Where(family =>
                family.Role == DungeonTileRole.Bridge).ToList());
        ValidateTileSet(
            arenas,
            families.Where(family =>
                family.Role == DungeonTileRole.Arena).ToList());
        ValidateTileSet(
            endCaps,
            families.Where(family =>
                family.Role == DungeonTileRole.EndCap).ToList());
        ValidateTileSet(
            starts,
            families.Where(family =>
                family.Role == DungeonTileRole.Start).ToList());
        ValidateTileSet(
            exits,
            families.Where(family =>
                family.Role == DungeonTileRole.Exit).ToList());

        return "TileFamilyWeight=PASS, "
            + $"FamilyCount={families.Count}, "
            + $"VariantCount={families.Sum(family => family.Variants.Count)}";
    }

    public static string NormalizeTileFamilyName(string value)
    {
        string normalized = RemoveProjectTilePrefix(
            value?.Trim() ?? string.Empty);
        return TryRemoveVariantSuffix(
            normalized,
            out string familyName)
            ? familyName
            : normalized;
    }

    private static void MigrateEndCapPrefabNames()
    {
        (string SourceName, string TargetName)[] migrations =
        {
            ("TD_Tile_24", "PF_DungeonTile_TD_EndCap_01"),
            ("TD_Tile_31", "PF_DungeonTile_TD_Arena_06"),
            ("TD_Tile_32", "PF_DungeonTile_TD_Arena_07"),
            ("TD_Tile_33", "PF_DungeonTile_TD_Arena_08")
        };

        for (int migrationIndex = 0;
             migrationIndex < migrations.Length;
             migrationIndex++)
        {
            (string sourceName, string targetName) =
                migrations[migrationIndex];
            string[] prefabGuids = AssetDatabase.FindAssets(
                "t:Prefab",
                new[] { DungeonContentAuthoringBuilder.TileOutputRoot });
            string currentPath = prefabGuids
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(path =>
                {
                    GameObject prefab =
                        AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    DungeonTileAuthoringMarker marker =
                        prefab?.GetComponent<DungeonTileAuthoringMarker>();
                    return marker?.SourcePrefab != null
                        && string.Equals(
                            marker.SourcePrefab.name,
                            sourceName,
                            StringComparison.Ordinal);
                });
            Require(
                !string.IsNullOrEmpty(currentPath),
                "EndCap 역할 전환 대상 Project 타일 누락: "
                + sourceName);

            string targetPath =
                DungeonContentAuthoringBuilder.TileOutputRoot
                + "/"
                + targetName
                + ".prefab";
            if (string.Equals(
                    currentPath,
                    targetPath,
                    StringComparison.Ordinal))
            {
                DungeonContentAuthoringBuilder
                    .NormalizeProjectPrefabIdentity(
                        targetPath,
                        Path.GetFileNameWithoutExtension(currentPath));
                continue;
            }

            Require(
                AssetDatabase.LoadMainAssetAtPath(targetPath) == null,
                "EndCap 역할 전환 대상 경로가 이미 사용 중입니다: "
                + targetPath);
            string previousRootName =
                Path.GetFileNameWithoutExtension(currentPath);
            string moveError =
                AssetDatabase.MoveAsset(currentPath, targetPath);
            Require(
                string.IsNullOrEmpty(moveError),
                "EndCap 역할 전환 리네임 실패: "
                + currentPath
                + " -> "
                + targetPath
                + " | "
                + moveError);
            DungeonContentAuthoringBuilder
                .NormalizeProjectPrefabIdentity(
                    targetPath,
                    previousRootName);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(
            ImportAssetOptions.ForceSynchronousImport);
    }

    private static List<TileFamily> DiscoverFamilies()
    {
        string[] guids = AssetDatabase.FindAssets(
            "t:Prefab",
            new[] { DungeonContentAuthoringBuilder.TileOutputRoot });
        List<GameObject> discoveredPrefabs = guids
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path =>
                path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                && path.StartsWith(
                    DungeonContentAuthoringBuilder.TileOutputRoot + "/",
                    StringComparison.Ordinal))
            .Select(path => AssetDatabase.LoadAssetAtPath<GameObject>(path))
            .Where(prefab => prefab != null)
            .OrderBy(prefab => prefab.name, StringComparer.Ordinal)
            .ToList();
        List<GameObject> prefabs = discoveredPrefabs
            .Where(prefab =>
                prefab.GetComponent<
                    DungeonTopologyRepairCandidateMarker>() == null)
            .ToList(); // 형태 보정 후보는 일반 DunGen 확률 풀에 넣지 않는다.

        Require(prefabs.Count > 0, "Project 던전 타일 Prefab이 없습니다.");
        Require(
            prefabs.Select(prefab => prefab.name)
                .Distinct(StringComparer.Ordinal)
                .Count() == prefabs.Count,
            "Project 던전 타일 Prefab 이름이 중복됩니다.");

        for (int i = 0; i < prefabs.Count; i++)
        {
            GameObject prefab = prefabs[i];
            Require(
                prefab.name.StartsWith(
                    ProjectTilePrefix,
                    StringComparison.Ordinal),
                "Project 던전 타일 이름 규칙 위반: " + prefab.name);
            Require(
                PrefabUtility.GetPrefabAssetType(prefab)
                    == PrefabAssetType.Regular,
                prefab.name + ": 독립 Regular Prefab이 아닙니다.");
            Require(
                prefab.GetComponent<Tile>() != null,
                prefab.name + ": 루트 DunGen Tile이 없습니다.");
        }

        HashSet<string> logicalNames = prefabs
            .Select(prefab => RemoveProjectTilePrefix(prefab.name))
            .ToHashSet(StringComparer.Ordinal);
        Dictionary<string, TileFamily> familyByName =
            new(StringComparer.Ordinal);

        for (int i = 0; i < prefabs.Count; i++)
        {
            GameObject prefab = prefabs[i];
            string logicalName =
                RemoveProjectTilePrefix(prefab.name);
            string familyName = logicalName;
            // 기본본이 있을 때만 끝의 대문자 토큰을 변형 접미사로 본다.
            if (TryRemoveVariantSuffix(
                    logicalName,
                    out string candidateFamily)
                && logicalNames.Contains(candidateFamily))
            {
                familyName = candidateFamily;
            }

            DungeonTileRole role =
                DungeonTileNamingCatalog.GetProjectRole(
                    familyName);
            string roleKey = BuildRoleKey(
                familyName,
                role);
            if (!familyByName.TryGetValue(
                    roleKey,
                    out TileFamily family))
            {
                family = new TileFamily(
                    familyName,
                    role);
                familyByName.Add(roleKey, family);
            }

            family.Variants.Add(prefab);
        }

        List<TileFamily> families = familyByName.Values
            .OrderBy(family => family.Role)
            .ThenBy(family => family.FamilyName, StringComparer.Ordinal)
            .ToList();
        for (int i = 0; i < families.Count; i++)
        {
            TileFamily family = families[i];
            family.Variants.Sort((left, right) =>
            {
                string leftName =
                    RemoveProjectTilePrefix(left.name);
                string rightName =
                    RemoveProjectTilePrefix(right.name);
                if (leftName == family.FamilyName)
                    return rightName == family.FamilyName ? 0 : -1;
                if (rightName == family.FamilyName)
                    return 1;
                return string.CompareOrdinal(leftName, rightName);
            });
        }

        return families;
    }

    private static void ConfigureTileSet(
        TileSet target,
        TileSet source,
        IEnumerable<TileFamily> families,
        IReadOnlyDictionary<
            string,
            DungeonTileFamilyWeightState> stateByFamily)
    {
        Dictionary<GameObject, GameObjectChance> previousWeights =
            target.TileWeights.Weights
                .Where(weight => weight?.Value != null)
                .GroupBy(weight => weight.Value)
                .ToDictionary(
                    group => group.Key,
                    group => group.First());
        List<TileFamily> orderedFamilies = families
            .OrderBy(family => family.FamilyName, StringComparer.Ordinal)
            .ToList();

        target.TileWeights.Weights.Clear();
        for (int familyIndex = 0;
             familyIndex < orderedFamilies.Count;
             familyIndex++)
        {
            TileFamily family = orderedFamilies[familyIndex];
            string roleKey = BuildRoleKey(
                family.FamilyName,
                family.Role);
            Require(
                stateByFamily.TryGetValue(
                    roleKey,
                    out DungeonTileFamilyWeightState state),
                "타일 계열 가중치 누락: " + family.FamilyName);
            Require(
                state.VariantCount == family.Variants.Count,
                family.FamilyName
                + ": 변형 수가 바뀌었습니다. 목록을 새로고침하세요.");

            float familyMainWeight =
                SanitizeWeight(state.MainPathWeight);
            float familyBranchWeight =
                SanitizeWeight(state.BranchPathWeight);
            float variantDivisor = family.Variants.Count;
            // 계열 합산 확률을 유지한 채 현재 변형 수로 균등 분배한다.
            AnimationCurve familyDepthCurve =
                ResolveFamilyDepthCurve(
                    family,
                    source,
                    previousWeights);

            for (int variantIndex = 0;
                 variantIndex < family.Variants.Count;
                 variantIndex++)
            {
                GameObject variant = family.Variants[variantIndex];
                target.TileWeights.Weights.Add(
                    new GameObjectChance(
                        variant,
                        familyMainWeight / variantDivisor,
                        familyBranchWeight / variantDivisor,
                        target)
                    {
                        DepthWeightScale =
                            CloneCurve(familyDepthCurve)
                    });
            }
        }

        EditorUtility.SetDirty(target);
    }

    private static void ValidateTileSet(
        TileSet tileSet,
        IReadOnlyList<TileFamily> families)
    {
        List<GameObject> expected = families
            .SelectMany(family => family.Variants)
            .ToList();
        List<GameObjectChance> actual = tileSet.TileWeights.Weights;
        Require(
            actual.Count == expected.Count,
            $"{tileSet.name}: 후보 수 불일치 "
            + $"{actual.Count}/{expected.Count}");
        Require(
            actual.All(weight =>
                weight?.Value != null
                && expected.Contains(weight.Value)),
            tileSet.name + ": 등록되지 않은 Project 타일 참조");
        Require(
            actual.Select(weight => weight.Value)
                .Distinct()
                .Count() == expected.Count,
            tileSet.name + ": 중복 타일 참조");

        Dictionary<GameObject, GameObjectChance> actualByPrefab =
            actual.ToDictionary(weight => weight.Value);
        for (int familyIndex = 0;
             familyIndex < families.Count;
             familyIndex++)
        {
            TileFamily family = families[familyIndex];
            GameObjectChance first =
                actualByPrefab[family.Variants[0]];
            Require(
                first.MainPathWeight >= 0f
                && first.BranchPathWeight >= 0f,
                family.FamilyName + ": 음수 가중치");

            for (int variantIndex = 1;
                 variantIndex < family.Variants.Count;
                 variantIndex++)
            {
                GameObjectChance current =
                    actualByPrefab[family.Variants[variantIndex]];
                Require(
                    Mathf.Approximately(
                        first.MainPathWeight,
                        current.MainPathWeight)
                    && Mathf.Approximately(
                        first.BranchPathWeight,
                        current.BranchPathWeight),
                    family.FamilyName
                    + ": 변형별 가중치가 균등하지 않습니다.");
                Require(
                    AreCurvesEquivalent(
                        first.DepthWeightScale,
                        current.DepthWeightScale),
                    family.FamilyName
                    + ": 변형별 깊이 가중치 곡선이 다릅니다.");
            }
        }
    }

    private static AnimationCurve ResolveFamilyDepthCurve(
        TileFamily family,
        TileSet source,
        IReadOnlyDictionary<GameObject, GameObjectChance>
            previousWeights)
    {
        GameObject basePrefab = family.Variants.FirstOrDefault(
            prefab =>
                RemoveProjectTilePrefix(prefab.name)
                == family.FamilyName);
        if (basePrefab != null
            && previousWeights.TryGetValue(
                basePrefab,
                out GameObjectChance previous))
        {
            return previous.DepthWeightScale;
        }

        GameObjectChance sourceTemplate =
            FindSourceTemplate(source, family);
        return sourceTemplate?.DepthWeightScale
            ?? AnimationCurve.Linear(0f, 1f, 1f, 1f);
    }

    private static GameObjectChance FindSourceTemplate(
        TileSet source,
        TileFamily family)
    {
        HashSet<GameObject> linkedSources = family.Variants
            .Select(prefab =>
                prefab.GetComponent<
                    DungeonTileAuthoringMarker>()?.SourcePrefab)
            .Where(prefab => prefab != null)
            .ToHashSet();
        GameObjectChance linkedTemplate =
            source.TileWeights.Weights
            .FirstOrDefault(weight =>
                weight?.Value != null
                && linkedSources.Contains(weight.Value));
        if (linkedTemplate != null)
            return linkedTemplate;

        return source.TileWeights.Weights.FirstOrDefault(
            weight =>
                weight?.Value != null
                && string.Equals(
                    NormalizeTileFamilyName(
                        DungeonTileNamingCatalog
                            .FromSourceName(weight.Value.name)
                            .ProjectLogicalName),
                    family.FamilyName,
                    StringComparison.Ordinal));
    }

    private static TileSet ResolveTargetTileSet(
        DungeonTileRole role,
        TileSet regular,
        TileSet arenas,
        TileSet endCaps,
        TileSet starts,
        TileSet exits)
    {
        return role switch
        {
            DungeonTileRole.Bridge => regular,
            DungeonTileRole.Arena => arenas,
            DungeonTileRole.EndCap => endCaps,
            DungeonTileRole.Start => starts,
            DungeonTileRole.Exit => exits,
            _ => throw new ArgumentOutOfRangeException(
                nameof(role),
                role,
                null)
        };
    }

    private static bool TryRemoveVariantSuffix(
        string value,
        out string familyName)
    {
        familyName = value;
        int separatorIndex = value.LastIndexOf('_');
        if (separatorIndex <= 0
            || separatorIndex >= value.Length - 1)
        {
            return false;
        }

        string suffix = value.Substring(separatorIndex + 1);
        for (int i = 0; i < suffix.Length; i++)
        {
            if (suffix[i] < 'A' || suffix[i] > 'Z')
                return false;
        }

        familyName = value.Substring(0, separatorIndex);
        return true;
    }

    private static string RemoveProjectTilePrefix(string value)
    {
        return value.StartsWith(
            ProjectTilePrefix,
            StringComparison.Ordinal)
            ? value.Substring(ProjectTilePrefix.Length)
            : value;
    }

    private static float SanitizeWeight(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return 0f;
        return Mathf.Max(0f, value);
    }

    private static AnimationCurve CloneCurve(AnimationCurve source)
    {
        if (source == null)
            return AnimationCurve.Linear(0f, 1f, 1f, 1f);

        return new AnimationCurve(source.keys)
        {
            preWrapMode = source.preWrapMode,
            postWrapMode = source.postWrapMode
        };
    }

    private static bool AreCurvesEquivalent(
        AnimationCurve left,
        AnimationCurve right)
    {
        if (left == null || right == null)
            return left == right;
        if (left.preWrapMode != right.preWrapMode
            || left.postWrapMode != right.postWrapMode
            || left.length != right.length)
        {
            return false;
        }

        Keyframe[] leftKeys = left.keys;
        Keyframe[] rightKeys = right.keys;
        for (int i = 0; i < leftKeys.Length; i++)
        {
            Keyframe a = leftKeys[i];
            Keyframe b = rightKeys[i];
            if (!Mathf.Approximately(a.time, b.time)
                || !Mathf.Approximately(a.value, b.value)
                || !Mathf.Approximately(a.inTangent, b.inTangent)
                || !Mathf.Approximately(a.outTangent, b.outTangent)
                || a.weightedMode != b.weightedMode
                || !Mathf.Approximately(a.inWeight, b.inWeight)
                || !Mathf.Approximately(a.outWeight, b.outWeight))
            {
                return false;
            }
        }

        return true;
    }

    private static string BuildRoleKey(
        string familyName,
        DungeonTileRole role)
    {
        return role + "|" + familyName;
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

    private sealed class TileFamily
    {
        public TileFamily(
            string familyName,
            DungeonTileRole role)
        {
            FamilyName = familyName;
            Role = role;
        }

        public string FamilyName { get; }
        public DungeonTileRole Role { get; }
        public List<GameObject> Variants { get; } = new();
    }
}
