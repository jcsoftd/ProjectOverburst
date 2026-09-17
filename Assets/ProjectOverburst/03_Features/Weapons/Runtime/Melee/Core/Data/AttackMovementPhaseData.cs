using System;
using UnityEngine;

public enum AttackMovementMode
{
    ForwardDistance = 0,
    SignedForwardTrajectory = 1
}

[Serializable]
public struct AttackMovementPhaseData
{
    [InspectorName("이동 시작 시간")]
    [Range(0f, 1f)] public float startNormalizedTime;
    [InspectorName("이동 종료 시간")]
    [Range(0f, 1f)] public float endNormalizedTime;
    [InspectorName("이동 방식")]
    public AttackMovementMode movementMode;
    [InspectorName("이동 거리")]
    [Min(0f)] public float distance;
    [InspectorName("진행 곡선")]
    public AnimationCurve progressCurve;
    [InspectorName("로컬 전진 궤적")]
    public AnimationCurve localForwardCurve;

    public float SafeStart => Mathf.Clamp01(startNormalizedTime);
    public float SafeEnd => Mathf.Max(SafeStart + 0.0001f, Mathf.Clamp01(endNormalizedTime));

    public float EvaluateProgress(float normalizedProgress)
    {
        float progress = Mathf.Clamp01(normalizedProgress);
        return progressCurve != null ? Mathf.Clamp01(progressCurve.Evaluate(progress)) : progress;
    }

    public Vector3 EvaluateLocalDisplacement(float normalizedProgress)
    {
        float progress = Mathf.Clamp01(normalizedProgress);
        if (movementMode != AttackMovementMode.SignedForwardTrajectory)
            return Vector3.forward * (Mathf.Max(0f, distance) * EvaluateProgress(progress));

        return Vector3.forward * (localForwardCurve != null ? localForwardCurve.Evaluate(progress) : 0f);
    }
}
