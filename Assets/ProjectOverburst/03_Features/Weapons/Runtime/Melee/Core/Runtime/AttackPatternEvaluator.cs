using UnityEngine;

public static class AttackPatternEvaluator
{
    public static bool TryEvaluate(
        AttackPatternRuntimeData pattern,
        AttackPatternBasis basis,
        Vector3 worldPoint,
        out float requiredProgress)
    {
        return TryEvaluate(
            pattern,
            basis,
            new CombatTargetVolume(worldPoint, 0f, 0f),
            0f,
            out requiredProgress);
    }

    public static bool TryEvaluate(
        AttackPatternRuntimeData pattern,
        AttackPatternBasis basis,
        Vector3 worldPoint,
        float rangeTolerance,
        out float requiredProgress)
    {
        return TryEvaluate(
            pattern,
            basis,
            new CombatTargetVolume(worldPoint, 0f, 0f),
            rangeTolerance,
            out requiredProgress);
    }

    public static bool TryEvaluate(
        AttackPatternRuntimeData pattern,
        AttackPatternBasis basis,
        CombatTargetVolume targetVolume,
        out float requiredProgress)
    {
        return TryEvaluate(pattern, basis, targetVolume, 0f, out requiredProgress);
    }

    public static bool TryEvaluate(
        AttackPatternRuntimeData pattern,
        AttackPatternBasis basis,
        CombatTargetVolume targetVolume,
        float rangeTolerance,
        out float requiredProgress)
    {
        requiredProgress = 0f;
        Vector3 patternOrigin = basis.GetPatternOrigin(pattern);
        Vector3 offset = targetVolume.Center - patternOrigin;

        float verticalDistance = Mathf.Max(0f, Mathf.Abs(offset.y) - targetVolume.HalfHeight);
        if (verticalDistance > pattern.VerticalTolerance)
            return false;

        float localX = Vector3.Dot(offset, basis.Right);
        float localZ = Vector3.Dot(offset, basis.Forward);
        float planarDistance = Mathf.Sqrt(localX * localX + localZ * localZ);
        float safeRange = pattern.Range + Mathf.Max(0f, rangeTolerance);
        float angularRadius = ResolveAngularRadius(planarDistance, targetVolume.Radius);

        if (!IsInsideShape(
                pattern,
                localX,
                localZ,
                planarDistance,
                safeRange,
                targetVolume.Radius,
                angularRadius,
                out float signedAngle))
        {
            return false;
        }

        requiredProgress = ResolveRequiredProgress(
            pattern,
            localZ,
            planarDistance,
            signedAngle,
            targetVolume.Radius,
            angularRadius);
        return true;
    }

    public static float ResolveBroadphaseRadius(AttackPatternRuntimeData pattern)
    {
        if (pattern.Shape != AttackAreaShape.Rectangle)
            return pattern.Range;

        float halfWidth = pattern.Width * 0.5f;
        return Mathf.Sqrt(pattern.Range * pattern.Range + halfWidth * halfWidth);
    }

    public static float ResolveLinearProgress(
        AttackPatternRuntimeData pattern,
        float localZ,
        float radius = 0f)
    {
        return Mathf.Clamp01(
            (localZ - Mathf.Max(0f, radius)) / Mathf.Max(0.0001f, pattern.Range));
    }

    public static float ResolveAngularProgress(
        AttackPatternRuntimeData pattern,
        float signedAngle,
        float angularRadius = 0f)
    {
        if (pattern.Shape == AttackAreaShape.Circle)
        {
            float directionSign = pattern.Direction == AttackFillDirection.RightToLeft ? -1f : 1f;
            float directedDelta = Mathf.Repeat(
                (signedAngle - pattern.AngleOffset) * directionSign,
                360f);

            if (directedDelta <= angularRadius || directedDelta >= 360f - angularRadius)
                return 0f;

            return Mathf.Clamp01((directedDelta - angularRadius) / 360f);
        }

        float relativeAngle = Mathf.DeltaAngle(pattern.AngleOffset, signedAngle);
        float halfAngle = pattern.Angle * 0.5f;
        if (pattern.Direction == AttackFillDirection.RightToLeft)
        {
            return Mathf.Clamp01(
                (halfAngle - relativeAngle - angularRadius) / Mathf.Max(0.0001f, pattern.Angle));
        }

        return Mathf.Clamp01(
            (relativeAngle + halfAngle - angularRadius) / Mathf.Max(0.0001f, pattern.Angle));
    }

    private static bool IsInsideShape(
        AttackPatternRuntimeData pattern,
        float localX,
        float localZ,
        float planarDistance,
        float safeRange,
        float targetRadius,
        float angularRadius,
        out float signedAngle)
    {
        signedAngle = Mathf.Atan2(localX, localZ) * Mathf.Rad2Deg;

        switch (pattern.Shape)
        {
            case AttackAreaShape.Circle:
                return planarDistance <= safeRange + targetRadius;

            case AttackAreaShape.Rectangle:
                return localZ + targetRadius >= 0f
                    && localZ - targetRadius <= safeRange
                    && Mathf.Abs(localX) <= pattern.Width * 0.5f + targetRadius;

            default:
                if (planarDistance > safeRange + targetRadius)
                    return false;

                if (planarDistance <= targetRadius)
                    return true;

                float relativeAngle = Mathf.DeltaAngle(pattern.AngleOffset, signedAngle);
                return Mathf.Abs(relativeAngle) <= pattern.Angle * 0.5f + angularRadius;
        }
    }

    private static float ResolveRequiredProgress(
        AttackPatternRuntimeData pattern,
        float localZ,
        float planarDistance,
        float signedAngle,
        float targetRadius,
        float angularRadius)
    {
        switch (pattern.FillMode)
        {
            case AttackFillMode.LinearFill:
                return ResolveLinearProgress(pattern, localZ, targetRadius);

            case AttackFillMode.RadialExpand:
                return Mathf.Clamp01(
                    (planarDistance - targetRadius) / Mathf.Max(0.0001f, pattern.Range));

            default:
                return ResolveAngularProgress(pattern, signedAngle, angularRadius);
        }
    }

    private static float ResolveAngularRadius(float planarDistance, float targetRadius)
    {
        if (targetRadius <= 0f)
            return 0f;

        if (planarDistance <= targetRadius)
            return 180f;

        float ratio = Mathf.Clamp01(targetRadius / planarDistance);
        return Mathf.Asin(ratio) * Mathf.Rad2Deg;
    }
}
