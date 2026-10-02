using System;
using UnityEngine;

public enum PlayerDodgeFollowUpKind { None, Light, Heavy }

public readonly struct PlayerDodgeFollowUpRequest
{
    public readonly PlayerDodgeFollowUpKind Kind;
    public readonly int EvadeExecutionId;
    public readonly string WeaponInstanceId;
    public readonly int InputRevision;
    public PlayerDodgeFollowUpRequest(PlayerDodgeFollowUpKind kind, int id, string weapon, int revision)
    { Kind = kind; EvadeExecutionId = id; WeaponInstanceId = weapon; InputRevision = revision; }
}

public sealed partial class PlayerCombatInputBuffer
{
    private int dodgeExecutionId;
    private int dodgeSampledFrame = -1;
    private string dodgeWeaponInstanceId;
    private PlayerDodgeFollowUpKind dodgeFollowUp;
    private int dodgeInputRevision;

    public PlayerDodgeFollowUpKind PendingDodgeFollowUp => dodgeFollowUp;

    public void BeginDodgeFollowUpWindow(int executionId)
    {
        ClearDodgeFollowUp();
        if (executionId <= 0 || !CanRead() || equipment == null || equipment.CurrentWeaponItem == null)
            return;
        dodgeExecutionId = executionId;
        dodgeWeaponInstanceId = equipment.CurrentWeaponItem.runtimeInstanceId;
        RefreshDodgeFollowUp(); // A fresh click on the successful Shift frame qualifies.
    }

    public void SampleDodgeFollowUp() { Refresh(); RefreshDodgeFollowUp(); }

    private bool IsDodgeOwnerValid()
    {
        return CanRead() && PlayerCombatModeController.IsSharedCombatModeActive()
            && equipment != null && equipment.CurrentWeaponItem != null
            && string.Equals(equipment.CurrentWeaponItem.runtimeInstanceId, dodgeWeaponInstanceId, StringComparison.Ordinal);
    }

    private void RefreshDodgeFollowUp()
    {
        if (dodgeExecutionId <= 0) return;
        if (!IsDodgeOwnerValid()) { ClearDodgeFollowUp(); return; }
        if (evade == null || !evade.IsEvading || evade.ActiveType != PlayerEvadeType.CombatDodge
            || evade.ActiveExecutionId != dodgeExecutionId || !evade.IsFollowUpInputWindowOpen)
            return;
        if (dodgeSampledFrame == Time.frameCount) return;
        dodgeSampledFrame = Time.frameCount;
        bool heavy = !heavyNeedsRelease && input.AimPressedThisFrame;
        bool light = !attackNeedsRelease && !PlayerPickupInteractor.IsPrimaryAttackSuppressed
            && input.AttackPressedThisFrame;
        if (heavy) { dodgeFollowUp = PlayerDodgeFollowUpKind.Heavy; heavyPending = false; }
        if (light)
        {
            if (dodgeFollowUp != PlayerDodgeFollowUpKind.Heavy) dodgeFollowUp = PlayerDodgeFollowUpKind.Light;
            attackPending = false;
        }
    }

    public bool TakeDodgeFollowUp(int executionId, bool completed, out PlayerDodgeFollowUpRequest request)
    {
        bool valid = completed && executionId == dodgeExecutionId && IsDodgeOwnerValid()
            && dodgeFollowUp != PlayerDodgeFollowUpKind.None;
        request = valid ? new PlayerDodgeFollowUpRequest(dodgeFollowUp, dodgeExecutionId, dodgeWeaponInstanceId, dodgeInputRevision) : default;
        ClearDodgeFollowUp();
        if (valid) { attackPending = false; heavyPending = false; }
        return valid;
    }

    public bool IsDodgeFollowUpRequestValid(in PlayerDodgeFollowUpRequest request)
    {
        return request.InputRevision == dodgeInputRevision && CanRead()
            && PlayerCombatModeController.IsSharedCombatModeActive()
            && equipment != null && equipment.CurrentWeaponItem != null
            && string.Equals(equipment.CurrentWeaponItem.runtimeInstanceId, request.WeaponInstanceId, StringComparison.Ordinal);
    }

    public void ClearDodgeFollowUp()
    {
        dodgeExecutionId = 0; dodgeSampledFrame = -1;
        dodgeWeaponInstanceId = null; dodgeFollowUp = PlayerDodgeFollowUpKind.None;
    }
}
