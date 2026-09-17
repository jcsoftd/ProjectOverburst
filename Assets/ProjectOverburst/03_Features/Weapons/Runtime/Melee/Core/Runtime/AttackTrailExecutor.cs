using System;
using UnityEngine;

public sealed class AttackTrailExecutor
{
    private AttackTrailPhaseData[] phases;
    private int activePhaseIndex = -1;
    private Action beginTrail;
    private Action endTrail;

    public bool Begin(AttackTrailPhaseData[] trailPhases, Action beginCallback, Action endCallback)
    {
        Cancel();

        if (trailPhases == null || trailPhases.Length == 0)
            return true;

        if (!MeleeComboStepValidator.TryValidateTrailPhases(trailPhases, out string error))
        {
            Debug.LogError("[AttackTrailExecutor] " + error);
            return false;
        }

        phases = trailPhases;
        beginTrail = beginCallback;
        endTrail = endCallback;
        return beginTrail != null && endTrail != null;
    }

    public void Tick(float attackNormalizedTime)
    {
        if (phases == null)
            return;

        float normalizedTime = Mathf.Clamp01(attackNormalizedTime);
        int nextPhaseIndex = ResolveActivePhaseIndex(normalizedTime);
        if (nextPhaseIndex == activePhaseIndex)
            return;

        if (activePhaseIndex >= 0)
            endTrail?.Invoke();

        activePhaseIndex = nextPhaseIndex;
        if (activePhaseIndex >= 0)
            beginTrail?.Invoke();
    }

    public void Cancel()
    {
        if (activePhaseIndex >= 0)
            endTrail?.Invoke();

        phases = null;
        activePhaseIndex = -1;
        beginTrail = null;
        endTrail = null;
    }

    private int ResolveActivePhaseIndex(float normalizedTime)
    {
        for (int i = 0; i < phases.Length; i++)
        {
            if (normalizedTime >= phases[i].SafeStart && normalizedTime < phases[i].SafeEnd)
                return i;
        }

        return -1;
    }
}
