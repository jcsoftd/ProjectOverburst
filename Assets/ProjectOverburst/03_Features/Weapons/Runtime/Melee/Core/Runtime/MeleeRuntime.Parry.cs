using UnityEngine;

public partial class MeleeRuntime
{
    private enum HeavyParryStage { None, Parry, Bridge }
    private HeavyParryStage heavyParryStage;
    private float heavyParryElapsed, heavyParryDuration, heavyParryBridgeDuration;
    private float heavyParrySavedElapsed, heavyParrySavedProgress;
    private bool heavyParrySwingPending;
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

    private void TryBeginHeavyParryMotion()
    {
        if (!isAttacking || !activeAttackIsHeavy || heavyDischargeCommitted || IsHeavyParryMotionActive
            || activeWeaponData == null || activeWeaponData.weaponClass != WeaponClass.Greatsword
            || playerAnimatorController == null) return;
        if (!playerAnimatorController.PlayHeavyParry(out heavyParryDuration,
            out heavyParryBridgeDuration, out float contactDelay)) return;

        heavyParrySavedElapsed = Mathf.Max(0f, Time.time - attackStartTime);
        heavyParrySavedProgress = GetAttackNormalizedTime();
        HeavyParryContactDelay = contactDelay;
        heavyParryElapsed = 0f;
        heavyParryStartedFrame = Time.frameCount;
        heavyParryStage = HeavyParryStage.Parry;
        attackTrailExecutor.Cancel();
        ClearAttackTrail();
        attackPatternDebugRenderer?.Hide();
        HoldHeavyParryLocks();
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
            && heavyParryElapsed >= heavyParryDuration - heavyParryBridgeDuration)
        {
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
        attackStartTime = Time.time - heavyParrySavedElapsed;
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

    private void ResetHeavyParryMotion()
    {
        if (IsHeavyParryMotionActive) playerAnimatorController?.CancelWeaponRuntimeState();
        heavyParryStage = HeavyParryStage.None;
        heavyParrySwingPending = false;
        heavyParryElapsed = heavyParryDuration = heavyParryBridgeDuration = 0f;
        heavyParrySavedElapsed = heavyParrySavedProgress = HeavyParryContactDelay = 0f;
    }
}
