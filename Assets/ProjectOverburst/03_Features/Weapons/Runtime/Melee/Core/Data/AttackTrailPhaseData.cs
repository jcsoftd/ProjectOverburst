using System;
using UnityEngine;

[Serializable]
public struct AttackTrailPhaseData
{
    [InspectorName("트레일 시작 시간")]
    [Range(0f, 1f)] public float startNormalizedTime;
    [InspectorName("트레일 종료 시간")]
    [Range(0f, 1f)] public float endNormalizedTime;

    public float SafeStart => Mathf.Clamp01(startNormalizedTime);
    public float SafeEnd => Mathf.Max(SafeStart + 0.0001f, Mathf.Clamp01(endNormalizedTime));
}
