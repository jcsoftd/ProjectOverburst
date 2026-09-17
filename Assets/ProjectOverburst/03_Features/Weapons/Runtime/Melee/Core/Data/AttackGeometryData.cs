using System;
using UnityEngine;

[Serializable]
public struct AttackGeometryData
{
    public float SafeRangeMultiplier => rangeMultiplier > 0f ? rangeMultiplier : 1f;
    public float SafeAngleMultiplier => angleMultiplier > 0f ? angleMultiplier : 1f;
    public float SafeWidthMultiplier => widthMultiplier > 0f ? widthMultiplier : 1f;
    public float SafeVfxScaleMultiplier => vfxScaleMultiplier > 0f ? vfxScaleMultiplier : 1f;

    [InspectorName("사거리 배율 (%)")]
    [Multiplier(0.01f)] public float rangeMultiplier;

    [InspectorName("각도 배율 (%)")]
    [Multiplier(0.01f)] public float angleMultiplier;

    [InspectorName("폭 배율 (%)")]
    [Multiplier(0.01f)] public float widthMultiplier;

    [InspectorName("VFX 크기 배율 (%)")]
    [Multiplier(0.01f)] public float vfxScaleMultiplier;

    [InspectorName("전방 오프셋 직접 지정")]
    public bool overrideForwardOffset;

    [InspectorName("전방 오프셋")]
    public float forwardOffset;

    public AttackPatternRuntimeData Resolve(
        AttackPatternDefinition definition,
        float baseRange,
        float baseAngle,
        float baseWidth)
    {
        if (definition == null)
            return default;

        return definition.Resolve(
            Mathf.Max(0.1f, baseRange) * SafeRangeMultiplier,
            Mathf.Max(1f, baseAngle) * SafeAngleMultiplier,
            Mathf.Max(0.05f, baseWidth) * SafeWidthMultiplier,
            overrideForwardOffset ? forwardOffset : 0f);
    }
}
