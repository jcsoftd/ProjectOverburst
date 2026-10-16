using UnityEngine;

public sealed partial class EnemyAbilityController
{
    private float attackInterval, nextAttackAllowedAt;
    private bool committedAttackRunning;
    public float AttackInterval => attackInterval;
    public float RemainingAttackInterval => Mathf.Max(0f, nextAttackAllowedAt - Time.time);
    public bool IsAttackIntervalReady
    {
        get { ObserveAttackCompletion(); return !committedAttackRunning && Time.time >= nextAttackAllowedAt; }
    }

    private void ConfigureAttackInterval(float seconds)
    {
        attackInterval = float.IsNaN(seconds) || float.IsInfinity(seconds) ? 0f : Mathf.Max(0f, seconds);
        committedAttackRunning = false; nextAttackAllowedAt = 0f;
    }
    private void ObserveAttackCompletion()
    {
        if (!committedAttackRunning || ResolveIsExecuting()) return;
        committedAttackRunning = false;
        nextAttackAllowedAt = Time.time + attackInterval;
        // Medium strong attacks must leave their full cooldown after execution,
        // including interruption; long combos must not consume it while playing.
        if (UsesMediumAttackMix && lastCommittedAbility != null && lastCommittedAbility.IsTelegraphedStrongAttack)
            readyTimeByAbility[lastCommittedAbility] = Time.time + lastCommittedAbility.Cooldown;
    }
}
