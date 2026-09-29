using UnityEngine;
using System;
using System.Collections.Generic;

// PlayerAnimation partial: 이동 입력·전투 이동 상태·기본 레이어 상태 재생. 필드와 Unity 수명주기는 PlayerAnimation.cs에 있다.
public partial class PlayerAnimation
{
    private Vector2 GetAnimatorMoveInput()
    {
        if (playerController.MoveInput.sqrMagnitude <= 0.001f)
            return Vector2.zero;

        if (!playerController.IsCombatMoveMode)
            return new Vector2(0f, Mathf.Clamp01(playerController.MoveInput.magnitude));

        Vector3 localMove = transform.InverseTransformDirection(playerController.MoveDirection); // 로컬 이동

        return Vector2.ClampMagnitude(new Vector2(localMove.x, localMove.z), 1f);
    }

    private void RefreshCombatLocomotionState()
    {
        if (!useCombatLocomotionStates || targetAnimator == null || playerController == null)
            return;

        if (!playerController.IsGrounded || targetAnimator.IsInTransition(0))
            return;

        string targetStateName = null;
        if (playerController.IsMeleeCombatLocomotionMode)
            targetStateName = playerController.IsMeleeGuarding ? combatGuardLocomotionStateName : combatLocomotionStateName;
        else if (IsCurrentBaseState(combatLocomotionStateName) || IsCurrentBaseState(combatGuardLocomotionStateName))
            targetStateName = normalLocomotionStateName;

        if (string.IsNullOrEmpty(targetStateName) || IsCurrentBaseState(targetStateName))
            return;

        if (!TryResolveBaseLayerStateHash(targetStateName, out int stateHash))
            return;

        float transitionDuration = ResolveCombatLocomotionTransitionDuration(targetStateName);
        float fixedTimeOffset = ResolveCombatLocomotionFixedTimeOffset(targetStateName);
        targetAnimator.CrossFadeInFixedTime(
            stateHash,
            transitionDuration,
            0,
            fixedTimeOffset);
    }

    private float ResolveCombatLocomotionTransitionDuration(string targetStateName)
    {
        if (!useCombatEquipToLocomotionBlendOnNextEnter)
            return Mathf.Max(0f, combatLocomotionTransitionDuration);

        if (!IsCombatLocomotionTargetState(targetStateName))
            return Mathf.Max(0f, combatLocomotionTransitionDuration);

        useCombatEquipToLocomotionBlendOnNextEnter = false;
        float transitionDuration = Mathf.Max(0f, combatEquipToLocomotionTransitionDuration);
        SchedulePendingMeleeFullBodyRestore(transitionDuration);
        return transitionDuration;
    }

    private float ResolveCombatLocomotionFixedTimeOffset(string targetStateName)
    {
        if (!useCombatLocomotionEntryOffsetOnNextEnter)
            return 0f;

        if (!IsCombatLocomotionTargetState(targetStateName))
            return 0f;

        useCombatLocomotionEntryOffsetOnNextEnter = false;
        return Mathf.Max(0f, combatLocomotionEntryFixedTimeOffset);
    }

    private bool IsCombatLocomotionTargetState(string stateName)
    {
        return string.Equals(stateName, combatLocomotionStateName, StringComparison.Ordinal)
            || string.Equals(stateName, combatGuardLocomotionStateName, StringComparison.Ordinal);
    }

    private bool IsCurrentBaseState(string stateName)
    {
        if (targetAnimator == null || string.IsNullOrEmpty(stateName))
            return false;

        AnimatorStateInfo stateInfo = targetAnimator.GetCurrentAnimatorStateInfo(0);
        return stateInfo.shortNameHash == Animator.StringToHash(stateName)
            || stateInfo.fullPathHash == Animator.StringToHash(BuildBaseLayerStatePath(stateName));
    }

    private string BuildBaseLayerStatePath(string stateName)
    {
        return BaseLayerName + "." + stateName;
    }

    private bool TryResolveBaseLayerStateHash(string stateName, out int stateHash)
    {
        stateHash = 0;
        if (targetAnimator == null || string.IsNullOrEmpty(stateName))
            return false;

        int fullPathHash = Animator.StringToHash(BuildBaseLayerStatePath(stateName));
        if (targetAnimator.HasState(0, fullPathHash))
        {
            stateHash = fullPathHash;
            return true;
        }

        int shortNameHash = Animator.StringToHash(stateName);
        if (targetAnimator.HasState(0, shortNameHash))
        {
            stateHash = shortNameHash;
            return true;
        }

        return false;
    }

    private void PlayBaseLayerState(string stateName)
    {
        PlayBaseLayerState(stateName, 0f);
    }

    private void PlayBaseLayerState(string stateName, float fixedTransitionDuration)
    {
        if (!TryResolveBaseLayerStateHash(stateName, out int stateHash))
            return;

        if (fixedTransitionDuration > 0f)
            targetAnimator.CrossFadeInFixedTime(stateHash, fixedTransitionDuration, 0, 0f);
        else
            targetAnimator.Play(stateHash, 0, 0f);

        targetAnimator.Update(0f);
    }
}
