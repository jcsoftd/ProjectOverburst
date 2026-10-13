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
    [InspectorName("Idle 복귀 후 직접 연계 시작 기준")]
    [Tooltip("이 타수의 진행률이 기준 이상이면 다음 타수는 준비 동작부터 시작합니다. 0은 사용하지 않습니다.")]
    [Range(0f, 1f)] public float settledIdleStartNormalizedTime;
    [InspectorName("동작 구간 가속")]
    public MeleePlaybackAcceleration playbackAcceleration;
    [InspectorName("다음 콤보 입력 구간")]
    public ComboNormalizedWindow comboInputWindow;
    [InspectorName("행동 취소 가능 시점")]
    [Range(0f, 1f)] public float actionCancelStartNormalized;
    [InspectorName("이동 구간")]
    public AttackMovementPhaseData[] movementPhases;
    [InspectorName("비주얼 Y 높이 (m)")]
    [Tooltip("클립 진행률 0~1에 따른 시각 높이입니다. 캐릭터 충돌체는 지면을 유지합니다.")]
    public AnimationCurve visualHeightCurve;
    [InspectorName("무기 트레일 구간")]
    public AttackTrailPhaseData[] trailPhases;
    [InspectorName("공격 판정 구간")]
    public AttackPhaseData[] attackPhases;
    [InspectorName("베기별 휘두름음")]
    public MeleeSwingSfxCueData[] swingSfxCues;

    public bool TryValidateSwingSfx(out string error)
    {
        error = null;
        if (swingSfxCues == null || swingSfxCues.Length == 0) return true;
        if (attackPhases == null || swingSfxCues.Length != attackPhases.Length || swingSfxCues.Length > 32)
        { error = "휘두름음 개수는 판정 구간 수와 같고 32개 이하여야 합니다."; return false; }
        for (int i = 0; i < swingSfxCues.Length; i++)
            if (!swingSfxCues[i].IsValid)
            { error = $"{i + 1}번째 휘두름음의 클립·피크 시간·정렬 진행률을 확인하세요."; return false; }
        return true;
    }
}

[Serializable]
public struct MeleeSwingSfxCueData
{
    public AudioClip clip;
    [Min(0f)] public float peakSeconds;
    [Range(0f, 1f)] public float alignmentProgress;

    public bool IsValid => clip != null && !float.IsNaN(peakSeconds) && !float.IsInfinity(peakSeconds)
        && peakSeconds >= 0f && peakSeconds < clip.length
        && !float.IsNaN(alignmentProgress) && alignmentProgress >= 0f && alignmentProgress <= 1f;
}

// Both the Animator and combat executors use this clock, preserving every source pose.
[Serializable]
public struct MeleePlaybackAcceleration
{
    [Tooltip("대시 회전 강공의 짧은 집중과 해방 시계를 사용합니다.")]
    public bool dashHeavyFocus;
    [Range(0f, 1f)] public float startNormalized;
    [Range(0f, 1f)] public float endNormalized;
    [Range(1f, 3f)] public float peakMultiplier;

    public bool IsEnabled => dashHeavyFocus || (peakMultiplier > 1f && endNormalized > startNormalized
        && startNormalized >= 0f && endNormalized <= 1f);
    private float MeanSpeed => (Mathf.Clamp(peakMultiplier, 1f, 3f) + 1f) * .5f;

    // Elapsed time is expressed in units of the unaccelerated clip duration.
    public float ToClipProgress(float elapsed)
    {
        if (dashHeavyFocus) return DashHeavyFocusClock.Sample(elapsed * DashHeavyFocusClock.ClipLength) / DashHeavyFocusClock.ClipLength;
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
        if (dashHeavyFocus) return DashHeavyFocusClock.RealAt(clipProgress * DashHeavyFocusClock.ClipLength) / DashHeavyFocusClock.ClipLength;
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
