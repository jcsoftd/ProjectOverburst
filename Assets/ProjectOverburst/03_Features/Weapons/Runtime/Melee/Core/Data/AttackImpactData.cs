using System;
using UnityEngine;

[Serializable]
public struct AttackImpactData
{
    [InspectorName("데미지 배율")]
    [Min(0f)] public float damageMultiplier;

    [InspectorName("넉백 배율")]
    [Min(0f)] public float knockbackMultiplier;

    [InspectorName("피격 경직 배율 (%)")]
    [Multiplier] public float hitStunMultiplier;

    [InspectorName("넉백 반응 시간")]
    [Min(0f)] public float knockbackReactionDuration;

    [InspectorName("대상 기본 반응 덮어쓰기")]
    public bool overrideTargetReaction;

    [InspectorName("적중 효과 실행")]
    public bool triggersOnHitEffects;

    [InspectorName("타격 피드백 프로필")]
    public CombatHitFeedbackProfile hitFeedbackProfile;

    [InspectorName("공중 충격량")]
    [Min(0f)] public float airborneImpulse;

    [InspectorName("공중 경직 시간")]
    [Min(0f)] public float airborneStunDuration;

    public float SafeDamageMultiplier => Mathf.Max(0f, damageMultiplier);
    public float SafeKnockbackMultiplier => Mathf.Max(0f, knockbackMultiplier);
    public float SafeHitStunMultiplier => Mathf.Max(0f, hitStunMultiplier);
}
