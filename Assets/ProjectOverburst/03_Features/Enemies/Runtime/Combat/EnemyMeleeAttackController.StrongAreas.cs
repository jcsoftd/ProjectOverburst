using System.Collections.Generic;
using UnityEngine;

public partial class EnemyMeleeAttackController
{
    private readonly List<CombatTarget> strongAreaTargets = new List<CombatTarget>(8);

    private bool WouldStrongAreaHitTarget(EnemyAbilityDefinition ability, CombatTarget candidate)
    {
        if (candidate == null || !candidate.IsAlive || !CombatTargetFilter.CanDamage(combatTarget, candidate)
            || (targetLayer.value & (1 << candidate.gameObject.layer)) == 0) return false;
        Vector3 center = EnemyAttackThreatGeometry.ResolveImpactCenter(actor, ability, AttackPoint.position);
        return EnemyAttackThreatGeometry.OverlapsStrongArea(actor, ability, center, transform.forward, candidate.CurrentVolume)
            && (!ability.RequireLineOfSight || HasDirectLineOfSight(candidate, candidate.CurrentVolume.Center, center));
    }

    private void ResolveStandardStrongAreaHit(float resolvedDamage, EnemyAbilityDefinition ability)
    {
        int token = executionGeneration, sequence = attackSequenceId, phase = attackPhaseIndex;
        uint lease = WeakOwnerLease;
        Vector3 center = EnemyAttackThreatGeometry.ResolveImpactCenter(actor, ability, AttackPoint.position);
        CombatTargetRegistry.CollectPotentialTargets(center, EnemyAttackThreatGeometry.ResolveRadius(actor, ability), strongAreaTargets);
        damagedTargets.Clear();
        for (int i = 0; i < strongAreaTargets.Count; i++)
        {
            if (IsAttackInterrupted() || token != executionGeneration || sequence != attackSequenceId || lease != WeakOwnerLease) break;
            CombatTarget candidate = strongAreaTargets[i];
            if (!WouldStrongAreaHitTarget(ability, candidate)) continue;
            CombatHealth receiver = candidate.DamageReceiver;
            if (receiver == null || receiver == health || receiver.IsDead || !damagedTargets.Add(receiver)) continue;
            Vector3 direction = candidate.transform.position - transform.position; direction.y = 0f;
            receiver.TakeDamage(new DamageInfo(resolvedDamage,
                EnemyAttackThreatGeometry.StrongContactPoint(center, candidate.CurrentVolume), gameObject,
                direction.sqrMagnitude > .0001f ? direction.normalized : transform.forward,
                sourceAttackSequenceId: sequence, sourceAttackPhaseIndex: phase, enemyAbility: ability));
            if (token != executionGeneration || sequence != attackSequenceId || lease != WeakOwnerLease) break;
        }
    }
}
