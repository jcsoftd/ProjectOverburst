using UnityEngine;

public readonly struct AttackPatternRuntimeData
{
    public const float ThrustWidthMultiplier = 2.5f;

    public readonly AttackAreaShape Shape;
    public readonly AttackFillMode FillMode;
    public readonly AttackFillDirection Direction;
    public readonly float Range;
    public readonly float Angle;
    public readonly float Width;
    public readonly float ForwardOffset;
    public readonly float AngleOffset;
    public readonly float VerticalTolerance;
    public readonly float HitRevalidationTolerance;
    public readonly AnimationCurve ProgressCurve;

    public bool IsThrust => Shape == AttackAreaShape.Rectangle
        && FillMode == AttackFillMode.LinearFill;

    public AttackPatternRuntimeData(
        AttackAreaShape shape,
        AttackFillMode fillMode,
        AttackFillDirection direction,
        float range,
        float angle,
        float width,
        float forwardOffset,
        float angleOffset,
        float verticalTolerance,
        float hitRevalidationTolerance,
        AnimationCurve progressCurve)
    {
        Shape = shape;
        FillMode = fillMode;
        Direction = direction;
        Range = range;
        Angle = angle;
        Width = width;
        ForwardOffset = forwardOffset;
        AngleOffset = angleOffset;
        VerticalTolerance = verticalTolerance;
        HitRevalidationTolerance = hitRevalidationTolerance;
        ProgressCurve = progressCurve;
    }

    public float EvaluateProgress(float linearProgress)
    {
        float safeProgress = Mathf.Clamp01(linearProgress);
        return ProgressCurve != null ? Mathf.Clamp01(ProgressCurve.Evaluate(safeProgress)) : safeProgress;
    }

    public AttackPatternRuntimeData WithWidthMultiplier(float multiplier)
    {
        return new AttackPatternRuntimeData(
            Shape,
            FillMode,
            Direction,
            Range,
            Angle,
            Mathf.Max(0.05f, Width * Mathf.Max(0.01f, multiplier)),
            ForwardOffset,
            AngleOffset,
            VerticalTolerance,
            HitRevalidationTolerance,
            ProgressCurve);
    }

}
