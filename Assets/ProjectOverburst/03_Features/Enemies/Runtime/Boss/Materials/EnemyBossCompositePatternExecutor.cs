using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Selected before the basic material executor for the three opt-in composite attacks.
[DisallowMultipleComponent]
[DefaultExecutionOrder(1001)]
public sealed partial class EnemyBossCompositePatternExecutor : EnemyAbilityExecutor
{
    [SerializeField] EnemyBossCompositePatternSet patterns;
    EnemyActor actor;
    EnemyBossMaterialExecutor basic;
    EnemyMovementReaction reaction;
    CombatTarget owner;
    Coroutine cast;
    int generation,sequence,throwCount;
    uint lease;
    bool entered,prepared,preparationSeen;
    EnemyBossThrowPayload? overridePayload,preparedPayload;
    EnemyBossAttackMaterial current;
    EnemyBossCompositePatternSet.Pattern spit;
    Transform target,mouth,leftHand,rightHand;
    Vector3 aim;
    Quaternion aimRotation;
    float progress,speed=1f,aimDistance;
    AnimatorUpdateMode previousUpdate;
    Animator samplingAnimator;
    readonly bool[] released=new bool[3],shown=new bool[3];
    bool[] emitted=Array.Empty<bool>();
    PendingEmission[] pendingEmissions=Array.Empty<PendingEmission>();
    readonly HashSet<CombatHealth>[] damaged={new HashSet<CombatHealth>(),new HashSet<CombatHealth>(),new HashSet<CombatHealth>()};
    readonly EnemyStrongAttackWarning[] warnings=new EnemyStrongAttackWarning[3];
    readonly Collider[] overlap=new Collider[64];
    readonly RaycastHit[] rayHits=new RaycastHit[64];
    readonly Dictionary<GameObject,EnemyBossBloodSpray> sprays=new Dictionary<GameObject,EnemyBossBloodSpray>();
    readonly List<Visual> visuals=new List<Visual>();
    readonly List<Flight> flights=new List<Flight>();
    readonly List<Add> adds=new List<Add>();
    Visual held;
    EnemyBossBloodSpray activeSpray;
    static readonly WaitForFixedUpdate AfterPhysics=new WaitForFixedUpdate();
    public EnemyBossCompositePatternSet Patterns=>patterns;
    public EnemyBossAttackMaterial CurrentMaterial=>current;
    public float NormalizedTime=>progress;
    public Vector3 BeamOrigin {get;private set;}
    public Vector3 BeamDirection {get;private set;}
    public float BeamLength {get;private set;}
    public int ActiveBeamPhase {get;private set;}=-1;
    public int DamageCount {get;private set;}
    public int ReleaseCount {get;private set;}
    public int EmissionCount {get;private set;}
    public int SummonedCount {get;private set;}
    public int RockThrowCount {get;private set;}
    public int EliteThrowCount {get;private set;}
    public int CompletedCount {get;private set;}
    public int ActiveFlightCount=>flights.Count;
    public int ActiveVisualCount {get{int n=0;foreach(var v in visuals)if(v.used)n++;return n;}}
    public int LiveAddCount {get{PruneAdds();return adds.Count;}}
    public int BloodParticleCount=>activeSpray!=null?activeSpray.ParticleCount:0;
    public bool IsRockHeld=>held!=null && held.payload==null && held.root.activeSelf;
    public bool IsEliteHeld=>held!=null && held.payload==patterns?.elite && held.root.activeSelf;
    public string LastFailure {get;private set;}
    public override bool IsExecuting=>cast!=null || flights.Count>0
        || UsesMotion && (current != null && !executionResult.IsTerminal || actor.AnimationBridge.OwnsMotion(playbackHandle));
    public event Action<EnemyActor> MonsterLanded;
    sealed class Visual {public GameObject root,prefab;public EnemyBossCompositePatternSet.Payload payload;public bool used;}
    sealed class Flight {public Visual visual;public Vector3 start,landing,position,end,velocity;public Quaternion facing;public float time,duration,arc,gravity,roll;public uint lease;public int phase;public bool impact,ballistic;}
    sealed class PendingEmission {public EnemyBossCompositePatternSet.Emission emission;public float normalized;public uint seed;}
    // A private sequence seed keeps replayable spread independent of every other actor's Unity Random state.
    struct SpreadRandom
    {
        uint state;
        public SpreadRandom(uint seed){state=seed==0?0x9E3779B9u:seed;}
        public float Range(float min,float max){state^=state<<13;state^=state>>17;state^=state<<5;return Mathf.Lerp(min,max,(state&0xFFFFFFu)/16777215f);}
    }
    void PlanEmissions()
    {
        var planned=new List<PendingEmission>();
        if(spit!=null)for(int i=0;i<spit.emissions.Length;i++){
            var emission=spit.emissions[i];var strike=current.strikes[emission.phase];
            for(int n=0;n<emission.count;n++){
                uint seed=unchecked((uint)sequence*0x9E3779B9u^lease*0x85EBCA6Bu^(uint)(i+1)*0xC2B2AE35u^(uint)(n+1)*0x27D4EB2Fu);
                var random=new SpreadRandom(seed);
                float maximumJitter=emission.launchJitterSeconds/Mathf.Max(.01f,current.runtimeClip.length);
                planned.Add(new PendingEmission{emission=emission,seed=seed,
                    normalized=Mathf.Min(strike.contactEnd-.0001f,emission.normalizedTime+random.Range(0f,maximumJitter))});
            }
        }
        planned.Sort((a,b)=>a.normalized.CompareTo(b.normalized));pendingEmissions=planned.ToArray();emitted=new bool[pendingEmissions.Length];
    }
    sealed class Add {public EnemyActor actor;public uint lease;public EnemyBossCompositePatternSet.Payload payload;public float wake;public bool ai;}
    int PlayerMask=>1<<LayerMask.NameToLayer("Player");
    int BlockerMask=>~((1<<LayerMask.NameToLayer("Enemy"))|(1<<LayerMask.NameToLayer("Ignore Raycast")));
    bool Usable=>actor!=null && actor.IsLeased && actor.Health!=null && !actor.Health.IsDead && actor.Melee!=null
        && actor.Melee.StatusActionSpeedMultiplier>0f && (reaction==null || !reaction.BlocksAttack);
    public void Configure(EnemyBossCompositePatternSet set){patterns=set;}
    void Resolve()
    {
        if(actor==null)actor=GetComponent<EnemyActor>();if(basic==null)basic=GetComponent<EnemyBossMaterialExecutor>();
        if(owner==null)owner=GetComponent<CombatTarget>();if(reaction==null)reaction=GetComponent<EnemyMovementReaction>();
        if(actor?.Animator==null || mouth!=null)return;
        foreach(var bone in actor.Animator.GetComponentsInChildren<Transform>(true)){
            if(bone.name=="Crustaspikan_ Head")mouth=bone;
            if(bone.name=="Crustaspikan_ L Hand")leftHand=bone;
            if(bone.name=="Crustaspikan_ R Hand")rightHand=bone;
        }
    }
    void Awake()=>Resolve();
    void OnEnable(){Resolve();if(actor?.Health!=null)actor.Health.OnDead+=Died;if(reaction!=null)reaction.ReactionStarted+=Reacted;}
    void OnDisable(){if(actor?.Health!=null)actor.Health.OnDead-=Died;if(reaction!=null)reaction.ReactionStarted-=Reacted;Cancel();ReleaseSummons(true);}
    void Died(CombatHealth health,DamageInfo info){Cancel();ReleaseSummons();}
    void Reacted(){if(reaction==null || reaction.BlocksAttack)Cancel();}
    public override bool Supports(EnemyAbilityDefinition ability)=>patterns!=null && patterns.IsValid
        && (patterns.Find(ability)!=null || patterns.throwMaterial.ability==ability);
    public override bool CanStart(EnemyAbilityDefinition ability,Transform aimTarget)
    {
        Resolve();
        if (UsesMotion) return CanStart(ability, aimTarget, default);
        if (actor != null && actor.AnimationBridge.HasInvalidMotionProfile) return false;
        if(!Supports(ability)||!Usable||aimTarget==null||IsExecuting||basic.IsExecuting||actor.Movement.IsActionLocked||actor.AnimationBridge.BlocksAttackStart)return false;
        var delta=actor.AbilityController.ResolveAimPosition(aimTarget)-transform.position;delta.y=0f;
        return EnemyAttackThreatGeometry.MatchesUseConditions(actor,ability,delta.magnitude,actor.Health.NormalizedHp);
    }
    public void SetNextThrowPayload(EnemyBossThrowPayload payload){overridePayload=payload;}
    EnemyBossThrowPayload ResolvePayload()
    {
        var mode=overridePayload??patterns.throwPayload;
        if(mode==EnemyBossThrowPayload.Alternate)mode=(throwCount&1)==0?EnemyBossThrowPayload.Rock:EnemyBossThrowPayload.Elite;
        if(mode==EnemyBossThrowPayload.Elite && Reserved(patterns.elite)>=patterns.elite.maximumAlive)mode=EnemyBossThrowPayload.Rock;
        return mode;
    }
    public override bool TryStart(EnemyAbilityDefinition ability,int index,Transform aimTarget)
    {
        Resolve();
        if (UsesMotion) return TryStart(ability, index, aimTarget, default);
        if(!CanStart(ability,aimTarget))return false;
        var payload=preparedPayload??ResolvePayload();basic.Cancel();ReleaseHeld();preparedPayload=null;overridePayload=null;prepared=false;
        reaction?.PrepareForAttack();spit=patterns.Find(ability);current=spit!=null?spit.material:patterns.throwMaterial;
        target=aimTarget;aim=actor.AbilityController.ResolveAimPosition(target);aim.y=transform.position.y;
        aimDistance=Mathf.Max(4f,Vector3.Distance(aim,transform.position));var facing=aim-transform.position;facing.y=0f;
        aimRotation=facing.sqrMagnitude>.001f?Quaternion.LookRotation(facing):transform.rotation;transform.rotation=aimRotation;
        sequence=EnemyAttackSequence.Next();lease=actor.LeaseVersion;generation++;progress=0f;entered=false;LastFailure=null;
        speed=actor.Melee.AbilityAnimationSpeed*current.AnimationSpeedMultiplier;
        for(int i=0;i<3;i++){released[i]=shown[i]=false;damaged[i].Clear();}
        PlanEmissions();
        samplingAnimator=actor.Animator;previousUpdate=samplingAnimator.updateMode;samplingAnimator.updateMode=AnimatorUpdateMode.Fixed;
        if(spit==null){basic.ClaimSupportPresentation(this);held=Acquire(payload==EnemyBossThrowPayload.Elite?patterns.elite:null);PlaceHeld(Hands(),aimRotation);throwCount++;}
        GetComponent<EnemyBossCombatDirector>()?.NotifyCommitted(ability);
        cast=StartCoroutine(Execute(generation));return true;
    }
    public override float ResolveCooldown(float cooldown)=>actor?.Melee!=null?actor.Melee.ResolveAbilityCooldown(cooldown):cooldown;
    IEnumerator Execute(int token)
    {
        var material=current;bool completed=false;float wait=0f,last=0f;
        try{
            actor.Movement.ApplyActionLock(material.ability.ResolveExecutionDuration(speed)+.5f);
            actor.AnimationBridge.SetAttackAnimSpeed(material.ability.ResolvePhaseAnimationSpeed(0f,speed));actor.AnimationBridge.PlayAttack(material.ability.AnimatorTrigger);
            while(token==generation && Usable && actor.LeaseVersion==lease){
                yield return AfterPhysics;if(token!=generation||!Usable||actor.LeaseVersion!=lease)yield break;
                if(!actor.AnimationBridge.TryGetAttackMotionTime(material.ability.AnimatorTrigger,material.runtimeClip,out float normalized)){
                    if(entered)break;wait+=Time.fixedDeltaTime;if(wait>.65f){LastFailure="Composite motion did not enter.";yield break;}continue;
                }
                entered=true;progress=Mathf.Clamp01(normalized);if(progress+.0001f<last){LastFailure="Composite timeline regressed.";yield break;}
                speed=actor.Melee.AbilityAnimationSpeed*material.AnimationSpeedMultiplier;
                actor.AnimationBridge.SetAttackAnimSpeed(material.ability.ResolvePhaseAnimationSpeed(progress,speed));actor.Movement.ApplyActionLock(.25f);
                // 시작 시 확정한 aim·aimRotation·aimDistance를 분사와 투척이 끝날 때까지 유지한다.
                ActiveBeamPhase=-1;
                for(int phase=0;phase<material.strikes.Length;phase++){
                    var strike=material.strikes[phase];
                    if(!released[phase] && progress>=strike.impact){released[phase]=true;ReleaseCount++;actor.AbilityController.NotifyAbilityImpact(material.ability,phase);if(spit==null)Throw(phase);}
                    if(token!=generation||!Usable)yield break;
                    bool crossed=last<strike.contactStart && progress>=strike.contactStart;
                    if(spit!=null && released[phase] && (progress<=strike.contactEnd || crossed)){
                        ActiveBeamPhase=phase;Beam(phase);if(token!=generation||!Usable)yield break;
                    }
                    Warning(phase);
                }
                if(spit!=null){
                    for(int i=0;i<emitted.Length;i++)if(!emitted[i] && progress>=pendingEmissions[i].normalized){
                        emitted[i]=true;var item=pendingEmissions[i];
                        if(last>material.strikes[item.emission.phase].contactEnd)continue;
                        Eject(item.emission,item.seed);
                    }
                    if(ActiveBeamPhase<0)activeSpray?.Stop(false);
                }
                last=progress;if(progress>=.999f)break;
            }
            if(token==generation && Usable && entered){completed=true;CompletedCount++;}
        }finally{if(token==generation){RestoreAnimator();cast=null;actor?.Movement?.CancelActionLock();activeSpray?.Stop(false);
            if(spit!=null)HideWarnings();if(!completed){ClearFlights();ReleaseHeld();HideWarnings();}
            if(flights.Count==0)current=null;ActiveBeamPhase=-1;}}
    }
    Vector3 Mouth()=>mouth.TransformPoint(current!=null?current.muzzleOffset:new Vector3(-.55f,.35f,0f));
    float SweepYaw(int phase,float normalized)
    {
        var strike=spit.material.strikes[phase];float t=Mathf.InverseLerp(strike.contactStart,strike.contactEnd,normalized);
        bool reverse=phase>0 && spit.reverseSecondSweep;
        return Mathf.Lerp(reverse?spit.firstSweepEndYaw:spit.firstSweepStartYaw,reverse?spit.firstSweepStartYaw:spit.firstSweepEndYaw,t);
    }
    Vector3 Endpoint(float yaw,float distance,float height=.8f)=>transform.position+aimRotation*Quaternion.Euler(0f,yaw,0f)*Vector3.forward*distance+Vector3.up*height;
    void Beam(int phase)
    {
        BeamOrigin=Mouth();float yaw=SweepYaw(phase,progress);
        Vector3 endpoint=Endpoint(yaw,Mathf.Min(aimDistance,current.ability.Range),1.25f);
        Vector3 planar=endpoint-BeamOrigin;planar.y=0f;float distance=Mathf.Max(1f,planar.magnitude);
        float duration=distance/spit.spraySpeed;
        float vertical=(endpoint.y-BeamOrigin.y+.5f*spit.sprayGravity*duration*duration)/duration;
        Vector3 velocity=planar.normalized*spit.spraySpeed+Vector3.up*vertical;
        BeamDirection=velocity.normalized;BeamLength=distance;
        int token=generation;
        // A curved curtain follows the same launch speed and world gravity as the visible liquid.
        int rays=Mathf.Clamp(Mathf.CeilToInt(2f*spit.sprayHalfAngle*Mathf.Deg2Rad*distance/(spit.beamRadius*1.5f))+1,3,32);
        const int steps=12;
        for(int ray=0;ray<rays;ray++){
            float offset=Mathf.Lerp(-spit.sprayHalfAngle,spit.sprayHalfAngle,ray/(float)(rays-1));
            Vector3 launch=Quaternion.AngleAxis(offset,Vector3.up)*velocity;
            Vector3 previous=BeamOrigin;
            for(int step=1;step<=steps;step++){
                float time=duration*step/steps;
                Vector3 next=BeamOrigin+launch*time+Vector3.down*(.5f*spit.sprayGravity*time*time);
                Vector3 segment=next-previous;float length=segment.magnitude;
                int count=Physics.SphereCastNonAlloc(previous,spit.beamRadius,segment.normalized,rayHits,length,BlockerMask,QueryTriggerInteraction.Ignore);
                float blocker=length;
                for(int i=0;i<count;i++)if(CombatTarget.Resolve(rayHits[i].collider)==null)blocker=Mathf.Min(blocker,rayHits[i].distance);
                for(int i=0;i<count;i++)if(rayHits[i].distance<=blocker+.001f){
                    var victim=CombatTarget.Resolve(rayHits[i].collider);if(victim!=null)Damage(victim,rayHits[i].point,phase);
                    if(token!=generation||!Usable)return;
                }
                if(blocker<length-.001f)break;previous=next;
            }
        }
        if(!sprays.TryGetValue(spit.bloodSpray,out activeSpray)){var root=Instantiate(spit.bloodSpray,transform,false);activeSpray=root.GetComponent<EnemyBossBloodSpray>();sprays.Add(spit.bloodSpray,activeSpray);}
        activeSpray.Place(BeamOrigin,velocity,duration,spit.sprayHalfAngle,spit.sprayGravity,sequence);
        activeSpray.Begin(GetComponent<BloodHitTarget>()?.Profile,spit.sprayScale);
    }
    void Damage(CombatTarget victim,Vector3 point,int phase,bool knockdown=false)
    {
        if(!Usable || actor.LeaseVersion!=lease || !CombatTargetFilter.CanDamage(owner,victim) || victim.DamageReceiver==null || victim.DamageReceiver.IsDead || !damaged[phase].Add(victim.DamageReceiver))return;
        float damage=current.ability.ResolveDamage(GetComponent<EnemyRank>()?.Level??1)*actor.RuntimeStats.DamageMultiplier*current.DamageMultiplier;
        victim.DamageReceiver.TakeDamage(new DamageInfo(damage,point,gameObject,BeamDirection,sourceAttackSequenceId:sequence,sourceAttackPhaseIndex:phase,enemyAbility:current.ability,knocksDownPlayer:knockdown));DamageCount++;
    }
    void Warning(int phase)
    {
        if(!current.showTelegraph)return;var strike=current.strikes[phase];
        if(spit!=null && progress>=strike.contactEnd){warnings[phase]?.Hide();return;}
        if(spit==null && released[phase])return;
        if(phase>0 && progress<current.strikes[phase-1].contactEnd)return;
        float lead=Mathf.Max(.01f,current.ability.ResolvePacedTime(strike.impact,speed)-current.ability.ResolvePacedTime(progress,speed));
        if(spit==null)lead+=held?.payload?.trajectory==EnemyBossPayloadTrajectory.Ballistic
            ?BallisticDuration(held.payload,Hands(),Ground(aim)+PayloadCenter(held.payload)):current.flightSeconds;
        if(warnings[phase]==null){warnings[phase]=gameObject.AddComponent<EnemyStrongAttackWarning>();warnings[phase].SetRadialProfiles(basic.Collection.radialFillProfile,basic.Collection.radialBorderProfile);}
        if(!shown[phase]){warnings[phase].Show(strike.shape==GroundIndicatorShape.Rectangle?strike.length:strike.radius,false,strike.angle,strike.shape==GroundIndicatorShape.Rectangle,true,lead,strike.width*.5f,strike.innerRadius,strike.shape);shown[phase]=true;
            if(strike.shape==GroundIndicatorShape.Rectangle)foreach(var indicator in warnings[phase].GetComponentsInChildren<ProceduralGroundIndicator>(true))indicator.SetCorridorCapRadius(0f);}
        warnings[phase].SetCenter(spit==null?aim:transform.position);warnings[phase].SetFacing(aimRotation*Vector3.forward);warnings[phase].SetRemaining(lead,false);
    }
    Visual Acquire(EnemyBossCompositePatternSet.Payload payload)
    {
        GameObject prefab=payload?.flightVisual;
        foreach(var visual in visuals)if(!visual.used && visual.prefab==prefab){visual.used=true;visual.payload=payload;visual.root.transform.localScale=Vector3.one*(payload?.visualScale??basic.Collection.boulderVisualRadius);return visual;}
        var created=new Visual{prefab=prefab,payload=payload,used=true};
        if(prefab!=null)created.root=Instantiate(prefab,transform,false);
        else{created.root=new GameObject("Composite held/flight rock");created.root.transform.SetParent(transform,false);created.root.AddComponent<MeshFilter>().sharedMesh=basic.Collection.boulderMesh;created.root.AddComponent<MeshRenderer>().sharedMaterial=basic.Collection.boulderMaterial;}
        created.root.transform.localScale=Vector3.one*(payload?.visualScale??basic.Collection.boulderVisualRadius);
        created.root.SetActive(false);visuals.Add(created);return created;
    }
    void Free(Visual visual){if(visual==null)return;visual.root.SetActive(false);visual.used=false;}
    Vector3 Hands()
    {
        bool elite = held?.payload == patterns?.elite || preparedPayload == EnemyBossThrowPayload.Elite || eliteGripReleaseUntil > Time.time;
        Vector3 offset = elite ? patterns.eliteHoldOffset : basic.Collection.boulderOffset;
        Vector3 left=leftHand.position,right=rightHand.position;
        eliteGrip?.ReadSourcePositions(ref left,ref right);
        return EnemyBossPayloadSocket.Position(left,right,transform.rotation,offset);
    }
    void ReleaseHeld(){eliteGrip?.Restore();basic?.ReleaseSupportPresentation(this);eliteGripReleaseUntil=0f;if(held!=null)Free(held);held=null;extractionAnchored=false;}
    static Vector3 PayloadCenter(EnemyBossCompositePatternSet.Payload payload)=>Vector3.up*payload.landingCenterHeight*payload.visualScale;
    static float BallisticDuration(EnemyBossCompositePatternSet.Payload payload,Vector3 start,Vector3 end)
    {
        float apex=Mathf.Max(start.y,end.y)+payload.arcHeight;
        return Mathf.Max(.05f,Mathf.Sqrt(2f*(apex-start.y)/payload.gravity)+Mathf.Sqrt(2f*(apex-end.y)/payload.gravity));
    }
    Flight CreateFlight(Visual visual,Vector3 start,Vector3 landing,float duration,float arc,int phase,bool impact)
    {
        var payload=visual.payload;Vector3 end=landing+(payload==null?Vector3.up*basic.Collection.boulderVisualRadius:PayloadCenter(payload));
        var flight=new Flight{visual=visual,start=start,position=start,landing=landing,end=end,duration=duration,arc=arc,facing=aimRotation,phase=phase,lease=lease,impact=impact};
        if(payload?.trajectory==EnemyBossPayloadTrajectory.Ballistic){
            flight.ballistic=true;flight.gravity=payload.gravity;float apex=Mathf.Max(start.y,end.y)+arc;
            flight.duration=Mathf.Max(.05f,Mathf.Sqrt(2f*(apex-start.y)/payload.gravity)+Mathf.Sqrt(2f*(apex-end.y)/payload.gravity));
            flight.velocity=(end-start)/flight.duration;
            flight.velocity.y=Mathf.Sqrt(2f*payload.gravity*(Mathf.Max(start.y,end.y)+arc-start.y));
        }
        return flight;
    }
    void Throw(int phase)
    {
        if(held==null)return;var payload=held.payload;
        Vector3 start=Hands();PlaceHeld(start,aimRotation);flights.Add(CreateFlight(held,start,Ground(aim),current.flightSeconds,current.arcHeight,phase,true));
        if(payload==null)RockThrowCount++;else {EliteThrowCount++;eliteGripReleaseUntil=Time.time+patterns.eliteGripReleaseSeconds;}held=null;
    }
    void Eject(EnemyBossCompositePatternSet.Emission emission,uint seed)
    {
        if(emission.count==0 || Reserved(emission.payload)>=emission.payload.maximumAlive)return;
        var random=new SpreadRandom(seed);
        float yaw=SweepYaw(emission.phase,progress)+emission.yawOffset+random.Range(-emission.yawScatter,emission.yawScatter);
        float distance=Mathf.Clamp(emission.landingDistance+random.Range(-emission.scatter,emission.scatter),2f,current.ability.Range-1f);
        Vector3 landing=Endpoint(yaw,distance,0f);
        Vector3 start=Mouth()+aimRotation*new Vector3(random.Range(-emission.originScatter,emission.originScatter),
            random.Range(-emission.originScatter*.5f,emission.originScatter*.5f),random.Range(-.15f,.25f));
        var visual=Acquire(emission.payload);visual.root.SetActive(true);visual.root.transform.position=start;
        var flight=CreateFlight(visual,start,Ground(landing),emission.payload.flightSeconds,
            emission.payload.arcHeight+random.Range(0f,emission.apexScatter),emission.phase,false);
        flight.facing=Quaternion.LookRotation(Vector3.ProjectOnPlane(flight.end-start,Vector3.up));
        flight.roll=random.Range(-45f,45f);flights.Add(flight);EmissionCount++;
    }
    Vector3 Ground(Vector3 point){if(Physics.Raycast(point+Vector3.up*16f,Vector3.down,out var hit,32f,BlockerMask&~PlayerMask,QueryTriggerInteraction.Ignore))point.y=hit.point.y;else point.y=transform.position.y;return point;}
    void PruneAdds(){for(int i=adds.Count-1;i>=0;i--)if(adds[i].actor==null || !adds[i].actor.IsLeased || adds[i].actor.LeaseVersion!=adds[i].lease || adds[i].actor.Health.IsDead)adds.RemoveAt(i);}
    int Reserved(EnemyBossCompositePatternSet.Payload payload){PruneAdds();int count=0;foreach(var add in adds)if(add.payload.definition==payload.definition)count++;foreach(var flight in flights)if(flight.visual.payload?.definition==payload.definition)count++;return count;}
    void Land(Flight flight)
    {
        int token=generation;
        if(flight.impact){BeamDirection=(flight.landing-flight.start).normalized;var strike=current.strikes[flight.phase];
            int count=strike.Query(transform,overlap,PlayerMask,flight.landing,Quaternion.identity);
            for(int i=0;i<count;i++)if(strike.Intersects(overlap[i],transform,flight.landing,Quaternion.identity)){var victim=CombatTarget.Resolve(overlap[i]);if(victim!=null)Damage(victim,overlap[i].ClosestPoint(flight.landing+Vector3.up*.8f),flight.phase,patterns.eliteImpactKnockdown&&flight.visual.payload==patterns.elite);if(token!=generation||!Usable)return;}
            EnemyStrongAttackImpactVfx.Play(flight.landing);warnings[flight.phase]?.Hide();
        }
        var payload=flight.visual.payload;if(payload==null)return;
        var service=EnemySpawnService.Current;if(service==null){LastFailure="No spawn service for boss payload.";return;}
        if(!service.RegisterAdditionalCatalog(patterns.summonCatalog,out string error)){LastFailure=error;return;}
        if(service.TrySpawn(new EnemySpawnRequest(payload.definition,flight.landing,aimRotation,target,gameObject,transform,transform.parent,
            seed:sequence,context:GetComponent<EnemyRank>()?.Encounter??EncounterContext.Test),out var summoned)){
            bool ai=summoned.AI.enabled;summoned.AI.enabled=false;summoned.Movement.ApplyActionLock(payload.wakeSeconds);
            adds.Add(new Add{actor=summoned,lease=summoned.LeaseVersion,payload=payload,wake=Time.time+payload.wakeSeconds,ai=ai});SummonedCount++;MonsterLanded?.Invoke(summoned);
        }else LastFailure="Boss payload spawn rejected: "+payload.definition.EnemyId;
    }
    void FixedUpdate()
    {
        if(IsExecuting && !Usable){Cancel();return;}int token=generation;
        for(int i=flights.Count-1;i>=0;i--){var flight=flights[i];if(actor.LeaseVersion!=flight.lease){Free(flight.visual);flights.RemoveAt(i);continue;}
            flight.time+=Time.fixedDeltaTime;float t=Mathf.Clamp01(flight.time/flight.duration);
            Vector3 next=flight.ballistic
                ?flight.start+flight.velocity*flight.time+Vector3.down*(.5f*flight.gravity*flight.time*flight.time)
                :Vector3.Lerp(flight.start,flight.end,t)+Vector3.up*(4f*flight.arc*t*(1f-t));
            if(t>=1f)next=flight.end;
            var step=next-flight.position;
            if(t<.98f && step.sqrMagnitude>.000001f && Physics.SphereCast(flight.position,.15f,step.normalized,out _,step.magnitude,BlockerMask&~PlayerMask,QueryTriggerInteraction.Ignore)){
                if(flight.impact)warnings[flight.phase]?.Hide();Free(flight.visual);flights.RemoveAt(i);continue;}
            flight.position=next;flight.visual.root.transform.position=next;flight.visual.root.transform.rotation=flight.facing;
            if(flight.ballistic){
                float verticalSpeed=flight.velocity.y-flight.gravity*flight.time;
                float pitch=Mathf.Clamp(-Mathf.Atan2(verticalSpeed,new Vector2(flight.velocity.x,flight.velocity.z).magnitude)*Mathf.Rad2Deg,
                    -flight.visual.payload.airPitch*.5f,flight.visual.payload.airPitch);
                pitch*=1f-Mathf.SmoothStep(0f,1f,Mathf.InverseLerp(.8f,1f,t));
                flight.visual.root.transform.rotation=flight.facing*Quaternion.Euler(pitch,0f,flight.roll*(1f-Mathf.SmoothStep(0f,1f,t)));
            }
            flight.visual.root.transform.localScale=Vector3.one*(flight.visual.payload?.visualScale??basic.Collection.boulderVisualRadius);
            if(t>=1f){Land(flight);if(token!=generation)return;Free(flight.visual);flights.RemoveAt(i);}
            else if(flight.impact){warnings[flight.phase]?.SetCenter(flight.landing);warnings[flight.phase]?.SetRemaining(flight.duration-flight.time,false);}
        }
        if (UsesMotion) TryFinishOwnedExecution();
        else if(cast==null && flights.Count==0 && current!=null){basic.ReleaseSupportPresentation(this);current=null;}
        PruneAdds();foreach(var add in adds)if(add.wake>0f && Time.time>=add.wake){add.actor.Movement.CancelActionLock();add.actor.AI.enabled=add.ai;add.wake=0f;}
    }
    void LateUpdate()
    {
        Resolve();if(patterns==null || actor?.Animator==null)return;
        if (UsesMotion) { ObserveOwnedPreparation(); FitEliteHands(); return; }
        var state=actor.Animator.GetCurrentAnimatorStateInfo(0);bool extracting=state.IsName("Material_UnearthRock");
        if(extracting && !preparationSeen){basic.ClaimSupportPresentation(this);preparedPayload=ResolvePayload();preparationSeen=true;prepared=true;extractionAnchored=false;}
        if(!extracting && !state.IsName("Material_WalkForwardWithRock") && !state.IsName("Material_WalkBackwardsWithRock"))preparationSeen=false;
        bool extractionPlaced=false;
        if(prepared && (extracting || state.IsName("Material_WalkForwardWithRock") || state.IsName("Material_WalkBackwardsWithRock")))
        {
            var clip=basic.Collection.FindMotion("UnearthRock").runtime;
            float frame=extracting?state.normalizedTime*clip.length*clip.frameRate:Mathf.Max(patterns.eliteFullSizeFrame,EnemyBossPayloadSocket.RockRevealFrame);
            if(!extracting || frame>=(preparedPayload==EnemyBossThrowPayload.Elite?patterns.eliteRevealFrame:EnemyBossPayloadSocket.RockRevealFrame))
            { PlacePreparedPayload(frame,transform.rotation);extractionPlaced=true; }
        }
        if(held!=null && !extractionPlaced)PlaceHeld(Hands(),transform.rotation);
        FitEliteHands();
        if(prepared && !basic.IsExecuting && !extracting && !state.IsName("Material_WalkForwardWithRock") && !state.IsName("Material_WalkBackwardsWithRock") && cast==null){ReleaseHeld();prepared=false;preparedPayload=null;}
    }
    void HideWarnings(){foreach(var warning in warnings)warning?.Hide();}
    void RestoreAnimator(){if(samplingAnimator!=null)samplingAnimator.updateMode=previousUpdate;samplingAnimator=null;}
    void ClearFlights(){foreach(var flight in flights)Free(flight.visual);flights.Clear();}
    public void ReleaseSummons(bool deferred = false)
    {
        var service = EnemySpawnService.Current;
        foreach (var add in adds)
            if (service != null && add.actor != null && add.actor.IsLeased && add.actor.LeaseVersion == add.lease)
            {
                if (deferred) service.ReleaseDeferred(add.actor, add.lease);
                else service.Release(add.actor);
            }
        adds.Clear();
    }
    public override void Cancel()
    {
        if (UsesMotion) { CancelOwnedExecution(); return; }
        generation++;pendingEmissions=Array.Empty<PendingEmission>();emitted=Array.Empty<bool>();if(cast!=null)StopCoroutine(cast);cast=null;RestoreAnimator();ClearFlights();ReleaseHeld();HideWarnings();
        foreach(var visual in sprays.Values)visual.Stop(true);activeSpray=null;current=null;spit=null;prepared=false;preparedPayload=null;ActiveBeamPhase=-1;
        actor?.Movement?.CancelActionLock();
    }
    public override void ResetForReuse(){Resolve();Cancel();ReleaseSummons();overridePayload=null;preparationSeen=false;throwCount=0;DamageCount=ReleaseCount=EmissionCount=SummonedCount=RockThrowCount=EliteThrowCount=CompletedCount=0;LastFailure=null;}
    void OnDestroy(){Cancel();ReleaseSummons(true);foreach(var visual in visuals)if(visual.root!=null)Destroy(visual.root);foreach(var spray in sprays.Values)if(spray!=null)Destroy(spray.gameObject);}
}
