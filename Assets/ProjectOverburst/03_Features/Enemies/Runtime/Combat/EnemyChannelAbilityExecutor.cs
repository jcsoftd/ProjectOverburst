using System.Collections;
using UnityEngine;

// A stationary sustained weak attack. The native clip clock owns its (at most three) damage pulses.
// It uses the normal ability selection, damage budget, action lock and pool lifecycle.
[DisallowMultipleComponent]
public sealed class EnemyChannelAbilityExecutor : EnemyAbilityExecutor
{
    [SerializeField] private EnemyAbilityDefinition channelAbility;
    [SerializeField] private Transform muzzle;
    [SerializeField, Min(.01f)] private float castRadius = .12f;
    private EnemyActor actor;
    private EnemyMovementReaction reaction;
    private Coroutine routine;
    private LineRenderer beam;
    private Material beamMaterial;
    private Vector3 direction, endpoint;
    private bool emitting;
    private uint lease;
    private int sequence;
    private readonly RaycastHit[] hits = new RaycastHit[24];
    public override bool IsExecuting => routine != null;
    public bool IsEmitting => emitting;
    public int PulseCount { get; private set; }
    public int DamageCount { get; private set; }
    private int Mask => ~((1 << LayerMask.NameToLayer("Enemy")) | (1 << LayerMask.NameToLayer("Ignore Raycast")));
    private Vector3 Origin => muzzle != null ? muzzle.position : transform.position + Vector3.up * .3f;
    private void Resolve() { if (actor == null) actor = GetComponent<EnemyActor>(); if (reaction == null) reaction = GetComponent<EnemyMovementReaction>(); }
    private void Awake() { Resolve(); }
    private void OnEnable()
    {
        Resolve();
        if (actor != null && actor.Health != null)
        {
            actor.Health.OnDead += Damaged;
            if (GetComponent<EnemyHitResponseCoordinator>() == null) actor.Health.OnDamaged += Damaged;
        }
        if (reaction != null) reaction.ReactionStarted += Reacted;
    }
    private void OnDisable()
    {
        if (actor != null && actor.Health != null) { actor.Health.OnDead -= Damaged; actor.Health.OnDamaged -= Damaged; }
        if (reaction != null) reaction.ReactionStarted -= Reacted;
        Cancel();
    }
    private void OnDestroy() { if (beamMaterial != null) Destroy(beamMaterial); }
    private void Damaged(CombatHealth source, DamageInfo info)
    { if (source.IsDead || !info.isDamageOverTime && info.triggersOnHitEffects && !info.suppressRepeatedAttackReaction) Cancel(); }
    private void Reacted() { if (reaction == null || reaction.BlocksAttack) Cancel(); }
    private bool Usable => actor != null && actor.IsLeased && actor.Health != null && !actor.Health.IsDead
        && actor.Movement != null && !actor.Movement.IsStatusMovementLocked && (reaction == null || !reaction.BlocksAttack);
    public override bool Supports(EnemyAbilityDefinition ability) => ability != null && ability == channelAbility
        && ability.ExecutionMode == EnemyAbilityExecutionMode.Zone && ability.HasWeakAttackExecution && !ability.IsTelegraphedStrongAttack
        && ability.HitCount > 0 && ability.HitCount <= 3;
    public override bool CanStart(EnemyAbilityDefinition ability, Transform target)
    {
        Resolve();
        if (!Supports(ability) || target == null || IsExecuting || !Usable || actor.Movement.IsActionLocked || actor.AnimationBridge.BlocksAttackStart) return false;
        Vector3 point = actor.AbilityController.ResolveAimPosition(target), delta = point - transform.position; delta.y = 0;
        if (!EnemyAttackThreatGeometry.MatchesUseConditions(actor, ability, delta.magnitude, actor.Health.NormalizedHp)
            || !actor.Movement.IsFacingForAttack(point)) return false;
        Vector3 aim = point + Vector3.up * .8f - Origin;
        int nearest = Nearest(Origin, aim.normalized, aim.magnitude);
        return nearest < 0 || hits[nearest].collider.GetComponentInParent<CombatHealth>() == target.GetComponentInParent<CombatHealth>();
    }
    public override bool TryStart(EnemyAbilityDefinition ability, int abilityIndex, Transform target)
    {
        if (!CanStart(ability, target)) return false;
        int level = GetComponent<EnemyRank>()?.Level ?? 1;
        if (!ability.TryResolveWeakDamageBudget(level, actor.RuntimeStats.DamageMultiplier, out var budget)) return false;
        reaction?.PrepareForAttack(); lease = actor.LeaseVersion; sequence = EnemyAttackSequence.Next();
        direction = (actor.AbilityController.ResolveAimPosition(target) + Vector3.up * .8f - Origin).normalized;
        routine = StartCoroutine(Execute(ability, budget, target)); return true;
    }
    public override float ResolveCooldown(float duration) { Resolve(); return actor != null ? actor.Melee.ResolveAbilityCooldown(duration) : duration; }
    private IEnumerator Execute(EnemyAbilityDefinition ability, EnemyWeakAttackDamageBudget budget, Transform target)
    {
        float speed = actor.Melee.AbilityAnimationSpeed, started = Time.time;
        float executionDuration = ability.ResolveExecutionDuration(speed), windup = ability.ResolveWindupDelay(speed);
        actor.Movement.ApplyActionLock(executionDuration);
        while (Time.time < started + windup)
        {
            if (!Usable || actor.LeaseVersion != lease || target == null) { EndEmission(); routine = null; yield break; }
            yield return null;
        }
        actor.AnimationBridge.SetAttackAnimSpeed(ability.ResolvePhaseAnimationSpeed(0f, speed));
        actor.AnimationBridge.PlayAttack(ability.AnimatorTrigger);
        bool entered = false; int next = 0; float elapsed = 0;
        float duration = ability.ResolvePacedTime(1f, speed);
        while (elapsed < duration + .4f && Usable && actor.LeaseVersion == lease && target != null)
        {
            if (actor.AnimationBridge.TryGetAttackNormalizedTime(ability.AnimatorTrigger, out float progress))
            {
                entered = true; actor.AnimationBridge.SetAttackAnimSpeed(ability.ResolvePhaseAnimationSpeed(progress, speed));
                while (next < ability.HitCount && progress >= ability.GetHitNormalizedTime(next))
                {
                    Pulse(ability, budget.ForPhase(next), next);
                    actor.AbilityController.NotifyAbilityImpact(ability, next); next++;
                }
                emitting = progress >= ability.HitNormalizedTime && progress <= ability.GetHitNormalizedTime(ability.HitCount - 1) + .05f;
                if (emitting) { EnsureBeam(); UpdateBeam(ability); }
                else if (beam != null) beam.enabled = false;
            }
            else if (entered || elapsed > .4f) break;
            elapsed += Time.fixedDeltaTime; yield return new WaitForFixedUpdate();
        }
        EndEmission();
        while (Time.time < started + executionDuration && Usable && actor.LeaseVersion == lease) yield return null;
        routine = null;
    }
    private int Nearest(Vector3 origin, Vector3 ray, float distance)
    {
        int count = Physics.SphereCastNonAlloc(origin, castRadius, ray, hits, Mathf.Max(.01f, distance), Mask, QueryTriggerInteraction.Ignore);
        int nearest = -1;
        for (int i = 0; i < count; i++) if (nearest < 0 || hits[i].distance < hits[nearest].distance) nearest = i;
        return nearest;
    }
    private void Pulse(EnemyAbilityDefinition ability, float damage, int phase)
    {
        PulseCount++;
        int nearest = Nearest(Origin, direction, ability.Range);
        endpoint = nearest >= 0 ? hits[nearest].point : Origin + direction * ability.Range;
        if (nearest < 0) return;
        var target = CombatTarget.Resolve(hits[nearest].collider);
        if (target == null || target.DamageReceiver == null || !CombatTargetFilter.CanDamage(GetComponent<CombatTarget>(), target)) return;
        target.DamageReceiver.TakeDamage(new DamageInfo(damage, endpoint, gameObject, direction,
            sourceAttackSequenceId: sequence, sourceAttackPhaseIndex: phase, enemyAbility: ability));
        DamageCount++; MeleeElementHitVfxService.TryPlay(WeaponElement.Electric, endpoint);
    }
    private void EnsureBeam()
    {
        if (beam != null) return;
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) return;
        beamMaterial = new Material(shader); beamMaterial.SetColor("_BaseColor", new Color(.15f, .8f, 1f));
        var visual = new GameObject("Reusable weak electric channel"); visual.transform.SetParent(transform, false);
        beam = visual.AddComponent<LineRenderer>(); beam.sharedMaterial = beamMaterial; beam.useWorldSpace = true;
        beam.positionCount = 9; beam.startWidth = .025f; beam.endWidth = .02f;
        beam.startColor = new Color(.4f, .9f, 1f); beam.endColor = new Color(.2f, .6f, 1f);
        beam.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; beam.receiveShadows = false;
    }
    private void UpdateBeam(EnemyAbilityDefinition ability)
    {
        if (beam == null) return;
        int nearest = Nearest(Origin, direction, ability.Range);
        endpoint = nearest >= 0 ? hits[nearest].point : Origin + direction * ability.Range;
        beam.enabled = true; Vector3 side = Vector3.Cross(direction, Vector3.up).normalized;
        for (int i = 0; i < beam.positionCount; i++)
        {
            float t = i / (float)(beam.positionCount - 1), jitter = i == 0 || i == beam.positionCount - 1 ? 0 : Mathf.Sin(Time.time * 53f + i * 2.3f) * .035f;
            beam.SetPosition(i, Vector3.Lerp(Origin, endpoint, t) + side * jitter);
        }
    }
    private void EndEmission() { emitting = false; if (beam != null) beam.enabled = false; }
    public override void Cancel()
    {
        if (routine != null) { StopCoroutine(routine); routine = null; actor?.Movement?.CancelActionLock(); }
        actor?.Movement?.ClearAttackDisplacement(); EndEmission(); direction = Vector3.zero;
    }
    public override void ResetForReuse() { Resolve(); Cancel(); PulseCount = 0; DamageCount = 0; sequence = 0; lease = 0; }
}
