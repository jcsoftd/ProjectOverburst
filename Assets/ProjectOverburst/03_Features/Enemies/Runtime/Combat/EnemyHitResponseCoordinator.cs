using System.Collections.Generic;
using UnityEngine;

public enum EnemyHitResponseOutcome
{
    None,
    FeedbackOnly,
    Flinch,
    Death
}

// The only direct-hit owner for theme actors. Damage, aggro and surface feedback stay with CombatHealth.
[DisallowMultipleComponent]
[RequireComponent(typeof(CombatHealth))]
public sealed class EnemyHitResponseCoordinator : MonoBehaviour
{
    private const float MediumHitWindow = 1.1f;
    private const float MediumFlinchCooldown = 0.85f;
    private const float EliteFlinchCooldown = 1.4f;
    private const float MissingSequenceDuplicateWindow = 0.12f;

    private readonly HashSet<long> mediumSequences = new HashSet<long>();
    private readonly Dictionary<int, float> missingSequenceTimes = new Dictionary<int, float>();
    private CombatHealth health;
    private EnemyRank rank;
    private EnemyMovementReaction reaction;
    private EnemyAnimationBridge animationBridge;
    private EnemyAbilityController abilityController;
    private EnemyMeleeAttackController melee;
    private EnemyDefenseController defense;
    private float lastDistinctHitAt;
    private float nextFlinchAt;
    private int distinctHitCount;

    public EnemyHitResponseOutcome LastOutcome { get; private set; }
    public int FlinchCount { get; private set; }
    public int FeedbackOnlyCount { get; private set; }
    public int DeathCount { get; private set; }
    public int MediumAccumulatedHits => distinctHitCount;

    private void Awake() => ResolveReferences();

    private void OnEnable()
    {
        ResolveReferences();
        ResetForReuse();
        if (health == null)
            return;
        health.OnDamageResolved += HandleResolvedDamage;
        health.OnReset += HandleHealthReset;
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.OnDamageResolved -= HandleResolvedDamage;
            health.OnReset -= HandleHealthReset;
        }
        ResetForReuse();
    }

    private void HandleHealthReset(CombatHealth source) => ResetForReuse();

    public void ResetForReuse()
    {
        mediumSequences.Clear();
        missingSequenceTimes.Clear();
        distinctHitCount = 0;
        lastDistinctHitAt = 0f;
        nextFlinchAt = 0f;
        LastOutcome = EnemyHitResponseOutcome.None;
        FlinchCount = 0;
        FeedbackOnlyCount = 0;
        DeathCount = 0;
    }

    private void HandleResolvedDamage(CombatHealth source, DamageInfo info, float actualDamage, bool fatal)
    {
        bool shieldBlocked = defense != null && defense.ConsumeBlockedHit();
        if (fatal)
        {
            ClearMediumWindow();
            LastOutcome = EnemyHitResponseOutcome.Death;
            DeathCount++;
            return; // OnDead owns the death animation and cancellation.
        }

        if (actualDamage <= 0f || shieldBlocked || info.isDamageOverTime
            || !info.triggersOnHitEffects || (animationBridge != null && animationBridge.IsFrozen))
        {
            RecordFeedbackOnly();
            return;
        }

        EnemyGradeType grade = rank != null ? rank.GradeType : EnemyGradeType.Normal;
        EnemyHitWeightProfile profile = reaction != null ? reaction.HitWeightProfile : null;
        EnemyHitWeight weight = profile != null ? profile.Weight : EnemyHitWeight.Standard;
        bool attackInProgress = (abilityController != null && abilityController.IsExecuting)
            || (melee != null && melee.IsAttacking);

        if (grade == EnemyGradeType.Elite || grade == EnemyGradeType.GreaterElite)
        {
            if (!info.isCritical || attackInProgress || Time.time < nextFlinchAt)
            {
                RecordFeedbackOnly();
                return;
            }
            if (ApplyFlinch(info))
                nextFlinchAt = Time.time + EliteFlinchCooldown;
            else
                RecordFeedbackOnly();
            return;
        }

        if (weight == EnemyHitWeight.Light)
        {
            if (!ApplyFlinch(info))
                RecordFeedbackOnly();
            return;
        }

        if (Time.time < nextFlinchAt)
        {
            RecordFeedbackOnly();
            return;
        }

        bool distinctHit = RegisterMediumHit(info);
        bool canBreak = info.isCritical || (distinctHit && distinctHitCount >= 2);
        if (!canBreak || attackInProgress)
        {
            RecordFeedbackOnly();
            return;
        }

        if (ApplyFlinch(info))
        {
            nextFlinchAt = Time.time + MediumFlinchCooldown;
            ClearMediumWindow();
        }
        else
            RecordFeedbackOnly();
    }

    private bool RegisterMediumHit(DamageInfo info)
    {
        float now = Time.time;
        if (distinctHitCount > 0 && now - lastDistinctHitAt > MediumHitWindow)
            ClearMediumWindow();

        int sourceId = info.source != null ? info.source.GetInstanceID() : 0;
        if (info.sourceAttackSequenceId != 0)
        {
            long key = ((long)sourceId << 32) ^ (uint)info.sourceAttackSequenceId;
            if (!mediumSequences.Add(key))
                return false;
        }
        else
        {
            if (missingSequenceTimes.TryGetValue(sourceId, out float lastTime)
                && now - lastTime < MissingSequenceDuplicateWindow)
                return false;
            missingSequenceTimes[sourceId] = now;
        }

        distinctHitCount++;
        lastDistinctHitAt = now;
        return true;
    }

    private bool ApplyFlinch(DamageInfo info)
    {
        if (reaction != null && reaction.HitWeightProfile != null)
        {
            if (!reaction.TryApplyWeightedHit(info))
                return false;
        }
        else if (reaction != null)
        {
            if (info.knockback > 0f)
            {
                Vector3 direction = info.direction;
                if (direction.sqrMagnitude < 0.0001f && info.source != null)
                    direction = transform.position - info.source.transform.position;
                reaction.ApplyKnockback(direction, info.knockback);
                reaction.ExtendKnockbackReaction(info.hitReaction.overridesTargetDefaults
                    ? info.hitReaction.knockbackReactionDuration : 0.4f);
            }
            else
                reaction.ApplyHitStun(info.hitReaction.overridesTargetDefaults
                    ? info.hitReaction.hitStunDuration : 0.4f);
        }

        abilityController?.Cancel();
        if (abilityController == null)
            melee?.CancelAttack();
        animationBridge?.PlayResolvedHit(info);
        LastOutcome = EnemyHitResponseOutcome.Flinch;
        FlinchCount++;
        return true;
    }

    private void RecordFeedbackOnly()
    {
        LastOutcome = EnemyHitResponseOutcome.FeedbackOnly;
        FeedbackOnlyCount++;
    }

    private void ClearMediumWindow()
    {
        mediumSequences.Clear();
        missingSequenceTimes.Clear();
        distinctHitCount = 0;
        lastDistinctHitAt = 0f;
    }

    private void ResolveReferences()
    {
        if (health == null) health = GetComponent<CombatHealth>();
        if (rank == null) rank = GetComponent<EnemyRank>();
        if (reaction == null) reaction = GetComponent<EnemyMovementReaction>();
        if (animationBridge == null) animationBridge = GetComponent<EnemyAnimationBridge>();
        if (abilityController == null) abilityController = GetComponent<EnemyAbilityController>();
        if (melee == null) melee = GetComponent<EnemyMeleeAttackController>();
        if (defense == null) defense = GetComponent<EnemyDefenseController>();
    }
}
