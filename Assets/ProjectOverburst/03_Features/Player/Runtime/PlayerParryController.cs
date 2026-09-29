using System.Collections.Generic;
using UnityEngine;

// One window belongs to the accepted heavy action, independently of energy and ordinary hit stun.
[DisallowMultipleComponent]
public sealed class PlayerParryController : MonoBehaviour
{
    private MeleeRuntime melee;
    private CombatHealth health;
    private CombatTarget playerTarget;
    private ParrySuccessVfx successVfx;
    private readonly List<EnemyRank> activeEnemies = new List<EnemyRank>(160);
    private readonly List<EnemyActor> eligibleEnemies = new List<EnemyActor>(12);
    private int actionId;
    private float windowEndsAt;
    private bool windowOpen;
    public int SuccessCount { get; private set; }
    public bool IsWindowOpen => windowOpen && Time.unscaledTime <= windowEndsAt;
    public float RemainingWindow => IsWindowOpen ? Mathf.Max(0f, windowEndsAt - Time.unscaledTime) : 0f;

    private void Awake()
    {
        melee = GetComponent<MeleeRuntime>();
        health = GetComponent<CombatHealth>();
        playerTarget = GetComponent<CombatTarget>();
        successVfx = GetComponent<ParrySuccessVfx>();
        if (successVfx == null) successVfx = gameObject.AddComponent<ParrySuccessVfx>();
    }
    public void OpenForHeavy(int acceptedActionId)
    {
        if (acceptedActionId <= 0 || actionId == acceptedActionId) return;
        actionId = acceptedActionId; windowEndsAt = Time.unscaledTime + .40f; windowOpen = true;
        TryParryThreats();
    }
    public void CloseWindow() => windowOpen = false;
    private void Update()
    {
        if (!windowOpen) return;
        if (health == null || health.IsDead || melee == null || !melee.IsHeavyAttackInProgress) { CloseWindow(); return; }
        if (GameplayInputBlocker.IsGameplayInputBlocked || Time.timeScale <= 0f)
            windowEndsAt += Time.unscaledDeltaTime;
        else if (Time.unscaledTime > windowEndsAt) CloseWindow();
        else TryParryThreats();
    }
    private void TryParryThreats()
    {
        if (!IsWindowOpen || playerTarget == null || !playerTarget.IsAlive
            || melee == null || !melee.IsHeavyAttackInProgress
            || GameplayInputBlocker.IsGameplayInputBlocked) return;

        activeEnemies.Clear();
        eligibleEnemies.Clear();
        EnemyRank.CollectActive(activeEnemies);
        for (int i = 0; i < activeEnemies.Count; i++)
        {
            EnemyRank rank = activeEnemies[i];
            if (rank == null || rank.GradeType == EnemyGradeType.Boss) continue;
            EnemyActor enemy = rank.GetComponent<EnemyActor>();
            if (enemy == null || !enemy.IsLeased || enemy.Health == null || enemy.Health.IsDead
                || enemy.AbilityController == null) continue;
            if (enemy.AbilityController.IsParryThreatTo(playerTarget))
                eligibleEnemies.Add(enemy);
        }

        if (eligibleEnemies.Count == 0) return;
        CloseWindow();
        for (int i = 0; i < eligibleEnemies.Count; i++)
        {
            successVfx?.EmitEnemy(eligibleEnemies[i]);
            CancelAndStun(eligibleEnemies[i]);
        }
        PlaySuccess();
    }
    private static void CancelAndStun(EnemyActor enemy)
    {
        EnemyRank rank = enemy.GetComponent<EnemyRank>();
        EnemyAbilityDefinition ability = enemy.AbilityController.LastCommittedAbility;
        float progress = 0f;
        bool hasPose = ability != null && enemy.AnimationBridge != null
            && enemy.AnimationBridge.TryGetAttackNormalizedTime(
                ability.AnimatorTrigger, out progress);
        enemy.AbilityController.Cancel();
        enemy.GetComponent<EnemyMovementReaction>()?.ApplyParryStun(
            (rank != null && rank.Rank == EnemyRankType.Elite ? .8f : 1.2f)
            + (hasPose ? .19f : 0f));
        if (hasPose) enemy.AnimationBridge.PlayParryRewind(ability.AnimatorTrigger, progress);
        else enemy.AnimationBridge?.PlayHit();
    }
    private void PlaySuccess()
    {
        SuccessCount++;
        successVfx?.Pulse();
        CombatActionSfxService.PlayParrySuccess(transform.position);
        // 슬로우는 앞의 패링 히트스톱을 지나서도 이어진다. 두 요청은 종류별로 따로 유지된다.
        OverburstTimeEffectArbiter.Request(this, OverburstTimeEffectKind.ParrySlow, .75f, .21f);
        OverburstTimeEffectArbiter.Request(this, OverburstTimeEffectKind.ParryHitStop, .01f, .06f);
    }
    public bool TryCancelDamage(DamageInfo info)
    {
        if (!IsWindowOpen || !isActiveAndEnabled || health == null || health.IsDead
            || melee == null || !melee.IsHeavyAttackInProgress
            || GameplayInputBlocker.IsGameplayInputBlocked || Time.timeScale <= 0f
            || info.isDamageOverTime || !info.triggersOnHitEffects || info.damage <= 0f
            || info.enemyAbility == null || !info.enemyAbility.IsParryable || info.source == null) return false;
        var enemy = info.source.GetComponentInParent<EnemyActor>();
        if (enemy == null || !enemy.IsLeased || enemy.Health.IsDead
            || !enemy.AbilityController.IsExecuting
            || enemy.AbilityController.LastCommittedAbility != info.enemyAbility) return false;
        var rank = enemy.GetComponent<EnemyRank>();
        if (rank != null && rank.GradeType == EnemyGradeType.Boss) return false;
        CloseWindow();
        successVfx?.EmitEnemy(enemy);
        CancelAndStun(enemy);
        PlaySuccess();
        return true;
    }
    private void OnDisable() { CloseWindow(); OverburstTimeEffectArbiter.ClearOwner(this); }
}
