using System.Collections.Generic;
using UnityEngine;

// 60D shared helpers for the light/dark upper-element schedulers.
public static class UpperElementCombatUtility
{
    private static readonly Dictionary<GameObject, float[]> BaseSpeeds = new Dictionary<GameObject, float[]>();
    private static readonly List<ParticleSystem> Particles = new List<ParticleSystem>(128);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => BaseSpeeds.Clear();

    // Pooled instances keep their last speed. Always restore from the prefab's authored values.
    public static void ApplyPlaybackSpeed(GameObject prefab, GameObject instance, float speed)
    {
        if (prefab == null || instance == null) return;
        if (!BaseSpeeds.TryGetValue(prefab, out float[] speeds))
        {
            prefab.GetComponentsInChildren(true, Particles);
            speeds = new float[Particles.Count];
            for (int i = 0; i < Particles.Count; i++) speeds[i] = Particles[i].main.simulationSpeed;
            BaseSpeeds[prefab] = speeds;
        }
        instance.GetComponentsInChildren(true, Particles);
        float factor = speed > 0f ? speed : 1f;
        for (int i = 0; i < Particles.Count && i < speeds.Length; i++)
        {
            ParticleSystem.MainModule main = Particles[i].main;
            main.simulationSpeed = speeds[i] * factor;
        }
        Particles.Clear();
    }

    public static bool IsValidEnemy(CombatTarget target, CombatTeam sourceTeam)
    {
        return target != null && target.IsAlive && target.DamageReceiver != null
            && !target.DamageReceiver.IsDead && target.Team != sourceTeam;
    }

    public static bool IsInRadius(CombatTarget target, Vector3 center, float radius)
    {
        CombatTargetVolume volume = target.CurrentHurtVolume;
        Vector3 delta = volume.Center - center;
        delta.y = 0f;
        float range = radius + volume.Radius;
        return delta.sqrMagnitude <= range * range;
    }

    public static bool IsInHeight(CombatTarget target, Vector3 center, float verticalTolerance)
    {
        CombatTargetVolume volume = target.CurrentHurtVolume;
        return Mathf.Max(0f, Mathf.Abs(volume.Center.y - center.y) - volume.HalfHeight) <= verticalTolerance;
    }

    public static float PlanarDistance(Vector3 a, Vector3 b)
    {
        Vector3 delta = a - b;
        delta.y = 0f;
        return delta.magnitude;
    }

    // 60D radiance: multiplies after the additive 1.5 cap, then clamps to the light-only final cap.
    public static WeaponFinalStats ApplyRadianceAttackSpeed(WeaponFinalStats stats, GameObject actor)
    {
        OverburstElementEnergy energy = actor != null ? actor.GetComponent<OverburstElementEnergy>() : null;
        if (energy == null || energy.RadianceStacks <= 0) return stats;
        return CombatBalanceFormulas.ApplyRadianceAttackSpeed(stats, OverburstElementTuning.Current, energy.RadianceStacks);
    }

    public static EnemyGradeType GradeOf(Component actor)
    {
        EnemyRank rank = actor != null ? actor.GetComponent<EnemyRank>() : null;
        return rank != null ? rank.GradeType : EnemyGradeType.Normal;
    }

    public static void DealDerivedDamage(CombatHealth target, float damage, Vector3 point, GameObject source,
        Vector3 direction, WeaponElement element)
    {
        if (target == null || target.IsDead || !(damage > 0f)) return;
        target.TakeDamage(new DamageInfo(damage, point, source, direction,
            triggersOnHitEffects: false, suppressDefaultHitVfx: true,
            element: element, playerAttackKind: PlayerAttackKind.Elemental));
    }
}
