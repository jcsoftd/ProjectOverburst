using System;
using UnityEngine;

public partial class MeleeRuntime
{
    private PlayerDodgeFollowUpKind requestedDodgeFollowUp;
    private PlayerDodgeFollowUpKind activeDodgeFollowUp;
    private MeleeComboDefinition dodgeTrajectoryDefinition;
    private bool dodgeLightWindup;
    private PlayerDodgeFollowUpRequest dodgeLightWindupRequest;
    private float dodgeLightWindupStart, dodgeLightWindupDuration, dodgeHandoffProgress;
    private AnimationClip dodgeLightWindupClip;
    public const float DodgeLightComboBlendDuration = .20f;
    public const float DodgeLightMovementBlendDuration = .24f;
    public const float DodgeLightFinishBlendDuration = .20f;
    private bool dodgeLightSwingPlayed;
    private int dodgeLightSwingSequence;
    private float dodgeLightSwingStartedAt = -1f;
    private int dodgeResumeAfterStep = -1, dodgeResumeExecutionId;
    private string dodgeResumeWeapon;

    public void CaptureDodgeComboContinuation(int executionId, bool keepCurrentCombo)
    {
        int afterStep = activeDodgeFollowUp == PlayerDodgeFollowUpKind.Light ? dodgeResumeAfterStep : comboStepIndex;
        bool preserve = keepCurrentCombo && isAttacking && activeAttackUsesCombo && !activeAttackIsHeavy;
        dodgeResumeAfterStep = preserve ? afterStep : -1;
        dodgeResumeExecutionId = executionId;
        dodgeResumeWeapon = playerEquipment?.CurrentWeaponItem?.runtimeInstanceId;
    }

    public void DiscardDodgeComboContinuation()
    {
        dodgeResumeAfterStep = -1; dodgeResumeExecutionId = 0; dodgeResumeWeapon = null;
    }

    private void RestoreDodgeComboContinuation()
    {
        ResetComboState();
        if (dodgeResumeAfterStep >= 0 && playerEquipment?.CurrentWeaponItem?.runtimeInstanceId == dodgeResumeWeapon)
        {
            comboStepIndex = dodgeResumeAfterStep;
            lastComboWindowTime = Time.time; // A held dodge preserves the sequence across the ordinary reset timeout.
        }
        DiscardDodgeComboContinuation();
    }
    public bool IsDodgeLightWindupActive => dodgeLightWindup;

    public float DodgeLightWindupLead
    {
        get
        {
            ResolveReferences();
            var definition = playerEquipment?.CurrentWeaponData?.GetMeleeDefinition();
            var combo = definition?.dodgeAttackDefinition;
            if (combo == null || !combo.HasSteps || combo.GetStep(0).animationClip == null) return 0f;
            var step = combo.GetStep(0);
            var stats = UpperElementCombatUtility.ApplyRadianceAttackSpeed(
                FlaskCombatModifiers.Apply(playerEquipment.CurrentWeaponStats, gameObject), gameObject);
            float speed = combo.baseAnimationSpeed * step.animationSpeedMultiplier * MeleeAttackSpeedPolicy.ToPlaybackMultiplier(
                Mathf.Max(.01f, stats.meleeAttackSpeedMultiplier), definition.baseSettings.SafeAnimationPlaybackBaseline);
            return step.animationClip.length / Mathf.Max(.01f, speed) * step.attackPhases[0].SafeStart;
        }
    }

    public void PreviewDodgeLight(in PlayerDodgeFollowUpRequest request, float earliestStart)
    {
        ResolveReferences();
        var inputs = ResolveFacade()?.CombatInputs;
        if (isAttacking || playerAnimatorController == null || inputs == null || !inputs.IsDodgeFollowUpRequestValid(request))
        { CancelDodgeLightWindup(); return; }
        float start = Mathf.Max(earliestStart, request.RequestedAt);
        if (OverburstGameClock.UnscaledTime < start) return;
        var definition = playerEquipment.CurrentWeaponData.GetMeleeDefinition();
        var step = definition.dodgeAttackDefinition.GetStep(0);
        float duration = DodgeLightWindupLead / Mathf.Max(.001f, step.attackPhases[0].SafeStart);
        if (!dodgeLightWindup)
        {
            float progress = Mathf.Clamp01((OverburstGameClock.UnscaledTime - start) / duration);
            if (!playerAnimatorController.PlayMeleeCombatAttack(0, step.animationClip, step.animationClip.length / duration,
                duration * (1f - progress), .08f * Time.timeScale, true, progress)) return;
            dodgeLightSwingPlayed = false;
            dodgeLightSwingStartedAt = -1f;
            dodgeLightWindup = true; dodgeLightWindupRequest = request;
            dodgeLightWindupStart = start; dodgeLightWindupDuration = duration; dodgeLightWindupClip = step.animationClip;
        }
        playerAnimatorController.UpdateDodgeLightWindupClock(dodgeLightWindupClip, dodgeLightWindupDuration);
        PlayDodgeLightSwing();
    }

    private void PlayDodgeLightSwing()
    {
        if (dodgeLightSwingPlayed || playerEquipment?.CurrentWeaponData == null
            || playerEquipment.CurrentWeaponData.weaponClass != WeaponClass.Greatsword) return;
        dodgeLightSwingPlayed = true;
        // The clip has about .15 s of quiet lead-in. Start with the visual windup,
        // so its audible sweep arrives at the blade acceleration, not after impact.
        if (CombatActionSfxService.PlayGreatswordSwing(0, false, transform.position))
        {
            dodgeLightSwingStartedAt = OverburstGameClock.UnscaledTime;
            dodgeLightSwingSequence++;
        }
    }

    public bool CancelDodgeLightWindup()
    {
        if (!dodgeLightWindup) return false;
        dodgeLightWindup = false; dodgeLightWindupClip = null;
        playerAnimatorController?.CancelWeaponRuntimeState();
        return true;
    }

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
        Vector3 direction = request.Direction; direction.y = 0f;
        if (direction.sqrMagnitude <= .0001f) return false;
        direction.Normalize();
        dodgeHandoffProgress = dodgeLightWindup && request.Kind == PlayerDodgeFollowUpKind.Light
            && request.EvadeExecutionId == dodgeLightWindupRequest.EvadeExecutionId
            && request.InputRevision == dodgeLightWindupRequest.InputRevision
            ? Mathf.Clamp((OverburstGameClock.UnscaledTime - dodgeLightWindupStart) / Mathf.Max(.01f, dodgeLightWindupDuration), 0f, .95f) : 0f;
        if (request.Kind != PlayerDodgeFollowUpKind.Light || request.EvadeExecutionId != dodgeResumeExecutionId
            || request.WeaponInstanceId != dodgeResumeWeapon) DiscardDodgeComboContinuation();
        requestedDodgeFollowUp = request.Kind;
        try
        {
            ResetComboState();
            WeaponActionResult result;
            if (request.Kind == PlayerDodgeFollowUpKind.Heavy) result = TryStartHeavyAttack(direction);
            else
            {
                var action = new WeaponActionRequest(WeaponActionSource.PlayerInput, null, direction);
                result = TryStartAction(action, out _);
            }
            if (result != WeaponActionResult.Accepted) { CancelDodgeLightWindup(); DiscardDodgeComboContinuation(); return false; }
            suppressHandoffMoveCancelUntilRelease = HasRawMoveInput();
            bufferedHandoffComboContinuation = false;
            return true;
        }
        finally
        {
            requestedDodgeFollowUp = PlayerDodgeFollowUpKind.None; dodgeHandoffProgress = 0f;
            dodgeLightWindup = false; dodgeLightWindupClip = null;
        }
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
            // The dodge already supplied the travel; retain heavy judgement and remove only authored motion.
            activeAttackStep.movementPhases = Array.Empty<AttackMovementPhaseData>();
            activeAttackStep.visualHeightCurve = null;
            return false;
        }
        if (activeDodgeFollowUp != PlayerDodgeFollowUpKind.Light || !activeAttackUsesCombo) return false;
        dodgeTrajectoryDefinition = definition.dodgeAttackDefinition;
        activeAttackStep = dodgeTrajectoryDefinition.GetStep(0);
        activeAttackAnimationClip = activeAttackStep.animationClip;
        activeAttackAnimationSpeed = CalculateComboAnimationSpeed(dodgeTrajectoryDefinition, activeAttackStep);
        if (!dodgeLightWindup)
        {
            dodgeLightSwingPlayed = false;
            dodgeLightSwingStartedAt = -1f;
        }
        activeAttackTransitionDuration = dodgeLightWindup ? 0f : .08f;
        comboStepIndex = 0; // The separate opener keeps a valid combo slot; handoff resets to normal hit 1.
        return true;
    }
}
