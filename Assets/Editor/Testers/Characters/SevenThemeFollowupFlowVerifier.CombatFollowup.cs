using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class SevenThemeFollowupFlowVerifier
{
    static IEnumerator CombatFollowup(EnemyCatalog catalog, CombatHealth hp, CombatTarget body)
    {
        if(Mode!="CombatAfter3"&&Mode!="CombatAfter4"&&Mode!="CombatAfter5"&&!Mode.StartsWith("CombatCueVisibility",StringComparison.Ordinal))yield return CombatRotation(catalog,hp);
        EnemyStrongAttackWarning.PlayerTarget=body;
        if(Mode=="CombatBefore") yield return ReaperTimeline(catalog,hp,body);
        else if(Mode=="CombatAfter5")yield return ParryBoundaryAndFreeze(catalog,hp,body);
        else yield return MultiHitParry(catalog,hp,body);
        EnemyStrongAttackWarning.PlayerTarget=null;parryTrace=null;
    }
    static IEnumerator CombatRotation(EnemyCatalog catalog,CombatHealth hp)
    {
        foreach(string id in new[]{"DeathHarvest_Reaper","DeathHarvest_RakeBrute"})
        foreach(float yaw in new[]{85f,175f,-90f})
        {
            if(!catalog.TryGet(id,out var d))throw new InvalidOperationException(id);
            currentId=id+"_rotation_"+yaw;begin=Time.time;trace=new JArray();damages=new JArray();phase=yaw<0?"backpedal":"attack_prepare";
            target.position=Quaternion.Euler(0,yaw<0?0:yaw,0)*Vector3.forward*Mathf.Max(1.5f,d.AbilitySet.GetAbility(0).Range*.88f);hp.SetMaxHp(1000000,true);Physics.SyncTransforms();
            if(!service.TrySpawn(new EnemySpawnRequest(d,Vector3.up*.035f,Quaternion.identity,target,context:EncounterContext.Test),out actor))throw new InvalidOperationException(id);
            actor.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            yield return null;yield return new WaitForFixedUpdate();
            camera=new Capture(Path.Combine(Folder,currentId),target,actor,false,30);camera.Mark(phase);
            float maxStep=0,previous=actor.transform.eulerAngles.y,firstError=-1,attackAt=-1;
            if(yaw<0)
            {
                actor.AI.enabled=false;actor.Movement.SetFacingDestination(new Vector3(0,0,-8),.1f,target.position,EnemyLocomotionMode.Backpedal);
                yield return new WaitForSeconds(.5f);target.position=new Vector3(-2,0,0);Physics.SyncTransforms();
                actor.Movement.SetFacingDestination(new Vector3(0,0,-8),.1f,target.position,EnemyLocomotionMode.Backpedal);previous=actor.transform.eulerAngles.y;
            }
            else actor.AI.RequestAggro(target);
            float until=Time.time+(yaw<0?1.5f:5f);
            while(Time.time<until)
            {
                float now=actor.transform.eulerAngles.y;maxStep=Mathf.Max(maxStep,Mathf.Abs(Mathf.DeltaAngle(previous,now)));previous=now;
                if(attackAt<0&&actor.AbilityController.IsExecuting){attackAt=Time.time-begin;firstError=Vector3.Angle(actor.transform.forward,target.position-actor.transform.position);camera.Mark("attack_committed");}
                if(attackAt>=0&&Time.time-begin>attackAt+.8f)break;
                yield return null;
            }
            results.Add(new JObject{["scenario"]=yaw<0?"saved_backpedal_motor":"saved_AI_attack_facing",["id"]=id,["name"]=d.DisplayName,["targetAngle"]=yaw,["maxRenderYawStep"]=maxStep,["firstAttackFacingError"]=firstError,["firstAttackAt"]=attackAt,["savedTurnSpeed"]=d.MovementProfile.TurnSpeed,["hasTurnClip"]=d.MovementProfile.HasTurnAnimation,["ownedFacing"]=(bool?)(typeof(EnemyMovement).GetProperty("UsesMotionFacing")?.GetValue(actor.Movement))??false,["AIEnabled"]=actor.AI.enabled,["interpolation"]=actor.GetComponent<Rigidbody>().interpolation.ToString(),["assetWrites"]=0});
            if(Mode.StartsWith("CombatAfter",StringComparison.Ordinal)&&yaw>=0&&(attackAt<0||firstError>6f||maxStep>Mathf.Max(20f,d.MovementProfile.TurnSpeed*.07f)))throw new InvalidOperationException("Smooth attack gate failed "+id+" "+yaw+" "+firstError+" "+maxStep);
            SaveCase(currentId);yield return null;
        }
    }
    static IEnumerator MultiHitParry(EnemyCatalog catalog,CombatHealth hp,CombatTarget body)
    {
        var definitions=Enumerable.Range(0,catalog.Count).Select(catalog.GetDefinition).Where(d=>d!=null).ToArray();
        var signalProperty=typeof(EnemyStrongAttackWarning).GetProperty("ParrySignalCount");
        var activeMethod=typeof(EnemyAbilityController).GetMethod("TryGetActiveParryMotionWindow");
        foreach(var d in definitions.Where(d=>(!Mode.StartsWith("CombatCueVisibility",StringComparison.Ordinal)||d.EnemyId=="V3_Kapeloproboskid")&&(Mode!="CombatAfter3"||d.EnemyId=="V3_GiantSlug"||d.EnemyId=="V3_Kapeloproboskid")))
        foreach(var ability in Enumerable.Range(0,d.AbilitySet.Count).Select(d.AbilitySet.GetAbility).Where(a=>a.IsParryable&&a.HitCount>1))
        foreach(float speed in d.EnemyId=="DeathHarvest_Reaper"?new[]{.75f,1f,1.5f}:new[]{1f})
        {
            currentId=d.EnemyId+"_signals_"+Mathf.RoundToInt(speed*100);begin=Time.time;trace=new JArray();damages=new JArray();phase="windup";
            target.position=Vector3.forward*2.5f;hp.SetMaxHp(1000000,true);Physics.SyncTransforms();
            if(!service.TrySpawn(new EnemySpawnRequest(d,Vector3.up*.035f,Quaternion.identity,target,context:EncounterContext.Test),out actor))throw new InvalidOperationException(d.EnemyId);
            actor.AI.enabled=false;actor.Movement.StopMovement();actor.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;actor.Melee.SetRuntimeAttackSpeedMultiplier(speed);
            cadenceField.SetValue(actor.AbilityController,3);yield return null;yield return new WaitForFixedUpdate();
            if(d.EnemyId=="V3_GiantSlug")
            {
                float inner=EnemyAttackThreatGeometry.ResolveSectorInnerRadius(actor,ability);
                float outer=EnemyAttackThreatGeometry.ResolveRadius(actor,ability);
                target.position=Vector3.forward*Mathf.Lerp(inner,outer,.65f);Physics.SyncTransforms();
            }
            JObject spatialDebug=null;
            if(d.EnemyId=="V3_GiantSlug")
            {
                var probes=new JArray();bool found=false;
                float outer=EnemyAttackThreatGeometry.ResolveRadius(actor,ability);
                for(float distance=.8f;distance<=outer+.1f;distance+=.2f)
                {
                    target.position=Vector3.forward*distance+Vector3.up*.035f;Physics.SyncTransforms();
                    bool overlap=actor.Melee.WouldAbilityHitTarget(ability,body);
                    probes.Add(new JObject{["distance"]=distance,["wouldHit"]=overlap});
                    if(overlap){found=true;break;}
                }
                var mask=(LayerMask)typeof(EnemyMeleeAttackController).GetField("targetLayer",PrivateInstance).GetValue(actor.Melee);
                Vector3 center=EnemyAttackThreatGeometry.ResolveImpactCenter(actor,ability,actor.Melee.AttackPoint.position);
                var hits=new JArray(Physics.OverlapSphere(center,outer,mask,QueryTriggerInteraction.Ignore).Select(c=>new JObject{["name"]=c.name,["layer"]=c.gameObject.layer,["position"]=V(c.transform.position),["combatTarget"]=CombatTarget.Resolve(c)?.name,["canDamage"]=CombatTargetFilter.CanDamage(actor.GetComponent<CombatTarget>(),CombatTarget.Resolve(c))}));
                Vector3 groundPlaced=target.position;groundPlaced.y=0;
                target.position=groundPlaced;Physics.SyncTransforms();
                bool groundThreat=actor.Melee.WouldAbilityHitTarget(ability,body);
                Vector3 sight=body.CurrentVolume.Center-center;
                var groundBlockers=new JArray(Physics.RaycastAll(center,sight.normalized,sight.magnitude,~0,QueryTriggerInteraction.Ignore).Where(h=>CombatTarget.Resolve(h.collider)==null).Select(h=>new JObject{["name"]=h.collider.name,["point"]=V(h.point)}));
                target.position=groundPlaced+Vector3.up*.035f;Physics.SyncTransforms();
                spatialDebug=new JObject{["foundThreat"]=found,["center"]=V(center),["outer"]=outer,["inner"]=EnemyAttackThreatGeometry.ResolveSectorInnerRadius(actor,ability),["mask"]=mask.value,["playerLayer"]=target.gameObject.layer,["playerBody"]=V(body.CurrentVolume.Center),["colliders"]=hits,["probes"]=probes,["attackPointBound"]=typeof(EnemyMeleeAttackController).GetField("attackPoint",PrivateInstance).GetValue(actor.Melee)!=null,["forward"]=V(actor.transform.forward),["lineOfSightRequired"]=ability.RequireLineOfSight,["fixtureGroundOffset"]=.035f,["groundRootThreat"]=groundThreat,["groundRootSightBlockers"]=groundBlockers};
            }
            float until=Time.time+2;
            while(!actor.AbilityController.TryStartAbility(ability,target)&&Time.time<until){actor.Movement.FacePosition(target.position);yield return null;}
            if(!actor.AbilityController.IsExecuting)throw new InvalidOperationException("Saved strong start refused "+d.EnemyId);
            begin=Time.time;camera=new Capture(Path.Combine(Folder,currentId),target,actor,false,30);camera.Mark("windup");
            // Disabled render-only capture cameras are not returned by Camera.main.
            // Supply the actual capture view to the same warning placement used by the live main camera.
            if(Mode.StartsWith("CombatCueVisibility",StringComparison.Ordinal))
                typeof(EnemyStrongAttackWarning).GetField("signalCamera",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,typeof(Capture).GetField("cam",PrivateInstance).GetValue(camera));
            var seen=new System.Collections.Generic.HashSet<int>();bool beforeClip=false,late=false;int mismatches=0;float started=Time.time;
            while(actor.AbilityController.IsExecuting&&Time.time-started<12)
            {
                bool motion=actor.AnimationBridge.TryGetAttackNormalizedTime(ability.AnimatorTrigger,out float n);
                bool eligible=actor.AbilityController.IsParryThreatTo(body);var warning=actor.GetComponent<EnemyStrongAttackWarning>();
                var args=new object[]{0};bool active=(bool)activeMethod.Invoke(actor.AbilityController,args);int strike=(int)args[0];
                if(eligible&&!motion)beforeClip=true;
                if(eligible&&damages.Any(x=>(int)x["phase"]==strike))late=true;
                // Warning is refreshed in Update; sample once again at render end for the authoritative comparison.
                parryTrace=new JObject{["motionPresent"]=motion,["normalized"]=n,["strike"]=strike,["eligible"]=eligible,["cue"]=warning!=null&&warning.FinalSignal,["signalCount"]=warning!=null?(int)signalProperty.GetValue(warning):0,["activeWindow"]=active};
                phase=active?"parry_window_"+(strike+1):"between_hits";
                if(active&&seen.Add(strike))camera.Mark(phase);
                if(warning!=null&&warning.FinalSignal!=eligible)mismatches++;
                yield return null;
            }
            var finalWarning=actor.GetComponent<EnemyStrongAttackWarning>();int count=finalWarning!=null?(int)signalProperty.GetValue(finalWarning):0;
            results.Add(new JObject{["scenario"]="multi_hit_motion_windows",["id"]=d.EnemyId,["name"]=d.DisplayName,["ability"]=ability.AbilityId,["hitCount"]=ability.HitCount,["parryStrikeCount"]=ability.ParryStrikeCount,["firstStrikeOnlyParry"]=ability.FirstStrikeOnlyParry,["speed"]=speed,["signalCount"]=count,["seenWindows"]=new JArray(seen.OrderBy(i=>i)),["beforeClipDenied"]=!beforeClip,["deliveredStrikeClosed"]=!late,["updateOrderSampleDifferences"]=mismatches,["damageEvents"]=damages.DeepClone(),["spatialDebug"]=spatialDebug,["captureCameraUsedForCue"]=Mode.StartsWith("CombatCueVisibility",StringComparison.Ordinal),["authoredCueAnchor"]=actor.GetComponent<EnemyParryCueAnchor>()?.Head?.name,["actualPlayerInput"]=false});
            if(beforeClip||late||seen.Count!=ability.ParryStrikeCount||count!=ability.ParryStrikeCount)throw new InvalidOperationException("Parry signal failed "+d.EnemyId+" "+count+"/"+ability.ParryStrikeCount);
            SaveCase(currentId);yield return null;
        }
        if(Mode.StartsWith("CombatCueVisibility",StringComparison.Ordinal))yield break;
        yield return ParryCancelAndPool(catalog,hp,body);
        yield return ParryBoundaryAndFreeze(catalog,hp,body);
    }
    static IEnumerator ParryCancelAndPool(EnemyCatalog catalog,CombatHealth hp,CombatTarget body)
    {
        catalog.TryGet("DeathHarvest_Reaper",out var d);var ability=Strong(d);
        var cancel=typeof(PlayerParryController).GetMethod("CancelAndReact",BindingFlags.Static|BindingFlags.NonPublic);
        var active=typeof(EnemyAbilityController).GetMethod("TryGetActiveParryMotionWindow");
        foreach(int strike in Enumerable.Range(0,ability.ParryStrikeCount))
        {
            currentId=d.EnemyId+"_parry_cancel_"+strike;begin=Time.time;trace=new JArray();damages=new JArray();phase="strong_attack";
            target.position=Vector3.forward*2.5f;hp.SetMaxHp(1000000,true);Physics.SyncTransforms();
            if(!service.TrySpawn(new EnemySpawnRequest(d,Vector3.up*.035f,Quaternion.identity,target,context:EncounterContext.Test),out actor))throw new InvalidOperationException("parry cancel");
            actor.AI.enabled=false;actor.Movement.StopMovement();actor.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;cadenceField.SetValue(actor.AbilityController,3);yield return null;
            if(!actor.AbilityController.TryStartAbility(ability,target))throw new InvalidOperationException("parry strong start");
            camera=new Capture(Path.Combine(Folder,currentId),target,actor,false,30);camera.Mark("strong_attack");
            float deadline=Time.time+8;bool reached=false;
            while(Time.time<deadline&&actor.AbilityController.IsExecuting){var args=new object[]{0};if((bool)active.Invoke(actor.AbilityController,args)&&(int)args[0]==strike&&actor.AbilityController.IsParryThreatTo(body)){reached=true;break;}yield return null;}
            if(!reached)throw new InvalidOperationException("parry strike not reached");
            int damageBefore=damages.Count;cancel.Invoke(null,new object[]{actor,target.position,ParryGrade.Normal});phase="production_cancel_and_react";camera.Mark(phase);
            yield return new WaitForSeconds(1.5f);
            bool cleared=!actor.AbilityController.IsExecuting&&!actor.AbilityController.IsParryThreatTo(body)&&EnemyStrongAttackWarning.ActiveThreatSignalCount==0&&damages.Count==damageBefore;
            int instance=actor.GetInstanceID();actor.AbilityController.ResetForReuse();service.Release(actor);actor=null;yield return null;
            if(!service.TrySpawn(new EnemySpawnRequest(d,Vector3.up*.035f,Quaternion.identity,target,context:EncounterContext.Test),out actor))throw new InvalidOperationException("parry pool");
            actor.AI.enabled=false;yield return null;
            bool reuse=!actor.AbilityController.IsExecuting&&!actor.AbilityController.IsParryThreatTo(body)&&EnemyStrongAttackWarning.ActiveThreatSignalCount==0;
            results.Add(new JObject{["scenario"]="production_parry_cancel_and_pool",["id"]=d.EnemyId,["strike"]=strike,["remainingDamageCancelled"]=cleared,["poolReuseFresh"]=reuse,["sameInstance"]=actor.GetInstanceID()==instance,["parryGrade"]="Normal",["productionCancelAndReactInvoked"]=true,["actualPlayerInput"]=false});
            if(!cleared||!reuse)throw new InvalidOperationException("parry cancel or reuse failed");SaveCase(currentId);yield return null;
        }
    }
    static IEnumerator ParryBoundaryAndFreeze(EnemyCatalog catalog,CombatHealth hp,CombatTarget body)
    {
        catalog.TryGet("DeathHarvest_Reaper",out var d);var ability=Strong(d);
        currentId=d.EnemyId+"_window_boundaries_and_freeze";begin=Time.time;trace=new JArray();damages=new JArray();phase="actual_animator_boundary_samples";
        target.position=Vector3.forward*2.5f;hp.SetMaxHp(1000000,true);Physics.SyncTransforms();
        if(!service.TrySpawn(new EnemySpawnRequest(d,Vector3.up*.035f,Quaternion.identity,target,context:EncounterContext.Test),out actor))throw new InvalidOperationException("boundary spawn");
        actor.AI.enabled=false;actor.Movement.StopMovement();actor.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;cadenceField.SetValue(actor.AbilityController,3);yield return null;
        if(!actor.AbilityController.TryStartAbility(ability,target))throw new InvalidOperationException("boundary strong start");
        float deadline=Time.time+8,n=0;while(!actor.AnimationBridge.TryGetAttackNormalizedTime(ability.AnimatorTrigger,out n)&&Time.time<deadline)yield return null;
        var active=typeof(EnemyAbilityController).GetMethod("TryGetActiveParryMotionWindow");
        var window=typeof(EnemyAbilityDefinition).GetMethod("TryGetParryMotionWindow");
        var checks=new JArray();
        for(int i=0;i<ability.ParryStrikeCount;i++)
        {
            var wargs=new object[]{i,default(Vector2)};if(!(bool)window.Invoke(ability,wargs))throw new InvalidOperationException("window missing");var w=(Vector2)wargs[1];
            foreach(var sample in new[]{(w.x-.001f,false),(w.x+.001f,true),(w.y-.001f,true),(w.y+.001f,false)})
            {
                string stateName=ability.AnimatorTrigger.StartsWith("Attack")&&ability.AnimatorTrigger.Length>6?"Attack_"+ability.AnimatorTrigger.Substring(6):ability.AnimatorTrigger;
                actor.Animator.Play("Base Layer."+stateName,0,sample.Item1);actor.Animator.Update(0f);
                bool hasMotion=actor.AnimationBridge.TryGetAttackNormalizedTime(ability.AnimatorTrigger,out float sampledPhase);
                var args=new object[]{0};bool actual=(bool)active.Invoke(actor.AbilityController,args);
                checks.Add(new JObject{["strike"]=i,["sampleNormalized"]=sample.Item1,["expected"]=sample.Item2,["actual"]=actual,["hasMotion"]=hasMotion,["actualNormalized"]=sampledPhase});
                if(actual!=sample.Item2)throw new InvalidOperationException("actual animator boundary failed "+i+" "+sample.Item1);
            }
            actor.AbilityController.NotifyAbilityImpact(ability,i);
            string consumedState=ability.AnimatorTrigger.StartsWith("Attack")&&ability.AnimatorTrigger.Length>6?"Attack_"+ability.AnimatorTrigger.Substring(6):ability.AnimatorTrigger;
            actor.Animator.Play("Base Layer."+consumedState,0,(w.x+w.y)*.5f);actor.Animator.Update(0f);
            var consumed=new object[]{0};if((bool)active.Invoke(actor.AbilityController,consumed))throw new InvalidOperationException("Consumed strike reopened");
        }
        // Direct Animator.Play boundary sampling is not a naturally completed attack.
        // Re-enter through the production pool before the independent freeze flow.
        actor.AbilityController.Cancel();service.Release(actor);actor=null;yield return new WaitForSeconds(1f);
        if(!service.TrySpawn(new EnemySpawnRequest(d,Vector3.up*.035f,Quaternion.identity,target,context:EncounterContext.Test),out actor))throw new InvalidOperationException("freeze spawn");
        actor.AI.enabled=false;actor.Movement.StopMovement();actor.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;cadenceField.SetValue(actor.AbilityController,3);yield return null;
        deadline=Time.time+3;
        while(!actor.AbilityController.TryStartAbility(ability,target)&&Time.time<deadline){actor.Movement.FacePosition(target.position);yield return null;}
        if(!actor.AbilityController.IsExecuting)throw new InvalidOperationException("freeze source start after production pool reset");
        deadline=Time.time+8;
        while(!actor.AbilityController.IsParryThreatTo(body)&&Time.time<deadline)yield return null;
        if(!actor.AbilityController.IsParryThreatTo(body))throw new InvalidOperationException("freeze window reached");
        actor.Movement.SetStatusMoveSpeedMultiplier(0f);actor.Melee.SetStatusActionSpeedMultiplier(0f);actor.AnimationBridge.SetFrozen(true);
        yield return new WaitForSeconds(.25f);
        bool cancelled=!actor.AbilityController.IsExecuting&&!actor.AbilityController.IsParryThreatTo(body)&&EnemyStrongAttackWarning.ActiveThreatSignalCount==0;
        actor.AnimationBridge.SetFrozen(false);actor.Movement.SetStatusMoveSpeedMultiplier(1f);actor.Melee.SetStatusActionSpeedMultiplier(1f);
        yield return null;
        results.Add(new JObject{["scenario"]="native_motion_boundaries_and_existing_freeze_cancel",["id"]=d.EnemyId,["boundarySamples"]=checks,["consumedStrikesClosed"]=true,["existingFreezeCancelsAttack"]=cancelled,["actualColdBuildup"]=false,["actualPlayerInput"]=false});
        if(!cancelled)throw new InvalidOperationException("freeze stale cue or attack");SaveCase(currentId);yield return null;
    }

}
