using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;

// Equip starts preparation; weak hits refresh demand through the existing shared frame budget.
// These limits bound idle preparation only. An unprepared heavy still plays every effect.
public static class MeleeHeavyVfxPreparation
{
    public const int MaximumPreparedPerPrefab = 256;
    private static readonly List<CombatTarget> candidates = new List<CombatTarget>(128);
    private static readonly ProfilerMarker RequestMarker = new ProfilerMarker("Overburst.Cost.Heavy.PreparationRequest");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => candidates.Clear();

    public static void RequestForEquippedWeapon(PlayerEquipment equipment, WeaponElement element)
    {
        if (!Application.isPlaying || equipment == null || equipment.CurrentWeaponData == null) return;
        var melee = equipment.CurrentWeaponData.GetMeleeDefinition();
        var definition = melee != null ? melee.heavyAttackDefinition : null;
        if (definition == null) return;
        using var scope = RequestMarker.Auto();
        Request(definition.elementVfx.GetImpact(element), 1);
        if (element == WeaponElement.Light) Request(definition.elementVfx.GetLightImpact(false), 1);
        GameObject burst = element == WeaponElement.Fire ? definition.elementVfx.FireChainExplosion
            : element == WeaponElement.Ice ? definition.elementVfx.iceShatter
            : element == WeaponElement.Electric ? definition.elementVfx.electricDirectHit : null;
        if (burst == null) return;
        var source = equipment.GetComponent<CombatTarget>();
        if (source == null) return;
        CombatTargetRegistry.CollectTeamTargets(source.Team == CombatTeam.Enemy ? CombatTeam.PlayerParty : CombatTeam.Enemy, candidates);
        int alive = 0;
        foreach (var target in candidates) if (target != null && target.IsAlive) alive++;
        candidates.Clear();
        if (alive > 0) Request(burst, alive);
    }

    private static void Request(GameObject prefab, int nextBurst)
    {
        if (prefab == null) return;
        var stats = TransientVfxPool.GetStatistics(prefab);
        // Previous visible tails may overlap a legitimate recharge; leave them alive and prepare extras.
        int desired = Mathf.Min(MaximumPreparedPerPrefab, stats.Active + nextBurst);
        MeleeElementPoolMaintenance.Request(prefab, desired);
    }

    public static int RetainedCapacity(GameObject prefab)
    {
        int target = MeleeElementPoolMaintenance.GetTarget(prefab);
        if (target > 0) MeleeElementPoolMaintenance.Touch(prefab);
        return Mathf.Max(12, target);
    }
}
