using UnityEngine;

public enum EnemyAbilityExecutionMode
{
    MeleeArc,
    DirectTarget,
    Charge,
    AreaSlam,
    Projectile,
    Summon,
    Zone
}

[CreateAssetMenu(menuName = "OVERBURST/Enemies/Ability Definition", fileName = "EAD_EnemyAbility")]
public sealed class EnemyAbilityDefinition : ScriptableObject
{
    [SerializeField] private string abilityId;
    [SerializeField] private string animatorTrigger = "Attack1";
    [SerializeField] private EnemyAbilityExecutionMode executionMode =
        EnemyAbilityExecutionMode.MeleeArc;
    [SerializeField, Min(0f)] private float damage = 10f;
    [SerializeField, Min(0f)] private float minimumRange;
    [SerializeField, Min(0f)] private float range = 1.7f;
    [SerializeField, Min(0.01f)] private float hitRadius = 0.8f;
    [SerializeField, Range(0f, 360f)] private float hitAngle = 120f;
    [SerializeField, Min(0f)] private float verticalTolerance = 0.75f;
    [SerializeField] private bool requireLineOfSight;
    [SerializeField, Min(0f)] private float cooldown = 1.25f;
    [SerializeField, Min(0f)] private float hitDelay = 0.45f;
    [SerializeField, Range(0.05f, 0.95f)] private float hitNormalizedTime = 0.45f;
    [SerializeField, Min(0.01f)] private float attackAnimationDuration = 1f;
    [SerializeField, Min(0f)] private float attackLockDuration = 0.9f;
    [SerializeField, Min(0f)] private float weight = 1f;
    [SerializeField] private int priority;
    [SerializeField, Range(0f, 1f)] private float minimumSelfHealthNormalized;
    [SerializeField, Range(0f, 1f)] private float maximumSelfHealthNormalized = 1f;
    [SerializeField] private bool requireTargetInRangeUntilHit = true;
    [SerializeField] private float[] additionalHitNormalizedTimes;
    public int HitCount => 1 + (additionalHitNormalizedTimes != null ? additionalHitNormalizedTimes.Length : 0);
    public float GetHitNormalizedTime(int index) => index == 0 ? HitNormalizedTime : additionalHitNormalizedTimes[index - 1];
    public void ConfigureAdditionalHits(params float[] times)
    {
        float previous = HitNormalizedTime;
        foreach (float time in times)
        {
            if (float.IsNaN(time) || time <= previous || time > .95f)
                throw new System.ArgumentException("추가 타격 시점은 첫 타격 이후 오름차순이어야 합니다.");
            previous = time;
        }
        additionalHitNormalizedTimes = (float[])times.Clone();
    }

    public string AbilityId => abilityId;
    public string AnimatorTrigger => animatorTrigger;
    public EnemyAbilityExecutionMode ExecutionMode => executionMode;
    public float Damage => Mathf.Max(0f, damage);
    public float MinimumRange => Mathf.Clamp(minimumRange, 0f, Range);
    public float Range => Mathf.Max(0f, range);
    public float HitRadius => Mathf.Max(0.01f, hitRadius);
    public float HitAngle => Mathf.Clamp(hitAngle, 0f, 360f);
    public float VerticalTolerance => Mathf.Max(0f, verticalTolerance);
    public bool RequireLineOfSight => requireLineOfSight;
    public float Cooldown => Mathf.Max(0f, cooldown);
    public float HitDelay => Mathf.Max(0f, hitDelay);
    public float HitNormalizedTime => Mathf.Clamp(hitNormalizedTime, 0.05f, 0.95f);
    public float AttackAnimationDuration => Mathf.Max(0.01f, attackAnimationDuration);
    public float AttackLockDuration => Mathf.Max(0f, attackLockDuration);
    public float Weight => Mathf.Max(0f, weight);
    public int Priority => priority;
    public float MinimumSelfHealthNormalized =>
        Mathf.Clamp01(Mathf.Min(minimumSelfHealthNormalized, maximumSelfHealthNormalized));
    public float MaximumSelfHealthNormalized =>
        Mathf.Clamp01(Mathf.Max(minimumSelfHealthNormalized, maximumSelfHealthNormalized));
    public bool RequireTargetInRangeUntilHit => requireTargetInRangeUntilHit;
    public bool IsValid => !string.IsNullOrWhiteSpace(abilityId)
        && !string.IsNullOrWhiteSpace(animatorTrigger)
        && Range > 0f
        && MinimumRange <= Range
        && Weight > 0f;

    public bool MatchesUseConditions(float distance, float selfHealthNormalized)
    {
        float resolvedDistance = Mathf.Max(0f, distance);
        float resolvedHealth = Mathf.Clamp01(selfHealthNormalized);
        return resolvedDistance >= MinimumRange
            && resolvedDistance <= Range
            && resolvedHealth >= MinimumSelfHealthNormalized
            && resolvedHealth <= MaximumSelfHealthNormalized;
    }

    public void ConfigureUsePolicy(
        float minRange,
        int selectionPriority,
        float minimumHealthNormalized = 0f,
        float maximumHealthNormalized = 1f)
    {
        minimumRange = Mathf.Max(0f, minRange);
        priority = selectionPriority;
        minimumSelfHealthNormalized = Mathf.Clamp01(minimumHealthNormalized);
        maximumSelfHealthNormalized = Mathf.Clamp01(maximumHealthNormalized);
        if (minimumSelfHealthNormalized > maximumSelfHealthNormalized)
        {
            float swap = minimumSelfHealthNormalized;
            minimumSelfHealthNormalized = maximumSelfHealthNormalized;
            maximumSelfHealthNormalized = swap;
        }
    }

    public void Configure(
        string id,
        string trigger,
        float abilityDamage,
        float abilityRange,
        float abilityCooldown,
        float normalizedHitTime,
        float selectionWeight)
    {
        Configure(
            id,
            trigger,
            abilityDamage,
            abilityRange,
            0.8f,
            120f,
            abilityCooldown,
            0.45f,
            normalizedHitTime,
            0.9f,
            selectionWeight,
            true);
    }

    public void Configure(
        string id,
        string trigger,
        float abilityDamage,
        float abilityRange,
        float radius,
        float angle,
        float abilityCooldown,
        float delay,
        float normalizedHitTime,
        float lockDuration,
        float selectionWeight,
        bool keepRangeGate,
        EnemyAbilityExecutionMode mode = EnemyAbilityExecutionMode.MeleeArc,
        float targetVerticalTolerance = 0.75f,
        bool lineOfSightRequired = false,
        float animationDuration = 1f)
    {
        abilityId = id != null ? id.Trim() : string.Empty;
        animatorTrigger = trigger != null ? trigger.Trim() : string.Empty;
        executionMode = mode;
        damage = Mathf.Max(0f, abilityDamage);
        minimumRange = 0f;
        range = Mathf.Max(0f, abilityRange);
        hitRadius = Mathf.Max(0.01f, radius);
        hitAngle = Mathf.Clamp(angle, 0f, 360f);
        verticalTolerance = Mathf.Max(0f, targetVerticalTolerance);
        requireLineOfSight = lineOfSightRequired;
        cooldown = Mathf.Max(0f, abilityCooldown);
        hitDelay = Mathf.Max(0f, delay);
        hitNormalizedTime = Mathf.Clamp(normalizedHitTime, 0.05f, 0.95f);
        attackAnimationDuration = Mathf.Max(0.01f, animationDuration);
        attackLockDuration = Mathf.Max(0f, lockDuration);
        weight = Mathf.Max(0f, selectionWeight);
        priority = 0;
        minimumSelfHealthNormalized = 0f;
        maximumSelfHealthNormalized = 1f;
        requireTargetInRangeUntilHit = keepRangeGate;
    }
}
