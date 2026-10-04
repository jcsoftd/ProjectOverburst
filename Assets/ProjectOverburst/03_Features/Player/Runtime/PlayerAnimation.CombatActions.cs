using UnityEngine;
using System;
using System.Collections.Generic;

// PlayerAnimation partial: 외부에서 부르는 전투 동작 진입점과 피격 클립 선택. 필드와 Unity 수명주기는 PlayerAnimation.cs에 있다.
public partial class PlayerAnimation
{
    public void PlayMeleeFullBodyFire(AnimationClip fireClip, float animationSpeed, float actionDuration)
    {
        PlayMeleeFullBodyFire(fireClip, animationSpeed, actionDuration, 0f);
    }

    public void PlayMeleeFullBodyFire(AnimationClip fireClip, float animationSpeed, float actionDuration, float fixedTransitionDuration)
    {
        AnimationClip desiredClip = fireClip != null ? fireClip : GetBaseFireClip(); // 근접 clip
        if (useLegacyWeaponAimLayers)
        {
            PlayWeaponAction(
                desiredClip,
                IsBaseFireClip,
                ref activeFireClip,
                fireTriggerParameter,
                fireAnimationSpeedParameter,
                1f,
                fireStateName);
        }

        if (IsWeaponCombatAnimatorRouterActive())
            weaponCombatAnimatorRouter.SuppressCombatLayerForLegacyAction(Mathf.Max(0.01f, actionDuration + fixedTransitionDuration));

        BeginMeleeFullBodyAction(desiredClip, animationSpeed, actionDuration, fixedTransitionDuration);
    }

    public bool PlayMeleeCombatAttack(
        int comboStepIndex,
        AnimationClip attackClip,
        float animationSpeed,
        float actionDuration,
        float transitionDuration,
        bool allowCombatEntry,
        float normalizedStartTime = 0f, MeleePlaybackAcceleration playbackAcceleration = default)
    {
        if (IsWeaponCombatAnimatorRouterActive())
        {
            return weaponCombatAnimatorRouter.TryPlayCombatAttack(
                comboStepIndex,
                attackClip,
                actionDuration,
                transitionDuration,
                allowCombatEntry,
                normalizedStartTime, playbackAcceleration);
        }

        if (normalizedStartTime > 0f || playbackAcceleration.IsEnabled) return false;
        PlayMeleeFullBodyFire(attackClip, animationSpeed, actionDuration, transitionDuration);
        return attackClip != null;
    }

    public bool IsHeavyParryClipComplete => weaponCombatAnimatorRouter != null && weaponCombatAnimatorRouter.IsHeavyParryClipComplete;

    public bool PlayHeavyParry(out float duration, out float bridgeDuration, out float contactDelay, out float heavyStartSeconds, bool parryOnly = false)
    {
        duration = bridgeDuration = contactDelay = heavyStartSeconds = 0f;
        return IsWeaponCombatAnimatorRouterActive()
            && weaponCombatAnimatorRouter.TryPlayHeavyParry(out duration, out bridgeDuration, out contactDelay, out heavyStartSeconds, parryOnly);
    }

    public bool BlendHeavyAfterParry(AnimationClip clip, float duration, float normalizedStart,
        MeleePlaybackAcceleration acceleration)
        => IsWeaponCombatAnimatorRouterActive()
            && weaponCombatAnimatorRouter.TryBlendHeavyAfterParry(clip, duration, normalizedStart, acceleration);

    public void CompleteHeavyParryBridge() => weaponCombatAnimatorRouter?.CompleteHeavyParryBridge();

    public void PlayEvadeFullBody(AnimationClip evadeClip, float actionDuration, float fixedTransitionDuration)
    {
        float duration = Mathf.Max(0.01f, actionDuration);
        if (playerController != null
            && playerController.IsMeleeCombatLocomotionMode
            && IsWeaponCombatAnimatorRouterActive()
            && weaponCombatAnimatorRouter.TryPlayCombatRoll(duration))
        {
            return;
        }

        if (evadeClip == null)
            return;

        float animationSpeed = Mathf.Max(0.01f, evadeClip.length / duration);
        BeginMeleeFullBodyAction(evadeClip, animationSpeed, duration, fixedTransitionDuration);
    }

    public void NotifyMeleeGuardBlockedHit()
    {
        if (IsWeaponCombatAnimatorRouterActive() && weaponCombatAnimatorRouter.TryPlayMeleeGuardBlock())
            return;

        AnimationClip clip = ShouldUseMovingGuardBlockClip() ? combatMovingBlockClip : combatBlockClip;
        if (clip == null)
            clip = combatBlockClip != null ? combatBlockClip : combatMovingBlockClip;

        if (clip == null)
            return;

        BeginMeleeFullBodyAction(clip, 1f, clip.length, combatBlockTransitionDuration);
    }

    public void NotifyCombatDamagedHit()
    {
        var melee = GetComponent<MeleeRuntime>();
        if (melee != null && melee.IsHeavyParryMotionActive)
            melee.CancelCurrentAction(WeaponActionCancelReason.Recovery);
        if (IsWeaponCombatAnimatorRouterActive() && weaponCombatAnimatorRouter.TryPlayCombatHit())
            return;

        AnimationClip clip = PickCombatHitClip();
        if (clip == null)
            return;

        BeginMeleeFullBodyAction(clip, 1f, clip.length, combatHitTransitionDuration);
    }

    private AnimationClip PickCombatHitClip()
    {
        if (combatHitClips == null || combatHitClips.Length == 0)
            return null;

        int availableCount = 0;
        for (int i = 0; i < combatHitClips.Length; i++)
        {
            if (combatHitClips[i] != null)
                availableCount++;
        }

        if (availableCount <= 0)
            return null;

        int selectedIndex = UnityEngine.Random.Range(0, availableCount);
        for (int i = 0; i < combatHitClips.Length; i++)
        {
            AnimationClip clip = combatHitClips[i];
            if (clip == null)
                continue;

            if (selectedIndex == 0)
                return clip;

            selectedIndex--;
        }

        return null;
    }

    private bool ShouldUseMovingGuardBlockClip()
    {
        return combatMovingBlockClip != null
            && playerController != null
            && playerController.MoveInput.sqrMagnitude > 0.001f;
    }

    private void PlayCombatJump()
    {
        if (combatJumpClip == null)
            return;

        BeginMeleeFullBodyAction(combatJumpClip, 1f, combatJumpClip.length, combatJumpTransitionDuration);
    }

    private void PlayCombatEquip(bool reverse)
    {
        if (combatEquipClip == null)
            return;

        AnimationClip clip = reverse && combatEquipExitClip != null ? combatEquipExitClip : combatEquipClip;
        float transitionDuration = reverse ? combatEquipExitTransitionDuration : combatEquipTransitionDuration;
        float actionDuration = reverse ? clip.length : Mathf.Max(0.01f, clip.length - Mathf.Max(0f, combatEquipEndTrimDuration));
        useCombatEquipToLocomotionBlendOnNextEnter = !reverse;
        useCombatLocomotionEntryOffsetOnNextEnter = !reverse && combatLocomotionEntryFixedTimeOffset > 0f;
        if (reverse)
        {
            float weaponHoldTime = Mathf.Max(0f, clip.length - Mathf.Max(0f, combatEquipExitWeaponBackLeadTime));
            playerEquipment?.CurrentWeaponPose?.BeginActivePose(weaponHoldTime);
        }

        BeginMeleeFullBodyAction(clip, 1f, actionDuration, transitionDuration, 0f, !reverse);
    }

    public void CancelWeaponRuntimeState()
    {
        weaponCombatAnimatorRouter?.CancelCombatAttack();
        forcedAimPoseClip = null; // 강제 해제
        useForcedAimPoseTime = false; // 시간 해제
        quickFireAimPoseClip = null; // QuickFire 해제
        quickFireAimPoseUntil = 0f; // 시간 해제
        weaponActionLayerHoldUntil = 0f;
        RestoreFullBodyAimPose();
        RestoreMeleeFullBodyOverrides();
        SetWeaponActionLayerWeight(0f);
        SetFullBodyAimLayerWeight(0f);
        ResetTriggerIfPresent(fireTriggerParameter);
        ResetTriggerIfPresent(recoverTriggerParameter);
    }

    private void PlayWeaponAction(
        AnimationClip clip,
        Func<AnimationClip, bool> baseClipMatcher,
        ref AnimationClip activeClip,
        string triggerParameter,
        string speedParameter,
        float animationSpeed,
        string stateName)
    {
        if (targetAnimator == null || string.IsNullOrEmpty(triggerParameter))
            return;

        if (useWeaponActionClipOverride)
        {
            EnsureWeaponOverrideController(); // override 준비

            if (clip != null)
                ApplyClipOverride(baseClipMatcher, clip, ref activeClip); // clip 교체
        }

        SetFloatIfPresent(speedParameter, Mathf.Max(0.01f, animationSpeed)); // 속도 파라미터
        HoldWeaponActionLayer(clip, animationSpeed);
        if (!RestartWeaponActionState(stateName))
            SetTriggerIfPresent(triggerParameter);
    }
}
