using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class CombatSpatialIndex
{
    public const float DefaultCellSize = 4f;

    private readonly CombatSpatialIndexCore<CombatTarget> core;
    private readonly List<CombatTarget> candidates = new List<CombatTarget>(128);

    public CombatSpatialIndex(float cellSize = DefaultCellSize)
    {
        core = new CombatSpatialIndexCore<CombatTarget>(cellSize);
    }

    public float CellSize => core.CellSize;
    public int Count => core.Count;
    public int OccupiedCellCount => core.OccupiedCellCount;

    public bool RegisterOrUpdate(CombatTarget target)
    {
        if (target == null)
            return false;

        return RegisterOrUpdate(target, target.CurrentVolume);
    }

    public bool RegisterOrUpdate(CombatTarget target, CombatTargetVolume volume)
    {
        if (target == null)
            return false;

        return core.RegisterOrUpdate(
            target.TargetId,
            target,
            volume.Center.x,
            volume.Center.z,
            volume.Radius);
    }

    public bool Unregister(CombatTarget target)
    {
        return target != null && core.Remove(target.TargetId);
    }

    public void Clear()
    {
        core.Clear();
        candidates.Clear();
    }

    public void CollectPotentialTargets(Vector3 origin, float radius, List<CombatTarget> results)
    {
        if (results == null)
            throw new ArgumentNullException(nameof(results));

        core.CollectPotential(origin.x, origin.z, Mathf.Max(0f, radius), candidates);
        results.Clear();
        for (int i = 0; i < candidates.Count; i++)
        {
            CombatTarget target = candidates[i];
            if (target == null || !target.IsAlive)
                continue;

            CombatTargetVolume currentVolume = target.CurrentVolume;
            Vector3 offset = currentVolume.Center - origin;
            float combinedRadius = Mathf.Max(0f, radius) + currentVolume.Radius;
            float planarDistanceSquared = offset.x * offset.x + offset.z * offset.z;
            if (planarDistanceSquared <= combinedRadius * combinedRadius)
                results.Add(target); // 현재 Volume으로 마지막 정확도 확인
        }
    }

    public bool ValidateIntegrity(out string error)
    {
        return core.ValidateIntegrity(out error);
    }
}
