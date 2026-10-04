using System;
using System.Collections;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static partial class MonsterWeakAttackPlayerLoopVerifier
{
    public const int ProjectileFixtureRevision = 3;
    static IEnumerator RunProjectileCase(JObject row,int fps,bool cancelBeforeImpact=false)
    {
        if ((bool?)row["verifyActualCrowdApproach"] == true)
        {
            if (cancelBeforeImpact) throw new InvalidOperationException("The crowd case has its own bounded autonomous scenario.");
            yield return RunActualCrowdApproachCase(row, fps);
            yield break;
        }
        Time.captureDeltaTime=1f/fps;
        string id=((string)row["cardKey"]).Substring("runtime:".Length);
        var definition=AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/ProjectOverburst/Resources/Enemies/Themes/Definitions/"+id+".asset");
        var ability=Enumerable.Range(0,definition.AbilitySet.Count).Select(definition.AbilitySet.GetAbility)
            .Single(a=>a.WeakAttackExecution?.SelectionKey==(string)row["selectionKey"]);
        var serviceRoot=new GameObject("Saved weak projectile test services");owned.Add(serviceRoot);
        var poolRoot=new GameObject("Inactive projectile test pool");poolRoot.transform.SetParent(serviceRoot.transform,false);poolRoot.SetActive(false);
        var pool=serviceRoot.AddComponent<EnemyPoolService>();pool.Configure(poolRoot.transform,0);
        var catalog=ScriptableObject.CreateInstance<EnemyCatalog>();owned.Add(catalog);catalog.Configure(new[]{definition});
        var service=serviceRoot.AddComponent<EnemySpawnService>();service.Configure(catalog,pool);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);owned.Add(floor);
        floor.transform.position=new Vector3(0,-.5f,0);floor.transform.localScale=new Vector3(100,1,100);
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab");
        var body=prefab.GetComponentsInChildren<CapsuleCollider>(true).Single(c=>c.enabled&&!c.isTrigger);
        var volume=prefab.GetComponent<CombatTarget>().ResolveVolumeAtRootPosition(Vector3.zero);
        var victim=new GameObject("Actual saved player capsule projectile target");owned.Add(victim);victim.layer=prefab.layer;
        victim.transform.position=new Vector3(0,0,Mathf.Max(ability.MinimumRange+.5f,ability.WeakAttackExecution.StationaryStartRange-.75f));
        var collider=victim.AddComponent<CapsuleCollider>();collider.center=prefab.transform.InverseTransformPoint(body.transform.TransformPoint(body.center));
        collider.radius=body.radius*Mathf.Max(Mathf.Abs(body.transform.lossyScale.x),Mathf.Abs(body.transform.lossyScale.z));
        collider.height=body.height*Mathf.Abs(body.transform.lossyScale.y);
        var target=victim.AddComponent<CombatTarget>();target.Configure(CombatTeam.PlayerParty,false);target.ConfigureVolume(volume.Center,volume.Radius,volume.HalfHeight*2);
        var health=target.DamageReceiver;health.SetMaxHp(100000,true);
        var settings=new SerializedObject(health);settings.FindProperty("showDamageNumbers").boolValue=false;settings.ApplyModifiedPropertiesWithoutUndo();
        var damage=new JArray();health.OnDamaged+=(_,info)=>damage.Add(new JObject{["phase"]=info.sourceAttackPhaseIndex,["damage"]=info.damage,
            ["sequence"]=info.sourceAttackSequenceId,["repeatReactionSuppressed"]=info.suppressRepeatedAttackReaction});
        var request=new EnemySpawnRequest(definition,Vector3.zero,Quaternion.identity,victim.transform,null,victim.transform,null,1,1,91);
        if(!service.TrySpawn(request,out var actor))throw new InvalidOperationException("Saved projectile Actor spawn failed: "+id);
        foreach(var component in actor.GetComponentsInChildren<MonoBehaviour>(true))
            if(component!=null&&new[]{"EnemyAIController","EnemyCrowdAgent","EnemySensor","RunFallGuard"}.Contains(component.GetType().Name))component.enabled=false;
        actor.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        var executor=actor.GetComponent<EnemyThemeSpecialExecutor>();
        EnemyActor peerActor=null;EnemyThemeSpecialExecutor peerExecutor=null;
        bool concurrentRequested=(bool?)row["verifyConcurrentProjectiles"]==true,peerStarted=true;
        if(concurrentRequested)
        {
            Vector3 peerPosition=Vector3.right*1.8f;
            if(!service.TrySpawn(new EnemySpawnRequest(definition,peerPosition,Quaternion.LookRotation(victim.transform.position-peerPosition),victim.transform,null,victim.transform,null,1,1,92),out peerActor))
                throw new InvalidOperationException("Concurrent saved Actor spawn failed.");
            foreach(var component in peerActor.GetComponentsInChildren<MonoBehaviour>(true))
                if(component!=null&&new[]{"EnemyAIController","EnemyCrowdAgent","EnemySensor","RunFallGuard"}.Contains(component.GetType().Name))component.enabled=false;
            peerActor.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;peerExecutor=peerActor.GetComponent<EnemyThemeSpecialExecutor>();
        }
        yield return null;yield return new WaitForFixedUpdate();yield return null;
        Physics.SyncTransforms();Vector3 start=actor.transform.position;
        bool blockedFallback=ability.WeakAttackExecution.RequiresBlockedApproach;
        bool rejectedBeforeBlocked=true,readyAfterBlocked=true,actionReleasedWhileFlight=false;
        bool movingRejected=true,closeRejected=true,blockedLineRejected=true,cooldownRejected=true;float freeTravel=0f;
        if(blockedFallback)
        {
            rejectedBeforeBlocked=!actor.AbilityController.TryStartAbility(ability,victim.transform);
            actor.AI.SetTarget(victim.transform);
            typeof(EnemyAIController).GetMethod("ChangeToChase",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(actor.AI,null);
            var fallback=actor.GetComponent<EnemyBlockedApproachProjectileExecutor>();
            if((bool?)row["verifyFallbackPolicies"]==true)
            {
                actor.Movement.SetDestination(victim.transform.position,.1f,EnemyLocomotionMode.Walk);
                Vector3 freeStart=actor.transform.position;float freeDeadline=Time.time+1.4f;
                while(Time.time<freeDeadline){movingRejected&=!fallback.HasFallbackPermission(ability);yield return null;}
                freeTravel=Vector3.Distance(freeStart,actor.transform.position);movingRejected&=freeTravel>.15f;
                actor.Movement.StopMovement();
                var rig=actor.GetComponent<Rigidbody>();if(rig!=null)rig.position=start;actor.transform.position=start;
                fallback.ResetForReuse();Physics.SyncTransforms();
            }
            // The motor restores its own constraints on movement, so Rigidbody FreezePosition is not a blocked-path fixture.
            // Retain the real Chase/destination intent but control displacement while testing the no-progress detector.
            actor.Movement.enabled=false;
            actor.Movement.SetDestination(victim.transform.position,.1f,EnemyLocomotionMode.Walk);
            float fallbackDeadline=Time.time+2.4f;
            while(!fallback.HasFallbackPermission(ability)&&Time.time<fallbackDeadline)yield return null;
            readyAfterBlocked=fallback.HasFallbackPermission(ability);
            if((bool?)row["verifyFallbackPolicies"]==true&&readyAfterBlocked)
            {
                Vector3 far=victim.transform.position;victim.transform.position=actor.transform.position+Vector3.forward*1.5f;
                Physics.SyncTransforms();closeRejected=!fallback.HasFallbackPermission(ability);victim.transform.position=far;
                var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);owned.Add(wall);
                wall.transform.position=(actor.transform.position+far)*.5f+Vector3.up;
                wall.transform.localScale=new Vector3(10,4,.3f);Physics.SyncTransforms();
                blockedLineRejected=!fallback.HasFallbackPermission(ability);wall.GetComponent<Collider>().enabled=false;Physics.SyncTransforms();
            }
            actor.Movement.StopMovement();actor.Movement.enabled=true;
        }
        bool started=actor.AbilityController.TryStartAbility(ability,victim.transform),cancelled=false;
        if(peerActor!=null)peerStarted=peerActor.AbilityController.TryStartAbility(ability,victim.transform);
        bool occlusionRequested=(bool?)row["verifyProjectileOcclusion"]==true;
        if(occlusionRequested&&started)
        {
            var flightWall=GameObject.CreatePrimitive(PrimitiveType.Cube);owned.Add(flightWall);
            flightWall.transform.position=(actor.transform.position+victim.transform.position)*.5f+Vector3.up*1.9f;
            flightWall.transform.localScale=new Vector3(10,4,.3f);Physics.SyncTransforms();
        }
        bool nextMeleeRequested=(bool?)row["verifyNextMeleeDuringFlight"]==true;
        bool nextMeleeAttempted=false,nextMeleeStarted=false,flightPreservedByAICleanup=false,nextMeleeStartedDuringFlight=false;
        GameObject nextVictim=null;
        string terminalPolicy=(string)row["terminalProjectilePolicy"];
        int damageBeforeTerminal=0,launchesBeforeTerminal=0;
        uint leaseBeforeTerminal=actor.LeaseVersion;
        bool terminalPolicyVerified=true;
        float deadline=Time.time+ability.ResolveExecutionDuration(actor.Melee.AbilityAnimationSpeed)+ability.Range/10f+3f;
        while(started&&(executor.IsExecuting||executor.HasProjectile||peerExecutor!=null&&(peerExecutor.IsExecuting||peerExecutor.HasProjectile)||nextMeleeStarted&&actor.AbilityController.IsExecuting)&&Time.time<deadline)
        {
            if(blockedFallback&&!executor.IsWeakProjectileActionExecuting&&executor.HasProjectile&&!actor.Movement.IsActionLocked)
                actionReleasedWhileFlight=true;
            if(nextMeleeRequested&&!nextMeleeAttempted&&!executor.IsWeakProjectileActionExecuting&&executor.HasProjectile&&!actor.Movement.IsActionLocked)
            {
                nextMeleeAttempted=true;
                actor.AI.CancelAttack();
                flightPreservedByAICleanup=executor.HasProjectile&&!actor.AbilityController.IsExecuting;
                var close=Enumerable.Range(0,definition.AbilitySet.Count).Select(definition.AbilitySet.GetAbility)
                    .First(a=>!a.IsTelegraphedStrongAttack&&a.WeakAttackExecution!=null&&a.WeakAttackExecution.IsStationaryMotion
                        &&a.ExecutionMode==EnemyAbilityExecutionMode.MeleeArc);
                nextVictim=new GameObject("Next melee target while previous weak shots fly");owned.Add(nextVictim);nextVictim.layer=prefab.layer;
                nextVictim.transform.position=actor.transform.position+actor.transform.forward*Mathf.Max(
                    actor.GetComponent<EnemyCrowdAgent>().BodyRadius+collider.radius+.1f,close.WeakAttackExecution.StationaryStartRange*.85f);
                var nextBody=nextVictim.AddComponent<CapsuleCollider>();nextBody.center=collider.center;nextBody.radius=collider.radius;nextBody.height=collider.height;
                var nextTarget=nextVictim.AddComponent<CombatTarget>();nextTarget.Configure(CombatTeam.PlayerParty,false);
                nextTarget.ConfigureVolume(volume.Center,volume.Radius,volume.HalfHeight*2);nextTarget.DamageReceiver.SetMaxHp(100000,true);
                foreach(var shape in actor.GetComponentsInChildren<Collider>(true))Physics.IgnoreCollision(shape,nextBody);
                Physics.SyncTransforms();
                nextMeleeStarted=actor.AbilityController.TryStartAbility(close,nextVictim.transform);
                nextMeleeStartedDuringFlight=nextMeleeStarted&&executor.HasProjectile&&actor.AbilityController.IsExecuting;
            }
            if(cancelBeforeImpact&&!cancelled&&executor.LaunchCount>0){actor.AbilityController.Cancel();cancelled=true;}
            if(terminalPolicy!=null&&!cancelled&&executor.LaunchCount>0
                &&(terminalPolicy!="explicit-after-action"||!executor.IsWeakProjectileActionExecuting&&executor.HasProjectile&&!actor.Movement.IsActionLocked))
            {
                damageBeforeTerminal=damage.Count;launchesBeforeTerminal=executor.LaunchCount;
                if(terminalPolicy=="death")
                {
                    actor.Health.TakeDamage(new DamageInfo(999999,actor.transform.position,victim,suppressDefaultHitVfx:true));
                    terminalPolicyVerified=actor.Health.IsDead&&!executor.HasProjectile;
                }
                else if(terminalPolicy=="lease")service.Release(actor);
                else if(terminalPolicy=="explicit-after-action")actor.AbilityController.Cancel();
                else throw new InvalidOperationException("Unknown terminal projectile policy.");
                cancelled=true;
            }
            yield return null;
        }
        if(terminalPolicy=="lease")
        {
            yield return null;yield return new WaitForFixedUpdate();yield return null;
            bool reused=service.TrySpawn(request,out var nextActor);
            terminalPolicyVerified=reused&&nextActor==actor&&actor.LeaseVersion!=leaseBeforeTerminal&&!executor.HasProjectile;
            if(reused)foreach(var component in nextActor.GetComponentsInChildren<MonoBehaviour>(true))
                if(component!=null&&new[]{"EnemyAIController","EnemyCrowdAgent","EnemySensor","RunFallGuard"}.Contains(component.GetType().Name))component.enabled=false;
        }
        if(cancelBeforeImpact||terminalPolicy!=null)for(int i=0;i<6;i++)yield return null;
        int actorCount=peerActor==null?1:2;
        int count=terminalPolicy!=null?damageBeforeTerminal:cancelBeforeImpact||occlusionRequested?0:ability.HitCount*actorCount;
        int level=actor.GetComponent<EnemyRank>()?.Level??1;
        float total=ability.UsesLevelDamageBudget?Mathf.Max(1f,OverburstCombatBalance.RoundStat(OverburstCombatBalance.ReferenceEffectiveHealth(level)*ability.ReferencePatternDamagePercent/100f)):ability.Damage;
        if(!EnemyWeakProjectileDamageBudget.TryCreate(total*actor.RuntimeStats.DamageMultiplier,ability.HitCount,out var budget))
            throw new InvalidOperationException("Selected projectile damage budget invalid.");
        bool stationary=Vector2.Distance(new Vector2(start.x,start.z),new Vector2(actor.transform.position.x,actor.transform.position.z))<.04f;
        float expectedDamage=terminalPolicy!=null?Enumerable.Range(0,count).Sum(budget.ForPhase):cancelBeforeImpact||occlusionRequested?0:budget.Total*actorCount;
        int observedLaunches=terminalPolicy=="lease"?launchesBeforeTerminal:executor.LaunchCount+(peerExecutor?.LaunchCount??0);
        int observedImpacts=executor.ImpactCount+(peerExecutor?.ImpactCount??0);
        bool phaseOrder=peerActor==null?damage.Select(d=>(int)d["phase"]).OrderBy(i=>i).SequenceEqual(Enumerable.Range(0,count))
            :damage.GroupBy(d=>(int)d["sequence"]).Count()==2&&damage.GroupBy(d=>(int)d["sequence"])
                .All(g=>g.Select(d=>(int)d["phase"]).OrderBy(i=>i).SequenceEqual(Enumerable.Range(0,ability.HitCount)));
        bool reactions=peerActor==null?damage.Select((d,i)=>(bool)d["repeatReactionSuppressed"]==(i>0)).All(v=>v)
            :damage.GroupBy(d=>(int)d["sequence"]).All(g=>g.Select((d,i)=>(bool)d["repeatReactionSuppressed"]==(i>0)).All(v=>v));
        bool pass=started&&peerStarted&&!executor.IsExecuting&&!executor.HasProjectile&&stationary
            &&damage.Count==count&&phaseOrder
            &&Mathf.Abs(damage.Sum(d=>(float)d["damage"])-expectedDamage)<.001f
            &&(terminalPolicy!=null?cancelled&&terminalPolicyVerified&&observedLaunches>0:cancelBeforeImpact?cancelled&&executor.LaunchCount==ability.ProjectilesPerRelease:observedLaunches==ability.HitCount*actorCount&&observedImpacts==count)
            &&(!blockedFallback||rejectedBeforeBlocked&&readyAfterBlocked&&(cancelBeforeImpact||terminalPolicy!=null||occlusionRequested||actionReleasedWhileFlight))
            &&reactions;
        if(blockedFallback&&started&&terminalPolicy==null)cooldownRejected=!actor.AbilityController.TryStartAbility(ability,victim.transform);
        pass&=movingRejected&&closeRejected&&blockedLineRejected&&cooldownRejected;
        if(nextMeleeRequested)pass&=nextMeleeAttempted&&nextMeleeStarted&&flightPreservedByAICleanup&&nextMeleeStartedDuringFlight;
        int launches=observedLaunches,impacts=observedImpacts;
        if(peerActor!=null)service.Release(peerActor);
        service.Release(actor);yield return null;yield return new WaitForFixedUpdate();
        bool reset=!actor.IsLeased&&!actor.gameObject.activeSelf&&!executor.IsExecuting&&!executor.HasProjectile&&executor.LaunchCount==0&&executor.ImpactCount==0
            &&pool.LeasedCount==0&&pool.PendingReturnCount==0&&(peerActor==null||!peerActor.IsLeased&&!peerExecutor.HasProjectile);
        cases.Add(new JObject{["id"]=id,["selectionKey"]=row["selectionKey"].DeepClone(),["clip"]=row["actualClip"].DeepClone(),
            ["projectile"]=true,["fps"]=fps,["scenario"]=concurrentRequested?"concurrent-two-actors":occlusionRequested?"wall-during-flight":terminalPolicy!=null?terminalPolicy:cancelBeforeImpact?"cancel-before-impact":nextMeleeRequested?"next-melee-while-flight":"normal",["started"]=started,
            ["expectedProjectiles"]=count,["launches"]=launches,["impacts"]=impacts,["stationaryRootPreserved"]=stationary,["reset"]=reset,
            ["targetRadius"]=collider.radius,["targetHeight"]=collider.height,["expectedDamage"]=expectedDamage,["damageEvents"]=damage,["pass"]=pass&&reset});
        if(concurrentRequested||occlusionRequested)
        {
            var saved=(JObject)cases[cases.Count-1];saved["actorCount"]=actorCount;saved["peerStarted"]=peerStarted;
            saved["expectedLaunches"]=ability.HitCount*actorCount;saved["expectedImpacts"]=count;
            saved["separateSequenceAndReactionScopes"]=phaseOrder&&reactions;
        }
        if(blockedFallback)
        {
            var saved=(JObject)cases[cases.Count-1];saved["fallbackRejectedBeforeBlocked"]=rejectedBeforeBlocked;
            saved["fallbackReadyAfterBlocked"]=readyAfterBlocked;saved["actionReleasedWhileFlight"]=actionReleasedWhileFlight;
            saved["projectilesPerRelease"]=ability.ProjectilesPerRelease;saved["releaseGroups"]=ability.ReleaseCount;saved["projectileFixtureRevision"]=ProjectileFixtureRevision;
            saved["policyChecksRequested"]=(bool?)row["verifyFallbackPolicies"]==true;
            saved["movingApproachRejected"]=movingRejected;saved["freeApproachTravel"]=freeTravel;
            saved["closeAttackAvailableRejected"]=closeRejected;saved["blockedLineRejected"]=blockedLineRejected;saved["longCooldownRejected"]=cooldownRejected;saved["blockedFixture"]="Real Chase/destination intent with controlled zero displacement; not a crowd collision test";
        }
        if(nextMeleeRequested)
        {
            var saved=(JObject)cases[cases.Count-1];saved["nextMeleeStarted"]=nextMeleeStarted;
            saved["flightPreservedByAICleanup"]=flightPreservedByAICleanup;saved["nextMeleeStartedDuringFlight"]=nextMeleeStartedDuringFlight;
        }
        if(terminalPolicy!=null)
        {
            var saved=(JObject)cases[cases.Count-1];saved["terminalPolicyVerified"]=terminalPolicyVerified;
            saved["damageBeforeTerminal"]=damageBeforeTerminal;saved["damageAfterTerminal"]=damage.Count;
            saved["cancelled"]=cancelled;
        }
        if(nextVictim!=null)UnityEngine.Object.Destroy(nextVictim);
        WriteResult("RUNNING");UnityEngine.Object.Destroy(serviceRoot);UnityEngine.Object.Destroy(victim);UnityEngine.Object.Destroy(floor);yield return null;
    }

    static IEnumerator RunActualCrowdApproachCase(JObject row, int fps)
    {
        Time.captureDeltaTime = 1f / fps;
        string id = ((string)row["cardKey"]).Substring("runtime:".Length);
        var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/ProjectOverburst/Resources/Enemies/Themes/Definitions/" + id + ".asset");
        var ability = Enumerable.Range(0, definition.AbilitySet.Count).Select(definition.AbilitySet.GetAbility)
            .Single(a => a.WeakAttackExecution?.SelectionKey == (string)row["selectionKey"]);
        if (!ability.WeakAttackExecution.RequiresBlockedApproach) throw new InvalidOperationException("Blocked approach ability required.");
        int initialCrowdCount = EnemyCrowdService.RegisteredCount;
        var services = new GameObject("Actual crowd approach test services"); owned.Add(services);
        var inactive = new GameObject("Actual crowd approach inactive pool"); inactive.transform.SetParent(services.transform, false); inactive.SetActive(false);
        var pool = services.AddComponent<EnemyPoolService>(); pool.Configure(inactive.transform, 0);
        var catalog = ScriptableObject.CreateInstance<EnemyCatalog>(); owned.Add(catalog); catalog.Configure(new[] { definition });
        var service = services.AddComponent<EnemySpawnService>(); service.Configure(catalog, pool);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(floor);
        floor.transform.position = Vector3.down * .5f; floor.transform.localScale = new Vector3(100, 1, 100);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab");
        var playerBody = prefab.GetComponentsInChildren<CapsuleCollider>(true).Single(c => c.enabled && !c.isTrigger);
        var volume = prefab.GetComponent<CombatTarget>().ResolveVolumeAtRootPosition(Vector3.zero);
        var victim = new GameObject("Player capsule beyond actual crowd"); owned.Add(victim); victim.layer = prefab.layer;
        victim.transform.position = Vector3.forward * 7f;
        var body = victim.AddComponent<CapsuleCollider>();
        body.center = prefab.transform.InverseTransformPoint(playerBody.transform.TransformPoint(playerBody.center));
        body.radius = playerBody.radius * Mathf.Max(Mathf.Abs(playerBody.transform.lossyScale.x), Mathf.Abs(playerBody.transform.lossyScale.z));
        body.height = playerBody.height * Mathf.Abs(playerBody.transform.lossyScale.y);
        var target = victim.AddComponent<CombatTarget>(); target.Configure(CombatTeam.PlayerParty, false);
        target.ConfigureVolume(volume.Center, volume.Radius, volume.HalfHeight * 2);
        var health = target.DamageReceiver; health.SetMaxHp(100000, true);
        var healthSettings = new SerializedObject(health); healthSettings.FindProperty("showDamageNumbers").boolValue = false;
        healthSettings.ApplyModifiedPropertiesWithoutUndo();
        var damage = new JArray(); health.OnDamaged += (_, info) => damage.Add(new JObject { ["phase"] = info.sourceAttackPhaseIndex, ["damage"] = info.damage });
        if (!service.TrySpawn(new EnemySpawnRequest(definition, Vector3.zero, Quaternion.identity, victim.transform, null, victim.transform, null, 1, 1, 91), out var actor))
            throw new InvalidOperationException("Crowd Actor spawn failed.");
        actor.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        // Only the surrounding fixture agents stop thinking. Their saved movement, body and crowd components remain active.
        // The tested actor keeps its real AI, movement, motor, cooldowns and ability selection throughout.
        var blockers = new System.Collections.Generic.List<EnemyActor>();
        float ringRadius = actor.GetComponent<EnemyCrowdAgent>().BodyRadius * 2f + .03f;
        for (int i = 0; i < 12; i++)
        {
            float angle = i * Mathf.PI * 2f / 12f;
            Vector3 point = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * ringRadius;
            if (!service.TrySpawn(new EnemySpawnRequest(definition, point, Quaternion.identity, null, null, null, null, 1, 1, 100 + i), out var blocker))
                throw new InvalidOperationException("Crowd fixture Actor spawn failed.");
            blocker.AI.enabled = false; blocker.AbilityController.enabled = false;
            var sensor = blocker.GetComponent<EnemySensor>(); if (sensor != null) sensor.enabled = false;
            blocker.Movement.StopMovement(); blockers.Add(blocker);
        }
        yield return null; yield return new WaitForFixedUpdate(); yield return null;
        Physics.SyncTransforms();
        var fallback = actor.GetComponent<EnemyBlockedApproachProjectileExecutor>();
        var executor = actor.GetComponent<EnemyThemeSpecialExecutor>();
        var motor = actor.GetComponent<EnemyMotor>();
        bool beforeBlockedRejected = !fallback.HasFallbackPermission(ability);
        actor.AI.RequestAggro(victim.transform);
        Vector3 start = actor.transform.position;
        float deadline = Time.time + 12f, maximumTravel = 0f, maximumBlocked = 0f;
        bool liveComponents = true, noForcedMovementLock = true, destinationObserved = false;
        int chaseFrames = 0; float nextSample = 0f; var samples = new JArray();
        while (executor.LaunchCount == 0 && Time.time < deadline)
        {
            maximumTravel = Mathf.Max(maximumTravel, Vector2.Distance(new Vector2(start.x, start.z), new Vector2(actor.transform.position.x, actor.transform.position.z)));
            maximumBlocked = Mathf.Max(maximumBlocked, fallback.BlockedApproachSeconds);
            liveComponents &= actor.AI.enabled && actor.Movement.enabled && actor.GetComponent<EnemyCrowdAgent>().IsCrowdActive;
            noForcedMovementLock &= !motor.IsFrozen && !actor.Movement.IsStatusMovementLocked;
            if (actor.AI.CurrentStateName == "Chase") { chaseFrames++; destinationObserved |= actor.Movement.HasDestination; }
            if (Time.time >= nextSample)
            {
                samples.Add(new JObject { ["time"] = Time.time, ["state"] = actor.AI.CurrentStateName, ["blockedSeconds"] = fallback.BlockedApproachSeconds,
                    ["x"] = actor.transform.position.x, ["z"] = actor.transform.position.z, ["hasDestination"] = actor.Movement.HasDestination,
                    ["yaw"] = actor.transform.eulerAngles.y, ["facingTarget"] = actor.Movement.IsFacingForAttack(actor.AbilityController.ResolveAimPosition(victim.transform)) });
                nextSample = Time.time + .25f;
            }
            yield return null;
        }
        bool autonomousStart = executor.LaunchCount > 0;
        float flightDeadline = Time.time + 5f;
        while (autonomousStart && (executor.IsExecuting || executor.HasProjectile) && Time.time < flightDeadline) yield return null;
        int launches = executor.LaunchCount, impacts = executor.ImpactCount;
        bool flightFinished = !executor.IsExecuting && !executor.HasProjectile;
        bool blockersPreserved = blockers.All(b => b.IsLeased && b.Movement.enabled && b.GetComponent<EnemyCrowdAgent>().IsCrowdActive);
        int activeCrowdCount = EnemyCrowdService.RegisteredCount;
        bool pass = beforeBlockedRejected && autonomousStart && liveComponents && noForcedMovementLock && destinationObserved && chaseFrames > 0
            && maximumBlocked >= .799f && maximumTravel < ringRadius && blockersPreserved && flightFinished
            && launches == ability.HitCount && impacts == ability.HitCount && damage.Count == ability.HitCount
            && damage.Select(d => (int)d["phase"]).OrderBy(i => i).SequenceEqual(Enumerable.Range(0, ability.HitCount));
        service.Release(actor); foreach (var blocker in blockers) service.Release(blocker);
        yield return null; yield return new WaitForFixedUpdate();
        bool reset = pool.LeasedCount == 0 && pool.PendingReturnCount == 0 && !actor.IsLeased && !executor.HasProjectile
            && EnemyCrowdService.RegisteredCount == initialCrowdCount;
        cases.Add(new JObject { ["id"] = id, ["selectionKey"] = row["selectionKey"].DeepClone(), ["clip"] = row["actualClip"].DeepClone(),
            ["scenario"] = "actual-crowd-blocked-autonomous-attack", ["fps"] = fps, ["pass"] = pass && reset, ["reset"] = reset,
            ["fixture"] = "12 saved stationary Actor bodies; tested Actor AI and central crowd motor enabled; no forced displacement or status freeze",
            ["autonomousAttackStarted"] = autonomousStart, ["manualAttackStartCalls"] = 0, ["liveComponents"] = liveComponents,
            ["noForcedMovementLock"] = noForcedMovementLock, ["beforeBlockedRejected"] = beforeBlockedRejected,
            ["destinationObserved"] = destinationObserved, ["chaseFrames"] = chaseFrames, ["maximumBlockedSeconds"] = maximumBlocked,
            ["maximumTravel"] = maximumTravel, ["ringRadius"] = ringRadius, ["blockerCount"] = blockers.Count,
            ["blockersPreserved"] = blockersPreserved, ["centralCrowdSolverEnabled"] = EnemyCrowdService.CentralMovementResolutionEnabled,
            ["enemyPhysicsSelfCollisionIgnored"] = EnemyCrowdService.IsEnemySelfCollisionDisabled,
            ["activeCrowdCount"] = activeCrowdCount, ["flightFinished"] = flightFinished, ["launches"] = launches, ["impacts"] = impacts,
            ["damageEvents"] = damage, ["samples"] = samples });
        WriteResult("RUNNING");
        UnityEngine.Object.Destroy(services); UnityEngine.Object.Destroy(victim); UnityEngine.Object.Destroy(floor); yield return null;
    }
}
