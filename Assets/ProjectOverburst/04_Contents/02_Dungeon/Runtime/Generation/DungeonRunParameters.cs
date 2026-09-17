using UnityEngine;

public readonly struct DungeonRunParameters
{
    public const int MinimumDifficultyLevel = 1;
    public const int MaximumDifficultyLevel = 30;
    public const int MinimumRoomCount = 1;
    public const int MaximumRoomCount = 30;
    public const int MinimumArenaCount = 0;
    public const int MaximumArenaCount = 30;
    public const int MinimumTotalMainPathTileCount = 2;
    public const int MaximumTotalMainPathTileCount = 100;

    public DungeonRunParameters(
        int difficultyLevel,
        int roomCount,
        int arenaCount,
        int totalMainPathTileCount)
    {
        DifficultyLevel = Mathf.Clamp(
            difficultyLevel,
            MinimumDifficultyLevel,
            MaximumDifficultyLevel);
        RoomCount = Mathf.Clamp(
            roomCount,
            MinimumRoomCount,
            MaximumRoomCount);
        ArenaCount = Mathf.Clamp(
            arenaCount,
            MinimumArenaCount,
            MaximumArenaCount);
        TotalMainPathTileCount = Mathf.Clamp(
            totalMainPathTileCount,
            Mathf.Max(
                MinimumTotalMainPathTileCount,
                ArenaCount + 2),
            MaximumTotalMainPathTileCount);
    }

    public int DifficultyLevel { get; }
    public int RoomCount { get; }
    public int ArenaCount { get; }
    public int TotalMainPathTileCount { get; }
    public float NormalizedDifficulty => Mathf.InverseLerp(
        MinimumDifficultyLevel,
        MaximumDifficultyLevel,
        DifficultyLevel);
}
