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
    [InspectorName("직접 연계 시 클립 시작 진행률")]
    [Range(0f, 0.95f)] public float continuationStartNormalizedTime;
    [InspectorName("동작 구간 가속")]
    public MeleePlaybackAcceleration playbackAcceleration;
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

// Both the Animator and combat executors use this clock, preserving every source pose.
[Serializable]
public struct MeleePlaybackAcceleration
{
    [Range(0f, 1f)] public float startNormalized;
    [Range(0f, 1f)] public float endNormalized;
    [Range(1f, 3f)] public float peakMultiplier;

    public bool IsEnabled => peakMultiplier > 1f && endNormalized > startNormalized
        && startNormalized >= 0f && endNormalized <= 1f;
    private float MeanSpeed => (Mathf.Clamp(peakMultiplier, 1f, 3f) + 1f) * .5f;

    // Elapsed time is expressed in units of the unaccelerated clip duration.
    public float ToClipProgress(float elapsed)
    {
        if (!IsEnabled || elapsed <= startNormalized) return elapsed;
        float span = endNormalized - startNormalized;
        float duration = span / MeanSpeed;
        if (elapsed >= startNormalized + duration)
            return elapsed + span - duration;
        float u = (elapsed - startNormalized) / duration;
        float eased = u - (1f - 1f / MeanSpeed) * Mathf.Sin(2f * Mathf.PI * u) / (2f * Mathf.PI);
        return startNormalized + span * eased;
    }

    public float ToElapsed(float clipProgress)
    {
        if (!IsEnabled || clipProgress <= startNormalized) return clipProgress;
        float span = endNormalized - startNormalized;
        float duration = span / MeanSpeed;
        if (clipProgress >= endNormalized) return clipProgress - span + duration;
        float low = startNormalized, high = startNormalized + duration;
        for (int i = 0; i < 24; i++)
        {
            float middle = (low + high) * .5f;
            if (ToClipProgress(middle) < clipProgress) low = middle;
            else high = middle;
        }
        return (low + high) * .5f;
    }
}
