using DunGen;

public static class DungeonEndCapContract
{
    public static int CountOpenDoorways(Dungeon dungeon)
    {
        if (dungeon?.AllTiles == null)
            return 0;

        int count = 0;
        for (int i = 0; i < dungeon.AllTiles.Count; i++)
        {
            Tile tile = dungeon.AllTiles[i];
            if (tile?.UnusedDoorways != null)
                count += tile.UnusedDoorways.Count;
        }

        return count;
    }

    public static int CountNonEndCapBranchTiles(
        Dungeon dungeon,
        TileSet endCapTileSet)
    {
        if (dungeon?.BranchPathTiles == null)
            return 0;
        if (endCapTileSet == null)
            return dungeon.BranchPathTiles.Count;

        int count = 0;
        for (int i = 0; i < dungeon.BranchPathTiles.Count; i++)
        {
            Tile tile = dungeon.BranchPathTiles[i];
            if (tile?.Placement?.TileSet != endCapTileSet)
                count++;
        }

        return count;
    }

    public static int CountPlacedEndCaps(
        Dungeon dungeon,
        TileSet endCapTileSet)
    {
        if (dungeon?.BranchPathTiles == null
            || endCapTileSet == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < dungeon.BranchPathTiles.Count; i++)
        {
            Tile tile = dungeon.BranchPathTiles[i];
            if (tile?.Placement?.TileSet == endCapTileSet)
                count++;
        }

        return count;
    }

    public static int CountTopologyRepairTiles(Dungeon dungeon)
    {
        if (dungeon?.AllTiles == null)
            return 0;

        int count = 0;
        for (int i = 0; i < dungeon.AllTiles.Count; i++)
        {
            Tile tile = dungeon.AllTiles[i];
            if (tile != null
                && tile.GetComponent<
                    DungeonTopologyRepairCandidateMarker>() != null)
            {
                count++;
            }
        }

        return count;
    }

    public static bool TryValidatePlacedEndCaps(
        Dungeon dungeon,
        TileSet endCapTileSet,
        out int endCapCount)
    {
        endCapCount = 0;
        if (dungeon?.BranchPathTiles == null)
            return false;
        if (endCapTileSet == null)
            return dungeon.BranchPathTiles.Count == 0;

        for (int i = 0; i < dungeon.BranchPathTiles.Count; i++)
        {
            Tile tile = dungeon.BranchPathTiles[i];
            if (tile == null || tile.Placement == null)
                return false;

            bool isEndCap =
                tile.Placement.TileSet == endCapTileSet;
            if (isEndCap)
            {
                endCapCount++;
                if (tile.Placement.IsOnMainPath
                    || tile.AllDoorways == null
                    || tile.AllDoorways.Count != 1
                    || tile.UsedDoorways == null
                    || tile.UsedDoorways.Count != 1)
                {
                    return false;
                }
            }

        }

        return true;
    }

    public static bool TryValidateFinalOpenDoorwayPass(
        Dungeon dungeon,
        DungeonRunGenerator generator,
        TileSet endCapTileSet,
        out string error)
    {
        error = string.Empty;
        if (!TryValidatePlacedEndCaps(
                dungeon,
                endCapTileSet,
                out int placedEndCapCount))
        {
            error = "EndCap 배치 계약 오류";
            return false;
        }

        if (generator == null || !generator.EndCapPassCompleted)
        {
            error = "최종 오픈 Doorway EndCap 패스 미완료";
            return false;
        }

        if (!generator.TopologyRepairPassCompleted)
        {
            error = "EndCap 이후 형태 보정 패스 미완료";
            return false;
        }

        int openDoorwayCount = CountOpenDoorways(dungeon);
        int topologyRepairTileCount =
            CountTopologyRepairTiles(dungeon);
        int expectedFinalEndCapCount =
            generator.PlacedEndCapCount
            + generator.ReplacedWithEndCapCount;
        if (placedEndCapCount != expectedFinalEndCapCount
            || generator.OpenDoorwayCountBeforeEndCaps
                - generator.PlacedEndCapCount
                != generator.OpenDoorwayCountAfterEndCaps)
        {
            error =
                "최종 오픈 Doorway EndCap 패스 수치 불일치: "
                + $"Open={generator.OpenDoorwayCountBeforeEndCaps}"
                + $"->{generator.OpenDoorwayCountAfterEndCaps}, "
                + "EndCap="
                + $"{generator.PlacedEndCapCount}"
                + $"+{generator.ReplacedWithEndCapCount}/"
                + $"{placedEndCapCount}";
            return false;
        }

        if (generator.OpenDoorwayCountBeforeTopologyRepair
                != generator.OpenDoorwayCountAfterEndCaps
            || generator.OpenDoorwayCountBeforeTopologyRepair
                - generator.RemovedOpenDoorwayCount
                != generator.OpenDoorwayCountAfterTopologyRepair
            || openDoorwayCount
                != generator.OpenDoorwayCountAfterTopologyRepair
            || topologyRepairTileCount
                != generator.ReplacedShapeTileCount
            || generator.ReplacedTopologyTileCount
                != generator.ReplacedWithEndCapCount
                    + generator.ReplacedShapeTileCount)
        {
            error =
                "형태 보정 패스 수치 불일치: "
                + "Open="
                + $"{generator.OpenDoorwayCountBeforeTopologyRepair}"
                + $"->{generator.OpenDoorwayCountAfterTopologyRepair}, "
                + "Removed="
                + $"{generator.RemovedOpenDoorwayCount}, "
                + "Replaced="
                + $"{generator.ReplacedTopologyTileCount}/"
                + "EndCap "
                + $"{generator.ReplacedWithEndCapCount}, "
                + "Shape "
                + $"{generator.ReplacedShapeTileCount}/"
                + topologyRepairTileCount;
            return false;
        }

        return true;
    }

}
