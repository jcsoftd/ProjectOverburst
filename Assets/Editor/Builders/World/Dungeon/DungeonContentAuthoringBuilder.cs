using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DunGen;
using DunGen.Graph;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum DungeonTileRole
{
    Bridge,
    Arena,
    EndCap,
    Start,
    Exit
}

public readonly struct DungeonTileNameMapping
{
    public DungeonTileNameMapping(
        string sourceName,
        string projectLogicalName,
        DungeonTileRole role)
    {
        SourceName = sourceName;
        ProjectLogicalName = projectLogicalName;
        Role = role;
    }

    public string SourceName { get; }
    public string ProjectLogicalName { get; }
    public DungeonTileRole Role { get; }
}

public static class DungeonTileNamingCatalog
{
    private const string SourceRegularPrefix = "TD_Tile_";
    private const string SourceExitPrefix = "TD_Tile_Exit_";
    private const string ProjectPrefix = "PF_DungeonTile_";

    private static readonly int[] ArenaSourceNumbers =
    {
        8, 9, 21, 22, 23, 31, 32, 33
    };

    private const int EndCapSourceNumber = 24;

    private static readonly int[] BridgeSourceNumbers =
    {
        1, 2, 3, 4, 5, 6, 7, 10, 11, 12, 13, 14,
        15, 16, 17, 18, 19, 20, 25, 26, 27, 28, 29, 30
    };

    private static readonly int[] StartSourceNumbers = { 2, 3, 4 };
    private static readonly int[] ExitSourceNumbers = { 1, 5 };

    public static DungeonTileNameMapping FromSourceName(
        string sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
            throw new ArgumentException("공급사 타일 이름이 비어 있습니다.");

        if (sourceName.StartsWith(
                SourceExitPrefix,
                StringComparison.Ordinal))
        {
            string numberText =
                sourceName.Substring(SourceExitPrefix.Length);
            RequireNumber(sourceName, numberText, out int sourceNumber);

            int startIndex =
                Array.IndexOf(StartSourceNumbers, sourceNumber);
            if (startIndex >= 0)
            {
                return new DungeonTileNameMapping(
                    sourceName,
                    $"TD_Start_{startIndex + 1:D2}",
                    DungeonTileRole.Start);
            }

            int exitIndex =
                Array.IndexOf(ExitSourceNumbers, sourceNumber);
            if (exitIndex >= 0)
            {
                return new DungeonTileNameMapping(
                    sourceName,
                    $"TD_Exit_{exitIndex + 1:D2}",
                    DungeonTileRole.Exit);
            }

            throw new InvalidOperationException(
                "분류되지 않은 공급사 시작·출구 타일: "
                + sourceName);
        }

        if (!sourceName.StartsWith(
                SourceRegularPrefix,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "지원하지 않는 공급사 타일 이름: " + sourceName);
        }

        string remainder =
            sourceName.Substring(SourceRegularPrefix.Length);
        int suffixIndex = remainder.IndexOf('_');
        string numberPart = suffixIndex >= 0
            ? remainder.Substring(0, suffixIndex)
            : remainder;
        string variantSuffix = suffixIndex >= 0
            ? remainder.Substring(suffixIndex)
            : string.Empty;
        RequireNumber(sourceName, numberPart, out int regularNumber);

        if (regularNumber == EndCapSourceNumber)
        {
            return new DungeonTileNameMapping(
                sourceName,
                $"TD_EndCap_01{variantSuffix}",
                DungeonTileRole.EndCap);
        }

        int arenaIndex =
            Array.IndexOf(ArenaSourceNumbers, regularNumber);
        if (arenaIndex >= 0)
        {
            return new DungeonTileNameMapping(
                sourceName,
                $"TD_Arena_{arenaIndex + 1:D2}{variantSuffix}",
                DungeonTileRole.Arena);
        }

        int bridgeIndex =
            Array.IndexOf(BridgeSourceNumbers, regularNumber);
        if (bridgeIndex >= 0)
        {
            return new DungeonTileNameMapping(
                sourceName,
                $"TD_Bridge_{bridgeIndex + 1:D2}{variantSuffix}",
                DungeonTileRole.Bridge);
        }

        throw new InvalidOperationException(
            "분류되지 않은 공급사 일반 타일: " + sourceName);
    }

    public static DungeonTileRole GetProjectRole(string value)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (normalized.StartsWith(
                ProjectPrefix,
                StringComparison.Ordinal))
        {
            normalized = normalized.Substring(ProjectPrefix.Length);
        }

        if (normalized.StartsWith(
                "TD_Bridge_",
                StringComparison.Ordinal))
        {
            return DungeonTileRole.Bridge;
        }

        if (normalized.StartsWith(
                "TD_Arena_",
                StringComparison.Ordinal))
        {
            return DungeonTileRole.Arena;
        }

        if (normalized.StartsWith(
                "TD_EndCap_",
                StringComparison.Ordinal))
        {
            return DungeonTileRole.EndCap;
        }

        if (normalized.StartsWith(
                "TD_Start_",
                StringComparison.Ordinal))
        {
            return DungeonTileRole.Start;
        }

        if (normalized.StartsWith(
                "TD_Exit_",
                StringComparison.Ordinal))
        {
            return DungeonTileRole.Exit;
        }

        throw new InvalidOperationException(
            "Project 던전 타일 역할을 판별할 수 없습니다: "
            + value);
    }

    public static bool IsEndpoint(DungeonTileRole role)
    {
        return role == DungeonTileRole.Start
            || role == DungeonTileRole.Exit;
    }

    public static bool IsOptionalSourceVariant(string sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName)
            || sourceName.StartsWith(
                SourceExitPrefix,
                StringComparison.Ordinal)
            || !sourceName.StartsWith(
                SourceRegularPrefix,
                StringComparison.Ordinal))
        {
            return false;
        }

        string remainder =
            sourceName.Substring(SourceRegularPrefix.Length);
        return remainder.IndexOf('_') >= 0;
    }

    private static void RequireNumber(
        string sourceName,
        string numberText,
        out int number)
    {
        if (!int.TryParse(numberText, out number))
        {
            throw new InvalidOperationException(
                "공급사 타일 번호를 읽을 수 없습니다: "
                + sourceName);
        }
    }
}

public static class DungeonContentAuthoringBuilder
{
    public const string ContentRoot =
        "Assets/ProjectOverburst/04_Contents/02_Dungeon/Content/MultistoryDungeons2";
    public const string TileOutputRoot = ContentRoot + "/Tiles";
    public const string FlowOutputPath = ContentRoot + "/Flows/DF_MultistoryDungeon_Pilot.asset";
    public const string ArchetypeOutputPath =
        ContentRoot + "/Archetypes/DA_MultistoryDungeon_Pilot.asset";
    public const string RegularTileSetOutputPath =
        ContentRoot + "/TileSets/TS_MultistoryDungeon_Pilot.asset";
    public const string ArenaTileSetOutputPath =
        ContentRoot + "/TileSets/TS_MultistoryDungeon_Arenas_Pilot.asset";
    public const string EndCapTileSetOutputPath =
        ContentRoot + "/TileSets/TS_MultistoryDungeon_EndCaps_Pilot.asset";
    public const string StartTileSetOutputPath =
        ContentRoot + "/TileSets/TS_MultistoryDungeon_Starts_Pilot.asset";
    public const string ExitTileSetOutputPath =
        ContentRoot + "/TileSets/TS_MultistoryDungeon_Exits_Pilot.asset";
    private const string ObsoleteJunctionTileSetOutputPath =
        ContentRoot + "/TileSets/TS_MultistoryDungeon_Junctions_Pilot.asset";

    private const string SourceRoot =
        "Assets/ThirdParty/04_환경맵/Multistory Dungeons 2/DunGen Presets";
    public const string SourceTileRoot = SourceRoot + "/Top-Down Tiles";
    public const string SourceFlowPath = SourceRoot + "/Demo/TD Demo Dungeon Flow.asset";
    public const string SourceArchetypePath =
        SourceRoot + "/Demo/TD Demo Dungeon Archetype.asset";
    public const string SourceRegularTileSetPath =
        SourceRoot + "/Demo/TD Demo Tileset.asset";
    public const string SourceExitTileSetPath =
        SourceRoot + "/Demo/TD Demo Exits Tileset.asset";
    private const string GeneratedRootName = "__DungeonAuthoring";
    private const int AuthoringVersion = 14;
    private const string LogPath = "Logs/DungeonContentAuthoring.log";
    [MenuItem(
        "OVERBURST/Codex/Setup/World/Dungeon/"
        + "Build Standalone Project Tile Content")]
    public static void BuildFromMenu()
    {
        string report = BuildAndValidate();
        Debug.Log(report);
    }

    public static void RunFromCommandLine()
    {
        Directory.CreateDirectory("Logs");
        try
        {
            string report = BuildAndValidate();
            File.WriteAllText(LogPath, report);
            Debug.Log(report);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            string report = exception.ToString();
            File.WriteAllText(LogPath, report);
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static string BuildAndValidate()
    {
        EnsureOutputFolders();
        List<string> tileReports = new();
        IReadOnlyList<string> selectedTileNames =
            GetSelectedTileNames();
        MigrateLegacyProjectTileNames(selectedTileNames);

        for (int i = 0; i < selectedTileNames.Count; i++)
        {
            string tileName = selectedTileNames[i];
            DungeonTileNameMapping mapping =
                DungeonTileNamingCatalog.FromSourceName(tileName);
            bool isEndpointTile =
                DungeonTileNamingCatalog.IsEndpoint(mapping.Role);
            string sourcePath = GetSourceTilePath(tileName);
            string outputPath = GetRuntimeTilePath(tileName);
            GameObject sourcePrefab =
                LoadRequiredAsset<GameObject>(sourcePath);
            EnsureStandaloneProjectCopy(sourcePrefab, outputPath);
            tileReports.Add(
                CreateOrUpdateProjectTile(
                    sourcePath,
                    outputPath,
                    isEndpointTile));
        }

        ConfigureDataAssets();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        string validationReport = DungeonContentValidator.ValidateOrThrow();
        return "[DungeonContentAuthoringBuilder] 완료\n"
            + $"SourceTileCount={selectedTileNames.Count}\n"
            + string.Join("\n", tileReports)
            + "\n"
            + validationReport;
    }

    public static IReadOnlyList<string> GetSelectedTileNames()
    {
        return GetAllSourceTileNames()
            .Where(name =>
                !DungeonTileNamingCatalog
                    .IsOptionalSourceVariant(name))
            .ToArray();
    }

    public static IReadOnlyList<string> GetAllSourceTileNames()
    {
        return AssetDatabase.FindAssets(
                "t:Prefab",
                new[] { SourceTileRoot })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(path =>
                path.EndsWith(
                    ".prefab",
                    StringComparison.OrdinalIgnoreCase)
                && string.Equals(
                    Path.GetDirectoryName(path)
                        ?.Replace('\\', '/'),
                    SourceTileRoot,
                    StringComparison.Ordinal))
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    public static string GetRuntimeTilePath(string tileName)
    {
        string logicalName = tileName.StartsWith(
                "TD_Tile_",
                StringComparison.Ordinal)
            ? DungeonTileNamingCatalog
                .FromSourceName(tileName)
                .ProjectLogicalName
            : tileName;
        return $"{TileOutputRoot}/PF_DungeonTile_{logicalName}.prefab";
    }

    public static string GetSourceTilePath(string sourceTileName)
    {
        return $"{SourceTileRoot}/{sourceTileName}.prefab";
    }

    private static string CreateOrUpdateProjectTile(
        string sourcePath,
        string outputPath,
        bool isExitTile)
    {
        GameObject sourcePrefab = LoadRequiredAsset<GameObject>(sourcePath);
        GameObject existingPrefab =
            AssetDatabase.LoadAssetAtPath<GameObject>(outputPath);
        DungeonTileAuthoringMarker existingMarker =
            existingPrefab != null
                ? existingPrefab.GetComponent<DungeonTileAuthoringMarker>()
                : null;
        if (existingMarker != null
            && existingMarker.AuthoringVersion == AuthoringVersion
            && existingMarker.IsStandaloneProjectTile
            && existingMarker.SourcePrefab == sourcePrefab
            && existingMarker.IsExitTile == isExitTile)
        {
            return $"{existingPrefab.name}: AuthoringVersion={AuthoringVersion}, "
                + $"Ground={existingMarker.WalkableSurfaceCount}, "
                + $"Ramp={existingMarker.StairRampCount}, 변경 없음";
        }

        GameObject root = PrefabUtility.LoadPrefabContents(outputPath);
        try
        {
            ResetSourceColliderOverrides(root);
            RemoveGeneratedRoot(root.transform);

            GameObject generatedRoot = new(GeneratedRootName);
            generatedRoot.transform.SetParent(root.transform, false);
            GameObject rampRootObject = new("StairRamps");
            rampRootObject.transform.SetParent(generatedRoot.transform, false);
            GameObject anchorRootObject = new("Anchors");
            anchorRootObject.transform.SetParent(generatedRoot.transform, false);

            int groundLayer = LayerMask.NameToLayer("Ground");
            Require(groundLayer >= 0, "Ground 레이어를 찾지 못했습니다.");
            float minimumWalkableHeight =
                DungeonWalkableHeightUtility.ResolveMinimumHeight(root);
            int backgroundColliderCount =
                DisableBackgroundColliders(
                    root,
                    generatedRoot.transform,
                    minimumWalkableHeight);
            int walkableSurfaceCount = ApplyWalkableSurfaceLayers(
                root,
                generatedRoot.transform,
                groundLayer,
                minimumWalkableHeight);
            RestoreTileGenerationSettings(root, sourcePrefab);
            DungeonStairRampBuilder.BuildResult rampResult =
                DungeonStairRampBuilder.Rebuild(
                    root,
                    rampRootObject.transform,
                    groundLayer,
                    minimumWalkableHeight);
            CreatePrimaryAnchor(
                root,
                anchorRootObject.transform,
                isExitTile);
            DungeonRoomAnchor primaryAnchor =
                anchorRootObject.GetComponentInChildren<
                    DungeonRoomAnchor>(true);
            Require(primaryAnchor != null, "Primary Anchor 생성 실패");
            DungeonTileAuthoringMarker marker =
                root.GetComponent<DungeonTileAuthoringMarker>();
            if (marker == null)
                marker = root.AddComponent<DungeonTileAuthoringMarker>();
            marker.Configure(
                sourcePrefab,
                AuthoringVersion,
                isExitTile,
                walkableSurfaceCount,
                rampResult.RampCount,
                false,
                true);

            PrefabUtility.SaveAsPrefabAsset(root, outputPath, out bool saved);
            Require(saved, "프리팹 저장 실패: " + outputPath);

            return $"{root.name}: StandaloneProjectTile=True, "
                + $"Ground={walkableSurfaceCount}, "
                + $"StairCandidate={rampResult.CandidateCount}, "
                + $"Ramp={rampResult.RampCount}, "
                + $"BackgroundColliderDisabled={backgroundColliderCount}, "
                + $"Skipped={rampResult.Skipped.Count}";
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void EnsureStandaloneProjectCopy(
        GameObject sourcePrefab,
        string outputPath)
    {
        GameObject existing =
            AssetDatabase.LoadAssetAtPath<GameObject>(outputPath);
        if (existing != null
            && PrefabUtility.GetPrefabAssetType(existing)
                == PrefabAssetType.Regular)
        {
            return;
        }

        Scene previewScene = EditorSceneManager.NewPreviewScene();
        GameObject instance = null;
        try
        {
            instance = PrefabUtility.InstantiatePrefab(sourcePrefab, previewScene) as GameObject;
            Require(instance != null, "원본 타일 인스턴스 생성 실패: " + sourcePrefab.name);
            PrefabUtility.UnpackPrefabInstance(
                instance,
                PrefabUnpackMode.OutermostRoot,
                InteractionMode.AutomatedAction);
            instance.name = Path.GetFileNameWithoutExtension(outputPath);
            PrefabUtility.SaveAsPrefabAsset(instance, outputPath, out bool saved);
            Require(saved, "Project 독립 타일 생성 실패: " + outputPath);
            GameObject standalone =
                AssetDatabase.LoadAssetAtPath<GameObject>(outputPath);
            Require(
                standalone != null
                && PrefabUtility.GetPrefabAssetType(standalone)
                    == PrefabAssetType.Regular,
                "Project 타일이 독립 Prefab이 아님: " + outputPath);
        }
        finally
        {
            if (instance != null)
                UnityEngine.Object.DestroyImmediate(instance);
            EditorSceneManager.ClosePreviewScene(previewScene);
        }
    }

    private static void MigrateLegacyProjectTileNames(
        IReadOnlyList<string> sourceTileNames)
    {
        for (int i = 0; i < sourceTileNames.Count; i++)
        {
            string sourceName = sourceTileNames[i];
            string legacyPath =
                $"{TileOutputRoot}/PF_DungeonTile_{sourceName}.prefab";
            string targetPath = GetRuntimeTilePath(sourceName);
            if (string.Equals(
                    legacyPath,
                    targetPath,
                    StringComparison.Ordinal))
            {
                continue;
            }

            GameObject legacy =
                AssetDatabase.LoadAssetAtPath<GameObject>(legacyPath);
            GameObject target =
                AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
            if (legacy != null && target != null)
            {
                throw new InvalidOperationException(
                    "기존 이름과 새 이름의 타일이 동시에 존재합니다: "
                    + legacyPath + " / " + targetPath);
            }

            if (legacy != null)
            {
                string moveError =
                    AssetDatabase.MoveAsset(legacyPath, targetPath);
                Require(
                    string.IsNullOrEmpty(moveError),
                    "Project 타일 이름 변경 실패: "
                    + legacyPath + " -> " + targetPath
                    + " | " + moveError);
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(targetPath)
                != null)
            {
                NormalizeProjectPrefabIdentity(
                    targetPath,
                    Path.GetFileNameWithoutExtension(legacyPath));
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(
            ImportAssetOptions.ForceSynchronousImport);
    }

    internal static void NormalizeProjectPrefabIdentity(
        string prefabPath,
        string legacyRootName)
    {
        GameObject root =
            PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            string previousRootName = root.name;
            string expectedRootName =
                Path.GetFileNameWithoutExtension(prefabPath);
            bool changed = !string.Equals(
                previousRootName,
                expectedRootName,
                StringComparison.Ordinal);
            root.name = expectedRootName;

            Tile projectTile = root.GetComponent<Tile>();
            if (DungeonTileNamingCatalog.GetProjectRole(expectedRootName)
                    == DungeonTileRole.EndCap
                && projectTile != null
                && !projectTile.AllowRotation)
            {
                // 출입구가 하나뿐인 EndCap은 어느 방향의 분기 끝에도 붙어야 한다.
                projectTile.AllowRotation = true;
                EditorUtility.SetDirty(projectTile);
                changed = true;
            }

            string[] replaceableRootNames =
            {
                previousRootName,
                legacyRootName
            };
            DungeonStairRampProxy[] ramps =
                root.GetComponentsInChildren<
                    DungeonStairRampProxy>(true);
            for (int i = 0; i < ramps.Length; i++)
            {
                DungeonStairRampProxy ramp = ramps[i];
                string path = ramp.SourceHierarchyPath;
                if (string.IsNullOrEmpty(path))
                    continue;

                for (int rootIndex = 0;
                     rootIndex < replaceableRootNames.Length;
                     rootIndex++)
                {
                    string replaceableRoot =
                        replaceableRootNames[rootIndex];
                    if (string.IsNullOrEmpty(replaceableRoot)
                        || string.Equals(
                            replaceableRoot,
                            expectedRootName,
                            StringComparison.Ordinal)
                        || !path.StartsWith(
                            replaceableRoot + "/",
                            StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ramp.Configure(
                        expectedRootName
                        + path.Substring(replaceableRoot.Length),
                        ramp.SlopeAngle);
                    EditorUtility.SetDirty(ramp);
                    changed = true;
                    break;
                }
            }

            if (!changed)
                return;

            PrefabUtility.SaveAsPrefabAsset(
                root,
                prefabPath,
                out bool saved);
            Require(
                saved,
                "Project 타일 루트 이름 저장 실패: "
                + prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ResetSourceColliderOverrides(GameObject root)
    {
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || IsGeneratedObject(collider.transform))
                continue;

            Collider sourceCollider =
                PrefabUtility.GetCorrespondingObjectFromOriginalSource(collider);
            GameObject sourceObject =
                PrefabUtility.GetCorrespondingObjectFromOriginalSource(collider.gameObject);
            if (sourceCollider != null)
                collider.enabled = sourceCollider.enabled;
            if (sourceObject != null)
                collider.gameObject.layer = sourceObject.layer;
        }
    }

    private static int DisableBackgroundColliders(
        GameObject root,
        Transform generatedRoot,
        float minimumWalkableHeight)
    {
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        int disabledCount = 0;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null
                || collider.transform.IsChildOf(generatedRoot)
                || !collider.enabled
                || collider.bounds.max.y >= minimumWalkableHeight)
            {
                continue;
            }

            collider.enabled = false;
            EditorUtility.SetDirty(collider);
            disabledCount++;
        }

        return disabledCount;
    }

    private static int ApplyWalkableSurfaceLayers(
        GameObject root,
        Transform generatedRoot,
        int groundLayer,
        float minimumWalkableHeight)
    {
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        HashSet<GameObject> walkableObjects = new();

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null
                || collider.transform.IsChildOf(generatedRoot)
                || !IsWalkableSurface(collider)
                || collider.bounds.max.y
                    < minimumWalkableHeight)
            {
                continue;
            }

            collider.gameObject.layer = groundLayer;
            walkableObjects.Add(collider.gameObject);
            EditorUtility.SetDirty(collider.gameObject);
        }

        return walkableObjects.Count;
    }

    private static void RestoreTileGenerationSettings(
        GameObject root,
        GameObject sourcePrefab)
    {
        Tile[] tiles = root.GetComponentsInChildren<Tile>(true);
        Require(tiles.Length == 1, $"{root.name}: DunGen Tile 수={tiles.Length}");
        Tile sourceTile = sourcePrefab.GetComponent<Tile>();
        Require(sourceTile != null,
            sourcePrefab.name + ": 원본 DunGen Tile 누락");
        tiles[0].AllowRotation = sourceTile.AllowRotation;
        tiles[0].RepeatMode = sourceTile.RepeatMode;
        tiles[0].OverrideAutomaticTileBounds =
            sourceTile.OverrideAutomaticTileBounds;
        tiles[0].TileBoundsOverride = sourceTile.TileBoundsOverride;
        tiles[0].OverrideConnectionChance =
            sourceTile.OverrideConnectionChance;
        tiles[0].ConnectionChance = sourceTile.ConnectionChance;
        EditorUtility.SetDirty(tiles[0]);
    }

    private static bool IsWalkableSurface(Collider collider)
    {
        if (!collider.enabled
            || collider.isTrigger
            || !collider.gameObject.activeInHierarchy
            || collider is not BoxCollider && collider is not MeshCollider)
        {
            return false;
        }

        string identity = collider.name;
        if (collider is MeshCollider meshCollider && meshCollider.sharedMesh != null)
            identity += " " + meshCollider.sharedMesh.name;

        if (!ContainsWalkableIdentity(identity) || ContainsExcludedIdentity(identity))
            return false;

        Bounds bounds = collider.bounds;
        if (bounds.size.x < 0.35f
            || bounds.size.z < 0.35f)
        {
            return false;
        }

        if (collider is BoxCollider)
        {
            bool isFloorBase =
                identity.Contains("base_", StringComparison.OrdinalIgnoreCase);
            float maximumHeight = isFloorBase ? 3.5f : 1.5f;
            if (bounds.size.y > maximumHeight)
                return false;

            Vector3 up = collider.transform.TransformDirection(Vector3.up).normalized;
            return Vector3.Dot(up, Vector3.up) >= 0.65f;
        }

        bool isWalkableBaseStructure =
            identity.Contains(
                "base_arch",
                StringComparison.OrdinalIgnoreCase)
            || identity.Contains(
                "base_doorway",
                StringComparison.OrdinalIgnoreCase);
        if (!isWalkableBaseStructure && bounds.size.y > 1.5f)
            return false;

        Vector3 origin = bounds.center + Vector3.up * (bounds.extents.y + 1f);
        if (collider.Raycast(new Ray(origin, Vector3.down), out RaycastHit hit, 10f))
            return Vector3.Dot(hit.normal, Vector3.up) >= 0.45f;

        return bounds.size.y <= 0.75f;
    }

    private static bool ContainsWalkableIdentity(string value)
    {
        return value.Contains("floor", StringComparison.OrdinalIgnoreCase)
            || value.Contains("base_", StringComparison.OrdinalIgnoreCase)
            || value.Contains("platform", StringComparison.OrdinalIgnoreCase)
            || value.Contains("ground", StringComparison.OrdinalIgnoreCase)
            || value.Contains("walkway", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsExcludedIdentity(string value)
    {
        if (value.Contains(
                "base_arch",
                StringComparison.OrdinalIgnoreCase)
            || value.Contains(
                "base_doorway",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] excluded =
        {
            "stair",
            "step",
            "wall",
            "arch",
            "door",
            "column",
            "pillar",
            "railing",
            "roof",
            "ceiling",
            "window",
            "fence",
            "candle",
            "decor"
        };

        for (int i = 0; i < excluded.Length; i++)
        {
            if (value.Contains(excluded[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void CreatePrimaryAnchor(
        GameObject root,
        Transform anchorRoot,
        bool isExitTile)
    {
        Doorway[] doorways = root.GetComponentsInChildren<Doorway>(true);
        Require(doorways.Length > 0, $"{root.name}: Doorway 누락");
        Bounds contentBounds = CalculateContentBounds(root, anchorRoot);
        Doorway referenceDoorway = doorways
            .Where(doorway => doorway != null)
            .OrderByDescending(doorway =>
                doorway.transform.position.y)
            .ThenBy(doorway =>
                GetHierarchyPath(doorway.transform),
                StringComparer.Ordinal)
            .First();
        Vector3 doorwayPosition =
            referenceDoorway.transform.position;
        Vector3 forward = contentBounds.center - doorwayPosition;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.0001f)
            forward = -referenceDoorway.transform.forward;
        if (forward.sqrMagnitude <= 0.0001f)
            forward = root.transform.forward;
        forward.Normalize();
        Vector3 position = doorwayPosition + forward * 2f;
        position.y = doorwayPosition.y + 0.08f;

        GameObject anchor = new("Anchor_Primary");
        anchor.transform.SetParent(anchorRoot, true);
        anchor.transform.SetPositionAndRotation(
            position,
            Quaternion.LookRotation(forward.normalized, Vector3.up));
        DungeonRoomAnchor component = anchor.AddComponent<DungeonRoomAnchor>();
        DungeonRoomAnchorRole roles = DungeonRoomAnchorRole.Spawn;
        if (isExitTile)
            roles |= DungeonRoomAnchorRole.Start | DungeonRoomAnchorRole.Exit;
        component.Configure(roles, "Primary");
    }

    private static Bounds CalculateContentBounds(GameObject root, Transform ignoredRoot)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool found = false;
        Bounds bounds = new(root.transform.position, Vector3.one);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer.transform.IsChildOf(ignoredRoot))
                continue;

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (found)
            return bounds;

        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || collider.transform.IsChildOf(ignoredRoot))
                continue;

            if (!found)
            {
                bounds = collider.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return bounds;
    }

    private static void ConfigureDataAssets()
    {
        CopyAssetIfMissing(SourceRegularTileSetPath, RegularTileSetOutputPath);
        CopyAssetIfMissing(SourceRegularTileSetPath, ArenaTileSetOutputPath);
        CopyAssetIfMissing(SourceRegularTileSetPath, EndCapTileSetOutputPath);
        CopyAssetIfMissing(SourceExitTileSetPath, StartTileSetOutputPath);
        CopyAssetIfMissing(SourceExitTileSetPath, ExitTileSetOutputPath);
        CopyAssetIfMissing(SourceArchetypePath, ArchetypeOutputPath);
        CopyAssetIfMissing(SourceFlowPath, FlowOutputPath);
        DeleteGeneratedAssetIfPresent(ObsoleteJunctionTileSetOutputPath);

        TileSet projectRegular =
            LoadRequiredAsset<TileSet>(RegularTileSetOutputPath);
        LoadRequiredAsset<TileSet>(ArenaTileSetOutputPath);
        LoadRequiredAsset<TileSet>(EndCapTileSetOutputPath);
        TileSet projectStart =
            LoadRequiredAsset<TileSet>(StartTileSetOutputPath);
        TileSet projectExit =
            LoadRequiredAsset<TileSet>(ExitTileSetOutputPath);
        DungeonTileFamilyWeightUtility
            .SynchronizePreservingFamilyWeights(false);

        DungeonArchetype sourceArchetype =
            LoadRequiredAsset<DungeonArchetype>(SourceArchetypePath);
        DungeonArchetype archetype =
            LoadRequiredAsset<DungeonArchetype>(ArchetypeOutputPath);
        archetype.TileSets = new List<TileSet> { projectRegular };
        archetype.BranchCapTileSets = new List<TileSet>();
        archetype.BranchCapType = sourceArchetype.BranchCapType;
        archetype.BranchingDepth = new IntRange(
            sourceArchetype.BranchingDepth.Min,
            sourceArchetype.BranchingDepth.Max);
        archetype.BranchCount = new IntRange(
            sourceArchetype.BranchCount.Min,
            sourceArchetype.BranchCount.Max);
        archetype.StraightenChance =
            sourceArchetype.StraightenChance;
        archetype.Unique = sourceArchetype.Unique;
        EditorUtility.SetDirty(archetype);

        DungeonFlow sourceFlow =
            LoadRequiredAsset<DungeonFlow>(SourceFlowPath);
        DungeonFlow flow = LoadRequiredAsset<DungeonFlow>(FlowOutputPath);
        flow.Length = new IntRange(
            sourceFlow.Length.Min,
            sourceFlow.Length.Max);
        flow.BranchMode = sourceFlow.BranchMode;
        flow.BranchCount = new IntRange(
            sourceFlow.BranchCount.Min,
            sourceFlow.BranchCount.Max);
        flow.DoorwayConnectionChance =
            sourceFlow.DoorwayConnectionChance;
        flow.RestrictConnectionToSameSection =
            sourceFlow.RestrictConnectionToSameSection;
        flow.TileInjectionRules =
            new List<TileInjectionRule>(
                sourceFlow.TileInjectionRules);

        for (int i = 0; i < flow.Nodes.Count; i++)
        {
            GraphNode node = flow.Nodes[i];
            if (node.NodeType == NodeType.Start)
                node.TileSets = new List<TileSet> { projectStart };
            else if (node.NodeType == NodeType.Goal)
                node.TileSets = new List<TileSet> { projectExit };
        }

        for (int i = 0; i < flow.Lines.Count; i++)
            flow.Lines[i].DungeonArchetypes = new List<DungeonArchetype> { archetype };

        EditorUtility.SetDirty(flow);
    }

    private static void CopyAssetIfMissing(string sourcePath, string outputPath)
    {
        if (AssetDatabase.LoadMainAssetAtPath(outputPath) != null)
            return;

        Require(AssetDatabase.CopyAsset(sourcePath, outputPath),
            $"에셋 복제 실패: {sourcePath} -> {outputPath}");
        AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceSynchronousImport);
    }

    private static void DeleteGeneratedAssetIfPresent(string assetPath)
    {
        if (AssetDatabase.LoadMainAssetAtPath(assetPath) == null)
            return;

        Require(
            AssetDatabase.DeleteAsset(assetPath),
            "Obsolete generated asset deletion failed: " + assetPath);
    }

    private static T LoadRequiredAsset<T>(string path) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        Require(asset != null, $"필수 에셋 누락: {path}");
        return asset;
    }

    private static void EnsureOutputFolders()
    {
        string[] folders =
        {
            "Assets/ProjectOverburst/04_Contents/02_Dungeon",
            "Assets/ProjectOverburst/04_Contents/02_Dungeon/Runtime",
            "Assets/ProjectOverburst/04_Contents/02_Dungeon/Runtime/Flow",
            "Assets/ProjectOverburst/04_Contents/02_Dungeon/Runtime/Generation",
            "Assets/ProjectOverburst/04_Contents/02_Dungeon/Runtime/Authoring",
            "Assets/ProjectOverburst/04_Contents/02_Dungeon/Runtime/Visibility",
            "Assets/ProjectOverburst/04_Contents/02_Dungeon/Data",
            "Assets/ProjectOverburst/04_Contents/02_Dungeon/Data/Definitions",
            "Assets/ProjectOverburst/04_Contents/02_Dungeon/Content",
            ContentRoot,
            ContentRoot + "/Flows",
            ContentRoot + "/Archetypes",
            ContentRoot + "/TileSets",
            TileOutputRoot,
            "Assets/ProjectOverburst/04_Contents/02_Dungeon/Prefabs",
            "Assets/ProjectOverburst/04_Contents/02_Dungeon/Prefabs/Portals"
        };

        for (int i = 0; i < folders.Length; i++)
            EnsureFolder(folders[i]);
    }

    private static void NormalizeGeneratedFolderCasing()
    {
        const string incorrectTileFolder =
            "Assets/ProjectOverburst/04_Contents/02_Dungeon/Content/MultistoryDungeons2/TileS";
        const string temporaryTileFolder =
            "Assets/ProjectOverburst/04_Contents/02_Dungeon/Content/MultistoryDungeons2/Tiles__CaseFix";

        if (!AssetDatabase.IsValidFolder(incorrectTileFolder))
            return;

        string physicalFolder = Directory
            .GetDirectories(ContentRoot)
            .FirstOrDefault(path =>
                string.Equals(
                    Path.GetFileName(path),
                    "TileS",
                    StringComparison.Ordinal));
        if (string.IsNullOrEmpty(physicalFolder))
            return;

        Require(
            !AssetDatabase.IsValidFolder(temporaryTileFolder),
            "타일 폴더 대소문자 보정용 임시 경로가 이미 존재합니다.");
        string firstMoveError =
            AssetDatabase.MoveAsset(incorrectTileFolder, temporaryTileFolder);
        Require(
            string.IsNullOrEmpty(firstMoveError),
            "타일 폴더 1차 대소문자 보정 실패: " + firstMoveError);
        string secondMoveError =
            AssetDatabase.MoveAsset(temporaryTileFolder, TileOutputRoot);
        Require(
            string.IsNullOrEmpty(secondMoveError),
            "타일 폴더 2차 대소문자 보정 실패: " + secondMoveError);
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;

        string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
        string name = Path.GetFileName(folder);
        Require(!string.IsNullOrWhiteSpace(parent), "폴더 부모 경로 누락: " + folder);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    private static void RemoveGeneratedRoot(Transform root)
    {
        Transform generated = root.Find(GeneratedRootName);
        if (generated != null)
            UnityEngine.Object.DestroyImmediate(generated.gameObject);
    }

    private static bool IsGeneratedObject(Transform transform)
    {
        Transform current = transform;
        while (current != null)
        {
            if (current.name == GeneratedRootName)
                return true;
            current = current.parent;
        }

        return false;
    }

    private static string GetHierarchyPath(Transform transform)
    {
        List<string> names = new();
        Transform current = transform;
        while (current != null)
        {
            names.Add(current.name);
            current = current.parent;
        }

        names.Reverse();
        return string.Join("/", names);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
