using UnityEngine;

// MeleeRuntime partial: 공격 갱신, 강공 원소 방출 확정, 취소·종료, 공격 상태 요청. 필드와 Unity 수명주기는 MeleeRuntime.cs에 있다.
public partial class MeleeRuntime
{
    private void UpdateAttack()
    {
        if (!isAttacking)
            return;

        if (TickHeavyParryMotion())
        {
            return;
        }

        float normalizedTime = GetAttackNormalizedTime();
        TickDashHeavyTravelAndFocus(normalizedTime);
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
        if (activeDodgeFollowUp != PlayerDodgeFollowUpKind.Heavy
            || normalizedTime >= activeAttackPhases[0].SafeEnd)
            heavyDischargeExecutor.ResolvePendingArea();
        CompleteDashHeavyWave(normalizedTime);
        heavyDischargeExecutor.Tick(Time.deltaTime);

        if (shouldContinueCombo)
        {
            ContinueActiveCombo();
            return;
        }

        // Finish the wave and committed element work before accepting recovery input.
        if (TryContinueAfterDashHeavyRecovery(normalizedTime))
            return;

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
            || activeAttackPhases.Length == 0)
            return;
        int impactPhaseIndex = activeHeavyDefinition != null ? activeHeavyDefinition.SafeDischargePhaseIndex : 0;
        AttackPhaseData impactPhase = activeAttackPhases[impactPhaseIndex];
        if (normalizedTime < impactPhase.SafeStart) return;

        if (activeDodgeFollowUp == PlayerDodgeFollowUpKind.Heavy)
        { CommitDashHeavyDischarge(impactPhase); return; }

        float normalizedEnergy = activeHeavyEnergy != null ? activeHeavyEnergy.Normalized : 0f;
        heavyDischargeCommitted = true;
        bool hasDischarge = activeHeavyEnergy != null && activeHeavyEnergy.TryCommitDischarge(
            activeStats.damage * impactPhase.impact.SafeDamageMultiplier, out activeDischarge, activeGemAttack);
        if (hasDischarge && heavyParried) activeDischarge.TryRefundParried();

        MeleeWeaponDefinition meleeDefinition = activeWeaponData != null
            ? activeWeaponData.GetMeleeDefinition() : null;
        if (meleeDefinition == null || activeHeavyDefinition == null) return;
        AttackPatternRuntimeData pattern = impactPhase.ResolvePattern(
            activeStats.range, activeStats.meleeSlashAngle, meleeDefinition.baseSettings.hitWidth);
        Vector3 center = transform.position + activeAttackDirection * pattern.ForwardOffset;
        if (activeWeaponData.weaponClass == WeaponClass.Greatsword)
        {
            // 기본 지면음은 모든 원소·에너지에서 100%, 패링 성공은 같은 소리를 50% 추가한다.
            CombatActionSfxService.PlayGreatswordGround(normalizedEnergy, center, heavyParried);
            // 불·어둠·빛의 원소 착지음은 기본음 위에 겹치며, 방출 에너지로만 볼륨을 조절한다.
            if (hasDischarge)
                MeleeElementSfxService.TryPlayUpperSlam(activeDischarge, activeDischarge.LightFirstHitIndex, center, heavyParried);
        }
        if (!hasDischarge)
        {
            // 2026-10-01: 원소 무기의 에너지 0 강공은 방출 없이 일반 강공으로 치되, 판정 원은 방출 최소 반경(1.5m) 그대로 둔다.
            if (OverburstElementRules.IsActive(ResolveActiveAttackElement()))
                attackPhaseExecutor.OverrideUnstartedCircleRadius(
                    CombatBalanceFormulas.DischargeRadius(OverburstElementTuning.Current, 0f));
            return;
        }
        if (activeWeaponData.weaponClass == WeaponClass.Greatsword && !MeleeElementSfxService.UsesUpperSlamCue(activeDischarge))
            MeleeElementSfxService.TryPlayHeavyImpact(activeDischarge.Element, center, heavyParried, activeDischarge.Energy);
        // 60D light: the slam is the triple's 1st hit (overcharged) or the double's 2nd hit, each with its own circle.
        bool lightHeavy = activeDischarge.Element == WeaponElement.Light;
        int lightSlamHit = lightHeavy ? activeDischarge.LightFirstHitIndex : 0;
        float slamRadius = lightHeavy ? activeDischarge.LightHitRadius(lightSlamHit) : activeDischarge.Radius;
        float slamDamage = lightHeavy ? activeDischarge.LightHitDamage(lightSlamHit) : activeDischarge.FirstBlastDamage;
        attackPhaseExecutor.OverrideUnstartedCircleRadius(slamRadius);
        heavyDischargeExecutor.Begin(activeDischarge, activeHeavyDefinition,
            combatTarget, gameObject, center, activeAttackDirection, slamRadius, pattern.VerticalTolerance);
        MeleeAttackRuntimeData baseRuntime = MeleeAttackStatResolver.Resolve(activeStats,
            meleeDefinition.baseSettings, impactPhase, 1f);
        var blastRuntime = new MeleeAttackRuntimeData(pattern, slamDamage,
            baseRuntime.Knockback, baseRuntime.HitStunDuration, baseRuntime.VfxScale, baseRuntime.AttackRangeScale);
        int darkBarrageId = activeDischarge.Element == WeaponElement.Dark
            ? DarkBarrageScheduler.PrepareSlam(activeDischarge, gameObject, combatTarget.Team, center,
                slamRadius, pattern.VerticalTolerance, activeHeavyDefinition.elementVfx) : 0;
        resolvingHeavyBlast = true;
        try
        {
            for (int i = 0; i < heavyDischargeExecutor.SnapshotCount; i++)
            {
                CombatTarget target = heavyDischargeExecutor.FirstBlastTarget(i);
                if (target == null) continue;
                Vector3 direction = target.WorldCenter - center; direction.y = 0f;
                if (direction.sqrMagnitude < .0001f) direction = activeAttackDirection;
                DealPatternDamage(new AttackPhaseHit(impactPhase, blastRuntime, target,
                    target.WorldCenter, direction.normalized, impactPhaseIndex), darkBarrageId);
            }
        }
        finally
        {
            resolvingHeavyBlast = false;
            DarkBarrageScheduler.CompleteSlam(darkBarrageId);
        }
    }

    private void ResetStateIfWeaponChanged()
    {
        ItemData currentWeaponItem = playerEquipment != null ? playerEquipment.CurrentWeaponItem : null;

        if (!isAttacking)
            return;

        if (playerEquipment == null || !playerEquipment.CanCurrentWeaponUseMeleeSlash || !IsSameRuntimeItem(activeAttackWeaponItem, currentWeaponItem) || !activeGemAttack.IsCurrent)
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
        EndDashHeavyPresentation();
        ResetHeavyParryMotion();
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
        activeGemAttack = default;
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
        EndDashHeavyPresentation();
        ResetHeavyParryMotion();
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
        if (heavyParryCounterMovement)
        {
            ApplyHeavyParryCounterDisplacement(displacement);
            return;
        }
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
