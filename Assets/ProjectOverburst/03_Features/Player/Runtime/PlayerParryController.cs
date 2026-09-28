using UnityEngine;

// One window belongs to the accepted heavy action, independently of energy and ordinary hit stun.
[DisallowMultipleComponent]
public sealed class PlayerParryController : MonoBehaviour
{
    private MeleeRuntime melee;
    private CombatHealth health;
    private int actionId;
    private float windowEndsAt;
    private bool windowOpen;
    public int SuccessCount { get; private set; }
    public bool IsWindowOpen => windowOpen && Time.unscaledTime <= windowEndsAt;
    public float RemainingWindow => IsWindowOpen ? Mathf.Max(0f, windowEndsAt - Time.unscaledTime) : 0f;

    private void Awake() { melee = GetComponent<MeleeRuntime>(); health = GetComponent<CombatHealth>(); }
    public void OpenForHeavy(int acceptedActionId)
    {
        if (acceptedActionId <= 0 || actionId == acceptedActionId) return;
        actionId = acceptedActionId; windowEndsAt = Time.unscaledTime + .40f; windowOpen = true;
    }
    public void CloseWindow() => windowOpen = false;
    private void Update()
    {
        if (!windowOpen) return;
        if (health == null || health.IsDead || melee == null || !melee.IsHeavyAttackInProgress) { CloseWindow(); return; }
        if (GameplayInputBlocker.IsGameplayInputBlocked || Time.timeScale <= 0f)
            windowEndsAt += Time.unscaledDeltaTime;
        else if (Time.unscaledTime > windowEndsAt) CloseWindow();
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
        Vector3 approach = enemy.transform.position - transform.position; approach.y = 0f;
        if (approach.sqrMagnitude < .0001f) { approach = -info.direction; approach.y = 0f; }
        if (approach.sqrMagnitude > .0001f && Vector3.Dot(transform.forward, approach.normalized) < 0f) return false;

        CloseWindow(); SuccessCount++;
        enemy.AbilityController.Cancel();
        enemy.GetComponent<EnemyMovementReaction>()?.ApplyParryStun(rank != null && rank.Rank == EnemyRankType.Elite ? .8f : 1.2f);
        enemy.AnimationBridge.PlayHit();
        CombatActionSfxService.PlayParrySuccess(transform.position);
        // The slow request lasts through the preceding hitstop. Their durations never add on repeat requests.
        OverburstTimeEffectArbiter.Request(this, OverburstTimeEffectKind.ParrySlow, .75f, .21f);
        OverburstTimeEffectArbiter.Request(this, OverburstTimeEffectKind.ParryHitStop, .01f, .06f);
        return true;
    }
    private void OnDisable() { CloseWindow(); OverburstTimeEffectArbiter.ClearOwner(this); }
}
