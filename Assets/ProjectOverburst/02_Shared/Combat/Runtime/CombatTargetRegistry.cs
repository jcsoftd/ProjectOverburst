using System.Collections.Generic;
using UnityEngine;

public static class CombatTargetRegistry
{
    private const int SpatialMaintenanceBudgetPerQueryFrame = 64;
    private static readonly List<CombatTarget> Targets = new List<CombatTarget>(128);
    private static readonly Dictionary<int, CombatTarget> TargetsByTransformId =
        new Dictionary<int, CombatTarget>(128);
    private static readonly CombatSpatialIndex SpatialIndex = new CombatSpatialIndex();
    private static int lastSpatialMaintenanceFrame = int.MinValue;
    private static int spatialMaintenanceCursor;
    private static int spatialRevision;

#if UNITY_EDITOR
    public static int LastSpatialMaintenanceProcessedCountForValidation { get; private set; }
#endif

    public static int RegisteredCount => Targets.Count;
    public static int SpatialRevision => spatialRevision;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Targets.Clear();
        TargetsByTransformId.Clear();
        SpatialIndex.Clear();
        lastSpatialMaintenanceFrame = int.MinValue;
        spatialMaintenanceCursor = 0;
        spatialRevision = 0;
#if UNITY_EDITOR
        LastSpatialMaintenanceProcessedCountForValidation = 0;
#endif
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RebuildForPlayMode()
    {
        ResetStatics();
        CombatTarget[] loadedTargets = Object.FindObjectsByType<CombatTarget>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < loadedTargets.Length; i++)
            Register(loadedTargets[i]);
    }

    public static void Register(CombatTarget target)
    {
        if (target == null)
            return;

        int transformId = target.transform.GetInstanceID();
        TargetsByTransformId[transformId] = target;
        if (Targets.Contains(target))
        {
            SpatialIndex.RegisterOrUpdate(target);
            MarkSpatialChanged();
            return;
        }

        Targets.Add(target);
        SpatialIndex.RegisterOrUpdate(target);
        MarkSpatialChanged();
    }

    public static void Unregister(CombatTarget target)
    {
        if (target == null)
            return;

        int targetIndex = Targets.IndexOf(target);
        if (targetIndex >= 0)
        {
            Targets.RemoveAt(targetIndex);
            if (targetIndex < spatialMaintenanceCursor)
                spatialMaintenanceCursor--;
            if (spatialMaintenanceCursor >= Targets.Count)
                spatialMaintenanceCursor = 0;
        }
        TargetsByTransformId.Remove(target.transform.GetInstanceID());
        if (SpatialIndex.Unregister(target) || targetIndex >= 0)
            MarkSpatialChanged();
    }

    public static void NotifySpatialChanged(Transform targetTransform)
    {
        if (targetTransform == null
            || !TargetsByTransformId.TryGetValue(targetTransform.GetInstanceID(), out CombatTarget target)
            || target == null)
        {
            return;
        }

        SpatialIndex.RegisterOrUpdate(target);
        MarkSpatialChanged();
    }

    public static void NotifySpatialChanged(CombatTarget target)
    {
        if (target != null && Targets.Contains(target))
        {
            SpatialIndex.RegisterOrUpdate(target);
            MarkSpatialChanged();
        }
    }

    public static void NotifySpatialMovement(Transform targetTransform, Vector3 intendedRootWorldPosition)
    {
        if (targetTransform == null
            || !TargetsByTransformId.TryGetValue(targetTransform.GetInstanceID(), out CombatTarget target)
            || target == null)
        {
            return;
        }

        SpatialIndex.RegisterOrUpdate(target, target.ResolveSweptHurtVolume(intendedRootWorldPosition));
        MarkSpatialChanged();
    }

    public static void CollectPotentialTargets(
        Vector3 patternOrigin,
        float patternBroadphaseRadius,
        List<CombatTarget> results)
    {
        if (results == null)
            return;

        MaintainSpatialIndexOncePerFrame();
        SpatialIndex.CollectPotentialTargets(
            patternOrigin,
            Mathf.Max(0f, patternBroadphaseRadius),
            results);
    }

    public static void CollectTeamTargets(CombatTeam team, List<CombatTarget> results)
    {
        if (results == null)
            return;

        results.Clear();
        PruneNullTargets();
        for (int i = Targets.Count - 1; i >= 0; i--)
        {
            CombatTarget target = Targets[i];
            if (target.IsAlive && target.Team == team)
                results.Add(target);
        }
    }

    public static bool ValidateSpatialIndexIntegrity(out string error)
    {
        return SpatialIndex.ValidateIntegrity(out error);
    }

    private static void MaintainSpatialIndexOncePerFrame()
    {
        int currentFrame = Time.frameCount;
        if (lastSpatialMaintenanceFrame == currentFrame)
            return;

        MaintainSpatialIndex(SpatialMaintenanceBudgetPerQueryFrame);
        lastSpatialMaintenanceFrame = currentFrame;
    }

    private static void MaintainSpatialIndex(int budget)
    {
        int processed = 0;
        while (processed < budget && Targets.Count > 0)
        {
            if (spatialMaintenanceCursor >= Targets.Count)
                spatialMaintenanceCursor = 0;

            CombatTarget target = Targets[spatialMaintenanceCursor];
            if (target == null)
            {
                Targets.RemoveAt(spatialMaintenanceCursor);
                RebuildSpatialIndexFromTargets(); // 생명주기 누락은 드문 예외 경로에서만 전체 복구
                processed++;
                continue;
            }

            SpatialIndex.RegisterOrUpdate(target);
            MarkSpatialChanged();
            spatialMaintenanceCursor++;
            processed++;
        }

#if UNITY_EDITOR
        LastSpatialMaintenanceProcessedCountForValidation = processed;
#endif
    }

    private static bool PruneNullTargets()
    {
        bool removedAny = false;
        for (int i = Targets.Count - 1; i >= 0; i--)
        {
            if (Targets[i] != null)
                continue;

            Targets.RemoveAt(i);
            removedAny = true;
        }

        if (!removedAny)
            return false;

        RebuildSpatialIndexFromTargets();
        return true;
    }

    private static void RebuildSpatialIndexFromTargets()
    {
        TargetsByTransformId.Clear();
        SpatialIndex.Clear();
        for (int i = 0; i < Targets.Count; i++)
        {
            CombatTarget target = Targets[i];
            if (target == null)
                continue;

            TargetsByTransformId[target.transform.GetInstanceID()] = target;
            SpatialIndex.RegisterOrUpdate(target);
        }

        spatialMaintenanceCursor = 0;
        MarkSpatialChanged();
    }

    private static void MarkSpatialChanged()
    {
        unchecked { spatialRevision++; }
    }


#if UNITY_EDITOR
    public static void MaintainSpatialIndexForValidation(int budget)
    {
        MaintainSpatialIndex(Mathf.Max(0, budget));
        lastSpatialMaintenanceFrame = int.MinValue;
    }
#endif
}
