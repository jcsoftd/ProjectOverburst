using System;

[Serializable]
public sealed class DungeonRunEntryRequest
{
    public int RunSeed { get; private set; }
    public string SourceSceneName { get; private set; }
    public string ReturnPointId { get; private set; }
    public int DifficultyLevel { get; private set; }
    public int RoomCount { get; private set; }
    public int ArenaCount { get; private set; }
    public int TotalMainPathTileCount { get; private set; }

    private DungeonRunEntryRequest(
        int runSeed,
        string sourceSceneName,
        string returnPointId,
        int difficultyLevel,
        int roomCount,
        int arenaCount,
        int totalMainPathTileCount)
    {
        RunSeed = runSeed;
        SourceSceneName = string.IsNullOrWhiteSpace(sourceSceneName)
            ? PersistentSceneFlow.DefaultHubSceneName
            : sourceSceneName;
        ReturnPointId = string.IsNullOrWhiteSpace(returnPointId)
            ? "Default"
            : returnPointId;
        DifficultyLevel = difficultyLevel;
        RoomCount = roomCount;
        ArenaCount = arenaCount;
        TotalMainPathTileCount = totalMainPathTileCount;
    }

    public static DungeonRunEntryRequest Create(
        int runSeed,
        string sourceSceneName,
        string returnPointId = "Default",
        int difficultyLevel = 0,
        int roomCount = 0,
        int arenaCount = -1,
        int totalMainPathTileCount = 0)
    {
        return new DungeonRunEntryRequest(
            runSeed,
            sourceSceneName,
            returnPointId,
            difficultyLevel,
            roomCount,
            arenaCount,
            totalMainPathTileCount);
    }

    public static DungeonRunEntryRequest CreateRandom(
        string sourceSceneName,
        string returnPointId = "Default",
        int difficultyLevel = 0,
        int roomCount = 0,
        int arenaCount = -1,
        int totalMainPathTileCount = 0)
    {
        return Create(
            RunSeedUtility.Create(),
            sourceSceneName,
            returnPointId,
            difficultyLevel,
            roomCount,
            arenaCount,
            totalMainPathTileCount);
    }
}
