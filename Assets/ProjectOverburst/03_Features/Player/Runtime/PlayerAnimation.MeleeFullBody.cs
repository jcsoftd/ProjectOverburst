using UnityEngine;
using System;
using System.Collections.Generic;

// PlayerAnimation partial: 근접 전신 동작 시작·갱신·복원과 전신 조준 포즈. 필드와 Unity 수명주기는 PlayerAnimation.cs에 있다.
public partial class PlayerAnimation
{
    private void BeginMeleeFullBodyAction(AnimationClip actionClip, float animationSpeed, float actionDuration)
    {
        BeginMeleeFullBodyAction(actionClip, animationSpeed, actionDuration, 0f);
    }

    private void BeginMeleeFullBodyAction(AnimationClip actionClip, float animationSpeed, float actionDuration, float fixedTransitionDuration)
    {
        BeginMeleeFullBodyAction(actionClip, animationSpeed, actionDuration, fixedTransitionDuration, animationSpeed < 0f ? 1f : 0f);
    }

    private void BeginMeleeFullBodyAction(AnimationClip actionClip, float animationSpeed, float actionDuration, float fixedTransitionDuration, float normalizedStartTime)
    {
        BeginMeleeFullBodyAction(actionClip, animationSpeed, actionDuration, fixedTransitionDuration, normalizedStartTime, false);
    }

    private void BeginMeleeFullBodyAction(AnimationClip actionClip, float animationSpeed, float actionDuration, float fixedTransitionDuration, float normalizedStartTime, bool deferRestoreForCombatLocomotion)
    {
        if (targetAnimator == null || actionClip == null)
            return;

        RestoreFullBodyAimPose();
        RestoreMeleeFullBodyOverrides(); // 이전 복구
        EnsureWeaponOverrideController(); // override 준비

        if (weaponOverrideController == null)
            return;

        if (!ApplyMeleeFullBodyClipOverride(actionClip))
            return;

        bool playFullBodyActionInReverse = animationSpeed < -0.001f;
        animatorSpeedBeforeMeleeFullBody = targetAnimator.speed; // 속도 백업
        targetAnimator.speed = Mathf.Max(0.01f, animationSpeed); // 전신 속도
        isMeleeFullBodyActionActive = true; // 전신 활성
        deferMeleeFullBodyRestoreToCombatLocomotion = deferRestoreForCombatLocomotion;
        isMeleeFullBodyRestorePending = false;
        meleeFullBodyRestoreTime = 0f;
        meleeFullBodyActionEndTime = Time.time + Mathf.Max(0.01f, actionDuration); // 종료 시간
        SetWeaponActionLayerWeight(0f);
        SetFullBodyAimLayerWeight(0f);
        if (playFullBodyActionInReverse)
            targetAnimator.speed = animationSpeed;

        ResetMeleeAnimatorParametersOnStart();
        PlayBaseLayerState(meleeFullBodyStateName, fixedTransitionDuration);
        if (normalizedStartTime > 0.001f && TryResolveBaseLayerStateHash(meleeFullBodyStateName, out int meleeFullBodyStateHash))
            targetAnimator.Play(meleeFullBodyStateHash, 0, Mathf.Clamp01(normalizedStartTime));
    }

    private bool UpdateMeleeFullBodyAction()
    {
        if (!isMeleeFullBodyActionActive)
            return false;

        if (Time.time >= meleeFullBodyActionEndTime)
        {
            if (ShouldDeferMeleeFullBodyRestoreToCombatLocomotion())
            {
                BeginDeferredMeleeFullBodyRestore();
                return false;
            }

            RestoreMeleeFullBodyOverrides(); // 전신 복구
            return false;
        }

        SetMeleeFullBodyAnimatorParameters();
        return true;
    }

    private void SetMeleeFullBodyAnimatorParameters()
    {
        targetAnimator.SetFloat(speedParameter, 0f);
        targetAnimator.SetFloat(moveXParameter, 0f);
        targetAnimator.SetFloat(moveYParameter, 0f);
        targetAnimator.SetBool(groundedParameter, playerController.IsGrounded);
        targetAnimator.SetFloat(verticalVelocityParameter, playerController.VerticalVelocity);
        targetAnimator.SetBool(aimingParameter, false);
        SetWeaponActionLayerWeight(0f);
        SetFullBodyAimLayerWeight(0f);
    }

    private void ResetMeleeAnimatorParametersOnStart()
    {
        if (targetAnimator == null)
            return;

        currentMoveBlend = Vector2.zero; // 근접 1타 시작 안정화
        moveBlendVelocity = Vector2.zero;
        targetAnimator.SetFloat(speedParameter, 0f);
        targetAnimator.SetFloat(moveXParameter, 0f);
        targetAnimator.SetFloat(moveYParameter, 0f);
        targetAnimator.SetBool(aimingParameter, false);
        targetAnimator.Update(0f);
    }

    private bool ApplyMeleeFullBodyClipOverride(AnimationClip actionClip)
    {
        meleeFullBodyRestoreOverrides.Clear(); // 복구 목록 초기화
        weaponOverrideController.GetOverrides(meleeFullBodyRestoreOverrides); // 원본 저장
        weaponOverrides.Clear(); // 적용 목록 초기화

        bool replaced = false;
        for (int i = 0; i < meleeFullBodyRestoreOverrides.Count; i++)
        {
            AnimationClip sourceClip = meleeFullBodyRestoreOverrides[i].Key; // 원본 clip
            AnimationClip replacementClip = meleeFullBodyRestoreOverrides[i].Value; // 기존 override 유지
            if (IsMeleeFullBodyBaseClip(sourceClip))
            {
                replacementClip = actionClip; // 전신 clip
                replaced = true;
            }

            weaponOverrides.Add(new KeyValuePair<AnimationClip, AnimationClip>(sourceClip, replacementClip));
        }

        if (!replaced)
        {
            meleeFullBodyRestoreOverrides.Clear();
            Debug.LogWarning($"PlayerAnimation could not find melee full-body base clip '{meleeFullBodyBaseClipName}'.", this);
            return false;
        }

        weaponOverrideController.ApplyOverrides(weaponOverrides);
        return true;
    }

    private bool ShouldDeferMeleeFullBodyRestoreToCombatLocomotion()
    {
        return deferMeleeFullBodyRestoreToCombatLocomotion
            && playerController != null
            && playerController.IsMeleeCombatLocomotionMode
            && meleeFullBodyRestoreOverrides.Count > 0;
    }

    private void BeginDeferredMeleeFullBodyRestore()
    {
        if (targetAnimator != null)
            targetAnimator.speed = animatorSpeedBeforeMeleeFullBody;

        isMeleeFullBodyActionActive = false;
        deferMeleeFullBodyRestoreToCombatLocomotion = false;
        isMeleeFullBodyRestorePending = true;
        meleeFullBodyRestoreTime = Time.time + Mathf.Max(0.05f, combatEquipToLocomotionTransitionDuration + 0.05f);
    }

    private void SchedulePendingMeleeFullBodyRestore(float transitionDuration)
    {
        if (!isMeleeFullBodyRestorePending)
            return;

        meleeFullBodyRestoreTime = Time.time + Mathf.Max(0f, transitionDuration) + 0.02f;
    }

    private void ProcessPendingMeleeFullBodyRestore()
    {
        if (!isMeleeFullBodyRestorePending)
            return;

        if (Time.time < meleeFullBodyRestoreTime)
            return;

        if (!CanCompletePendingMeleeFullBodyRestore())
            return;

        RestoreMeleeFullBodyOverrides();
    }

    private bool CanCompletePendingMeleeFullBodyRestore()
    {
        if (targetAnimator != null && targetAnimator.IsInTransition(0))
            return false;

        if (playerController == null || !playerController.IsMeleeCombatLocomotionMode)
            return true;

        return IsCurrentBaseState(combatLocomotionStateName)
            || IsCurrentBaseState(combatGuardLocomotionStateName);
    }

    private void RestoreMeleeFullBodyOverrides()
    {
        if (!isMeleeFullBodyActionActive && meleeFullBodyRestoreOverrides.Count == 0)
            return;

        if (weaponOverrideController != null && meleeFullBodyRestoreOverrides.Count > 0)
            weaponOverrideController.ApplyOverrides(meleeFullBodyRestoreOverrides); // clip 복구

        if (targetAnimator != null)
            targetAnimator.speed = animatorSpeedBeforeMeleeFullBody; // 속도 복구

        isMeleeFullBodyActionActive = false; // 전신 종료
        deferMeleeFullBodyRestoreToCombatLocomotion = false;
        isMeleeFullBodyRestorePending = false;
        meleeFullBodyRestoreTime = 0f;
        meleeFullBodyRestoreOverrides.Clear(); // 복구 목록 해제
    }

    private void RefreshFullBodyAimPose()
    {
        AnimationClip aimClip = GetFullBodyAimPoseClip();
        if (aimClip == null)
        {
            RestoreFullBodyAimPose();
            return;
        }

        if (isFullBodyAimPoseActive && activeFullBodyAimPoseClip == aimClip)
            return;

        ApplyFullBodyAimPose(aimClip);
    }

    private AnimationClip GetFullBodyAimPoseClip()
    {
        if (isMeleeFullBodyActionActive || targetAnimator == null || playerController == null || playerEquipment == null)
            return null;

        if (!ShouldUseCurrentAimPoseFullBody())
            return null;

        return playerEquipment.CurrentWeaponData.GetAimPoseClip();
    }

    private bool ShouldUseCurrentAimPoseFullBody()
    {
        if (playerController == null || playerEquipment == null || !playerController.IsWeaponAimPoseActive)
            return false;

        WeaponItemData weaponData = playerEquipment.CurrentWeaponData;
        return weaponData != null
            && weaponData.GetResolvedAimPoseBodyMode() == WeaponAimPoseBodyMode.FullBody
            && weaponData.GetAimPoseClip() != null;
    }

    private void ApplyFullBodyAimPose(AnimationClip aimClip)
    {
        if (aimClip == null)
            return;

        RestoreMeleeFullBodyOverrides();
        EnsureWeaponOverrideController();

        if (weaponOverrideController == null)
            return;

        ApplyClipOverride(IsBaseAimPoseClip, aimClip, ref activeAimPoseClip);
        activeFullBodyAimPoseClip = aimClip;
        isFullBodyAimPoseActive = true;
        PlayFullBodyAimStateAt(0f);
        SetFullBodyAimLayerWeight(1f);
    }

    private void RestoreFullBodyAimPose()
    {
        if (!isFullBodyAimPoseActive)
            return;

        isFullBodyAimPoseActive = false;
        activeFullBodyAimPoseClip = null;
        fullBodyAimPoseRestoreOverrides.Clear();
        SetFullBodyAimLayerWeight(0f);
        targetAnimator?.Update(0f);
    }

    private void PlayFullBodyAimStateAt(float normalizedTime)
    {
        if (targetAnimator == null)
            return;

        EnsureActionStateHashes();
        if (fullBodyAimLayerIndex < 0 || fullBodyAimStateHash == 0)
            return;

        int stateHash = fullBodyAimStateHash;
        if (!targetAnimator.HasState(fullBodyAimLayerIndex, stateHash))
        {
            stateHash = Animator.StringToHash(FullBodyAimStateName);
            if (!targetAnimator.HasState(fullBodyAimLayerIndex, stateHash))
                return;
        }

        targetAnimator.CrossFadeInFixedTime(stateHash, 0.1f, fullBodyAimLayerIndex, Mathf.Clamp01(normalizedTime));
        targetAnimator.Update(0f);
    }
}
