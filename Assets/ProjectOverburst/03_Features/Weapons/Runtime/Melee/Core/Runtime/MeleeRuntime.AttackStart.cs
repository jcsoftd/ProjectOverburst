using UnityEngine;

// MeleeRuntime partial: 공격 시작 판정, 단계 시작, 애니메이션·방향·지속시간 결정. 필드와 Unity 수명주기는 MeleeRuntime.cs에 있다.
public partial class MeleeRuntime
{
    private bool ShouldStartAttack()
    {
        if (!CanStartManualAttackInput())
            return false;

        if (bufferedHandoffComboContinuation)
            return true;

        return IsPrimaryAttackInputPressed();
    }

    private bool ShouldStartHeavyAttack()
    {
        if (!manualInputEnabled || activeAttackIsHeavy
            || (isAttacking && !activeAttackUsesCombo)
            || !CanUseCurrentWeapon || IsPlayerEvading()
            || !CanAttackFromCurrentMovementState())
            return false;

        MeleeWeaponDefinition melee = playerEquipment.CurrentWeaponData.GetMeleeDefinition();
        PlayerInputFacade facade = ResolveFacade();
        return melee != null && melee.heavyAttackDefinition != null
            && facade != null && facade.CombatInputs != null
            && facade.CombatInputs.HasHeavy;
    }

    private bool CanStartManualAttackInput()
    {
        return !isAttacking && manualInputEnabled
            && !GameplayInputBlocker.IsGameplayInputBlocked
            && !IsPlayerEvading()
            && playerEquipment != null && playerEquipment.CanCurrentWeaponUseMeleeSlash
            && CanAttackFromCurrentMovementState();
    }

    private bool IsPlayerEvading()
    {
        return playerController != null && playerController.IsEvading;
    }

    private bool TryStartAttackStep(bool isDirectComboContinuation, Vector3 requestedDirection, bool isHeavy = false)
    {
        if (isAttacking)
            StopActiveAttackStep();

        comboMovementCollisionPusher.BeginComboStep(); // 새 타수의 이동 충돌 기록 초기화
        activeHitFeedbackSequenceId = AllocateHitFeedbackSequenceId(); // 홀드 콤보도 타수별 분리
        activeStats = UpperElementCombatUtility.ApplyRadianceAttackSpeed(
            FlaskCombatModifiers.Apply(playerEquipment.CurrentWeaponStats, gameObject), gameObject);
        activeWeaponData = playerEquipment.CurrentWeaponData;
        activeComboDefinition = activeWeaponData != null
            ? activeWeaponData.GetMeleeComboDefinition()
            : null;
        activeHeavyDefinition = isHeavy && activeWeaponData != null
            ? activeWeaponData.GetMeleeDefinition()?.heavyAttackDefinition
            : null;
        activeAttackIsHeavy = isHeavy;
        heavyDischargeCommitted = false;
        heavyParried = false;
        activeDischarge = null;
        activeAttackWeaponItem = playerEquipment.CurrentWeaponItem;
        activeAttackUsedMeleeCombatStance = playerController != null && playerController.IsMeleeCombatStance;
        activeAttackUsesCombo = !isHeavy && ShouldUseCombo(activeWeaponData);
        ResolveAttackAnimation(isDirectComboContinuation);

        activeHeavyEnergy = isHeavy ? GetComponent<OverburstElementEnergy>() : null;
        bool hasEnergy = activeHeavyEnergy != null && activeHeavyEnergy.Amount > 0f
            && activeAttackWeaponItem != null
            && activeHeavyEnergy.WeaponInstanceId == activeAttackWeaponItem.runtimeInstanceId
            && activeHeavyEnergy.Element == activeAttackWeaponItem.ResolvedElement;
        activeAttackDamageMultiplier = isHeavy
            ? activeHeavyDefinition.GetDamageMultiplier(hasEnergy)
            : 1f;
        if (activeWeaponData != null && activeWeaponData.weaponClass == WeaponClass.Greatsword)
            activeAttackDamageMultiplier = isHeavy ? OverburstCombatBalance.EmptyHeavyDamage : OverburstCombatBalance.GreatswordWeakDamage;


        attackDuration = ResolveAttackDuration();
        float entryProgress = isDirectComboContinuation && activeAttackUsesCombo
            ? Mathf.Clamp(activeAttackStep.continuationStartNormalizedTime, 0f, .95f) : 0f;
        MeleePlaybackAcceleration acceleration = activeAttackStep.playbackAcceleration;
        float remainingDuration = attackDuration * (acceleration.ToElapsed(1f) - acceleration.ToElapsed(entryProgress));
        ResolveAttackTrail(activeAttackStep);
        ResolveAttackPhases();

        playerEquipment.RefreshCurrentWeaponReferences();
        activeAttackDirection = ResolvePlanarDirection(requestedDirection);
        if (!TryBeginAttackPhases())
        {
            CancelActiveAttack(WeaponActionCompletionReason.InvalidConfiguration, true);
            return false;
        }

        if (!attackMovementExecutor.Begin(
                activeAttackStep.movementPhases,
                activeAttackDirection,
                ApplyAttackDisplacement,
                entryProgress)
            || !attackTrailExecutor.Begin(
                activeAttackStep.trailPhases,
                StartAttackTrail,
                StopAttackTrail))
        {
            CancelActiveAttack(WeaponActionCompletionReason.InvalidConfiguration, true);
            return false;
        }

        attackVisualHeight.Begin(transform.Find("VisualRoot"), activeAttackStep.visualHeightCurve);
        attackVisualHeight.Tick(entryProgress);
        RotateOwnerToAttackDirection();
        isAttacking = true;
        attackStartTime = Time.time - attackDuration * acceleration.ToElapsed(entryProgress);

        playerEquipment.CurrentWeaponPose?.BeginActivePose(remainingDuration);

        MeleeAttackLock.Begin(
            playerController,
            remainingDuration,
            activeAttackDirection);

        // GOAL A2: 공격 이동 ControlledMove와 melee action Attack을 명시 요청한다.
        RequestAttackStates();

        bool animationStarted = playerAnimatorController != null
            && playerAnimatorController.PlayMeleeCombatAttack(
                isHeavy ? 0 : comboStepIndex,
                activeAttackAnimationClip,
                activeAttackAnimationSpeed,
                remainingDuration,
                activeAttackTransitionDuration,
                activeActionSource == WeaponActionSource.PlayerInput,
                entryProgress, acceleration);
        if (!animationStarted)
        {
            Debug.LogError("[MeleeRuntime] Formal melee combat animation could not start.", this);
            CancelActiveAttack(WeaponActionCompletionReason.InvalidConfiguration, true);
            return false;
        }

        if (activeWeaponData != null && activeWeaponData.weaponClass == WeaponClass.Greatsword)
            CombatActionSfxService.PlayGreatswordSwing(comboStepIndex, isHeavy, transform.position);

        return true;
    }

    private void NotifyAcceptedMeleeAction()
    {
        if (activeActionSource == WeaponActionSource.PlayerInput)
        {
            PlayerCombatModeController.NotifyPlayerMeleeAccepted(
                playerActorRuntime,
                activeActionId);
            return;
        }

    }

    private void ResolveAttackAnimation(bool isDirectComboContinuation)
    {
        WeaponAnimationSettings animation = activeWeaponData != null && activeWeaponData.combatDefinition != null
            ? activeWeaponData.combatDefinition.animation
            : default;
        activeAttackAnimationClip = animation.primaryAttackClip;
        activeAttackAnimationSpeed = Mathf.Max(0.01f, animation.primaryAttackSpeed);
        activeAttackTransitionDuration = 0f;
        activeAttackStep = default;
        ResetActiveTrailState();

        if (activeAttackIsHeavy)
        {
            activeAttackStep = activeHeavyDefinition.attack;
            activeAttackAnimationClip = activeAttackStep.animationClip;
            activeAttackAnimationSpeed = Mathf.Max(
                0.01f,
                ResolveAttackPlaybackMultiplier()
                    * Mathf.Max(0.01f, activeAttackStep.animationSpeedMultiplier));
            activeAttackTransitionDuration = Mathf.Max(0f, activeAttackStep.transitionDuration);
            return;
        }

        if (!activeAttackUsesCombo)
            return;

        int stepIndex = ResolveNextComboStepIndex(activeComboDefinition);
        MeleeComboStepData step = activeComboDefinition.GetStep(stepIndex);
        activeAttackStep = step;
        activeAttackAnimationClip = step.animationClip != null ? step.animationClip : animation.primaryAttackClip;
        activeAttackAnimationSpeed = CalculateComboAnimationSpeed(activeComboDefinition, step);
        activeAttackTransitionDuration = ResolveComboTransitionDuration(
            activeComboDefinition,
            step,
            isDirectComboContinuation);
    }

    private float ResolveAttackPlaybackMultiplier()
    {
        MeleeWeaponDefinition melee = activeWeaponData != null ? activeWeaponData.GetMeleeDefinition() : null;
        float baseline = melee != null
            ? melee.baseSettings.SafeAnimationPlaybackBaseline
            : MeleeAttackSpeedPolicy.BaselineAnimationSpeedMultiplier;
        return MeleeAttackSpeedPolicy.ToPlaybackMultiplier(
            Mathf.Max(0.01f, activeStats.meleeAttackSpeedMultiplier), baseline);
    }

    private void ResolveAttackPhases()
    {
        activeAttackPhases = activeAttackStep.attackPhases;
    }

    private WeaponElement ResolveActiveAttackElement()
    {
        return activeAttackWeaponItem != null ? activeAttackWeaponItem.ResolvedElement : WeaponElement.None;
    }

    private bool TryBeginAttackPhases()
    {
        if (activeAttackPhases == null || activeAttackPhases.Length == 0)
        {
            Debug.LogError(
                "[MeleeRuntime] The melee attack has no configured AttackPhase. The attack was cancelled.",
                this);
            return false;
        }

        ResolveAttackPatternDebugRenderer();
        MeleeWeaponDefinition meleeDefinition = activeWeaponData != null
            ? activeWeaponData.GetMeleeDefinition()
            : null;
        if (meleeDefinition == null)
            return false;

        MeleeAttackStepTrajectoryBakeData bakedTrajectoryStep = null;
        string trajectoryError = "콤보 정의가 없습니다.";
        if (RequiresBakedAttackTrajectory(activeAttackPhases)
            && (activeComboDefinition == null
                || !activeComboDefinition.TryGetAttackTrajectoryStep(
                    comboStepIndex,
                    out bakedTrajectoryStep,
                    out trajectoryError)))
        {
            Debug.LogError(
                "[MeleeRuntime] WeaponTip 공격 궤적 베이크가 유효하지 않습니다. " + trajectoryError,
                this);
            return false;
        }

        bool started = attackPhaseExecutor.Begin(
            activeAttackPhases,
            transform,
            combatTarget,
            activeAttackDirection,
            activeStats,
            meleeDefinition.baseSettings,
            ResolveActiveAttackElement(),
            activeAttackDamageMultiplier,
            bakedTrajectoryStep,
            playerEquipment.CurrentWeaponTraceBinding,
            attackPatternDebugRenderer,
            DealPatternDamage,
            activeHitFeedbackSequenceId,
            activeAttackWeaponItem != null ? activeAttackWeaponItem.runtimeInstanceId : string.Empty,
            EarthElementZoneRuntimeService.ReportAttackRange);

        if (!started)
        {
            Debug.LogError(
                "[MeleeRuntime] AttackPhase execution failed. The attack was cancelled.",
                this);
        }

        return started;
    }

    private static bool RequiresBakedAttackTrajectory(AttackPhaseData[] phases)
    {
        if (phases == null)
            return false;

        for (int i = 0; i < phases.Length; i++)
        {
            if (phases[i].progressSource != AttackProgressSource.NormalizedTime)
                return true;
        }

        return false;
    }

    private void ResolveAttackPatternDebugRenderer()
    {
        if (attackPatternDebugRenderer == null)
            attackPatternDebugRenderer = GetComponent<AttackPatternDebugRenderer>();
    }

    private bool CanAttackFromCurrentMovementState()
    {
        if (playerController == null)
            return true;

        if (!playerController.IsGrounded)
            return false;

        // GOAL A2: Space 직접 읽기 대신 Gameplay Jump 눌림으로 공격 시작을 차단한다(점프 우선). 부정 로직 보존.
        PlayerInputFacade facade = ResolveFacade();
        return (stateCoordinator == null || stateCoordinator.CurrentCondition == PlayerConditionState.Normal)
            && !GameplayInputBlocker.IsGameplayInputBlocked
            && (facade == null || !facade.JumpPressedThisFrame);
    }

    private Vector3 CaptureAttackStartDirection()
    {
        if (!MeleeAimCalculator.TryGetMouseDirectionFromPlayer(
                transform,
                attackCamera,
                out Vector3 attackDirection))
        {
            attackDirection = transform.forward; // 방향 fallback
        }

        attackDirection.y = 0f; // XZ 평면

        if (attackDirection.sqrMagnitude <= 0.0001f)
            attackDirection = Vector3.forward;

        return attackDirection.normalized;
    }

    private void RotateOwnerToAttackDirection()
    {
        Vector3 attackDirection = activeAttackDirection;
        attackDirection.y = 0f;

        if (attackDirection.sqrMagnitude <= 0.0001f)
            return;

        transform.rotation = Quaternion.LookRotation(attackDirection, Vector3.up);
    }

    private float ResolveAttackDuration()
    {
        if (activeAttackAnimationClip != null)
            return Mathf.Max(MinAttackDuration, activeAttackAnimationClip.length / Mathf.Max(0.01f, activeAttackAnimationSpeed));

        return MinAttackDuration;
    }
}
