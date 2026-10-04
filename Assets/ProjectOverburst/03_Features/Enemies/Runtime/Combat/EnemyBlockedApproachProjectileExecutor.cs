using UnityEngine;

// Uses the existing shot executor; this adapter owns only fallback eligibility.
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyThemeSpecialExecutor))]
public sealed class EnemyBlockedApproachProjectileExecutor : EnemyAbilityExecutor
{
    [SerializeField] private EnemyThemeSpecialExecutor projectileExecutor;
    private EnemyActor actor;
    private EnemyMovementReaction reaction;
    private Vector3 samplePosition;
    private Transform sampledTarget;
    private float nextSample, blockedSeconds, permittedUntil;
    public float BlockedApproachSeconds => blockedSeconds;
    public override bool IsExecuting => projectileExecutor != null && projectileExecutor.IsWeakProjectileActionExecuting;
    public void Configure(EnemyThemeSpecialExecutor executor) => projectileExecutor = executor;
    private void Awake()
    {
        actor = GetComponent<EnemyActor>(); reaction = GetComponent<EnemyMovementReaction>();
        if (projectileExecutor == null) projectileExecutor = GetComponent<EnemyThemeSpecialExecutor>();
    }
    private void OnEnable() => ResetTracking();
    private void OnDisable() => ResetTracking();
    private void ResetTracking()
    { sampledTarget = null; nextSample = 0f; blockedSeconds = 0f; permittedUntil = 0f; samplePosition = transform.position; }
    private bool CanObserve => actor != null && actor.IsLeased && actor.Health != null && !actor.Health.IsDead
        && actor.Movement != null && !actor.Movement.IsStatusMovementLocked && !actor.Movement.IsActionLocked
        && (reaction == null || !reaction.BlocksAttack)
        && !actor.AbilityController.IsExecuting && !actor.AnimationBridge.BlocksAttackStart;
    private void FixedUpdate()
    {
        Transform target = actor != null && actor.AI != null ? actor.AI.Target : null;
        if (!CanObserve || target == null) { ResetTracking(); return; }
        if (sampledTarget != target) { ResetTracking(); sampledTarget = target; nextSample = Time.time + .4f; }
        bool approach = actor.AI.CurrentStateName == "Chase" && actor.Movement.HasDestination
            && actor.Movement.IsFacingForAttack(actor.AbilityController.ResolveAimPosition(target));
        if (!approach)
        {
            blockedSeconds = 0f; samplePosition = transform.position; nextSample = Time.time + .4f;
            return; // Keep the short permission latch while Chase hands over to Attack.
        }
        if (Time.time < nextSample) return;
        Vector3 travel = transform.position - samplePosition; travel.y = 0f;
        blockedSeconds = travel.magnitude <= .05f ? blockedSeconds + .4f : 0f;
        samplePosition = transform.position; nextSample = Time.time + .4f;
        if (blockedSeconds >= 1.199f) permittedUntil = Time.time + .6f;
        else permittedUntil = 0f;
    }
    public override bool Supports(EnemyAbilityDefinition ability) => ability != null
        && ability.ExecutionMode == EnemyAbilityExecutionMode.Projectile && ability.WeakAttackExecution?.RequiresBlockedApproach == true;
    public bool HasFallbackPermission(EnemyAbilityDefinition ability)
    {
        if (!Supports(ability) || Time.time >= permittedUntil || !CanObserve || actor.AI?.Target == null
            || !actor.AbilityController.IsCooldownReady(ability)) return false;
        Transform target = actor.AI.Target;
        Vector3 point = actor.AbilityController.ResolveAimPosition(target), delta = point - transform.position; delta.y = 0f;
        if (!projectileExecutor.HasPositioningLine(target, transform.position, point)) return false;
        var set = actor.Definition.AbilitySet;
        for (int i = 0; i < set.Count; i++)
        {
            var close = set.GetAbility(i);
            if (close == null || close.ExecutionMode == EnemyAbilityExecutionMode.Projectile || !close.IsValid
                || !actor.AbilityController.IsCooldownReady(close)
                || close.IsTelegraphedStrongAttack && actor.AbilityController.IsStrongAttackLocked) continue;
            if (EnemyAttackThreatGeometry.MatchesUseConditions(actor, close, delta.magnitude, actor.Health.NormalizedHp)) return false;
        }
        return true;
    }
    public override bool CanStart(EnemyAbilityDefinition ability, Transform target)
        => target == actor?.AI?.Target && HasFallbackPermission(ability) && projectileExecutor.CanStart(ability, target);
    public override bool TryStart(EnemyAbilityDefinition ability, int abilityIndex, Transform target)
    {
        if (!CanStart(ability, target) || !projectileExecutor.TryStart(ability, abilityIndex, target)) return false;
        permittedUntil = 0f; blockedSeconds = 0f; return true;
    }
    public override float ResolveCooldown(float duration) => projectileExecutor.ResolveCooldown(duration);
    public override void Cancel() { projectileExecutor?.Cancel(); ResetTracking(); }
    public override void ResetForReuse() { projectileExecutor?.ResetForReuse(); ResetTracking(); }
}
