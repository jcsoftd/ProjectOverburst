using UnityEngine;

public static class ElementalCombatProcessor
{
    public static ElementalApplicationResultType Process(ElementalApplicationContext context)
    {
        if (context.TargetHealth == null
            || context.TargetHealth.IsDead
            || context.TargetHealth.CurrentHp <= 0f
            || context.StatusOwner == null
            || context.ActualDirectDamage <= 0f
            || !context.TriggersOnHitEffects
            || context.IsDamageOverTime
            || !ElementalStatusRules.TryGetRule(context.IncomingElement, out _))
        {
            return ElementalApplicationResultType.Ignored;
        }

        ElementalBasicStatusSet previousStatuses = ElementalBasicStatusSet.Capture(context.StatusOwner);
        if (context.CanTriggerReaction
            && ElementalReactionResolver.TryResolve(
                previousStatuses,
                context.IncomingElement,
                out ElementalReactionType reactionType))
        {
            if (reactionType == ElementalReactionType.Vaporize)
            {
                if (context.Target == null || !context.Target.IsAlive)
                    return ElementalApplicationResultType.ReactionFailed;

                ElementalReactionOwnerSnapshot owner = new ElementalReactionOwnerSnapshot(context);
                long sequenceId = ElementalReactionEvents.AllocateSequenceId();
                Vector3 center = context.Target.WorldCenter;
                context.StatusOwner.ClearAllStatuses(ElementalStatusClearReason.Reaction); // 반응과 상태 제거 단일 확정
                ElementalReactionEvents.RaiseReactionStarted(new ElementalReactionEvent(
                    sequenceId,
                    ElementalReactionType.Vaporize,
                    context.Target,
                    center,
                    owner));
                ElementalReactionProcExecutor.ExecuteVaporize(context, owner); // 반경 2.5m에 총 45%를 즉시 1회 적용
                return ElementalApplicationResultType.ReactionApplied;
            }

            if (reactionType == ElementalReactionType.ThermalFracture
                && context.Target != null
                && context.Target.IsAlive)
            {
                ElementalReactionOwnerSnapshot owner = new ElementalReactionOwnerSnapshot(context);
                context.StatusOwner.ClearAllStatuses(ElementalStatusClearReason.Reaction);
                ElementalReactionEvents.RaiseReactionStarted(new ElementalReactionEvent(
                    ElementalReactionEvents.AllocateSequenceId(),
                    ElementalReactionType.ThermalFracture,
                    context.Target,
                    context.Target.WorldCenter,
                    owner));
                ElementalReactionProcExecutor.ExecuteFracture(context, owner);
                return ElementalApplicationResultType.ReactionApplied;
            }

            if ((reactionType == ElementalReactionType.Plasma
                    || reactionType == ElementalReactionType.Freeze
                    || reactionType == ElementalReactionType.ColdCharge)
                && context.StatusOwner is IElementalReactionStateOwner pendingStateOwner)
            {
                ElementalReactionOwnerSnapshot owner = new ElementalReactionOwnerSnapshot(context);
                if (!TryApplyResultState(pendingStateOwner, reactionType, owner))
                    return ElementalApplicationResultType.ReactionFailed;

                context.StatusOwner.ClearAllStatuses(ElementalStatusClearReason.Reaction); // 생성 Hit은 결과만 저장
                ElementalReactionEvents.RaiseReactionStarted(new ElementalReactionEvent(
                    ElementalReactionEvents.AllocateSequenceId(),
                    reactionType,
                    context.Target,
                    context.Target != null ? context.Target.WorldCenter : context.HitPoint,
                    owner));
                return ElementalApplicationResultType.ReactionApplied;
            }

            if (reactionType == ElementalReactionType.ChainElectricity && context.Target != null)
            {
                ElementalReactionOwnerSnapshot owner = new ElementalReactionOwnerSnapshot(context);
                long sequenceId = ElementalReactionEvents.AllocateSequenceId();
                ElementalReactionEvents.RaiseReactionStarted(new ElementalReactionEvent(
                    sequenceId,
                    reactionType,
                    context.Target,
                    context.Target.WorldCenter,
                    owner));
                ElementalReactionChainExecutor.Execute(sequenceId, context, owner);
                return ElementalApplicationResultType.ReactionApplied;
            }

            return ElementalApplicationResultType.ReactionFailed;
        }

        if (!context.CanApplyStatus)
            return ElementalApplicationResultType.Ignored;

        return context.StatusOwner.TryApplyDirectHit(context.ToStatusApplication())
            ? ElementalApplicationResultType.StatusApplied
            : ElementalApplicationResultType.Ignored;
    }

    private static bool TryApplyResultState(
        IElementalReactionStateOwner stateOwner,
        ElementalReactionType reactionType,
        ElementalReactionOwnerSnapshot owner)
    {
        switch (reactionType)
        {
            case ElementalReactionType.Plasma:
                return stateOwner.TryApplyReactionState(new ElementalReactionStateApplication(
                    reactionType,
                    0f,
                    1f,
                    owner,
                    owner.ActualDirectDamage));
            case ElementalReactionType.Freeze:
                return stateOwner.TryApplyReactionState(new ElementalReactionStateApplication(
                    reactionType,
                    ElementalReactionRules.FreezeDuration,
                    1f,
                    owner,
                    0f,
                    0f,
                    0f));
            case ElementalReactionType.ColdCharge:
                return stateOwner.TryApplyReactionState(new ElementalReactionStateApplication(
                    reactionType,
                    ElementalReactionRules.ColdChargeDuration,
                    1f,
                    owner));
            default:
                return false;
        }
    }
}
