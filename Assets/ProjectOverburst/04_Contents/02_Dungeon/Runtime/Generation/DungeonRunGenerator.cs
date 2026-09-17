using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DunGen;
using UnityEngine;

public sealed class DungeonRunGenerator : DungeonGenerator
{
    private TileSet endCapTileSet;
    private DungeonTopologyRepairCatalog topologyRepairCatalog;

    public int OpenDoorwayCountBeforeEndCaps { get; private set; }
    public int PlacedEndCapCount { get; private set; }
    public int OpenDoorwayCountAfterEndCaps { get; private set; }
    public bool EndCapPassCompleted { get; private set; }
    public int OpenDoorwayCountBeforeTopologyRepair { get; private set; }
    public int ReplacedTopologyTileCount { get; private set; }
    public int ReplacedWithEndCapCount { get; private set; }
    public int ReplacedShapeTileCount { get; private set; }
    public int RemovedOpenDoorwayCount { get; private set; }
    public int OpenDoorwayCountAfterTopologyRepair { get; private set; }
    public bool TopologyRepairPassCompleted { get; private set; }

    public DungeonRunGenerator()
    {
    }

    public DungeonRunGenerator(
        GameObject root,
        TileSet configuredEndCapTileSet)
        : this(root, configuredEndCapTileSet, null)
    {
    }

    public DungeonRunGenerator(
        GameObject root,
        TileSet configuredEndCapTileSet,
        DungeonTopologyRepairCatalog configuredTopologyRepairCatalog)
        : base(root)
    {
        endCapTileSet = configuredEndCapTileSet;
        topologyRepairCatalog = configuredTopologyRepairCatalog;
    }

    public void ConfigureEndCapTileSet(TileSet configuredEndCapTileSet)
    {
        endCapTileSet = configuredEndCapTileSet;
    }

    public void ConfigureTopologyRepairCatalog(
        DungeonTopologyRepairCatalog configuredCatalog)
    {
        topologyRepairCatalog = configuredCatalog;
    }

    public static DungeonRunGenerator CreateFrom(
        DungeonGenerator source,
        GameObject root,
        TileSet configuredEndCapTileSet,
        DungeonTopologyRepairCatalog configuredTopologyRepairCatalog =
            null)
    {
        if (source is DungeonRunGenerator projectGenerator)
        {
            projectGenerator.Root = root;
            projectGenerator.ConfigureEndCapTileSet(
                configuredEndCapTileSet);
            projectGenerator.ConfigureTopologyRepairCatalog(
                configuredTopologyRepairCatalog);
            return projectGenerator;
        }

        DungeonRunGenerator generator =
            new(
                root,
                configuredEndCapTileSet,
                configuredTopologyRepairCatalog);
        if (source == null)
            return generator;

        generator.Seed = source.Seed;
        generator.ShouldRandomizeSeed = source.ShouldRandomizeSeed;
        generator.MaxAttemptCount = source.MaxAttemptCount;
        generator.UseMaximumPairingAttempts =
            source.UseMaximumPairingAttempts;
        generator.MaxPairingAttempts = source.MaxPairingAttempts;
        generator.IgnoreSpriteBounds = source.IgnoreSpriteBounds;
        generator.UpDirection = source.UpDirection;
        generator.OverrideRepeatMode = source.OverrideRepeatMode;
        generator.RepeatMode = source.RepeatMode;
        generator.OverrideAllowTileRotation =
            source.OverrideAllowTileRotation;
        generator.AllowTileRotation = source.AllowTileRotation;
        generator.DebugRender = source.DebugRender;
        generator.LengthMultiplier = source.LengthMultiplier;
        generator.PlaceTileTriggers = source.PlaceTileTriggers;
        generator.TileTriggerLayer = source.TileTriggerLayer;
        generator.GenerateAsynchronously =
            source.GenerateAsynchronously;
        generator.MaxAsyncFrameMilliseconds =
            source.MaxAsyncFrameMilliseconds;
        generator.PauseBetweenRooms = source.PauseBetweenRooms;
        generator.RestrictDungeonToBounds =
            source.RestrictDungeonToBounds;
        generator.TilePlacementBounds = source.TilePlacementBounds;
        generator.OverlapThreshold = source.OverlapThreshold;
        generator.Padding = source.Padding;
        generator.DisallowOverhangs = source.DisallowOverhangs;
        generator.DungeonFlow = source.DungeonFlow;
        generator.IsAnalysis = source.IsAnalysis;
        return generator;
    }

    protected override IEnumerator GenerateBranchPaths()
    {
        OpenDoorwayCountBeforeEndCaps = 0;
        PlacedEndCapCount = 0;
        OpenDoorwayCountAfterEndCaps = 0;
        EndCapPassCompleted = false;
        OpenDoorwayCountBeforeTopologyRepair = 0;
        ReplacedTopologyTileCount = 0;
        ReplacedWithEndCapCount = 0;
        ReplacedShapeTileCount = 0;
        RemovedOpenDoorwayCount = 0;
        OpenDoorwayCountAfterTopologyRepair = 0;
        TopologyRepairPassCompleted = false;

        IEnumerator baseGeneration = base.GenerateBranchPaths();
        while (baseGeneration.MoveNext())
            yield return baseGeneration.Current;

        if (proxyDungeon == null
            || DungeonFlow == null
            || endCapTileSet == null
            || endCapTileSet.TileWeights == null
            || !endCapTileSet.TileWeights.Weights.Any(weight =>
                weight?.Value != null
                && weight.BranchPathWeight > 0f))
        {
            yield break;
        }

        // 본체 배치가 끝난 뒤 서로 맞닿은 기존 Doorway를 먼저 연결한다.
        // 그 뒤에도 열린 Doorway만 EndCap 대상이 된다.
        proxyDungeon.ConnectOverlappingDoorways(
            DungeonFlow.DoorwayConnectionChance,
            DungeonFlow,
            RandomStream);

        List<TileProxy> completedLayoutTiles =
            proxyDungeon.AllTiles.ToList();
        OpenDoorwayCountBeforeEndCaps =
            CountOpenDoorways(completedLayoutTiles);

        bool previousPairingLimit =
            UseMaximumPairingAttempts;
        List<InjectedTile> pendingInjections =
            tilesPendingInjection;
        try
        {
            // 열린 Doorway마다 모든 EndCap 소켓·회전 조합을 끝까지 검사한다.
            UseMaximumPairingAttempts = false;
            // 본체용 Injection이 EndCap 전용 후보를 가로채지 못하게 격리한다.
            tilesPendingInjection = new List<InjectedTile>();
            for (int i = 0; i < completedLayoutTiles.Count; i++)
            {
                TileProxy attachTo = completedLayoutTiles[i];
                int maximumPlacements =
                    attachTo?.Doorways?.Count ?? 0;

                for (int placement = 0;
                     placement < maximumPlacements
                     && attachTo.UnusedDoorways.Any();
                     placement++)
                {
                    TileProxy endCap = AddTile(
                        attachTo,
                        new[] { endCapTileSet },
                        1f,
                        attachTo.Placement.Archetype);
                    if (endCap == null)
                        break;

                    endCap.Placement.NormalizedBranchDepth = 1f;
                    endCap.Placement.GraphNode =
                        attachTo.Placement.GraphNode;
                    endCap.Placement.GraphLine =
                        attachTo.Placement.GraphLine;
                    PlacedEndCapCount++;

                    if (GenerateAsynchronously)
                        yield return null;
                }
            }
        }
        finally
        {
            tilesPendingInjection = pendingInjections;
            UseMaximumPairingAttempts =
                previousPairingLimit;
            OpenDoorwayCountAfterEndCaps =
                CountOpenDoorways(completedLayoutTiles);
        }

        EndCapPassCompleted = true;
        ApplyTopologyRepairPass(completedLayoutTiles);
        TopologyRepairPassCompleted = true;
    }

    protected override TileProxy AddTile(
        TileProxy attachTo,
        IEnumerable<TileSet> useableTileSets,
        float normalizedDepth,
        DungeonArchetype archetype,
        TilePlacementResult result = TilePlacementResult.None)
    {
        List<TileSet> tileSets = useableTileSets?.ToList()
            ?? new List<TileSet>();
        bool isEndCapPlacement =
            endCapTileSet != null
            && tileSets.Count == 1
            && tileSets[0] == endCapTileSet
            && endCapTileSet.TileWeights.Weights.Any(weight =>
                weight?.Value != null
                && weight.Value.GetComponent<Tile>()?.AllowRotation
                    == true);
        if (!isEndCapPlacement)
        {
            return base.AddTile(
                attachTo,
                tileSets,
                normalizedDepth,
                archetype,
                result);
        }

        bool previousOverride = OverrideAllowTileRotation;
        bool previousAllowRotation = AllowTileRotation;
        try
        {
            // 본체 생성 뒤 열린 Doorway를 마감할 때만 EndCap 회전을 허용한다.
            OverrideAllowTileRotation = true;
            AllowTileRotation = true;
            return base.AddTile(
                attachTo,
                tileSets,
                normalizedDepth,
                archetype,
                result);
        }
        finally
        {
            OverrideAllowTileRotation = previousOverride;
            AllowTileRotation = previousAllowRotation;
        }
    }

    private static int CountOpenDoorways(
        IEnumerable<TileProxy> tiles)
    {
        if (tiles == null)
            return 0;

        int count = 0;
        foreach (TileProxy tile in tiles)
        {
            if (tile != null)
                count += tile.UnusedDoorways.Count();
        }

        return count;
    }

    private void ApplyTopologyRepairPass(
        IList<TileProxy> completedLayoutTiles)
    {
        OpenDoorwayCountBeforeTopologyRepair =
            CountOpenDoorways(completedLayoutTiles);
        OpenDoorwayCountAfterTopologyRepair =
            OpenDoorwayCountBeforeTopologyRepair;
        if (OpenDoorwayCountBeforeTopologyRepair == 0)
        {
            return;
        }

        int connectionCountBefore = proxyDungeon.Connections.Count;
        for (int tileIndex = 0;
             tileIndex < completedLayoutTiles.Count;
             tileIndex++)
        {
            TileProxy sourceTile = completedLayoutTiles[tileIndex];
            if (sourceTile == null
                || !sourceTile.UnusedDoorways.Any())
            {
                continue;
            }

            List<DoorwayProxy> usedSourceDoorways =
                sourceTile.UsedDoorways
                    .OrderBy(doorway => doorway.Index)
                    .ToList();
            TopologyReplacementMatch selected = null;
            bool replacedWithEndCap = false;
            if (CanReplaceWithEndCap(
                    sourceTile,
                    usedSourceDoorways))
            {
                selected = TryCreateEndCapReplacement(
                    sourceTile,
                    usedSourceDoorways[0]);
                replacedWithEndCap = selected != null;
            }
            else if (topologyRepairCatalog != null
                && topologyRepairCatalog.TryGetRule(
                    sourceTile.Prefab,
                    out DungeonTopologyRepairRule rule))
            {
                DungeonTopologyRepairShape requiredShape =
                    ResolveRequiredTopology(
                        usedSourceDoorways.Select(
                            doorway => doorway.Forward));
                if (requiredShape !=
                    DungeonTopologyRepairShape.None)
                {
                    selected =
                        FindFirstPriorityReplacement(
                            sourceTile,
                            usedSourceDoorways,
                            rule,
                            requiredShape,
                            BuildRepairSeed(
                                ChosenSeed,
                                tileIndex,
                                rule.SourceFamilyName));
                }
            }

            if (selected == null)
                continue;

            ReplaceProxyTile(sourceTile, selected);
            completedLayoutTiles[tileIndex] =
                selected.Replacement;
            if (replacedWithEndCap)
                ReplacedWithEndCapCount++;
            else
                ReplacedShapeTileCount++;
        }

        ReplacedTopologyTileCount =
            ReplacedWithEndCapCount
            + ReplacedShapeTileCount;
        if (ReplacedTopologyTileCount > 0)
            proxyDungeon.ClearDebugVisuals();
        OpenDoorwayCountAfterTopologyRepair =
            CountOpenDoorways(completedLayoutTiles);
        RemovedOpenDoorwayCount =
            OpenDoorwayCountBeforeTopologyRepair
            - OpenDoorwayCountAfterTopologyRepair;
        if (proxyDungeon.Connections.Count != connectionCountBefore)
        {
            throw new InvalidOperationException(
                "형태 보정 중 Dungeon 연결 수가 변경됐습니다.");
        }
    }

    public static DungeonTopologyRepairShape
        ResolveRequiredTopology(
            IEnumerable<Vector3> connectedDirections)
    {
        List<Vector3> directions = connectedDirections?
            .Where(direction => direction.sqrMagnitude > 0.0001f)
            .Select(direction => direction.normalized)
            .ToList()
            ?? new List<Vector3>();
        if (directions.Count == 3)
            return DungeonTopologyRepairShape.T;
        if (directions.Count != 2)
            return DungeonTopologyRepairShape.None;

        float dot = Vector3.Dot(
            directions[0],
            directions[1]);
        if (dot <= -0.95f)
            return DungeonTopologyRepairShape.I;
        return Mathf.Abs(dot) <= 0.05f
            ? DungeonTopologyRepairShape.L
            : DungeonTopologyRepairShape.None;
    }

    private bool CanReplaceWithEndCap(
        TileProxy sourceTile,
        IReadOnlyList<DoorwayProxy> usedSourceDoorways)
    {
        return sourceTile?.Placement != null
            && !sourceTile.Placement.IsOnMainPath
            && sourceTile.Doorways.Count > 1
            && usedSourceDoorways.Count == 1
            && usedSourceDoorways[0].ConnectedDoorway != null
            && endCapTileSet?.TileWeights?.Weights != null;
    }

    private TopologyReplacementMatch TryCreateEndCapReplacement(
        TileProxy sourceTile,
        DoorwayProxy usedSourceDoorway)
    {
        foreach (GameObjectChance weight
                 in endCapTileSet.TileWeights.Weights)
        {
            GameObject prefab = weight?.Value;
            if (prefab == null
                || weight.BranchPathWeight <= 0f)
            {
                continue;
            }

            TileProxy replacement =
                new(GetTileTemplate(prefab));
            if (replacement.Doorways.Count != 1)
                continue;

            DoorwayProxy replacementDoorway =
                replacement.Doorways[0];
            TilePlacementData placement =
                new(sourceTile.Placement)
                {
                    LocalBounds =
                        replacement.Placement.LocalBounds,
                    TileSet = endCapTileSet
                };
            replacement.Placement = placement;
            replacement.PositionBySocket(
                replacementDoorway,
                usedSourceDoorway.ConnectedDoorway);
            if (!DoorwaysMatch(
                    usedSourceDoorway,
                    replacementDoorway,
                    replacement)
                || IsInvalidReplacementCollision(
                    sourceTile,
                    replacement))
            {
                continue;
            }

            return new TopologyReplacementMatch(
                replacement,
                new List<DoorwayReplacementPair>
                {
                    new(
                        usedSourceDoorway,
                        replacementDoorway,
                        usedSourceDoorway.ConnectedDoorway)
                },
                prefab.name,
                0);
        }

        return null;
    }

    private TopologyReplacementMatch
        FindFirstPriorityReplacement(
        TileProxy sourceTile,
        IReadOnlyList<DoorwayProxy> usedSourceDoorways,
        DungeonTopologyRepairRule rule,
        DungeonTopologyRepairShape requiredShape,
        int seed)
    {
        IEnumerable<DungeonTopologyRepairCandidateGroup>
            orderedGroups = rule.CandidateGroups
                .Where(group =>
                    group != null
                    && group.Shape == requiredShape)
                .OrderBy(group => group.Priority)
                .ThenBy(
                    group => group.TopologyId,
                    StringComparer.Ordinal);
        foreach (DungeonTopologyRepairCandidateGroup group
                 in orderedGroups)
        {
            if (group.Variants == null)
                continue;

            List<TopologyReplacementMatch> matches = new();
            foreach (GameObject prefab in group.Variants
                         .Where(item => item != null)
                         .OrderBy(
                             item => item.name,
                             StringComparer.Ordinal))
            {
                DungeonTopologyRepairCandidateMarker marker =
                    prefab.GetComponent<
                        DungeonTopologyRepairCandidateMarker>();
                if (marker == null
                    || marker.SourceFamilyPrefab
                        != rule.SourceFamilyPrefab
                    || !string.Equals(
                        marker.TopologyId,
                        group.TopologyId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                int rotationCount =
                    group.AllowQuarterTurns ? 4 : 1;
                for (int quarterTurns = 0;
                     quarterTurns < rotationCount;
                     quarterTurns++)
                {
                    TopologyReplacementMatch match =
                        TryCreateReplacement(
                            sourceTile,
                            usedSourceDoorways,
                            prefab,
                            quarterTurns);
                    if (match != null)
                        matches.Add(match);
                }
            }

            if (matches.Count > 0)
            {
                return SelectReplacement(
                    matches,
                    unchecked(seed * 397 ^ group.Priority));
            }
        }

        return null;
    }

    private TopologyReplacementMatch TryCreateReplacement(
        TileProxy sourceTile,
        IReadOnlyList<DoorwayProxy> usedSourceDoorways,
        GameObject candidatePrefab,
        int quarterTurns)
    {
        TileProxy replacement =
            new(GetTileTemplate(candidatePrefab));
        if (replacement.Doorways.Count
            != usedSourceDoorways.Count)
        {
            return null; // 연결된 소켓만 남긴 후보만 교체할 수 있다.
        }

        Quaternion relativeRotation =
            Quaternion.AngleAxis(
                quarterTurns * 90f,
                Vector3.up);
        TilePlacementData placement =
            new(sourceTile.Placement)
            {
                LocalBounds = ResolveInPlaceLocalBounds(
                    sourceTile.Placement.LocalBounds,
                    relativeRotation), // 원본 배치 영역 안에서 프리팹만 교체한다.
                Position = sourceTile.Placement.Position,
                Rotation = sourceTile.Placement.Rotation
                    * relativeRotation
            };
        replacement.Placement = placement;

        List<DoorwayReplacementPair> pairs = new();
        HashSet<DoorwayProxy> claimedCandidateDoorways = new();
        for (int sourceIndex = 0;
             sourceIndex < usedSourceDoorways.Count;
             sourceIndex++)
        {
            DoorwayProxy sourceDoorway =
                usedSourceDoorways[sourceIndex];
            DoorwayProxy candidateDoorway =
                replacement.Doorways.FirstOrDefault(candidate =>
                    !claimedCandidateDoorways.Contains(candidate)
                    && DoorwaysMatch(
                        sourceDoorway,
                        candidate,
                        replacement));
            if (candidateDoorway == null)
                return null;

            claimedCandidateDoorways.Add(candidateDoorway);
            pairs.Add(
                new DoorwayReplacementPair(
                    sourceDoorway,
                    candidateDoorway,
                    sourceDoorway.ConnectedDoorway));
        }

        if (IsInvalidReplacementCollision(
                sourceTile,
                replacement))
        {
            return null;
        }

        return new TopologyReplacementMatch(
            replacement,
            pairs,
            candidatePrefab.name,
            quarterTurns);
    }

    private static Bounds ResolveInPlaceLocalBounds(
        Bounds sourceLocalBounds,
        Quaternion relativeRotation)
    {
        if (Mathf.Abs(
                Quaternion.Dot(
                    relativeRotation,
                    Quaternion.identity))
            >= 0.999999f)
        {
            return sourceLocalBounds;
        }

        Quaternion inverseRotation =
            Quaternion.Inverse(relativeRotation);
        Vector3 rotatedCenter =
            inverseRotation * sourceLocalBounds.center;
        Vector3 sourceExtents = sourceLocalBounds.extents;
        Vector3 rotatedX =
            inverseRotation
            * new Vector3(sourceExtents.x, 0f, 0f);
        Vector3 rotatedY =
            inverseRotation
            * new Vector3(0f, sourceExtents.y, 0f);
        Vector3 rotatedZ =
            inverseRotation
            * new Vector3(0f, 0f, sourceExtents.z);
        Vector3 rotatedExtents = new(
            Mathf.Abs(rotatedX.x)
                + Mathf.Abs(rotatedY.x)
                + Mathf.Abs(rotatedZ.x),
            Mathf.Abs(rotatedX.y)
                + Mathf.Abs(rotatedY.y)
                + Mathf.Abs(rotatedZ.y),
            Mathf.Abs(rotatedX.z)
                + Mathf.Abs(rotatedY.z)
                + Mathf.Abs(rotatedZ.z));
        return new Bounds(rotatedCenter, rotatedExtents * 2f);
    }

    private bool DoorwaysMatch(
        DoorwayProxy sourceDoorway,
        DoorwayProxy candidateDoorway,
        TileProxy replacement)
    {
        const float positionTolerance = 0.001f;
        const float directionTolerance = 0.9999f;
        if (sourceDoorway.Socket != candidateDoorway.Socket
            || Vector3.SqrMagnitude(
                sourceDoorway.Position
                - candidateDoorway.Position)
                > positionTolerance * positionTolerance
            || Vector3.Dot(
                sourceDoorway.Forward,
                candidateDoorway.Forward)
                < directionTolerance
            || Vector3.Dot(
                sourceDoorway.Up,
                candidateDoorway.Up)
                < directionTolerance)
        {
            return false;
        }

        DoorwayProxy connected = sourceDoorway.ConnectedDoorway;
        if (connected == null)
            return false;
        ProposedConnection proposed =
            new(
                proxyDungeon,
                replacement,
                connected.TileProxy,
                candidateDoorway,
                connected);
        return DungeonFlow.CanDoorwaysConnect(proposed);
    }

    private bool IsInvalidReplacementCollision(
        TileProxy sourceTile,
        TileProxy replacement)
    {
        if (RestrictDungeonToBounds
            && !TilePlacementBounds.Contains(
                replacement.Placement.Bounds))
        {
            return true;
        }

        HashSet<TileProxy> connectedTiles =
            sourceTile.UsedDoorways
                .Select(doorway =>
                    doorway.ConnectedDoorway?.TileProxy)
                .Where(tile => tile != null)
                .ToHashSet();
        foreach (TileProxy other in proxyDungeon.AllTiles)
        {
            if (other == null || other == sourceTile)
                continue;

            bool isConnected = connectedTiles.Contains(other);
            float maximumOverlap =
                isConnected ? OverlapThreshold : -Padding;
            bool overlaps =
                DisallowOverhangs && !isConnected
                    ? replacement.IsOverlappingOrOverhanging(
                        other,
                        UpDirection,
                        maximumOverlap)
                    : replacement.IsOverlapping(
                        other,
                        maximumOverlap);
            if (overlaps)
                return true;
        }

        return false;
    }

    private static TopologyReplacementMatch SelectReplacement(
        IReadOnlyList<TopologyReplacementMatch> matches,
        int seed)
    {
        if (matches == null || matches.Count == 0)
            return null;

        RandomStream random = new(seed);
        List<IGrouping<string, TopologyReplacementMatch>>
            matchesByVariant = matches
                .GroupBy(match => match.PrefabName)
                .OrderBy(
                    group => group.Key,
                    StringComparer.Ordinal)
                .ToList();
        IGrouping<string, TopologyReplacementMatch>
            selectedVariant = matchesByVariant[
                random.Next(0, matchesByVariant.Count)];
        List<TopologyReplacementMatch> rotations =
            selectedVariant
                .OrderBy(match => match.QuarterTurns)
                .ToList();
        return rotations[random.Next(0, rotations.Count)];
    }

    private void ReplaceProxyTile(
        TileProxy source,
        TopologyReplacementMatch selected)
    {
        for (int i = 0; i < selected.Pairs.Count; i++)
        {
            DoorwayReplacementPair pair = selected.Pairs[i];
            ProxyDoorwayConnection connection =
                proxyDungeon.Connections.FirstOrDefault(item =>
                    item.A == pair.Source
                    || item.B == pair.Source);
            if (connection.A == null || connection.B == null)
            {
                throw new InvalidOperationException(
                    "형태 보정 대상 Doorway 연결을 찾지 못했습니다.");
            }

            proxyDungeon.RemoveConnection(connection);
        }

        ReplaceTileListEntry(proxyDungeon.AllTiles, source, selected.Replacement);
        if (source.Placement.IsOnMainPath)
        {
            ReplaceTileListEntry(
                proxyDungeon.MainPathTiles,
                source,
                selected.Replacement);
        }
        else
        {
            ReplaceTileListEntry(
                proxyDungeon.BranchPathTiles,
                source,
                selected.Replacement);
        }

        for (int i = 0; i < selected.Pairs.Count; i++)
        {
            DoorwayReplacementPair pair = selected.Pairs[i];
            proxyDungeon.MakeConnection(
                pair.Replacement,
                pair.Connected);
        }
    }

    private static void ReplaceTileListEntry(
        IList<TileProxy> tiles,
        TileProxy source,
        TileProxy replacement)
    {
        int index = tiles.IndexOf(source);
        if (index < 0)
        {
            throw new InvalidOperationException(
                "형태 보정 대상 TileProxy를 찾지 못했습니다.");
        }

        tiles[index] = replacement;
    }

    private static int BuildRepairSeed(
        int chosenSeed,
        int tileIndex,
        string familyName)
    {
        unchecked
        {
            int value = chosenSeed;
            value = value * 397 ^ tileIndex;
            for (int i = 0; i < familyName.Length; i++)
                value = value * 31 + familyName[i];
            return value;
        }
    }

    private sealed class TopologyReplacementMatch
    {
        public TopologyReplacementMatch(
            TileProxy replacement,
            List<DoorwayReplacementPair> pairs,
            string prefabName,
            int quarterTurns)
        {
            Replacement = replacement;
            Pairs = pairs;
            PrefabName = prefabName;
            QuarterTurns = quarterTurns;
        }

        public TileProxy Replacement { get; }
        public List<DoorwayReplacementPair> Pairs { get; }
        public string PrefabName { get; }
        public int QuarterTurns { get; }
    }

    private readonly struct DoorwayReplacementPair
    {
        public DoorwayReplacementPair(
            DoorwayProxy source,
            DoorwayProxy replacement,
            DoorwayProxy connected)
        {
            Source = source;
            Replacement = replacement;
            Connected = connected;
        }

        public DoorwayProxy Source { get; }
        public DoorwayProxy Replacement { get; }
        public DoorwayProxy Connected { get; }
    }
}
