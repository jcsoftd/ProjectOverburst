using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Opt-in execution for independently reusable authored boss attacks. AI selection and phase composition stay outside this component.
[DisallowMultipleComponent]
public sealed class EnemyBossMaterialExecutor : EnemyAbilityExecutor
{
    [SerializeField] private EnemyBossMaterialCollection collection;
    private EnemyActor actor;
    private CombatTarget ownerTarget;
    private EnemyMovementReaction reaction;
    private Coroutine cast, motion;
    private Animator samplingAnimator;
    private AnimatorUpdateMode previousUpdate;
    private int generation, sequence;
    private uint lease;
    private readonly Collider[] colliders=new Collider[64];
    private readonly Collider[] previewColliders=new Collider[64];
    private readonly RaycastHit[] rayHits=new RaycastHit[32];
    private readonly HashSet<CombatHealth>[] hitTargets={new HashSet<CombatHealth>(),new HashSet<CombatHealth>(),new HashSet<CombatHealth>()};
    private readonly List<Flight> flights=new List<Flight>(3);
    private readonly List<ProjectileVisual> visuals=new List<ProjectileVisual>(3);
    private readonly EnemyStrongAttackWarning[] warnings=new EnemyStrongAttackWarning[3];
    private GameObject heldBoulder;
    private Transform boulderLeftHand, boulderRightHand;
    private float previousAnimatorSpeed;
    private bool poseHeld;
    private Vector3 committedAim;
    private float castSpeed=1f;
    private int upcomingStrike;
    private bool entered;
    private float progress;
    private readonly bool[] released=new bool[3];
    private readonly bool[] warningShown=new bool[3];
    public EnemyBossAttackMaterial CurrentMaterial { get; private set; }
    public EnemyBossMaterialCollection Collection => collection;
    public float NormalizedTime => progress;
    public bool HasEnteredMotion => entered;
    public int ImpactCount { get; private set; }
    public int LaunchCount { get; private set; }
    public int CompletedCount { get; private set; }
    public int DamageCount { get; private set; }
    public int ActiveProjectileCount => flights.Count;
    public int ActiveAttackSequenceId => sequence;
    public bool IsRockHeld => heldBoulder!=null && heldBoulder.activeSelf;
    public string LastFailure { get; private set; }
    public event Action<EnemyBossAttackMaterial,int> StrikeReleased;
    public event Action<EnemyBossAttackMaterial> AttackCancelled;
    public override bool IsExecuting => cast!=null || motion!=null || flights.Count!=0;
    private int TargetMask => 1<<LayerMask.NameToLayer("Player");
    private int FlightMask => ~((1<<LayerMask.NameToLayer("Enemy"))|(1<<LayerMask.NameToLayer("Ignore Raycast")));
    private static readonly WaitForFixedUpdate AfterPhysics=new WaitForFixedUpdate();

    sealed class ProjectileVisual
    {
        public GameObject root;
        public EnemyBioProjectileVisual bio;
        public bool boulder,used;
    }
    sealed class Flight
    {
        public EnemyBossAttackMaterial material;
        public int phase,sequence;
        public uint lease;
        public Vector3 start,position,direction,landing;
        public float distance,elapsed,damage;
        public ProjectileVisual visual;
    }
    public void Configure(EnemyBossMaterialCollection data) { collection=data; }
    private void Resolve()
    {
        if(actor==null)actor=GetComponent<EnemyActor>();
        if(ownerTarget==null)ownerTarget=GetComponent<CombatTarget>();
        if(reaction==null)reaction=GetComponent<EnemyMovementReaction>();
    }
    private void Awake()=>Resolve();
    private void OnEnable()
    {
        Resolve();
        if(actor!=null && actor.Health!=null)actor.Health.OnDead+=Died;
        if(reaction!=null)reaction.ReactionStarted+=ReactionStarted;
    }
    private void OnDisable()
    {
        if(actor!=null && actor.Health!=null)actor.Health.OnDead-=Died;
        if(reaction!=null)reaction.ReactionStarted-=ReactionStarted;
        Cancel();
    }
    private void Died(CombatHealth source,DamageInfo damage)=>Cancel();
    private void ReactionStarted() { if(reaction==null || reaction.BlocksAttack)Cancel(); }
    private bool Usable => (GetComponent<CrustaspikanTemporaryReaction>()?.BlocksActions != true) && actor!=null && actor.IsLeased && actor.Health!=null && !actor.Health.IsDead
        && actor.Movement!=null && (reaction==null || !reaction.BlocksAttack)
        && actor.Melee!=null && actor.Melee.StatusActionSpeedMultiplier>0f;
    public override bool Supports(EnemyAbilityDefinition ability)=>collection!=null && collection.Find(ability)?.IsValid==true;
    public override bool CanStart(EnemyAbilityDefinition ability,Transform target)
    {
        Resolve();
        if(!Supports(ability)||!Usable||target==null||IsExecuting||actor.Movement.IsActionLocked||actor.AnimationBridge.BlocksAttackStart) return false;
        Vector3 aim=actor.AbilityController.ResolveAimPosition(target);
        Vector3 delta=aim-transform.position;delta.y=0f;
        return EnemyAttackThreatGeometry.MatchesUseConditions(actor,ability,delta.magnitude,actor.Health.NormalizedHp);
    }
    public override bool TryStart(EnemyAbilityDefinition ability,int abilityIndex,Transform target)
    {
        if(!CanStart(ability,target))return false;
        ReleaseHeldPose();
        reaction?.PrepareForAttack();
        CurrentMaterial=collection.Find(ability);LastFailure=null;progress=0f;entered=false;upcomingStrike=0;
        sequence=EnemyAttackSequence.Next();lease=actor.LeaseVersion;generation++;
        for(int i=0;i<3;i++){hitTargets[i].Clear();released[i]=false;warningShown[i]=false;}
        committedAim=actor.AbilityController.ResolveAimPosition(target);committedAim.y=transform.position.y;
        castSpeed=actor.Melee.AbilityAnimationSpeed*CurrentMaterial.AnimationSpeedMultiplier;
        if(CurrentMaterial.delivery==EnemyBossMaterialDelivery.Boulder)SetRockHeld(true);
        samplingAnimator=actor.Animator;previousUpdate=samplingAnimator.updateMode;samplingAnimator.updateMode=AnimatorUpdateMode.Fixed;
        cast=StartCoroutine(Execute(CurrentMaterial,target,generation));return true;
    }
    public override float ResolveCooldown(float baseCooldown)
    {Resolve();return actor!=null && actor.Melee!=null?actor.Melee.ResolveAbilityCooldown(baseCooldown):Mathf.Max(0f,baseCooldown);}

    private IEnumerator Execute(EnemyBossAttackMaterial material,Transform target,int token)
    {
        var ability=material.ability;float wait=0f,lastProgress=0f;bool completed=false;
        try
        {
            actor.Movement.ApplyActionLock(ability.ResolveExecutionDuration(castSpeed)+.5f);
            actor.AnimationBridge.SetAttackAnimSpeed(ability.ResolvePhaseAnimationSpeed(0f,castSpeed));
            actor.AnimationBridge.PlayAttack(ability.AnimatorTrigger);
            while(token==generation && Usable && actor.LeaseVersion==lease)
            {
                yield return AfterPhysics;
                if(token!=generation || !Usable || actor.LeaseVersion!=lease)yield break;
                bool inState=actor.AnimationBridge.TryGetAttackMotionTime(ability.AnimatorTrigger,material.runtimeClip,out float normalized);
                if(!inState)
                {
                    if(entered)break;
                    wait+=Time.fixedDeltaTime;
                    if(wait>.65f){LastFailure="Attack motion did not enter; no damage released.";yield break;}
                    continue;
                }
                entered=true;progress=Mathf.Clamp01(normalized);
                if(progress+.0001f<lastProgress){LastFailure="Attack timeline regressed; cancelled.";yield break;}
                castSpeed=actor.Melee.AbilityAnimationSpeed*material.AnimationSpeedMultiplier;
                float firstRemaining=ability.ResolvePacedTime(material.strikes[0].impact,castSpeed)-ability.ResolvePacedTime(progress,castSpeed);
                if(material.tracksTargetDuringWindup && target!=null && firstRemaining>material.aimLockLeadSeconds)
                {
                    committedAim=target.position;committedAim.y=transform.position.y;
                    Vector3 facing=committedAim-transform.position;facing.y=0f;
                    if(facing.sqrMagnitude>.0001f)transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(facing),60f*Time.fixedDeltaTime);
                }
                actor.AnimationBridge.SetAttackAnimSpeed(ability.ResolvePhaseAnimationSpeed(progress,castSpeed));
                actor.Movement.ApplyActionLock(.25f);
                if(material.advanceDistance>0f)
                {
                    float prior=Mathf.Clamp01(Mathf.InverseLerp(material.advanceWindow.x,material.advanceWindow.y,lastProgress));
                    float now=Mathf.Clamp01(Mathf.InverseLerp(material.advanceWindow.x,material.advanceWindow.y,progress));
                    if(now>prior)actor.Movement.RequestAttackDisplacement(transform.forward*(now-prior)*material.advanceDistance);
                }
                for(int phase=0;phase<material.strikes.Length;phase++)
                {
                    var strike=material.strikes[phase];
                    if(!released[phase] && progress>=strike.impact)
                    {
                        released[phase]=true;ImpactCount++;upcomingStrike=Mathf.Min(phase+1,material.strikes.Length-1);
                        if(material.delivery!=EnemyBossMaterialDelivery.Melee)Launch(material,phase);
                        else EnemyStrongAttackImpactVfx.Play(strike.Origin(transform));
                        actor.AbilityController.NotifyAbilityImpact(ability,phase);
                        StrikeReleased?.Invoke(material,phase);
                        if(token!=generation || !Usable)yield break;
                    }
                    // Low-frame crossings still query one impact. Each physical body is damaged at most once per strike.
                    bool crossed=lastProgress<strike.impact && progress>=strike.impact;
                    if(material.delivery==EnemyBossMaterialDelivery.Melee && released[phase]
                        && (progress<=strike.contactEnd+.0001f || crossed))
                        DealShape(material,phase,strike.Origin(transform),strike.Rotation(transform),sequence,lease);
                    if(token!=generation || !Usable)yield break;
                    UpdateWarning(material,phase,progress);
                }
                lastProgress=progress;
                if(progress>=.999f)break;
            }
            if(token==generation && Usable && entered && AllReleased(material)){CompletedCount++;completed=true;}
        }
        finally
        {
            if(token==generation)
            {
                RestoreSampling();actor?.Movement?.ClearAttackDisplacement();actor?.Movement?.CancelActionLock();cast=null;
                if(material.delivery==EnemyBossMaterialDelivery.Melee || !completed)HideWarnings();
                if(!completed){for(int i=flights.Count-1;i>=0;i--)EndFlight(i);SetRockHeld(false);}
                if(flights.Count==0)CurrentMaterial=null;
            }
        }
    }
    private bool AllReleased(EnemyBossAttackMaterial material)
    {for(int i=0;i<material.strikes.Length;i++)if(!released[i])return false;return true;}

    private void UpdateWarning(EnemyBossAttackMaterial material,int phase,float normalized)
    {
        if(!material.showTelegraph)return;
        var strike=material.strikes[phase];
        float warningStart=phase==0?0f:material.strikes[phase-1].contactEnd;
        if(normalized<warningStart)return;
        if(material.delivery==EnemyBossMaterialDelivery.Melee && normalized>=strike.impact)
        {warnings[phase]?.Hide();return;}
        if(material.delivery!=EnemyBossMaterialDelivery.Melee && released[phase])return;
        float remaining=Mathf.Max(0f,material.ability.ResolvePacedTime(strike.impact,castSpeed)-material.ability.ResolvePacedTime(normalized,castSpeed));
        if(material.delivery==EnemyBossMaterialDelivery.Boulder)remaining+=material.flightSeconds;
        Vector3 origin=strike.Origin(transform);Quaternion rotation=strike.Rotation(transform);
        if(material.delivery==EnemyBossMaterialDelivery.Boulder)origin=committedAim;
        else if(material.delivery==EnemyBossMaterialDelivery.Spit)
        {
            origin=ResolveMuzzle(material);origin.y=transform.position.y;
            Vector3 direction=committedAim-origin;direction.y=0f;
            if(direction.sqrMagnitude>.0001f)rotation=Quaternion.LookRotation(direction);
        }
        var warning=EnsureWarning(material,phase,Mathf.Max(.01f,remaining));
        warning.SetCenter(origin);warning.SetFacing(rotation*Vector3.forward);
        bool threatens=material.IsParryWindowOpen(phase,normalized,remaining)
            && EnemyStrongAttackWarning.PlayerTarget!=null && WouldHit(material.ability,EnemyStrongAttackWarning.PlayerTarget,phase);
        warning.SetRemaining(remaining,threatens);
    }
    private EnemyStrongAttackWarning EnsureWarning(EnemyBossAttackMaterial material,int phase,float lead)
    {
        var strike=material.strikes[phase];
        if(warnings[phase]==null)
        {warnings[phase]=gameObject.AddComponent<EnemyStrongAttackWarning>();warnings[phase].SetRadialProfiles(collection.radialFillProfile,collection.radialBorderProfile);}
        if(!warningShown[phase])
        {
            float size=strike.shape==GroundIndicatorShape.Rectangle?strike.length:strike.radius;
            warnings[phase].Show(size,material.IsStrikeParryable(phase),strike.shape==GroundIndicatorShape.Circle?360f:strike.angle,
                strike.shape==GroundIndicatorShape.Rectangle,true,lead,strike.width*.5f,strike.innerRadius,strike.shape);
            // This rectangle has square ends. The authored strike and approved indicator share that boundary.
            if(strike.shape==GroundIndicatorShape.Rectangle)
                foreach(var indicator in warnings[phase].GetComponentsInChildren<ProceduralGroundIndicator>(true))
                    if(indicator.gameObject.activeSelf)indicator.SetCorridorCapRadius(0f);
            warningShown[phase]=true;
        }
        return warnings[phase];
    }
    public bool WouldHit(EnemyAbilityDefinition ability,CombatTarget target,int phase=-1)
    {
        Resolve();var material=collection!=null?collection.Find(ability):null;
        if(material==null||target==null||!CombatTargetFilter.CanDamage(ownerTarget,target)||material.delivery!=EnemyBossMaterialDelivery.Melee)return false;
        if(phase<0)phase=CurrentMaterial==material?upcomingStrike:0;
        phase=Mathf.Clamp(phase,0,material.strikes.Length-1);var strike=material.strikes[phase];
        int count=strike.Query(transform,previewColliders,TargetMask);
        for(int i=0;i<count;i++)if(CombatTarget.Resolve(previewColliders[i])==target && strike.Intersects(previewColliders[i],transform)
            && HasSight(target,previewColliders[i].ClosestPoint(transform.position+Vector3.up*.8f),ability))return true;
        return false;
    }
    public bool IsParryThreatTo(CombatTarget target)
    {
        var material=CurrentMaterial;
        if(material==null||!entered||!material.ability.IsParryable||material.delivery!=EnemyBossMaterialDelivery.Melee||!Usable)return false;
        for(int phase=0;phase<material.strikes.Length;phase++)
        {
            if(released[phase])continue;
            float remaining=material.ability.ResolvePacedTime(material.strikes[phase].impact,castSpeed)-material.ability.ResolvePacedTime(progress,castSpeed);
            return material.IsParryWindowOpen(phase,progress,remaining) && WouldHit(material.ability,target,phase);
        }
        return false;
    }
    private float ResolveIncomingDamage(EnemyAbilityDefinition ability)
        => ability.ResolveDamage(GetComponent<EnemyRank>()?.Level??1)*actor.RuntimeStats.DamageMultiplier
            *(collection?.Find(ability)?.DamageMultiplier??1f);

    public bool TryGetParryDamageSnapshot(CombatTarget victim, out DamageInfo info)
    {
        info=default;var material=CurrentMaterial;
        if(material==null||victim==null||!IsExecuting||!material.ability.IsParryable
            ||material.delivery!=EnemyBossMaterialDelivery.Melee||!Usable)return false;
        for(int phase=0;phase<material.strikes.Length;phase++)
        {
            if(released[phase])continue;
            Vector3 direction=victim.CurrentVolume.Center-transform.position;direction.y=0f;
            info=new DamageInfo(ResolveIncomingDamage(material.ability),victim.CurrentVolume.Center,gameObject,
                direction.sqrMagnitude>.0001f?direction.normalized:transform.forward,
                sourceAttackSequenceId:sequence,sourceAttackPhaseIndex:phase,enemyAbility:material.ability);
            return true;
        }
        return false;
    }

    private bool HasSight(CombatTarget target,Vector3 point,EnemyAbilityDefinition ability)
    {
        if(!ability.RequireLineOfSight)return true;
        Vector3 origin=transform.position+Vector3.up*.8f;Vector3 delta=point-origin;
        if(delta.sqrMagnitude<.0001f)return true;
        if(Physics.Raycast(origin,delta.normalized,out var hit,delta.magnitude,FlightMask,QueryTriggerInteraction.Ignore))
            return CombatTarget.Resolve(hit.collider)==target;
        return true;
    }
    private void DealShape(EnemyBossAttackMaterial material,int phase,Vector3 origin,Quaternion rotation,int attackSequence,uint attackLease)
    {
        if(!Usable||actor.LeaseVersion!=attackLease)return;
        var strike=material.strikes[phase];int count=strike.Query(transform,colliders,TargetMask,origin,rotation);
        int token=generation;
        for(int i=0;i<count;i++)
        {
            if(token!=generation || !Usable || actor.LeaseVersion!=attackLease)break;
            var collider=colliders[i];var target=CombatTarget.Resolve(collider);
            if(target==null||!CombatTargetFilter.CanDamage(ownerTarget,target)||!strike.Intersects(collider,transform,origin,rotation))continue;
            var receiver=target.DamageReceiver;
            if(receiver==null||receiver.IsDead||hitTargets[phase].Contains(receiver)||!HasSight(target,collider.ClosestPoint(origin+Vector3.up*.8f),material.ability))continue;
            hitTargets[phase].Add(receiver);Vector3 direction=receiver.transform.position-transform.position;direction.y=0;
            float damage=ResolveIncomingDamage(material.ability);
            receiver.TakeDamage(new DamageInfo(damage,collider.ClosestPoint(origin+Vector3.up*.8f),gameObject,
                direction.sqrMagnitude>.0001f?direction.normalized:transform.forward,sourceAttackSequenceId:attackSequence,
                sourceAttackPhaseIndex:phase,enemyAbility:material.ability));DamageCount++;
        }
    }
    private readonly Dictionary<string,Transform> sockets=new Dictionary<string,Transform>();
    private Transform Socket(string name)
    {
        if(string.IsNullOrEmpty(name))return transform;
        if(sockets.TryGetValue(name,out var socket)&&socket!=null)return socket;
        foreach(var node in actor.Animator.GetComponentsInChildren<Transform>(true))if(node.name==name){sockets[name]=node;return node;}
        throw new InvalidOperationException("Boss material socket missing: "+name);
    }
    private Vector3 ResolveMuzzle(EnemyBossAttackMaterial material)=>Socket(material.muzzleBone).TransformPoint(material.muzzleOffset);
    private void Launch(EnemyBossAttackMaterial material,int phase)
    {
        Vector3 start=material.delivery==EnemyBossMaterialDelivery.Boulder?RockPosition():ResolveMuzzle(material);var target=EnemyStrongAttackWarning.PlayerTarget;
        float aimHeight=target!=null?Mathf.Clamp(target.CurrentVolume.Center.y-target.transform.position.y,.7f,1.2f):.8f;
        Vector3 aim=committedAim+Vector3.up*aimHeight;
        var visual=AcquireVisual(material.delivery==EnemyBossMaterialDelivery.Boulder);
        var flight=new Flight{material=material,phase=phase,sequence=sequence,lease=lease,start=start,position=start,
            direction=(aim-start).normalized,landing=committedAim,distance=material.ability.Range,visual=visual,
            damage=ResolveIncomingDamage(material.ability)};
        visual.root.transform.position=start;visual.root.SetActive(true);
        if(visual.bio!=null)visual.bio.Launch(GetComponent<BloodHitTarget>()?.Profile,1f);
        if(material.delivery==EnemyBossMaterialDelivery.Boulder)SetRockHeld(false);
        flights.Add(flight);LaunchCount++;
    }
    private ProjectileVisual AcquireVisual(bool boulder)
    {
        foreach(var visual in visuals)if(!visual.used && visual.boulder==boulder){visual.used=true;return visual;}
        var created=new ProjectileVisual{boulder=boulder,used=true};
        if(!boulder && EnemyProjectileVfxCatalog.Current?.projectile!=null)
        {created.root=Instantiate(EnemyProjectileVfxCatalog.Current.projectile,transform,false);created.bio=created.root.GetComponent<EnemyBioProjectileVisual>();}
        else created.root=CreateRock("Boss boulder projectile");
        created.root.name=boulder?"Boss material boulder":"Boss material spit";visuals.Add(created);return created;
    }
    private GameObject CreateRock(string name)
    {
        var root=new GameObject(name);root.transform.SetParent(transform,false);root.layer=LayerMask.NameToLayer("Enemy");
        root.AddComponent<MeshFilter>().sharedMesh=collection.boulderMesh;root.AddComponent<MeshRenderer>().sharedMaterial=collection.boulderMaterial;
        root.transform.localScale=Vector3.one*collection.boulderVisualRadius;root.SetActive(false);return root;
    }
    public void SetRockHeld(bool held)
    {
        Resolve();if(collection==null)return;
        if(!held && heldBoulder==null)return;
        if(heldBoulder==null)heldBoulder=CreateRock("Held boss boulder");
        heldBoulder.transform.position=RockPosition();
        heldBoulder.transform.rotation=transform.rotation;
        heldBoulder.transform.localScale=Vector3.one*collection.boulderVisualRadius;
        heldBoulder.SetActive(held);
    }
    private Vector3 RockPosition()
    {
        if(boulderLeftHand==null)boulderLeftHand=Socket(collection.boulderLeftHandBone);
        if(boulderRightHand==null)boulderRightHand=Socket(collection.boulderRightHandBone);
        return (boulderLeftHand.position+boulderRightHand.position)*.5f+transform.TransformVector(collection.boulderOffset);
    }
    private void LateUpdate(){if(IsRockHeld)heldBoulder.transform.position=RockPosition();}
    // Support motions have no damaging windows. A single cycle can precede any assembled attack.
    public bool TryPlayMotion(string id,bool holdLastPose=false)
    {
        Resolve();var entry=collection!=null?collection.FindMotion(id):null;
        if(entry==null||!entry.IsPlayable||!Usable||IsExecuting||actor.Movement.IsActionLocked||actor.AnimationBridge.BlocksAttackStart)return false;
        ReleaseHeldPose();LastFailure=null;int token=++generation;lease=actor.LeaseVersion;
        motion=StartCoroutine(PlayMotion(entry,holdLastPose,token));return true;
    }
    private IEnumerator PlayMotion(EnemyBossMaterialCollection.Motion entry,bool hold,int token)
    {
        float elapsed=0f;bool done=false;
        try
        {
            if(entry.id.Contains("WithRock"))SetRockHeld(true);
            if(entry.id=="UnearthRock")SetRockHeld(false);
            actor.Animator.Play(entry.state,0,0f);actor.Movement.ApplyActionLock(entry.runtime.length+.1f);
            while(token==generation && Usable && actor.LeaseVersion==lease)
            {
                yield return null;
                if(token!=generation||!Usable||actor.LeaseVersion!=lease)yield break;
                var state=actor.Animator.GetCurrentAnimatorStateInfo(0);
                if(!state.IsName(entry.state))
                {if(elapsed>.25f){LastFailure="Support motion interrupted.";yield break;}elapsed+=Time.deltaTime;continue;}
                elapsed+=Time.deltaTime;
                if(entry.id=="UnearthRock" && state.normalizedTime>=100f/(entry.runtime.length*entry.runtime.frameRate))SetRockHeld(true);
                if(state.normalizedTime>=.99f)
                {
                    done=true;
                    if(hold){previousAnimatorSpeed=actor.Animator.speed;actor.Animator.Play(entry.state,0,.999f);actor.Animator.speed=0f;poseHeld=true;}
                    else actor.Animator.Play("Locomotion",0,0f);
                    break;
                }
            }
        }
        finally{if(token==generation){motion=null;actor?.Movement?.CancelActionLock();if(!done)SetRockHeld(false);}}
    }
    private void ReleaseHeldPose(){if(poseHeld&&actor!=null&&actor.Animator!=null)actor.Animator.speed=previousAnimatorSpeed;poseHeld=false;}
    private void FixedUpdate()
    {
        if(flights.Count==0)return;
        if(!Usable){Cancel();return;}
        int token=generation;
        for(int i=flights.Count-1;i>=0;i--)
        {
            Flight flight=flights[i];if(flight.lease!=actor.LeaseVersion){EndFlight(i);continue;}
            var material=flight.material;Vector3 next;
            if(material.delivery==EnemyBossMaterialDelivery.Boulder)
            {
                flight.elapsed+=Time.fixedDeltaTime;float fraction=Mathf.Clamp01(flight.elapsed/material.flightSeconds);
                next=Vector3.Lerp(flight.start,flight.landing+Vector3.up*collection.boulderVisualRadius,fraction)
                    +Vector3.up*(4f*material.arcHeight*fraction*(1f-fraction));
                // Geometry blockers cancel the throw. Player bodies do not create an unadvertised midair explosion.
                int blockerMask=FlightMask & ~(1<<LayerMask.NameToLayer("Player"));
                Vector3 step=next-flight.position;
                bool blocked=fraction<.98f && Physics.SphereCast(flight.position,material.projectileRadius,step.normalized,out _,step.magnitude,blockerMask,QueryTriggerInteraction.Ignore);
                if(blocked){warnings[flight.phase]?.Hide();EndFlight(i);continue;}
                if(fraction>=1f)
                {
                    DealShape(material,flight.phase,flight.landing,Quaternion.identity,flight.sequence,flight.lease);
                    if(token!=generation || !Usable)return;
                    EnemyStrongAttackImpactVfx.Play(flight.landing);warnings[flight.phase]?.Hide();EndFlight(i);continue;
                }
                if(warnings[flight.phase]!=null){warnings[flight.phase].SetCenter(flight.landing);warnings[flight.phase].SetRemaining(material.flightSeconds-flight.elapsed,false);}
            }
            else
            {
                float distance=Mathf.Min(flight.distance,material.projectileSpeed*Time.fixedDeltaTime);
                int count=Physics.SphereCastNonAlloc(flight.position,material.projectileRadius,flight.direction,rayHits,distance,FlightMask,QueryTriggerInteraction.Ignore);
                int nearest=-1;for(int hit=0;hit<count;hit++)if(nearest<0||rayHits[hit].distance<rayHits[nearest].distance)nearest=hit;
                if(nearest>=0)
                {
                    var target=CombatTarget.Resolve(rayHits[nearest].collider);
                    if(target!=null && CombatTargetFilter.CanDamage(ownerTarget,target)&&target.DamageReceiver!=null&&hitTargets[flight.phase].Add(target.DamageReceiver))
                    {target.DamageReceiver.TakeDamage(new DamageInfo(flight.damage,rayHits[nearest].point,gameObject,flight.direction,
                        sourceAttackSequenceId:flight.sequence,sourceAttackPhaseIndex:flight.phase,enemyAbility:material.ability));DamageCount++;}
                    if(token!=generation || !Usable)return;
                    warnings[flight.phase]?.Hide();EndFlight(i);continue;
                }
                next=flight.position+flight.direction*distance;flight.distance-=distance;
                if(flight.distance<=0f){warnings[flight.phase]?.Hide();EndFlight(i);continue;}
            }
            flight.position=next;flight.visual.root.transform.position=next;
        }
        if(flights.Count==0 && cast==null)CurrentMaterial=null;
    }
    private void EndFlight(int index)
    {var flight=flights[index];if(flight.visual.bio!=null)flight.visual.bio.Stop();flight.visual.root.SetActive(false);flight.visual.used=false;flights.RemoveAt(index);}
    private void RestoreSampling()
    {if(samplingAnimator!=null && samplingAnimator.updateMode==AnimatorUpdateMode.Fixed)samplingAnimator.updateMode=previousUpdate;samplingAnimator=null;}
    private void HideWarnings(){for(int i=0;i<3;i++){warnings[i]?.Hide();warningShown[i]=false;}}
    public override void Cancel()
    {
        if(cast!=null && entered && CurrentMaterial!=null) AttackCancelled?.Invoke(CurrentMaterial);
        generation++;if(cast!=null){StopCoroutine(cast);cast=null;}if(motion!=null){StopCoroutine(motion);motion=null;}ReleaseHeldPose();RestoreSampling();
        actor?.Movement?.ClearAttackDisplacement();actor?.Movement?.CancelActionLock();
        for(int i=flights.Count-1;i>=0;i--)EndFlight(i);HideWarnings();
        if(heldBoulder!=null)heldBoulder.SetActive(false);CurrentMaterial=null;entered=false;progress=0f;
    }
    public override void ResetForReuse(){Resolve();Cancel();ImpactCount=LaunchCount=CompletedCount=DamageCount=0;LastFailure=null;}
    private void OnDestroy()
    {
        Cancel();foreach(var visual in visuals)if(visual.root!=null)Destroy(visual.root);visuals.Clear();
        if(heldBoulder!=null)Destroy(heldBoulder);
    }
}
