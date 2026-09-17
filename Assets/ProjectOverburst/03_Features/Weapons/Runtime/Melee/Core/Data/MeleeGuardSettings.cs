using System;
using UnityEngine;

[Serializable]
public struct MeleeGuardSettings
{
    [InspectorName("가드 중 이동 잠금")]
    public bool lockMovementWhileGuarding;

    [InspectorName("받는 데미지 배율")]
    [Range(0f, 1f)] public float incomingDamageMultiplier;
    [InspectorName("패링 판정 시간")]
    [Min(0f)] public float parryWindow;
    [InspectorName("패링 데미지")]
    [Min(1f)] public float parryDamage;

    public float SafeIncomingDamageMultiplier => Mathf.Clamp01(incomingDamageMultiplier);
    public float SafeParryWindow => Mathf.Max(0f, parryWindow);
    public float SafeParryDamage => Mathf.Max(1f, parryDamage);
    public bool AllowsMovementWhileGuarding => !lockMovementWhileGuarding;
}
