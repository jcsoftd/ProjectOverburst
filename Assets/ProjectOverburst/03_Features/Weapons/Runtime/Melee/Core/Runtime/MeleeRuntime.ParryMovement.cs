using UnityEngine;

public partial class MeleeRuntime
{
    public const float ParryCounterMaximumTravel = 2f;
    public const float ParryCounterMinimumSeparation = 1f;
    // This counter's jump peaks at source frame18 and its landing impact begins at frame25 (30fps).
    public const float ParryCounterSlamLeadSeconds = 7f / 30f;
    private bool heavyParryCounterMovement, heavyParryCounterTravelStopped;
    private EnemyActor heavyParryCounterTarget;
    private CombatTarget heavyParryCounterBody;
    private uint heavyParryCounterLease;
    private float heavyParryCounterRemainingTravel;
    private Vector3 heavyParryCounterDirection;

    public static float ParryCounterTravelDistance(float distance) =>
        Mathf.Clamp(distance - ParryCounterMinimumSeparation, 0f, ParryCounterMaximumTravel);

    public static AttackMovementPhaseData[] CreateParryCounterMovementPhases(
        MeleeComboStepData comboThird, MeleeComboStepData counter, int slamIndex, float totalDistance)
    {
        if (counter.animationClip == null || counter.attackPhases == null || slamIndex < 0
            || slamIndex >= counter.attackPhases.Length || comboThird.movementPhases == null)
            return System.Array.Empty<AttackMovementPhaseData>();
        float comboDistance = 0f;
        AnimationCurve comboCurve = null;
        foreach (var phase in comboThird.movementPhases)
        {
            comboDistance += Mathf.Max(0f, phase.EvaluateLocalDisplacement(1f).z) * AttackMovementExecutor.ComboMovementDistanceMultiplier;
            if (comboCurve == null) comboCurve = phase.progressCurve;
        }
        float total = Mathf.Clamp(totalDistance, 0f, ParryCounterMaximumTravel);
        float spinDistance = Mathf.Min(total, comboDistance);
        float slamDistance = Mathf.Max(0f, total - spinDistance);
        float impact = counter.attackPhases[slamIndex].SafeStart;
        float start = counter.continuationStartNormalizedTime;
        float descent = Mathf.Max(start + .0001f, impact - ParryCounterSlamLeadSeconds / counter.animationClip.length);
        AnimationCurve Copy(AnimationCurve curve) => curve == null ? AnimationCurve.EaseInOut(0f, 0f, 1f, 1f)
            : new AnimationCurve(curve.keys) { preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode };
        return new[] {
            new AttackMovementPhaseData { startNormalizedTime = start, endNormalizedTime = descent,
                useAuthoredTiming = true, movementMode = AttackMovementMode.ForwardDistance,
                distance = spinDistance / AttackMovementExecutor.ComboMovementDistanceMultiplier, progressCurve = Copy(comboCurve) },
            new AttackMovementPhaseData { startNormalizedTime = descent, endNormalizedTime = impact,
                useAuthoredTiming = true, movementMode = AttackMovementMode.ForwardDistance,
                distance = slamDistance / AttackMovementExecutor.ComboMovementDistanceMultiplier,
                progressCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f) }
        };
    }

    private void SetHeavyParryCounterTarget(EnemyActor target)
    {
        // NotifyHeavyParried accepts only the first success for this action, so later contacts cannot retarget it.
        heavyParryCounterTarget = target;
        heavyParryCounterLease = target != null ? target.LeaseVersion : 0;
        heavyParryCounterBody = target != null ? target.GetComponent<CombatTarget>() : null;
    }

    private bool HasHeavyParryCounterTarget => heavyParryCounterTarget != null && heavyParryCounterTarget.IsLeased
        && heavyParryCounterTarget.LeaseVersion == heavyParryCounterLease
        && heavyParryCounterBody != null && heavyParryCounterBody.IsAlive;

    private bool BeginHeavyParryCounterMovement()
    {
        attackMovementExecutor.Cancel();
        heavyParryCounterMovement = true;
        heavyParryCounterTravelStopped = true;
        heavyParryCounterRemainingTravel = 0f;
        if (!HasHeavyParryCounterTarget || combatTarget == null) return true;
        CombatTargetVolume owner = combatTarget.CurrentVolume, target = heavyParryCounterBody.CurrentVolume;
        Vector3 toTarget = target.Center - owner.Center; toTarget.y = 0f;
        if (toTarget.sqrMagnitude <= .000001f) return true;
        heavyParryCounterDirection = toTarget.normalized;
        activeAttackDirection = heavyParryCounterDirection;
        RotateOwnerToAttackDirection();
        // Rebind the not-yet-ticked counter phases to the direction chosen when the counter begins.
        attackPhaseExecutor.Cancel();
        if (!TryBeginAttackPhases())
        { CancelActiveAttack(WeaponActionCompletionReason.InvalidConfiguration, true); return false; }
        heavyParryCounterRemainingTravel = ParryCounterTravelDistance(toTarget.magnitude);
        if (heavyParryCounterRemainingTravel <= .0001f) return true;
        heavyParryCounterTravelStopped = false;
        var combo = activeWeaponData.GetMeleeDefinition()?.comboDefinition;
        if (combo == null || combo.StepCount < 3 || activeHeavyDefinition == null)
        { CancelActiveAttack(WeaponActionCompletionReason.InvalidConfiguration, true); return false; }
        var phases = CreateParryCounterMovementPhases(combo.steps[2], activeAttackStep,
            activeHeavyDefinition.SafeDischargePhaseIndex, heavyParryCounterRemainingTravel);
        if (attackMovementExecutor.Begin(phases, heavyParryCounterDirection, ApplyAttackDisplacement, heavyParrySavedProgress)) return true;
        CancelActiveAttack(WeaponActionCompletionReason.InvalidConfiguration, true); return false;
    }

    private void ApplyHeavyParryCounterDisplacement(Vector3 displacement)
    {
        if (heavyParryCounterTravelStopped || !HasHeavyParryCounterTarget || combatTarget == null)
        { heavyParryCounterTravelStopped = true; return; }
        CombatTargetVolume owner = combatTarget.CurrentVolume, target = heavyParryCounterBody.CurrentVolume;
        Vector3 toTarget = target.Center - owner.Center; toTarget.y = 0f;
        // The fixed approach direction cannot follow the monster through or around the player.
        float clearance = Vector3.Dot(toTarget, heavyParryCounterDirection) - ParryCounterMinimumSeparation;
        // A closer target reduces this action's budget permanently; later knockback cannot grow it again.
        heavyParryCounterRemainingTravel = Mathf.Min(heavyParryCounterRemainingTravel, Mathf.Max(0f, clearance));
        float requested = Mathf.Max(0f, Vector3.Dot(displacement, heavyParryCounterDirection));
        float allowed = Mathf.Min(requested, heavyParryCounterRemainingTravel, Mathf.Max(0f, clearance));
        if (allowed <= .0001f)
        { if (clearance <= .0001f || heavyParryCounterRemainingTravel <= .0001f) heavyParryCounterTravelStopped = true; return; }
        Vector3 before = transform.position;
        // Counter approach does not spend an extra collision-push budget on the parried monster.
        playerController?.ApplyWeaponRootMotionDisplacement(heavyParryCounterDirection * allowed, false);
        float applied = Mathf.Max(0f, Vector3.Dot(transform.position - before, heavyParryCounterDirection));
        heavyParryCounterRemainingTravel = Mathf.Max(0f, heavyParryCounterRemainingTravel - applied);
        if (applied + .001f < allowed || allowed + .0001f < requested || heavyParryCounterRemainingTravel <= .0001f)
            heavyParryCounterTravelStopped = true;
    }

    private void ResetHeavyParryMovement()
    {
        heavyParryCounterMovement = heavyParryCounterTravelStopped = false;
        heavyParryCounterTarget = null; heavyParryCounterBody = null; heavyParryCounterLease = 0;
        heavyParryCounterRemainingTravel = 0f; heavyParryCounterDirection = Vector3.zero;
    }
}
