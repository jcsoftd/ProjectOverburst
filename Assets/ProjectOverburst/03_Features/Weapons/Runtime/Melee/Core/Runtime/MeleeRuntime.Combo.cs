using UnityEngine;

// MeleeRuntime partial: 콤보 이어가기, 입력 창, 이동 입력 취소. 필드와 Unity 수명주기는 MeleeRuntime.cs에 있다.
public partial class MeleeRuntime
{
    private static bool ShouldUseCombo(WeaponItemData weaponData)
    {
        MeleeComboDefinition comboDefinition = weaponData != null
            ? weaponData.GetMeleeComboDefinition()
            : null;
        return comboDefinition != null && comboDefinition.HasSteps;
    }

    private int ResolveNextComboStepIndex(MeleeComboDefinition comboDefinition)
    {
        int stepCount = comboDefinition.StepCount;
        float resetDelay = Mathf.Max(0.01f, comboDefinition.resetDelay);
        bool resetCombo = comboStepIndex < 0 || Time.time - lastComboWindowTime > resetDelay;
        int nextStepIndex = resetCombo ? 0 : (comboStepIndex + 1) % stepCount;

        comboStepIndex = nextStepIndex;
        return nextStepIndex;
    }

    private static float ResolveComboTransitionDuration(
        MeleeComboDefinition comboDefinition,
        MeleeComboStepData step,
        bool isDirectComboContinuation)
    {
        if (isDirectComboContinuation)
            return Mathf.Max(0f, step.transitionDuration);

        return Mathf.Max(0f, comboDefinition.entryTransitionDuration);
    }

    private float CalculateComboAnimationSpeed(MeleeComboDefinition comboDefinition, MeleeComboStepData step)
    {
        float baseSpeed = comboDefinition != null ? comboDefinition.baseAnimationSpeed : 1f;
        float playbackAttackSpeed = ResolveAttackPlaybackMultiplier();
        return Mathf.Max(0.01f, baseSpeed * playbackAttackSpeed * Mathf.Max(0.01f, step.animationSpeedMultiplier));
    }

    private bool ShouldContinueActiveCombo(float normalizedTime)
    {
        if (!manualInputEnabled
            || !activeAttackUsesCombo
            || !CanContinueCombo()
            || !activeAttackStep.comboInputWindow.Contains(normalizedTime)
            || GameplayInputBlocker.IsGameplayInputBlocked)
        {
            return false;
        }

        if (bufferedHandoffComboContinuation)
            return true;

        return IsPrimaryAttackInputPressed();
    }

    private bool IsPrimaryAttackInputPressed()
    {
        if (PlayerPickupInteractor.IsPrimaryAttackSuppressed)
            return false; // 월드 라벨 클릭 release까지 공격 누출 차단

        // GOAL A2: 좌클릭 홀드 직접 읽기 대신 Gameplay Attack 유지를 사용한다. 콤보 계속 의미 유지.
        PlayerInputFacade facade = ResolveFacade();
        return facade != null && facade.CombatInputs != null
            && (facade.CombatInputs.HasAttack || facade.CombatInputs.AllowsHeldAttack);
    }

    private void ContinueActiveCombo()
    {
        bufferedHandoffComboContinuation = false;
        WeaponActionRequest request = new WeaponActionRequest(
            activeActionSource,
            activeRequestedTarget,
            CaptureAttackStartDirection());
        TryContinue(new WeaponActionHandle(activeActionId), request);
    }

    private bool IsContinuationWindowOpen()
    {
        if (!isAttacking
            || !activeAttackUsesCombo
            || !CanContinueCombo()
            || attackDuration <= 0f)
        {
            return false;
        }

        float normalizedTime = GetAttackNormalizedTime();
        return activeAttackStep.comboInputWindow.Contains(normalizedTime);
    }

    private bool IsActiveComboActionOpen()
    {
        if (!activeAttackUsesCombo || attackDuration <= 0f)
            return false;

        float normalizedTime = GetAttackNormalizedTime();
        return normalizedTime >= Mathf.Clamp01(activeAttackStep.actionCancelStartNormalized);
    }

    private bool ShouldCancelActiveComboByMoveInput(float normalizedTime)
    {
        if (!manualInputEnabled
            || (!activeAttackUsesCombo && !activeAttackIsHeavy)
            || normalizedTime < Mathf.Clamp01(activeAttackStep.actionCancelStartNormalized))
        {
            return false;
        }

        bool hasRawMoveInput = HasRawMoveInput();
        if (suppressHandoffMoveCancelUntilRelease)
        {
            if (!hasRawMoveInput)
                suppressHandoffMoveCancelUntilRelease = false;

            return false;
        }

        return hasRawMoveInput;
    }

    private bool HasRawMoveInput()
    {
        if (GameplayInputBlocker.IsGameplayInputBlocked)
            return false;

        // GOAL A2: WASD 직접 읽기 대신 Gameplay Move 벡터를 사용한다. 후반 콤보 이동 취소 의미 유지.
        PlayerInputFacade facade = ResolveFacade();
        return facade != null && facade.MoveValue.sqrMagnitude > 0.001f;
    }

    private void CancelActiveAttackByMoveInput()
    {
        KeepComboWindowForCancel();
        CancelActiveAttack(WeaponActionCompletionReason.CancelledByMovement, false);
        playerAnimatorController?.CancelWeaponRuntimeState(); // 후반 이동 취소 시 공격 애니메이션도 종료
    }

    private void KeepComboWindowForCancel()
    {
        if (activeAttackUsesCombo && IsActiveComboActionOpen())
            lastComboWindowTime = Time.time;
    }

    private void ResetComboState()
    {
        comboStepIndex = -1;
        lastComboWindowTime = -999f;
    }

    private bool CanContinueCombo()
    {
        return activeComboDefinition != null
            && activeComboDefinition.StepCount > 1
            && comboStepIndex >= 0;
    }
}
