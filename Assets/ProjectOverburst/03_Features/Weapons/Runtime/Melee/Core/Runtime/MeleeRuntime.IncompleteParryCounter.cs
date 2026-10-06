using UnityEngine;

public partial class MeleeRuntime
{
    private bool incompleteParryCounterConfigured, incompleteParrySwingPlayed;
    private MeleeComboStepData incompleteParryCounterStep;

    private bool TryResolveIncompleteParryCounterTempo(out float speed)
    {
        speed = 1f;
        var definition = activeWeaponData != null ? activeWeaponData.GetMeleeDefinition() : null;
        var profile = definition != null ? definition.animationProfile : null;
        var counter = profile != null ? profile.incompleteHeavyParryCounterDefinition : null;
        var weak = definition != null ? definition.comboDefinition : null;
        if (counter == null || counter.StepCount != 1 || weak == null || !weak.HasSteps) return false;
        var step = counter.GetStep(0);
        var reference = weak.GetStep(0);
        if (step.animationClip == null || step.animationClip != profile.incompleteHeavyParryClip
            || step.attackPhases == null || step.attackPhases.Length != 1
            || reference.animationClip == null || reference.attackPhases == null || reference.attackPhases.Length == 0)
            return false;
        var phase = step.attackPhases[0];
        var weakPhase = reference.attackPhases[0];
        float weakSpan = reference.playbackAcceleration.ToElapsed(weakPhase.SafeEnd)
            - reference.playbackAcceleration.ToElapsed(weakPhase.SafeStart);
        float weakSeconds = reference.animationClip.length * weakSpan / CalculateComboAnimationSpeed(weak, reference);
        if (weakSeconds <= 0f || phase.SafeEnd <= phase.SafeStart) return false;
        // Match the actual weak cut's duration, including the accepted weapon's attack-speed stat.
        speed = Mathf.Max(.01f, step.animationClip.length * (phase.SafeEnd - phase.SafeStart) / weakSeconds);
        incompleteParryCounterStep = step;
        return true;
    }

    private bool BeginIncompleteParryCounter()
    {
        activeAttackStep = incompleteParryCounterStep;
        activeAttackPhases = activeAttackStep.attackPhases;
        activeAttackDamageMultiplier = CombatBalanceFormulas.AttackDamageMultiplier(activeWeaponData, null, false, false);
        ResolveAttackTrail(activeAttackStep);
        if (!TryBeginAttackPhases() || !attackTrailExecutor.Begin(activeAttackStep.trailPhases, StartAttackTrail, StopAttackTrail))
            return false;
        incompleteParryCounterConfigured = true;
        incompleteParrySwingPlayed = false;
        return true;
    }

    private void TickIncompleteParryCounter()
    {
        if (!incompleteParryCounterConfigured || playerAnimatorController == null
            || !playerAnimatorController.TryGetHeavyParryClipProgress(out float progress)) return;
        if (!incompleteParrySwingPlayed && progress >= incompleteParryCounterStep.attackPhases[0].SafeStart)
        {
            incompleteParrySwingPlayed = true;
            CombatActionSfxService.PlayGreatswordSwing(0, false, transform.position);
        }
        attackTrailExecutor.Tick(progress);
        attackPhaseExecutor.Tick(progress);
    }

    private void ResetIncompleteParryCounter()
    {
        incompleteParryCounterConfigured = incompleteParrySwingPlayed = false;
        incompleteParryCounterStep = default;
    }
}
