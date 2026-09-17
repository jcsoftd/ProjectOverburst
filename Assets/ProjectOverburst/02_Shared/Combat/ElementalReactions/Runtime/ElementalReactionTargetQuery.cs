using System.Collections.Generic;
using UnityEngine;

public static class ElementalReactionTargetQuery
{
    private const int SharedFrameCacheCapacity = 8;

    private sealed class SharedFrameCacheEntry
    {
        public int Frame = int.MinValue;
        public int SpatialRevision;
        public int ScopeTargetId;
        public Vector3 Center;
        public float Radius;
        public CombatTeam SourceTeam;
        public int SourceTargetId;
        public readonly List<CombatTarget> Targets = new List<CombatTarget>(128);
    }

    private static readonly SharedFrameCacheEntry[] sharedFrameCache = CreateSharedFrameCache();
    private static int nextSharedFrameCacheIndex;

#if UNITY_EDITOR
    public static int SharedFrameCacheHitCountForValidation { get; private set; }
    public static int SharedFrameSpatialQueryCountForValidation { get; private set; }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSharedFrameCache()
    {
        for (int i = 0; i < sharedFrameCache.Length; i++)
        {
            sharedFrameCache[i].Frame = int.MinValue;
            sharedFrameCache[i].Targets.Clear();
        }

        nextSharedFrameCacheIndex = 0;
#if UNITY_EDITOR
        SharedFrameCacheHitCountForValidation = 0;
        SharedFrameSpatialQueryCountForValidation = 0;
#endif
    }

    public static void CollectHostileTargets(
        Vector3 center,
        float radius,
        CombatTeam sourceTeam,
        int sourceTargetId,
        List<CombatTarget> results)
    {
        CombatTargetRegistry.CollectPotentialTargets(center, radius, results);
        float radiusSquared = radius * radius;
        int writeIndex = 0;
        for (int i = 0; i < results.Count; i++)
        {
            CombatTarget target = results[i];
            if (!CanDamage(sourceTeam, sourceTargetId, target)
                || (target.WorldCenter - center).sqrMagnitude > radiusSquared)
            {
                continue;
            }

            results[writeIndex++] = target;
        }

        if (writeIndex < results.Count)
            results.RemoveRange(writeIndex, results.Count - writeIndex);
    }

    public static bool TryFindNearestPreferredStatusTarget(
        Vector3 center,
        float radius,
        WeaponElement requiredStatus,
        CombatTeam sourceTeam,
        int sourceTargetId,
        List<int> visitedTargetIds,
        List<CombatTarget> buffer,
        out CombatTarget selected,
        out bool hasRequiredStatus)
    {
        CollectHostileTargets(center, radius, sourceTeam, sourceTargetId, buffer);
        CombatTarget nearestPreferred = null;
        CombatTarget nearestFallback = null;
        float preferredDistanceSquared = float.PositiveInfinity;
        float fallbackDistanceSquared = float.PositiveInfinity;
        int preferredTargetId = int.MaxValue;
        int fallbackTargetId = int.MaxValue;
        for (int i = 0; i < buffer.Count; i++)
        {
            CombatTarget candidate = buffer[i];
            if (visitedTargetIds.Contains(candidate.TargetId))
                continue;

            float distanceSquared = (candidate.WorldCenter - center).sqrMagnitude;
            IElementalStatusReceiver statusOwner = candidate.ElementalStatusReceiver;
            bool preferred = statusOwner != null && statusOwner.TryGetStatus(requiredStatus, out _);
            if (preferred)
            {
                if (IsNearerOrLowerId(
                        distanceSquared,
                        candidate.TargetId,
                        preferredDistanceSquared,
                        preferredTargetId))
                {
                    nearestPreferred = candidate;
                    preferredDistanceSquared = distanceSquared;
                    preferredTargetId = candidate.TargetId;
                }
                continue;
            }

            if (!IsNearerOrLowerId(
                    distanceSquared,
                    candidate.TargetId,
                    fallbackDistanceSquared,
                    fallbackTargetId))
                continue;

            nearestFallback = candidate;
            fallbackDistanceSquared = distanceSquared;
            fallbackTargetId = candidate.TargetId;
        }

        hasRequiredStatus = nearestPreferred != null;
        selected = hasRequiredStatus ? nearestPreferred : nearestFallback;
        return selected != null;
    }

    private static bool IsNearerOrLowerId(
        float candidateDistanceSquared,
        int candidateTargetId,
        float currentDistanceSquared,
        int currentTargetId)
    {
        return candidateDistanceSquared < currentDistanceSquared - 0.0001f
            || (Mathf.Abs(candidateDistanceSquared - currentDistanceSquared) <= 0.0001f
                && candidateTargetId < currentTargetId);
    }

    public static void CollectHostileTargetsSharedForFrame(
        int scopeTargetId,
        Vector3 center,
        float radius,
        CombatTeam sourceTeam,
        int sourceTargetId,
        List<CombatTarget> results)
    {
        if (results == null)
            return;

        int frame = Time.frameCount;
        int spatialRevision = CombatTargetRegistry.SpatialRevision;
        for (int i = 0; i < sharedFrameCache.Length; i++)
        {
            SharedFrameCacheEntry cached = sharedFrameCache[i];
            if (!Matches(
                    cached,
                    frame,
                    spatialRevision,
                    scopeTargetId,
                    center,
                    radius,
                    sourceTeam,
                    sourceTargetId))
            {
                continue;
            }

            CopyCurrentValidTargets(cached.Targets, center, radius, sourceTeam, sourceTargetId, results);
#if UNITY_EDITOR
            SharedFrameCacheHitCountForValidation++;
#endif
            return;
        }

        SharedFrameCacheEntry entry = ResolveWritableEntry(frame);
        CollectHostileTargets(center, radius, sourceTeam, sourceTargetId, entry.Targets);
        entry.Frame = frame;
        entry.SpatialRevision = CombatTargetRegistry.SpatialRevision; // Query 중 분할 보정 뒤 버전 저장
        entry.ScopeTargetId = scopeTargetId;
        entry.Center = center;
        entry.Radius = radius;
        entry.SourceTeam = sourceTeam;
        entry.SourceTargetId = sourceTargetId;
        CopyCurrentValidTargets(entry.Targets, center, radius, sourceTeam, sourceTargetId, results);
#if UNITY_EDITOR
        SharedFrameSpatialQueryCountForValidation++;
#endif
    }

#if UNITY_EDITOR
    public static void ResetSharedFrameCacheForValidation()
    {
        ResetSharedFrameCache();
    }
#endif

    private static SharedFrameCacheEntry[] CreateSharedFrameCache()
    {
        SharedFrameCacheEntry[] entries = new SharedFrameCacheEntry[SharedFrameCacheCapacity];
        for (int i = 0; i < entries.Length; i++)
            entries[i] = new SharedFrameCacheEntry();
        return entries;
    }

    private static SharedFrameCacheEntry ResolveWritableEntry(int frame)
    {
        for (int i = 0; i < sharedFrameCache.Length; i++)
        {
            if (sharedFrameCache[i].Frame != frame)
                return sharedFrameCache[i];
        }

        SharedFrameCacheEntry entry = sharedFrameCache[nextSharedFrameCacheIndex];
        nextSharedFrameCacheIndex = (nextSharedFrameCacheIndex + 1) % sharedFrameCache.Length;
        return entry;
    }

    private static bool Matches(
        SharedFrameCacheEntry entry,
        int frame,
        int spatialRevision,
        int scopeTargetId,
        Vector3 center,
        float radius,
        CombatTeam sourceTeam,
        int sourceTargetId)
    {
        return entry.Frame == frame
            && entry.SpatialRevision == spatialRevision
            && entry.ScopeTargetId == scopeTargetId
            && entry.Center.x == center.x
            && entry.Center.y == center.y
            && entry.Center.z == center.z
            && entry.Radius == radius
            && entry.SourceTeam == sourceTeam
            && entry.SourceTargetId == sourceTargetId;
    }

    private static void CopyCurrentValidTargets(
        List<CombatTarget> cached,
        Vector3 center,
        float radius,
        CombatTeam sourceTeam,
        int sourceTargetId,
        List<CombatTarget> results)
    {
        results.Clear();
        float radiusSquared = radius * radius;
        for (int i = 0; i < cached.Count; i++)
        {
            CombatTarget target = cached[i];
            if (CanDamage(sourceTeam, sourceTargetId, target)
                && (target.WorldCenter - center).sqrMagnitude <= radiusSquared)
            {
                results.Add(target); // 캐시 적중 시에도 현재 생존·위치 재검증
            }
        }
    }

    private static bool CanDamage(CombatTeam sourceTeam, int sourceTargetId, CombatTarget target)
    {
        if (target == null || !target.IsAlive || target.TargetId == sourceTargetId)
            return false;

        CombatTeam targetTeam = target.Team;
        return sourceTeam == CombatTeam.Neutral
            || targetTeam == CombatTeam.Neutral
            || sourceTeam != targetTeam;
    }
}
