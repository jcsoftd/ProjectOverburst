using System;
using System.Collections.Generic;
using System.Linq;
using DunGen;
using UnityEngine;

public sealed class DungeonPlayRoomPlanEntry
{
    public DungeonPlayRoomPlanEntry(
        int index,
        Vector2Int gridSizeCells,
        TileSet tileSet,
        float normalizedPathDepth)
    {
        Index = index;
        GridSizeCells = gridSizeCells;
        TileSet = tileSet;
        NormalizedPathDepth = normalizedPathDepth;
    }

    public int Index { get; }
    public Vector2Int GridSizeCells { get; }
    public TileSet TileSet { get; }
    public float NormalizedPathDepth { get; }
}

public sealed class DungeonPlayRoomPlan
{
    internal DungeonPlayRoomPlan(
        DungeonRunParameters parameters,
        int seed,
        IReadOnlyList<DungeonPlayRoomPlanEntry> entries)
    {
        Parameters = parameters;
        Seed = seed;
        Entries = entries;
    }

    public DungeonRunParameters Parameters { get; }
    public int Seed { get; }
    public IReadOnlyList<DungeonPlayRoomPlanEntry> Entries { get; }
    public int RoomCount => Entries.Count;

    public void AppendRequiredMainPathInjections(
        ref List<InjectedTile> tilesToInject)
    {
        if (tilesToInject == null)
            tilesToInject = new List<InjectedTile>();

        for (int i = 0; i < Entries.Count; i++)
        {
            DungeonPlayRoomPlanEntry entry = Entries[i];
            tilesToInject.Add(
                new InjectedTile(
                    entry.TileSet,
                    true,
                    entry.NormalizedPathDepth,
                    0f,
                    true));
        }
    }
}

public static class DungeonPlayRoomPlanner
{
    public static bool TryCreate(
        DungeonRunDefinition definition,
        DungeonRunParameters parameters,
        int seed,
        out DungeonPlayRoomPlan plan,
        out string error)
    {
        plan = null;
        error = string.Empty;
        if (definition == null || definition.PlayRoomCatalog == null)
        {
            error = "DungeonPlayRoomCatalog 참조가 없습니다.";
            return false;
        }

        List<DungeonPlayRoomCatalogEntry> catalogEntries =
            definition.PlayRoomCatalog.Entries
                .Where(entry =>
                    entry != null
                    && entry.TileSet != null
                    && DungeonPlayRoomAuthoring.IsSupportedGridSize(
                        entry.GridSizeCells))
                .OrderBy(entry => entry.GridSizeCells.x)
                .ThenBy(entry => entry.GridSizeCells.y)
                .ToList();
        int expectedSizeCount = CountSupportedGridSizes();
        if (catalogEntries.Count != expectedSizeCount
            || catalogEntries
                .Select(entry => entry.GridSizeCells)
                .Distinct()
                .Count() != expectedSizeCount)
        {
            error = $"플레이 방 카탈로그 크기 수 불일치: "
                + $"{catalogEntries.Count}/{expectedSizeCount}";
            return false;
        }

        float[] weights = new float[catalogEntries.Count];
        float totalWeight = 0f;
        for (int i = 0; i < catalogEntries.Count; i++)
        {
            weights[i] = definition.GetPlayRoomSizeWeight(
                catalogEntries[i].GridSizeCells,
                parameters.DifficultyLevel);
            totalWeight += weights[i];
        }

        if (totalWeight <= 0f)
        {
            error = "플레이 방 크기 가중치 합이 0입니다.";
            return false;
        }

        List<DungeonPlayRoomPlanEntry> selected = new();
        uint state = Mix(
            unchecked((uint)seed)
            ^ unchecked((uint)parameters.DifficultyLevel * 0x9E3779B9u)
            ^ unchecked((uint)parameters.RoomCount * 0x85EBCA6Bu));
        for (int roomIndex = 0;
             roomIndex < parameters.RoomCount;
             roomIndex++)
        {
            state = Next(state);
            float selection =
                (state / (float)uint.MaxValue) * totalWeight;
            int selectedIndex = weights.Length - 1;
            for (int sizeIndex = 0;
                 sizeIndex < weights.Length;
                 sizeIndex++)
            {
                selection -= weights[sizeIndex];
                if (selection <= 0f)
                {
                    selectedIndex = sizeIndex;
                    break;
                }
            }

            DungeonPlayRoomCatalogEntry catalogEntry =
                catalogEntries[selectedIndex];
            selected.Add(
                new DungeonPlayRoomPlanEntry(
                    roomIndex,
                    catalogEntry.GridSizeCells,
                    catalogEntry.TileSet,
                    (roomIndex + 1f)
                        / (parameters.RoomCount + 1f)));
        }

        plan = new DungeonPlayRoomPlan(parameters, seed, selected);
        return true;
    }

    private static int CountSupportedGridSizes()
    {
        int count = 0;
        for (int width = DungeonPlayRoomAuthoring.MinimumCellCount;
             width <= DungeonPlayRoomAuthoring.MaximumCellCount;
             width++)
        {
            for (int depth = width;
                 depth <= DungeonPlayRoomAuthoring.MaximumCellCount;
                 depth++)
            {
                count++;
            }
        }

        return count;
    }

    private static uint Next(uint value)
    {
        value ^= value << 13;
        value ^= value >> 17;
        value ^= value << 5;
        return value != 0u ? value : 0xA341316Cu;
    }

    private static uint Mix(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return value != 0u ? value : 0x9E3779B9u;
    }
}

public sealed class DungeonPlayRoomNode
{
    public DungeonPlayRoomNode(
        Tile tile,
        DungeonPlayRoomAuthoring room,
        int mainPathIndex)
    {
        Tile = tile;
        Room = room;
        MainPathIndex = mainPathIndex;
    }

    public Tile Tile { get; }
    public DungeonPlayRoomAuthoring Room { get; }
    public int MainPathIndex { get; }
}

public sealed class DungeonPlayRoomLayout
{
    internal DungeonPlayRoomLayout(
        DungeonPlayRoomPlan plan,
        Tile entranceTile,
        Tile exitTile,
        IReadOnlyList<DungeonPlayRoomNode> rooms,
        int entranceConnectorTileCount,
        int exitConnectorTileCount,
        int minimumConnectorTileCountBetweenRooms)
    {
        Plan = plan;
        EntranceTile = entranceTile;
        ExitTile = exitTile;
        Rooms = rooms;
        EntranceConnectorTileCount =
            entranceConnectorTileCount;
        ExitConnectorTileCount = exitConnectorTileCount;
        MinimumConnectorTileCountBetweenRooms =
            minimumConnectorTileCountBetweenRooms;
    }

    public DungeonPlayRoomPlan Plan { get; }
    public Tile EntranceTile { get; }
    public Tile ExitTile { get; }
    public IReadOnlyList<DungeonPlayRoomNode> Rooms { get; }
    public int RoomCount => Rooms.Count;
    public int EntranceConnectorTileCount { get; }
    public int ExitConnectorTileCount { get; }
    public int MinimumConnectorTileCountBetweenRooms { get; }
    public int MinimumConnectorTileCount =>
        Mathf.Min(
            EntranceConnectorTileCount,
            Mathf.Min(
                ExitConnectorTileCount,
                MinimumConnectorTileCountBetweenRooms));
}

public static class DungeonPlayRoomLayoutResolver
{
    public static bool TryResolve(
        Dungeon dungeon,
        DungeonPlayRoomPlan plan,
        out DungeonPlayRoomLayout layout,
        out string error)
    {
        layout = null;
        error = string.Empty;
        if (dungeon == null
            || dungeon.MainPathTiles == null
            || dungeon.MainPathTiles.Count < 2
            || plan == null)
        {
            error = "Dungeon, Main Path 또는 플레이 방 계획이 없습니다.";
            return false;
        }

        List<DungeonPlayRoomNode> rooms = new();
        for (int i = 0; i < dungeon.AllTiles.Count; i++)
        {
            Tile tile = dungeon.AllTiles[i];
            if (tile == null)
                continue;

            DungeonPlayRoomAuthoring[] authorings =
                tile.GetComponentsInChildren<
                    DungeonPlayRoomAuthoring>(true);
            if (authorings.Length == 0)
                continue;
            if (authorings.Length != 1)
            {
                error = $"{tile.name}: 플레이 방 Authoring 수="
                    + authorings.Length;
                return false;
            }

            if (!tile.Placement.IsOnMainPath
                || tile.Placement.InjectionData == null
                || !tile.Placement.InjectionData.IsRequired)
            {
                error = $"{tile.name}: 필수 Main Path 주입 방이 아닙니다.";
                return false;
            }

            int mainPathIndex =
                dungeon.MainPathTiles.IndexOf(tile);
            if (mainPathIndex < 0)
            {
                error =
                    $"{tile.name}: Main Path 인덱스를 찾지 못했습니다.";
                return false;
            }

            rooms.Add(
                new DungeonPlayRoomNode(
                    tile,
                    authorings[0],
                    mainPathIndex));
        }

        rooms = rooms
            .OrderBy(node => node.MainPathIndex)
            .ThenBy(node => node.Tile.name, StringComparer.Ordinal)
            .ToList();
        if (rooms.Count != plan.RoomCount)
        {
            error = $"생성 플레이 방 수 불일치: "
                + $"{rooms.Count}/{plan.RoomCount}";
            return false;
        }

        Dictionary<Vector2Int, int> expected =
            BuildSizeHistogram(
                plan.Entries.Select(entry => entry.GridSizeCells));
        Dictionary<Vector2Int, int> actual =
            BuildSizeHistogram(
                rooms.Select(node => node.Room.GridSizeCells));
        if (expected.Count != actual.Count
            || expected.Any(pair =>
                !actual.TryGetValue(pair.Key, out int count)
                || count != pair.Value))
        {
            error = "계획과 생성 결과의 플레이 방 크기 분포가 다릅니다.";
            return false;
        }

        Tile entrance = dungeon.MainPathTiles[0];
        Tile exit =
            dungeon.MainPathTiles[dungeon.MainPathTiles.Count - 1];
        HashSet<Tile> reachable = BuildReachableTiles(dungeon, entrance);
        if (rooms.Any(node => !reachable.Contains(node.Tile)))
        {
            error = "입구에서 도달할 수 없는 플레이 방이 있습니다.";
            return false;
        }

        int entranceConnectorTileCount =
            rooms[0].MainPathIndex - 1;
        int exitConnectorTileCount =
            dungeon.MainPathTiles.Count
                - rooms[rooms.Count - 1].MainPathIndex
                - 2;
        int minimumConnectorTileCountBetweenRooms =
            int.MaxValue;
        for (int roomIndex = 1;
             roomIndex < rooms.Count;
             roomIndex++)
        {
            int connectorTileCount =
                rooms[roomIndex].MainPathIndex
                - rooms[roomIndex - 1].MainPathIndex
                - 1;
            minimumConnectorTileCountBetweenRooms =
                Mathf.Min(
                    minimumConnectorTileCountBetweenRooms,
                    connectorTileCount);
        }

        if (minimumConnectorTileCountBetweenRooms
            == int.MaxValue)
        {
            minimumConnectorTileCountBetweenRooms =
                Mathf.Min(
                    entranceConnectorTileCount,
                    exitConnectorTileCount);
        }

        int requiredConnectorTileCount =
            DungeonRunDefinition.MinimumConnectorTilesPerGap;
        if (entranceConnectorTileCount
                < requiredConnectorTileCount
            || exitConnectorTileCount
                < requiredConnectorTileCount
            || minimumConnectorTileCountBetweenRooms
                < requiredConnectorTileCount)
        {
            error =
                "PlayRoom 사이 연결 통로가 부족합니다. "
                + $"Entrance={entranceConnectorTileCount}, "
                + $"Between={minimumConnectorTileCountBetweenRooms}, "
                + $"Exit={exitConnectorTileCount}, "
                + $"Required={requiredConnectorTileCount}";
            return false;
        }

        layout = new DungeonPlayRoomLayout(
            plan,
            entrance,
            exit,
            rooms,
            entranceConnectorTileCount,
            exitConnectorTileCount,
            minimumConnectorTileCountBetweenRooms);
        return true;
    }

    private static Dictionary<Vector2Int, int> BuildSizeHistogram(
        IEnumerable<Vector2Int> sizes)
    {
        Dictionary<Vector2Int, int> result = new();
        foreach (Vector2Int size in sizes)
        {
            if (result.TryGetValue(size, out int count))
                result[size] = count + 1;
            else
                result.Add(size, 1);
        }

        return result;
    }

    private static HashSet<Tile> BuildReachableTiles(
        Dungeon dungeon,
        Tile entrance)
    {
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
            if (a == null
                || b == null
                || !adjacency.ContainsKey(a)
                || !adjacency.ContainsKey(b))
            {
                continue;
            }

            adjacency[a].Add(b);
            adjacency[b].Add(a);
        }

        HashSet<Tile> visited = new();
        Queue<Tile> queue = new();
        visited.Add(entrance);
        queue.Enqueue(entrance);
        while (queue.Count > 0)
        {
            Tile current = queue.Dequeue();
            foreach (Tile next in adjacency[current])
            {
                if (visited.Add(next))
                    queue.Enqueue(next);
            }
        }

        return visited;
    }
}
