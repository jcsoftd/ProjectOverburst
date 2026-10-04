using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// V3 weak shots retain every authored release. A missed projectile never spends
// another projectile's damage, and flight remains owned by the current Actor lease.
public sealed partial class EnemyThemeSpecialExecutor
{
    private sealed class WeakProjectile
    {
        public GameObject gameObject;
        public EnemyBioProjectileVisual visual;
        public EnemyAbilityDefinition ability;
        public BloodHitProfile tint;
        public Vector3 position, direction;
        public float remaining, damage, scale;
        public uint lease;
        public int sequence, phase;
        public bool active, electric;
    }
    private readonly List<WeakProjectile> weakProjectiles = new List<WeakProjectile>();
    private EnemyWeakAttackReactionScope weakProjectileReaction;
    private bool preserveWeakFlightDuringAIActionCleanup;
    public bool IsWeakProjectileActionExecuting => routine != null;
    public bool BeginCompletedWeakActionCleanup()
    {
        if (preserveWeakFlightDuringAIActionCleanup || routine != null || boltFlying || !HasWeakProjectiles
            || !isActiveAndEnabled || !Usable() || actor.Movement.IsActionLocked
            || actor.AnimationBridge.BlocksAttackStart || actor.AbilityController.IsExecuting) return false;
        preserveWeakFlightDuringAIActionCleanup = true;
        return true;
    }
    public void EndCompletedWeakActionCleanup() => preserveWeakFlightDuringAIActionCleanup = false;
    private bool HasWeakProjectiles
    {
        get { foreach (var shot in weakProjectiles) if (shot.active) return true; return false; }
    }
    private IEnumerator ExecuteWeakProjectile(EnemyAbilityDefinition ability, Transform target)
    {
        uint lease = actor.LeaseVersion;
        int sequence = attackSequenceId, next = 0;
        int level = GetComponent<EnemyRank>()?.Level ?? 1;
        float total = ability.UsesLevelDamageBudget
            ? Mathf.Max(1f, OverburstCombatBalance.RoundStat(OverburstCombatBalance.ReferenceEffectiveHealth(level)
                * ability.ReferencePatternDamagePercent / 100f)) : ability.Damage;
        total *= actor.RuntimeStats.DamageMultiplier;
        float allocation = total / ability.HitCount;
        bool useContactBudget = EnemyWeakProjectileDamageBudget.TryCreate(total, ability.HitCount, out var budget);
        weakProjectileReaction = ability.HitCount > 1 ? new EnemyWeakAttackReactionScope(gameObject, sequence, actor) : null;
        float speed = actor.Melee.AbilityAnimationSpeed, started = Time.time;
        float executionDuration = ability.ResolveExecutionDuration(speed);
        float windup = ability.ResolveWindupDelay(speed), duration = ability.ResolvePacedTime(1f, speed);
        Vector3 aim = actor.AbilityController.ResolveAimPosition(target) + Vector3.up * .8f;
        Vector3 facing = aim - transform.position; facing.y = 0f;
        chargeDirection = facing.sqrMagnitude > .0001f ? facing.normalized : transform.forward;
        actor.Movement.ApplyActionLock(executionDuration);
        while (Time.time < started + windup)
        {
            if (!Usable() || actor.LeaseVersion != lease || target == null) { routine = null; yield break; }
            yield return null;
        }
        actor.AnimationBridge.SetAttackAnimSpeed(ability.ResolvePhaseAnimationSpeed(0f, speed));
        actor.AnimationBridge.PlayAttack(ability.AnimatorTrigger);
        bool entered = false; float elapsed = 0f, lastRelease = started;
        while (elapsed < duration + .4f && Usable() && actor.LeaseVersion == lease && target != null)
        {
            if (actor.AnimationBridge.TryGetAttackNormalizedTime(ability.AnimatorTrigger, out float progress))
            {
                entered = true;
                actor.AnimationBridge.SetAttackAnimSpeed(ability.ResolvePhaseAnimationSpeed(progress, speed));
                // Aim locks on the first release; later shots use the current muzzle pose.
                while (next < ability.HitCount && progress >= ability.GetHitNormalizedTime(next))
                {
                    if (next == 0) aim = actor.AbilityController.ResolveAimPosition(target) + Vector3.up * .8f;
                    float damage = useContactBudget ? budget.ForPhase(next)
                        : next == ability.HitCount - 1 ? total - allocation * next : allocation;
                    LaunchWeakProjectile(ability, aim, damage, sequence, next, lease);
                    actor.AbilityController.NotifyAbilityImpact(ability, next++);
                    lastRelease = Time.time;
                }
            }
            else if (entered || elapsed > .4f) break;
            elapsed += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }
        float recoveryEnd = Mathf.Max(started + executionDuration, lastRelease + ability.MinimumRecoveryTime);
        while (Time.time < recoveryEnd && Usable() && actor.LeaseVersion == lease) yield return null;
        routine = null;
        if (!HasWeakProjectiles) { weakProjectileReaction?.Cancel(); weakProjectileReaction = null; }
    }
    private void LaunchWeakProjectile(EnemyAbilityDefinition ability, Vector3 aim, float damage, int sequence, int phase, uint lease)
    {
        WeakProjectile shot = null;
        foreach (var candidate in weakProjectiles) if (!candidate.active) { shot = candidate; break; }
        var catalog = EnemyProjectileVfxCatalog.Current;
        if (shot == null)
        {
            shot = new WeakProjectile();
            if (catalog != null && catalog.projectile != null)
            {
                shot.gameObject = Instantiate(catalog.projectile, transform, false);
                shot.visual = shot.gameObject.GetComponent<EnemyBioProjectileVisual>();
            }
            else
            {
                shot.gameObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                shot.gameObject.transform.SetParent(transform, false);
                shot.gameObject.transform.localScale = Vector3.one * .28f;
                var collider = shot.gameObject.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
                shot.gameObject.GetComponent<Renderer>().sharedMaterial = signalMaterial;
            }
            shot.gameObject.name = "Reusable V3 weak projectile";
            weakProjectiles.Add(shot);
        }
        shot.ability = ability; shot.position = ResolveMuzzle(ability);
        string muzzlePath = ability.WeakAttackExecution?.ProjectileMuzzleBonePath(phase);
        if (!string.IsNullOrEmpty(muzzlePath))
        {
            var muzzle = transform.Find(muzzlePath);
            if (muzzle == null) throw new System.InvalidOperationException("Missing authored projectile muzzle: " + muzzlePath);
            shot.position = muzzle.position;
        }
        shot.direction = (aim - shot.position).normalized; shot.remaining = ability.Range + 2f;
        shot.damage = damage; shot.sequence = sequence; shot.phase = phase; shot.lease = lease;
        shot.tint = GetComponent<BloodHitTarget>()?.Profile; shot.electric = false;
        float overrideScale = 0f;
        if (catalog != null && catalog.TryGetOverride(ability, out var entry))
        {
            if (entry.tint != null) shot.tint = entry.tint;
            shot.electric = entry.electricImpact; overrideScale = entry.scale;
        }
        var rank = GetComponent<EnemyRank>();
        shot.scale = (catalog != null ? catalog.scale * (rank != null && rank.GradeType >= EnemyGradeType.Elite ? catalog.strongScale : 1f) : 1f)
            * (overrideScale > 0f ? overrideScale : 1f);
        shot.active = true; shot.gameObject.transform.position = shot.position; shot.gameObject.SetActive(true);
        shot.visual?.Launch(shot.tint, shot.scale); LaunchCount++;
        if (shot.tint != null) BloodHitVfxService.RequestAt(shot.tint, shot.position + shot.direction * .3f, shot.direction,
            CombatImpactShape.Thrust, (catalog != null ? catalog.launchSpraySize : .6f) * Mathf.Sqrt(shot.scale),
            0, 1f, GetInstanceID(), allowSuppressed: true);
    }
    private void TickWeakProjectiles()
    {
        foreach (var shot in weakProjectiles)
        {
            if (!shot.active) continue;
            if (!Usable() || actor.LeaseVersion != shot.lease) { EndWeakProjectile(shot); continue; }
            float step = Mathf.Min(shot.remaining, 10f * Time.fixedDeltaTime);
            int count = Physics.SphereCastNonAlloc(shot.position, .14f, shot.direction, hits, step, Mask, QueryTriggerInteraction.Ignore);
            int nearest = Nearest(count);
            if (nearest >= 0)
            {
                Vector3 point = hits[nearest].point.sqrMagnitude > .0001f ? hits[nearest].point : shot.position;
                var target = CombatTarget.Resolve(hits[nearest].collider);
                SplashWeakProjectile(shot, point);
                if (target != null && target.DamageReceiver != null && CombatTargetFilter.CanDamage(GetComponent<CombatTarget>(), target))
                {
                    target.DamageReceiver.TakeDamage(new DamageInfo(shot.damage, point, gameObject, shot.direction,
                        sourceAttackSequenceId: shot.sequence, sourceAttackPhaseIndex: shot.phase,
                        enemyAbility: shot.ability, weakAttackReactionScope: weakProjectileReaction));
                    ImpactCount++;
                }
                EndWeakProjectile(shot); continue;
            }
            shot.position += shot.direction * step; shot.remaining -= step;
            shot.gameObject.transform.position = shot.position;
            if (shot.remaining <= 0f) { SplashWeakProjectile(shot, shot.position); EndWeakProjectile(shot); }
        }
        if (routine == null && !HasWeakProjectiles) { weakProjectileReaction?.Cancel(); weakProjectileReaction = null; }
    }
    private void SplashWeakProjectile(WeakProjectile shot, Vector3 point)
    {
        var catalog = EnemyProjectileVfxCatalog.Current;
        if (shot.tint != null) BloodHitVfxService.RequestAt(shot.tint, point, shot.direction, CombatImpactShape.Downward,
            (catalog != null ? catalog.impactSplashSize : .9f) * Mathf.Sqrt(shot.scale), 1, 1f, GetInstanceID(), allowSuppressed: true);
        if (shot.electric) MeleeElementHitVfxService.TryPlay(WeaponElement.Electric, point);
    }
    private static void EndWeakProjectile(WeakProjectile shot)
    {
        shot.active = false; shot.ability = null;
        if (shot.visual != null) shot.visual.Stop(); else if (shot.gameObject != null) shot.gameObject.SetActive(false);
    }
    private void UpdateWeakProjectileVisuals()
    { foreach (var shot in weakProjectiles) if (shot.active && shot.gameObject != null) shot.gameObject.transform.position = shot.position; }
    private void CancelWeakProjectiles()
    {
        // Only the AI's cleanup of an already completed action keeps released weak shots.
        // Direct cancellation, reactions, death and pool reset continue to remove them.
        if (preserveWeakFlightDuringAIActionCleanup && isActiveAndEnabled && Usable()) return;
        foreach (var shot in weakProjectiles) EndWeakProjectile(shot);
        weakProjectileReaction?.Cancel(); weakProjectileReaction = null;
    }
}
