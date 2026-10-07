using UnityEngine;

public partial class MeleeWeaponCombatAnimatorDriver
{
    public const float IncompleteHeavyParryRecoverySpeed = .7f;

    private bool incompleteHeavyParry;
    private bool incompleteHeavyParryRecoveryStarted;
    private bool incompleteHeavyParryCounterStarted;
    private float heavyParryCounterNormalizedTime, heavyParryCounterSpeed;
    private float heavyParryContactNormalizedTime;
    private AnimationClip heavyParryActiveClip;
    private AnimatorOverrideController heavyParryClipOverrideController;
    private AnimationClip heavyParryTemplateClip;
    private AnimationClip heavyParryClipBeforeOverride;
    private readonly System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<AnimationClip, AnimationClip>> heavyParryOverrideBindings
        = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<AnimationClip, AnimationClip>>();
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

    public bool TryPlayHeavyParry(out float duration, out float bridgeDuration, out float contactDelay, out float heavyStartSeconds, bool parryOnly = false, float incompleteCounterSpeed = 1f)
    {
        duration = bridgeDuration = contactDelay = heavyStartSeconds = 0f;
        ApplyCurrentProfile();
        if (activeAction != DriverAction.Attack || activeProfile == null
            || activeProfile.heavyParryClip == null || !HasState(activeProfile.heavyParryStateName))
            return false;

        AnimationClip clip = parryOnly && activeProfile.incompleteHeavyParryClip != null
            ? activeProfile.incompleteHeavyParryClip : activeProfile.heavyParryClip;
        if (parryOnly && clip != activeProfile.heavyParryClip && !TryOverrideIncompleteHeavyParryClip(clip))
            return false;
        heavyParryActiveClip = clip;
        float playbackSpeed = parryOnly ? 1f : Mathf.Max(.05f, activeProfile.heavyParryPlaybackSpeed);
        float clipLength = Mathf.Max(.01f, clip.length);
        float contactSeconds = Mathf.Clamp(activeProfile.heavyParryContactSeconds, 0f, clipLength);
        float counterSeconds = Mathf.Clamp(activeProfile.incompleteParryCounterStartSeconds, contactSeconds, clipLength);
        heavyParryCounterSpeed = Mathf.Max(.01f, incompleteCounterSpeed);
        heavyParryCounterNormalizedTime = counterSeconds / clipLength;
        duration = parryOnly
            ? contactSeconds + (counterSeconds - contactSeconds) / IncompleteHeavyParryRecoverySpeed
                + (clipLength - counterSeconds) / heavyParryCounterSpeed
            : clipLength / playbackSpeed;
        incompleteHeavyParry = parryOnly;
        incompleteHeavyParryRecoveryStarted = false;
        incompleteHeavyParryCounterStarted = false;
        heavyParryContactNormalizedTime = contactSeconds / clipLength;
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
        if (incompleteHeavyParry && !incompleteHeavyParryCounterStarted && activeProfile != null
            && Time.frameCount > heavyParryEntryFrame + 1)
        {
            var state = targetAnimator.GetCurrentAnimatorStateInfo(layerIndex);
            if (targetAnimator.IsInTransition(layerIndex))
            {
                var next = targetAnimator.GetNextAnimatorStateInfo(layerIndex);
                // A repeated parry can still have the previous parry as its outgoing state.
                if (next.IsName(activeProfile.heavyParryStateName)) state = next;
            }
            if (state.IsName(activeProfile.heavyParryStateName)
                && state.normalizedTime >= heavyParryCounterNormalizedTime)
            {
                incompleteHeavyParryRecoveryStarted = incompleteHeavyParryCounterStarted = true;
                targetAnimator.SetFloat(activeProfile.heavyParrySpeedParameterName, heavyParryCounterSpeed);
            }
            else if (!incompleteHeavyParryRecoveryStarted && state.IsName(activeProfile.heavyParryStateName)
                && state.normalizedTime >= heavyParryContactNormalizedTime)
            {
                incompleteHeavyParryRecoveryStarted = true;
                targetAnimator.SetFloat(activeProfile.heavyParrySpeedParameterName, IncompleteHeavyParryRecoverySpeed);
            }
        }
        targetAnimator.speed = Time.frameCount <= heavyParryEntryFrame + 1
            || MeleeRuntime.IsHeavyParryClockPaused ? 0f : 1f;
    }

    private void RestoreHeavyParryClock()
    {
        heavyParryResumeFrame = -1;
        incompleteHeavyParry = false;
        incompleteHeavyParryRecoveryStarted = false;
        incompleteHeavyParryCounterStarted = false;
        heavyParryCounterNormalizedTime = heavyParryCounterSpeed = 0f;
        heavyParryContactNormalizedTime = 0f;
        heavyParryActiveClip = null;
        RestoreHeavyParryClipOverride();
        if (!heavyParryClockOwned) return;
        heavyParryClockOwned = false;
        if (targetAnimator == null) return;
        targetAnimator.updateMode = updateModeBeforeHeavyParry;
        targetAnimator.speed = animatorSpeedBeforeHeavyParry;
    }

    public bool TryGetHeavyParryClipProgress(out float progress)
    {
        progress = 0f;
        if (!heavyParryClockOwned || activeAction != DriverAction.Parry || targetAnimator == null || activeProfile == null
            || Time.frameCount <= heavyParryEntryFrame + 1) return false;
        var state = targetAnimator.GetCurrentAnimatorStateInfo(layerIndex);
        if (targetAnimator.IsInTransition(layerIndex))
        {
            var next = targetAnimator.GetNextAnimatorStateInfo(layerIndex);
            if (next.IsName(activeProfile.heavyParryStateName)) state = next;
        }
        if (!state.IsName(activeProfile.heavyParryStateName)) return false;
        progress = Mathf.Clamp01(state.normalizedTime);
        return true;
    }

    private bool TryOverrideIncompleteHeavyParryClip(AnimationClip clip)
    {
        if (runtimeOverrideController == null || activeProfile == null || activeProfile.heavyParryClip == null || clip == null)
            return false;
        heavyParryOverrideBindings.Clear();
        runtimeOverrideController.GetOverrides(heavyParryOverrideBindings);
        int index = heavyParryOverrideBindings.FindIndex(pair => pair.Key == activeProfile.heavyParryClip);
        if (index < 0) index = heavyParryOverrideBindings.FindIndex(pair => pair.Value == activeProfile.heavyParryClip);
        if (index < 0) { heavyParryOverrideBindings.Clear(); return false; }
        var binding = heavyParryOverrideBindings[index];
        heavyParryOverrideBindings.Clear();
        heavyParryClipOverrideController = runtimeOverrideController;
        heavyParryTemplateClip = binding.Key;
        heavyParryClipBeforeOverride = binding.Value;
        runtimeOverrideController[heavyParryTemplateClip] = clip;
        return true;
    }

    private void RestoreHeavyParryClipOverride()
    {
        if (heavyParryClipOverrideController != null && heavyParryTemplateClip != null)
            heavyParryClipOverrideController[heavyParryTemplateClip] = heavyParryClipBeforeOverride;
        heavyParryClipOverrideController = null;
        heavyParryTemplateClip = heavyParryClipBeforeOverride = null;
        heavyParryOverrideBindings.Clear();
    }

    private void OnDisable() => RestoreHeavyParryClock();
}
