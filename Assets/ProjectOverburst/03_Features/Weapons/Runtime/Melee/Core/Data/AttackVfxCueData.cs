using System;
using UnityEngine;
using UnityEngine.Serialization;

public enum AttackVfxPlacementMode
{
    PatternOrigin = 0,
    WeaponTracePoint = 1,
    PatternGround = 2,
    OwnerOrigin = 3
}

public enum AttackVfxMotionRole
{
    Unspecified,
    HorizontalSweep,
    HorizontalCircular,
    VerticalRising,
    VerticalFalling,
    Thrust,
    GroundImpact
}

public enum AttackVfxMirrorAxis
{
    None,
    Horizontal,
    Vertical
}

[Serializable]
public struct AttackVfxCueData
{
    [InspectorName("VFX 동작 역할")]
    public AttackVfxMotionRole motionRole;

    [FormerlySerializedAs("mirrorPrefab")]
    [InspectorName("VFX 미러 축")]
    public AttackVfxMirrorAxis mirrorAxis;

    [InspectorName("VFX 정의")]
    public MeleeAttackVfxDefinition definition;

    [InspectorName("재생 진행률")]
    [Range(0f, 1f)] public float triggerProgress;

    [InspectorName("배치 방식")]
    public AttackVfxPlacementMode placementMode;

    [InspectorName("크기 배율 (%)")]
    [Multiplier(0.01f)] public float scaleMultiplier;

    [InspectorName("WeaponTip 경사 자동 적용")]
    public bool autoSwingSlope;

    [InspectorName("이 VFX의 추가 각도 보정")]
    [Range(-180f, 180f)] public float swingSlopeOffsetDegrees;

    [InspectorName("이 VFX의 추가 XYZ 회전")]
    public Vector3 localEulerOffset;

    [InspectorName("속성 교체 키")]
    public string elementOverrideKey;

    public float SafeScaleMultiplier => scaleMultiplier > 0f ? scaleMultiplier : 1f;

    public float ResolveSwingSlope(float attackSwingSlope)
    {
        return (autoSwingSlope ? attackSwingSlope : 0f) + swingSlopeOffsetDegrees;
    }
}
