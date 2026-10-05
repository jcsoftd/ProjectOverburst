using System;using System.Collections;using System.IO;using System.Linq;using Newtonsoft.Json.Linq;using UnityEditor;using UnityEngine;
public static partial class MonsterWeakAttackPlayerLoopVerifier
{
    static IEnumerator RunPresentationCalibrationCases(JObject author)
    {
        Time.captureDeltaTime=1f/30;
        var services=new GameObject("Presentation calibrated actor services");owned.Add(services);var inactive=new GameObject("Presentation inactive pool");inactive.transform.SetParent(services.transform,false);inactive.SetActive(false);
        var pool=services.AddComponent<EnemyPoolService>();pool.Configure(inactive.transform,0);var definitions=author["definitions"].Values<string>().Select(p=>AssetDatabase.LoadAssetAtPath<EnemyDefinition>(p)).ToArray();
        var catalog=ScriptableObject.CreateInstance<EnemyCatalog>();owned.Add(catalog);catalog.Configure(definitions);var service=services.AddComponent<EnemySpawnService>();service.Configure(catalog,pool);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);owned.Add(floor);floor.name="Owned calibrated ground";floor.transform.position=new Vector3(0,-.5f,0);floor.transform.localScale=new Vector3(160,1,160);
        foreach(var d in definitions)
        {
            var request=new EnemySpawnRequest(d,Vector3.zero,Quaternion.identity,null,null,null,null,1,1,95);
            if(!service.TrySpawn(request,out var actor))throw new Exception("Spawn failed: "+d.EnemyId);
            actor.AI.enabled=false;foreach(var b in actor.GetComponentsInChildren<MonoBehaviour>(true))if(b!=null&&new[]{"EnemyCrowdAgent","EnemySensor","RunFallGuard"}.Contains(b.GetType().Name))b.enabled=false;
            var animator=actor.Animator;var originalCull=animator.cullingMode;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;var rb=actor.GetComponent<Rigidbody>();
            var selector=actor.GetComponent<EnemyLocomotionVariantSelector>();if(selector!=null)selector.enabled=false;
            yield return null;yield return new WaitForFixedUpdate();yield return null;
            var samples=new JArray();bool valid=true;
            foreach(var mode in new[]{EnemyLocomotionMode.Walk,EnemyLocomotionMode.Run,EnemyLocomotionMode.Backpedal})
            {
                actor.Movement.StopMovement();rb.position=Vector3.zero;rb.rotation=Quaternion.identity;rb.linearVelocity=Vector3.zero;actor.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);Physics.SyncTransforms();
                Vector3 from=actor.transform.position;
                for(int frame=0;frame<36;frame++){
                    if(mode==EnemyLocomotionMode.Backpedal)actor.Movement.SetFacingDestination(actor.transform.position-Vector3.forward*20,0,actor.transform.position+Vector3.forward*20,mode);else actor.Movement.SetDestination(actor.transform.position+Vector3.forward*20,0,mode);
                    yield return new WaitForFixedUpdate();yield return null;
                }
                float distance=Vector2.Distance(new Vector2(from.x,from.z),new Vector2(actor.transform.position.x,actor.transform.position.z));
                File.WriteAllText(Path.Combine(plan.directory,"last-pose-state.json"),new JObject{{"id",d.EnemyId},{"mode",mode.ToString()},{"leased",actor.IsLeased},{"active",actor.gameObject.activeInHierarchy},{"hp",actor.Health.CurrentHp},{"position",new JArray(actor.transform.position.x,actor.transform.position.y,actor.transform.position.z)},{"fallGuardEnabled",actor.RunFallGuard!=null&&actor.RunFallGuard.enabled},{"renderers",new JArray(actor.VisualRoot.GetComponentsInChildren<Renderer>(true).Select(r=>new JObject{{"name",r.name},{"enabled",r.enabled},{"active",r.gameObject.activeInHierarchy},{"mesh",r is SkinnedMeshRenderer sk&&sk.sharedMesh!=null?sk.sharedMesh.name:""}}))}}.ToString());
                var bounds=UpcomingMonsterThemeReviewSizing.GeometryBounds(actor.VisualRoot.gameObject);var clearance=actor.GetComponent<EnemyLocomotionGroundClearance>();float playback=animator.GetFloat("MoveAnimSpeed");float expected=actor.Movement.ActiveMoveSpeed/d.MovementProfile.GetAnimationReferenceSpeed(mode);
                bool pass=distance>.25f&&float.IsFinite(playback)&&playback>.005f&&Mathf.Abs(playback-expected)<Mathf.Max(.3f,expected*.35f)&&bounds.min.y>-.085f&&actor.transform.localScale==Vector3.one;
                samples.Add(new JObject{{"mode",mode.ToString()},{"distance",distance},{"geometryBottom",bounds.min.y},{"playback",playback},{"expectedPlayback",expected},{"lift",clearance!=null?clearance.CurrentLift:0},{"pass",pass}});valid&=pass;
                actor.Movement.StopMovement();for(int frame=0;frame<12;frame++){yield return new WaitForFixedUpdate();yield return null;}
            }
            actor.Movement.StopMovement();Vector3 stopped=actor.transform.position;for(int frame=0;frame<18;frame++){yield return new WaitForFixedUpdate();yield return null;}
            bool idleReset=Vector3.Distance(stopped,actor.transform.position)<.04f&&Mathf.Abs(animator.GetFloat("Locomotion"))<.08f&&Mathf.Abs(animator.GetFloat("MoveAnimSpeed")-1)<.12f;
            // The model offset must not grow across pooled leases.
            Vector3 beforeScale=animator.transform.localScale;var grounding=actor.GetComponent<EnemyLocomotionGroundClearance>();animator.cullingMode=originalCull;service.Release(actor);yield return null;yield return new WaitForFixedUpdate();
            bool released=!actor.IsLeased&&!actor.gameObject.activeSelf&&pool.LeasedCount==0&&pool.PendingReturnCount==0&&(grounding==null||grounding.CurrentLift==0);
            cases.Add(new JObject{{"id",d.EnemyId},{"scenario","calibrated-walk-run-back-stop"},{"samples",samples},{"idleReset",idleReset},{"pass",valid&&idleReset}});WriteResult("RUNNING");
            bool spawned=service.TrySpawn(request,out var reused);if(!spawned)throw new Exception("Reuse failed");reused.AI.enabled=false;yield return null;yield return new WaitForFixedUpdate();yield return null;var cap=reused.CollisionRoot.GetComponentInChildren<CapsuleCollider>();
            bool reusePass=reused.IsAuthoringValid&&reused.transform.localScale==Vector3.one&&reused.Animator.transform.localScale==beforeScale&&!reused.Movement.IsActionLocked&&cap.radius>0&&cap.height>0&&reused.Health.CurrentHp==reused.Health.MaxHp;
            service.Release(reused);yield return null;yield return new WaitForFixedUpdate();cases.Add(new JObject{{"id",d.EnemyId},{"scenario","calibrated-pool-reuse"},{"pass",reusePass&&pool.LeasedCount==0&&pool.PendingReturnCount==0&&released}});WriteResult("RUNNING");
        }
    }
    static IEnumerator RunPresentationChannelCase(JObject row,int fps)
    {
        Time.captureDeltaTime=1f/fps;
        string id=((string)row["cardKey"]).Substring("runtime:".Length);
        var d=AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/ProjectOverburst/Resources/Enemies/Themes/Definitions/"+id+".asset");
        var ability=Enumerable.Range(0,d.AbilitySet.Count).Select(d.AbilitySet.GetAbility).Single(a=>a.WeakAttackExecution?.SelectionKey==(string)row["selectionKey"]);
        if(ability.ExecutionMode!=EnemyAbilityExecutionMode.Zone)throw new Exception("Saved channel mode required");
        var services=new GameObject("Presentation channel services");owned.Add(services);
        var inactive=new GameObject("Presentation channel pool");inactive.transform.SetParent(services.transform,false);inactive.SetActive(false);
        var pool=services.AddComponent<EnemyPoolService>();pool.Configure(inactive.transform,0);
        var catalog=ScriptableObject.CreateInstance<EnemyCatalog>();owned.Add(catalog);catalog.Configure(new[]{d});
        var service=services.AddComponent<EnemySpawnService>();service.Configure(catalog,pool);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);owned.Add(floor);floor.transform.position=new Vector3(0,-.5f,0);floor.transform.localScale=new Vector3(100,1,100);
        var player=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab");
        var body=player.GetComponentsInChildren<CapsuleCollider>(true).Single(c=>c.enabled&&!c.isTrigger);
        var volume=player.GetComponent<CombatTarget>().ResolveVolumeAtRootPosition(Vector3.zero);
        var victim=new GameObject("Saved player capsule channel target");owned.Add(victim);victim.layer=player.layer;
        var collider=victim.AddComponent<CapsuleCollider>();collider.center=player.transform.InverseTransformPoint(body.transform.TransformPoint(body.center));
        collider.radius=body.radius*Mathf.Max(Mathf.Abs(body.transform.lossyScale.x),Mathf.Abs(body.transform.lossyScale.z));collider.height=body.height*Mathf.Abs(body.transform.lossyScale.y);
        victim.transform.position=new Vector3(0,collider.height*.5f-collider.center.y,ability.WeakAttackExecution.ApproachStartRange-.1f);
        var target=victim.AddComponent<CombatTarget>();target.Configure(CombatTeam.PlayerParty,false);target.ConfigureVolume(volume.Center,volume.Radius,volume.HalfHeight*2);
        var health=target.DamageReceiver;health.SetMaxHp(100000,true);var settings=new SerializedObject(health);settings.FindProperty("showDamageNumbers").boolValue=false;settings.ApplyModifiedPropertiesWithoutUndo();
        var hits=new JArray();health.OnDamaged+=(_,info)=>hits.Add(new JObject{{"phase",info.sourceAttackPhaseIndex},{"damage",info.damage},{"sequence",info.sourceAttackSequenceId}});
        var request=new EnemySpawnRequest(d,Vector3.zero,Quaternion.identity,victim.transform,null,victim.transform,null,1,1,91);
        if(!service.TrySpawn(request,out var actor))throw new Exception("Channel actor spawn failed");
        foreach(var b in actor.GetComponentsInChildren<MonoBehaviour>(true))if(b!=null&&new[]{"EnemyAIController","EnemyCrowdAgent","EnemySensor","RunFallGuard"}.Contains(b.GetType().Name))b.enabled=false;
        actor.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;var channel=actor.GetComponent<EnemyChannelAbilityExecutor>();
        yield return null;yield return new WaitForFixedUpdate();yield return null;Physics.SyncTransforms();
        Vector3 start=actor.transform.position;bool started=actor.AbilityController.TryStartAbility(ability,victim.transform);
        float deadline=Time.time+ability.ResolveExecutionDuration(actor.Melee.AbilityAnimationSpeed)+4;
        while(actor.AbilityController.IsExecuting&&Time.time<deadline)yield return null;
        bool normal=started&&!actor.AbilityController.IsExecuting&&channel.PulseCount==ability.HitCount&&channel.DamageCount==ability.HitCount
            &&hits.Count==ability.HitCount&&hits.Select(h=>(int)h["phase"]).SequenceEqual(Enumerable.Range(0,ability.HitCount))&&Vector3.Distance(start,actor.transform.position)<.05f&&!channel.IsEmitting;
        var evidence=hits.DeepClone();service.Release(actor);yield return null;yield return new WaitForFixedUpdate();
        bool reset=channel.PulseCount==0&&channel.DamageCount==0&&!channel.IsExecuting&&!channel.IsEmitting&&pool.PendingReturnCount==0;
        if(!service.TrySpawn(request,out actor))throw new Exception("Channel reuse failed");
        foreach(var b in actor.GetComponentsInChildren<MonoBehaviour>(true))if(b!=null&&new[]{"EnemyAIController","EnemyCrowdAgent","EnemySensor","RunFallGuard"}.Contains(b.GetType().Name))b.enabled=false;
        channel=actor.GetComponent<EnemyChannelAbilityExecutor>();yield return null;yield return new WaitForFixedUpdate();yield return null;Physics.SyncTransforms();
        int previousHits=hits.Count;bool restarted=actor.AbilityController.TryStartAbility(ability,victim.transform);actor.AbilityController.Cancel();
        for(int i=0;i<12;i++){yield return new WaitForFixedUpdate();yield return null;}
        bool cancelled=restarted&&hits.Count==previousHits&&!channel.IsExecuting&&!channel.IsEmitting&&channel.PulseCount==0;
        service.Release(actor);yield return null;yield return new WaitForFixedUpdate();
        cases.Add(new JObject{{"id",id},{"selectionKey",row["selectionKey"]},{"scenario","saved-channel-pulses-cancel-reuse"},{"fps",fps},{"hits",evidence},{"expectedHits",ability.HitCount},{"normal",normal},{"poolReset",reset},{"cancelled",cancelled},{"pass",normal&&reset&&cancelled&&pool.LeasedCount==0&&pool.PendingReturnCount==0}});WriteResult("RUNNING");
        UnityEngine.Object.Destroy(services);UnityEngine.Object.Destroy(victim);UnityEngine.Object.Destroy(floor);yield return null;
    }
}
