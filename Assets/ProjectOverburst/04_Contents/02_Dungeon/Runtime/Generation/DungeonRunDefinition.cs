using DunGen;
using DunGen.Graph;
using UnityEngine;

[CreateAssetMenu(
    fileName = "DungeonRunDefinition",
    menuName = "OVERBURST/World/Dungeon Run Definition")]
public sealed class DungeonRunDefinition : ScriptableObject
{
    public const int MinimumConnectorTilesPerGap = 1;

    [SerializeField] private DungeonFlow dungeonFlow;
    [SerializeField] private DungeonPlayRoomCatalog playRoomCatalog;
    [SerializeField] private int defaultSeed = 20260726;
    [SerializeField, Range(1, 100)] private int maxAttemptCount = 100;
    [SerializeField, Range(1f, 12f)] private float maxAsyncFrameMilliseconds = 5f;
    [SerializeField, Min(5f)] private float generationTimeoutSeconds = 45f;
    [Header("Run Scale")]
    [SerializeField, Range(
        DungeonRunParameters.MinimumDifficultyLevel,
        DungeonRunParameters.MaximumDifficultyLevel)]
    private int defaultDifficultyLevel = 1;
    [SerializeField, Range(
        DungeonRunParameters.MinimumRoomCount,
        DungeonRunParameters.MaximumRoomCount)]
    private int defaultRoomCount = 8;
    [Header("Arena Placement")]
    [SerializeField] private TileSet arenaTileSet;
    [SerializeField] private TileSet endCapTileSet;
    [SerializeField] private DungeonTopologyRepairCatalog
        topologyRepairCatalog;
    [Tooltip(
        "메인 경로에 반드시 배치할 Arena 타일 수입니다. "
        + "0이면 Arena를 배치하지 않습니다.")]
    [SerializeField, Range(
        DungeonRunParameters.MinimumArenaCount,
        DungeonRunParameters.MaximumArenaCount)]
    private int defaultArenaCount = 4;
    [Tooltip(
        "Start, Exit, Bridge, Arena를 모두 포함한 최종 Main Path 타일 수입니다.")]
    [SerializeField, Range(
        DungeonRunParameters.MinimumTotalMainPathTileCount,
        DungeonRunParameters.MaximumTotalMainPathTileCount)]
    private int defaultTotalMainPathTileCount = 24;
    [SerializeField, Range(0.05f, 1f)]
    private float minimumDifficultyLargeRoomWeight = 0.25f;
    [SerializeField, Range(1f, 4f)]
    private float maximumDifficultyLargeRoomWeight = 4f;
    [Header("Difficulty Length Scale")]
    [Tooltip("난이도 1에서 공급사 Flow 길이에 적용할 배율입니다.")]
    [SerializeField, Range(0.5f, 4f)]
    private float difficulty1LengthMultiplier = 2f;
    [Tooltip("난이도 30에서 공급사 Flow 길이에 적용할 배율입니다.")]
    [SerializeField, Range(0.5f, 4f)]
    private float difficulty30LengthMultiplier = 2f;
    [Header("Layout Quality")]
    [SerializeField, Range(1, 10)]
    private int layoutCandidateCount =
        DungeonLayoutQualityEvaluator.DefaultCandidateCount;
    [SerializeField, Min(0)]
    private int minimumBranchTileCount =
        DungeonLayoutQualityEvaluator.DefaultMinimumBranchTileCount;
    [SerializeField, Range(1f, 6f)]
    private float maximumPlanarAspectRatio =
        DungeonLayoutQualityEvaluator.DefaultMaximumPlanarAspectRatio;

    public DungeonFlow DungeonFlow => dungeonFlow;
    public DungeonPlayRoomCatalog PlayRoomCatalog => playRoomCatalog;
    public int DefaultSeed => defaultSeed;
    public int MaxAttemptCount => Mathf.Clamp(maxAttemptCount, 1, 100);
    public float MaxAsyncFrameMilliseconds =>
        Mathf.Clamp(maxAsyncFrameMilliseconds, 1f, 12f);
    public float GenerationTimeoutSeconds =>
        Mathf.Max(5f, generationTimeoutSeconds);
    public int DefaultDifficultyLevel => Mathf.Clamp(
        defaultDifficultyLevel,
        DungeonRunParameters.MinimumDifficultyLevel,
        DungeonRunParameters.MaximumDifficultyLevel);
    public int DefaultRoomCount => Mathf.Clamp(
        defaultRoomCount,
        DungeonRunParameters.MinimumRoomCount,
        DungeonRunParameters.MaximumRoomCount);
    public TileSet ArenaTileSet => arenaTileSet;
    public TileSet EndCapTileSet => endCapTileSet;
    public DungeonTopologyRepairCatalog TopologyRepairCatalog =>
        topologyRepairCatalog;
    public int DefaultArenaCount => Mathf.Clamp(
        defaultArenaCount,
        DungeonRunParameters.MinimumArenaCount,
        DungeonRunParameters.MaximumArenaCount);
    public int DefaultTotalMainPathTileCount => Mathf.Clamp(
        defaultTotalMainPathTileCount,
        Mathf.Max(
            DungeonRunParameters.MinimumTotalMainPathTileCount,
            DefaultArenaCount + 2),
        DungeonRunParameters.MaximumTotalMainPathTileCount);
    public float Difficulty1LengthMultiplier =>
        Mathf.Clamp(difficulty1LengthMultiplier, 0.5f, 4f);
    public float Difficulty30LengthMultiplier =>
        Mathf.Clamp(difficulty30LengthMultiplier, 0.5f, 4f);
    public int LayoutCandidateCount =>
        Mathf.Clamp(layoutCandidateCount, 1, 10);
    public int MinimumBranchTileCount =>
        Mathf.Max(0, minimumBranchTileCount);
    public float MaximumPlanarAspectRatio =>
        Mathf.Clamp(maximumPlanarAspectRatio, 1f, 6f);

    public DungeonRunParameters ResolveParameters(
        int requestedDifficultyLevel,
        int requestedRoomCount,
        int requestedArenaCount = -1,
        int requestedTotalMainPathTileCount = 0)
    {
        int difficulty = requestedDifficultyLevel > 0
            ? requestedDifficultyLevel
            : DefaultDifficultyLevel;
        int roomCount = requestedRoomCount > 0
            ? requestedRoomCount
            : DefaultRoomCount;
        int arenaCount = requestedArenaCount >= 0
            ? requestedArenaCount
            : DefaultArenaCount;
        int totalMainPathTileCount =
            requestedTotalMainPathTileCount > 0
                ? requestedTotalMainPathTileCount
                : DefaultTotalMainPathTileCount;
        return new DungeonRunParameters(
            difficulty,
            roomCount,
            arenaCount,
            totalMainPathTileCount);
    }

    public float GetDungeonLengthMultiplier(
        DungeonRunParameters parameters)
    {
        return Mathf.Lerp(
            Difficulty1LengthMultiplier,
            Difficulty30LengthMultiplier,
            parameters.NormalizedDifficulty);
    }

    public int GetRequiredMainPathTileCount(
        DungeonRunParameters parameters)
    {
        int roomCount = Mathf.Max(0, parameters.RoomCount);
        int connectorGapCount = roomCount + 1;
        return Mathf.Max(
            2,
            2
                + roomCount
                + connectorGapCount
                    * MinimumConnectorTilesPerGap);
    }

    public float GetPlayRoomSizeWeight(
        Vector2Int gridSizeCells,
        int difficultyLevel)
    {
        Vector2Int normalized =
            DungeonPlayRoomAuthoring.NormalizeGridSize(gridSizeCells);
        float area = Mathf.Clamp(
            normalized.x * normalized.y,
            DungeonPlayRoomAuthoring.MinimumCellCount
                * DungeonPlayRoomAuthoring.MinimumCellCount,
            DungeonPlayRoomAuthoring.MaximumCellCount
                * DungeonPlayRoomAuthoring.MaximumCellCount);
        float area01 = Mathf.InverseLerp(
            DungeonPlayRoomAuthoring.MinimumCellCount
                * DungeonPlayRoomAuthoring.MinimumCellCount,
            DungeonPlayRoomAuthoring.MaximumCellCount
                * DungeonPlayRoomAuthoring.MaximumCellCount,
            area);
        float difficulty01 = Mathf.InverseLerp(
            DungeonRunParameters.MinimumDifficultyLevel,
            DungeonRunParameters.MaximumDifficultyLevel,
            Mathf.Clamp(
                difficultyLevel,
                DungeonRunParameters.MinimumDifficultyLevel,
                DungeonRunParameters.MaximumDifficultyLevel));
        float lowWeight = Mathf.Clamp(
            minimumDifficultyLargeRoomWeight,
            0.05f,
            1f);
        float highWeight = Mathf.Clamp(
            maximumDifficultyLargeRoomWeight,
            1f,
            4f);
        float largeAreaWeight = Mathf.Exp(
            Mathf.Lerp(
                Mathf.Log(lowWeight),
                Mathf.Log(highWeight),
                difficulty01));
        return Mathf.Pow(largeAreaWeight, area01);
    }

    [System.Obsolete(
        "구형 EventArea 후보용 호환 API입니다. "
        + "신규 생성은 GetPlayRoomSizeWeight를 사용하세요.")]
    public float GetAreaSizeWeight(
        Vector2Int legacyDimensions,
        int difficultyLevel)
    {
        return GetPlayRoomSizeWeight(
            legacyDimensions,
            difficultyLevel);
    }

    public void Configure(
        DungeonFlow flow,
        int seed,
        int attempts,
        float asyncFrameMilliseconds,
        float timeoutSeconds)
    {
        dungeonFlow = flow;
        defaultSeed = seed;
        maxAttemptCount = Mathf.Clamp(attempts, 1, 100);
        maxAsyncFrameMilliseconds =
            Mathf.Clamp(asyncFrameMilliseconds, 1f, 12f);
        generationTimeoutSeconds = Mathf.Max(5f, timeoutSeconds);
    }

    public void ConfigurePlayRooms(DungeonPlayRoomCatalog catalog)
    {
        playRoomCatalog = catalog;
    }

    public void ConfigureArenaTiles(
        TileSet configuredArenaTileSet,
        int arenaCount,
        int totalMainPathTileCount)
    {
        arenaTileSet = configuredArenaTileSet;
        defaultArenaCount = Mathf.Clamp(
            arenaCount,
            DungeonRunParameters.MinimumArenaCount,
            DungeonRunParameters.MaximumArenaCount);
        defaultTotalMainPathTileCount = Mathf.Clamp(
            totalMainPathTileCount,
            Mathf.Max(
                DungeonRunParameters.MinimumTotalMainPathTileCount,
                defaultArenaCount + 2),
            DungeonRunParameters.MaximumTotalMainPathTileCount);
    }

    public void ConfigureEndCapTiles(TileSet configuredEndCapTileSet)
    {
        endCapTileSet = configuredEndCapTileSet;
    }

    public void ConfigureTopologyRepairs(
        DungeonTopologyRepairCatalog configuredCatalog)
    {
        topologyRepairCatalog = configuredCatalog;
    }

    public void ConfigureLayoutQuality(
        int candidateCount,
        int minimumBranches,
        float maximumAspectRatio)
    {
        layoutCandidateCount = Mathf.Clamp(candidateCount, 1, 10);
        minimumBranchTileCount = Mathf.Max(0, minimumBranches);
        maximumPlanarAspectRatio =
            Mathf.Clamp(maximumAspectRatio, 1f, 6f);
    }

    public void ConfigureRunScale(
        int difficultyLevel,
        int roomCount,
        float lowDifficultyLargeAreaWeight,
        float highDifficultyLargeAreaWeight)
    {
        defaultDifficultyLevel = Mathf.Clamp(
            difficultyLevel,
            DungeonRunParameters.MinimumDifficultyLevel,
            DungeonRunParameters.MaximumDifficultyLevel);
        defaultRoomCount = Mathf.Clamp(
            roomCount,
            DungeonRunParameters.MinimumRoomCount,
            DungeonRunParameters.MaximumRoomCount);
        minimumDifficultyLargeRoomWeight = Mathf.Clamp(
            lowDifficultyLargeAreaWeight,
            0.05f,
            1f);
        maximumDifficultyLargeRoomWeight = Mathf.Clamp(
            highDifficultyLargeAreaWeight,
            1f,
            4f);
    }
}
