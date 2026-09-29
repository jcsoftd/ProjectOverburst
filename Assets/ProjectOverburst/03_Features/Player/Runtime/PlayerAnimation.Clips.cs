using UnityEngine;
using System;
using System.Collections.Generic;

// PlayerAnimation partial: 애니메이터 파라미터 도우미와 클립 덮어쓰기·기본 클립 판별. 필드와 Unity 수명주기는 PlayerAnimation.cs에 있다.
public partial class PlayerAnimation
{
    private void ApplyClipOverride(Func<AnimationClip, bool> clipMatcher, AnimationClip desiredClip, ref AnimationClip activeClip)
    {
        if (weaponOverrideController == null || clipMatcher == null || desiredClip == null)
            return;

        if (desiredClip == activeClip)
            return;

        weaponOverrideController.GetOverrides(weaponOverrides); // 현재 목록

        bool replaced = false; // 교체 여부
        for (int i = 0; i < weaponOverrides.Count; i++)
        {
            AnimationClip sourceClip = weaponOverrides[i].Key; // 원본 clip
            if (!clipMatcher(sourceClip))
                continue;

            weaponOverrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(sourceClip, desiredClip);
            replaced = true; // 교체 완료
        }

        if (!replaced)
            return;

        weaponOverrideController.ApplyOverrides(weaponOverrides);
        activeClip = desiredClip; // 활성 clip
        targetAnimator.Update(0f);
    }

    private void SetTriggerIfPresent(string triggerParameter)
    {
        if (!HasParameter(triggerParameter, AnimatorControllerParameterType.Trigger))
            return;

        targetAnimator.ResetTrigger(triggerParameter);
        targetAnimator.SetTrigger(triggerParameter);
    }

    private void ResetTriggerIfPresent(string triggerParameter)
    {
        if (!HasParameter(triggerParameter, AnimatorControllerParameterType.Trigger))
            return;

        targetAnimator.ResetTrigger(triggerParameter);
    }

    private void SetFloatIfPresent(string floatParameter, float value)
    {
        if (!HasParameter(floatParameter, AnimatorControllerParameterType.Float))
            return;

        targetAnimator.SetFloat(floatParameter, value);
    }

    private void SetBoolIfPresent(string boolParameter, bool value)
    {
        if (!HasParameter(boolParameter, AnimatorControllerParameterType.Bool))
            return;

        targetAnimator.SetBool(boolParameter, value);
    }

    private bool HasParameter(string parameterName, AnimatorControllerParameterType parameterType)
    {
        if (targetAnimator == null || string.IsNullOrEmpty(parameterName))
            return false;

        AnimatorControllerParameter[] parameters = targetAnimator.parameters; // 파라미터 목록
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.type == parameterType && parameter.name == parameterName)
                return true;
        }

        return false;
    }

    private void EnsureWeaponOverrideController()
    {
        if (weaponOverrideController != null)
        {
            if (targetAnimator.runtimeAnimatorController != weaponOverrideController)
                targetAnimator.runtimeAnimatorController = weaponOverrideController;

            return;
        }

        RuntimeAnimatorController currentController = targetAnimator.runtimeAnimatorController; // 현재 controller
        if (currentController == null)
            return;

        AnimatorOverrideController currentOverride = currentController as AnimatorOverrideController; // 기존 override
        baseAnimatorController = currentOverride != null ? currentOverride.runtimeAnimatorController : currentController; // 원본 controller

        if (baseAnimatorController == null)
            return;

        weaponOverrideController = new AnimatorOverrideController(baseAnimatorController); // 무기 override
        weaponOverrideController.name = baseAnimatorController.name + "_WeaponClipOverride"; // 식별 이름
        targetAnimator.runtimeAnimatorController = weaponOverrideController; // 적용
    }

    private AnimationClip GetDesiredAimPoseClip()
    {
        if (forcedAimPoseClip != null)
            return forcedAimPoseClip;

        if (Time.time < quickFireAimPoseUntil && quickFireAimPoseClip != null)
            return quickFireAimPoseClip;

        if (playerController != null && !playerController.IsWeaponAimPoseActive)
            return GetBaseAimPoseClip();

        WeaponItemData weaponData = playerEquipment != null ? playerEquipment.CurrentWeaponData : null; // 현재 무기
        AnimationClip weaponClip = weaponData != null ? weaponData.GetAimPoseClip() : null; // 무기 포즈

        if (weaponClip != null)
            return weaponClip;

        return GetBaseAimPoseClip();
    }

    private AnimationClip GetBaseAimPoseClip()
    {
        if (baseAimClip != null)
            return baseAimClip;

        return FindControllerClip(baseAimClipName);
    }

    private AnimationClip GetBaseFireClip()
    {
        if (baseFireClip != null)
            return baseFireClip;

        return FindControllerClip(baseFireClipName);
    }

    private AnimationClip GetBaseRecoverClip()
    {
        if (baseRecoverClip != null)
            return baseRecoverClip;

        return FindControllerClip(baseRecoverClipName);
    }

    private AnimationClip FindControllerClip(string clipName)
    {
        if (string.IsNullOrEmpty(clipName))
            return null;

        RuntimeAnimatorController controller = baseAnimatorController != null ? baseAnimatorController : targetAnimator.runtimeAnimatorController; // 검색 대상

        if (controller == null)
            return null;

        AnimationClip[] clips = controller.animationClips;
        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip clip = clips[i];
            if (clip != null && clip.name == clipName)
                return clip;
        }

        return null;
    }

    private bool IsBaseAimPoseClip(AnimationClip clip)
    {
        if (clip == null)
            return false;

        if (baseAimClip != null && clip == baseAimClip)
            return true;

        return !string.IsNullOrEmpty(baseAimClipName) && clip.name == baseAimClipName;
    }

    private bool IsMeleeFullBodyBaseClip(AnimationClip clip)
    {
        if (clip == null)
            return false;

        if (meleeFullBodyBaseClip != null && clip == meleeFullBodyBaseClip)
            return true;

        if (!string.IsNullOrEmpty(meleeFullBodyBaseClipName)
            && string.Equals(clip.name, meleeFullBodyBaseClipName, StringComparison.Ordinal))
            return true;

        return !string.IsNullOrEmpty(meleeFullBodyStateName)
            && string.Equals(clip.name, meleeFullBodyStateName, StringComparison.Ordinal);
    }

    private bool IsBaseFireClip(AnimationClip clip)
    {
        if (clip == null)
            return false;

        if (baseFireClip != null && clip == baseFireClip)
            return true;

        return !string.IsNullOrEmpty(baseFireClipName) && clip.name == baseFireClipName;
    }

    private bool IsBaseRecoverClip(AnimationClip clip)
    {
        if (clip == null)
            return false;

        if (baseRecoverClip != null && clip == baseRecoverClip)
            return true;

        return !string.IsNullOrEmpty(baseRecoverClipName) && clip.name == baseRecoverClipName;
    }
}
