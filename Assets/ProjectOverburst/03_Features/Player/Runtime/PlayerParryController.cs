using System.Collections.Generic;
using UnityEngine;

// One window belongs to the accepted heavy action, independently of energy and ordinary hit stun.
// 2026-09-30 핵앤슬래시 기준: 판정은 쉽게(창 0.6초, 첫 성공 후에도 창 유지), 피드백은 누르는 즉시 강하게.
// 같은 프레임에 패링한 적들을 한 번의 이벤트로 묶고, 마리 수와 강공 여부로 연출 등급을 정한다.
[DisallowMultipleComponent]
public sealed class PlayerParryController : MonoBehaviour
{
    private const float WindowSeconds = .60f;
    private const float SlowScale = .5f;
    private const float SlowCooldown = 1.5f;
    private const float PushBackDistance = .4f;

    private MeleeRuntime melee;
    private CombatHealth health;
    private CombatTarget playerTarget;
    private ParrySuccessVfx successVfx;
    private readonly List<EnemyRank> activeEnemies = new List<EnemyRank>(160);
    private readonly List<EnemyActor> eligibleEnemies = new List<EnemyActor>(12);
    private readonly HashSet<EnemyActor> parriedThisAction = new HashSet<EnemyActor>();
    private int actionId;
    private int chainIndex;
    private float windowEndsAt;
    private float nextSlowAt;
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
    private void OnEnable()
    {
        if (playerTarget == null) playerTarget = GetComponent<CombatTarget>();
        if (playerTarget != null) EnemyStrongAttackWarning.PlayerTarget = playerTarget;
    }
    public void OpenForHeavy(int acceptedActionId)
    {
        if (acceptedActionId <= 0 || actionId == acceptedActionId) return;
        actionId = acceptedActionId; windowEndsAt = Time.unscaledTime + WindowSeconds; windowOpen = true;
        parriedThisAction.Clear();
        chainIndex = 0;
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
            if (enemy == null || parriedThisAction.Contains(enemy) || !enemy.IsLeased
                || enemy.Health == null || enemy.Health.IsDead || enemy.AbilityController == null) continue;
            if (enemy.AbilityController.IsParryThreatTo(playerTarget))
                eligibleEnemies.Add(enemy);
        }

        if (eligibleEnemies.Count == 0) return;
        ResolveParry(eligibleEnemies);
    }
    // Snapshot the threat list first, then cancel every target in it, then play one shared event.
    private void ResolveParry(List<EnemyActor> enemies)
    {
        bool anyStrong = false;
        Vector3 center = Vector3.zero;
        Vector3 playerCenter = playerTarget != null ? playerTarget.CurrentVolume.Center
            : transform.position + Vector3.up;
        for (int i = 0; i < enemies.Count; i++)
        {
            EnemyActor enemy = enemies[i];
            EnemyAbilityDefinition ability = enemy.AbilityController.LastCommittedAbility;
            anyStrong |= ability != null && ability.IsTelegraphedStrongAttack;
            center += ContactPoint(enemy, playerCenter);
            parriedThisAction.Add(enemy);
        }
        center /= enemies.Count;

        for (int i = 0; i < enemies.Count; i++)
        {
            successVfx?.EmitEnemy(enemies[i]);
            enemies[i].GetComponent<HitFlashFeedback>()?.FlashOnce();
            CancelAndStun(enemies[i], transform.position);
        }
        PlaySuccess(center, enemies.Count, anyStrong);
        chainIndex++;
    }
    private static Vector3 ContactPoint(EnemyActor enemy, Vector3 playerCenter)
    {
        CombatTarget target = enemy.GetComponent<CombatTarget>();
        if (target == null) return enemy.transform.position + Vector3.up;
        CombatTargetVolume volume = target.CurrentVolume;
        Vector3 toPlayer = playerCenter - volume.Center; toPlayer.y = 0f;
        return toPlayer.sqrMagnitude > .0001f
            ? volume.Center + toPlayer.normalized * volume.Radius : volume.Center;
    }
    private static void CancelAndStun(EnemyActor enemy, Vector3 playerPosition)
    {
        EnemyRank rank = enemy.GetComponent<EnemyRank>();
        EnemyAbilityDefinition ability = enemy.AbilityController.LastCommittedAbility;
        float progress = 0f;
        bool hasPose = ability != null && enemy.AnimationBridge != null
            && enemy.AnimationBridge.TryGetAttackNormalizedTime(
                ability.AnimatorTrigger, out progress);
        enemy.AbilityController.Cancel();
        EnemyMovementReaction reaction = enemy.GetComponent<EnemyMovementReaction>();
        reaction?.ApplyParryStun(
            (rank != null && rank.Rank == EnemyRankType.Elite ? .8f : 1.2f)
            + (hasPose ? EnemyAnimationBridge.ParryRewindSeconds : 0f));
        // 튕겨나는 느낌만 준다. 멀리 밀면 이어지는 내 강공이 빗나간다.
        Vector3 away = enemy.transform.position - playerPosition; away.y = 0f;
        if (reaction != null && away.sqrMagnitude > .0001f)
            reaction.ApplyKnockbackDistance(away, PushBackDistance, false);
        if (hasPose) enemy.AnimationBridge.PlayParryRewind(ability.AnimatorTrigger, progress);
        else enemy.AnimationBridge?.PlayHit();
        EnemyParryStunIndicator.Show(enemy);
    }
    private void PlaySuccess(Vector3 center, int parriedCount, bool anyStrong)
    {
        SuccessCount++;
        ParryFeedbackService.Tier tier = ParryFeedbackService.ResolveTier(parriedCount, anyStrong);
        successVfx?.EmitCenter(center, 36);
        CombatActionSfxService.PlayParrySuccess(center);
        ParryFeedbackService.Play(center, transform.position, tier, parriedCount, chainIndex, parriedThisAction);
        // 히트스톱은 매번, 슬로우는 1.5초에 한 번만. 두 요청은 종류별로 따로 유지된다.
        if (Time.unscaledTime >= nextSlowAt)
        {
            nextSlowAt = Time.unscaledTime + SlowCooldown;
            OverburstTimeEffectArbiter.Request(this, OverburstTimeEffectKind.ParrySlow, SlowScale, tier.HitStop + tier.Slow);
        }
        OverburstTimeEffectArbiter.Request(this, OverburstTimeEffectKind.ParryHitStop, .01f, tier.HitStop);
    }
    public bool TryCancelDamage(DamageInfo info)
    {
        if (!IsWindowOpen || !isActiveAndEnabled || health == null || health.IsDead
            || melee == null || !melee.IsHeavyAttackInProgress
            || GameplayInputBlocker.IsGameplayInputBlocked || Time.timeScale <= 0f
            || info.isDamageOverTime || !info.triggersOnHitEffects || info.damage <= 0f
            || info.enemyAbility == null || !info.enemyAbility.IsParryable || info.source == null) return false;
        var enemy = info.source.GetComponentInParent<EnemyActor>();
        if (enemy == null || !enemy.IsLeased || enemy.Health.IsDead) return false;
        if (parriedThisAction.Contains(enemy)) return true; // 이미 받아친 공격의 잔여 판정
        if (!enemy.AbilityController.IsExecuting
            || enemy.AbilityController.LastCommittedAbility != info.enemyAbility) return false;
        var rank = enemy.GetComponent<EnemyRank>();
        if (rank != null && rank.GradeType == EnemyGradeType.Boss) return false;
        eligibleEnemies.Clear();
        eligibleEnemies.Add(enemy);
        ResolveParry(eligibleEnemies);
        return true;
    }
    private void OnDisable()
    {
        CloseWindow();
        OverburstTimeEffectArbiter.ClearOwner(this);
        if (EnemyStrongAttackWarning.PlayerTarget == playerTarget) EnemyStrongAttackWarning.PlayerTarget = null;
    }
}
