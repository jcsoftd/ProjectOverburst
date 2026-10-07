using UnityEngine;

// MeleeRuntime partial: IWeaponActionPort 진입점(시작·강공·연속·취소·상태 조회)과 요청 검증. 필드와 Unity 수명주기는 MeleeRuntime.cs에 있다.
public partial class MeleeRuntime
{
    public void SetManualInputEnabled(bool enabledValue)
    {
        manualInputEnabled = enabledValue;
        if (!enabledValue) ResolveFacade()?.CombatInputs?.Invalidate();
    }

    public void CancelCurrentAttackState()
    {
        CancelActiveAttack(WeaponActionCompletionReason.CancelledByRequest, true);
    }

    public void CancelCurrentAction()
    {
        CancelCurrentAction(WeaponActionCancelReason.Request);
    }

    public void CancelCurrentAction(WeaponActionCancelReason reason)
    {
        CancelActiveAttack(ResolveCompletionReason(reason), true);
    }

    public bool TryGetAttackWindow(out WeaponAttackWindow window)
    {
        ResolveReferences();
        window = default;
        if (!CanUseCurrentWeapon || playerEquipment == null)
            return false;

        WeaponItemData weaponData = playerEquipment.CurrentWeaponData;
        MeleeWeaponDefinition meleeDefinition = weaponData != null
            ? weaponData.GetMeleeDefinition()
            : null;
        if (meleeDefinition == null)
            return false;

        WeaponFinalStats stats = FlaskCombatModifiers.Apply(playerEquipment.CurrentWeaponStats, gameObject);
        float maxDistance = Mathf.Max(0.1f, stats.range);
        float verticalTolerance = 3f;
        MeleeComboDefinition comboDefinition = weaponData.GetMeleeComboDefinition();
        if (comboDefinition != null && comboDefinition.HasSteps)
        {
            AttackPhaseData[] phases = comboDefinition.GetStep(0).attackPhases;
            if (phases != null)
            {
                for (int i = 0; i < phases.Length; i++)
                {
                    if (phases[i].attackPattern == null)
                        continue;

                    AttackPatternRuntimeData pattern = phases[i].ResolvePattern(
                        stats.range,
                        stats.meleeSlashAngle,
                        meleeDefinition.baseSettings.hitWidth);
                    maxDistance = Mathf.Max(
                        maxDistance,
                        pattern.Range + Mathf.Max(0f, pattern.ForwardOffset));
                    verticalTolerance = Mathf.Max(verticalTolerance, pattern.VerticalTolerance);
                }
            }
        }

        window = new WeaponAttackWindow(
            0f,
            maxDistance * 0.72f,
            maxDistance,
            verticalTolerance);
        return true;
    }

    public WeaponActionResult TryStartAction(
        in WeaponActionRequest request,
        out WeaponActionHandle handle)
    {
        ResolveReferences();
        handle = WeaponActionHandle.Invalid;
        if (isAttacking || activeActionId > 0)
            return WeaponActionResult.RejectedBusy;

        if (!CanUseCurrentWeapon)
            return WeaponActionResult.RejectedUnsupported;

        if (request.Source == WeaponActionSource.PlayerInput && !manualInputEnabled)
            return WeaponActionResult.RejectedNotReady;

        if (IsPlayerEvading() || !CanAttackFromCurrentMovementState())
            return WeaponActionResult.RejectedNotReady;

        WeaponActionResult validation = ValidateActionRequest(request);
        if (validation != WeaponActionResult.Accepted)
            return validation;

        suppressHandoffMoveCancelUntilRelease = false;
        int actionId = AllocateActionId();
        activeActionId = actionId;
        activeActionSource = request.Source;
        activeRequestedTarget = request.Target;
        Vector3 attackDirection = ResolveRequestDirection(request);
        if (!TryStartAttackStep(false, attackDirection))
            return WeaponActionResult.RejectedNotReady;

        handle = new WeaponActionHandle(actionId);
        if (request.Source == WeaponActionSource.PlayerInput)
            ResolveFacade()?.CombatInputs?.ConsumeAttack();
        NotifyAcceptedMeleeAction();
        return WeaponActionResult.Accepted;
    }

    public WeaponActionResult TryStartHeavyAttack(Vector3 requestedDirection)
    {
        ResolveReferences();
        if ((isAttacking || activeActionId > 0) && (!activeAttackUsesCombo || activeAttackIsHeavy))
            return WeaponActionResult.RejectedBusy;
        if (!CanUseCurrentWeapon)
            return WeaponActionResult.RejectedUnsupported;
        if (!manualInputEnabled || IsPlayerEvading() || !CanAttackFromCurrentMovementState())
            return WeaponActionResult.RejectedNotReady;

        MeleeHeavyAttackDefinition heavy = playerEquipment.CurrentWeaponData
            .GetMeleeDefinition() is MeleeWeaponDefinition definition
                ? (requestedDodgeFollowUp == PlayerDodgeFollowUpKind.Heavy ? definition.dashHeavyAttackDefinition : definition.heavyAttackDefinition) : null;
        if (heavy == null || !heavy.IsConfigured)
            return WeaponActionResult.RejectedUnsupported;

        if (isAttacking)
            CancelActiveAttack(WeaponActionCompletionReason.CancelledByRequest, true);

        suppressHandoffMoveCancelUntilRelease = false;
        ResetComboState();
        activeActionId = AllocateActionId();
        activeActionSource = WeaponActionSource.PlayerInput;
        activeRequestedTarget = null;
        if (!TryStartAttackStep(false, requestedDirection, true))
            return WeaponActionResult.RejectedNotReady;

        ResolveFacade()?.CombatInputs?.ConsumeHeavy();
        var parry = GetComponent<PlayerParryController>();
        if (parry == null) parry = gameObject.AddComponent<PlayerParryController>();
        parry.OpenForHeavy(activeActionId);
        if (activeWeaponData.weaponClass == WeaponClass.Greatsword)
        {
            heavyParrySwingPending = IsHeavyParryMotionActive;
            if (!heavyParrySwingPending && activeDodgeFollowUp != PlayerDodgeFollowUpKind.Heavy)
                CombatActionSfxService.PlayGreatswordSwing(comboStepIndex, true, transform.position);
        }
        NotifyAcceptedMeleeAction();
        return WeaponActionResult.Accepted;
    }

    public WeaponActionResult TryContinue(
        in WeaponActionHandle handle,
        in WeaponActionRequest request)
    {
        if (!handle.IsValid
            || handle.ActionId != activeActionId
            || !isAttacking)
        {
            return WeaponActionResult.RejectedNotReady;
        }

        if (request.Source != activeActionSource)
            return WeaponActionResult.RejectedInvalidTarget;

        if (activeActionSource == WeaponActionSource.CompanionAI
            && request.Target != activeRequestedTarget)
        {
            return WeaponActionResult.RejectedInvalidTarget;
        }

        if (!CanContinueCombo() || !IsContinuationWindowOpen())
            return WeaponActionResult.RejectedNotReady;

        WeaponActionResult validation = ValidateActionRequest(request);
        if (validation != WeaponActionResult.Accepted)
            return validation;

        lastComboWindowTime = Time.time;
        Vector3 attackDirection = ResolveRequestDirection(request);
        if (!TryStartAttackStep(true, attackDirection))
            return WeaponActionResult.RejectedNotReady;

        if (request.Source == WeaponActionSource.PlayerInput)
            ResolveFacade()?.CombatInputs?.ConsumeAttack();
        NotifyAcceptedMeleeAction();
        return WeaponActionResult.Accepted;
    }

    public bool TryTransferActiveActionToPlayerInput(
        in WeaponActionHandle handle)
    {
        if (!handle.IsValid
            || handle.ActionId != activeActionId
            || !isAttacking
            || activeActionSource != WeaponActionSource.CompanionAI
            || !activeAttackUsesCombo
            || !CanContinueCombo())
        {
            return false;
        }

        activeActionSource = WeaponActionSource.PlayerInput;
        activeRequestedTarget = null;
        bufferedHandoffComboContinuation = true;
        return true;
    }

    public bool TryArmPlayerComboHandoff()
    {
        if (activeActionId <= 0
            || !isAttacking
            || activeActionSource != WeaponActionSource.PlayerInput
            || !activeAttackUsesCombo
            || !CanContinueCombo())
        {
            return false;
        }

        suppressHandoffMoveCancelUntilRelease = HasRawMoveInput();
        bufferedHandoffComboContinuation = true;
        return true;
    }

    public bool IsActivePlayerInputAction(int actionRevision)
    {
        return actionRevision > 0
            && actionRevision == activeActionId
            && isAttacking
            && activeActionSource == WeaponActionSource.PlayerInput;
    }

    public bool TryGetActionState(
        in WeaponActionHandle handle,
        out WeaponActionState state)
    {
        if (!handle.IsValid)
        {
            state = default;
            return false;
        }

        if (handle.ActionId == activeActionId)
        {
            state = WeaponActionState.Running(
                IsContinuationWindowOpen(),
                activeAttackDirection);
            return true;
        }

        if (handle.ActionId == terminalActionId)
        {
            state = terminalActionState;
            return true;
        }

        state = default;
        return false;
    }

    public void CancelAction(
        in WeaponActionHandle handle,
        WeaponActionCancelReason reason)
    {
        if (!handle.IsValid || handle.ActionId != activeActionId)
            return;

        CancelActiveAttack(ResolveCompletionReason(reason), true);
    }

    public void CancelActiveComboForEvade()
    {
        if (!CanCancelActiveComboForEvade)
            return;

        CancelActiveAttack(
            WeaponActionCompletionReason.CancelledByEvade,
            MeleeEvadeCancelPolicy.ResetComboOnCancel); // 구르기 뒤에는 항상 1타부터 시작
        playerAnimatorController?.CancelWeaponRuntimeState(); // 회피 시작 시 콤보 후반 공격 애니메이션 종료
        playerController?.CancelWeaponActionLocks();
    }

    public WeaponRuntimeStatus GetRuntimeStatus()
    {
        return new WeaponRuntimeStatus(RuntimeKind, CanUseCurrentWeapon, IsBusy, IsReady, CooldownRemaining, CooldownProgress01);
    }

    private WeaponActionResult ValidateActionRequest(in WeaponActionRequest request)
    {
        if (request.Source == WeaponActionSource.PlayerInput)
            return WeaponActionResult.Accepted;

        CombatTarget target = request.Target;
        if (combatTarget == null
            || target == null
            || target.Team != CombatTeam.Enemy
            || !CombatTargetFilter.CanDamage(combatTarget, target))
        {
            return WeaponActionResult.RejectedInvalidTarget;
        }

        if (!TryGetAttackWindow(out WeaponAttackWindow window))
            return WeaponActionResult.RejectedUnsupported;

        CombatTargetVolume ownerVolume = combatTarget.CurrentVolume;
        CombatTargetVolume targetVolume = target.CurrentHurtVolume;
        Vector3 offset = targetVolume.Center - ownerVolume.Center;
        float planarCenterDistance = new Vector2(offset.x, offset.z).magnitude;
        float surfaceDistance = Mathf.Max(
            0f,
            planarCenterDistance - ownerVolume.Radius - targetVolume.Radius);
        float verticalGap = Mathf.Max(
            0f,
            Mathf.Abs(offset.y) - ownerVolume.HalfHeight - targetVolume.HalfHeight);

        if (surfaceDistance < window.MinStartDistance
            || surfaceDistance > window.MaxStartDistance
            || verticalGap > window.VerticalTolerance)
        {
            return WeaponActionResult.RejectedOutOfRange;
        }

        return WeaponActionResult.Accepted;
    }

    private Vector3 ResolveRequestDirection(in WeaponActionRequest request)
    {
        Vector3 direction = request.AimDirection;
        if (direction.sqrMagnitude <= 0.0001f && request.Target != null)
            direction = request.Target.WorldCenter - transform.position;

        return ResolvePlanarDirection(direction);
    }

    private Vector3 ResolvePlanarDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            direction = transform.forward;
            direction.y = 0f;
        }

        return direction.sqrMagnitude > 0.0001f
            ? direction.normalized
            : Vector3.forward;
    }

    private static WeaponActionCompletionReason ResolveCompletionReason(
        WeaponActionCancelReason reason)
    {
        switch (reason)
        {
            case WeaponActionCancelReason.WeaponSwitch:
                return WeaponActionCompletionReason.CancelledByWeaponSwitch;
            case WeaponActionCancelReason.Evade:
                return WeaponActionCompletionReason.CancelledByEvade;
            case WeaponActionCancelReason.Movement:
                return WeaponActionCompletionReason.CancelledByMovement;
            case WeaponActionCancelReason.Recovery:
                return WeaponActionCompletionReason.CancelledByRecovery;
            case WeaponActionCancelReason.Death:
                return WeaponActionCompletionReason.CancelledByDeath;
            case WeaponActionCancelReason.RuntimeDisabled:
                return WeaponActionCompletionReason.RuntimeDisabled;
            case WeaponActionCancelReason.InvalidConfiguration:
                return WeaponActionCompletionReason.InvalidConfiguration;
            default:
                return WeaponActionCompletionReason.CancelledByRequest;
        }
    }

    public float HeavyParryEnergyNormalized => activeAttackIsHeavy && activeHeavyEnergy != null
        && activeAttackWeaponItem != null && activeHeavyEnergy.WeaponInstanceId == activeAttackWeaponItem.runtimeInstanceId
        && activeHeavyEnergy.Element == activeGemAttack.Element ? activeHeavyEnergy.Normalized : 0f;

    // Compatibility for direct counter-motion tools; product defense supplies the locked grade.
    public void NotifyHeavyParried(int actionId) => NotifyHeavyParried(actionId, ParryGrade.Perfect);

    public void NotifyHeavyParried(int actionId, ParryGrade grade) => NotifyHeavyParried(actionId, grade, null);

    public void NotifyHeavyParried(int actionId, ParryGrade grade, EnemyActor counterTarget)
    {
        if (!activeAttackIsHeavy || heavyParried || heavyParryOnly || actionId <= 0 || actionId != activeActionId) return;
        if (grade == ParryGrade.Incomplete)
        {
            // A released slam keeps its committed effects and recovery; never refund or restart it.
            if (heavyDischargeCommitted) return;
            heavyParryOnly = true;
            if (!TryBeginHeavyParryMotion(true)) FinishIncompleteParry();
            return;
        }
        heavyParried = true;
        SetHeavyParryCounterTarget(counterTarget);
        if (heavyDischargeCommitted) activeDischarge?.TryRefundParried();
        else TryBeginHeavyParryMotion();
    }
}
