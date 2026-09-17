using System;
using UnityEngine;

[Serializable]
public struct ComboNormalizedWindow
{
    [InspectorName("입력 시작 시간")]
    [Range(0f, 1f)] public float startNormalizedTime;
    [InspectorName("입력 종료 시간")]
    [Range(0f, 1f)] public float endNormalizedTime;

    public float SafeStart => Mathf.Clamp01(startNormalizedTime);
    public float SafeEnd => Mathf.Max(SafeStart, Mathf.Clamp01(endNormalizedTime));

    public bool Contains(float normalizedTime)
    {
        float time = Mathf.Clamp01(normalizedTime);
        return time >= SafeStart && time <= SafeEnd;
    }
}
