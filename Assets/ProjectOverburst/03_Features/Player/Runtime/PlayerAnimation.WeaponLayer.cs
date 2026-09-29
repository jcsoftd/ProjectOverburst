using UnityEngine;
using System;
using System.Collections.Generic;

// PlayerAnimation partial: 무기 조준 포즈·상체 액션 레이어 가중치·레이어 찾기. 필드와 Unity 수명주기는 PlayerAnimation.cs에 있다.
public partial class PlayerAnimation
{
    public void BeginQuickFireAimPose(AnimationClip aimPoseClip, float holdTime)
    {
        if (!useLegacyWeaponAimLayers)
            return;

        quickFireAimPoseClip = aimPoseClip; // QuickFire 포즈
        quickFireAimPoseUntil = Mathf.Max(quickFireAimPoseUntil, Time.time + Mathf.Max(0f, holdTime)); // 유지 시간
        SetWeaponActionLayerWeight(1f);
    }

    public void SetForcedWeaponAimPose(AnimationClip aimPoseClip)
    {
        if (!useLegacyWeaponAimLayers)
            return;

        forcedAimPoseClip = aimPoseClip; // 강제 포즈
        useForcedAimPoseTime = false; // 시간 해제
        SetWeaponActionLayerWeight(1f);
    }

    public void SetForcedWeaponAimPose(AnimationClip aimPoseClip, float normalizedTime)
    {
        if (!useLegacyWeaponAimLayers)
            return;

        forcedAimPoseClip = aimPoseClip; // 강제 포즈
        forcedAimPoseNormalizedTime = Mathf.Clamp01(normalizedTime); // 고정 시간
        useForcedAimPoseTime = true; // 시간 사용
        SetWeaponActionLayerWeight(1f);
    }

    public void ClearForcedWeaponAimPose()
    {
        forcedAimPoseClip = null; // 강제 해제
        useForcedAimPoseTime = false; // 시간 해제
    }

    private void RefreshWeaponAimPoseOverride()
    {
        if (!useWeaponAimPoseOverride || targetAnimator == null)
            return;

        if (ShouldUseCurrentAimPoseFullBody())
            return;

        EnsureWeaponOverrideController();

        if (weaponOverrideController == null)
            return;

        AnimationClip desiredClip = GetDesiredAimPoseClip(); // 목표 포즈

        if (desiredClip == null)
            return;

        ApplyClipOverride(IsBaseAimPoseClip, desiredClip, ref activeAimPoseClip);

        if (useForcedAimPoseTime && desiredClip == forcedAimPoseClip)
            PlayWeaponAimStateAt(forcedAimPoseNormalizedTime); // 시간 고정
    }

    private void PlayWeaponAimStateAt(float normalizedTime)
    {
        if (targetAnimator == null || string.IsNullOrEmpty(aimStateName))
            return;

        EnsureActionStateHashes();
        int layerIndex = weaponActionLayerIndex; // 액션 layer
        if (layerIndex < 0)
            return;

        int stateHash = Animator.StringToHash(BuildActionStatePath(aimStateName)); // 전체 경로
        if (!targetAnimator.HasState(layerIndex, stateHash))
        {
            stateHash = Animator.StringToHash(aimStateName); // 짧은 이름
            if (!targetAnimator.HasState(layerIndex, stateHash))
                return;
        }

        targetAnimator.Play(stateHash, layerIndex, Mathf.Clamp01(normalizedTime));
        targetAnimator.Update(0f);
    }

    private void RefreshWeaponActionLayerWeight()
    {
        EnsureActionStateHashes();

        WeaponUpperBodyAimChannel targetChannel = ShouldUseWeaponActionLayer()
            ? GetCurrentUpperBodyAimChannel()
            : WeaponUpperBodyAimChannel.None;
        float step = Mathf.Max(0f, weaponActionLayerBlendSpeed) * Time.deltaTime;

        magicActionLayerWeight = MoveLayerWeight(magicActionLayerIndex, magicActionLayerWeight, targetChannel == WeaponUpperBodyAimChannel.Magic ? 1f : 0f, step);

        weaponActionLayerWeight = GetCurrentActionLayerWeight(targetChannel);
    }

    private bool ShouldUseWeaponActionLayer()
    {
        if (isMeleeFullBodyActionActive)
            return false;

        if (isFullBodyAimPoseActive)
            return false;

        if (GetCurrentUpperBodyAimChannel() == WeaponUpperBodyAimChannel.None)
            return false;

        if (playerController != null && playerController.IsWeaponAimPoseActive)
            return true;

        if (forcedAimPoseClip != null)
            return true;

        if (Time.time < quickFireAimPoseUntil)
            return true;

        return Time.time < weaponActionLayerHoldUntil;
    }

    private void ApplyMeleeAimUpperBodyYawOffset()
    {
        float yawOffset = GetCurrentMeleeAimUpperBodyYawOffset();
        if (!useMeleeAimUpperBodyYawOffset || Mathf.Approximately(yawOffset, 0f))
            return;

        if (targetAnimator == null || !targetAnimator.isHuman || playerController == null)
            return;

        if (!playerController.IsMeleeCombatStance || playerController.IsMeleeGuarding || isMeleeFullBodyActionActive)
            return;

        if (playerEquipment != null && !playerEquipment.CanCurrentWeaponUseMeleeCombatStance)
            return;

        Transform upperBody = targetAnimator.GetBoneTransform(HumanBodyBones.UpperChest);
        if (upperBody == null)
            upperBody = targetAnimator.GetBoneTransform(HumanBodyBones.Chest);

        if (upperBody == null)
            upperBody = targetAnimator.GetBoneTransform(HumanBodyBones.Spine);

        if (upperBody == null)
            return;

        upperBody.rotation = Quaternion.AngleAxis(yawOffset, transform.up) * upperBody.rotation;
    }

    private float GetCurrentMeleeAimUpperBodyYawOffset()
    {
        if (playerEquipment == null || !playerEquipment.HasCurrentWeapon)
            return meleeAimUpperBodyYawOffset;

        return playerEquipment.CurrentMeleeAimUpperBodyYawOffset;
    }

    private void HoldWeaponActionLayer(AnimationClip clip, float animationSpeed)
    {
        float duration = clip != null ? clip.length / Mathf.Max(0.01f, animationSpeed) : 0.25f;
        weaponActionLayerHoldUntil = Mathf.Max(weaponActionLayerHoldUntil, Time.time + Mathf.Max(0.05f, duration));
        SetWeaponActionLayerWeight(1f);
    }

    private void SetWeaponActionLayerWeight(float weight)
    {
        EnsureActionStateHashes();

        if (targetAnimator == null)
            return;

        weaponActionLayerWeight = Mathf.Clamp01(weight);
        WeaponUpperBodyAimChannel targetChannel = weaponActionLayerWeight > 0f
            ? GetCurrentUpperBodyAimChannel()
            : WeaponUpperBodyAimChannel.None;

        magicActionLayerWeight = SetLayerWeightImmediate(magicActionLayerIndex, targetChannel == WeaponUpperBodyAimChannel.Magic ? weaponActionLayerWeight : 0f);
    }

    private void SetFullBodyAimLayerWeight(float weight)
    {
        EnsureActionStateHashes();

        fullBodyAimLayerWeight = Mathf.Clamp01(weight);
        if (targetAnimator != null && fullBodyAimLayerIndex >= 0)
            targetAnimator.SetLayerWeight(fullBodyAimLayerIndex, fullBodyAimLayerWeight);
    }

    private float MoveLayerWeight(int layerIndex, float currentWeight, float targetWeight, float step)
    {
        float nextWeight = Mathf.MoveTowards(currentWeight, Mathf.Clamp01(targetWeight), step);
        if (targetAnimator != null && layerIndex >= 0)
            targetAnimator.SetLayerWeight(layerIndex, nextWeight);

        return nextWeight;
    }

    private float SetLayerWeightImmediate(int layerIndex, float weight)
    {
        float clampedWeight = Mathf.Clamp01(weight);
        if (targetAnimator != null && layerIndex >= 0)
            targetAnimator.SetLayerWeight(layerIndex, clampedWeight);

        return clampedWeight;
    }

    private float GetCurrentActionLayerWeight(WeaponUpperBodyAimChannel channel)
    {
        switch (channel)
        {
            case WeaponUpperBodyAimChannel.Magic:
                return magicActionLayerWeight;
            default:
                return 0f;
        }
    }

    private float CalculateRecoverAnimationSpeed(AnimationClip recoverClip, float targetDuration)
    {
        if (recoverClip == null || targetDuration <= 0f)
            return 1f;

        return Mathf.Max(0.01f, recoverClip.length / targetDuration);
    }

    private bool RestartWeaponActionState(string stateName)
    {
        if (!restartWeaponActionStates || string.IsNullOrEmpty(stateName) || targetAnimator == null)
            return false;

        EnsureActionStateHashes();

        if (weaponActionLayerIndex < 0)
            return false;

        int stateHash = stateName == fireStateName ? fireStateHash : recoverStateHash; // 캐시 hash
        if (stateHash == 0)
            return false;

        if (!targetAnimator.HasState(weaponActionLayerIndex, stateHash))
        {
            stateHash = Animator.StringToHash(stateName); // 짧은 이름
            if (!targetAnimator.HasState(weaponActionLayerIndex, stateHash))
                return false;
        }

        targetAnimator.Play(stateHash, weaponActionLayerIndex, 0f);
        targetAnimator.Update(0f);
        return true;
    }

    private void EnsureActionStateHashes()
    {
        ResolveActionLayerIndices();
        WeaponUpperBodyAimChannel resolvedChannel = GetCurrentUpperBodyAimChannel();
        int resolvedLayerIndex = ResolveWeaponActionLayerIndex(resolvedChannel);
        string resolvedLayerName = GetActionLayerName(resolvedChannel);

        if (actionStateHashesInitialized
            && weaponActionLayerIndex == resolvedLayerIndex
            && activeWeaponActionChannel == resolvedChannel
            && activeWeaponActionLayerName == resolvedLayerName)
            return;

        weaponActionLayerIndex = resolvedLayerIndex;
        activeWeaponActionChannel = resolvedChannel;
        activeWeaponActionLayerName = resolvedLayerIndex >= 0 ? resolvedLayerName : null;

        fireStateHash = !string.IsNullOrEmpty(fireStateName) ? Animator.StringToHash(BuildActionStatePath(fireStateName)) : 0;
        recoverStateHash = !string.IsNullOrEmpty(recoverStateName) ? Animator.StringToHash(BuildActionStatePath(recoverStateName)) : 0;
        fullBodyAimStateHash = Animator.StringToHash(FullBodyAimLayerName + "." + FullBodyAimStateName);
        actionStateHashesInitialized = true; // hash 완료
    }

    private void ResolveActionLayerIndices()
    {
        if (targetAnimator == null)
            return;

        magicActionLayerIndex = ResolveLayerIndexWithFallback(MagicActionLayerName, weaponActionLayerName);
        fullBodyAimLayerIndex = targetAnimator.GetLayerIndex(FullBodyAimLayerName);
    }

    private int ResolveWeaponActionLayerIndex(WeaponUpperBodyAimChannel channel)
    {
        switch (channel)
        {
            case WeaponUpperBodyAimChannel.Magic:
                return magicActionLayerIndex;
            default:
                return -1;
        }
    }

    private int ResolveLayerIndexWithFallback(params string[] layerNames)
    {
        if (targetAnimator == null || layerNames == null)
            return -1;

        for (int i = 0; i < layerNames.Length; i++)
        {
            string layerName = layerNames[i];
            if (string.IsNullOrEmpty(layerName))
                continue;

            int layerIndex = targetAnimator.GetLayerIndex(layerName);
            if (layerIndex >= 0)
                return layerIndex;
        }

        return -1;
    }

    private WeaponUpperBodyAimChannel GetCurrentUpperBodyAimChannel()
    {
        WeaponItemData weaponData = playerEquipment != null ? playerEquipment.CurrentWeaponData : null;
        if (weaponData == null)
            return WeaponUpperBodyAimChannel.None;

        return weaponData.GetResolvedUpperBodyAimChannel();
    }

    private string GetActionLayerName(WeaponUpperBodyAimChannel channel)
    {
        switch (channel)
        {
            case WeaponUpperBodyAimChannel.Magic:
                return magicActionLayerIndex >= 0 ? MagicActionLayerName : weaponActionLayerName;
            default:
                return null;
        }
    }

    private string BuildActionStatePath(string stateName)
    {
        string layerName = string.IsNullOrEmpty(activeWeaponActionLayerName)
            ? weaponActionLayerName
            : activeWeaponActionLayerName;

        if (string.IsNullOrEmpty(layerName))
            return stateName;

        return layerName + "." + stateName;
    }
}
