using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DunGen;
using DunGen.Graph;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DungeonTraversalRegressionValidator
{
    private const int FirstSeed = 27001; // 생성 회귀와 같은 요청 Seed 표본을 공유한다.
    private const int SeedCount = 20;
    private const int MaximumGenerationAttempts = 100;
    private const float MaximumRampAngle = 44.5f;
    private const float MaximumRampLandingDelta = 0.25f;
    private const float MaximumDoorwayPositionDelta = 0.05f;
    private const float MaximumDoorwayFloorDelta = 0.45f;
    private const float MaximumAnchorFloorDelta = 0.75f;
    private const float MinimumWalkableNormalY = 0.45f;
    private static readonly float[] DoorwayGroundSearchOffsets =
    {
        0f,
        0.25f,
        -0.25f,
        0.5f,
        -0.5f,
        0.75f,
        -0.75f,
        1f,
        -1f
    };
    private static readonly float[] RampLandingProbeOffsets =
    {
        0.08f,
        0.18f,
        0.32f
    };
    private static readonly float[] RampLandingLateralRatios =
    {
        0f,
        -0.5f,
        0.5f,
        -0.8f,
        0.8f,
        -0.95f,
        0.95f
    };
    private const string LogPath =
        "Logs/DungeonTraversalRegression.log";

    [MenuItem(
        "OVERBURST/Codex/Validation/World/Dungeon/Run 20 Traversal Seeds")]
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
        DungeonFlow flow = AssetDatabase.LoadAssetAtPath<DungeonFlow>(
            DungeonContentAuthoringBuilder.FlowOutputPath);
        Require(flow != null, "Project DungeonFlow is missing.");
        DungeonRunDefinition definition =
            AssetDatabase.LoadAssetAtPath<DungeonRunDefinition>(
                DungeonRunSceneAuthoringBuilder.DefinitionPath);
        Require(definition != null, "DungeonRunDefinition is missing.");
        HashSet<string> expectedRampKeys =
            ResolveExpectedRampKeys(definition);

        Scene scene = UnityEditor.SceneManagement.EditorSceneManager
            .NewPreviewScene();
        PhysicsScene physicsScene = scene.GetPhysicsScene();
        Require(physicsScene.IsValid(), "Local 3D physics scene is invalid.");

        GameObject root = new("DungeonTraversalRegressionRoot");
        SceneManager.MoveGameObjectToScene(root, scene);
        DungeonRunParameters parameters =
            definition.ResolveParameters(0, 0);
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
            MaxAttemptCount = MaximumGenerationAttempts,
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
        report.AppendLine("[DungeonTraversalRegressionValidator] PASS");
        report.AppendLine(
            $"TotalMainPath={parameters.TotalMainPathTileCount}");
        report.AppendLine($"ArenaCount={parameters.ArenaCount}");
        int totalTiles = 0;
        int totalConnections = 0;
        int totalGroundColliders = 0;
        int totalRamps = 0;
        int rejectedCandidateCount = 0;
        int maximumCandidateAttempt = 0;
        float maximumObservedRampLandingDelta = 0f;
        HashSet<string> seenRampKeys = new(StringComparer.Ordinal);

        try
        {
            for (int index = 0; index < SeedCount; index++)
            {
                int requestedSeed = FirstSeed + index;
                TraversalSnapshot snapshot = default;
                int acceptedAttempt = 0;
                int chosenSeed = 0;
                string lastRejection = string.Empty;
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
                    if (generator.Status != GenerationStatus.Complete)
                    {
                        rejectedCandidateCount++;
                        lastRejection =
                            $"generation status={generator.Status}";
                        generator.Clear(false);
                        continue;
                    }

                    DungeonLayoutQualityEvaluation quality =
                        DungeonLayoutQualityEvaluator.Evaluate(
                            generator.CurrentDungeon,
                            definition.MinimumBranchTileCount,
                            definition.MaximumPlanarAspectRatio,
                            definition.EndCapTileSet);
                    if (!quality.IsAccepted)
                    {
                        rejectedCandidateCount++;
                        lastRejection = quality.RejectionReason;
                        generator.Clear(false);
                        continue;
                    }

                    Physics.SyncTransforms();
                    snapshot = ValidateDungeon(
                        generator.CurrentDungeon,
                        generator,
                        physicsScene,
                        requestedSeed,
                        seenRampKeys,
                        parameters,
                        definition.ArenaTileSet,
                        definition.EndCapTileSet);
                    acceptedAttempt = candidateIndex + 1;
                    chosenSeed = generator.ChosenSeed;
                    break;
                }

                Require(
                    acceptedAttempt > 0,
                    $"Seed {requestedSeed}: "
                    + $"후보 {definition.LayoutCandidateCount}개 모두 실패. "
                    + lastRejection);
                maximumCandidateAttempt = Math.Max(
                    maximumCandidateAttempt,
                    acceptedAttempt);
                totalTiles += snapshot.TileCount;
                totalConnections += snapshot.ConnectionCount;
                totalGroundColliders += snapshot.GroundColliderCount;
                totalRamps += snapshot.RampCount;
                maximumObservedRampLandingDelta = Mathf.Max(
                    maximumObservedRampLandingDelta,
                    snapshot.MaximumRampLandingDelta);
                report.AppendLine(
                    $"Requested={requestedSeed}, Chosen={chosenSeed}, "
                    + $"Attempt={acceptedAttempt}, "
                    + $"Tiles={snapshot.TileCount}, "
                    + $"Connections={snapshot.ConnectionCount}, "
                    + $"Ground={snapshot.GroundColliderCount}, "
                    + $"Ramps={snapshot.RampCount}, "
                    + $"RampLandingDeltaMax="
                    + $"{snapshot.MaximumRampLandingDelta:F3}");

                generator.Clear(false);
                Require(
                    root.transform.childCount == 0,
                    $"Seed {requestedSeed}: generated tile leak after clear.");
            }
        }
        finally
        {
            arenaTileInjection.Dispose();
            generator.Clear(false);
            generationFlow.Dispose();
            UnityEngine.Object.DestroyImmediate(root);
            UnityEditor.SceneManagement.EditorSceneManager
                .ClosePreviewScene(scene);
        }

        string[] missingRampKeys =
            expectedRampKeys.Except(seenRampKeys).ToArray();
        string[] unexpectedRampKeys =
            seenRampKeys.Except(expectedRampKeys).ToArray();
        Require(
            missingRampKeys.Length == 0
            && unexpectedRampKeys.Length == 0,
            "Ramp coverage mismatch: "
            + $"{seenRampKeys.Count}/{expectedRampKeys.Count}, "
            + "missing="
            + string.Join(",", missingRampKeys)
            + ", unexpected="
            + string.Join(",", unexpectedRampKeys));
        report.AppendLine($"SeedCount={SeedCount}");
        report.AppendLine("FailedSeedCount=0");
        report.AppendLine(
            $"RejectedCandidateCount={rejectedCandidateCount}");
        report.AppendLine(
            $"MaximumCandidateAttempt={maximumCandidateAttempt}");
        report.AppendLine($"TotalTileChecks={totalTiles}");
        report.AppendLine($"TotalConnectionChecks={totalConnections}");
        report.AppendLine($"TotalGroundColliderChecks={totalGroundColliders}");
        report.AppendLine($"TotalRampChecks={totalRamps}");
        report.AppendLine(
            $"RampCoverage="
            + $"{seenRampKeys.Count}/{expectedRampKeys.Count}");
        report.AppendLine(
            $"MaximumRampLandingDelta="
            + $"{maximumObservedRampLandingDelta:F3}");
        return report.ToString().TrimEnd();
    }

    private static HashSet<string> ResolveExpectedRampKeys(
        DungeonRunDefinition definition)
    {
        string[] tileSetPaths =
        {
            DungeonContentAuthoringBuilder.RegularTileSetOutputPath,
            DungeonContentAuthoringBuilder.ArenaTileSetOutputPath,
            DungeonContentAuthoringBuilder.StartTileSetOutputPath,
            DungeonContentAuthoringBuilder.ExitTileSetOutputPath,
            DungeonContentAuthoringBuilder.EndCapTileSetOutputPath
        };
        HashSet<GameObject> prefabs = new();
        foreach (string tileSetPath in tileSetPaths)
        {
            TileSet tileSet =
                AssetDatabase.LoadAssetAtPath<TileSet>(tileSetPath);
            Require(
                tileSet?.TileWeights?.Weights != null,
                tileSetPath + ": TileSet 누락");
            foreach (GameObjectChance weight
                     in tileSet.TileWeights.Weights)
            {
                if (weight?.Value != null)
                    prefabs.Add(weight.Value);
            }
        }

        if (definition.TopologyRepairCatalog != null)
        {
            foreach (GameObject candidate
                     in definition.TopologyRepairCatalog
                         .GetCandidatePrefabs())
            {
                if (candidate != null)
                    prefabs.Add(candidate);
            }
        }

        HashSet<string> expectedKeys =
            new(StringComparer.Ordinal);
        foreach (GameObject prefab in prefabs)
        {
            foreach (DungeonStairRampProxy ramp
                     in prefab.GetComponentsInChildren<
                         DungeonStairRampProxy>(true))
            {
                expectedKeys.Add(
                    prefab.name
                    + "|"
                    + ramp.SourceHierarchyPath);
            }
        }

        return expectedKeys;
    }

    private static TraversalSnapshot ValidateDungeon(
        Dungeon dungeon,
        DungeonRunGenerator generator,
        PhysicsScene physicsScene,
        int seed,
        HashSet<string> seenRampKeys,
        DungeonRunParameters parameters,
        TileSet arenaTileSet,
        TileSet endCapTileSet)
    {
        Require(dungeon != null, $"Seed {seed}: dungeon is missing.");
        Require(
            dungeon.AllTiles != null && dungeon.AllTiles.Count >= 2,
            $"Seed {seed}: insufficient tiles.");
        Require(
            dungeon.MainPathTiles != null
            && dungeon.MainPathTiles.Count
                == parameters.TotalMainPathTileCount,
            $"Seed {seed}: main-path count mismatch.");
        int placedArenaCount =
            DungeonArenaTileInjection.CountPlacedArenas(
                dungeon,
                arenaTileSet);
        Require(
            placedArenaCount == parameters.ArenaCount
            && DungeonArenaTileInjection.AreAllPlacedArenasOnMainPath(
                dungeon,
                arenaTileSet),
            $"Seed {seed}: Arena count/path mismatch.");
        Require(
            DungeonEndCapContract.TryValidateFinalOpenDoorwayPass(
                dungeon,
                generator,
                endCapTileSet,
                out string endCapError),
            $"Seed {seed}: {endCapError}");

        int groundLayer = LayerMask.NameToLayer("Ground");
        Require(groundLayer >= 0, "Ground layer is missing.");
        int groundMask = 1 << groundLayer;
        int groundColliderCount = 0;
        int rampCount = 0;
        float maximumRampLandingDelta = 0f;

        for (int tileIndex = 0;
             tileIndex < dungeon.AllTiles.Count;
             tileIndex++)
        {
            Tile tile = dungeon.AllTiles[tileIndex];
            Require(tile != null, $"Seed {seed}: null tile.");

            Collider[] colliders =
                tile.GetComponentsInChildren<Collider>(true);
            int tileGroundCount = 0;
            for (int colliderIndex = 0;
                 colliderIndex < colliders.Length;
                 colliderIndex++)
            {
                Collider collider = colliders[colliderIndex];
                if (collider == null
                    || !collider.enabled
                    || collider.isTrigger
                    || !collider.gameObject.activeInHierarchy
                    || collider.gameObject.layer != groundLayer)
                {
                    continue;
                }

                Require(
                    collider.bounds.size.sqrMagnitude > 0.0001f,
                    $"Seed {seed}: zero-size Ground collider "
                    + collider.name + ".");
                tileGroundCount++;
            }

            Require(
                tileGroundCount > 0,
                $"Seed {seed}: tile {tile.name} has no Ground collider.");
            groundColliderCount += tileGroundCount;

            DungeonStairRampProxy[] ramps =
                tile.GetComponentsInChildren<DungeonStairRampProxy>(true);
            for (int rampIndex = 0;
                 rampIndex < ramps.Length;
                 rampIndex++)
            {
                float rampLandingDelta = ValidateRamp(
                    ramps[rampIndex],
                    tile,
                    physicsScene,
                    groundMask,
                    groundLayer,
                    seed);
                maximumRampLandingDelta = Mathf.Max(
                    maximumRampLandingDelta,
                    rampLandingDelta);
                seenRampKeys.Add(
                    NormalizeTileName(tile.name)
                    + "|"
                    + ramps[rampIndex].SourceHierarchyPath);
                rampCount++;
            }
        }

        ValidateAnchorGround(
            dungeon.MainPathTiles[0],
            DungeonRoomAnchorRole.Start,
            physicsScene,
            groundMask,
            seed);
        ValidateAnchorGround(
            dungeon.MainPathTiles[dungeon.MainPathTiles.Count - 1],
            DungeonRoomAnchorRole.Exit,
            physicsScene,
            groundMask,
            seed);

        Require(
            dungeon.Connections != null
            && dungeon.Connections.Count
                >= dungeon.MainPathTiles.Count - 1,
            $"Seed {seed}: insufficient doorway connections.");
        for (int connectionIndex = 0;
             connectionIndex < dungeon.Connections.Count;
             connectionIndex++)
        {
            ValidateConnection(
                dungeon.Connections[connectionIndex],
                physicsScene,
                groundMask,
                seed,
                connectionIndex);
        }

        return new TraversalSnapshot(
            dungeon.AllTiles.Count,
            dungeon.Connections.Count,
            groundColliderCount,
            rampCount,
            maximumRampLandingDelta);
    }

    private static float ValidateRamp(
        DungeonStairRampProxy ramp,
        Tile tile,
        PhysicsScene physicsScene,
        int groundMask,
        int groundLayer,
        int seed)
    {
        Require(ramp != null, $"Seed {seed}: null ramp marker.");
        BoxCollider collider = ramp.GetComponent<BoxCollider>();
        Require(
            collider != null
            && collider.enabled
            && !collider.isTrigger
            && collider.gameObject.layer == groundLayer,
            $"Seed {seed}: invalid ramp collider {ramp.name}.");
        Require(
            collider.size.x > 0.05f
            && collider.size.y > 0.01f
            && collider.size.z > 0.05f,
            $"Seed {seed}: invalid ramp size {ramp.name}.");
        Require(
            ramp.SlopeAngle > 0.01f
            && ramp.SlopeAngle <= MaximumRampAngle + 0.01f,
            $"Seed {seed}: invalid ramp angle "
            + $"{ramp.SlopeAngle:F2} on {ramp.name}.");
        Require(
            !string.IsNullOrWhiteSpace(ramp.SourceHierarchyPath),
            $"Seed {seed}: ramp source path is missing.");

        Vector3 horizontalForward = Vector3.ProjectOnPlane(
            collider.transform.forward,
            Vector3.up);
        Require(
            horizontalForward.sqrMagnitude > 0.0001f,
            $"Seed {seed}: ramp horizontal direction is missing "
            + ramp.name);
        horizontalForward.Normalize();

        float maximumLandingDelta = 0f;
        for (int directionIndex = 0;
             directionIndex < 2;
             directionIndex++)
        {
            float direction = directionIndex == 0 ? -1f : 1f;
            Vector3 localEndpoint =
                collider.center
                + Vector3.up * (collider.size.y * 0.5f)
                + Vector3.forward
                    * (collider.size.z * 0.5f * direction);
            Vector3 worldEndpoint =
                collider.transform.TransformPoint(localEndpoint);
            Require(
                TryFindRampLanding(
                    physicsScene,
                    worldEndpoint,
                    horizontalForward * direction,
                    tile,
                    collider,
                    groundMask,
                    out RaycastHit landingHit),
                $"Seed {seed}: ramp landing is missing "
                + $"{tile.name}/{ramp.name}/"
                + (direction < 0f ? "Lower" : "Upper")
                + " "
                + DescribeRampLanding(
                    physicsScene,
                    worldEndpoint,
                    horizontalForward * direction,
                    tile,
                    collider));
            float landingDelta =
                Mathf.Abs(landingHit.point.y - worldEndpoint.y);
            Require(
                landingDelta <= MaximumRampLandingDelta,
                $"Seed {seed}: ramp landing delta="
                + $"{landingDelta:F3} on {tile.name}/{ramp.name}/"
                + (direction < 0f ? "Lower" : "Upper"));
            maximumLandingDelta = Mathf.Max(
                maximumLandingDelta,
                landingDelta);
        }

        return maximumLandingDelta;
    }

    private static string DescribeRampLanding(
        PhysicsScene physicsScene,
        Vector3 endpoint,
        Vector3 outwardDirection,
        Tile tile,
        Collider rampCollider)
    {
        StringBuilder result = new();
        result.Append("endpoint=").Append(endpoint)
            .Append(", outward=").Append(outwardDirection)
            .Append(", probes=");
        RaycastHit[] hits = new RaycastHit[64];
        for (int offsetIndex = 0;
             offsetIndex < RampLandingProbeOffsets.Length;
             offsetIndex++)
        {
            float offset = RampLandingProbeOffsets[offsetIndex];
            Vector3 probe = endpoint + outwardDirection * offset;
            int hitCount = physicsScene.Raycast(
                probe + Vector3.up * 1.5f,
                Vector3.down,
                hits,
                4f,
                ~0,
                QueryTriggerInteraction.Ignore);
            result.Append("{o=").Append(offset.ToString("F2"))
                .Append(",hits=");
            int written = 0;
            for (int hitIndex = 0;
                 hitIndex < hitCount && written < 6;
                 hitIndex++)
            {
                RaycastHit hit = hits[hitIndex];
                if (hit.collider == null || hit.collider == rampCollider)
                    continue;
                if (written > 0)
                    result.Append("|");
                result.Append(hit.collider.name)
                    .Append("[")
                    .Append(LayerMask.LayerToName(
                        hit.collider.gameObject.layer))
                    .Append("]@")
                    .Append(hit.point)
                    .Append(",nY=")
                    .Append(hit.normal.y.ToString("F2"));
                written++;
            }
            if (written == 0)
                result.Append("none");
            result.Append("}");
        }

        Transform dungeonRoot = tile.Dungeon != null
            ? tile.Dungeon.transform
            : tile.transform.root;
        Collider[] colliders =
            dungeonRoot.GetComponentsInChildren<Collider>(true);
        List<(Collider Collider, float Distance)> nearby = new();
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null
                || collider == rampCollider
                || !collider.enabled
                || collider.isTrigger)
            {
                continue;
            }

            float distance = Vector3.Distance(
                collider.bounds.ClosestPoint(endpoint),
                endpoint);
            nearby.Add((collider, distance));
        }
        nearby.Sort((left, right) =>
            left.Distance.CompareTo(right.Distance));
        result.Append(", nearby=");
        int nearbyCount = Mathf.Min(nearby.Count, 10);
        for (int i = 0; i < nearbyCount; i++)
        {
            if (i > 0)
                result.Append("|");
            Collider collider = nearby[i].Collider;
            result.Append(collider.name)
                .Append("[")
                .Append(LayerMask.LayerToName(collider.gameObject.layer))
                .Append("],d=")
                .Append(nearby[i].Distance.ToString("F2"))
                .Append(",b=")
                .Append(collider.bounds.center)
                .Append("/")
                .Append(collider.bounds.size);
        }

        List<(RaycastHit Hit, float HorizontalDistance, float HeightDelta)>
            walkableCandidates = new();
        Vector3 lateralDirection = Vector3.Cross(
            Vector3.up,
            outwardDirection).normalized;
        RaycastHit[] gridHits = new RaycastHit[64];
        for (float forwardOffset = 0f;
             forwardOffset <= 3.001f;
             forwardOffset += 0.25f)
        {
            for (float lateralOffset = -2f;
                 lateralOffset <= 2.001f;
                 lateralOffset += 0.25f)
            {
                Vector3 probe =
                    endpoint
                    + outwardDirection * forwardOffset
                    + lateralDirection * lateralOffset;
                int hitCount = physicsScene.Raycast(
                    probe + Vector3.up * 5f,
                    Vector3.down,
                    gridHits,
                    10f,
                    ~0,
                    QueryTriggerInteraction.Ignore);
                for (int hitIndex = 0; hitIndex < hitCount; hitIndex++)
                {
                    RaycastHit hit = gridHits[hitIndex];
                    if (hit.collider == null
                        || hit.collider == rampCollider
                        || Vector3.Dot(hit.normal, Vector3.up)
                            < MinimumWalkableNormalY)
                    {
                        continue;
                    }

                    float heightDelta =
                        Mathf.Abs(hit.point.y - endpoint.y);
                    if (heightDelta > 1.5f)
                        continue;

                    Vector3 horizontalDelta = Vector3.ProjectOnPlane(
                        hit.point - endpoint,
                        Vector3.up);
                    walkableCandidates.Add(
                        (hit, horizontalDelta.magnitude, heightDelta));
                }
            }
        }

        walkableCandidates.Sort((left, right) =>
        {
            int distanceCompare = left.HorizontalDistance.CompareTo(
                right.HorizontalDistance);
            return distanceCompare != 0
                ? distanceCompare
                : left.HeightDelta.CompareTo(right.HeightDelta);
        });
        result.Append(", walkable-grid=");
        int candidateCount = Mathf.Min(walkableCandidates.Count, 16);
        for (int i = 0; i < candidateCount; i++)
        {
            if (i > 0)
                result.Append("|");
            (RaycastHit hit, float horizontalDistance, float heightDelta) =
                walkableCandidates[i];
            result.Append(hit.collider.name)
                .Append("[")
                .Append(LayerMask.LayerToName(
                    hit.collider.gameObject.layer))
                .Append("]@")
                .Append(hit.point)
                .Append(",hd=")
                .Append(horizontalDistance.ToString("F2"))
                .Append(",yd=")
                .Append(heightDelta.ToString("F2"));
        }
        if (candidateCount == 0)
            result.Append("none");

        return result.ToString();
    }

    private static void ValidateAnchorGround(
        Tile tile,
        DungeonRoomAnchorRole role,
        PhysicsScene physicsScene,
        int groundMask,
        int seed)
    {
        DungeonRoomAnchor[] anchors =
            tile.GetComponentsInChildren<DungeonRoomAnchor>(true);
        DungeonRoomAnchor anchor = null;
        for (int i = 0; i < anchors.Length; i++)
        {
            if (anchors[i] != null
                && anchors[i].Supports(role)
                && anchors[i].Supports(DungeonRoomAnchorRole.Spawn))
            {
                anchor = anchors[i];
                break;
            }
        }

        Require(
            anchor != null,
            $"Seed {seed}: {role} anchor is missing.");
        Require(
            TryRaycastGround(
                physicsScene,
                anchor.transform.position,
                tile,
                groundMask,
                out RaycastHit hit),
            $"Seed {seed}: {role} anchor has no Ground support.");
        Require(
            Mathf.Abs(hit.point.y - anchor.transform.position.y)
                <= MaximumAnchorFloorDelta,
            $"Seed {seed}: {role} anchor Ground delta="
            + $"{Mathf.Abs(hit.point.y - anchor.transform.position.y):F3}.");
    }

    private static void ValidateConnection(
        DoorwayConnection connection,
        PhysicsScene physicsScene,
        int groundMask,
        int seed,
        int connectionIndex)
    {
        Require(
            connection != null
            && connection.A != null
            && connection.B != null
            && connection.A.Tile != null
            && connection.B.Tile != null,
            $"Seed {seed}: connection {connectionIndex} is invalid.");

        Doorway a = connection.A;
        Doorway b = connection.B;
        float doorwayDelta = Vector3.Distance(
            a.transform.position,
            b.transform.position);
        Require(
            doorwayDelta <= MaximumDoorwayPositionDelta,
            $"Seed {seed}: connection {connectionIndex} doorway delta="
            + $"{doorwayDelta:F3}.");
        Require(
            Vector3.Dot(
                a.transform.forward.normalized,
                b.transform.forward.normalized) <= -0.95f,
            $"Seed {seed}: connection {connectionIndex} "
            + "doorways do not face each other.");

        Require(
            TryFindDoorwayGround(
                physicsScene,
                a,
                groundMask,
                out RaycastHit aHit),
            $"Seed {seed}: connection {connectionIndex} A has no floor. "
            + DescribeDoorway(a));
        Require(
            TryFindDoorwayGround(
                physicsScene,
                b,
                groundMask,
                out RaycastHit bHit),
            $"Seed {seed}: connection {connectionIndex} B has no floor. "
            + DescribeDoorway(b));
        Require(
            Mathf.Abs(aHit.point.y - bHit.point.y)
                <= MaximumDoorwayFloorDelta,
            $"Seed {seed}: connection {connectionIndex} floor delta="
            + $"{Mathf.Abs(aHit.point.y - bHit.point.y):F3}.");
    }

    private static string DescribeDoorway(Doorway doorway)
    {
        StringBuilder result = new();
        result.Append("tile=").Append(doorway.Tile.name)
            .Append(", doorway=").Append(doorway.name)
            .Append(", position=").Append(doorway.transform.position)
            .Append(", forward=").Append(doorway.transform.forward)
            .Append(", nearbyGround=");

        int groundLayer = LayerMask.NameToLayer("Ground");
        Collider[] colliders =
            doorway.Tile.GetComponentsInChildren<Collider>(true);
        List<(Collider Collider, float Distance)> nearby = new();
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null
                || collider.gameObject.layer != groundLayer)
            {
                continue;
            }

            float distance = Vector3.Distance(
                collider.bounds.ClosestPoint(doorway.transform.position),
                doorway.transform.position);
            nearby.Add((collider, distance));
        }

        nearby.Sort((left, right) =>
            left.Distance.CompareTo(right.Distance));
        int written = Mathf.Min(nearby.Count, 8);
        for (int i = 0; i < written; i++)
        {
            Collider collider = nearby[i].Collider;

            if (i > 0)
                result.Append(" | ");
            result.Append(collider.name)
                .Append("@")
                .Append(collider.bounds.center)
                .Append("/")
                .Append(collider.bounds.size)
                .Append(",d=")
                .Append(nearby[i].Distance.ToString("F2"));
        }

        if (written == 0)
            result.Append("none");

        result.Append(", nearbyAny=");
        List<(Collider Collider, float Distance)> allNearby = new();
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null
                || !collider.enabled
                || collider.isTrigger)
            {
                continue;
            }

            float distance = Vector3.Distance(
                collider.bounds.ClosestPoint(doorway.transform.position),
                doorway.transform.position);
            allNearby.Add((collider, distance));
        }

        allNearby.Sort((left, right) =>
            left.Distance.CompareTo(right.Distance));
        int allWritten = Mathf.Min(allNearby.Count, 12);
        for (int i = 0; i < allWritten; i++)
        {
            Collider collider = allNearby[i].Collider;
            if (i > 0)
                result.Append(" | ");
            result.Append(collider.name)
                .Append("[")
                .Append(LayerMask.LayerToName(collider.gameObject.layer))
                .Append("]@")
                .Append(collider.bounds.center)
                .Append("/")
                .Append(collider.bounds.size)
                .Append(",d=")
                .Append(allNearby[i].Distance.ToString("F2"));
        }
        return result.ToString();
    }

    private static bool TryFindDoorwayGround(
        PhysicsScene physicsScene,
        Doorway doorway,
        int groundMask,
        out RaycastHit hit)
    {
        for (int i = 0; i < DoorwayGroundSearchOffsets.Length; i++)
        {
            Vector3 samplePosition =
                doorway.transform.position
                + doorway.transform.forward
                    * DoorwayGroundSearchOffsets[i];
            if (TryRaycastGround(
                    physicsScene,
                    samplePosition,
                    doorway.Tile,
                    groundMask,
                    out hit))
            {
                return true;
            }
        }

        hit = default;
        return false;
    }

    private static bool TryFindRampLanding(
        PhysicsScene physicsScene,
        Vector3 endpoint,
        Vector3 outwardDirection,
        Tile tile,
        Collider rampCollider,
        int groundMask,
        out RaycastHit hit)
    {
        RaycastHit[] hits = new RaycastHit[64];
        Transform dungeonRoot = tile.Dungeon != null
            ? tile.Dungeon.transform
            : tile.transform.root;
        Vector3 lateralDirection = Vector3.ProjectOnPlane(
            rampCollider.transform.right,
            Vector3.up).normalized;
        float halfWidth = 0f;
        if (rampCollider is BoxCollider boxCollider)
        {
            halfWidth = rampCollider.transform.TransformVector(
                Vector3.right * (boxCollider.size.x * 0.5f)).magnitude;
        }

        for (int lateralIndex = 0;
             lateralIndex < RampLandingLateralRatios.Length;
             lateralIndex++)
        {
            Vector3 lateralOffset =
                lateralDirection
                * halfWidth
                * RampLandingLateralRatios[lateralIndex];
            for (int offsetIndex = 0;
                 offsetIndex < RampLandingProbeOffsets.Length;
                 offsetIndex++)
            {
                Vector3 probe =
                    endpoint
                    + lateralOffset
                    + outwardDirection
                        * RampLandingProbeOffsets[offsetIndex];
                Vector3 rayOrigin = probe + Vector3.up * 1.5f;
                int hitCount = physicsScene.Raycast(
                    rayOrigin,
                    Vector3.down,
                    hits,
                    4f,
                    groundMask,
                    QueryTriggerInteraction.Ignore);
                bool found = false;
                float bestHeightDelta = float.PositiveInfinity;
                float bestDistance = float.PositiveInfinity;
                hit = default;
                for (int hitIndex = 0;
                     hitIndex < hitCount;
                     hitIndex++)
                {
                    RaycastHit candidate = hits[hitIndex];
                    if (candidate.collider == null
                        || candidate.collider == rampCollider
                        || !candidate.collider.transform.IsChildOf(
                            dungeonRoot)
                        || Vector3.Dot(candidate.normal, Vector3.up)
                            < MinimumWalkableNormalY)
                    {
                        continue;
                    }

                    float heightDelta =
                        Mathf.Abs(candidate.point.y - endpoint.y);
                    if (heightDelta
                        > MaximumRampLandingDelta + 0.001f)
                    {
                        continue;
                    }

                    if (!found
                        || heightDelta < bestHeightDelta - 0.001f
                        || Mathf.Abs(heightDelta - bestHeightDelta)
                            <= 0.001f
                        && candidate.distance < bestDistance)
                    {
                        found = true;
                        hit = candidate;
                        bestHeightDelta = heightDelta;
                        bestDistance = candidate.distance;
                    }
                }

                if (found)
                    return true;
            }
        }

        hit = default;
        return false;
    }

    private static bool TryRaycastGround(
        PhysicsScene physicsScene,
        Vector3 samplePosition,
        Tile tile,
        int groundMask,
        out RaycastHit hit)
    {
        Vector3 rayOrigin = samplePosition + Vector3.up * 1.5f;
        RaycastHit[] hits = new RaycastHit[64];
        int hitCount = physicsScene.Raycast(
            rayOrigin,
            Vector3.down,
            hits,
            4f,
            groundMask,
            QueryTriggerInteraction.Ignore);
        bool found = false;
        hit = default;
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit candidate = hits[i];
            if (candidate.collider == null
                || !candidate.collider.transform.IsChildOf(tile.transform)
                || Vector3.Dot(candidate.normal, Vector3.up)
                    < MinimumWalkableNormalY)
            {
                continue;
            }

            if (!found || candidate.distance < bestDistance)
            {
                found = true;
                hit = candidate;
                bestDistance = candidate.distance;
            }
        }

        return found;
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

    private readonly struct TraversalSnapshot
    {
        public TraversalSnapshot(
            int tileCount,
            int connectionCount,
            int groundColliderCount,
            int rampCount,
            float maximumRampLandingDelta)
        {
            TileCount = tileCount;
            ConnectionCount = connectionCount;
            GroundColliderCount = groundColliderCount;
            RampCount = rampCount;
            MaximumRampLandingDelta = maximumRampLandingDelta;
        }

        public int TileCount { get; }
        public int ConnectionCount { get; }
        public int GroundColliderCount { get; }
        public int RampCount { get; }
        public float MaximumRampLandingDelta { get; }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
