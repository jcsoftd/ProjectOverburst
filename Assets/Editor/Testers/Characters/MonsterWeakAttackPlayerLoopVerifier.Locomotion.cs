using System;
using System.Collections;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static partial class MonsterWeakAttackPlayerLoopVerifier
{
    static IEnumerator RunRakeLocomotionCases()
    {
        Time.captureDeltaTime=1f/30;
        var definition=AssetDatabase.LoadAssetAtPath<EnemyDefinition>("Assets/ProjectOverburst/Resources/Enemies/Themes/Definitions/DeathHarvest_RakeBrute.asset");
        var services=new GameObject("Rake locomotion saved Actor services");owned.Add(services);
        var inactive=new GameObject("Rake inactive pool");inactive.transform.SetParent(services.transform,false);inactive.SetActive(false);
        var pool=services.AddComponent<EnemyPoolService>();pool.Configure(inactive.transform,0);
        var catalog=ScriptableObject.CreateInstance<EnemyCatalog>();owned.Add(catalog);catalog.Configure(new[]{definition});
        var service=services.AddComponent<EnemySpawnService>();service.Configure(catalog,pool);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);owned.Add(floor);floor.transform.position=new Vector3(0,-.5f,0);floor.transform.localScale=new Vector3(100,1,100);
        var victim=new GameObject("Rake controlled player-team target");owned.Add(victim);victim.transform.position=Vector3.forward*35;
        var target=victim.AddComponent<CombatTarget>();target.Configure(CombatTeam.PlayerParty,false);target.ConfigureVolume(Vector3.up*.78f,.2f,1.55f);
        var body=victim.AddComponent<CapsuleCollider>();body.center=Vector3.up*.78f;body.radius=.2f;body.height=1.55f;
        var health=target.DamageReceiver;health.SetMaxHp(100000,true);
        var request=new EnemySpawnRequest(definition,Vector3.zero,Quaternion.identity,victim.transform,null,victim.transform,null,1,1,95);
        if(!service.TrySpawn(request,out var actor))throw new InvalidOperationException("Saved Rake spawn failed.");
        foreach(var c in actor.GetComponentsInChildren<MonoBehaviour>(true))
            if(c!=null&&new[]{"EnemyAIController","EnemyCrowdAgent","EnemySensor","RunFallGuard"}.Contains(c.GetType().Name))c.enabled=false;
        foreach(var collider in actor.GetComponentsInChildren<Collider>(true))Physics.IgnoreCollision(collider,body);
        var selector=actor.GetComponent<EnemyLocomotionVariantSelector>();var animator=actor.Animator;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        var motor=actor.GetComponent<EnemyMotor>();selector.enabled=false;actor.Movement.StopMovement();
        yield return null;yield return new WaitForFixedUpdate();
        var idleClips=new[]{"Zombie_Idle_01","Zombie_Idle_03","Zombie_Idle_2_IPC"};var idleEvidence=new JArray();
        for(int i=0;i<idleClips.Length;i++)
        {
            animator.SetFloat("Locomotion",0);animator.Play("IdleVariant_"+i,0,0f);yield return null;
            float deadline=Time.time+17;var start=actor.transform.position;
            while(animator.GetCurrentAnimatorStateInfo(0).normalizedTime<1.05f&&Time.time<deadline)yield return null;
            var state=animator.GetCurrentAnimatorStateInfo(0);var clips=animator.GetCurrentAnimatorClipInfo(0);
            bool pass=state.IsName("IdleVariant_"+i)&&state.loop&&state.normalizedTime>=1.05f&&clips.Any(c=>c.clip.name==idleClips[i]&&c.weight>.9f)
                &&Vector2.Distance(new Vector2(start.x,start.z),new Vector2(actor.transform.position.x,actor.transform.position.z))<.03f;
            idleEvidence.Add(new JObject{["clip"]=idleClips[i],["normalized"]=state.normalizedTime,["pass"]=pass});
        }
        cases.Add(new JObject{["id"]=definition.EnemyId,["scenario"]="saved-three-idle-loop-playback",["clips"]=idleEvidence,["pass"]=idleEvidence.All(e=>(bool)e["pass"])});WriteResult("RUNNING");
        var moves=new[]{"Zombie_Walk_01_Forward_InPlace","Zombie_Walk_F_2_Loop_IPC"};var moveEvidence=new JArray();
        for(int i=0;i<moves.Length;i++)
        {
            animator.Play("Locomotion",0,0);animator.SetFloat("MoveVariant",i);var start=actor.transform.position;
            for(int frame=0;frame<60;frame++){yield return new WaitForFixedUpdate();actor.Movement.SetDestination(actor.transform.position+Vector3.forward*20,0f,EnemyLocomotionMode.Walk);yield return null;}
            var clips=animator.GetCurrentAnimatorClipInfo(0);
            bool pass=animator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion")&&clips.Any(c=>c.clip.name==moves[i]&&c.weight>.9f)
                &&actor.transform.position.z-start.z>.3f;
            moveEvidence.Add(new JObject{["clip"]=moves[i],["moved"]=actor.transform.position.z-start.z,["pass"]=pass});actor.Movement.StopMovement();
        }
        selector.enabled=true;animator.SetFloat("Locomotion",1);animator.Play("Locomotion",0,0f);
        for(int frame=0;frame<12;frame++){yield return new WaitForFixedUpdate();actor.Movement.SetDestination(actor.transform.position+Vector3.forward*20,0f,EnemyLocomotionMode.Walk);yield return null;}
        int selected=selector.MoveSelectionCount,choice=selector.MoveIndex;bool held=true;
        for(int frame=0;frame<35;frame++){yield return new WaitForFixedUpdate();actor.Movement.SetDestination(actor.transform.position+Vector3.forward*20,0f,EnemyLocomotionMode.Walk);yield return null;held&=selector.MoveSelectionCount==selected&&selector.MoveIndex==choice;}
        cases.Add(new JObject{["id"]=definition.EnemyId,["scenario"]="saved-walk-variants-and-interval-hold",["clips"]=moveEvidence,["selectionHeld"]=held,["pass"]=moveEvidence.All(e=>(bool)e["pass"])&&selected>0&&held});WriteResult("RUNNING");
        actor.Movement.StopMovement();animator.SetFloat("Locomotion",0);
        float idleDeadline=Time.time+21;
        while(selector.IdleSelectionCount<2&&Time.time<idleDeadline)yield return null;
        bool naturalCycle=selector.IdleSelectionCount>=2;
        victim.transform.position=actor.transform.position+Vector3.forward*.85f;Physics.SyncTransforms();
        var weak=Enumerable.Range(0,definition.AbilitySet.Count).Select(definition.AbilitySet.GetAbility).First(a=>!a.IsTelegraphedStrongAttack);
        int before=selector.IdleSelectionCount;bool started=actor.AbilityController.TryStartAbility(weak,victim.transform),protectedAttack=true,enteredAttack=false;
        float attackDeadline=Time.time+weak.ResolveExecutionDuration(actor.Melee.AbilityAnimationSpeed)+4;
        while(started&&actor.AbilityController.IsExecuting&&Time.time<attackDeadline)
        {yield return null;enteredAttack|=animator.GetCurrentAnimatorStateInfo(0).IsName("Attack_"+weak.AnimatorTrigger.Substring(6));if(actor.AbilityController.IsExecuting)protectedAttack&=selector.IdleSelectionCount==before;}
        bool finished=!actor.AbilityController.IsExecuting;
        service.Release(actor);yield return null;yield return new WaitForFixedUpdate();
        bool reset=!actor.IsLeased&&!actor.gameObject.activeSelf&&selector.IdleSelectionCount==0&&selector.MoveSelectionCount==0&&pool.LeasedCount==0&&pool.PendingReturnCount==0;
        cases.Add(new JObject{["id"]=definition.EnemyId,["scenario"]="natural-idle-cycle-attack-interruption-pool-reset",["naturalCycle"]=naturalCycle,["attackStarted"]=started,
            ["attackEntered"]=enteredAttack,["attackProtected"]=protectedAttack,["attackFinished"]=finished,["poolReset"]=reset,["pass"]=naturalCycle&&started&&enteredAttack&&protectedAttack&&finished&&reset});WriteResult("RUNNING");
        UnityEngine.Object.Destroy(services);UnityEngine.Object.Destroy(victim);UnityEngine.Object.Destroy(floor);yield return null;
    }
}
