public static class AttackPhaseValidator
{
    private const float TimeEpsilon = 0.0001f;

    public static bool TryValidate(AttackPhaseData[] phases, out string error)
    {
        if (phases == null || phases.Length == 0)
        {
            error = "At least one AttackPhase is required.";
            return false;
        }

        for (int i = 0; i < phases.Length; i++)
        {
            AttackPhaseData phase = phases[i];
            if (phase.attackPattern == null)
            {
                error = "Every AttackPhase must reference an attack pattern.";
                return false;
            }

            if (!IsProgressSourceCompatible(phase, out error))
                return false;

            AttackGeometryData geometry = phase.geometry;
            if (geometry.rangeMultiplier <= 0f
                || geometry.angleMultiplier <= 0f
                || geometry.widthMultiplier <= 0f
                || geometry.vfxScaleMultiplier <= 0f)
            {
                error = "AttackPhase geometry multipliers must be positive.";
                return false;
            }

            if (phase.startNormalizedTime < 0f
                || phase.startNormalizedTime > 1f
                || phase.endNormalizedTime < 0f
                || phase.endNormalizedTime > 1f
                || phase.endNormalizedTime <= phase.startNormalizedTime + TimeEpsilon)
            {
                error = "Every AttackPhase must have a valid normalized time range.";
                return false;
            }

            AttackImpactData impact = phase.impact;
            if (impact.damageMultiplier <= 0f
                || impact.knockbackMultiplier < 0f
                || impact.hitStunMultiplier < 0f
                || impact.knockbackReactionDuration < 0f
                || impact.airborneImpulse < 0f
                || impact.airborneStunDuration < 0f)
            {
                error = "AttackImpact damageMultiplier must be positive and other values cannot be negative.";
                return false;
            }
        }

        for (int leftIndex = 0; leftIndex < phases.Length - 1; leftIndex++)
        {
            AttackPhaseData left = phases[leftIndex];
            for (int rightIndex = leftIndex + 1; rightIndex < phases.Length; rightIndex++)
            {
                AttackPhaseData right = phases[rightIndex];
                float overlapStart = left.startNormalizedTime > right.startNormalizedTime
                    ? left.startNormalizedTime
                    : right.startNormalizedTime;
                float overlapEnd = left.endNormalizedTime < right.endNormalizedTime
                    ? left.endNormalizedTime
                    : right.endNormalizedTime;
                if (overlapEnd - overlapStart > TimeEpsilon)
                {
                    error = "Overlapping AttackPhases are not supported.";
                    return false;
                }
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool IsProgressSourceCompatible(AttackPhaseData phase, out string error)
    {
        AttackPatternDefinition pattern = phase.attackPattern;
        switch (phase.progressSource)
        {
            case AttackProgressSource.NormalizedTime:
                error = string.Empty;
                return true;

            case AttackProgressSource.WeaponTipAngularTravel:
                if (pattern.fillMode == AttackFillMode.AngularSweep
                    && pattern.shape == AttackAreaShape.Circle)
                {
                    error = string.Empty;
                    return true;
                }

                error = "WeaponTipAngularTravel requires an AngularSweep Circle pattern.";
                return false;

            case AttackProgressSource.WeaponTipSectorAngle:
                if (pattern.fillMode == AttackFillMode.AngularSweep
                    && pattern.shape == AttackAreaShape.Sector)
                {
                    error = string.Empty;
                    return true;
                }

                error = "WeaponTipSectorAngle requires an AngularSweep Sector pattern.";
                return false;

            case AttackProgressSource.WeaponTipForward:
                if (pattern.fillMode == AttackFillMode.LinearFill
                    && pattern.shape == AttackAreaShape.Rectangle)
                {
                    error = string.Empty;
                    return true;
                }

                error = "WeaponTipForward requires a LinearFill Rectangle pattern.";
                return false;

            default:
                error = "AttackPhase has an unsupported progress source.";
                return false;
        }
    }
}
