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
        float deadline=Time.time+ability.ResolveExecutionDuration(actor.Melee.AbilityAnimationSpeed)+ability.Range/10f+3f;
        while(started&&executor.IsExecuting&&Time.time<deadline)
        {
            if(blockedFallback&&!actor.GetComponent<EnemyBlockedApproachProjectileExecutor>().IsExecuting&&executor.HasProjectile)
                actionReleasedWhileFlight=true;
            if(cancelBeforeImpact&&!cancelled&&executor.LaunchCount>0){actor.AbilityController.Cancel();cancelled=true;}
            yield return null;
        }
        if(cancelBeforeImpact)for(int i=0;i<6;i++)yield return null;
        int count=cancelBeforeImpact?0:ability.HitCount;
        int level=actor.GetComponent<EnemyRank>()?.Level??1;
        float total=ability.UsesLevelDamageBudget?Mathf.Max(1f,OverburstCombatBalance.RoundStat(OverburstCombatBalance.ReferenceEffectiveHealth(level)*ability.ReferencePatternDamagePercent/100f)):ability.Damage;
        if(!EnemyWeakProjectileDamageBudget.TryCreate(total*actor.RuntimeStats.DamageMultiplier,ability.HitCount,out var budget))
            throw new InvalidOperationException("Selected projectile damage budget invalid.");
        bool stationary=Vector2.Distance(new Vector2(start.x,start.z),new Vector2(actor.transform.position.x,actor.transform.position.z))<.04f;
        bool pass=started&&!executor.IsExecuting&&!executor.HasProjectile&&stationary
            &&damage.Count==count&&damage.Select(d=>(int)d["phase"]).OrderBy(i=>i).SequenceEqual(Enumerable.Range(0,count))
            &&Mathf.Abs(damage.Sum(d=>(float)d["damage"])-(cancelBeforeImpact?0:budget.Total))<.001f
            &&(cancelBeforeImpact?cancelled&&executor.LaunchCount==ability.ProjectilesPerRelease:executor.LaunchCount==count&&executor.ImpactCount==count)
            &&(!blockedFallback||rejectedBeforeBlocked&&readyAfterBlocked&&(cancelBeforeImpact||actionReleasedWhileFlight))
            &&damage.Select((d,i)=>(bool)d["repeatReactionSuppressed"]==(i>0)).All(v=>v);
        if(blockedFallback&&started)cooldownRejected=!actor.AbilityController.TryStartAbility(ability,victim.transform);
        pass&=movingRejected&&closeRejected&&blockedLineRejected&&cooldownRejected;
        int launches=executor.LaunchCount,impacts=executor.ImpactCount;
        service.Release(actor);yield return null;yield return new WaitForFixedUpdate();
        bool reset=!actor.IsLeased&&!actor.gameObject.activeSelf&&!executor.IsExecuting&&!executor.HasProjectile&&executor.LaunchCount==0&&executor.ImpactCount==0
            &&pool.LeasedCount==0&&pool.PendingReturnCount==0;
        cases.Add(new JObject{["id"]=id,["selectionKey"]=row["selectionKey"].DeepClone(),["clip"]=row["actualClip"].DeepClone(),
            ["projectile"]=true,["fps"]=fps,["scenario"]=cancelBeforeImpact?"cancel-before-impact":"normal",["started"]=started,
            ["expectedProjectiles"]=count,["launches"]=launches,["impacts"]=impacts,["stationaryRootPreserved"]=stationary,["reset"]=reset,
            ["targetRadius"]=collider.radius,["targetHeight"]=collider.height,["expectedDamage"]=cancelBeforeImpact?0:budget.Total,["damageEvents"]=damage,["pass"]=pass&&reset});
        if(blockedFallback)
        {
            var saved=(JObject)cases[cases.Count-1];saved["fallbackRejectedBeforeBlocked"]=rejectedBeforeBlocked;
            saved["fallbackReadyAfterBlocked"]=readyAfterBlocked;saved["actionReleasedWhileFlight"]=actionReleasedWhileFlight;
            saved["projectilesPerRelease"]=ability.ProjectilesPerRelease;saved["releaseGroups"]=ability.ReleaseCount;saved["projectileFixtureRevision"]=ProjectileFixtureRevision;
            saved["policyChecksRequested"]=(bool?)row["verifyFallbackPolicies"]==true;
            saved["movingApproachRejected"]=movingRejected;saved["freeApproachTravel"]=freeTravel;
            saved["closeAttackAvailableRejected"]=closeRejected;saved["blockedLineRejected"]=blockedLineRejected;saved["longCooldownRejected"]=cooldownRejected;saved["blockedFixture"]="Real Chase/destination intent with controlled zero displacement; not a crowd collision test";
        }
        WriteResult("RUNNING");UnityEngine.Object.Destroy(serviceRoot);UnityEngine.Object.Destroy(victim);UnityEngine.Object.Destroy(floor);yield return null;
    }
}
