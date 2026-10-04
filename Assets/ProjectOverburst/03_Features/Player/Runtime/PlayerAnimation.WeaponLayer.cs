using UnityEngine;
using System;
using System.Collections.Generic;

// PlayerAnimation partial: 무기 조준 포즈·상체 액션 레이어 가중치·레이어 찾기. 필드와 Unity 수명주기는 PlayerAnimation.cs에 있다.
public partial class PlayerAnimation
{
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

        // 마법 상체 레이어를 없앤 뒤 상체 조준 채널은 None뿐이라, 액션 레이어 가중치는 매 프레임 0으로 돌아간다.
        weaponActionLayerWeight = 0f;
    }

    private void ApplyMeleeAimUpperBodyYawOffset()
    {
        var facing = combatFacing;
        if (facing != null && facing.Set != null) return;
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
    }

    private void SetFullBodyAimLayerWeight(float weight)
    {
        EnsureActionStateHashes();

        fullBodyAimLayerWeight = Mathf.Clamp01(weight);
        if (targetAnimator != null && fullBodyAimLayerIndex >= 0)
            targetAnimator.SetLayerWeight(fullBodyAimLayerIndex, fullBodyAimLayerWeight);
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

        fullBodyAimLayerIndex = targetAnimator.GetLayerIndex(FullBodyAimLayerName);
    }

    private int ResolveWeaponActionLayerIndex(WeaponUpperBodyAimChannel channel)
    {
        return -1; // 마법 상체 레이어 제거 뒤 연결된 상체 액션 레이어가 없다.
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
        return null; // 마법 상체 레이어 제거 뒤 연결된 상체 액션 레이어가 없다.
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
