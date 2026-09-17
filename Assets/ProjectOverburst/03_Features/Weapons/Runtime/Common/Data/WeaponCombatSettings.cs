using System;
using UnityEngine;

[Serializable]
public struct WeaponUsageSettings
{
    [InspectorName("조준 가능")]
    public bool canAim;
    [InspectorName("기본 공격 가능")]
    public bool canPrimaryAttack;
    [InspectorName("조준 방식")]
    public WeaponAimType aimType;
    [InspectorName("공격 방식")]
    public WeaponAttackType attackType;
    [InspectorName("공격 입력 방식")]
    public WeaponFireMode fireMode;
    [InspectorName("공격속도 표시 방식")]
    public AttackRateDisplayType attackRateDisplayType;
}

[Serializable]
public struct WeaponAimSettings
{
    [InspectorName("조준 모드")]
    public WeaponAimMode mode;
    [InspectorName("조준 중 전투 이동 사용")]
    public bool usesCombatMove;
    [InspectorName("조준 중 상체 포즈 사용")]
    public bool usesUpperBodyPose;
    [InspectorName("마우스 방향 회전")]
    public bool rotatesToMouse;
    [InspectorName("조준 포즈 적용 범위")]
    public WeaponAimPoseBodyMode poseBodyMode;
    [InspectorName("상체 조준 채널")]
    public WeaponUpperBodyAimChannel upperBodyChannel;
    [InspectorName("근접 조준 상체 Y축 보정")]
    public float meleeUpperBodyYawOffset;
    [InspectorName("조준 포즈 클립")]
    public AnimationClip poseClip;
    [InspectorName("대체 조준 포즈 클립")]
    public AnimationClip alternatePoseClip;
    [InspectorName("대체 조준 포즈 사용")]
    public bool useAlternatePoseClip;
    [InspectorName("조준 이동속도 배율")]
    [Min(0f)] public float moveSpeedMultiplier;

    public AnimationClip ResolvePoseClip()
    {
        return useAlternatePoseClip && alternatePoseClip != null
            ? alternatePoseClip
            : poseClip;
    }
}

[Serializable]
public struct WeaponAnimationSettings
{
    [InspectorName("기본 공격 애니메이션")]
    public AnimationClip primaryAttackClip;
    [InspectorName("기본 공격 애니메이션 속도")]
    [Min(0.01f)] public float primaryAttackSpeed;
    [InspectorName("재장전 애니메이션")]
    public AnimationClip reloadClip;
    [InspectorName("빠른 공격 조준 포즈")]
    public AnimationClip quickFireAimPoseClip;
    [InspectorName("빠른 공격 애니메이션")]
    public AnimationClip quickFireClip;
}
