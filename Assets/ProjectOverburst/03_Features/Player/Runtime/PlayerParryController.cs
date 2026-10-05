using System;
using System.Collections.Generic;
using UnityEngine;

public enum ParryGrade { Incomplete, Normal, Perfect }

// The accepted heavy action owns one grade, even if energy changes before a later contact.
[DisallowMultipleComponent]
public sealed class PlayerParryController : MonoBehaviour
{
    private const float WindowSeconds = .60f, SlowCooldown = 1.5f, PushBackDistance = .4f;
    private const float FallbackStunMedium = 1.2f, FallbackStunLarge = .8f;
    [SerializeField, Range(0f, 1f)] private float incompleteDamageFraction = .20f;
    private MeleeRuntime melee;
    private CombatHealth health;
    private CombatTarget playerTarget;
    private readonly List<EnemyRank> activeEnemies = new List<EnemyRank>(160);
    private readonly List<ParryThreat> eligible = new List<ParryThreat>(12);
    private readonly HashSet<EnemyActor> parriedThisAction = new HashSet<EnemyActor>();
    private readonly HashSet<ExecutionKey> cancelledExecutions = new HashSet<ExecutionKey>();
    private readonly List<Vector3> pendingContacts = new List<Vector3>(12);
    private int actionId, pendingStartFrame;
    private float windowEndsAt, nextSlowAt, pendingElapsed, pendingDelay;
    private bool windowOpen, feedbackScheduled, feedbackPending;
    private ParryGrade lastSlowGrade;
    private Vector3 pendingCenter;
    private int pendingCount;

    public int SuccessCount { get; private set; }
    public int ParriedAttackCount { get; private set; }
    public int FeedbackCount { get; private set; }
    public ParryGrade ActionGrade { get; private set; }
    public float ActionEnergyNormalized { get; private set; }
    public float IncompleteDamageFraction => Mathf.Clamp01(incompleteDamageFraction);
    public bool IsWindowOpen => windowOpen && Time.unscaledTime <= windowEndsAt;
    public float RemainingWindow => IsWindowOpen ? Mathf.Max(0f, windowEndsAt - Time.unscaledTime) : 0f;
    public static ParryGrade ResolveGrade(float energyNormalized) => energyNormalized >= .80f
        ? ParryGrade.Perfect : energyNormalized >= .30f ? ParryGrade.Normal : ParryGrade.Incomplete;

    private readonly struct ExecutionKey : IEquatable<ExecutionKey>
    {
        private readonly int actorId, sequence;
        private readonly uint lease;
        public ExecutionKey(EnemyActor actor, int sequence)
        { actorId = actor.GetInstanceID(); lease = actor.LeaseVersion; this.sequence = sequence; }
        public bool Equals(ExecutionKey other) => actorId == other.actorId && sequence == other.sequence && lease == other.lease;
        public override bool Equals(object other) => other is ExecutionKey key && Equals(key);
        public override int GetHashCode() => unchecked((actorId * 397 ^ (int)lease) * 397 ^ sequence);
    }
    private readonly struct ParryThreat
    {
        public readonly EnemyActor Enemy;
        public readonly DamageInfo Damage;
        public readonly ExecutionKey Key;
        public readonly Vector3 Contact;
        public ParryThreat(EnemyActor enemy, DamageInfo damage, Vector3 playerCenter)
        { Enemy = enemy; Damage = damage; Key = new ExecutionKey(enemy, damage.sourceAttackSequenceId); Contact = ContactPoint(enemy, playerCenter); }
    }
    private void Awake()
    {
        melee = GetComponent<MeleeRuntime>(); health = GetComponent<CombatHealth>();
        playerTarget = GetComponent<CombatTarget>();
        if (GetComponent<ParrySuccessVfx>() == null) gameObject.AddComponent<ParrySuccessVfx>();
    }
    private void OnEnable()
    {
        if (playerTarget == null) playerTarget = GetComponent<CombatTarget>();
        if (playerTarget != null) EnemyStrongAttackWarning.PlayerTarget = playerTarget;
    }
    public void OpenForHeavy(int acceptedActionId)
    {
        if (acceptedActionId <= 0 || actionId == acceptedActionId) return;
        actionId = acceptedActionId;
        ActionEnergyNormalized = melee != null ? melee.HeavyParryEnergyNormalized : 0f;
        ActionGrade = ResolveGrade(ActionEnergyNormalized);
        windowEndsAt = Time.unscaledTime + WindowSeconds; windowOpen = true;
        parriedThisAction.Clear(); pendingContacts.Clear(); feedbackScheduled = feedbackPending = false;
        if (cancelledExecutions.Count > 256) cancelledExecutions.Clear();
        TryParryThreats();
    }
    public void CloseWindow()
    { windowOpen = false; feedbackPending = false; pendingContacts.Clear(); }
    private void Update()
    {
        if (!windowOpen) return;
        if (health == null || health.IsDead || melee == null || !melee.IsHeavyAttackInProgress) { CloseWindow(); return; }
        if (feedbackPending && Time.frameCount > pendingStartFrame + 1 && !MeleeRuntime.IsHeavyParryClockPaused)
        {
            pendingElapsed += Time.unscaledDeltaTime;
            if (pendingElapsed >= pendingDelay) FlushPendingFeedback();
        }
        if (GameplayInputBlocker.IsGameplayInputBlocked || Time.timeScale <= 0f)
            windowEndsAt += Time.unscaledDeltaTime;
        else if (Time.unscaledTime > windowEndsAt) CloseWindow();
        else TryParryThreats();
    }
    private bool CanParryNow => IsWindowOpen && isActiveAndEnabled && playerTarget != null && playerTarget.IsAlive
        && health != null && !health.IsDead && melee != null && melee.IsHeavyAttackInProgress
        && !GameplayInputBlocker.IsGameplayInputBlocked && Time.timeScale > 0f;
    private static bool EligibleEnemy(EnemyActor enemy)
    {
        if (enemy == null || !enemy.IsLeased || enemy.Health == null || enemy.Health.IsDead || enemy.AbilityController == null) return false;
        EnemyRank rank = enemy.GetComponent<EnemyRank>();
        return rank == null || rank.GradeType != EnemyGradeType.Boss || EnemyBossCombatDirector.AcceptsParry(rank);
    }
    private void TryParryThreats()
    {
        if (!CanParryNow) return;
        activeEnemies.Clear(); eligible.Clear(); EnemyRank.CollectActive(activeEnemies);
        for (int i = 0; i < activeEnemies.Count; i++)
        {
            EnemyRank rank = activeEnemies[i];
            EnemyActor enemy = rank != null ? rank.GetComponent<EnemyActor>() : null;
            if (!EligibleEnemy(enemy) || !enemy.AbilityController.IsParryThreatTo(playerTarget)
                || !TrySnapshot(enemy, out DamageInfo info)) continue;
            var threat = new ParryThreat(enemy, info, playerTarget.CurrentVolume.Center);
            if (!cancelledExecutions.Contains(threat.Key)) eligible.Add(threat);
        }
        if (eligible.Count > 0) ResolveParry(eligible.ToArray());
    }
    private bool TrySnapshot(EnemyActor enemy, out DamageInfo info)
    {
        info = default;
        EnemyAbilityDefinition ability = enemy.AbilityController.LastCommittedAbility;
        var boss = enemy.GetComponent<EnemyBossMaterialExecutor>();
        if (boss != null && boss.CurrentMaterial?.ability == ability)
            return boss.TryGetParryDamageSnapshot(playerTarget, out info);
        var special = enemy.GetComponent<EnemyThemeSpecialExecutor>();
        if (special != null && special.IsExecuting && ability != null && ability.ExecutionMode == EnemyAbilityExecutionMode.Charge)
            return special.TryGetParryDamageSnapshot(ability, playerTarget, out info);
        return enemy.Melee != null && enemy.Melee.TryGetParryDamageSnapshot(ability, playerTarget, out info);
    }
    private void ResolveParry(ParryThreat[] threats)
    {
        ParryGrade grade = ActionGrade; int resolvedActionId = actionId;
        Vector3 center = Vector3.zero;
        EnemyActor counterTarget = null; float nearestDistance = float.PositiveInfinity;
        Vector3 playerCenter = playerTarget != null ? playerTarget.CurrentVolume.Center : transform.position;
        for (int i = 0; i < threats.Length; i++)
        {
            EnemyActor enemy = threats[i].Enemy;
            CombatTarget target = enemy.GetComponent<CombatTarget>();
            Vector3 offset = (target != null ? target.CurrentVolume.Center : enemy.transform.position) - playerCenter;
            offset.y = 0f;
            float distance = offset.sqrMagnitude;
            if (distance < nearestDistance || distance == nearestDistance
                && (counterTarget == null || enemy.GetInstanceID() < counterTarget.GetInstanceID()))
            { nearestDistance = distance; counterTarget = enemy; }
        }
        // Record every execution before cancelling any of them or emitting HP events.
        for (int i = 0; i < threats.Length; i++)
        { cancelledExecutions.Add(threats[i].Key); parriedThisAction.Add(threats[i].Enemy); center += threats[i].Contact; }
        center /= threats.Length;
        for (int i = 0; i < threats.Length; i++)
        {
            EnemyActor enemy = threats[i].Enemy;
            enemy.GetComponent<HitFlashFeedback>()?.FlashOnce();
            CancelAndReact(enemy, transform.position, grade);
        }
        melee?.NotifyHeavyParried(resolvedActionId, grade, counterTarget);
        SuccessCount++; ParriedAttackCount += threats.Length;
        if (grade == ParryGrade.Incomplete)
            for (int i = 0; i < threats.Length; i++) health?.TakeParryResidualDamage(threats[i].Damage, IncompleteDamageFraction);
        // A confirmed parry still presents its contact if the parry-only fallback closed the action.
        if (health == null || health.IsDead || actionId != resolvedActionId) return;
        if (!feedbackScheduled)
        {
            feedbackScheduled = true; pendingCenter = center; pendingCount = threats.Length;
            pendingElapsed = 0f; pendingStartFrame = Time.frameCount;
            pendingDelay = melee != null && melee.IsHeavyParryMotionActive ? melee.HeavyParryContactDelay : 0f;
            feedbackPending = true;
            if (pendingDelay <= 0f) FlushPendingFeedback();
        }
        else if (feedbackPending)
        {
            // Later threats do not flush the first contact's presentation early.
            for (int i = 0; i < threats.Length; i++) pendingContacts.Add(threats[i].Contact);
        }
        else ParryFeedbackService.PlayContact(center, grade);
    }
    private static Vector3 ContactPoint(EnemyActor enemy, Vector3 playerCenter)
    {
        CombatTarget target = enemy.GetComponent<CombatTarget>();
        if (target == null) return enemy.transform.position + Vector3.up;
        CombatTargetVolume volume = target.CurrentVolume;
        Vector3 direction = playerCenter - volume.Center; direction.y = 0f;
        return direction.sqrMagnitude > .0001f ? volume.Center + direction.normalized * volume.Radius : volume.Center;
    }
    private static void CancelAndReact(EnemyActor enemy, Vector3 playerPosition, ParryGrade grade)
    {
        enemy.AbilityController.Cancel();
        var boss = enemy.GetComponent<EnemyBossCombatDirector>();
        if (boss != null) { boss.NotifyParried(grade); if (grade == ParryGrade.Perfect) enemy.AnimationBridge?.PlayHit(); return; }
        if (grade == ParryGrade.Incomplete) return;
        EnemyRank rank = enemy.GetComponent<EnemyRank>();
        EnemyAnimationBridge bridge = enemy.AnimationBridge;
        var reaction = enemy.GetComponent<EnemyMovementReaction>();
        if (grade == ParryGrade.Normal)
        {
            // A forced brief reaction blocks elites without granting the perfect-parry stun reward.
            if (reaction != null && reaction.IsParryStunned) return;
            if (bridge != null && bridge.TryPlayNormalParryReaction(out float reactionSeconds))
                reaction?.ApplyHitStun(reactionSeconds);
            else { reaction?.ApplyHitStun(.35f); bridge?.PlayHit(); }
            return;
        }
        if (bridge != null && bridge.TryPlayParryStun(out float seconds)) reaction?.ApplyParryStun(seconds);
        else { reaction?.ApplyParryStun(rank != null && rank.Rank == EnemyRankType.Elite ? FallbackStunLarge : FallbackStunMedium); bridge?.PlayHit(); }
        Vector3 away = enemy.transform.position - playerPosition; away.y = 0f;
        if (reaction != null && away.sqrMagnitude > .0001f) reaction.ApplyKnockbackDistance(away, PushBackDistance, false);
        EnemyParryStunIndicator.Show(enemy);
    }
    private void FlushPendingFeedback()
    {
        if (!feedbackPending) return;
        feedbackPending = false; FeedbackCount++;
        ShowGradeLabel();
        ParryFeedbackService.Tier tier = ParryFeedbackService.ResolveTier(ActionGrade);
        CombatActionSfxService.PlayParrySuccess(pendingCenter);
        ParryFeedbackService.Play(pendingCenter, transform.position, tier, pendingCount, 0, parriedThisAction);
        if (Time.unscaledTime >= nextSlowAt || ActionGrade > lastSlowGrade)
        {
            nextSlowAt = Time.unscaledTime + SlowCooldown; lastSlowGrade = ActionGrade;
            OverburstTimeEffectArbiter.Request(this, OverburstTimeEffectKind.ParrySlow, tier.SlowScale,
                tier.HitStop + tier.Slow + tier.SlowRecover, tier.SlowRecover);
        }
        OverburstTimeEffectArbiter.Request(this, OverburstTimeEffectKind.ParryHitStop, .01f, tier.HitStop);
        for (int i = 0; i < pendingContacts.Count; i++) ParryFeedbackService.PlayContact(pendingContacts[i], ActionGrade);
        pendingContacts.Clear();
    }
    public void CompleteParryContact(int acceptedActionId)
    {
        if (acceptedActionId == actionId && feedbackPending) FlushPendingFeedback();
    }
    private void ShowGradeLabel()
    {
        DamageNumberKind kind = ActionGrade == ParryGrade.Perfect ? DamageNumberKind.PerfectParry
            : ActionGrade == ParryGrade.Normal ? DamageNumberKind.Parry : DamageNumberKind.IncompleteParry;
        Vector3 head = transform.position + Vector3.up * 2.1f;
        if (playerTarget != null)
        {
            CombatTargetVolume volume = playerTarget.CurrentVolume;
            head = volume.Center + Vector3.up * (volume.HalfHeight + .35f);
        }
        DamageNumberSpawner.SpawnParry(head, kind);
    }
    public bool TryCancelDamage(DamageInfo info)
    {
        if (!isActiveAndEnabled || info.isDamageOverTime || !info.triggersOnHitEffects || info.damage <= 0f
            || info.enemyAbility == null || !info.enemyAbility.IsParryable || info.source == null) return false;
        var enemy = info.source.GetComponentInParent<EnemyActor>();
        if (enemy == null || !enemy.IsLeased || enemy.Health == null || enemy.Health.IsDead || info.sourceAttackSequenceId <= 0) return false;
        var key = new ExecutionKey(enemy, info.sourceAttackSequenceId);
        if (cancelledExecutions.Contains(key)) return true;
        if (!EligibleEnemy(enemy) || !CanParryNow || !enemy.AbilityController.IsExecuting || enemy.AbilityController.LastCommittedAbility != info.enemyAbility) return false;
        var boss = enemy.GetComponent<EnemyBossMaterialExecutor>();
        var special = enemy.GetComponent<EnemyThemeSpecialExecutor>();
        int activeSequence = boss != null && boss.CurrentMaterial?.ability == info.enemyAbility ? boss.ActiveAttackSequenceId
            : special != null && special.IsExecuting && info.enemyAbility.ExecutionMode == EnemyAbilityExecutionMode.Charge ? special.ActiveAttackSequenceId
            : enemy.Melee != null ? enemy.Melee.ActiveAttackSequenceId : 0;
        if (activeSequence != info.sourceAttackSequenceId) return false;
        // NotifyAbilityImpact may have advanced the source phase; keep this original hit.
        eligible.Clear(); eligible.Add(new ParryThreat(enemy, info, playerTarget.CurrentVolume.Center));
        ResolveParry(eligible.ToArray());
        return true;
    }
    private void OnDisable()
    {
        CloseWindow(); cancelledExecutions.Clear(); OverburstTimeEffectArbiter.ClearOwner(this);
        if (EnemyStrongAttackWarning.PlayerTarget == playerTarget) EnemyStrongAttackWarning.PlayerTarget = null;
    }
}
