using System;
using UnityEngine;

public static class MeleeAttackSpeedPolicy // 표시 공격속도와 실제 재생 기준 분리
{
    public const float BaselineAnimationSpeedMultiplier = 1.30f;
    public const float MaximumDisplayedAttackSpeedMultiplier = 1.50f;

    public static float ToPlaybackMultiplier(float displayedAttackSpeedMultiplier, float weaponBaseline = BaselineAnimationSpeedMultiplier)
    {
        return weaponBaseline * displayedAttackSpeedMultiplier;
    }
}

[Serializable]
public struct MeleeWeaponBaseSettings
{
    [InspectorName("기본 공격속도 배율")]
    [Min(0.01f)] public float attackSpeedMultiplier;

    [InspectorName("기본 공격 모션 재생 배율 (0 = 기존 1.3배)")]
    [Min(0f)] public float animationPlaybackBaseline;

    [InspectorName("기본 부채꼴 각도")]
    [Range(1f, 180f)] public float slashAngle;
    [InspectorName("기본 판정 폭")]
    [Min(0.05f)] public float hitWidth;
    [InspectorName("기본 경직 시간")]
    [Min(0f)] public float hitStunDuration;
    [InspectorName("전체 근접 VFX 배율")]
    [Min(0.01f)] public float vfxScaleMultiplier;

    public float SafeAttackSpeedMultiplier => attackSpeedMultiplier > 0f ? attackSpeedMultiplier : 1f;
    public float SafeAnimationPlaybackBaseline => animationPlaybackBaseline > 0f
        ? animationPlaybackBaseline
        : MeleeAttackSpeedPolicy.BaselineAnimationSpeedMultiplier;
}
