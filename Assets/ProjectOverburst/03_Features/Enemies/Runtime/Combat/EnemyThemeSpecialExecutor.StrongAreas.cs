using System.Collections.Generic;
using UnityEngine;

public sealed partial class EnemyThemeSpecialExecutor
{
    private readonly List<CombatTarget> strongChargeTargets = new List<CombatTarget>(8);
    private readonly Collider[] strongChargeStartBuffer = new Collider[16];

    private CombatTarget ResolveStandardStrongChargeTarget(EnemyAbilityDefinition ability, Vector3 direction)
    {
        float reach = Mathf.Max(.8f, EnemyAttackThreatGeometry.ResolveRadius(actor, ability));
        float obstruction = reach;
        int count = Physics.SphereCastNonAlloc(Origin, EnemyAttackThreatGeometry.ChargeHalfWidth, direction,
            hits, reach, Mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (hits[i].collider != null && CombatTarget.Resolve(hits[i].collider) == null)
                obstruction = Mathf.Min(obstruction, hits[i].distance);
        count = Physics.OverlapSphereNonAlloc(Origin, EnemyAttackThreatGeometry.ChargeHalfWidth,
            strongChargeStartBuffer, Mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (strongChargeStartBuffer[i] != null && CombatTarget.Resolve(strongChargeStartBuffer[i]) == null)
                return null;
        CombatTargetRegistry.CollectPotentialTargets(Origin, reach + EnemyAttackThreatGeometry.ChargeHalfWidth, strongChargeTargets);
        CombatTarget source = GetComponent<CombatTarget>(), nearest = null; float best = float.PositiveInfinity;
        for (int i = 0; i < strongChargeTargets.Count; i++)
        {
            CombatTarget candidate = strongChargeTargets[i];
            if (candidate == null || !candidate.IsAlive || !CombatTargetFilter.CanDamage(source, candidate)
                || (Mask & (1 << candidate.gameObject.layer)) == 0
                || !EnemyAttackThreatGeometry.OverlapsStrongArea(actor, ability, Origin, direction, candidate.CurrentVolume)) continue;
            float entry = EnemyAttackThreatGeometry.StrongChargeEntry(Origin, direction, candidate.CurrentVolume);
            if (entry > obstruction + .0001f || entry >= best) continue;
            nearest = candidate; best = entry;
        }
        return nearest;
    }

    private void ResolveStandardStrongChargeHit(EnemyAbilityDefinition ability, Vector3 direction)
    {
        CombatTarget candidate = ResolveStandardStrongChargeTarget(ability, direction);
        if (candidate == null || candidate.DamageReceiver == null) return;
        candidate.DamageReceiver.TakeDamage(new DamageInfo(ResolveIncomingDamage(ability),
            EnemyAttackThreatGeometry.StrongContactPoint(Origin, candidate.CurrentVolume), gameObject, direction,
            sourceAttackSequenceId: attackSequenceId, sourceAttackPhaseIndex: 0, enemyAbility: ability));
        ImpactCount++;
    }
}
