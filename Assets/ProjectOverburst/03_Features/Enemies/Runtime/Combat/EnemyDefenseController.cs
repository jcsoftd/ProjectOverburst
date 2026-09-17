using UnityEngine;

public sealed class EnemyDefenseController : MonoBehaviour // 방패 방어 판정과 피해 감소
{
    [SerializeField] private EnemyBehaviorProfile behaviorProfile;
    [SerializeField] private EnemyAnimationBridge animationBridge;

    private bool isDefending;
    private bool blockedHitPending;

    public bool IsDefending { get { return isDefending && behaviorProfile != null && behaviorProfile.HasShield; } }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnDisable()
    {
        isDefending = false;
        blockedHitPending = false;
    }

    public void SetProfile(EnemyBehaviorProfile profile)
    {
        behaviorProfile = profile;
        if (behaviorProfile == null || !behaviorProfile.HasShield)
            SetDefending(false);
    }

    public void SetDefending(bool value)
    {
        isDefending = value && behaviorProfile != null && behaviorProfile.HasShield;
        if (!isDefending)
            blockedHitPending = false;
    }

    public bool TryModifyIncomingDamage(ref DamageInfo info, ref float damage)
    {
        if (!IsDefending || info.isDamageOverTime || info.source == null || damage <= 0f)
            return false;

        Vector3 toSource = info.source.transform.position - transform.position;
        toSource.y = 0f;
        if (toSource.sqrMagnitude <= 0.0001f)
            return false;

        float halfAngle = behaviorProfile.BlockAngle * 0.5f;
        if (Vector3.Angle(transform.forward, toSource.normalized) > halfAngle)
            return false;

        damage *= behaviorProfile.BlockedDamageMultiplier;
        info.damage = damage;
        info.knockback = 0f;
        blockedHitPending = true;
        ResolveReferences();
        animationBridge?.PlayBlockHit();
        return true;
    }

    public bool ConsumeBlockedHit()
    {
        if (!blockedHitPending)
            return false;

        blockedHitPending = false;
        return true;
    }

    public void ResolveReferences()
    {
        if (animationBridge == null)
            animationBridge = GetComponent<EnemyAnimationBridge>();
    }
}
