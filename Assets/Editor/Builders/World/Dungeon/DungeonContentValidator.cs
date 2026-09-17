using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DunGen;
using DunGen.Graph;
using UnityEditor;
using UnityEngine;

public static class DungeonContentValidator
{
    private const string LogPath = "Logs/DungeonContentValidation.log";
    private const string StairAuditLogPath =
        "Logs/DungeonStairRampCandidateAudit.log";
    private const float MaximumRampAngle = 44.5f;
    private const int ExpectedSourceTileCount = 55;
    private const int ExpectedRequiredProjectTileCount = 38;
    private const int ExpectedRequiredStairTileCount = 14;
    private const int ExpectedRequiredRampCount = 23;

    [MenuItem(
        "OVERBURST/Codex/Validation/World/Dungeon/"
        + "Validate Project Tile Content")]
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
            string report = exception.ToString();
            File.WriteAllText(LogPath, report);
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    [MenuItem(
        "OVERBURST/Codex/Validation/World/Dungeon/Audit Full Stair Candidates")]
    public static void AuditStairCandidatesFromMenu()
    {
        Debug.Log(AuditStairCandidates());
    }

    public static void RunStairAuditFromCommandLine()
    {
        Directory.CreateDirectory("Logs");
        try
        {
            string report = AuditStairCandidates();
            File.WriteAllText(StairAuditLogPath, report);
            Debug.Log(report);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            File.WriteAllText(StairAuditLogPath, exception.ToString());
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    public static string AuditStairCandidates()
    {
        IReadOnlyList<string> selectedTiles =
            DungeonTileFamilyWeightUtility.GetProjectTileNames();
        StringBuilder report = new();
        report.AppendLine("[DungeonContentValidator] 전체 계단 후보 감사");
        int candidateCount = 0;
        int acceptedCount = 0;
        int skippedCount = 0;
        int namedTileCount = 0;

        for (int i = 0; i < selectedTiles.Count; i++)
        {
            string tileName = selectedTiles[i];
            string prefabPath =
                DungeonContentAuthoringBuilder.GetRuntimeTilePath(tileName);
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                DungeonStairRampBuilder.CandidateAudit audit =
                    DungeonStairRampBuilder.AuditCandidates(
                        root,
                        DungeonWalkableHeightUtility
                            .ResolveMinimumHeight(root));
                if (audit.CandidateCount > 0)
                    namedTileCount++;
                candidateCount += audit.CandidateCount;
                acceptedCount += audit.AcceptedPaths.Count;
                skippedCount += audit.SkippedDescriptions.Count;
                report.AppendLine(
                    $"{tileName}: Candidate={audit.CandidateCount}, "
                    + $"Accepted={audit.AcceptedPaths.Count}, "
                    + $"Skipped={audit.SkippedDescriptions.Count}");
                for (int acceptedIndex = 0;
                     acceptedIndex < audit.AcceptedPaths.Count;
                     acceptedIndex++)
                {
                    report.AppendLine(
                        "  ACCEPTED | "
                        + audit.AcceptedPaths[acceptedIndex]);
                }

                for (int skippedIndex = 0;
                     skippedIndex < audit.SkippedDescriptions.Count;
                     skippedIndex++)
                {
                    report.AppendLine(
                        "  SKIPPED | "
                        + audit.SkippedDescriptions[skippedIndex]);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        report.AppendLine($"NamedStairTileCount={namedTileCount}");
        report.AppendLine($"CandidateCount={candidateCount}");
        report.AppendLine($"AcceptedCount={acceptedCount}");
        report.AppendLine($"SkippedCount={skippedCount}");
        return report.ToString().TrimEnd();
    }

    public static void InspectPilotCollidersFromCommandLine()
    {
        Directory.CreateDirectory("Logs");
        const string sourcePath =
            "Assets/ThirdParty/04_환경맵/Multistory Dungeons 2/DunGen Presets/"
            + "Top-Down Tiles/TD_Tile_01.prefab";
        const string reportPath = "Logs/DungeonTileColliderInspection.log";
        GameObject root = PrefabUtility.LoadPrefabContents(sourcePath);
        try
        {
            StringBuilder report = new();
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            report.AppendLine("ColliderCount=" + colliders.Length);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                string meshName = collider is MeshCollider meshCollider
                    && meshCollider.sharedMesh != null
                        ? meshCollider.sharedMesh.name
                        : "-";
                Bounds bounds = collider.bounds;
                report.AppendLine(
                    $"{i:D3}|{collider.GetType().Name}|{GetHierarchyPath(collider.transform)}"
                    + $"|mesh={meshName}|enabled={collider.enabled}|trigger={collider.isTrigger}"
                    + $"|active={collider.gameObject.activeInHierarchy}"
                    + $"|size={bounds.size.x:F2},{bounds.size.y:F2},{bounds.size.z:F2}");
            }

            File.WriteAllText(reportPath, report.ToString());
            Debug.Log(report);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            File.WriteAllText(reportPath, exception.ToString());
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    public static string ValidateOrThrow()
    {
        int groundLayer = LayerMask.NameToLayer("Ground");
        Require(groundLayer >= 0, "Ground 레이어 누락");
        IReadOnlyList<string> allSourceTiles =
            DungeonContentAuthoringBuilder.GetAllSourceTileNames();
        Require(allSourceTiles.Count == ExpectedSourceTileCount,
            $"공급사 원본 타일 수 불일치: "
            + $"{allSourceTiles.Count}/{ExpectedSourceTileCount}");
        IReadOnlyList<string> requiredSourceTiles =
            DungeonContentAuthoringBuilder.GetSelectedTileNames();
        Require(
            requiredSourceTiles.Count
                == ExpectedRequiredProjectTileCount,
            "필수 Project 기준 타일 수 불일치: "
            + $"{requiredSourceTiles.Count}/"
            + ExpectedRequiredProjectTileCount);
        IReadOnlyList<string> selectedTiles =
            DungeonTileFamilyWeightUtility.GetProjectTileNames();
        HashSet<string> requiredProjectTileSet =
            requiredSourceTiles
                .Select(sourceName =>
                    DungeonTileNamingCatalog
                        .FromSourceName(sourceName)
                        .ProjectLogicalName)
                .ToHashSet(StringComparer.Ordinal);
        Require(
            requiredProjectTileSet.All(selectedTiles.Contains),
            "필수 Project 기준 38개 타일이 누락됐습니다.");
        Require(
            selectedTiles.Distinct(StringComparer.Ordinal).Count()
                == selectedTiles.Count,
            "전체 타일 목록에 중복 이름이 있습니다.");
        StringBuilder report = new();
        report.AppendLine("[DungeonContentValidator] PASS");

        int totalGroundColliderCount = 0;
        int totalRampCount = 0;
        int stairTileCount = 0;
        int totalRendererCount = 0;
        int totalColliderCount = 0;
        int totalLightCount = 0;
        int totalParticleSystemCount = 0;
        int sourceStairTileCount = 0;
        int sourceRampCount = 0;
        for (int i = 0; i < selectedTiles.Count; i++)
        {
            string sourceTileName = selectedTiles[i];
            string prefabPath =
                DungeonContentAuthoringBuilder.GetRuntimeTilePath(sourceTileName);
            GameObject prefab = LoadRequiredAsset<GameObject>(prefabPath);
            DungeonTileAuthoringMarker prefabMarker =
                prefab.GetComponent<DungeonTileAuthoringMarker>();
            Require(
                prefabMarker != null
                && prefabMarker.SourcePrefab != null,
                sourceTileName + ": 원본 연결 marker 누락");
            GameObject sourcePrefab = prefabMarker.SourcePrefab;
            Require(
                AssetDatabase.GetAssetPath(prefab).StartsWith(
                    DungeonContentAuthoringBuilder.TileOutputRoot,
                    StringComparison.Ordinal),
                "Project 독립 타일 경로 위반: " + prefabPath);
            Require(
                PrefabUtility.GetPrefabAssetType(prefab)
                    == PrefabAssetType.Regular,
                sourceTileName + ": Prefab Variant가 남아 있음");

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Tile[] tiles = root.GetComponentsInChildren<Tile>(true);
                Doorway[] doorways = root.GetComponentsInChildren<Doorway>(true);
                Require(tiles.Length == 1,
                    $"{sourceTileName}: DunGen Tile 수={tiles.Length}");
                Tile sourceTile = sourcePrefab.GetComponent<Tile>();
                Require(sourceTile != null,
                    sourceTileName + ": 원본 DunGen Tile 누락");
                bool rotationMatches =
                    DungeonTileNamingCatalog.GetProjectRole(prefab.name)
                        == DungeonTileRole.EndCap
                        ? tiles[0].AllowRotation
                        : tiles[0].AllowRotation == sourceTile.AllowRotation;
                Require(
                    tiles[0].RepeatMode == sourceTile.RepeatMode
                    && rotationMatches
                    && tiles[0].OverrideAutomaticTileBounds
                        == sourceTile.OverrideAutomaticTileBounds
                    && tiles[0].TileBoundsOverride
                        == sourceTile.TileBoundsOverride
                    && tiles[0].OverrideConnectionChance
                        == sourceTile.OverrideConnectionChance
                    && Mathf.Approximately(
                        tiles[0].ConnectionChance,
                        sourceTile.ConnectionChance),
                    sourceTileName + ": DunGen Tile 원본 설정 불일치");
                int sourceDoorwayCount =
                    sourcePrefab.GetComponentsInChildren<Doorway>(true).Length;
                Require(doorways.Length == sourceDoorwayCount,
                    $"{sourceTileName}: 원본 Doorway 수 불일치="
                    + $"{doorways.Length}/{sourceDoorwayCount}");

                int missingScriptCount =
                    GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(root);
                Require(missingScriptCount == 0,
                    $"{sourceTileName}: Missing Script 수={missingScriptCount}");

                DungeonTileAuthoringMarker marker =
                    root.GetComponent<DungeonTileAuthoringMarker>();
                Require(marker != null, sourceTileName + ": Authoring marker 누락");
                Require(
                    marker.IsStandaloneProjectTile
                    && !marker.IsDirectSourceTile
                    && marker.SourcePrefab == sourcePrefab,
                    sourceTileName + ": Project 독립 타일 marker 불일치");
                string sourcePath =
                    AssetDatabase.GetAssetPath(marker.SourcePrefab);
                Require(
                    sourcePath.StartsWith(
                        "Assets/ThirdParty/04_환경맵/Multistory Dungeons 2/",
                        StringComparison.Ordinal),
                    sourceTileName + ": 선정된 원본 타일 경로가 아님");

                Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
                for (int colliderIndex = 0;
                     colliderIndex < colliders.Length;
                     colliderIndex++)
                {
                    Collider collider = colliders[colliderIndex];
                    Require(
                        collider != null
                        && IsPositiveScale(collider.transform.lossyScale),
                        $"{sourceTileName}: Collider 음수 또는 0 스케일 "
                        + GetHierarchyPath(collider.transform));
                }

                int groundColliderCount = colliders.Count(
                    collider => collider != null
                        && collider.enabled
                        && !collider.isTrigger
                        && collider.gameObject.layer == groundLayer);
                float minimumWalkableHeight =
                    DungeonWalkableHeightUtility
                        .ResolveMinimumHeight(root);
                Require(groundColliderCount > 0,
                    sourceTileName + ": Ground Collider 누락");
                Require(marker.WalkableSurfaceCount > 0,
                    sourceTileName + ": Ground 보행면 분류 수 0");
                Require(
                    colliders
                        .Where(collider =>
                            collider != null
                            && collider.enabled
                            && !collider.isTrigger
                            && collider.gameObject.layer
                                == groundLayer)
                        .All(collider =>
                            collider.bounds.max.y
                                >= minimumWalkableHeight),
                    sourceTileName
                    + ": 하층 배경 Ground 분류 잔존");

                DungeonStairRampProxy[] ramps =
                    root.GetComponentsInChildren<DungeonStairRampProxy>(true);
                IReadOnlyList<string> stairCandidatePaths =
                    DungeonStairRampBuilder
                        .GetAcceptedCandidatePaths(
                            root,
                            minimumWalkableHeight);
                if (stairCandidatePaths.Count > 0)
                {
                    stairTileCount++;
                    if (requiredProjectTileSet.Contains(sourceTileName))
                        sourceStairTileCount++;
                }
                Require(
                    ramps.Length == stairCandidatePaths.Count,
                    $"{sourceTileName}: 계단 후보와 Ramp 수 불일치 "
                    + $"candidate={stairCandidatePaths.Count}, ramp={ramps.Length}");
                Require(ramps.Length == marker.StairRampCount,
                    $"{sourceTileName}: Ramp marker 수 불일치 "
                    + $"actual={ramps.Length}, recorded={marker.StairRampCount}");

                HashSet<string> rampSourcePaths = ramps
                    .Select(ramp => ramp.SourceHierarchyPath)
                    .ToHashSet(StringComparer.Ordinal);
                for (int candidateIndex = 0;
                     candidateIndex < stairCandidatePaths.Count;
                     candidateIndex++)
                {
                    string candidatePath = stairCandidatePaths[candidateIndex];
                    Require(
                        rampSourcePaths.Contains(candidatePath),
                        $"{sourceTileName}: 계단 Ramp 원본 대응 누락={candidatePath}");
                    Collider sourceCollider = colliders.FirstOrDefault(
                        collider => collider != null
                            && GetHierarchyPath(collider.transform)
                                == candidatePath);
                    Require(
                        sourceCollider != null && !sourceCollider.enabled,
                        $"{sourceTileName}: 원본 계단 Collider 비활성화 누락="
                        + candidatePath);
                }

                for (int rampIndex = 0; rampIndex < ramps.Length; rampIndex++)
                    ValidateRamp(sourceTileName, ramps[rampIndex], groundLayer);

                DungeonRoomAnchor[] anchors =
                    root.GetComponentsInChildren<DungeonRoomAnchor>(true);
                Require(anchors.Length == 1,
                    $"{sourceTileName}: Primary Anchor 수={anchors.Length}");
                Require(anchors[0].Supports(DungeonRoomAnchorRole.Spawn),
                    sourceTileName + ": Spawn Anchor 역할 누락");
                float highestDoorwayY = root
                    .GetComponentsInChildren<Doorway>(true)
                    .Max(doorway =>
                        doorway.transform.position.y);
                Require(
                    Mathf.Abs(
                        anchors[0].transform.position.y
                        - (highestDoorwayY + 0.08f))
                    <= 0.01f,
                    sourceTileName
                    + ": Primary Anchor 상층 높이 불일치");

                bool isEndpoint =
                    DungeonTileNamingCatalog.IsEndpoint(
                        DungeonTileNamingCatalog.GetProjectRole(
                            sourceTileName));
                Require(marker.IsExitTile == isEndpoint,
                    sourceTileName + ": Exit marker 상태 불일치");
                if (isEndpoint)
                {
                    Require(anchors[0].Supports(DungeonRoomAnchorRole.Start),
                        sourceTileName + ": Start Anchor 역할 누락");
                    Require(anchors[0].Supports(DungeonRoomAnchorRole.Exit),
                        sourceTileName + ": Exit Anchor 역할 누락");
                }

                Require(
                    root.GetComponentsInChildren<
                        DungeonEventAreaAuthoring>(true).Length == 0,
                    sourceTileName
                    + ": 구형 EventArea Authoring 잔존");

                totalGroundColliderCount += groundColliderCount;
                totalRampCount += ramps.Length;
                if (requiredProjectTileSet.Contains(sourceTileName))
                    sourceRampCount += ramps.Length;
                Renderer[] renderers =
                    root.GetComponentsInChildren<Renderer>(true);
                Light[] lights = root.GetComponentsInChildren<Light>(true);
                ParticleSystem[] particleSystems =
                    root.GetComponentsInChildren<ParticleSystem>(true);
                totalRendererCount += renderers.Length;
                totalColliderCount += colliders.Length;
                totalLightCount += lights.Length;
                totalParticleSystemCount += particleSystems.Length;
                report.AppendLine(
                    $"{sourceTileName}: Doorways={doorways.Length}, "
                    + $"Ground={groundColliderCount}, Ramps={ramps.Length}, "
                    + $"Renderer={renderers.Length}, Collider={colliders.Length}, "
                    + $"Light={lights.Length}, Particle={particleSystems.Length}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        Require(
            sourceStairTileCount == ExpectedRequiredStairTileCount,
            $"필수 기준 계단 포함 타일 수 불일치: "
            + $"{sourceStairTileCount}/"
            + ExpectedRequiredStairTileCount);
        Require(
            sourceRampCount == ExpectedRequiredRampCount,
            $"필수 기준 계단 Ramp 수 불일치: "
            + $"{sourceRampCount}/"
            + ExpectedRequiredRampCount);
        ValidateDataAssets(selectedTiles);
        report.AppendLine($"TileCount={selectedTiles.Count}");
        report.AppendLine($"StairTileCount={stairTileCount}");
        report.AppendLine($"GroundColliderCount={totalGroundColliderCount}");
        report.AppendLine($"RampCount={totalRampCount}");
        report.AppendLine("TraversalTileEventAreaCount=0");
        report.AppendLine($"RendererCount={totalRendererCount}");
        report.AppendLine($"ColliderCount={totalColliderCount}");
        report.AppendLine($"LightCount={totalLightCount}");
        report.AppendLine($"ParticleSystemCount={totalParticleSystemCount}");
        report.AppendLine("MissingScriptCount=0");
        report.AppendLine(
            $"SupplierSourceTileCount={allSourceTiles.Count}");
        report.AppendLine(
            $"RequiredProjectTileCount={requiredSourceTiles.Count}");
        report.AppendLine(
            "OptionalVariantTileCount="
            + (selectedTiles.Count - requiredSourceTiles.Count));
        report.AppendLine(
            $"StandaloneProjectTileCount={selectedTiles.Count}");
        report.AppendLine("PrefabVariantCount=0");
        return report.ToString().TrimEnd();
    }

    private static void ValidateInheritedPresentationParity(
        string tileName,
        GameObject sourcePrefab,
        GameObject projectRoot)
    {
        Renderer[] sourceRenderers =
            sourcePrefab.GetComponentsInChildren<Renderer>(true);
        Renderer[] projectRenderers =
            projectRoot.GetComponentsInChildren<Renderer>(true);
        Light[] sourceLights =
            sourcePrefab.GetComponentsInChildren<Light>(true);
        Light[] projectLights =
            projectRoot.GetComponentsInChildren<Light>(true);
        ParticleSystem[] sourceParticles =
            sourcePrefab.GetComponentsInChildren<ParticleSystem>(true);
        ParticleSystem[] projectParticles =
            projectRoot.GetComponentsInChildren<ParticleSystem>(true);
        Collider[] sourceColliders =
            sourcePrefab.GetComponentsInChildren<Collider>(true);
        Collider[] inheritedProjectColliders = projectRoot
            .GetComponentsInChildren<Collider>(true)
            .Where(collider => !IsUnderGeneratedRoot(collider.transform))
            .ToArray();

        Require(sourceRenderers.Length == projectRenderers.Length,
            $"{tileName}: 원본 Renderer 수 불일치="
            + $"{projectRenderers.Length}/{sourceRenderers.Length}");
        Require(sourceLights.Length == projectLights.Length,
            $"{tileName}: 원본 Light 수 불일치="
            + $"{projectLights.Length}/{sourceLights.Length}");
        Require(sourceParticles.Length == projectParticles.Length,
            $"{tileName}: 원본 ParticleSystem 수 불일치="
            + $"{projectParticles.Length}/{sourceParticles.Length}");
        Require(sourceColliders.Length == inheritedProjectColliders.Length,
            $"{tileName}: 원본 Collider 수 불일치="
            + $"{inheritedProjectColliders.Length}/{sourceColliders.Length}");

        for (int i = 0; i < projectRenderers.Length; i++)
        {
            Renderer renderer = projectRenderers[i];
            Renderer sourceRenderer =
                PrefabUtility.GetCorrespondingObjectFromOriginalSource(
                    renderer);
            Require(sourceRenderer != null,
                tileName + ": 원본 연결 없는 Renderer="
                + GetHierarchyPath(renderer.transform));
            Material[] sourceMaterials = sourceRenderer.sharedMaterials;
            Material[] projectMaterials = renderer.sharedMaterials;
            Require(sourceMaterials.Length == projectMaterials.Length,
                tileName + ": Renderer 재질 슬롯 수 불일치="
                + GetHierarchyPath(renderer.transform));
            for (int materialIndex = 0;
                 materialIndex < sourceMaterials.Length;
                 materialIndex++)
            {
                Require(
                    sourceMaterials[materialIndex]
                        == projectMaterials[materialIndex],
                    tileName + ": Renderer 재질 원본 불일치="
                    + GetHierarchyPath(renderer.transform)
                    + $"[{materialIndex}]");
            }
        }

        for (int i = 0; i < projectLights.Length; i++)
        {
            Require(
                PrefabUtility.GetCorrespondingObjectFromOriginalSource(
                    projectLights[i]) != null,
                tileName + ": 원본 연결 없는 Light="
                + GetHierarchyPath(projectLights[i].transform));
        }

        for (int i = 0; i < projectParticles.Length; i++)
        {
            Require(
                PrefabUtility.GetCorrespondingObjectFromOriginalSource(
                    projectParticles[i]) != null,
                tileName + ": 원본 연결 없는 ParticleSystem="
                + GetHierarchyPath(projectParticles[i].transform));
        }

        for (int i = 0; i < inheritedProjectColliders.Length; i++)
        {
            Require(
                PrefabUtility.GetCorrespondingObjectFromOriginalSource(
                    inheritedProjectColliders[i]) != null,
                tileName + ": 원본 연결 없는 Collider="
                + GetHierarchyPath(
                    inheritedProjectColliders[i].transform));
        }
    }

    private static bool IsUnderGeneratedRoot(Transform transform)
    {
        Transform current = transform;
        while (current != null)
        {
            if (current.name == "__DungeonAuthoring")
                return true;
            current = current.parent;
        }

        return false;
    }

    private static void ValidateRamp(
        string tileName,
        DungeonStairRampProxy ramp,
        int groundLayer)
    {
        Require(ramp.gameObject.layer == groundLayer,
            tileName + ": Ramp Ground 레이어 누락");
        Require(ramp.SlopeAngle <= MaximumRampAngle + 0.01f,
            $"{tileName}: Ramp 경사 초과={ramp.SlopeAngle:F2}");
        Require(IsPositiveScale(ramp.transform.lossyScale),
            tileName + ": Ramp 음수 또는 0 스케일");
        BoxCollider box = ramp.GetComponent<BoxCollider>();
        Require(box != null && box.enabled && !box.isTrigger,
            tileName + ": Ramp BoxCollider 상태 오류");
        Require(box.size.x >= 0.65f && box.size.z > 0.5f,
            tileName + ": Ramp 크기 오류");
        Require(!string.IsNullOrWhiteSpace(ramp.SourceHierarchyPath),
            tileName + ": Ramp 원본 경로 누락");
    }

    private static void ValidateDataAssets(IReadOnlyList<string> selectedTiles)
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
        DungeonArchetype archetype = LoadRequiredAsset<DungeonArchetype>(
            DungeonContentAuthoringBuilder.ArchetypeOutputPath);
        DungeonFlow flow = LoadRequiredAsset<DungeonFlow>(
            DungeonContentAuthoringBuilder.FlowOutputPath);
        TileSet sourceRegular = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.SourceRegularTileSetPath);
        TileSet sourceExits = LoadRequiredAsset<TileSet>(
            DungeonContentAuthoringBuilder.SourceExitTileSetPath);
        DungeonArchetype sourceArchetype =
            LoadRequiredAsset<DungeonArchetype>(
                DungeonContentAuthoringBuilder.SourceArchetypePath);
        DungeonFlow sourceFlow = LoadRequiredAsset<DungeonFlow>(
            DungeonContentAuthoringBuilder.SourceFlowPath);

        int expectedStartCount = selectedTiles.Count(
            name =>
                DungeonTileNamingCatalog.GetProjectRole(name)
                == DungeonTileRole.Start);
        int expectedExitCount = selectedTiles.Count(
            name =>
                DungeonTileNamingCatalog.GetProjectRole(name)
                == DungeonTileRole.Exit);
        int expectedRegularCount = selectedTiles.Count(
            name =>
                DungeonTileNamingCatalog.GetProjectRole(name)
                == DungeonTileRole.Bridge);
        int expectedArenaCount = selectedTiles.Count(
            name =>
                DungeonTileNamingCatalog.GetProjectRole(name)
                == DungeonTileRole.Arena);
        int expectedEndCapCount = selectedTiles.Count(
            name =>
                DungeonTileNamingCatalog.GetProjectRole(name)
                == DungeonTileRole.EndCap);
        Require(regular.TileWeights.Weights.Count == expectedRegularCount,
            $"일반 TileSet 수 불일치: {regular.TileWeights.Weights.Count}");
        Require(arenas.TileWeights.Weights.Count == expectedArenaCount,
            $"Arena TileSet 수 불일치: {arenas.TileWeights.Weights.Count}");
        Require(endCaps.TileWeights.Weights.Count == expectedEndCapCount,
            $"EndCap TileSet 수 불일치: {endCaps.TileWeights.Weights.Count}");
        Require(starts.TileWeights.Weights.Count == expectedStartCount,
            $"시작 TileSet 수 불일치: {starts.TileWeights.Weights.Count}");
        Require(exits.TileWeights.Weights.Count == expectedExitCount,
            $"출구 TileSet 수 불일치: {exits.TileWeights.Weights.Count}");
        ValidateTileSetPaths(regular);
        ValidateTileSetPaths(arenas);
        ValidateTileSetPaths(endCaps);
        ValidateTileSetPaths(starts);
        ValidateTileSetPaths(exits);
        DungeonTileFamilyWeightUtility.ValidateOrThrow();
        ValidateTopologyRepairCatalog(
            regular,
            arenas,
            endCaps,
            starts,
            exits);
        Require(
            sourceRegular.LockPrefabs.Count
                == regular.LockPrefabs.Count,
            "일반 TileSet LockPrefab 원본 설정 불일치");
        Require(
            sourceRegular.LockPrefabs.Count
                == arenas.LockPrefabs.Count,
            "Arena TileSet LockPrefab 원본 설정 불일치");
        Require(
            sourceRegular.LockPrefabs.Count
                == endCaps.LockPrefabs.Count,
            "EndCap TileSet LockPrefab 원본 설정 불일치");
        Require(
            sourceExits.LockPrefabs.Count
                == starts.LockPrefabs.Count,
            "시작 TileSet LockPrefab 원본 설정 불일치");
        Require(
            sourceExits.LockPrefabs.Count
                == exits.LockPrefabs.Count,
            "출구 TileSet LockPrefab 원본 설정 불일치");

        Require(archetype.TileSets.Count == 1 && archetype.TileSets[0] == regular,
            "Archetype 일반 TileSet 연결 오류");
        Require(
            archetype.BranchCapTileSets.Count == 0
            && archetype.BranchCapType == sourceArchetype.BranchCapType,
            "Archetype BranchCap 비활성 계약 오류");
        Require(
            endCaps.TileWeights.Weights.All(weight =>
                weight?.Value != null
                && weight.Value
                    .GetComponentsInChildren<Doorway>(true)
                    .Length == 1
                && weight.Value.GetComponent<Tile>()?.AllowRotation == true
                && weight.BranchPathWeight > 0f),
            "EndCap은 출입구 1개, 회전 허용, 양수 Branch 가중치가 필요합니다.");
        Require(
            archetype.BranchCount.Min == sourceArchetype.BranchCount.Min
            && archetype.BranchCount.Max == sourceArchetype.BranchCount.Max,
            "Archetype BranchCount 원본 설정 불일치");
        Require(
            archetype.BranchingDepth.Min
                == sourceArchetype.BranchingDepth.Min
            && archetype.BranchingDepth.Max
                == sourceArchetype.BranchingDepth.Max,
            "Archetype BranchingDepth 원본 설정 불일치");
        Require(
            Mathf.Approximately(
                archetype.StraightenChance,
                sourceArchetype.StraightenChance)
            && archetype.Unique == sourceArchetype.Unique,
            "Archetype 기타 원본 설정 불일치");

        Require(
            flow.Length.Min == sourceFlow.Length.Min
            && flow.Length.Max == sourceFlow.Length.Max,
            "Flow Length 원본 설정 불일치");
        Require(flow.BranchMode == sourceFlow.BranchMode,
            "Flow BranchMode 원본 설정 불일치");
        Require(
            flow.BranchCount.Min == sourceFlow.BranchCount.Min
            && flow.BranchCount.Max == sourceFlow.BranchCount.Max,
            "Flow BranchCount 원본 설정 불일치");
        Require(
            Mathf.Approximately(
                flow.DoorwayConnectionChance,
                sourceFlow.DoorwayConnectionChance)
            && flow.RestrictConnectionToSameSection
                == sourceFlow.RestrictConnectionToSameSection,
            "Flow Doorway 연결 원본 설정 불일치");
        Require(
            Mathf.Approximately(flow.DoorwayConnectionChance, 1f)
            && flow.RestrictConnectionToSameSection,
            "최종 EndCap 패스 전 Doorway 연결 계약 오류");
        Require(
            flow.TileInjectionRules.Count
                == sourceFlow.TileInjectionRules.Count
            && flow.GlobalProps.Count == sourceFlow.GlobalProps.Count
            && flow.TileConnectionTags.Count
                == sourceFlow.TileConnectionTags.Count
            && flow.BranchPruneTags.Count
                == sourceFlow.BranchPruneTags.Count
            && flow.TileTagConnectionMode
                == sourceFlow.TileTagConnectionMode
            && flow.BranchTagPruneMode
                == sourceFlow.BranchTagPruneMode,
            "Flow 부가 규칙 원본 설정 불일치");
        Require(
            flow.BranchPruneTags.Count == 0,
            "최종 EndCap 패스 이후 Branch Prune 금지 계약 오류");
        Require(flow.Nodes.Count >= 2, "Flow Node 부족");
        Require(flow.Lines.Count >= 1, "Flow Line 부족");
        Require(
            flow.Nodes.Count == sourceFlow.Nodes.Count
            && flow.Lines.Count == sourceFlow.Lines.Count,
            "Flow Graph 구조 원본 설정 불일치");

        for (int i = 0; i < flow.Nodes.Count; i++)
        {
            GraphNode node = flow.Nodes[i];
            GraphNode sourceNode = sourceFlow.Nodes[i];
            Require(
                node.NodeType == sourceNode.NodeType
                && Mathf.Approximately(node.Position, sourceNode.Position)
                && node.Keys.Count == sourceNode.Keys.Count
                && node.Locks.Count == sourceNode.Locks.Count,
                $"Flow Node[{i}] 원본 구조 불일치");
            if (node.NodeType != NodeType.Start && node.NodeType != NodeType.Goal)
                continue;

            TileSet expectedNodeTileSet =
                node.NodeType == NodeType.Start
                    ? starts
                    : exits;
            Require(
                node.TileSets.Count == 1
                && node.TileSets[0] == expectedNodeTileSet,
                $"Flow {node.NodeType} 전용 TileSet 연결 오류");
        }

        for (int i = 0; i < flow.Lines.Count; i++)
        {
            GraphLine line = flow.Lines[i];
            GraphLine sourceLine = sourceFlow.Lines[i];
            Require(
                line.DungeonArchetypes.Count == 1
                && line.DungeonArchetypes[0] == archetype,
                $"Flow Line[{i}] Archetype 연결 오류");
            Require(
                Mathf.Approximately(line.Position, sourceLine.Position)
                && Mathf.Approximately(line.Length, sourceLine.Length)
                && line.Keys.Count == sourceLine.Keys.Count
                && line.Locks.Count == sourceLine.Locks.Count,
                $"Flow Line[{i}] 원본 구조 불일치");
        }
    }

    private static void ValidateTileSetPaths(TileSet tileSet)
    {
        for (int i = 0; i < tileSet.TileWeights.Weights.Count; i++)
        {
            GameObjectChance weight = tileSet.TileWeights.Weights[i];
            Require(weight != null && weight.Value != null,
                tileSet.name + ": null 타일 가중치");
            string path = AssetDatabase.GetAssetPath(weight.Value);
            Require(
                path.StartsWith(
                    DungeonContentAuthoringBuilder.TileOutputRoot + "/",
                    StringComparison.Ordinal),
                tileSet.name + ": Project 독립 타일 참조가 아님=" + path);
        }
    }

    private static void ValidateTopologyRepairCatalog(
        params TileSet[] activeTileSets)
    {
        DungeonTopologyRepairCatalog catalog =
            LoadRequiredAsset<DungeonTopologyRepairCatalog>(
                DungeonTopologyRepairAuthoringUtility.CatalogPath);
        Require(catalog.Rules.Count > 0, "형태 보정 규칙이 없습니다.");

        HashSet<GameObject> activePrefabs = activeTileSets
            .Where(tileSet => tileSet != null)
            .SelectMany(tileSet => tileSet.TileWeights.Weights)
            .Where(weight => weight?.Value != null)
            .Select(weight => weight.Value)
            .ToHashSet();
        HashSet<GameObject> candidates = new();
        for (int ruleIndex = 0;
             ruleIndex < catalog.Rules.Count;
             ruleIndex++)
        {
            DungeonTopologyRepairRule rule =
                catalog.Rules[ruleIndex];
            Require(
                rule?.SourceFamilyPrefab != null,
                $"형태 보정 Rule[{ruleIndex}] 원본 누락");
            Require(
                rule.SourceFamilyPrefab
                    .GetComponentsInChildren<Doorway>(true)
                    .Length >= 2,
                rule.SourceFamilyName
                + ": 형태 보정 원본 Doorway가 2개 미만입니다.");
            Require(
                activePrefabs.Contains(rule.SourceFamilyPrefab),
                rule.SourceFamilyName
                + ": 원본 계열 기본본이 활성 TileSet에 없습니다.");
            Require(
                rule.CandidateGroups.Count > 0,
                rule.SourceFamilyName + ": 형태 보정 후보 그룹 누락");

            for (int groupIndex = 0;
                 groupIndex < rule.CandidateGroups.Count;
                 groupIndex++)
            {
                DungeonTopologyRepairCandidateGroup group =
                    rule.CandidateGroups[groupIndex];
                bool validTopologyId =
                    group != null
                    && DungeonTopologyRepairId.TryParse(
                        group.TopologyId,
                        out _,
                        out _);
                Require(
                    validTopologyId
                    && group.Variants.Count > 0,
                    rule.SourceFamilyName
                    + $": 후보 그룹[{groupIndex}] 설정 오류");
                for (int variantIndex = 0;
                     variantIndex < group.Variants.Count;
                     variantIndex++)
                {
                    GameObject candidate =
                        group.Variants[variantIndex];
                    Require(
                        candidate != null
                        && candidates.Add(candidate),
                        rule.SourceFamilyName
                        + ": null 또는 중복 형태 보정 후보");
                    Require(
                        !activePrefabs.Contains(candidate),
                        candidate.name
                        + ": 형태 보정 후보가 일반 TileSet에 포함됨");
                    Require(
                        PrefabUtility.GetPrefabAssetType(candidate)
                            == PrefabAssetType.Regular
                        && candidate.GetComponent<Tile>() != null,
                        candidate.name
                        + ": 독립 Regular Prefab 또는 Tile 계약 오류");
                    DungeonTopologyRepairCandidateMarker marker =
                        candidate.GetComponent<
                            DungeonTopologyRepairCandidateMarker>();
                    Require(
                        marker != null
                        && marker.SourceFamilyPrefab
                            == rule.SourceFamilyPrefab
                        && marker.TopologyId == group.TopologyId,
                        candidate.name + ": 형태 보정 marker 불일치");
                    Require(
                        DungeonTileVisualFamilyName.Resolve(
                            candidate.name)
                        == rule.SourceFamilyName
                            + "_"
                            + group.TopologyId,
                        candidate.name
                        + ": 원본 번호 또는 형태 ID 이름 불일치");
                    Require(
                        GameObjectUtility
                            .GetMonoBehavioursWithMissingScriptCount(
                                candidate) == 0,
                        candidate.name + ": Missing Script");
                }
            }
        }

        Require(
            candidates.Count >= 2,
            "Bridge_21 L1·L2 형태 보정 후보가 부족합니다.");
    }

    private static string GetHierarchyPath(Transform transform)
    {
        string path = transform.name;
        Transform current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }

    private static bool IsPositiveScale(Vector3 scale)
    {
        return scale.x > 0f && scale.y > 0f && scale.z > 0f;
    }

    private static T LoadRequiredAsset<T>(string path) where T : UnityEngine.Object
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
