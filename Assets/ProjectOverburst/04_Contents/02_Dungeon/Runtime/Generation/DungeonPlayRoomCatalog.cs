using System;
using System.Collections.Generic;
using DunGen;
using UnityEngine;

[Serializable]
public sealed class DungeonPlayRoomCatalogEntry
{
    [SerializeField] private Vector2Int gridSizeCells =
        new(
            DungeonPlayRoomAuthoring.MinimumCellCount,
            DungeonPlayRoomAuthoring.MinimumCellCount);
    [SerializeField] private TileSet tileSet;

    public Vector2Int GridSizeCells =>
        DungeonPlayRoomAuthoring.NormalizeGridSize(gridSizeCells);
    public TileSet TileSet => tileSet;

    public void Configure(
        Vector2Int configuredGridSizeCells,
        TileSet configuredTileSet)
    {
        gridSizeCells = DungeonPlayRoomAuthoring.NormalizeGridSize(
            configuredGridSizeCells);
        tileSet = configuredTileSet;
    }
}

[CreateAssetMenu(
    fileName = "DungeonPlayRoomCatalog",
    menuName = "OVERBURST/World/Dungeon Play Room Catalog")]
public sealed class DungeonPlayRoomCatalog : ScriptableObject
{
    [SerializeField]
    private List<DungeonPlayRoomCatalogEntry> entries = new();

    public IReadOnlyList<DungeonPlayRoomCatalogEntry> Entries => entries;

    public bool TryGetTileSet(
        Vector2Int gridSizeCells,
        out TileSet tileSet)
    {
        Vector2Int normalized =
            DungeonPlayRoomAuthoring.NormalizeGridSize(gridSizeCells);
        for (int i = 0; i < entries.Count; i++)
        {
            DungeonPlayRoomCatalogEntry entry = entries[i];
            if (entry != null
                && entry.GridSizeCells == normalized
                && entry.TileSet != null)
            {
                tileSet = entry.TileSet;
                return true;
            }
        }

        tileSet = null;
        return false;
    }

    public void Configure(
        IReadOnlyList<Vector2Int> gridSizes,
        IReadOnlyList<TileSet> tileSets)
    {
        if (gridSizes == null
            || tileSets == null
            || gridSizes.Count != tileSets.Count)
        {
            throw new ArgumentException(
                "플레이 방 크기와 TileSet 수가 일치해야 합니다.");
        }

        entries.Clear();
        for (int i = 0; i < gridSizes.Count; i++)
        {
            DungeonPlayRoomCatalogEntry entry = new();
            entry.Configure(gridSizes[i], tileSets[i]);
            entries.Add(entry);
        }
    }
}
