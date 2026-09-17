using UnityEngine;

public enum WeaponActionSource
{
    PlayerInput,
    CompanionAI
}

public enum WeaponActionResult
{
    Accepted,
    RejectedBusy,
    RejectedNotReady,
    RejectedInvalidTarget,
    RejectedOutOfRange,
    RejectedUnsupported
}

public readonly struct WeaponActionRequest
{
    public WeaponActionRequest(
        WeaponActionSource source,
        CombatTarget target,
        Vector3 aimDirection)
    {
        Source = source;
        Target = target;
        AimDirection = aimDirection;
    }

    public WeaponActionSource Source { get; }
    public CombatTarget Target { get; }
    public Vector3 AimDirection { get; }
}
