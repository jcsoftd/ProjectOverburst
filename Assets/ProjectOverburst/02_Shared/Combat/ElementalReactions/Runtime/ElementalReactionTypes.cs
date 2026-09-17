using System;
using UnityEngine;

public enum ElementalReactionType
{
    None = 0,
    Vaporize = 1,
    ThermalFracture = 2,
    Plasma = 3,
    Freeze = 4,
    Shatter = 5,
    ChainElectricity = 6,
    ColdCharge = 7
}

public enum ElementalApplicationResultType
{
    Ignored = 0,
    StatusApplied = 1,
    ReactionFailed = 2,
    ReactionApplied = 3
}

public enum ElementalReactionStateChangeReason { Applied = 0, Refreshed = 1 }
public enum ElementalReactionStateRemoveReason { Expired = 0, Cleared = 1, Consumed = 2 }
public enum ElementalReactionProcType { Shatter = 0, Plasma = 1, ColdCharge = 2 }

public readonly struct ElementalReactionStateApplication
{
    public readonly ElementalReactionType ReactionType;
    public readonly float Duration;
    public readonly float IncomingDamageMultiplier;
    public readonly float StoredDamage;
    public readonly float MoveSpeedMultiplier;
    public readonly float ActionSpeedMultiplier;
    public readonly ElementalReactionOwnerSnapshot Owner;

    public ElementalReactionStateApplication(
        ElementalReactionType reactionType,
        float duration,
        float incomingDamageMultiplier,
        ElementalReactionOwnerSnapshot owner,
        float storedDamage = 0f,
        float moveSpeedMultiplier = 1f,
        float actionSpeedMultiplier = 1f)
    {
        ReactionType = reactionType;
        Duration = Mathf.Max(0f, duration);
        IncomingDamageMultiplier = Mathf.Max(1f, incomingDamageMultiplier);
        StoredDamage = Mathf.Max(0f, storedDamage);
        MoveSpeedMultiplier = Mathf.Clamp01(moveSpeedMultiplier);
        ActionSpeedMultiplier = Mathf.Clamp01(actionSpeedMultiplier);
        Owner = owner;
    }
}

public readonly struct ElementalReactionStateSnapshot
{
    public readonly ElementalReactionType ReactionType;
    public readonly bool IsActive;
    public readonly float RemainingDuration;
    public readonly float IncomingDamageMultiplier;
    public readonly float StoredDamage;
    public readonly float MoveSpeedMultiplier;
    public readonly float ActionSpeedMultiplier;
    public readonly ElementalReactionOwnerSnapshot Owner;

    public ElementalReactionStateSnapshot(
        ElementalReactionType reactionType,
        bool isActive,
        float remainingDuration,
        float incomingDamageMultiplier,
        ElementalReactionOwnerSnapshot owner,
        float storedDamage = 0f,
        float moveSpeedMultiplier = 1f,
        float actionSpeedMultiplier = 1f)
    {
        ReactionType = reactionType;
        IsActive = isActive;
        RemainingDuration = Mathf.Max(0f, remainingDuration);
        IncomingDamageMultiplier = Mathf.Max(1f, incomingDamageMultiplier);
        StoredDamage = Mathf.Max(0f, storedDamage);
        MoveSpeedMultiplier = Mathf.Clamp01(moveSpeedMultiplier);
        ActionSpeedMultiplier = Mathf.Clamp01(actionSpeedMultiplier);
        Owner = owner;
    }
}

public interface IElementalReactionStateOwner
{
    bool TryApplyReactionState(ElementalReactionStateApplication application);
    bool TryGetReactionState(ElementalReactionType reactionType, out ElementalReactionStateSnapshot snapshot);
    void ResolvePendingReactionResults(ElementalApplicationContext triggerContext);
    void ClearAllReactionStates(ElementalStatusClearReason reason = ElementalStatusClearReason.Explicit);

    event Action<ElementalReactionStateSnapshot, ElementalReactionStateChangeReason> ReactionStateChanged;
    event Action<ElementalReactionType, ElementalReactionStateRemoveReason> ReactionStateRemoved;
    event Action<ElementalStatusClearReason> ReactionStatesCleared;
}

public readonly struct ElementalReactionTriggerSnapshot
{
    public readonly GameObject SourceActor;
    public readonly string SourceWeaponRuntimeInstanceId;
    public readonly WeaponElement Element;
    public readonly float ActualDamage;
    public readonly Vector3 HitPoint;
    public readonly Vector3 Direction;

    public ElementalReactionTriggerSnapshot(ElementalApplicationContext context)
    {
        SourceActor = context.SourceActor;
        SourceWeaponRuntimeInstanceId = context.SourceWeaponRuntimeInstanceId ?? string.Empty;
        Element = context.IncomingElement;
        ActualDamage = Mathf.Max(0f, context.ActualDirectDamage);
        HitPoint = context.HitPoint;
        Direction = context.Direction;
    }
}

public readonly struct ElementalReactionProcEvent
{
    public readonly long SequenceId;
    public readonly ElementalReactionProcType ProcType;
    public readonly CombatTarget TriggerTarget;
    public readonly Vector3 Center;
    public readonly ElementalReactionOwnerSnapshot Owner;
    public readonly ElementalReactionTriggerSnapshot TriggerHit;
    public readonly int DamagedTargetCount;

    public ElementalReactionProcEvent(
        long sequenceId,
        ElementalReactionProcType procType,
        CombatTarget triggerTarget,
        Vector3 center,
        ElementalReactionOwnerSnapshot owner,
        ElementalReactionTriggerSnapshot triggerHit,
        int damagedTargetCount)
    {
        SequenceId = sequenceId;
        ProcType = procType;
        TriggerTarget = triggerTarget;
        Center = center;
        Owner = owner;
        TriggerHit = triggerHit;
        DamagedTargetCount = Mathf.Max(0, damagedTargetCount);
    }
}

public readonly struct ElementalReactionChainHopEvent
{
    public readonly long SequenceId;
    public readonly int HopIndex;
    public readonly CombatTarget Target;
    public readonly Vector3 Center;
    public readonly float Coefficient;
    public readonly float Damage;

    public ElementalReactionChainHopEvent(
        long sequenceId,
        int hopIndex,
        CombatTarget target,
        Vector3 center,
        float coefficient,
        float damage)
    {
        SequenceId = sequenceId;
        HopIndex = Mathf.Max(0, hopIndex);
        Target = target;
        Center = center;
        Coefficient = Mathf.Max(0f, coefficient);
        Damage = Mathf.Max(0f, damage);
    }
}

public readonly struct ElementalReactionChainCompletedEvent
{
    public readonly long SequenceId;
    public readonly int HopCount;

    public ElementalReactionChainCompletedEvent(long sequenceId, int hopCount)
    {
        SequenceId = sequenceId;
        HopCount = Mathf.Clamp(hopCount, 0, ElementalReactionRules.ChainMaximumTargetCount);
    }
}

public readonly struct ElementalBasicStatusSet
{
    public readonly bool Burning;
    public readonly bool Wet;
    public readonly bool Chilled;
    public readonly bool Shocked;

    public ElementalBasicStatusSet(bool burning, bool wet, bool chilled, bool shocked)
    {
        Burning = burning;
        Wet = wet;
        Chilled = chilled;
        Shocked = shocked;
    }

    public static ElementalBasicStatusSet Capture(IElementalStatusReceiver receiver)
    {
        if (receiver == null)
            return default;

        return new ElementalBasicStatusSet(
            receiver.TryGetStatus(WeaponElement.Fire, out _),
            receiver.TryGetStatus(WeaponElement.Water, out _),
            receiver.TryGetStatus(WeaponElement.Ice, out _),
            receiver.TryGetStatus(WeaponElement.Electric, out _));
    }
}

public readonly struct ElementalApplicationContext
{
    public readonly CombatHealth TargetHealth;
    public readonly CombatTarget Target;
    public readonly IElementalStatusReceiver StatusOwner;
    public readonly WeaponElement IncomingElement;
    public readonly float ActualDirectDamage;
    public readonly GameObject SourceActor;
    public readonly string SourceWeaponRuntimeInstanceId;
    public readonly Vector3 HitPoint;
    public readonly Vector3 Direction;
    public readonly bool CanTriggerReaction;
    public readonly bool CanApplyStatus;
    public readonly bool IsDamageOverTime;
    public readonly bool TriggersOnHitEffects;

    public ElementalApplicationContext(
        CombatHealth targetHealth,
        CombatTarget target,
        IElementalStatusReceiver statusOwner,
        WeaponElement incomingElement,
        float actualDirectDamage,
        GameObject sourceActor,
        string sourceWeaponRuntimeInstanceId,
        Vector3 hitPoint,
        Vector3 direction,
        bool canTriggerReaction,
        bool canApplyStatus,
        bool isDamageOverTime,
        bool triggersOnHitEffects)
    {
        TargetHealth = targetHealth;
        Target = target;
        StatusOwner = statusOwner;
        IncomingElement = incomingElement;
        ActualDirectDamage = Mathf.Max(0f, actualDirectDamage);
        SourceActor = sourceActor;
        SourceWeaponRuntimeInstanceId = sourceWeaponRuntimeInstanceId ?? string.Empty;
        HitPoint = hitPoint;
        Direction = direction;
        CanTriggerReaction = canTriggerReaction;
        CanApplyStatus = canApplyStatus;
        IsDamageOverTime = isDamageOverTime;
        TriggersOnHitEffects = triggersOnHitEffects;
    }

    public ElementalStatusApplication ToStatusApplication()
    {
        return new ElementalStatusApplication(
            IncomingElement,
            ActualDirectDamage,
            SourceActor,
            SourceWeaponRuntimeInstanceId,
            TriggersOnHitEffects,
            IsDamageOverTime,
            HitPoint,
            Direction);
    }
}

public readonly struct ElementalReactionOwnerSnapshot
{
    public readonly GameObject SourceActor;
    public readonly string SourceWeaponRuntimeInstanceId;
    public readonly WeaponElement IncomingElement;
    public readonly float ActualDirectDamage;
    public readonly CombatTeam SourceTeam;
    public readonly int SourceTargetId;

    public ElementalReactionOwnerSnapshot(ElementalApplicationContext context)
    {
        SourceActor = context.SourceActor;
        SourceWeaponRuntimeInstanceId = context.SourceWeaponRuntimeInstanceId ?? string.Empty;
        IncomingElement = context.IncomingElement;
        ActualDirectDamage = Mathf.Max(0f, context.ActualDirectDamage);
        CombatTarget sourceTarget = context.SourceActor != null
            ? context.SourceActor.GetComponentInParent<CombatTarget>()
            : null;
        SourceTeam = sourceTarget != null ? sourceTarget.Team : CombatTeam.Neutral;
        SourceTargetId = sourceTarget != null ? sourceTarget.TargetId : 0;
    }
}

public readonly struct ElementalReactionEvent
{
    public readonly long SequenceId;
    public readonly ElementalReactionType ReactionType;
    public readonly CombatTarget TriggerTarget;
    public readonly Vector3 Center;
    public readonly ElementalReactionOwnerSnapshot Owner;

    public ElementalReactionEvent(
        long sequenceId,
        ElementalReactionType reactionType,
        CombatTarget triggerTarget,
        Vector3 center,
        ElementalReactionOwnerSnapshot owner)
    {
        SequenceId = sequenceId;
        ReactionType = reactionType;
        TriggerTarget = triggerTarget;
        Center = center;
        Owner = owner;
    }
}

public static class ElementalReactionEvents
{
    public static event Action<ElementalReactionEvent> ReactionStarted;
    public static event Action<ElementalReactionProcEvent> ReactionProcExecuted;
    public static event Action<ElementalReactionChainHopEvent> ChainHopExecuted;
    public static event Action<ElementalReactionChainCompletedEvent> ChainCompleted;
    private static long nextSequenceId = 1;

    internal static long AllocateSequenceId()
    {
        if (nextSequenceId <= 0)
            nextSequenceId = 1;

        return nextSequenceId++;
    }

    internal static void RaiseReactionStarted(ElementalReactionEvent reactionEvent)
    {
        ReactionStarted?.Invoke(reactionEvent);
    }

    internal static void RaiseReactionProcExecuted(ElementalReactionProcEvent procEvent)
    {
        ReactionProcExecuted?.Invoke(procEvent);
    }

    internal static void RaiseChainHopExecuted(ElementalReactionChainHopEvent hopEvent)
    {
        ChainHopExecuted?.Invoke(hopEvent);
    }

    internal static void RaiseChainCompleted(ElementalReactionChainCompletedEvent completedEvent)
    {
        ChainCompleted?.Invoke(completedEvent);
    }

    internal static void Reset()
    {
        ReactionStarted = null;
        ReactionProcExecuted = null;
        ChainHopExecuted = null;
        ChainCompleted = null;
        nextSequenceId = 1;
    }
}

public static class ElementalReactionStateEvents
{
    public static event Action<IElementalReactionStateOwner, ElementalReactionStateSnapshot,
        ElementalReactionStateChangeReason> StateChanged;
    public static event Action<IElementalReactionStateOwner, ElementalReactionType,
        ElementalReactionStateRemoveReason> StateRemoved;
    public static event Action<IElementalReactionStateOwner, ElementalStatusClearReason> StatesCleared;

    internal static void RaiseStateChanged(
        IElementalReactionStateOwner owner,
        ElementalReactionStateSnapshot snapshot,
        ElementalReactionStateChangeReason reason)
    {
        StateChanged?.Invoke(owner, snapshot, reason);
    }

    internal static void RaiseStateRemoved(
        IElementalReactionStateOwner owner,
        ElementalReactionType reactionType,
        ElementalReactionStateRemoveReason reason)
    {
        StateRemoved?.Invoke(owner, reactionType, reason);
    }

    internal static void RaiseStatesCleared(
        IElementalReactionStateOwner owner,
        ElementalStatusClearReason reason)
    {
        StatesCleared?.Invoke(owner, reason);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        StateChanged = null;
        StateRemoved = null;
        StatesCleared = null;
    }
}
