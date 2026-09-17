using System.Collections.Generic;
using UnityEngine;

public sealed class WorldItemNameplateStableOrderRegistry
{
    private readonly Dictionary<int, int> stableOrders = new Dictionary<int, int>(96);
    private readonly HashSet<int> activeIds = new HashSet<int>();
    private readonly List<int> sortedIds = new List<int>(96);
    private int nextStableOrder;

    public int Count => stableOrders.Count;

    public bool Synchronize(IReadOnlyList<int> instanceIds)
    {
        activeIds.Clear();
        sortedIds.Clear();
        if (instanceIds != null)
        {
            for (int i = 0; i < instanceIds.Count; i++)
            {
                int instanceId = instanceIds[i];
                if (instanceId != 0 && activeIds.Add(instanceId))
                    sortedIds.Add(instanceId);
            }
        }

        bool changed = stableOrders.Count != activeIds.Count;
        var removed = new List<int>();
        foreach (KeyValuePair<int, int> pair in stableOrders)
        {
            if (!activeIds.Contains(pair.Key))
                removed.Add(pair.Key);
        }

        for (int i = 0; i < removed.Count; i++)
        {
            stableOrders.Remove(removed[i]);
            changed = true;
        }

        sortedIds.Sort();
        for (int i = 0; i < sortedIds.Count; i++)
        {
            int instanceId = sortedIds[i];
            if (stableOrders.ContainsKey(instanceId))
                continue;

            stableOrders.Add(instanceId, nextStableOrder++);
            changed = true;
        }

        return changed;
    }

    public bool TryGetStableOrder(int instanceId, out int stableOrder)
    {
        return stableOrders.TryGetValue(instanceId, out stableOrder);
    }

    public void Clear()
    {
        stableOrders.Clear();
        activeIds.Clear();
        sortedIds.Clear();
        nextStableOrder = 0;
    }

}

public readonly struct WorldItemNameplateDisplayOption
{
    public int InstanceId { get; }
    public int StableOrder { get; }
    public ItemGrade Grade { get; }
    public bool IsEligible { get; }
    public bool IsSelected { get; }
    public bool IsPendingAutoMove { get; }

    public WorldItemNameplateDisplayOption(
        int instanceId,
        int stableOrder,
        ItemGrade grade,
        bool isEligible,
        bool isSelected,
        bool isPendingAutoMove)
    {
        InstanceId = instanceId;
        StableOrder = stableOrder;
        Grade = grade;
        IsEligible = isEligible;
        IsSelected = isSelected;
        IsPendingAutoMove = isPendingAutoMove;
    }
}

public sealed class WorldItemNameplateDisplaySet
{
    private readonly HashSet<int> selectedIds = new HashSet<int>();
    private readonly List<WorldItemNameplateDisplayOption> working = new List<WorldItemNameplateDisplayOption>(96);

    public int Count => selectedIds.Count;

    public bool Contains(int instanceId)
    {
        return instanceId != 0 && selectedIds.Contains(instanceId);
    }

    public void Clear()
    {
        selectedIds.Clear();
        working.Clear();
    }

    public void CopyInstanceIds(List<int> output)
    {
        if (output == null)
            return;

        output.Clear();
        foreach (int instanceId in selectedIds)
            output.Add(instanceId);
        output.Sort();
    }

    public void Rebuild(IReadOnlyList<WorldItemNameplateDisplayOption> options, int capacity)
    {
        selectedIds.Clear();
        working.Clear();
        if (options == null || capacity <= 0)
            return;

        for (int i = 0; i < options.Count; i++)
        {
            if (options[i].IsEligible)
                working.Add(options[i]);
        }

        working.Sort(WorldItemNameplateLayout.CompareDisplayPriority);
        int resolvedCapacity = Mathf.Min(capacity, working.Count);
        for (int i = 0; i < resolvedCapacity; i++)
            selectedIds.Add(working[i].InstanceId);
    }

    public bool EnsureGuaranteed(
        IReadOnlyList<WorldItemNameplateDisplayOption> options,
        int targetInstanceId,
        int capacity,
        out int removedInstanceId)
    {
        removedInstanceId = 0;
        if (targetInstanceId == 0 || selectedIds.Contains(targetInstanceId))
            return false;
        if (!TryFindOption(options, targetInstanceId, out WorldItemNameplateDisplayOption target)
            || !target.IsEligible)
        {
            return false;
        }

        if (selectedIds.Count < capacity)
        {
            selectedIds.Add(targetInstanceId);
            return true;
        }

        bool foundLowest = false;
        WorldItemNameplateDisplayOption lowest = default;
        for (int i = 0; i < options.Count; i++)
        {
            WorldItemNameplateDisplayOption option = options[i];
            if (!selectedIds.Contains(option.InstanceId))
                continue;
            if (!foundLowest || WorldItemNameplateLayout.CompareDisplayPriority(option, lowest) > 0)
            {
                lowest = option;
                foundLowest = true;
            }
        }

        if (!foundLowest || WorldItemNameplateLayout.CompareDisplayPriority(target, lowest) >= 0)
            return false;

        selectedIds.Remove(lowest.InstanceId);
        selectedIds.Add(targetInstanceId);
        removedInstanceId = lowest.InstanceId;
        return true;
    }

    private static bool TryFindOption(
        IReadOnlyList<WorldItemNameplateDisplayOption> options,
        int instanceId,
        out WorldItemNameplateDisplayOption result)
    {
        if (options != null)
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].InstanceId == instanceId)
                {
                    result = options[i];
                    return true;
                }
            }
        }

        result = default;
        return false;
    }
}

public readonly struct WorldItemNameplateLayoutCandidate
{
    public WorldItemPickup Pickup { get; }
    public string DisplayText { get; }
    public Vector2 DirectAnchorPosition { get; }
    public Vector2 Size { get; }
    public float Distance { get; }
    public int InstanceId { get; }
    public int StableOrder { get; }
    public ItemGrade Grade { get; }
    public bool IsSelected { get; }
    public bool IsPendingAutoMove { get; }
    public bool IsTemporaryHover { get; }
    public bool IsWithinPickupRange { get; }
    public bool IsHoverEmphasized { get; }

    public WorldItemNameplateLayoutCandidate(
        WorldItemPickup pickup,
        string displayText,
        Vector2 directAnchorPosition,
        Vector2 size,
        float distance,
        int instanceId,
        int stableOrder,
        ItemGrade grade,
        bool isSelected,
        bool isPendingAutoMove,
        bool isTemporaryHover,
        bool isWithinPickupRange,
        bool isHoverEmphasized = false)
    {
        Pickup = pickup;
        DisplayText = displayText ?? string.Empty;
        DirectAnchorPosition = directAnchorPosition;
        Size = new Vector2(Mathf.Max(1f, size.x), Mathf.Max(1f, size.y));
        Distance = Mathf.Max(0f, distance);
        InstanceId = instanceId;
        StableOrder = stableOrder;
        Grade = grade;
        IsSelected = isSelected;
        IsPendingAutoMove = isPendingAutoMove;
        IsTemporaryHover = isTemporaryHover;
        IsWithinPickupRange = isWithinPickupRange;
        IsHoverEmphasized = isHoverEmphasized;
    }
}

public sealed class WorldItemNameplateVisibilityHysteresis
{
    private readonly HashSet<int> visibleIds = new HashSet<int>();
    private readonly HashSet<int> nextVisibleIds = new HashSet<int>();
    private readonly Dictionary<int, Vector2> visibleOffsets = new Dictionary<int, Vector2>();
    private readonly Dictionary<int, Vector2> nextVisibleOffsets = new Dictionary<int, Vector2>();

    public int VisibleCount => visibleIds.Count;

    public bool WasVisible(int instanceId)
    {
        return instanceId != 0 && visibleIds.Contains(instanceId);
    }

    public void BeginFrame()
    {
        nextVisibleIds.Clear();
        nextVisibleOffsets.Clear();
    }

    public bool TryGetPreviousOffset(int instanceId, out Vector2 offset)
    {
        return visibleOffsets.TryGetValue(instanceId, out offset);
    }

    public void MarkVisible(int instanceId, Vector2 offset)
    {
        if (instanceId == 0)
            return;

        nextVisibleIds.Add(instanceId);
        nextVisibleOffsets[instanceId] = offset;
    }

    public void EndFrame()
    {
        visibleIds.Clear();
        visibleOffsets.Clear();
        foreach (int instanceId in nextVisibleIds)
        {
            visibleIds.Add(instanceId);
            if (nextVisibleOffsets.TryGetValue(instanceId, out Vector2 offset))
                visibleOffsets[instanceId] = offset;
        }
    }

    public void Clear()
    {
        visibleIds.Clear();
        nextVisibleIds.Clear();
        visibleOffsets.Clear();
        nextVisibleOffsets.Clear();
    }
}

public readonly struct WorldItemNameplatePlacement
{
    public WorldItemNameplateLayoutCandidate Candidate { get; }
    public Vector2 Position { get; }
    public Rect Bounds { get; }

    public WorldItemNameplatePlacement(
        WorldItemNameplateLayoutCandidate candidate,
        Vector2 position,
        Rect bounds)
    {
        Candidate = candidate;
        Position = position;
        Bounds = bounds;
    }
}

public static class WorldItemNameplateLayout
{
    private const int LocalSearchRingCount = 4;
    private const float LocalSearchHorizontalStep = 36f;
    private static readonly List<WorldItemNameplateLayoutCandidate> workingCandidates = new List<WorldItemNameplateLayoutCandidate>(96);
    private static readonly List<Vector2Int> localSearchGrid = new List<Vector2Int>(81);

    public static void Build(
        IReadOnlyList<WorldItemNameplateLayoutCandidate> source,
        int capacity,
        Rect safeRect,
        float rowCenterSpacing,
        List<WorldItemNameplatePlacement> output,
        WorldItemNameplateVisibilityHysteresis hysteresis = null,
        float enterSafeInset = 0f,
        float exitSafeOutset = 0f,
        float enterOverlapPadding = 0f)
    {
        if (output == null)
            return;

        output.Clear();
        hysteresis?.BeginFrame();
        workingCandidates.Clear();
        if (source == null || capacity <= 0)
        {
            hysteresis?.EndFrame();
            return;
        }

        for (int i = 0; i < source.Count; i++)
            workingCandidates.Add(source[i]);

        workingCandidates.Sort((left, right) => CompareLayoutCandidatePriority(left, right, hysteresis));
        EnsureLocalSearchGrid();
        float verticalStep = Mathf.Max(1f, rowCenterSpacing);
        Rect enterSafeRect = Inset(safeRect, Mathf.Max(0f, enterSafeInset));
        Rect exitSafeRect = Expand(safeRect, Mathf.Max(0f, exitSafeOutset));
        for (int i = 0; i < workingCandidates.Count && output.Count < capacity; i++)
        {
            WorldItemNameplateLayoutCandidate candidate = workingCandidates[i];
            bool wasVisible = hysteresis != null && hysteresis.WasVisible(candidate.InstanceId);
            Rect visibilityRect = wasVisible ? exitSafeRect : enterSafeRect;
            if (!visibilityRect.Contains(candidate.DirectAnchorPosition)
                || !TryResolveLocalPosition(
                    candidate,
                    visibilityRect,
                    verticalStep,
                    output,
                    hysteresis,
                    enterOverlapPadding,
                    out Vector2 position,
                    out Rect bounds))
            {
                continue;
            }

            output.Add(new WorldItemNameplatePlacement(candidate, position, bounds));
            hysteresis?.MarkVisible(candidate.InstanceId, position - candidate.DirectAnchorPosition);
        }

        hysteresis?.EndFrame();
    }

    public static int CompareDisplayPriority(WorldItemNameplateDisplayOption left, WorldItemNameplateDisplayOption right)
    {
        int compare = CompareDescending(left.IsPendingAutoMove, right.IsPendingAutoMove);
        if (compare != 0)
            return compare;
        compare = CompareDescending(left.IsSelected, right.IsSelected);
        if (compare != 0)
            return compare;
        compare = CompareDescending(left.Grade >= ItemGrade.Legendary, right.Grade >= ItemGrade.Legendary);
        if (compare != 0)
            return compare;
        compare = right.Grade.CompareTo(left.Grade);
        return compare != 0 ? compare : left.StableOrder.CompareTo(right.StableOrder);
    }

    private static bool TryResolveLocalPosition(
        WorldItemNameplateLayoutCandidate candidate,
        Rect visibilityRect,
        float verticalStep,
        List<WorldItemNameplatePlacement> output,
        WorldItemNameplateVisibilityHysteresis hysteresis,
        float enterOverlapPadding,
        out Vector2 position,
        out Rect bounds)
    {
        if (candidate.Size.x > visibilityRect.width || candidate.Size.y > visibilityRect.height)
        {
            position = default;
            bounds = default;
            return false;
        }

        bool wasVisible = hysteresis != null && hysteresis.WasVisible(candidate.InstanceId);
        if (wasVisible
            && hysteresis.TryGetPreviousOffset(candidate.InstanceId, out Vector2 previousOffset)
            && TryPlaceAtOffset(candidate, previousOffset, visibilityRect, output, 0f, out position, out bounds))
        {
            return true;
        }

        float resolvedVerticalStep = Mathf.Max(verticalStep, candidate.Size.y + 2f);
        float collisionPadding = wasVisible ? 0f : Mathf.Max(0f, enterOverlapPadding);
        for (int i = 0; i < localSearchGrid.Count; i++)
        {
            Vector2Int grid = localSearchGrid[i];
            Vector2 offset = new Vector2(
                grid.x * LocalSearchHorizontalStep,
                grid.y * resolvedVerticalStep);
            if (TryPlaceAtOffset(
                candidate,
                offset,
                visibilityRect,
                output,
                collisionPadding,
                out position,
                out bounds))
            {
                return true;
            }
        }

        position = default;
        bounds = default;
        return false;
    }

    private static bool TryPlaceAtOffset(
        WorldItemNameplateLayoutCandidate candidate,
        Vector2 offset,
        Rect visibilityRect,
        List<WorldItemNameplatePlacement> output,
        float collisionPadding,
        out Vector2 position,
        out Rect bounds)
    {
        position = new Vector2(
            Mathf.Round(candidate.DirectAnchorPosition.x + offset.x),
            Mathf.Round(candidate.DirectAnchorPosition.y + offset.y));
        bounds = Rect.MinMaxRect(
            position.x - candidate.Size.x * 0.5f,
            position.y - candidate.Size.y * 0.5f,
            position.x + candidate.Size.x * 0.5f,
            position.y + candidate.Size.y * 0.5f);
        Rect collisionBounds = collisionPadding > 0f ? Expand(bounds, collisionPadding) : bounds;
        return ContainsRect(visibilityRect, bounds) && !OverlapsAny(collisionBounds, output);
    }

    private static void EnsureLocalSearchGrid()
    {
        if (localSearchGrid.Count > 0)
            return;

        for (int y = -LocalSearchRingCount; y <= LocalSearchRingCount; y++)
        {
            for (int x = -LocalSearchRingCount; x <= LocalSearchRingCount; x++)
                localSearchGrid.Add(new Vector2Int(x, y));
        }

        localSearchGrid.Sort((left, right) =>
        {
            int leftDistance = left.x * left.x + left.y * left.y;
            int rightDistance = right.x * right.x + right.y * right.y;
            int compare = leftDistance.CompareTo(rightDistance);
            if (compare != 0)
                return compare;
            compare = Mathf.Abs(left.y).CompareTo(Mathf.Abs(right.y));
            if (compare != 0)
                return compare;
            compare = right.y.CompareTo(left.y);
            return compare != 0 ? compare : left.x.CompareTo(right.x);
        });
    }

    private static Rect Inset(Rect value, float amount)
    {
        float resolved = Mathf.Min(Mathf.Max(0f, amount), Mathf.Min(value.width, value.height) * 0.49f);
        return Rect.MinMaxRect(
            value.xMin + resolved,
            value.yMin + resolved,
            value.xMax - resolved,
            value.yMax - resolved);
    }

    private static Rect Expand(Rect value, float amount)
    {
        float resolved = Mathf.Max(0f, amount);
        return Rect.MinMaxRect(
            value.xMin - resolved,
            value.yMin - resolved,
            value.xMax + resolved,
            value.yMax + resolved);
    }

    private static bool ContainsRect(Rect container, Rect value)
    {
        const float epsilon = 0.01f;
        return value.xMin >= container.xMin - epsilon
            && value.xMax <= container.xMax + epsilon
            && value.yMin >= container.yMin - epsilon
            && value.yMax <= container.yMax + epsilon;
    }

    private static bool OverlapsAny(Rect bounds, List<WorldItemNameplatePlacement> output)
    {
        for (int i = 0; i < output.Count; i++)
        {
            if (bounds.Overlaps(output[i].Bounds))
                return true;
        }

        return false;
    }

    private static int CompareLayoutCandidatePriority(
        WorldItemNameplateLayoutCandidate left,
        WorldItemNameplateLayoutCandidate right,
        WorldItemNameplateVisibilityHysteresis hysteresis)
    {
        int compare = CompareDescending(left.IsTemporaryHover, right.IsTemporaryHover);
        if (compare != 0)
            return compare;
        compare = CompareDescending(left.IsPendingAutoMove, right.IsPendingAutoMove);
        if (compare != 0)
            return compare;
        compare = CompareDescending(left.IsSelected, right.IsSelected);
        if (compare != 0)
            return compare;
        compare = CompareDescending(left.Grade >= ItemGrade.Legendary, right.Grade >= ItemGrade.Legendary);
        if (compare != 0)
            return compare;
        compare = right.Grade.CompareTo(left.Grade);
        if (compare != 0)
            return compare;
        if (hysteresis != null)
        {
            compare = CompareDescending(
                hysteresis.WasVisible(left.InstanceId),
                hysteresis.WasVisible(right.InstanceId));
            if (compare != 0)
                return compare;
        }

        return left.StableOrder.CompareTo(right.StableOrder);
    }

    private static int CompareDescending(bool left, bool right)
    {
        return right.CompareTo(left);
    }
}
