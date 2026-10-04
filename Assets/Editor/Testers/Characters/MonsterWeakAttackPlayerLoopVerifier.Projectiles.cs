using System;
using System.Collections;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static partial class MonsterWeakAttackPlayerLoopVerifier
{
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
        bool started=actor.AbilityController.TryStartAbility(ability,victim.transform),cancelled=false;
        float deadline=Time.time+ability.ResolveExecutionDuration(actor.Melee.AbilityAnimationSpeed)+ability.Range/10f+3f;
        while(started&&executor.IsExecuting&&Time.time<deadline)
        {
            if(cancelBeforeImpact&&!cancelled&&executor.LaunchCount>0){actor.AbilityController.Cancel();cancelled=true;}
            yield return null;
        }
        if(cancelBeforeImpact)for(int i=0;i<6;i++)yield return null;
        int count=cancelBeforeImpact?0:ability.HitCount;
        if(!ability.TryResolveWeakDamageBudget(actor.GetComponent<EnemyRank>()?.Level??1,actor.RuntimeStats.DamageMultiplier,out var budget))
            throw new InvalidOperationException("Selected projectile damage budget invalid.");
        bool stationary=Vector2.Distance(new Vector2(start.x,start.z),new Vector2(actor.transform.position.x,actor.transform.position.z))<.04f;
        bool pass=started&&!executor.IsExecuting&&!executor.HasProjectile&&stationary
            &&damage.Count==count&&damage.Select(d=>(int)d["phase"]).SequenceEqual(Enumerable.Range(0,count))
            &&Mathf.Abs(damage.Sum(d=>(float)d["damage"])-(cancelBeforeImpact?0:budget.Total))<.001f
            &&(cancelBeforeImpact?cancelled&&executor.LaunchCount==1:executor.LaunchCount==count&&executor.ImpactCount==count)
            &&damage.Select((d,i)=>(bool)d["repeatReactionSuppressed"]==(i>0)).All(v=>v);
        int launches=executor.LaunchCount,impacts=executor.ImpactCount;
        service.Release(actor);yield return null;yield return new WaitForFixedUpdate();
        bool reset=!actor.IsLeased&&!actor.gameObject.activeSelf&&!executor.IsExecuting&&!executor.HasProjectile&&executor.LaunchCount==0&&executor.ImpactCount==0
            &&pool.LeasedCount==0&&pool.PendingReturnCount==0;
        cases.Add(new JObject{["id"]=id,["selectionKey"]=row["selectionKey"].DeepClone(),["clip"]=row["actualClip"].DeepClone(),
            ["projectile"]=true,["fps"]=fps,["scenario"]=cancelBeforeImpact?"cancel-before-impact":"normal",["started"]=started,
            ["expectedProjectiles"]=count,["launches"]=launches,["impacts"]=impacts,["stationaryRootPreserved"]=stationary,["reset"]=reset,
            ["targetRadius"]=collider.radius,["targetHeight"]=collider.height,["expectedDamage"]=cancelBeforeImpact?0:budget.Total,["damageEvents"]=damage,["pass"]=pass&&reset});
        WriteResult("RUNNING");UnityEngine.Object.Destroy(serviceRoot);UnityEngine.Object.Destroy(victim);UnityEngine.Object.Destroy(floor);yield return null;
    }
}
