using System;
using UnityEngine;

public partial class MeleeRuntime
{
    private PlayerDodgeFollowUpKind requestedDodgeFollowUp;
    private PlayerDodgeFollowUpKind activeDodgeFollowUp;
    private MeleeComboDefinition dodgeTrajectoryDefinition;
    public PlayerDodgeFollowUpKind ActiveDodgeFollowUp => isAttacking ? activeDodgeFollowUp : PlayerDodgeFollowUpKind.None;

    public bool TryStartDodgeFollowUp(in PlayerDodgeFollowUpRequest request)
    {
        ResolveReferences();
        if (!CanUseCurrentWeapon || playerEquipment.CurrentWeaponItem == null
            || !string.Equals(playerEquipment.CurrentWeaponItem.runtimeInstanceId, request.WeaponInstanceId, StringComparison.Ordinal))
            return false;
        var definition = playerEquipment.CurrentWeaponData.GetMeleeDefinition();
        if (definition == null || request.Kind == PlayerDodgeFollowUpKind.None) return false;
        if (request.Kind == PlayerDodgeFollowUpKind.Light
            && (definition.dodgeAttackDefinition == null || !definition.dodgeAttackDefinition.HasSteps)) return false;
        if (request.Kind == PlayerDodgeFollowUpKind.Heavy
            && (definition.dodgeHeavyAnimationClip == null || definition.heavyAttackDefinition == null
                || definition.heavyAttackDefinition.attack.animationClip == null
                || Mathf.Abs(definition.dodgeHeavyAnimationClip.length - definition.heavyAttackDefinition.attack.animationClip.length) > .0001f))
            return false;
        requestedDodgeFollowUp = request.Kind;
        try
        {
            ResetComboState();
            WeaponActionResult result;
            if (request.Kind == PlayerDodgeFollowUpKind.Heavy) result = TryStartHeavyAttack(CaptureAttackStartDirection());
            else
            {
                var action = new WeaponActionRequest(WeaponActionSource.PlayerInput, null, CaptureAttackStartDirection());
                result = TryStartAction(action, out _);
            }
            if (result != WeaponActionResult.Accepted) return false;
            suppressHandoffMoveCancelUntilRelease = HasRawMoveInput();
            bufferedHandoffComboContinuation = false;
            return true;
        }
        finally { requestedDodgeFollowUp = PlayerDodgeFollowUpKind.None; }
    }

    private bool ResolveDodgeFollowUpAnimation()
    {
        activeDodgeFollowUp = requestedDodgeFollowUp;
        dodgeTrajectoryDefinition = null;
        var definition = activeWeaponData != null ? activeWeaponData.GetMeleeDefinition() : null;
        if (definition == null) return false;
        if (activeAttackIsHeavy && activeDodgeFollowUp == PlayerDodgeFollowUpKind.Heavy)
        {
            activeAttackAnimationClip = definition.dodgeHeavyAnimationClip;
            return false; // Keep every field of the original heavy step.
        }
        if (activeDodgeFollowUp != PlayerDodgeFollowUpKind.Light || !activeAttackUsesCombo) return false;
        dodgeTrajectoryDefinition = definition.dodgeAttackDefinition;
        activeAttackStep = dodgeTrajectoryDefinition.GetStep(0);
        activeAttackAnimationClip = activeAttackStep.animationClip;
        activeAttackAnimationSpeed = CalculateComboAnimationSpeed(dodgeTrajectoryDefinition, activeAttackStep);
        activeAttackTransitionDuration = Mathf.Max(0f, activeAttackStep.transitionDuration);
        comboStepIndex = 0; // The opener occupies normal hit 1; continuation chooses hit 2.
        return true;
    }
}
