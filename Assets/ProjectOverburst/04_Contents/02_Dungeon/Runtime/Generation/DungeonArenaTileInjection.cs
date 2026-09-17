using System;
using System.Collections.Generic;
using DunGen;

public sealed class DungeonArenaTileInjection : IDisposable
{
    private DungeonGenerator generator;

    public DungeonArenaTileInjection(
        DungeonGenerator generator,
        TileSet arenaTileSet,
        int arenaCount)
    {
        this.generator = generator
            ?? throw new ArgumentNullException(nameof(generator));
        ArenaTileSet = arenaTileSet;
        ArenaCount = Math.Max(
            DungeonRunParameters.MinimumArenaCount,
            Math.Min(
                DungeonRunParameters.MaximumArenaCount,
                arenaCount));

        if (ArenaCount > 0 && ArenaTileSet == null)
        {
            throw new ArgumentNullException(
                nameof(arenaTileSet),
                "Arena 개수가 1 이상이면 Arena TileSet이 필요합니다.");
        }

        if (ArenaCount > 0)
            generator.TileInjectionMethods += AppendRequiredArenas;
    }

    public TileSet ArenaTileSet { get; }
    public int ArenaCount { get; }

    public static int CountPlacedArenas(
        Dungeon dungeon,
        TileSet arenaTileSet)
    {
        if (dungeon?.AllTiles == null || arenaTileSet == null)
            return 0;

        int count = 0;
        for (int i = 0; i < dungeon.AllTiles.Count; i++)
        {
            Tile tile = dungeon.AllTiles[i];
            if (tile?.Placement?.TileSet == arenaTileSet)
                count++;
        }

        return count;
    }

    public static bool AreAllPlacedArenasOnMainPath(
        Dungeon dungeon,
        TileSet arenaTileSet)
    {
        if (dungeon?.AllTiles == null || arenaTileSet == null)
            return true;

        for (int i = 0; i < dungeon.AllTiles.Count; i++)
        {
            Tile tile = dungeon.AllTiles[i];
            if (tile?.Placement?.TileSet == arenaTileSet
                && !tile.Placement.IsOnMainPath)
            {
                return false;
            }
        }

        return true;
    }

    public void Dispose()
    {
        if (generator == null)
            return;

        if (ArenaCount > 0)
            generator.TileInjectionMethods -= AppendRequiredArenas;
        generator = null;
    }

    private void AppendRequiredArenas(
        RandomStream randomStream,
        ref List<InjectedTile> tilesToInject)
    {
        if (tilesToInject == null)
            tilesToInject = new List<InjectedTile>();

        for (int index = 0; index < ArenaCount; index++)
        {
            float normalizedPathDepth =
                (index + 1f) / (ArenaCount + 1f);
            tilesToInject.Add(
                new InjectedTile(
                    ArenaTileSet,
                    true,
                    normalizedPathDepth,
                    0f,
                    true));
        }
    }
}
