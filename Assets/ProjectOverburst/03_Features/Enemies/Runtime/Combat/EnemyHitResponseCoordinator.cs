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
    private const float MediumFlinchCooldown = 0.12f;
    private const float EliteFlinchCooldown = 0.18f;
    private const float MissingSequenceDuplicateWindow = 0.12f;

    private readonly HashSet<(int source, int sequence, int phase)> mediumSequences = new HashSet<(int, int, int)>();
    private readonly Queue<(int source, int sequence, int phase)> recentSequences = new Queue<(int, int, int)>();
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
        recentSequences.Clear();
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
        if (grade == EnemyGradeType.Boss || Time.time < nextFlinchAt
            || (abilityController != null && abilityController.IsOrdinaryHitProtected))
        {
            RecordFeedbackOnly();
            return;
        }

        if (!RegisterMediumHit(info))
        {
            RecordFeedbackOnly();
            return;
        }

        float cooldown = grade == EnemyGradeType.Elite || grade == EnemyGradeType.GreaterElite
            ? EliteFlinchCooldown : MediumFlinchCooldown;
        if (ApplyFlinch(info, cooldown))
        {
            nextFlinchAt = Time.time + cooldown;
        }
        else
            RecordFeedbackOnly();
    }

    // Derived hits (lightning hops) never carry on-hit gameplay effects such as stacks or energy.
    // They borrow only the flinch: same boss immunity, protected attacks, freeze and cooldown rules.
    public bool TryApplyDerivedFlinch(DamageInfo info)
    {
        ResolveReferences();
        if (health == null || health.IsDead || (animationBridge != null && animationBridge.IsFrozen))
            return false;
        EnemyGradeType grade = rank != null ? rank.GradeType : EnemyGradeType.Normal;
        if (grade == EnemyGradeType.Boss || Time.time < nextFlinchAt
            || (abilityController != null && abilityController.IsOrdinaryHitProtected))
            return false;
        info.triggersOnHitEffects = true;
        info.isDamageOverTime = false;
        float cooldown = grade == EnemyGradeType.Elite || grade == EnemyGradeType.GreaterElite
            ? EliteFlinchCooldown : MediumFlinchCooldown;
        if (!ApplyFlinch(info, cooldown))
            return false;
        nextFlinchAt = Time.time + cooldown;
        return true;
    }

    private bool RegisterMediumHit(DamageInfo info)
    {
        float now = Time.time;
        if (distinctHitCount > 0 && now - lastDistinctHitAt > MediumHitWindow)
            ClearMediumWindow();

        int sourceId = info.source != null ? info.source.GetInstanceID() : 0;
        if (info.sourceAttackSequenceId != 0)
        {
            var key = (sourceId, info.sourceAttackSequenceId, info.sourceAttackPhaseIndex);
            if (!mediumSequences.Add(key))
                return false;
            recentSequences.Enqueue(key);
            if (recentSequences.Count > 128) mediumSequences.Remove(recentSequences.Dequeue());
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

    private bool ApplyFlinch(DamageInfo info, float cooldown)
    {
        if (reaction != null && reaction.HitWeightProfile != null)
        {
            if (!reaction.TryApplyWeightedHit(info, cooldown))
                return false;
        }
        else if (reaction != null)
        {
            if (info.knockback > 0f)
            {
                Vector3 direction = info.direction;
                if (direction.sqrMagnitude < 0.0001f && info.source != null)
                    direction = transform.position - info.source.transform.position;
                reaction.ApplyKnockback(direction, info.knockback, true);
                reaction.ExtendKnockbackReaction(info.hitReaction.overridesTargetDefaults
                    ? info.hitReaction.knockbackReactionDuration : 0.4f, true);
            }
            else
                reaction.ApplyHitStun(info.hitReaction.overridesTargetDefaults
                    ? info.hitReaction.hitStunDuration : 0.4f, true);
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
        recentSequences.Clear();
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
