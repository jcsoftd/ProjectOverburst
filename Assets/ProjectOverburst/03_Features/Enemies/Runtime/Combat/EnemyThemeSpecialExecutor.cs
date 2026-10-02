using System.Collections;
using UnityEngine;

// Optional extension for theme actors. Selection, turn reservation and movement still belong to existing AI.
[DisallowMultipleComponent]
public sealed class EnemyThemeSpecialExecutor : EnemyAbilityExecutor
{
    [SerializeField] private Material signalMaterial;
    [SerializeField] private Color signalColor = new Color(1f,.45f,.15f);
    [System.Serializable]
    public sealed class MuzzleOverride
    {
        public EnemyAbilityDefinition ability;
        public Transform socket;
        public Vector3 localOffset;
    }
    [SerializeField] private MuzzleOverride[] muzzleOverrides = new MuzzleOverride[0];
    private EnemyActor actor;
    private EnemyMovementReaction reaction;
    private Coroutine routine;
    private GameObject bolt;
    private bool boltFlying;
    private Vector3 boltDirection;
    private Vector3 boltPosition;
    private float boltRemaining, boltDamage;
    private EnemyAbilityDefinition boltAbility;
    private EnemyBioProjectileVisual boltVisual;
    private BloodHitProfile boltTint;
    private bool boltElectric;
    private float boltScale = 1f;
    private Vector3 chargeDirection;
    private int attackSequenceId;
    private readonly RaycastHit[] hits = new RaycastHit[24];
    // Flight belongs to the attack too: a short animation must not cancel a distant shot.
    public override bool IsExecuting => routine != null || boltFlying;
    public bool HasProjectile => boltFlying;
    public Vector3 ChargeDirection => chargeDirection;
    public int LaunchCount { get; private set; }
    public int ImpactCount { get; private set; }

    private void Awake() { Resolve(); }
    private void Resolve() { if (actor == null) actor = GetComponent<EnemyActor>(); if (reaction == null) reaction = GetComponent<EnemyMovementReaction>(); }
    private void OnEnable()
    {
        Resolve();
        if (actor != null && actor.Health != null)
        {
            actor.Health.OnDead += Damaged;
            if (GetComponent<EnemyHitResponseCoordinator>() == null)
                actor.Health.OnDamaged += Damaged;
        }
        if (reaction != null) reaction.ReactionStarted += HandleReactionStarted;
    }
    private void OnDisable()
    {
        if (actor != null && actor.Health != null) { actor.Health.OnDead -= Damaged; actor.Health.OnDamaged -= Damaged; }
        if (reaction != null) reaction.ReactionStarted -= HandleReactionStarted;
        Cancel();
    }
    private void Damaged(CombatHealth source, DamageInfo info) { if (!info.isDamageOverTime && info.triggersOnHitEffects || source.IsDead) Cancel(); }
    public void Configure(Material material, Color color) { signalMaterial = material; signalColor = color; }
    public override bool Supports(EnemyAbilityDefinition ability) => ability != null
        && (ability.ExecutionMode == EnemyAbilityExecutionMode.Charge || ability.ExecutionMode == EnemyAbilityExecutionMode.Projectile);
    public override bool CanStart(EnemyAbilityDefinition ability, Transform target)
    {
        Resolve();
        if (!Supports(ability) || target == null || routine != null || boltFlying || !Usable()
            || actor.Movement.IsActionLocked || actor.AnimationBridge.BlocksAttackStart) return false;
        Vector3 point = actor.AbilityController.ResolveAimPosition(target);
        float distance = Vector3.Distance(new Vector3(point.x, transform.position.y, point.z), transform.position);
        return EnemyAttackThreatGeometry.MatchesUseConditions(actor, ability, distance, actor.Health.NormalizedHp)
            && actor.Movement.IsFacingForAttack(point)
            && (ability.ExecutionMode == EnemyAbilityExecutionMode.Projectile
                ? HasPositioningLine(target, transform.position, point) : HasLineOfSight(target, point));
    }
    private bool Usable() => actor != null && actor.IsLeased && actor.Health != null && !actor.Health.IsDead
        && actor.Movement != null && !actor.Movement.IsStatusMovementLocked
        && (reaction == null || !reaction.BlocksAttack);
    private void HandleReactionStarted()
    {
        if (reaction == null || reaction.BlocksAttack) Cancel();
    }
    private int Mask => ~((1 << LayerMask.NameToLayer("Enemy")) | (1 << LayerMask.NameToLayer("Ignore Raycast")));
    private Vector3 Origin => transform.position + Vector3.up * .8f;
    public bool HasPositioningLine(Transform target, Vector3 position, Vector3 point)
    {
        if (target == null) return false;
        Vector3 origin = position + Vector3.up * .8f;
        Vector3 delta = point + Vector3.up * .8f - origin;
        if (delta.sqrMagnitude < .0001f) return true;
        if (Physics.SphereCast(origin, .14f, delta.normalized, out var hit, delta.magnitude, Mask, QueryTriggerInteraction.Ignore))
        {
            var targetHealth = target.GetComponentInParent<CombatHealth>();
            return targetHealth != null && hit.collider.GetComponentInParent<CombatHealth>() == targetHealth;
        }
        return true;
    }
    private bool HasLineOfSight(Transform target, Vector3 point)
    {
        Vector3 delta = point + Vector3.up * .8f - Origin;
        if (Physics.Raycast(Origin, delta.normalized, out var hit, delta.magnitude, Mask, QueryTriggerInteraction.Ignore))
            return hit.collider.GetComponentInParent<CombatHealth>() == target.GetComponentInParent<CombatHealth>();
        return true;
    }
    public override bool TryStart(EnemyAbilityDefinition ability, int index, Transform target)
    {
        if (!CanStart(ability,target)) return false;
        reaction?.PrepareForAttack();
        attackSequenceId = EnemyAttackSequence.Next();
        if (ability.ExecutionMode == EnemyAbilityExecutionMode.Projectile) EnsureProjectileVisual();
        routine = StartCoroutine(Execute(ability,target));
        return true;
    }
    public override float ResolveCooldown(float duration) { Resolve(); return actor != null ? actor.Melee.ResolveAbilityCooldown(duration) : duration; }
    private IEnumerator Execute(EnemyAbilityDefinition ability, Transform target)
    {
        Vector3 destination = actor.AbilityController.ResolveAimPosition(target);
        Vector3 direction = destination - transform.position; direction.y = 0; direction.Normalize();
        chargeDirection = direction;
        // Facing is completed before CanStart succeeds. Aim and release keep this committed direction.
        float speed = actor.Melee.AbilityAnimationSpeed;
        float duration = ability.ResolvePacedTime(1f, speed);
        float windup = ability.ResolveWindupDelay(speed);
        float startedAt = Time.time;
        float executionDuration = ability.ResolveExecutionDuration(speed);
        actor.Movement.ApplyActionLock(executionDuration);
        while (Time.time < startedAt + windup)
        {
            if (!Usable() || target == null) { routine = null; yield break; }
            yield return null;
        }
        actor.AnimationBridge.SetAttackAnimSpeed(ability.ResolvePhaseAnimationSpeed(0f, speed));
        actor.AnimationBridge.PlayAttack(ability.AnimatorTrigger);
        float elapsed = 0, progress = 0, lastImpactTime = startedAt;
        bool entered = false, committed = false;
        float remainingTravel = Mathf.Max(0, Vector3.Distance(transform.position,destination) - .9f);
        while (elapsed < duration + .35f)
        {
            if (!Usable() || target == null) break;
            if (!committed && ability.IsTelegraphedAttack)
            {
                destination = actor.AbilityController.ResolveAimPosition(target);
                direction = destination - transform.position; direction.y = 0f; direction.Normalize();
                chargeDirection = direction;
            }
            bool inState = actor.AnimationBridge.TryGetAttackNormalizedTime(ability.AnimatorTrigger,out float normalized);
            if (inState)
            {
                entered = true; progress = normalized;
                actor.AnimationBridge.SetAttackAnimSpeed(ability.ResolvePhaseAnimationSpeed(progress, speed));
            }
            else if (entered) break;
            else if (elapsed > .4f) break; // An unconnected animation never produces an invisible attack.
            if (inState && ability.ExecutionMode == EnemyAbilityExecutionMode.Charge && progress > .2f && progress < ability.HitNormalizedTime && remainingTravel > 0)
            {
                float step = Mathf.Min(remainingTravel, Mathf.Min(.3f, ability.Range * Time.fixedDeltaTime / Mathf.Max(.15f,duration*(ability.HitNormalizedTime-.2f))));
                if (actor.Movement.RequestAttackDisplacement(direction*step)) remainingTravel -= step;
            }
            if (inState && !committed && progress >= ability.HitNormalizedTime)
            {
                committed = true;
                lastImpactTime = Time.time;
                actor.AbilityController.NotifyAbilityImpact(ability, ability.HitCount - 1);
                if (ability.IsTelegraphedStrongAttack && ability.ExecutionMode == EnemyAbilityExecutionMode.Charge)
                {
                    CombatActionSfxService.PlayEnemyStrongRelease(transform.position);
                    CombatActionSfxService.PlayEnemyGroundImpact(transform.position);
                    EnemyStrongAttackImpactVfx.Play(transform.position);
                }
                if (ability.ExecutionMode == EnemyAbilityExecutionMode.Projectile)
                {
                    boltPosition = ResolveMuzzle(ability);
                    bolt.transform.position = boltPosition;
                    boltDirection = (destination + Vector3.up*.8f - boltPosition).normalized;
                    boltRemaining = ability.Range + 2; boltDamage = ability.ResolveDamage(GetComponent<EnemyRank>()?.Level ?? 1) * actor.RuntimeStats.DamageMultiplier;
                    boltAbility = ability;
                    boltFlying = true; bolt.SetActive(true); LaunchCount++;
                    LaunchVisual(ability);
                }
                else ResolveChargeHit(ability,direction);
            }
            elapsed += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }
        actor.Movement.ClearAttackDisplacement();
        float recoveryEnd = Mathf.Max(startedAt + executionDuration, lastImpactTime + ability.MinimumRecoveryTime);
        while (Time.time < recoveryEnd && Usable())
        {
            if (actor.AnimationBridge.TryGetAttackNormalizedTime(ability.AnimatorTrigger, out float recoveryProgress))
                actor.AnimationBridge.SetAttackAnimSpeed(ability.ResolvePhaseAnimationSpeed(recoveryProgress, speed));
            yield return null;
        }
        routine = null;
    }
    private void ResolveChargeHit(EnemyAbilityDefinition ability,Vector3 direction)
    {
        float reach = EnemyAttackThreatGeometry.ResolveRadius(actor, ability);
        int count = Physics.SphereCastNonAlloc(Origin,.4f,direction,hits,Mathf.Max(.8f,reach),Mask,QueryTriggerInteraction.Ignore);
        int nearest = Nearest(count);
        if (nearest >= 0) Damage(hits[nearest],ability.ResolveDamage(GetComponent<EnemyRank>()?.Level ?? 1)*actor.RuntimeStats.DamageMultiplier,direction,ability);
    }
    public bool WouldChargeHit(EnemyAbilityDefinition ability, CombatTarget target)
    {
        if (ability == null || target == null || chargeDirection.sqrMagnitude < .0001f)
            return false;
        int count = Physics.SphereCastNonAlloc(Origin, .4f, chargeDirection, hits,
            Mathf.Max(.8f, EnemyAttackThreatGeometry.ResolveRadius(actor, ability)),
            Mask, QueryTriggerInteraction.Ignore);
        int nearest = Nearest(count);
        return nearest >= 0 && CombatTarget.Resolve(hits[nearest].collider) == target;
    }
    private int Nearest(int count)
    { int index=-1; for(int i=0;i<count;i++) if(index<0 || hits[i].distance<hits[index].distance)index=i; return index; }
    private void Damage(RaycastHit hit,float amount,Vector3 direction,EnemyAbilityDefinition ability)
    {
        var target = hit.collider.GetComponentInParent<CombatTarget>();
        if (target == null || !CombatTargetFilter.CanDamage(GetComponent<CombatTarget>(),target) || target.DamageReceiver == null) return;
        target.DamageReceiver.TakeDamage(new DamageInfo(amount,hit.point,gameObject,direction,
            sourceAttackSequenceId:attackSequenceId,sourceAttackPhaseIndex:0,enemyAbility:ability)); ImpactCount++;
    }
    private void FixedUpdate()
    {
        if (!boltFlying) return;
        if (!Usable()) { EndBolt(); return; }
        float step = Mathf.Min(boltRemaining,10f*Time.fixedDeltaTime);
        int count = Physics.SphereCastNonAlloc(boltPosition,.14f,boltDirection,hits,step,Mask,QueryTriggerInteraction.Ignore);
        int nearest = Nearest(count);
        if (nearest>=0)
        {
            Vector3 impact = hits[nearest].point.sqrMagnitude > .0001f ? hits[nearest].point : boltPosition;
            SplashBolt(impact); Damage(hits[nearest],boltDamage,boltDirection,boltAbility); EndBolt(); return;
        }
        boltPosition += boltDirection*step; bolt.transform.position = boltPosition; boltRemaining-=step;
        if (boltRemaining<=0) { SplashBolt(boltPosition); EndBolt(); }
    }
    private void LateUpdate() { if (boltFlying && bolt != null) bolt.transform.position = boltPosition; }
    private void EnsureProjectileVisual()
    {
        if (bolt == null)
        {
            var catalog = EnemyProjectileVfxCatalog.Current;
            if (catalog != null && catalog.projectile != null)
            {
                bolt = Instantiate(catalog.projectile, transform, false); bolt.name = "Reusable theme projectile";
                boltVisual = bolt.GetComponent<EnemyBioProjectileVisual>();
                bolt.SetActive(false);
                return;
            }
            bolt=GameObject.CreatePrimitive(PrimitiveType.Sphere);bolt.name="Reusable theme projectile";
            bolt.transform.SetParent(transform,false);bolt.transform.localScale=Vector3.one*.28f;
            var collider=bolt.GetComponent<Collider>();collider.enabled=false;Destroy(collider);
            bolt.GetComponent<Renderer>().sharedMaterial=signalMaterial;bolt.SetActive(false);
        }
    }
    private readonly System.Collections.Generic.Dictionary<string, Transform> muzzleBones = new System.Collections.Generic.Dictionary<string, Transform>();
    // The shot leaves the authored mouth, tail or hand bone; without one it keeps the old body origin.
    private Vector3 ResolveMuzzle(EnemyAbilityDefinition ability) => ResolveMuzzlePosition(ability,
        chargeDirection.sqrMagnitude > .0001f ? chargeDirection : transform.forward);

    public Vector3 ResolveMuzzlePosition(EnemyAbilityDefinition ability, Vector3 direction)
    {
        for (int i = 0; muzzleOverrides != null && i < muzzleOverrides.Length; i++)
        {
            var tuning = muzzleOverrides[i];
            if (tuning == null || tuning.ability != ability) continue;
            if (tuning.socket != null && (tuning.socket == transform || tuning.socket.IsChildOf(transform)))
                return tuning.socket.TransformPoint(tuning.localOffset);
            break;
        }
        var catalog = EnemyProjectileVfxCatalog.Current;
        if (catalog == null || !catalog.TryGetOverride(ability, out var entry) || string.IsNullOrEmpty(entry.muzzleBone)) return Origin;
        if (!muzzleBones.TryGetValue(entry.muzzleBone, out var bone) || bone == null)
        {
            bone = null;
            foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == entry.muzzleBone) { bone = t; break; }
            muzzleBones[entry.muzzleBone] = bone;
        }
        if (bone == null) return Origin;
        Vector3 forward = direction.sqrMagnitude > .0001f ? direction : transform.forward;
        return bone.position + forward * entry.muzzleForward;
    }
    // Spit leaves the mouth as a short jet in the thrower's blood colour and bursts where it lands.
    private void LaunchVisual(EnemyAbilityDefinition ability)
    {
        var catalog = EnemyProjectileVfxCatalog.Current;
        var blood = GetComponent<BloodHitTarget>();
        boltTint = blood != null ? blood.Profile : null;
        boltElectric = false;
        float overrideScale = 0f;
        if (catalog != null && catalog.TryGetOverride(ability, out var entry))
        {
            if (entry.tint != null) boltTint = entry.tint;
            boltElectric = entry.electricImpact;
            overrideScale = entry.scale;
        }
        var rank = GetComponent<EnemyRank>();
        bool strong = ability.IsTelegraphedStrongAttack || (rank != null && rank.GradeType >= EnemyGradeType.Elite);
        boltScale = (catalog != null ? catalog.scale * (strong ? catalog.strongScale : 1f) : 1f) * (overrideScale > 0f ? overrideScale : 1f);
        if (boltVisual != null) boltVisual.Launch(boltTint, boltScale);
        if (boltTint != null)
            BloodHitVfxService.RequestAt(boltTint, boltPosition + boltDirection * .3f, boltDirection, CombatImpactShape.Thrust,
                (catalog != null ? catalog.launchSpraySize : .6f) * Mathf.Sqrt(boltScale), 0, 1f, GetInstanceID(), allowSuppressed: true);
    }
    private void SplashBolt(Vector3 point)
    {
        var catalog = EnemyProjectileVfxCatalog.Current;
        if (boltTint != null)
            BloodHitVfxService.RequestAt(boltTint, point, boltDirection, CombatImpactShape.Downward,
                (catalog != null ? catalog.impactSplashSize : .9f) * Mathf.Sqrt(boltScale), 1, 1f, GetInstanceID(), allowSuppressed: true);
        if (boltElectric) MeleeElementHitVfxService.TryPlay(WeaponElement.Electric, point);
    }
    private void EndBolt()
    {
        boltFlying=false;boltAbility=null;
        if (boltVisual != null) boltVisual.Stop(); // drips already in the air keep falling
        else if (bolt != null) bolt.SetActive(false);
    }
    public override void Cancel()
    {
        if (routine!=null) { StopCoroutine(routine);routine=null; if(actor!=null)actor.Movement.CancelActionLock(); }
        if(actor!=null && actor.Movement!=null)actor.Movement.ClearAttackDisplacement();
        chargeDirection = Vector3.zero;
        EndBolt();
    }
    public override void ResetForReuse() { Resolve();Cancel();LaunchCount=0;ImpactCount=0; }
}
