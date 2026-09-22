using System.Collections;
using UnityEngine;

// Optional extension for theme actors. Selection, turn reservation and movement still belong to existing AI.
[DisallowMultipleComponent]
public sealed class EnemyThemeSpecialExecutor : EnemyAbilityExecutor
{
    [SerializeField] private Material signalMaterial;
    [SerializeField] private Color signalColor = new Color(1f,.45f,.15f);
    private EnemyActor actor;
    private EnemyMovementReaction reaction;
    private Coroutine routine;
    private LineRenderer warning;
    private GameObject bolt;
    private bool boltFlying;
    private Vector3 boltDirection;
    private Vector3 boltPosition;
    private float boltRemaining, boltDamage;
    private readonly RaycastHit[] hits = new RaycastHit[24];
    public override bool IsExecuting => routine != null;
    public bool HasProjectile => boltFlying;
    public int LaunchCount { get; private set; }
    public int ImpactCount { get; private set; }

    private void Awake() { Resolve(); }
    private void Resolve() { if (actor == null) actor = GetComponent<EnemyActor>(); if (reaction == null) reaction = GetComponent<EnemyMovementReaction>(); }
    private void OnEnable()
    {
        Resolve();
        if (actor != null && actor.Health != null) { actor.Health.OnDead += Damaged; actor.Health.OnDamaged += Damaged; }
        if (reaction != null) reaction.ReactionStarted += Cancel;
    }
    private void OnDisable()
    {
        if (actor != null && actor.Health != null) { actor.Health.OnDead -= Damaged; actor.Health.OnDamaged -= Damaged; }
        if (reaction != null) reaction.ReactionStarted -= Cancel;
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
            || actor.Movement.IsActionLocked || actor.AnimationBridge.IsBlockingActionActive) return false;
        float distance = Vector3.Distance(new Vector3(target.position.x, transform.position.y, target.position.z), transform.position);
        return ability.MatchesUseConditions(distance, actor.Health.NormalizedHp)
            && actor.Movement.IsFacingForAttack(target.position) && HasLineOfSight(target);
    }
    private bool Usable() => actor != null && actor.IsLeased && actor.Health != null && !actor.Health.IsDead
        && actor.Movement != null && !actor.Movement.IsStatusMovementLocked
        && (reaction == null || !reaction.IsHitStunActive && !reaction.IsKnockbackActive);
    private int Mask => ~((1 << LayerMask.NameToLayer("Enemy")) | (1 << LayerMask.NameToLayer("Ignore Raycast")));
    private Vector3 Origin => transform.position + Vector3.up * .8f;
    private bool HasLineOfSight(Transform target)
    {
        Vector3 delta = target.position + Vector3.up * .8f - Origin;
        if (Physics.Raycast(Origin, delta.normalized, out var hit, delta.magnitude, Mask, QueryTriggerInteraction.Ignore))
            return hit.collider.GetComponentInParent<CombatHealth>() == target.GetComponentInParent<CombatHealth>();
        return true;
    }
    public override bool TryStart(EnemyAbilityDefinition ability, int index, Transform target)
    {
        if (!CanStart(ability,target)) return false;
        EnsureVisuals();
        routine = StartCoroutine(Execute(ability,target));
        return true;
    }
    public override float ResolveCooldown(float duration) { Resolve(); return actor != null ? actor.Melee.ResolveAbilityCooldown(duration) : duration; }
    private IEnumerator Execute(EnemyAbilityDefinition ability, Transform target)
    {
        Vector3 destination = target.position;
        Vector3 direction = destination - transform.position; direction.y = 0; direction.Normalize();
        // Facing is completed before CanStart succeeds. Aim and release keep this committed direction.
        float duration = ResolveCooldown(ability.AttackAnimationDuration);
        actor.Movement.ApplyActionLock(duration + .12f);
        actor.AnimationBridge.SetAttackAnimSpeed(ability.AttackAnimationDuration / Mathf.Max(.01f,duration));
        actor.AnimationBridge.PlayAttack(ability.AnimatorTrigger);
        warning.enabled = true;
        float elapsed = 0, progress = 0;
        bool entered = false, committed = false;
        float remainingTravel = Mathf.Max(0, Vector3.Distance(transform.position,destination) - .9f);
        while (elapsed < duration + .35f)
        {
            if (!Usable() || target == null) break;
            bool inState = actor.AnimationBridge.TryGetAttackNormalizedTime(ability.AnimatorTrigger,out float normalized);
            if (inState) { entered = true; progress = normalized; }
            else if (entered) break;
            else if (elapsed > .4f) break; // An unconnected animation never produces an invisible attack.
            warning.SetPosition(0,transform.position+Vector3.up*.08f);
            warning.SetPosition(1,(ability.ExecutionMode == EnemyAbilityExecutionMode.Projectile ? destination : transform.position+direction*Mathf.Min(remainingTravel,ability.Range))+Vector3.up*.08f);
            if (inState && ability.ExecutionMode == EnemyAbilityExecutionMode.Charge && progress > .2f && progress < ability.HitNormalizedTime && remainingTravel > 0)
            {
                float step = Mathf.Min(remainingTravel, Mathf.Min(.3f, ability.Range * Time.fixedDeltaTime / Mathf.Max(.15f,duration*(ability.HitNormalizedTime-.2f))));
                if (actor.Movement.RequestAttackDisplacement(direction*step)) remainingTravel -= step;
            }
            if (inState && !committed && progress >= ability.HitNormalizedTime)
            {
                committed = true; warning.enabled = false;
                if (ability.ExecutionMode == EnemyAbilityExecutionMode.Projectile)
                {
                    boltPosition = Origin;
                    bolt.transform.position = boltPosition;
                    boltDirection = (destination + Vector3.up*.8f - Origin).normalized;
                    boltRemaining = ability.Range + 2; boltDamage = ability.Damage * actor.RuntimeStats.DamageMultiplier;
                    boltFlying = true; bolt.SetActive(true); LaunchCount++;
                }
                else ResolveChargeHit(ability,direction);
            }
            elapsed += Time.fixedDeltaTime;
            yield return new WaitForFixedUpdate();
        }
        warning.enabled = false; actor.Movement.ClearAttackDisplacement(); routine = null;
    }
    private void ResolveChargeHit(EnemyAbilityDefinition ability,Vector3 direction)
    {
        int count = Physics.SphereCastNonAlloc(Origin,.4f,direction,hits,Mathf.Max(.8f,ability.HitRadius),Mask,QueryTriggerInteraction.Ignore);
        int nearest = Nearest(count);
        if (nearest >= 0) Damage(hits[nearest],ability.Damage*actor.RuntimeStats.DamageMultiplier,direction);
    }
    private int Nearest(int count)
    { int index=-1; for(int i=0;i<count;i++) if(index<0 || hits[i].distance<hits[index].distance)index=i; return index; }
    private void Damage(RaycastHit hit,float amount,Vector3 direction)
    {
        var target = hit.collider.GetComponentInParent<CombatTarget>();
        if (target == null || !CombatTargetFilter.CanDamage(GetComponent<CombatTarget>(),target) || target.DamageReceiver == null) return;
        target.DamageReceiver.TakeDamage(new DamageInfo(amount,hit.point,gameObject,direction)); ImpactCount++;
    }
    private void FixedUpdate()
    {
        if (!boltFlying) return;
        if (!Usable()) { EndBolt(); return; }
        float step = Mathf.Min(boltRemaining,10f*Time.fixedDeltaTime);
        int count = Physics.SphereCastNonAlloc(boltPosition,.14f,boltDirection,hits,step,Mask,QueryTriggerInteraction.Ignore);
        int nearest = Nearest(count);
        if (nearest>=0) { Damage(hits[nearest],boltDamage,boltDirection);EndBolt();return; }
        boltPosition += boltDirection*step; bolt.transform.position = boltPosition; boltRemaining-=step;
        if (boltRemaining<=0) EndBolt();
    }
    private void LateUpdate() { if (boltFlying && bolt != null) bolt.transform.position = boltPosition; }
    private void EnsureVisuals()
    {
        if (warning == null)
        {
            var go=new GameObject("Attack direction warning");go.transform.SetParent(transform,false);
            warning=go.AddComponent<LineRenderer>();warning.sharedMaterial=signalMaterial;warning.positionCount=2;
            warning.startWidth=.12f;warning.endWidth=.32f;warning.startColor=signalColor;warning.endColor=signalColor;
            warning.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;warning.receiveShadows=false;
        }
        if (bolt == null)
        {
            bolt=GameObject.CreatePrimitive(PrimitiveType.Sphere);bolt.name="Reusable theme projectile";
            bolt.transform.SetParent(transform,false);bolt.transform.localScale=Vector3.one*.28f;
            var collider=bolt.GetComponent<Collider>();collider.enabled=false;Destroy(collider);
            bolt.GetComponent<Renderer>().sharedMaterial=signalMaterial;bolt.SetActive(false);
        }
    }
    private void EndBolt() { boltFlying=false;if(bolt!=null)bolt.SetActive(false); }
    public override void Cancel()
    {
        if (routine!=null) { StopCoroutine(routine);routine=null; if(actor!=null)actor.Movement.CancelActionLock(); }
        if(actor!=null && actor.Movement!=null)actor.Movement.ClearAttackDisplacement();
        if(warning!=null)warning.enabled=false;EndBolt();
    }
    public override void ResetForReuse() { Resolve();Cancel();LaunchCount=0;ImpactCount=0; }
}
