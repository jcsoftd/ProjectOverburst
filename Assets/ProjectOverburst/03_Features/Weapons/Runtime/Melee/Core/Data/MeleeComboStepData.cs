using System;
using UnityEngine;

[Serializable]
public struct MeleeComboStepData
{
    [InspectorName("공격 ID")]
    public string attackId;
    [InspectorName("공격 이름")]
    public string attackName;
    [InspectorName("애니메이션")]
    public AnimationClip animationClip;
    [InspectorName("애니메이션 속도 배율")]
    [Min(0.01f)] public float animationSpeedMultiplier;
    [InspectorName("진입 블렌딩 시간")]
    [Min(0f)] public float transitionDuration;
    [InspectorName("다음 콤보 입력 구간")]
    public ComboNormalizedWindow comboInputWindow;
    [InspectorName("행동 취소 가능 시점")]
    [Range(0f, 1f)] public float actionCancelStartNormalized;
    [InspectorName("이동 구간")]
    public AttackMovementPhaseData[] movementPhases;
    [InspectorName("무기 트레일 구간")]
    public AttackTrailPhaseData[] trailPhases;
    [InspectorName("공격 판정 구간")]
    public AttackPhaseData[] attackPhases;
}
