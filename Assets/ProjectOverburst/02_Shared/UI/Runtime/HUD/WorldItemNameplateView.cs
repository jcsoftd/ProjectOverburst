using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class WorldItemNameplateView : MonoBehaviour
{
    [SerializeField] private RectTransform rowsRoot;
    [SerializeField] private WorldItemNameplateRowView[] authoredRows;
    private WorldItemNameplateRowView initializedMeasurementRow;

    public RectTransform RowsRoot => rowsRoot;
    public int AuthoredRowCount => authoredRows != null ? authoredRows.Length : 0;
    public IReadOnlyList<WorldItemNameplateRowView> AuthoredRows => authoredRows;
    public Vector2 AuthoredRowSize
    {
        get
        {
            if (authoredRows == null || authoredRows.Length == 0 || authoredRows[0] == null || authoredRows[0].RootRect == null)
                return Vector2.zero;

            return authoredRows[0].RootRect.rect.size;
        }
    }

    public void BindBridge(WorldItemNameplateBridge bridge)
    {
        if (authoredRows == null)
            return;

        for (int i = 0; i < authoredRows.Length; i++)
            authoredRows[i]?.BindBridge(bridge);
    }

    public Vector2 MeasureRowSize(
        string text,
        float minWidth,
        float maxWidth,
        float horizontalPadding,
        float height)
    {
        float resolvedMin = Mathf.Max(1f, minWidth);
        float resolvedMax = Mathf.Max(resolvedMin, maxWidth);
        float preferredWidth = 0f;
        if (authoredRows != null
            && authoredRows.Length > 0
            && authoredRows[0] != null
            && authoredRows[0].Label != null)
        {
            WorldItemNameplateRowView row = authoredRows[0];
            if (initializedMeasurementRow != row
                && rowsRoot != null
                && rowsRoot.gameObject.activeInHierarchy)
            {
                // TMP caches its special glyphs in Awake. Pooled rows begin inactive,
                // so initialize the measuring row before its first preferred-size query.
                // Restore visibility in this frame, before the canvas renders.
                bool wasActive = row.gameObject.activeSelf;
                try
                {
                    row.gameObject.SetActive(true);
                    initializedMeasurementRow = row;
                }
                finally
                {
                    row.gameObject.SetActive(wasActive);
                }
            }
            preferredWidth = row.Label.GetPreferredValues(text ?? string.Empty).x;
        }

        return new Vector2(
            Mathf.Clamp(preferredWidth + Mathf.Max(0f, horizontalPadding), resolvedMin, resolvedMax),
            Mathf.Max(1f, height));
    }

    public void Render(
        IReadOnlyList<WorldItemNameplatePlacement> placements,
        bool allowPointerInput)
    {
        if (authoredRows == null)
            return;

        int visibleCount = placements != null
            ? Mathf.Min(placements.Count, authoredRows.Length)
            : 0;
        for (int i = 0; i < authoredRows.Length; i++)
        {
            WorldItemNameplateRowView row = authoredRows[i];
            if (row == null)
                continue;

            if (i < visibleCount)
            {
                WorldItemNameplatePlacement placement = placements[i];
                bool clickable = allowPointerInput && !placement.Candidate.IsTemporaryHover;
                row.Show(placement.Candidate, placement.Position, clickable);
            }
            else
            {
                row.Hide();
            }
        }
    }

    public void HideAll()
    {
        WorldItemNameplateVisibilityRegistry.Clear();
        if (authoredRows == null)
            return;

        for (int i = 0; i < authoredRows.Length; i++)
            authoredRows[i]?.Hide();
    }

    public bool ValidateAuthoredReferences(int expectedRowCount, out string error)
    {
        if (rowsRoot == null)
        {
            error = "WorldItemNameplate rowsRoot is missing.";
            return false;
        }

        if (authoredRows == null || authoredRows.Length != expectedRowCount)
        {
            error = "WorldItemNameplate authored row count mismatch.";
            return false;
        }

        for (int i = 0; i < authoredRows.Length; i++)
        {
            WorldItemNameplateRowView row = authoredRows[i];
            if (row == null
                || row.RootRect == null
                || row.Background == null
                || row.HoverHighlight == null
                || row.Label == null
                || row.PendingMarker == null
                || row.GradeFrame == null
                || row.CanvasGroup == null)
            {
                error = "WorldItemNameplate authored row reference is missing at index " + i + ".";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }
}

/// <summary>
/// 최종 화면 배치에 포함된 월드 아이템만 외부 시스템에 읽기 전용으로 공개한다.
/// </summary>
public static class WorldItemNameplateVisibilityRegistry
{
    private static readonly HashSet<int> visibleInstanceIds = new HashSet<int>();

    public static int VisibleCount => visibleInstanceIds.Count;

    public static bool IsVisible(WorldItemPickup pickup)
    {
        return pickup != null && IsVisible(pickup.GetInstanceID());
    }

    public static bool IsVisible(int instanceId)
    {
        return instanceId != 0 && visibleInstanceIds.Contains(instanceId);
    }

    internal static void Publish(
        IReadOnlyList<WorldItemNameplatePlacement> placements,
        int renderCapacity)
    {
        visibleInstanceIds.Clear();
        int count = placements != null
            ? Mathf.Min(placements.Count, Mathf.Max(0, renderCapacity))
            : 0;
        for (int i = 0; i < count; i++)
        {
            WorldItemNameplateLayoutCandidate candidate = placements[i].Candidate;
            if (candidate.Pickup != null && candidate.InstanceId != 0)
                visibleInstanceIds.Add(candidate.InstanceId);
        }
    }

    internal static void Clear()
    {
        visibleInstanceIds.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        visibleInstanceIds.Clear();
    }
}
