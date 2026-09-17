using System;
using UnityEngine;
using UnityEngine.Serialization;

[Serializable]
public struct MeleeGripSettings
{
    [InspectorName("그립 IK 사용")]
    public bool enabled;
    [FormerlySerializedAs("lowerArmWeight")]
    [InspectorName("왼손 위치 가중치")]
    [Range(0f, 1f)] public float positionWeight;
    [InspectorName("블렌딩 속도")]
    [Min(0f)] public float blendSpeed;
}
