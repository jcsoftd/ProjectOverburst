using System;
using UnityEngine;

public enum AttackVfxSwingOrientation
{
    Horizontal,
    Vertical
}

public enum AttackVfxBakeMask
{
    HorizontalFrontSector,
    VerticalFrontSector
}

[Serializable]
public struct AttackVfxSwingSettings
{
    [InspectorName("VFX 베기 방향")]
    public AttackVfxSwingOrientation orientation;

    [InspectorName("베이크 전용 마스크")]
    public AttackVfxBakeMask bakeMask;

    [InspectorName("기준각 허용 편차")]
    [Range(0f, 90f)] public float maxDeviationDegrees;

    [InspectorName("베이크 마스크 각도")]
    [Range(1f, 180f)] public float maskAngleDegrees;

    [InspectorName("수직 마스크 중심 높이")]
    [Min(0f)] public float verticalPivotHeight;

    [InspectorName("수직 마스크 좌우 허용폭")]
    [Min(0.01f)] public float verticalHalfWidth;

    [InspectorName("VFX 진행 방향 반전")]
    public bool reverseDirection;

    public float BaseSlopeDegrees => orientation == AttackVfxSwingOrientation.Vertical
        ? 90f
        : 0f;

    public float SafeDeviationDegrees => Mathf.Clamp(maxDeviationDegrees, 0f, 90f);
    public float SafeMaskAngleDegrees => Mathf.Clamp(maskAngleDegrees, 1f, 180f);
    public float SafeVerticalPivotHeight => Mathf.Max(0f, verticalPivotHeight);
    public float SafeVerticalHalfWidth => Mathf.Max(0.01f, verticalHalfWidth);

    public float ResolveSlope(float sampledSlopeDegrees, float offsetDegrees)
    {
        float baseSlope = BaseSlopeDegrees;
        float lineSlope = NormalizeLineAngleNear(sampledSlopeDegrees + offsetDegrees, baseSlope);
        float deviation = Mathf.DeltaAngle(baseSlope, lineSlope);
        float resolved = baseSlope + Mathf.Clamp(
            deviation,
            -SafeDeviationDegrees,
            SafeDeviationDegrees);

        if (reverseDirection)
            resolved += 180f;

        return Mathf.DeltaAngle(0f, resolved);
    }

    public float ResolvePrefabRotationOffset(float sampledSlopeDegrees, float offsetDegrees)
    {
        float resolvedSlope = ResolveSlope(sampledSlopeDegrees, offsetDegrees);
        return Mathf.DeltaAngle(BaseSlopeDegrees, resolvedSlope);
    }

    private static float NormalizeLineAngleNear(float angle, float reference)
    {
        float normalized = Mathf.DeltaAngle(0f, angle);
        float alternate = Mathf.DeltaAngle(0f, normalized + 180f);
        return Mathf.Abs(Mathf.DeltaAngle(reference, alternate))
            < Mathf.Abs(Mathf.DeltaAngle(reference, normalized))
                ? alternate
                : normalized;
    }
}

[Serializable]
public struct AttackPhaseData
{
    [InspectorName("공격 패턴")]
    public AttackPatternDefinition attackPattern;

    [InspectorName("판정 시작 시간")]
    [Range(0f, 1f)] public float startNormalizedTime;

    [InspectorName("판정 종료 시간")]
    [Range(0f, 1f)] public float endNormalizedTime;

    [InspectorName("판정 범위")]
    public AttackGeometryData geometry;

    [InspectorName("진행률 기준")]
    public AttackProgressSource progressSource;

    [InspectorName("판정 원점 추적")]
    public AttackBasisFollowMode basisFollowMode;

    [InspectorName("타격 효과")]
    public AttackImpactData impact;

    [InspectorName("공격 VFX Cue")]
    public AttackVfxCueData[] vfxCues;

    [Header("공격별 VFX 경사")]
    [InspectorName("확정된 공격 경사 사용")]
    public bool useBakedVfxSwingSlope;

    [InspectorName("공격별 확정 경사각")]
    [Range(-180f, 180f)] public float bakedVfxSwingSlopeDegrees;

    [InspectorName("공격별 추가 각도 보정")]
    [Range(-180f, 180f)] public float vfxSwingSlopeOffsetDegrees;

    [InspectorName("VFX 베기 베이크 설정")]
    public AttackVfxSwingSettings vfxSwingSettings;

    public float SafeStart => Mathf.Clamp01(startNormalizedTime);
    public float SafeEnd => Mathf.Max(SafeStart + 0.0001f, Mathf.Clamp01(endNormalizedTime));

    public float ResolveVfxSwingSlope(float sampledSlopeDegrees)
    {
        return vfxSwingSettings.ResolveSlope(
            sampledSlopeDegrees,
            vfxSwingSlopeOffsetDegrees);
    }

    public float ResolveVfxSwingRotationOffset(float sampledSlopeDegrees)
    {
        return vfxSwingSettings.ResolvePrefabRotationOffset(
            sampledSlopeDegrees,
            vfxSwingSlopeOffsetDegrees);
    }

    public AttackPatternRuntimeData ResolvePattern(
        float fallbackRange,
        float fallbackAngle,
        float fallbackWidth)
    {
        return geometry.Resolve(
            attackPattern,
            fallbackRange,
            fallbackAngle,
            fallbackWidth);
    }
}
