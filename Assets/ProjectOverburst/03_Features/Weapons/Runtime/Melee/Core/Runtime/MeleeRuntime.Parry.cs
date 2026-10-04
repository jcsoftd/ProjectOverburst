using UnityEngine;

public partial class MeleeRuntime
{
    private enum HeavyParryStage { None, Parry, Bridge }
    private HeavyParryStage heavyParryStage;
    private float heavyParryElapsed, heavyParryDuration, heavyParryBridgeDuration;
    private float heavyParrySavedElapsed, heavyParrySavedProgress;
    private float heavyParryMovementFloor;
    private bool heavyParrySwingPending;
    private bool heavyParryOnly;
    private int heavyParryStartedFrame;
    public bool IsHeavyParryMotionActive => heavyParryStage != HeavyParryStage.None;
    public float HeavyParryContactDelay { get; private set; }

    public static bool IsHeavyParryClockPaused
    {
        get
        {
            var kind = OverburstTimeEffectArbiter.ActiveKind;
            return Time.timeScale <= 0f || OverburstTimeEffectArbiter.IsPaused
                || GameplayInputBlocker.IsGameplayInputBlocked
                || kind == OverburstTimeEffectKind.HitStop || kind == OverburstTimeEffectKind.ParryHitStop;
        }
    }

    private bool TryBeginHeavyParryMotion(bool parryOnly = false)
    {
        if (!isAttacking || !activeAttackIsHeavy || heavyDischargeCommitted || IsHeavyParryMotionActive
            || activeWeaponData == null || activeWeaponData.weaponClass != WeaponClass.Greatsword
            || playerAnimatorController == null) return false;
        if (!playerAnimatorController.PlayHeavyParry(out heavyParryDuration,
            out heavyParryBridgeDuration, out float contactDelay, out float heavyStartSeconds, parryOnly)) return false;

        float previousProgress = GetAttackNormalizedTime();
        if (!parryOnly && !ConfigureParriedHeavyAttack())
        {
            CancelActiveAttack(WeaponActionCompletionReason.InvalidConfiguration, true);
            return false;
        }
        if (parryOnly)
        {
            EndDashHeavyPresentation();
            attackPhaseExecutor.Cancel();
            attackMovementExecutor.Cancel();
        }
        heavyParrySavedProgress = parryOnly ? previousProgress : Mathf.Clamp(heavyStartSeconds / Mathf.Max(.01f, activeAttackAnimationClip.length), 0f, .95f);
        heavyParrySavedElapsed = attackDuration * activeAttackStep.playbackAcceleration.ToElapsed(heavyParrySavedProgress);
        // A fixed pose restart must neither undo late-parry travel nor apply skipped windup travel.
        heavyParryMovementFloor = Mathf.Max(previousProgress, heavyParrySavedProgress);
        HeavyParryContactDelay = contactDelay;
        heavyParryElapsed = 0f;
        heavyParryStartedFrame = Time.frameCount;
        heavyParryStage = HeavyParryStage.Parry;
        attackTrailExecutor.Cancel();
        ClearAttackTrail();
        attackPatternDebugRenderer?.Hide();
        HoldHeavyParryLocks();
        return true;
    }

    private bool ConfigureParriedHeavyAttack()
    {
        MeleeHeavyAttackDefinition counter = activeWeaponData.GetMeleeDefinition()?.parriedHeavyAttackDefinition;
        if (counter == null) return true;
        if (!counter.IsConfigured) return false;
        EndDashHeavyPresentation();
        activeHeavyDefinition = counter;
        activeAttackStep = counter.attack;
        activeAttackAnimationClip = activeAttackStep.animationClip;
        activeAttackAnimationSpeed = Mathf.Max(.01f, ResolveAttackPlaybackMultiplier()
            * Mathf.Max(.01f, activeAttackStep.animationSpeedMultiplier));
        activeAttackTransitionDuration = activeAttackStep.transitionDuration;
        attackDuration = ResolveAttackDuration();
        // A successful parry chooses the counter even when this action began as a dodge heavy.
        activeDodgeFollowUp = PlayerDodgeFollowUpKind.None;
        dodgeTrajectoryDefinition = null;
        bool hasEnergy = activeHeavyEnergy != null && activeHeavyEnergy.Amount > 0f
            && activeAttackWeaponItem != null && activeHeavyEnergy.WeaponInstanceId == activeAttackWeaponItem.runtimeInstanceId
            && activeHeavyEnergy.Element == activeGemAttack.Element;
        activeAttackDamageMultiplier = CombatBalanceFormulas.AttackDamageMultiplier(
            activeWeaponData, counter, true, hasEnergy);
        ResolveAttackTrail(activeAttackStep);
        ResolveAttackPhases();
        attackPhaseExecutor.Cancel();
        attackMovementExecutor.Cancel();
        attackVisualHeight.Begin(transform.Find("VisualRoot"), activeAttackStep.visualHeightCurve);
        return TryBeginAttackPhases();
    }

    private bool TickHeavyParryMotion()
    {
        if (!IsHeavyParryMotionActive) return false;
        // Freeze every heavy executor and its scaled clock while showing the parry and crossfade.
        attackStartTime = Time.time - heavyParrySavedElapsed;
        HoldHeavyParryLocks();
        if (IsHeavyParryClockPaused) return true;
        // Allow the queued parry state to be evaluated once before spending motion time.
        // A long first frame must not replace it with the bridge before the Animator sees it.
        if (Time.frameCount <= heavyParryStartedFrame + 1) return true;
        heavyParryElapsed += Time.unscaledDeltaTime;
        if (heavyParryStage == HeavyParryStage.Parry
            && heavyParryElapsed >= heavyParryDuration
            && playerAnimatorController.IsHeavyParryClipComplete)
        {
            if (heavyParryOnly)
            {
                FinishIncompleteParry();
                return true;
            }
            float remaining = attackDuration * activeAttackStep.playbackAcceleration.ToElapsed(1f) - heavyParrySavedElapsed;
            if (!playerAnimatorController.BlendHeavyAfterParry(activeAttackAnimationClip,
                remaining, heavyParrySavedProgress, activeAttackStep.playbackAcceleration))
            {
                CancelActiveAttack(WeaponActionCompletionReason.InvalidConfiguration, true);
                return true;
            }
            heavyParryStage = HeavyParryStage.Bridge;
            // Count the full actual crossfade even when a frame crossed the phase boundary.
            heavyParryElapsed = 0f;
            return true;
        }
        if (heavyParryStage != HeavyParryStage.Bridge || heavyParryElapsed < heavyParryBridgeDuration)
            return true;

        playerAnimatorController.CompleteHeavyParryBridge();
        heavyParryStage = HeavyParryStage.None;
        BeginHeavyFocusPresentation();
        attackStartTime = Time.time - heavyParrySavedElapsed;
        attackMovementExecutor.Begin(activeAttackStep.movementPhases, activeAttackDirection,
            ApplyAttackDisplacement, heavyParryMovementFloor);
        attackTrailExecutor.Begin(activeAttackStep.trailPhases, StartAttackTrail, StopAttackTrail);
        float remainingDuration = attackDuration * activeAttackStep.playbackAcceleration.ToElapsed(1f) - heavyParrySavedElapsed;
        MeleeAttackLock.Begin(playerController, remainingDuration, activeAttackDirection);
        playerEquipment.CurrentWeaponPose?.BeginActivePose(remainingDuration);
        if (heavyParrySwingPending)
        {
            heavyParrySwingPending = false;
            CombatActionSfxService.PlayGreatswordSwing(comboStepIndex, true, transform.position);
        }
        return true;
    }

    private void HoldHeavyParryLocks()
    {
        // Reissuing the owned lock also covers a late parry after some heavy advance.
        MeleeAttackLock.Begin(playerController, Mathf.Max(.1f, attackDuration), activeAttackDirection);
        playerEquipment?.CurrentWeaponPose?.BeginActivePose(Mathf.Max(.1f, attackDuration));
    }

    private void FinishIncompleteParry()
    {
        ResolveFacade()?.CombatInputs?.ClearAttack();
        ResolveFacade()?.CombatInputs?.ClearHeavy();
        Vector3 direction = activeAttackDirection;
        StopActiveAttackStep();
        ResetComboState();
        CompleteActiveAction(true, WeaponActionCompletionReason.Completed, direction);
    }

    private void ResetHeavyParryMotion()
    {
        if (IsHeavyParryMotionActive) playerAnimatorController?.CancelWeaponRuntimeState();
        heavyParryStage = HeavyParryStage.None;
        heavyParrySwingPending = false;
        heavyParryOnly = false;
        heavyParryElapsed = heavyParryDuration = heavyParryBridgeDuration = 0f;
        heavyParrySavedElapsed = heavyParrySavedProgress = HeavyParryContactDelay = 0f;
        heavyParryMovementFloor = 0f;
    }
}
