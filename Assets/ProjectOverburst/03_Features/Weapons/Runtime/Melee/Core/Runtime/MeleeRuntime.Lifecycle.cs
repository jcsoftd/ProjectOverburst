using UnityEngine;

// MeleeRuntime partial: 공격 갱신, 강공 원소 방출 확정, 취소·종료, 공격 상태 요청. 필드와 Unity 수명주기는 MeleeRuntime.cs에 있다.
public partial class MeleeRuntime
{
    private void UpdateAttack()
    {
        if (!isAttacking)
            return;

        float normalizedTime = GetAttackNormalizedTime();
        bool shouldContinueCombo = ShouldContinueActiveCombo(normalizedTime);
        bool shouldCancelByMoveInput = !shouldContinueCombo
            && ShouldCancelActiveComboByMoveInput(normalizedTime);

        if (!shouldContinueCombo && !shouldCancelByMoveInput)
        {
            attackMovementExecutor.Tick(normalizedTime);
            attackVisualHeight.Tick(normalizedTime);
            attackTrailExecutor.Tick(normalizedTime);
        }

        CommitHeavyDischargeAtImpact(normalizedTime);
        if (!isAttacking) return;
        attackPhaseExecutor.Tick(normalizedTime); // 전환·취소 프레임의 마지막 검끝 표본까지 먼저 판정
        if (!isAttacking) return;
        heavyDischargeExecutor.ResolvePendingArea();
        heavyDischargeExecutor.Tick(Time.deltaTime);

        if (shouldContinueCombo)
        {
            ContinueActiveCombo();
            return;
        }

        if (shouldCancelByMoveInput)
        {
            CancelActiveAttackByMoveInput();
            return;
        }

        if (normalizedTime >= 1f)
            FinishActiveAttack();
    }

    private float GetAttackNormalizedTime()
    {
        return attackDuration > 0f
            ? Mathf.Clamp01(activeAttackStep.playbackAcceleration.ToClipProgress((Time.time - attackStartTime) / attackDuration))
            : 1f;
    }

    private void CommitHeavyDischargeAtImpact(float normalizedTime)
    {
        using var costScope = ElementCombatCostMarkers.Heavy_Commit.Auto();
        if (!activeAttackIsHeavy || heavyDischargeCommitted || activeAttackPhases == null
            || activeAttackPhases.Length == 0
            || normalizedTime < activeAttackPhases[0].SafeStart)
            return;

        float normalizedEnergy = activeHeavyEnergy != null ? activeHeavyEnergy.Normalized : 0f;
        heavyDischargeCommitted = true;
        bool hasDischarge = activeHeavyEnergy != null && activeHeavyEnergy.TryCommitDischarge(
            activeStats.damage * activeAttackPhases[0].impact.SafeDamageMultiplier, out activeDischarge);
        if (hasDischarge && heavyParried) activeDischarge.TryRefundParried();

        MeleeWeaponDefinition meleeDefinition = activeWeaponData != null
            ? activeWeaponData.GetMeleeDefinition() : null;
        if (meleeDefinition == null || activeHeavyDefinition == null) return;
        AttackPatternRuntimeData pattern = activeAttackPhases[0].ResolvePattern(
            activeStats.range, activeStats.meleeSlashAngle, meleeDefinition.baseSettings.hitWidth);
        Vector3 center = transform.position + activeAttackDirection * pattern.ForwardOffset;
        if (activeWeaponData.weaponClass == WeaponClass.Greatsword)
        {
            // 화염·암흑·빛 강공은 대검 지면음 대신 자기 내려치기 소리만 낸다(2026-09-30 청음 결정).
            bool groundReplaced = hasDischarge && MeleeElementSfxService.TryPlayUpperSlam(activeDischarge, activeDischarge.LightFirstHitIndex, center);
            if (!groundReplaced)
                CombatActionSfxService.PlayGreatswordGround(normalizedEnergy, center);
        }
        if (!hasDischarge) return;
        if (activeWeaponData.weaponClass == WeaponClass.Greatsword && !MeleeElementSfxService.ReplacesGreatswordGround(activeDischarge))
            MeleeElementSfxService.TryPlayHeavyImpact(activeDischarge.Element, center);
        // 60D light: the slam is the triple's 1st hit (overcharged) or the double's 2nd hit, each with its own circle.
        bool lightHeavy = activeDischarge.Element == WeaponElement.Light;
        int lightSlamHit = lightHeavy ? activeDischarge.LightFirstHitIndex : 0;
        float slamRadius = lightHeavy ? activeDischarge.LightHitRadius(lightSlamHit) : activeDischarge.Radius;
        float slamDamage = lightHeavy ? activeDischarge.LightHitDamage(lightSlamHit) : activeDischarge.FirstBlastDamage;
        attackPhaseExecutor.OverrideUnstartedCircleRadius(slamRadius);
        heavyDischargeExecutor.Begin(activeDischarge, activeHeavyDefinition,
            combatTarget, gameObject, center, activeAttackDirection, slamRadius, pattern.VerticalTolerance);
        MeleeAttackRuntimeData baseRuntime = MeleeAttackStatResolver.Resolve(activeStats,
            meleeDefinition.baseSettings, activeAttackPhases[0], 1f);
        var blastRuntime = new MeleeAttackRuntimeData(pattern, slamDamage,
            baseRuntime.Knockback, baseRuntime.HitStunDuration, baseRuntime.VfxScale, baseRuntime.AttackRangeScale);
        resolvingHeavyBlast = true;
        try
        {
            for (int i = 0; i < heavyDischargeExecutor.SnapshotCount; i++)
            {
                CombatTarget target = heavyDischargeExecutor.FirstBlastTarget(i);
                if (target == null) continue;
                Vector3 direction = target.WorldCenter - center; direction.y = 0f;
                if (direction.sqrMagnitude < .0001f) direction = activeAttackDirection;
                DealPatternDamage(new AttackPhaseHit(activeAttackPhases[0], blastRuntime, target,
                    target.WorldCenter, direction.normalized));
            }
        }
        finally { resolvingHeavyBlast = false; }
    }

    private void ResetStateIfWeaponChanged()
    {
        ItemData currentWeaponItem = playerEquipment != null ? playerEquipment.CurrentWeaponItem : null;

        if (!isAttacking)
            return;

        if (playerEquipment == null || !playerEquipment.CanCurrentWeaponUseMeleeSlash || !IsSameRuntimeItem(activeAttackWeaponItem, currentWeaponItem))
            CancelActiveAttack(WeaponActionCompletionReason.CancelledByWeaponSwitch, true);
    }

    private void CancelActiveAttack(
        WeaponActionCompletionReason completionReason,
        bool resetCombo)
    {
        ResolveFacade()?.CombatInputs?.ClearAttack();
        ResolveFacade()?.CombatInputs?.ClearHeavy();
        Vector3 committedDirection = activeAttackDirection;
        StopActiveAttackStep();
        if (resetCombo)
            ResetComboState();

        CompleteActiveAction(false, completionReason, committedDirection);
    }

    private void StopActiveAttackStep()
    {
        GetComponent<PlayerParryController>()?.CloseWindow();
        heavyDischargeExecutor.End();
        activeDischarge?.End();
        activeDischarge = null;
        activeHeavyEnergy = null;
        heavyDischargeCommitted = false;
        activeAttackIsHeavy = false;
        activeHeavyDefinition = null;
        activeAttackDamageMultiplier = 1f;
        isAttacking = false;
        ReleaseAttackStates();
        activeAttackWeaponItem = null;
        activeAttackUsesCombo = false;
        activeAttackAnimationClip = null;
        activeAttackAnimationSpeed = 1f;
        activeAttackTransitionDuration = 0f;
        activeAttackPhases = null;
        attackPhaseExecutor.Cancel();
        attackMovementExecutor.Cancel();
        attackVisualHeight.Cancel();
        attackTrailExecutor.Cancel();
        activeAttackStep = default;
        ClearAttackTrail();
        ResetActiveTrailState();
        playerController?.CancelWeaponActionLocks();

        attackPatternDebugRenderer?.Hide();
    }

    private void FinishActiveAttack()
    {
        bool keepManualCombo = activeAttackUsesCombo
            && activeActionSource == WeaponActionSource.PlayerInput
            && manualInputEnabled;
        Vector3 committedDirection = activeAttackDirection;
        if (keepManualCombo)
            lastComboWindowTime = Time.time;
        else if (activeAttackUsesCombo)
            ResetComboState();

        FinishActiveAttackStep();
        CompleteActiveAction(
            true,
            WeaponActionCompletionReason.Completed,
            committedDirection);
    }

    private void FinishActiveAttackStep()
    {
        heavyDischargeExecutor.End();
        activeDischarge?.End();
        activeDischarge = null;
        activeHeavyEnergy = null;
        heavyDischargeCommitted = false;
        activeAttackIsHeavy = false;
        activeHeavyDefinition = null;
        activeAttackDamageMultiplier = 1f;
        ReleaseAttackStates();
        activeAttackStep = default;
        activeAttackPhases = null;
        attackPhaseExecutor.Cancel();
        attackMovementExecutor.Cancel();
        attackVisualHeight.Cancel();
        attackTrailExecutor.Cancel();
        ResetActiveTrailState();
        isAttacking = false;
    }

    private void CompleteActiveAction(
        bool completed,
        WeaponActionCompletionReason completionReason,
        Vector3 committedDirection)
    {
        if (activeActionId <= 0)
            return;

        terminalActionId = activeActionId;
        terminalActionState = WeaponActionState.Terminal(
            completed,
            committedDirection,
            completionReason);
        activeActionId = 0;
        activeActionSource = default;
        activeRequestedTarget = null;
        suppressHandoffMoveCancelUntilRelease = false;
        bufferedHandoffComboContinuation = false;
    }

    private void ApplyAttackDisplacement(Vector3 displacement)
    {
        // Greatsword weak hits already spend the full advance-based knockback budget.
        // A second, damage-free body push would exceed it before the hit lands.
        if (activeAttackIsHeavy || activeWeaponData == null || activeWeaponData.weaponClass != WeaponClass.Greatsword)
            comboMovementCollisionPusher.PushBeforeMove(playerController, combatTarget, displacement);
        playerController?.ApplyWeaponRootMotionDisplacement(displacement, !activeAttackIsHeavy);
    }

    private void RequestAttackStates()
    {
        PlayerStateCoordinator coordinator = ResolveStateCoordinator();
        if (coordinator == null)
            return;
        coordinator.RequestLocomotion(this, PlayerLocomotionState.ControlledMove);
        coordinator.RequestAction(this, PlayerActionState.Attack);
    }

    private void ReleaseAttackStates()
    {
        if (stateCoordinator == null)
            stateCoordinator = GetComponent<PlayerStateCoordinator>();
        if (stateCoordinator == null)
            stateCoordinator = PlayerStateCoordinator.Current;
        if (stateCoordinator == null)
            return;
        stateCoordinator.ReleaseLocomotion(this);
        stateCoordinator.ReleaseAction(this);
    }
}
