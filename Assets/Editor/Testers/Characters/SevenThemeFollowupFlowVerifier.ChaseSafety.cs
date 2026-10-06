using System;
using System.IO;
using System.Collections;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class SevenThemeFollowupFlowVerifier
{
    static IEnumerator ChaseSafety(EnemyCatalog catalog,CombatHealth health)
    {
        if(!catalog.TryGet("SpiderBrood_Formickarce",out var d))throw new InvalidOperationException("Safety actor");
        currentId="observation_safety";trace=new JArray();damages=new JArray();begin=Time.time;phase="observation_hold";
        target.position=Vector3.forward*15;health.SetMaxHp(1e9f,true);
        if(!service.TrySpawn(new EnemySpawnRequest(d,Vector3.up*.035f,Quaternion.identity,target,context:EncounterContext.Test),out actor))throw new InvalidOperationException("Safety spawn");
        UseObservationInterval(actor.AI,.65f);actor.AI.RequestAggro(target);yield return null;
        var ai=actor.AI;var resolve=(Func<Vector3>)Delegate.CreateDelegate(typeof(Func<Vector3>),ai,typeof(EnemyAIController).GetMethod("ResolveChasePlan",PrivateInstance));
        resolve();float due=(float)ObservationDeadline.GetValue(ai);int count=ai.PlanningEvaluationCount;
        Vector3 heading=(Vector3)ObservationHeading.GetValue(ai);
        target.position=Vector3.right*15;typeof(EnemyAIController).GetField("hasCachedChasePlan",PrivateInstance).SetValue(ai,false);
        // Force waypoint arrival as well as a large target displacement without a new observation.
        var plan=(Vector3)typeof(EnemyAIController).GetField("cachedChasePlan",PrivateInstance).GetValue(ai);
        actor.GetComponent<Rigidbody>().position=plan;Physics.SyncTransforms();resolve();
        bool held=ai.PlanningEvaluationCount==count&&(float)ObservationDeadline.GetValue(ai)==due&&(Vector3)ObservationHeading.GetValue(ai)==heading;
        phase="parry_priority";bool played=actor.AnimationBridge.TryPlayParryStun(out float seconds);
        if(played)actor.GetComponent<EnemyMovementReaction>().ApplyParryStun(seconds);
        yield return new WaitForSeconds(.12f);
        bool stopped=actor.Movement.IsActionLocked||actor.GetComponent<EnemyMovementReaction>().IsParryStunned;
        service.Release(actor);actor=null;yield return null;
        if(!service.TrySpawn(new EnemySpawnRequest(d,Vector3.up*.035f,Quaternion.identity,target,context:EncounterContext.Test),out actor))throw new InvalidOperationException("Safety respawn");
        bool reset=!(bool)typeof(EnemyAIController).GetField("hasChaseObservation",PrivateInstance).GetValue(actor.AI)
            &&(float)ObservationDeadline.GetValue(actor.AI)==0f;
        results.Add(new JObject{["scenario"]="observation_cache_arrival_parry_pool",["heldAgainstCacheResetArrivalAndTargetMove"]=held,
            ["parryMotionStarted"]=played,["parryPriorityWithinSeconds"]=.12f,["parryStopsMovement"]=stopped,["poolObservationReset"]=reset});
        SaveCase(currentId);yield return null;
    }
}
