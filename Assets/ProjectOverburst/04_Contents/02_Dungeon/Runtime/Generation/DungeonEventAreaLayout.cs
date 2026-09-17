using System;
using System.Collections.Generic;
using System.Linq;
using DunGen;
using UnityEngine;

public sealed class DungeonEventAreaNode
{
    public DungeonEventAreaNode(
        Tile tile,
        DungeonEventAreaAuthoring area)
    {
        Tile = tile;
        Area = area;
    }

    public Tile Tile { get; }
    public DungeonEventAreaAuthoring Area { get; }
}

public sealed class DungeonEventAreaLayout
{
    internal DungeonEventAreaLayout(
        DungeonRunParameters parameters,
        Tile entranceTile,
        Tile exitTile,
        IReadOnlyList<DungeonEventAreaAuthoring> candidates,
        IReadOnlyList<DungeonEventAreaNode> selectedNodes,
        IReadOnlyList<DoorwayConnection> doorwayConnections)
    {
        Parameters = parameters;
        EntranceTile = entranceTile;
        ExitTile = exitTile;
        Candidates = candidates;
        SelectedNodes = selectedNodes;
        DoorwayConnections = doorwayConnections;
    }

    public DungeonRunParameters Parameters { get; }
    public Tile EntranceTile { get; }
    public Tile ExitTile { get; }
    public IReadOnlyList<DungeonEventAreaAuthoring> Candidates { get; }
    public IReadOnlyList<DungeonEventAreaNode> SelectedNodes { get; }
    public IReadOnlyList<DoorwayConnection> DoorwayConnections { get; }
    public int GameplayRoomCount => SelectedNodes.Count;
}

public static class DungeonEventAreaLayoutResolver
{
    public static bool TryResolve(
        Dungeon dungeon,
        DungeonRunDefinition definition,
        DungeonRunParameters parameters,
        int seed,
        out DungeonEventAreaLayout layout,
        out string error)
    {
        layout = null;
        error = string.Empty;
        if (dungeon == null
            || dungeon.AllTiles == null
            || dungeon.MainPathTiles == null
            || dungeon.MainPathTiles.Count < 2)
        {
            error = "Dungeon 또는 Main Path가 유효하지 않습니다.";
            return false;
        }

        Tile entranceTile = dungeon.MainPathTiles[0];
        Tile exitTile =
            dungeon.MainPathTiles[dungeon.MainPathTiles.Count - 1];
        Dictionary<Tile, DungeonEventAreaAuthoring[]> candidatesByTile =
            new();
        List<DungeonEventAreaAuthoring> allCandidates = new();
        for (int i = 0; i < dungeon.AllTiles.Count; i++)
        {
            Tile tile = dungeon.AllTiles[i];
            if (tile == null)
                continue;

            DungeonEventAreaAuthoring[] candidates = tile
                .GetComponentsInChildren<DungeonEventAreaAuthoring>(true)
                .Where(candidate => candidate != null)
                .OrderBy(candidate => candidate.AreaId, StringComparer.Ordinal)
                .ToArray();
            if (candidates.Length == 0)
                continue;

            candidatesByTile[tile] = candidates;
            allCandidates.AddRange(candidates);
        }

        List<Tile> eligibleTiles = candidatesByTile.Keys
            .Where(tile => tile != entranceTile && tile != exitTile)
            .ToList();
        if (eligibleTiles.Count < parameters.RoomCount)
        {
            error = $"게임플레이 영역 후보 부족: "
                + $"{eligibleTiles.Count}/{parameters.RoomCount}";
            return false;
        }

        List<Tile> selectedTiles = SelectGameplayTiles(
            eligibleTiles,
            parameters.RoomCount,
            seed);
        if (selectedTiles.Count != parameters.RoomCount)
        {
            error = $"게임플레이 영역 선택 수 불일치: "
                + $"{selectedTiles.Count}/{parameters.RoomCount}";
            return false;
        }

        List<DungeonEventAreaNode> selectedNodes = new();
        for (int i = 0; i < selectedTiles.Count; i++)
        {
            Tile tile = selectedTiles[i];
            selectedNodes.Add(
                new DungeonEventAreaNode(
                    tile,
                    SelectArea(
                        candidatesByTile[tile],
                        definition,
                        parameters,
                        seed,
                        tile)));
        }

        if (!AreSelectedTilesReachable(
                dungeon,
                entranceTile,
                selectedNodes,
                out error))
        {
            return false;
        }

        layout = new DungeonEventAreaLayout(
            parameters,
            entranceTile,
            exitTile,
            allCandidates,
            selectedNodes,
            dungeon.Connections);
        return true;
    }

    private static List<Tile> SelectGameplayTiles(
        IReadOnlyList<Tile> eligibleTiles,
        int roomCount,
        int seed)
    {
        List<Tile> mainTiles = eligibleTiles
            .Where(tile => tile.Placement.IsOnMainPath)
            .OrderBy(tile => tile.Placement.PathDepth)
            .ThenBy(tile => tile.name, StringComparer.Ordinal)
            .ToList();
        List<Tile> branchTiles = eligibleTiles
            .Where(tile => !tile.Placement.IsOnMainPath)
            .OrderBy(tile => StableHash(seed, tile, null))
            .ToList();

        int desiredBranchCount =
            branchTiles.Count > 0 && roomCount >= 4
                ? Mathf.Min(branchTiles.Count, Mathf.Max(1, roomCount / 4))
                : 0;
        int mainCount = Mathf.Min(
            mainTiles.Count,
            roomCount - desiredBranchCount);
        int branchCount = Mathf.Min(
            branchTiles.Count,
            roomCount - mainCount);
        if (mainCount + branchCount < roomCount)
        {
            mainCount = Mathf.Min(
                mainTiles.Count,
                roomCount - branchCount);
        }

        List<Tile> selected = SelectEvenly(mainTiles, mainCount, seed);
        selected.AddRange(branchTiles.Take(branchCount));
        if (selected.Count < roomCount)
        {
            HashSet<Tile> selectedSet = new(selected);
            selected.AddRange(
                eligibleTiles
                    .Where(tile => !selectedSet.Contains(tile))
                    .OrderBy(tile => StableHash(seed ^ 0x2f6e2b1, tile, null))
                    .Take(roomCount - selected.Count));
        }

        return selected;
    }

    private static List<Tile> SelectEvenly(
        IReadOnlyList<Tile> source,
        int count,
        int seed)
    {
        List<Tile> remaining = new(source);
        List<Tile> result = new();
        for (int slot = 0; slot < count && remaining.Count > 0; slot++)
        {
            float target = (slot + 1f) / (count + 1f);
            Tile selected = remaining
                .OrderBy(tile =>
                    Mathf.Abs(
                        tile.Placement.NormalizedDepth - target))
                .ThenBy(tile => StableHash(seed + slot, tile, null))
                .First();
            result.Add(selected);
            remaining.Remove(selected);
        }

        return result;
    }

    private static DungeonEventAreaAuthoring SelectArea(
        IReadOnlyList<DungeonEventAreaAuthoring> candidates,
        DungeonRunDefinition definition,
        DungeonRunParameters parameters,
        int seed,
        Tile tile)
    {
        if (candidates.Count == 1)
            return candidates[0];

        float totalWeight = 0f;
        float[] weights = new float[candidates.Count];
        for (int i = 0; i < candidates.Count; i++)
        {
            weights[i] = definition.GetPlayRoomSizeWeight(
                candidates[i].DimensionsMeters,
                parameters.DifficultyLevel);
            totalWeight += weights[i];
        }

        uint hash = StableHash(seed, tile, "AreaSize");
        float selection = hash / (float)uint.MaxValue * totalWeight;
        for (int i = 0; i < candidates.Count; i++)
        {
            selection -= weights[i];
            if (selection <= 0f)
                return candidates[i];
        }

        return candidates[candidates.Count - 1];
    }

    private static bool AreSelectedTilesReachable(
        Dungeon dungeon,
        Tile entranceTile,
        IReadOnlyList<DungeonEventAreaNode> selectedNodes,
        out string error)
    {
        error = string.Empty;
        Dictionary<Tile, List<Tile>> adjacency = new();
        for (int i = 0; i < dungeon.AllTiles.Count; i++)
        {
            Tile tile = dungeon.AllTiles[i];
            if (tile != null)
                adjacency[tile] = new List<Tile>();
        }

        for (int i = 0; i < dungeon.Connections.Count; i++)
        {
            DoorwayConnection connection = dungeon.Connections[i];
            Tile a = connection?.A?.Tile;
            Tile b = connection?.B?.Tile;
            if (a == null || b == null)
                continue;
            adjacency[a].Add(b);
            adjacency[b].Add(a);
        }

        HashSet<Tile> visited = new();
        Queue<Tile> queue = new();
        visited.Add(entranceTile);
        queue.Enqueue(entranceTile);
        while (queue.Count > 0)
        {
            Tile current = queue.Dequeue();
            List<Tile> neighbors = adjacency[current];
            for (int i = 0; i < neighbors.Count; i++)
            {
                Tile neighbor = neighbors[i];
                if (visited.Add(neighbor))
                    queue.Enqueue(neighbor);
            }
        }

        for (int i = 0; i < selectedNodes.Count; i++)
        {
            if (!visited.Contains(selectedNodes[i].Tile))
            {
                error = "선택 EventArea가 입구에서 도달 불가능합니다: "
                    + selectedNodes[i].Tile.name;
                return false;
            }
        }

        return true;
    }

    private static uint StableHash(
        int seed,
        Tile tile,
        string suffix)
    {
        unchecked
        {
            uint hash = 2166136261u;
            hash = (hash ^ (uint)seed) * 16777619u;
            string value = tile != null
                ? tile.name
                    + "|"
                    + tile.Placement.PathDepth
                    + "|"
                    + tile.Placement.Depth
                    + "|"
                    + suffix
                : suffix ?? string.Empty;
            for (int i = 0; i < value.Length; i++)
                hash = (hash ^ value[i]) * 16777619u;
            return hash;
        }
    }
}
