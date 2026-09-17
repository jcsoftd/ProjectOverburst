using UnityEngine;

public enum WeaponActionCompletionReason
{
    None,
    Completed,
    CancelledByRequest,
    CancelledByWeaponSwitch,
    CancelledByEvade,
    CancelledByMovement,
    CancelledByRecovery,
    CancelledByDeath,
    RuntimeDisabled,
    InvalidConfiguration
}

public enum WeaponActionCancelReason
{
    Request,
    WeaponSwitch,
    Evade,
    Movement,
    Recovery,
    Death,
    RuntimeDisabled,
    InvalidConfiguration
}

public readonly struct WeaponActionState
{
    public WeaponActionState(
        bool active,
        bool canContinue,
        Vector3 committedDirection,
        bool completed,
        bool cancelled,
        WeaponActionCompletionReason completionReason)
    {
        Active = active;
        CanContinue = canContinue;
        CurrentStepCommittedDirection = committedDirection;
        Completed = completed;
        Cancelled = cancelled;
        CompletionReason = completionReason;
    }

    public bool Active { get; }
    public bool CanContinue { get; }
    public Vector3 CurrentStepCommittedDirection { get; }
    public bool Completed { get; }
    public bool Cancelled { get; }
    public bool IsTerminal => Completed || Cancelled;
    public WeaponActionCompletionReason CompletionReason { get; }

    public static WeaponActionState Running(bool canContinue, Vector3 committedDirection)
    {
        return new WeaponActionState(
            true,
            canContinue,
            committedDirection,
            false,
            false,
            WeaponActionCompletionReason.None);
    }

    public static WeaponActionState Terminal(
        bool completed,
        Vector3 committedDirection,
        WeaponActionCompletionReason reason)
    {
        return new WeaponActionState(
            false,
            false,
            committedDirection,
            completed,
            !completed,
            reason);
    }
}
