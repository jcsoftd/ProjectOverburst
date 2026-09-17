using UnityEngine;

[DisallowMultipleComponent]
public sealed class DungeonPlayRoomAuthoring : MonoBehaviour
{
    public const int MinimumCellCount = 4;
    public const int MaximumCellCount = 8;
    public const float CellSizeMeters = 4f;
    public const float DefaultBackgroundDepthMeters = 18f;

    [SerializeField] private string roomId = "PlayRoom_04x04_A";
    [SerializeField] private Vector2Int gridSizeCells =
        new(MinimumCellCount, MinimumCellCount);
    [SerializeField, Min(0f)]
    private float backgroundDepthMeters =
        DefaultBackgroundDepthMeters;
    [SerializeField, Min(1)] private int authoringVersion = 1;

    public string RoomId => string.IsNullOrWhiteSpace(roomId)
        ? "PlayRoom_04x04_A"
        : roomId;
    public Vector2Int GridSizeCells => gridSizeCells;
    public Vector2 WorldSizeMeters => new(
        gridSizeCells.x * CellSizeMeters,
        gridSizeCells.y * CellSizeMeters);
    public int CellArea => gridSizeCells.x * gridSizeCells.y;
    public float BackgroundDepthMeters =>
        Mathf.Max(0f, backgroundDepthMeters);
    public int AuthoringVersion => Mathf.Max(1, authoringVersion);

    public void Configure(
        string configuredRoomId,
        Vector2Int configuredGridSizeCells,
        float configuredBackgroundDepthMeters,
        int configuredAuthoringVersion)
    {
        roomId = string.IsNullOrWhiteSpace(configuredRoomId)
            ? "PlayRoom_04x04_A"
            : configuredRoomId;
        gridSizeCells = NormalizeGridSize(configuredGridSizeCells);
        backgroundDepthMeters =
            Mathf.Max(0f, configuredBackgroundDepthMeters);
        authoringVersion = Mathf.Max(1, configuredAuthoringVersion);
    }

    public static Vector2Int NormalizeGridSize(Vector2Int value)
    {
        int width = Mathf.Clamp(
            value.x,
            MinimumCellCount,
            MaximumCellCount);
        int depth = Mathf.Clamp(
            value.y,
            MinimumCellCount,
            MaximumCellCount);
        return width <= depth
            ? new Vector2Int(width, depth)
            : new Vector2Int(depth, width);
    }

    public static bool IsSupportedGridSize(Vector2Int value)
    {
        Vector2Int normalized = NormalizeGridSize(value);
        return normalized == value
            && value.x >= MinimumCellCount
            && value.y <= MaximumCellCount;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        gridSizeCells = NormalizeGridSize(gridSizeCells);
        backgroundDepthMeters =
            Mathf.Max(0f, backgroundDepthMeters);
        authoringVersion = Mathf.Max(1, authoringVersion);
        if (string.IsNullOrWhiteSpace(roomId))
            roomId = "PlayRoom_04x04_A";
    }

    private void OnDrawGizmosSelected()
    {
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;
        Gizmos.matrix = transform.localToWorldMatrix;

        Vector2 size = WorldSizeMeters;
        Gizmos.color = new Color(0.12f, 0.88f, 0.48f, 0.95f);
        Gizmos.DrawWireCube(
            new Vector3(0f, -0.05f, 0f),
            new Vector3(size.x, 0.1f, size.y));

        if (BackgroundDepthMeters > 0f)
        {
            Gizmos.color = new Color(0.24f, 0.48f, 0.92f, 0.45f);
            Gizmos.DrawWireCube(
                new Vector3(0f, -BackgroundDepthMeters * 0.5f, 0f),
                new Vector3(
                    size.x,
                    BackgroundDepthMeters,
                    size.y));
        }

        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }
#endif
}
