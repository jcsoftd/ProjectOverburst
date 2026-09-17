using UnityEngine;

public readonly struct AttackProgressSample
{
    public readonly bool HasTrace;
    public readonly Vector3 TracePoint;
    public readonly float RawProgress;
    public readonly float ResolvedProgress;

    public AttackProgressSample(
        bool hasTrace,
        Vector3 tracePoint,
        float rawProgress,
        float resolvedProgress)
    {
        HasTrace = hasTrace;
        TracePoint = tracePoint;
        RawProgress = rawProgress;
        ResolvedProgress = resolvedProgress;
    }

    public static AttackProgressSample FromTime(float progress)
    {
        float safeProgress = Mathf.Clamp01(progress);
        return new AttackProgressSample(false, default, safeProgress, safeProgress);
    }
}
