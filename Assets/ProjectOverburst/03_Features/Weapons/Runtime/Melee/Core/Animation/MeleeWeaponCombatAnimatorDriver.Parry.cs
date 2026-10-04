using UnityEngine;

public partial class MeleeWeaponCombatAnimatorDriver
{
    private bool heavyParryClockOwned;
    private AnimatorUpdateMode updateModeBeforeHeavyParry;
    private float animatorSpeedBeforeHeavyParry;
    private float heavyParryBlendSeconds;
    private int heavyParryResumeFrame = -1;
    private int heavyParryEntryFrame;

    public bool IsHeavyParryClipComplete => heavyParryClockOwned && activeAction == DriverAction.Parry
        && targetAnimator != null && activeProfile != null
        && targetAnimator.GetCurrentAnimatorStateInfo(layerIndex).IsName(activeProfile.heavyParryStateName)
        && targetAnimator.GetCurrentAnimatorStateInfo(layerIndex).normalizedTime >= 1f;

    public bool TryPlayHeavyParry(out float duration, out float bridgeDuration, out float contactDelay, out float heavyStartSeconds, bool parryOnly = false)
    {
        duration = bridgeDuration = contactDelay = heavyStartSeconds = 0f;
        ApplyCurrentProfile();
        if (activeAction != DriverAction.Attack || activeProfile == null
            || activeProfile.heavyParryClip == null || !HasState(activeProfile.heavyParryStateName))
            return false;

        float playbackSpeed = parryOnly ? 1f : Mathf.Max(.05f, activeProfile.heavyParryPlaybackSpeed);
        duration = Mathf.Max(.01f, activeProfile.heavyParryClip.length) / playbackSpeed;
        bridgeDuration = Mathf.Max(0f, activeProfile.heavyParryToAttackBlend);
        heavyParryBlendSeconds = bridgeDuration;
        contactDelay = Mathf.Clamp(activeProfile.heavyParryContactSeconds / playbackSpeed, 0f, duration);
        heavyStartSeconds = Mathf.Max(0f, activeProfile.heavyParryHeavyStartSeconds);
        updateModeBeforeHeavyParry = targetAnimator.updateMode;
        animatorSpeedBeforeHeavyParry = targetAnimator.speed;
        heavyParryClockOwned = true;
        heavyParryEntryFrame = Time.frameCount;
        targetAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
        targetAnimator.SetFloat(activeProfile.heavyParrySpeedParameterName, playbackSpeed);
        targetAnimator.SetFloat(actionSpeedParameterName, 0f);
        PlayActionState(activeProfile.heavyParryStateName, DriverAction.Parry,
            duration + bridgeDuration, activeProfile.heavyParryEntryBlend);
        targetLayerWeight = 1f;
        targetTransitionLowerLayerWeight = 0f;
        targetAnimator.SetLayerWeight(layerIndex, 1f);
        if (transitionLowerLayerIndex >= 0) targetAnimator.SetLayerWeight(transitionLowerLayerIndex, 0f);
        // Even the first successful-parry frame respects an existing hit stop.
        UpdateHeavyParryClock();
        return true;
    }

    public bool TryBlendHeavyAfterParry(AnimationClip clip, float remainingDuration,
        float normalizedStart, MeleePlaybackAcceleration acceleration)
    {
        if (!heavyParryClockOwned || activeAction != DriverAction.Parry) return false;
        ApplyCurrentProfile();
        if (!CanPlayAttack(true) || clip == null || !HasState(attackStateName) || !ApplyAttackClip(clip))
            return false;
        normalizedStart = Mathf.Clamp(normalizedStart, 0f, .95f);
        attackAcceleration = acceleration;
        acceleratedAttackClip = clip;
        attackBaseDuration = Mathf.Max(.01f, remainingDuration)
            / (acceleration.ToElapsed(1f) - acceleration.ToElapsed(normalizedStart));
        heavyParryResumeElapsed = attackBaseDuration * acceleration.ToElapsed(normalizedStart);
        attackClockOrigin = Time.time - heavyParryResumeElapsed;
        attackPreviousSampleTime = Time.time;
        // Use a normalized pose offset so the frozen incoming state retains its saved pose.
        targetAnimator.SetFloat(actionSpeedParameterName, 0f);
        // The non-looping outgoing clip has reached its final pose. Use its native
        // 1x clock for a real-seconds transition while that final pose stays clamped.
        targetAnimator.SetFloat(activeProfile.heavyParrySpeedParameterName, 1f);
        int stateHash = Animator.StringToHash(layerName + "." + attackStateName);
        if (!targetAnimator.HasState(layerIndex, stateHash)) stateHash = Animator.StringToHash(attackStateName);
        targetAnimator.CrossFade(stateHash,
            heavyParryBlendSeconds / activeProfile.heavyParryClip.length,
            layerIndex, normalizedStart, 0f);
        activeAction = DriverAction.Attack;
        activeActionEndTime = Time.time + Mathf.Max(.01f, remainingDuration);
        return true;
    }

    public void CompleteHeavyParryBridge()
    {
        if (!heavyParryClockOwned) return;
        RestoreHeavyParryClock();
        if (activeAction != DriverAction.Attack || acceleratedAttackClip == null) return;
        // The runtime supplies the saved elapsed offset; no time is spent on damage during blending.
        attackClockOrigin = Time.time - heavyParryResumeElapsed;
        attackPreviousSampleTime = Time.time;
        activeActionEndTime = Time.time + Mathf.Max(.01f,
            attackBaseDuration * attackAcceleration.ToElapsed(1f) - heavyParryResumeElapsed);
        // Time.time already includes this frame's delta. Do not advance the native
        // clip again in that frame while the runtime still has the saved progress.
        heavyParryResumeFrame = Time.frameCount;
        targetAnimator.SetFloat(actionSpeedParameterName, 0f);
    }

    private float heavyParryResumeElapsed;

    private void UpdateHeavyParryClock()
    {
        if (heavyParryResumeFrame >= 0 && Time.frameCount > heavyParryResumeFrame)
        {
            heavyParryResumeFrame = -1;
            if (activeAction == DriverAction.Attack && acceleratedAttackClip != null)
                SetActionSpeedForClip(acceleratedAttackClip, attackBaseDuration);
        }
        if (!heavyParryClockOwned || targetAnimator == null) return;
        targetAnimator.speed = Time.frameCount <= heavyParryEntryFrame + 1
            || MeleeRuntime.IsHeavyParryClockPaused ? 0f : 1f;
    }

    private void RestoreHeavyParryClock()
    {
        heavyParryResumeFrame = -1;
        if (!heavyParryClockOwned) return;
        heavyParryClockOwned = false;
        if (targetAnimator == null) return;
        targetAnimator.updateMode = updateModeBeforeHeavyParry;
        targetAnimator.speed = animatorSpeedBeforeHeavyParry;
    }

    private void OnDisable() => RestoreHeavyParryClock();
}
