using UnityEngine;

public enum DungeonEventAreaShape
{
    Rectangle = 0
}

[DisallowMultipleComponent]
public sealed class DungeonEventAreaAuthoring : MonoBehaviour
{
    public const int MinimumDimension = 4;
    public const int MaximumDimension = 8;

    [SerializeField] private string areaId = "Size_04x04";
    [SerializeField] private DungeonEventAreaShape shape =
        DungeonEventAreaShape.Rectangle;
    [SerializeField] private Vector2Int dimensionsMeters =
        new(MinimumDimension, MinimumDimension);
    [SerializeField, Min(0.05f)] private float lowerFootTolerance = 0.35f;
    [SerializeField, Min(0.05f)] private float upperFootTolerance = 0.85f;

    public string AreaId => string.IsNullOrWhiteSpace(areaId)
        ? "Size_04x04"
        : areaId;
    public DungeonEventAreaShape Shape => shape;
    public Vector2Int DimensionsMeters => dimensionsMeters;
    public float LowerFootTolerance => Mathf.Max(0.05f, lowerFootTolerance);
    public float UpperFootTolerance => Mathf.Max(0.05f, upperFootTolerance);
    public float SurfaceArea =>
        dimensionsMeters.x * dimensionsMeters.y;

    public void Configure(
        string configuredAreaId,
        Vector2Int configuredDimensions,
        float configuredLowerFootTolerance = 0.35f,
        float configuredUpperFootTolerance = 0.85f)
    {
        areaId = string.IsNullOrWhiteSpace(configuredAreaId)
            ? "Size_04x04"
            : configuredAreaId;
        shape = DungeonEventAreaShape.Rectangle;
        dimensionsMeters = NormalizeDimensions(configuredDimensions);
        lowerFootTolerance =
            Mathf.Max(0.05f, configuredLowerFootTolerance);
        upperFootTolerance =
            Mathf.Max(0.05f, configuredUpperFootTolerance);
    }

    public bool ContainsFootPosition(Vector3 worldPosition)
    {
        Vector3 local = transform.InverseTransformPoint(worldPosition);
        Vector2 half = new(
            dimensionsMeters.x * 0.5f,
            dimensionsMeters.y * 0.5f);
        return Mathf.Abs(local.x) <= half.x
            && Mathf.Abs(local.z) <= half.y
            && local.y >= -LowerFootTolerance
            && local.y <= UpperFootTolerance;
    }

    public Vector3 GetWorldCorner(int index)
    {
        Vector2 half = new(
            dimensionsMeters.x * 0.5f,
            dimensionsMeters.y * 0.5f);
        Vector3 local = index switch
        {
            0 => new Vector3(-half.x, 0f, -half.y),
            1 => new Vector3(-half.x, 0f, half.y),
            2 => new Vector3(half.x, 0f, half.y),
            _ => new Vector3(half.x, 0f, -half.y)
        };
        return transform.TransformPoint(local);
    }

    private static Vector2Int NormalizeDimensions(Vector2Int value)
    {
        int width = Mathf.Clamp(
            value.x,
            MinimumDimension,
            MaximumDimension);
        int depth = Mathf.Clamp(
            value.y,
            MinimumDimension,
            MaximumDimension);
        if (width <= depth)
            return new Vector2Int(width, depth);
        return new Vector2Int(depth, width);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        dimensionsMeters = NormalizeDimensions(dimensionsMeters);
        lowerFootTolerance = Mathf.Max(0.05f, lowerFootTolerance);
        upperFootTolerance = Mathf.Max(0.05f, upperFootTolerance);
        if (string.IsNullOrWhiteSpace(areaId))
            areaId = "Size_04x04";
    }

    private void OnDrawGizmosSelected()
    {
        Matrix4x4 previous = Gizmos.matrix;
        Color previousColor = Gizmos.color;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.15f, 0.95f, 0.55f, 0.9f);
        Gizmos.DrawWireCube(
            Vector3.up * (UpperFootTolerance - LowerFootTolerance) * 0.5f,
            new Vector3(
                dimensionsMeters.x,
                UpperFootTolerance + LowerFootTolerance,
                dimensionsMeters.y));
        Gizmos.matrix = previous;
        Gizmos.color = previousColor;
    }
#endif
}
