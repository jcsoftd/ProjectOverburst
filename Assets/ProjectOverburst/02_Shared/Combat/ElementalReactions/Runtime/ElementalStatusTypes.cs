using System;
using UnityEngine;

public enum ElementalStatusChangeReason { Applied = 0, StackIncreased = 1, Refreshed = 2 }
public enum ElementalStatusRemoveReason { Expired = 0, Cleared = 1 }
public enum ElementalStatusClearReason { Death = 0, Reset = 1, Disabled = 2, Explicit = 3, Reaction = 4 }

public readonly struct ElementalStatusApplication
{
    public readonly WeaponElement Element;
    public readonly float ActualDirectDamage;
    public readonly GameObject SourceActor;
    public readonly string SourceWeaponRuntimeInstanceId;
    public readonly bool TriggersOnHitEffects;
    public readonly bool IsDamageOverTime;
    public readonly Vector3 HitPoint;
    public readonly Vector3 Direction;

    public ElementalStatusApplication(
        WeaponElement element,
        float actualDirectDamage,
        GameObject sourceActor,
        string sourceWeaponRuntimeInstanceId,
        bool triggersOnHitEffects,
        bool isDamageOverTime,
        Vector3 hitPoint,
        Vector3 direction)
    {
        Element = element;
        ActualDirectDamage = actualDirectDamage;
        SourceActor = sourceActor;
        SourceWeaponRuntimeInstanceId = sourceWeaponRuntimeInstanceId ?? string.Empty;
        TriggersOnHitEffects = triggersOnHitEffects;
        IsDamageOverTime = isDamageOverTime;
        HitPoint = hitPoint;
        Direction = direction;
    }
}

public readonly struct ElementalStatusOwnerSnapshot
{
    public readonly GameObject SourceActor;
    public readonly string SourceWeaponRuntimeInstanceId;
    public readonly WeaponElement Element;
    public readonly float ActualDirectDamage;
    public readonly Vector3 HitPoint;
    public readonly Vector3 Direction;

    public ElementalStatusOwnerSnapshot(ElementalStatusApplication application)
    {
        SourceActor = application.SourceActor;
        SourceWeaponRuntimeInstanceId = application.SourceWeaponRuntimeInstanceId ?? string.Empty;
        Element = application.Element;
        ActualDirectDamage = Mathf.Max(0f, application.ActualDirectDamage);
        HitPoint = application.HitPoint;
        Direction = application.Direction;
    }
}

public readonly struct ElementalStatusSnapshot
{
    public readonly WeaponElement Element;
    public readonly bool IsActive;
    public readonly int StackCount;
    public readonly float RemainingDuration;
    public readonly float MoveSpeedMultiplier;
    public readonly float ActionSpeedMultiplier;
    public readonly ElementalStatusOwnerSnapshot Owner;

    public ElementalStatusSnapshot(
        WeaponElement element,
        bool isActive,
        int stackCount,
        float remainingDuration,
        float moveSpeedMultiplier,
        float actionSpeedMultiplier,
        ElementalStatusOwnerSnapshot owner)
    {
        Element = element;
        IsActive = isActive;
        StackCount = Mathf.Max(0, stackCount);
        RemainingDuration = Mathf.Max(0f, remainingDuration);
        MoveSpeedMultiplier = Mathf.Clamp01(moveSpeedMultiplier);
        ActionSpeedMultiplier = Mathf.Clamp01(actionSpeedMultiplier);
        Owner = owner;
    }
}

public interface IElementalStatusReceiver
{
    bool TryApplyDirectHit(ElementalStatusApplication application);
    bool TryGetStatus(WeaponElement element, out ElementalStatusSnapshot snapshot);
    void ClearAllStatuses(ElementalStatusClearReason reason = ElementalStatusClearReason.Explicit);

    event Action<ElementalStatusSnapshot, ElementalStatusChangeReason> StatusChanged;
    event Action<WeaponElement, ElementalStatusRemoveReason> StatusRemoved;
    event Action<ElementalStatusClearReason> StatusesCleared;
}
