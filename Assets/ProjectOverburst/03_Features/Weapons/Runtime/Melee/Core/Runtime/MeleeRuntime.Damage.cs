using UnityEngine;

// MeleeRuntime partial: 패턴 피해, 넉백·띄우기, 치명타, 지속 피해, 흡혈. 필드와 Unity 수명주기는 MeleeRuntime.cs에 있다.
public partial class MeleeRuntime
{
    // A committed heavy retains its sound strength after consumption/refund and during its remaining trails.
    public float ElementSfxEnergy => activeAttackIsHeavy && activeDischarge != null
        ? activeDischarge.Energy : GetComponent<OverburstElementEnergy>()?.Amount ?? 0f;

    private void DealPatternDamage(AttackPhaseHit hit) => DealPatternDamage(hit, 0);

    private void DealPatternDamage(AttackPhaseHit hit, int darkBarrageId)
    {
        // The elemental first circle commits once at impact. The phase executor still drives its visual wave.
        if (activeAttackIsHeavy && activeDischarge != null && !resolvingHeavyBlast) return;
        if (hit.Damageable == null)
            return;

        OverburstElementDischarge.TargetSnapshot dischargeTarget = default;
        bool hasDischargeTarget = activeDischarge != null
            && hit.TargetHealth != null
            && activeDischarge.TryCaptureTarget(hit.TargetHealth, out dischargeTarget);

        AttackPhaseData phase = hit.Phase;
        AttackImpactData impact = phase.impact;
        MeleeAttackRuntimeData runtimeData = hit.RuntimeData;
        // 2026-10-01: 방출 없는 강공(에너지 0)은 원소 타격 VFX·소리·원소 피해 보너스 없이 일반 타격으로 친다.
        WeaponElement attackElement = activeAttackIsHeavy && activeDischarge == null
            ? WeaponElement.None : ResolveActiveAttackElement();
        bool useElementHitVfx = MeleeElementHitVfxService.CanPlay(attackElement)
            || (hit.TargetHealth != null && hit.TargetHealth.GetComponent<EnemyDeathPresentation>() != null);
        int hitActionId = activeActionId;
        var hitDischarge = activeDischarge;
        int darkDonorId = hasDischargeTarget ? hit.TargetHealth.GetInstanceID() : 0;
        float elementSfxEnergy = ElementSfxEnergy; // Capture before confirmed damage charges a weak hit.
        MeleeDamageResult result = MeleeDamageResolver.Apply(new MeleeDamageRequest(
            hit.Damageable,
            runtimeData.Damage,
            impact,
            runtimeData.HitStunDuration,
            GetActiveCritChance(),
            activeStats.critDamageMultiplier,
            hit.HitPoint,
            gameObject,
            hit.Direction,
            activeAttackIsHeavy && activeDischarge != null && activeDischarge.Element == WeaponElement.Dark
                ? 0f : ApplyCombatStanceKnockback(runtimeData.Knockback),
            useElementHitVfx,
            attackElement,
            activeAttackWeaponItem != null ? activeAttackWeaponItem.runtimeInstanceId : string.Empty,
            activeHitFeedbackSequenceId,
            activeAttackIsHeavy ? PlayerAttackKind.Heavy : PlayerAttackKind.Weak,
            hit.PhaseIndex,
            ResolveWeakKnockbackDistance(phase), activeGemAttack));

        // Ammo belongs to confirmed slam hits, including lethal hits, before a reward can clear the action.
        if (darkBarrageId != 0 && hasDischargeTarget && result.ActualDamage > 0f)
            DarkBarrageScheduler.ConfirmSlamHit(darkBarrageId, darkDonorId, dischargeTarget.Stacks,
                dischargeTarget.Status, dischargeTarget.Life, result.TargetHealth != null && result.TargetHealth.IsDead);

        // Lethal-hit rewards may synchronously restore the account projection and end this action.
        // Its damage has committed, but its cleared discharge/feedback state must not be reused.
        if (activeActionId != hitActionId || activeDischarge != hitDischarge) return;

        if (hasDischargeTarget && result.ActualDamage > 0f
            && activeDischarge.TryResolveConfirmedHit(
                dischargeTarget, result.ActualDamage, out OverburstDischargeResult dischargeResult))
            heavyDischargeExecutor.ResolveDirectHit(hit.TargetHealth, hit.HitPoint, dischargeResult);

        ApplyAirborne(
            result.TargetHealth,
            result.ActualDamage,
            impact.airborneImpulse,
            impact.airborneStunDuration);

        if (result.ActualDamage > 0f
            && impact.triggersOnHitEffects)
        {
            CombatHitFeedbackService.Request(new CombatHitFeedbackRequest(
                this,
                activeHitFeedbackSequenceId,
                impact.hitFeedbackProfile,
                result.IsCritical,
                attackElement,
                hit.HitPoint,
                playerActorRuntime != null && PlayerContext.GetOrCreate()?.CurrentActor == playerActorRuntime,
                CombatCameraRequestKind.AttackHit,
                hit.Direction,
                impact.hitFeedbackProfile != null ? impact.hitFeedbackProfile.CameraPriority : 1f,
                isLethal: result.TargetHealth != null && result.TargetHealth.IsDead,
                phaseIndex: hit.PhaseIndex,
                target: result.TargetHealth,
                impactShape: runtimeData.Pattern.IsThrust ? CombatImpactShape.Thrust
                    : phase.vfxSwingSettings.orientation == AttackVfxSwingOrientation.Vertical
                        ? CombatImpactShape.Downward : CombatImpactShape.Sweep,
                impactDirection: runtimeData.Pattern.IsThrust || phase.vfxSwingSettings.orientation == AttackVfxSwingOrientation.Vertical
                    ? hit.Direction : Vector3.Cross(Vector3.up, hit.Direction)
                        * (phase.vfxSwingSettings.reverseDirection ? -1f : 1f),
                elementSfxEnergy: elementSfxEnergy));
        }

        if (impact.triggersOnHitEffects)
            ApplyOnHitEffects(result.TargetHealth, result.ActualDamage, hit.Direction);
    }

    private void ApplyAirborne(
        CombatHealth targetHealth,
        float actualDamage,
        float airborneImpulse,
        float airborneStunDuration)
    {
        if (targetHealth == null || targetHealth.IsDead || actualDamage <= 0f || airborneImpulse <= 0f)
            return;

        Rigidbody targetBody = targetHealth.GetComponentInParent<Rigidbody>();
        if (targetBody != null && !targetBody.isKinematic)
            targetBody.AddForce(Vector3.up * airborneImpulse, ForceMode.Impulse);

        EnemyMovementReaction movementReaction = targetHealth.GetComponentInParent<EnemyMovementReaction>();
        if (movementReaction != null)
            movementReaction.ExtendKnockbackReaction(airborneStunDuration > 0f ? airborneStunDuration : 0.35f);
    }

    private float ResolveWeakKnockbackDistance(AttackPhaseData phase)
    {
        if (activeAttackIsHeavy || activeWeaponData == null || activeWeaponData.weaponClass != WeaponClass.Greatsword)
            return -1f;
        float advance = 0f;
        if (activeAttackStep.movementPhases != null)
            foreach (var move in activeAttackStep.movementPhases) advance += Mathf.Max(0f, move.distance);
        float phaseWeight = 0f;
        foreach (var candidate in activeAttackPhases) phaseWeight += candidate.impact.SafeDamageMultiplier;
        return advance * AttackMovementExecutor.ComboMovementDistanceMultiplier
            * Mathf.Min(1f, .9f * Mathf.Max(0f, activeWeaponData.baseStats.knockback) / 4f)
            * phase.impact.SafeDamageMultiplier / Mathf.Max(.001f, phaseWeight);
    }

    private float GetActiveCritChance()
    {
        // 전투 자세 치명 보너스 포함, 최종 상한 65%.
        return CombatBalanceFormulas.EffectiveCriticalChance(activeStats.critChance, activeAttackUsedMeleeCombatStance);
    }

    private float ApplyCombatStanceKnockback(float knockback)
    {
        float multiplier = activeAttackUsedMeleeCombatStance ? MeleeCombatStanceKnockbackMultiplier : 1f; // 자세 배율
        return knockback * multiplier;
    }

    private void ApplyOnHitEffects(CombatHealth targetHealth, float actualDamage, Vector3 direction)
    {
        if (actualDamage <= 0f)
            return;

        ApplyDamageOverTime(targetHealth, actualDamage, direction);
        HealSourceOnHit(actualDamage);
    }

    private void ApplyDamageOverTime(CombatHealth targetHealth, float actualDamage, Vector3 direction)
    {
        if (targetHealth == null || targetHealth.IsDead)
            return;

        float damagePerTick = activeStats.dotDamageFlat + actualDamage * activeStats.dotDamagePercent / 100f; // DoT 피해
        if (damagePerTick <= 0f)
            return;

        targetHealth.ApplyDamageOverTime(damagePerTick, activeStats.duration, DamageOverTimeTickInterval, gameObject, direction);
    }

    private void HealSourceOnHit(float actualDamage)
    {
        float healAmount = activeStats.onHitHeal + actualDamage * activeStats.lifeStealPercent / 100f; // 회복량
        if (healAmount <= 0f)
            return;

        CombatHealth sourceHealth = GetComponentInParent<CombatHealth>();
        if (sourceHealth == null)
            return;

        sourceHealth.Heal(healAmount);
    }
}
