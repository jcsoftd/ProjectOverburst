using UnityEngine;

public sealed class WeaponTipProgressResolver
{
#if UNITY_EDITOR
    private bool editorInitialized;
    private float editorResolvedProgress;
    private float editorPreviousAngle;
    private float editorPreviousDirectionalAngle;
    private float editorAccumulatedTravel;
    private float editorInitialForward;
    private bool editorEnteredSector;
#endif

    public void Reset()
    {
#if UNITY_EDITOR
        editorInitialized = false;
        editorResolvedProgress = 0f;
        editorPreviousAngle = 0f;
        editorPreviousDirectionalAngle = 0f;
        editorAccumulatedTravel = 0f;
        editorInitialForward = 0f;
        editorEnteredSector = false;
#endif
    }

    public bool TryEvaluate(
        AttackProgressSource source,
        float attackNormalizedTime,
        MeleeAttackPhaseTrajectoryBakeData bakedPhase,
        WeaponTraceBinding liveTraceBinding,
        out AttackProgressSample sample)
    {
        sample = default;
        if (bakedPhase == null
            || !bakedPhase.IsUsableFor(source)
            || !bakedPhase.TryEvaluate(
                attackNormalizedTime,
                out float rawProgress,
                out float resolvedProgress))
        {
            return false;
        }

        bool hasLiveTrace = liveTraceBinding != null && liveTraceBinding.IsValid;
        Vector3 liveTracePoint = hasLiveTrace
            ? liveTraceBinding.WeaponTip.position
            : default;
        sample = new AttackProgressSample(
            hasLiveTrace,
            liveTracePoint,
            Mathf.Clamp01(rawProgress),
            Mathf.Clamp01(resolvedProgress));
        return true;
    }

#if UNITY_EDITOR
    // 구 Editor 스모크 검사 호환 전용
    public bool TryPrime(
        AttackProgressSource source,
        AttackPatternRuntimeData pattern,
        AttackPatternBasis basis,
        WeaponTraceBinding binding)
    {
        if (!TryResolveEditorTrace(pattern, basis, binding, out _, out float x, out float z))
            return false;
        CaptureEditorBaseline(source, pattern, x, z);
        return true;
    }

    public bool TryEvaluate(
        AttackProgressSource source,
        AttackPatternRuntimeData pattern,
        AttackPatternBasis basis,
        WeaponTraceBinding binding,
        out AttackProgressSample sample)
    {
        sample = default;
        if (!TryResolveEditorTrace(pattern, basis, binding, out Vector3 point, out float x, out float z))
            return false;
        if (!editorInitialized)
        {
            CaptureEditorBaseline(source, pattern, x, z);
            sample = new AttackProgressSample(true, point, 0f, 0f);
            return false;
        }

        float raw;
        switch (source)
        {
            case AttackProgressSource.WeaponTipAngularTravel:
                float angle = Mathf.Atan2(x, z) * Mathf.Rad2Deg;
                float sign = pattern.Direction == AttackFillDirection.RightToLeft ? -1f : 1f;
                editorAccumulatedTravel += Mathf.DeltaAngle(editorPreviousAngle, angle) * sign;
                editorPreviousAngle = angle;
                raw = editorAccumulatedTravel / 360f;
                break;
            case AttackProgressSource.WeaponTipSectorAngle:
                float current = Mathf.Atan2(x, z) * Mathf.Rad2Deg;
                float sectorSign = pattern.Direction == AttackFillDirection.RightToLeft ? -1f : 1f;
                float previousDirectional = editorPreviousDirectionalAngle;
                float currentDirectional = previousDirectional
                    + Mathf.DeltaAngle(editorPreviousAngle, current) * sectorSign;
                editorPreviousAngle = current;
                editorPreviousDirectionalAngle = currentDirectional;
                if (!editorEnteredSector)
                {
                    if (currentDirectional < previousDirectional
                        || currentDirectional < 0f
                        || previousDirectional > pattern.Angle)
                    {
                        sample = new AttackProgressSample(true, point, 0f, editorResolvedProgress);
                        return false;
                    }
                    editorEnteredSector = true;
                }
                raw = Mathf.Max(previousDirectional, currentDirectional)
                    / Mathf.Max(0.0001f, pattern.Angle);
                break;
            case AttackProgressSource.WeaponTipForward:
                raw = (z - editorInitialForward) / Mathf.Max(0.0001f, pattern.Range);
                break;
            default:
                return false;
        }

        raw = Mathf.Clamp01(raw);
        editorResolvedProgress = Mathf.Max(editorResolvedProgress, raw);
        sample = new AttackProgressSample(true, point, raw, editorResolvedProgress);
        return true;
    }

    private void CaptureEditorBaseline(
        AttackProgressSource source,
        AttackPatternRuntimeData pattern,
        float x,
        float z)
    {
        editorInitialized = true;
        editorPreviousAngle = Mathf.Atan2(x, z) * Mathf.Rad2Deg;
        editorInitialForward = z;
        editorPreviousDirectionalAngle = ResolveEditorDirectionalAngle(pattern, editorPreviousAngle);
        editorEnteredSector = source == AttackProgressSource.WeaponTipSectorAngle
            && editorPreviousDirectionalAngle >= 0f
            && editorPreviousDirectionalAngle <= pattern.Angle;
    }

    private static bool TryResolveEditorTrace(
        AttackPatternRuntimeData pattern,
        AttackPatternBasis basis,
        WeaponTraceBinding binding,
        out Vector3 point,
        out float x,
        out float z)
    {
        point = default;
        x = 0f;
        z = 0f;
        if (binding == null || !binding.IsValid)
            return false;
        point = binding.WeaponTip.position;
        Vector3 offset = point - basis.GetPatternOrigin(pattern);
        x = Vector3.Dot(offset, basis.Right);
        z = Vector3.Dot(offset, basis.Forward);
        return true;
    }

    private static float ResolveEditorDirectionalAngle(
        AttackPatternRuntimeData pattern,
        float angle)
    {
        float half = pattern.Angle * 0.5f;
        bool rightToLeft = pattern.Direction == AttackFillDirection.RightToLeft;
        float start = pattern.AngleOffset + (rightToLeft ? half : -half);
        float delta = Mathf.DeltaAngle(start, angle);
        return rightToLeft ? -delta : delta;
    }
#endif
}
