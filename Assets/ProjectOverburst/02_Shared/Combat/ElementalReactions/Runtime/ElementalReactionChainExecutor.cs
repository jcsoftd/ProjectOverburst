using System.Collections.Generic;
using UnityEngine;

public static class ElementalReactionChainExecutor
{
    private static readonly List<CombatTarget> targetBuffer = new List<CombatTarget>(128);
    private static readonly List<int> visitedTargetIds = new List<int>(ElementalReactionRules.ChainMaximumTargetCount);

    public static void Execute(
        long sequenceId,
        ElementalApplicationContext context,
        ElementalReactionOwnerSnapshot owner)
    {
        CombatTarget currentTarget = context.Target;
        if (currentTarget == null)
            return;

        WeaponElement requiredStatus = context.IncomingElement == WeaponElement.Water
            ? WeaponElement.Electric
            : WeaponElement.Water;
        visitedTargetIds.Clear();
        Vector3 previousCenter = currentTarget.WorldCenter;
        bool currentTargetHasRequiredStatus = true;

        int hopCount = Mathf.Min(
            ElementalReactionRules.ChainMaximumTargetCount,
            ElementalReactionRules.ChainDamageCoefficients.Length);
        int executedHopCount = 0;
        for (int hopIndex = 0; hopIndex < hopCount; hopIndex++)
        {
            if (hopIndex > 0)
            {
                if (!ElementalReactionTargetQuery.TryFindNearestPreferredStatusTarget(
                        previousCenter,
                        ElementalReactionRules.ChainRadius,
                        requiredStatus,
                        owner.SourceTeam,
                        owner.SourceTargetId,
                        visitedTargetIds,
                        targetBuffer,
                        out currentTarget,
                        out currentTargetHasRequiredStatus))
                {
                    break;
                }
            }

            if (currentTarget == null || !currentTarget.IsAlive)
                break;

            visitedTargetIds.Add(currentTarget.TargetId);
            CombatHealth health = currentTarget.DamageReceiver;
            if (health == null || health.IsDead || health.CurrentHp <= 0f || !health.isActiveAndEnabled)
                continue;

            float coefficient = ElementalReactionRules.ChainDamageCoefficients[hopIndex];
            if (hopIndex > 0 && !currentTargetHasRequiredStatus)
                coefficient *= ElementalReactionRules.ChainFallbackDamageMultiplier;
            float damage = Mathf.Max(0f, context.ActualDirectDamage) * coefficient;
            Vector3 center = currentTarget.WorldCenter;
            previousCenter = center; // 파괴 뒤에도 다음 탐색 중심 보존
            health.TakeDamage(new DamageInfo(
                damage,
                center,
                owner.SourceActor,
                Vector3.zero,
                0f,
                false,
                false,
                false,
                default,
                true,
                owner.IncomingElement,
                owner.SourceWeaponRuntimeInstanceId,
                ElementalReactionType.ChainElectricity));

            ElementalReactionEvents.RaiseChainHopExecuted(new ElementalReactionChainHopEvent(
                sequenceId,
                hopIndex,
                currentTarget,
                center,
                coefficient,
                damage));
            executedHopCount++;
        }

        ElementalReactionEvents.RaiseChainCompleted(
            new ElementalReactionChainCompletedEvent(sequenceId, executedHopCount));
    }
}
