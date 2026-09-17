using System.Collections.Generic;
using UnityEngine;

public static class EnemyFlowFieldService // 타겟별 공유 방향장 캐시
{
    public const int MaximumCachedTargets = 8;
    public const int MaximumBuildCellsPerFrame = 8192;
    public const int MaximumBuildCellsPerRequest = 2048;

    private sealed class CacheEntry
    {
        public Transform Target;
        public EnemyFlowField Field;
        public int TargetX;
        public int TargetZ;
        public int LastTouchedFrame;
    }

    private static readonly Dictionary<int, CacheEntry> Entries = new Dictionary<int, CacheEntry>();
    private static readonly List<int> StaleKeys = new List<int>(MaximumCachedTargets);
    private static RunWalkableArea observedArea;
    private static int observedContextRevision = -1;
    private static int budgetFrame = -1;
    private static int remainingFrameBudget;

    public static bool Enabled { get; private set; } = true;
    public static int CachedTargetCount => Entries.Count;
    public static int BuildCount { get; private set; }
    public static int CacheHitCount { get; private set; }
    public static int BuiltCellCountThisFrame { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        Enabled = true;
        ClearCache();
    }

    public static void SetEnabled(bool enabled)
    {
        if (Enabled == enabled)
            return;

        Enabled = enabled;
        ClearEntries();
    }

    public static void ClearCache()
    {
        ClearEntries();
        observedArea = RunWalkableContext.Current;
        observedContextRevision = RunWalkableContext.Revision;
        budgetFrame = -1;
        remainingFrameBudget = 0;
        BuildCount = 0;
        CacheHitCount = 0;
        BuiltCellCountThisFrame = 0;
    }

    public static bool TryGetDirection(
        Transform target,
        Vector3 agentPosition,
        out Vector3 direction,
        out Vector3 waypoint)
    {
        direction = Vector3.zero;
        waypoint = agentPosition;
        RunWalkableArea area = RunWalkableContext.Current;
        if (!Enabled || target == null || area == null)
            return false;

        SynchronizeContext(area);
        if (!TryResolveWalkableCell(area, target.position, out int targetX, out int targetZ))
            return false;

        int targetId = target.GetInstanceID();
        if (!Entries.TryGetValue(targetId, out CacheEntry entry)
            || entry.Target != target
            || entry.Field == null
            || entry.Field.Area != area)
        {
            EnsureCacheCapacity();
            entry = new CacheEntry
            {
                Target = target,
                Field = new EnemyFlowField(area, targetX, targetZ),
                TargetX = targetX,
                TargetZ = targetZ
            };
            Entries[targetId] = entry;
            BuildCount++;
        }
        else
        {
            CacheHitCount++;
            if (entry.TargetX != targetX || entry.TargetZ != targetZ)
            {
                entry.Field.Rebuild(targetX, targetZ);
                entry.TargetX = targetX;
                entry.TargetZ = targetZ;
                BuildCount++;
            }
        }

        entry.LastTouchedFrame = Time.frameCount;
        AdvanceBuild(entry.Field);
        return entry.Field.TryGetDirection(agentPosition, out direction, out waypoint);
    }

    private static void SynchronizeContext(RunWalkableArea area)
    {
        int revision = RunWalkableContext.Revision;
        if (observedContextRevision == revision && observedArea == area)
            return;

        ClearEntries();
        observedArea = area;
        observedContextRevision = revision;
        budgetFrame = -1;
    }

    private static void AdvanceBuild(EnemyFlowField field)
    {
        int frame = Time.frameCount;
        if (budgetFrame != frame)
        {
            budgetFrame = frame;
            remainingFrameBudget = MaximumBuildCellsPerFrame;
            BuiltCellCountThisFrame = 0;
        }

        int budget = Mathf.Min(MaximumBuildCellsPerRequest, remainingFrameBudget);
        if (budget <= 0)
            return;

        int processed = field.Advance(budget);
        remainingFrameBudget -= processed;
        BuiltCellCountThisFrame += processed;
    }

    private static bool TryResolveWalkableCell(
        RunWalkableArea area,
        Vector3 worldPosition,
        out int x,
        out int z)
    {
        if (area.TryGetCell(worldPosition, out x, out z) && area.IsWalkableCell(x, z))
            return true;
        return area.TryFindNearestWalkableCell(worldPosition, out x, out z);
    }

    private static void EnsureCacheCapacity()
    {
        StaleKeys.Clear();
        foreach (KeyValuePair<int, CacheEntry> pair in Entries)
        {
            if (pair.Value == null || pair.Value.Target == null)
                StaleKeys.Add(pair.Key);
        }

        for (int i = 0; i < StaleKeys.Count; i++)
            Entries.Remove(StaleKeys[i]);
        if (Entries.Count < MaximumCachedTargets)
            return;

        int oldestKey = 0;
        int oldestFrame = int.MaxValue;
        foreach (KeyValuePair<int, CacheEntry> pair in Entries)
        {
            if (pair.Value.LastTouchedFrame >= oldestFrame)
                continue;
            oldestKey = pair.Key;
            oldestFrame = pair.Value.LastTouchedFrame;
        }

        Entries.Remove(oldestKey);
    }

    private static void ClearEntries()
    {
        Entries.Clear();
        StaleKeys.Clear();
    }
}
