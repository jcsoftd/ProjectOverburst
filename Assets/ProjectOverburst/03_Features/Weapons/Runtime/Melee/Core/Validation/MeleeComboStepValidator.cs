using System;

public static class MeleeComboStepValidator
{
    public static bool TryValidate(MeleeComboStepData step, out string error)
    {
        if (string.IsNullOrWhiteSpace(step.attackId))
        {
            error = "Attack ID is missing.";
            return false;
        }

        if (step.animationClip == null)
        {
            error = "Animation clip is missing for " + step.attackId + ".";
            return false;
        }

        if (!IsNormalizedWindowValid(
                step.comboInputWindow.startNormalizedTime,
                step.comboInputWindow.endNormalizedTime))
        {
            error = "Combo input window is invalid for " + step.attackId + ".";
            return false;
        }

        if (!TryValidateMovementPhases(step.movementPhases, out error)
            || !TryValidateTrailPhases(step.trailPhases, out error)
            || !AttackPhaseValidator.TryValidate(step.attackPhases, out error))
        {
            error = step.attackId + ": " + error;
            return false;
        }

        error = null;
        return true;
    }

    public static bool TryValidateMovementPhases(AttackMovementPhaseData[] phases, out string error)
    {
        if (phases == null || phases.Length == 0)
        {
            error = null;
            return true;
        }

        float previousEnd = -1f;
        for (int i = 0; i < phases.Length; i++)
        {
            AttackMovementPhaseData phase = phases[i];
            if (!IsNormalizedWindowValid(phase.startNormalizedTime, phase.endNormalizedTime))
            {
                error = "Movement phase " + i + " is invalid.";
                return false;
            }

            if (phase.movementMode == AttackMovementMode.ForwardDistance
                && (phase.distance < 0f || !IsProgressCurveValid(phase.progressCurve)))
            {
                error = "Movement phase " + i + " has a non-monotonic progress curve.";
                return false;
            }

            if (phase.movementMode == AttackMovementMode.SignedForwardTrajectory
                && !AreTrajectoryCurvesValid(phase))
            {
                error = "Movement phase " + i + " has invalid local root trajectory curves.";
                return false;
            }

            if (phase.SafeStart < previousEnd - 0.0001f)
            {
                error = "Movement phases overlap or are not ordered at index " + i + ".";
                return false;
            }

            previousEnd = phase.SafeEnd;
        }

        error = null;
        return true;
    }

    private static bool IsProgressCurveValid(UnityEngine.AnimationCurve curve)
    {
        if (curve == null)
            return true;

        const int sampleCount = 32;
        float previous = curve.Evaluate(0f);
        if (System.Math.Abs(previous) > 0.001f)
            return false;

        for (int i = 1; i <= sampleCount; i++)
        {
            float value = curve.Evaluate(i / (float)sampleCount);
            if (value < previous - 0.0001f || value < -0.0001f || value > 1.0001f)
                return false;

            previous = value;
        }

        return System.Math.Abs(previous - 1f) <= 0.001f;
    }

    private static bool AreTrajectoryCurvesValid(AttackMovementPhaseData phase)
    {
        if (phase.localForwardCurve == null)
            return false;

        return IsFiniteDisplacementCurve(phase.localForwardCurve);
    }

    private static bool IsFiniteDisplacementCurve(UnityEngine.AnimationCurve curve)
    {
        if (curve == null)
            return true;

        const int sampleCount = 32;
        if (System.Math.Abs(curve.Evaluate(0f)) > 0.001f)
            return false;

        for (int i = 0; i <= sampleCount; i++)
        {
            float value = curve.Evaluate(i / (float)sampleCount);
            if (float.IsNaN(value) || float.IsInfinity(value))
                return false;
        }

        return true;
    }

    public static bool TryValidateTrailPhases(AttackTrailPhaseData[] phases, out string error)
    {
        if (phases == null || phases.Length == 0)
        {
            error = null;
            return true;
        }

        float previousEnd = -1f;
        for (int i = 0; i < phases.Length; i++)
        {
            AttackTrailPhaseData phase = phases[i];
            if (!IsNormalizedWindowValid(phase.startNormalizedTime, phase.endNormalizedTime))
            {
                error = "Trail phase " + i + " is invalid.";
                return false;
            }

            if (phase.SafeStart < previousEnd - 0.0001f)
            {
                error = "Trail phases overlap or are not ordered at index " + i + ".";
                return false;
            }

            previousEnd = phase.SafeEnd;
        }

        error = null;
        return true;
    }

    private static bool IsNormalizedWindowValid(float start, float end)
    {
        return start >= 0f && start <= 1f
            && end >= 0f && end <= 1f
            && end > start;
    }
}
