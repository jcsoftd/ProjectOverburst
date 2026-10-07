using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class MonsterWeakAttackPlayerLoopVerifier
{
    // The saved strong is played unchanged; this demonstration does not request a player parry.
    static IEnumerator RunStrongCueReviewCase(EnemyDefinition definition,EnemySpawnService service,PlayerInputFacade player,
        PlayerActorRuntime playerActor,MeleeRuntime melee,MonsterParryVideoCapture capture,string output)
    {
        EnemyActor enemy=null;EnemyAbilitySet practiceSet=null,savedSet=null;
        var disabled=new Dictionary<Behaviour,bool>();var events=new JArray();var samples=new JArray();
        var pendingDamage=new Queue<JObject>();Action<CombatHealth,DamageInfo,float,bool> damageObserver=null;
        var knockdown=player.GetComponent<PlayerKnockdownController>();bool knockdownEnabled=knockdown!=null&&knockdown.enabled;
        try
        {
            var strong=Enumerable.Range(0,definition.AbilitySet.Count).Select(i=>definition.AbilitySet.GetAbility(i)).Single(a=>a.IsParryable);
            float minimum=strong.MinimumRange+.35f;
            float distance=Mathf.Max(1.6f,minimum+.3f);
            Vector3 spawn=player.transform.position+Vector3.forward*distance;
            ParryRequire(Physics.Raycast(spawn+Vector3.up*4,Vector3.down,out var floor,9,LayerMask.GetMask("Default","Environment","Ground")),"Cue capture spawn floor missing");
            ParryRequire(service.TrySpawn(new EnemySpawnRequest(definition,floor.point+Vector3.up*.035f,Quaternion.LookRotation(Vector3.back),player.transform,context:EncounterContext.Test),out enemy),"Cue capture saved Actor spawn failed");
            foreach(var component in enemy.GetComponentsInChildren<Behaviour>(true))
                if(component!=null&&(component==enemy.AI||new[]{"EnemyCrowdAgent","EnemySensor"}.Contains(component.GetType().Name)))
                {disabled[component]=component.enabled;component.enabled=false;}
            enemy.Movement.StopMovement();enemy.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            enemy.Health.SetMaxHp(100000,true);playerActor.Health.SetMaxHp(100000,true);
            if(knockdown!=null)knockdown.enabled=false; // Keep the inspection target in range across 2/3 strikes; restored in finally.
            melee.CancelCurrentAttackState();
            savedSet=enemy.AbilityController.AbilitySet;
            practiceSet=ScriptableObject.CreateInstance<EnemyAbilitySet>();practiceSet.Configure("StrongCueInspection_"+definition.EnemyId,new[]{strong});
            enemy.AbilityController.Configure(practiceSet,1,1);
            float inner=EnemyAttackThreatGeometry.ResolveSectorInnerRadius(enemy,strong);
            distance=Mathf.Min(Mathf.Max(distance,inner+.65f),EnemyAttackThreatGeometry.ResolveStartRange(enemy,strong)-.12f);
            ParryRequire(distance>Mathf.Max(strong.MinimumRange,inner+.2f),"Cue capture has no native valid target range");
            Vector3 targetPosition=enemy.transform.position+enemy.transform.forward*distance;
            ParryRequire(Physics.Raycast(targetPosition+Vector3.up*4,Vector3.down,out var targetFloor,9,LayerMask.GetMask("Default","Environment","Ground")),"Cue target floor missing");
            var capsule=player.GetComponent<CharacterController>();bool capsuleEnabled=capsule!=null&&capsule.enabled;
            if(capsuleEnabled)capsule.enabled=false;player.transform.position=targetFloor.point+Vector3.up*.035f;if(capsuleEnabled)capsule.enabled=true;
            playerActor.Movement?.ResetMotionAfterTeleport();Physics.SyncTransforms();
            yield return null;yield return new WaitForFixedUpdate();
            var parry=player.GetComponent<PlayerParryController>();int parryBefore=parry!=null?parry.SuccessCount:0;
            capture.BeginTake(output,player.transform,enemy.transform,"actual-strong-cue.mp4","before-attack");
            float recordingStart=Time.time;bool started=false,ended=false;int seenSignals=0;bool previousLegacy=false;
            damageObserver=(health,info,hpLoss,dead)=>
            {
                if(info.enemyAbility!=strong||info.source!=enemy.gameObject)return;
                bool animated=enemy.AnimationBridge.TryGetAttackNormalizedTime(strong.AnimatorTrigger,out float phase);
                pendingDamage.Enqueue(new JObject{["event"]="hit-"+(info.sourceAttackPhaseIndex+1),
                    ["strike"]=info.sourceAttackPhaseIndex+1,["gameSeconds"]=Time.time-recordingStart,
                    ["originalNormalized"]=animated?(JToken)phase:null,
                    ["originalClipSeconds"]=animated?(JToken)(phase*strong.AttackAnimationDuration):null,
                    ["actualHpLoss"]=hpLoss,["playerHp"]=health.CurrentHp,["distanceMeters"]=Vector3.Distance(enemy.transform.position,player.transform.position)});
            };
            playerActor.Health.OnDamageResolved+=damageObserver;
            capture.FrameObserved=(frame,time)=>
            {
                bool executing=enemy.AbilityController.IsExecuting&&enemy.AbilityController.LastCommittedAbility==strong;
                float normalized=0;bool animated=enemy.AnimationBridge.TryGetAttackNormalizedTime(strong.AnimatorTrigger,out normalized);
                Action<string,int> mark=(name,strike)=>
                {
                    capture.Mark(name);events.Add(new JObject{["event"]=name,["strike"]=strike,["frame1Based"]=frame,["videoSeconds"]=time,
                        ["gameSeconds"]=Time.time-recordingStart,["originalNormalized"]=animated?(JToken)normalized:null,
                        ["originalClipSeconds"]=animated?(JToken)(normalized*strong.AttackAnimationDuration):null});
                };
                while(pendingDamage.Count>0)
                {
                    var damage=pendingDamage.Dequeue();damage["frame1Based"]=frame;damage["videoSeconds"]=time;
                    damage["frameOriginalNormalized"]=animated?(JToken)normalized:null;
                    capture.Mark((string)damage["event"]);events.Add(damage);
                }
                if(executing&&!started){started=true;mark("attack-start",0);}
                var warning=enemy.GetComponent<EnemyStrongAttackWarning>();
                if(started&&warning!=null)
                {
                    bool actualSignal=warning.IsVisible&&warning.FinalSignal&&EnemyStrongAttackWarning.ActiveThreatSignalCount>0;
                    if(strong.HasParryMotionWindows)
                    {
                        if(warning.ParrySignalCount>seenSignals)
                        {seenSignals=warning.ParrySignalCount;mark("cue-"+seenSignals,seenSignals);}
                    }
                    else if(actualSignal&&!previousLegacy){seenSignals++;mark("cue-"+seenSignals,seenSignals);}
                    previousLegacy=actualSignal;
                }
                if(started&&!executing&&!ended){ended=true;mark("attack-end",0);}
            };
            float preRoll=Time.time+.8f;while(Time.time<preRoll)yield return null;
            enemy.AbilityController.ClearPreparedAim();
            ParryRequire(enemy.AbilityController.TryStartAbility(strong,player.transform),"Saved strong refused in cue practice placement");
            float deadline=Time.time+strong.ResolveExecutionDuration(enemy.Melee.AbilityAnimationSpeed)+5;
            var target=player.GetComponent<CombatTarget>();
            while(enemy.AbilityController.IsExecuting)
            {
                ParryRequire(Time.time<deadline,"Cue attack execution timeout");
                if(enemy.AnimationBridge.TryGetAttackNormalizedTime(strong.AnimatorTrigger,out float phase))
                {
                    bool authoredOpen=false;
                    for(int strike=0;strike<strong.HitCount;strike++)
                        if(strong.TryGetParryMotionWindow(strike,out var w)&&phase>=w.x&&phase<=w.y)authoredOpen=true;
                    samples.Add(new JObject{["gameSeconds"]=Time.time-recordingStart,["originalNormalized"]=phase,
                        ["eligible"]=enemy.AbilityController.IsParryThreatTo(target),["authoredOpen"]=authoredOpen});
                }
                yield return null;
            }
            float tail=Time.time+.4f;while(Time.time<tail)yield return null;
            capture.FrameObserved=null;bool recorded=capture.Complete();
            bool noParry=parry==null||parry.SuccessCount==parryBefore;
            var cues=events.Where(e=>((string)e["event"]).StartsWith("cue-",StringComparison.Ordinal)).ToArray();
            bool preAttack=events.Any(e=>(string)e["event"]=="attack-start"&&(float)e["videoSeconds"]>=.7f);
            bool eligibilityMatches=!strong.HasParryMotionWindows||samples.Count>0&&samples.All(s=>!(bool)s["eligible"]||(bool)s["authoredOpen"]);
            var damageHits=events.Where(e=>((string)e["event"]).StartsWith("hit-",StringComparison.Ordinal)).ToArray();
            bool damageCoverage=damageHits.Length==strong.HitCount&&damageHits.Select(e=>(int)e["strike"]).Distinct().Count()==strong.HitCount;
            bool firstOnlyMatches=!strong.FirstStrikeOnlyParry||samples.Count>0
                &&samples.All(s=>(float)s["originalNormalized"]<=strong.GetHitNormalizedTime(0)+.00001f||!(bool)s["eligible"]);
            bool observedEligibility=samples.Any(s=>(bool)s["eligible"]);
            bool pass=recorded&&started&&ended&&preAttack&&noParry&&cues.Length==strong.ParryStrikeCount
                &&eligibilityMatches&&firstOnlyMatches&&observedEligibility&&damageCoverage;
            var result=new JObject{["pass"]=pass,["id"]=definition.EnemyId,["strongCueReview"]=true,
                ["actualPlayerHeavyParry"]=false,["playerParryInputRequested"]=false,["actualParrySuccessCount"]=parry==null?0:parry.SuccessCount-parryBefore,
                ["strongAbility"]=strong.AbilityId,["savedStrongUnmodified"]=true,["cueEvents"]=events,["cueCount"]=cues.Length,["expectedCueCount"]=strong.ParryStrikeCount,
                ["beforeAttackCaptured"]=preAttack,["fixtureAISelectionDisabled"]=true,["fixturePlayerKnockdownSuppressed"]=true,
                ["authoredParryWindows"]=strong.HasParryMotionWindows,["eligibilityMatchesAuthoredWindows"]=eligibilityMatches,
                ["firstStrikeOnlyParry"]=strong.FirstStrikeOnlyParry,["firstOnlyEligibilityVerified"]=firstOnlyMatches,["observedParryEligibility"]=observedEligibility,
                ["runtimeEligibilitySamples"]=samples,["nativeDamageHitCount"]=damageHits.Length,
                ["nativeDamageCoverageComplete"]=damageCoverage,
                ["damageFramePrecisionSeconds"]=1f/30,["abilityAnimationSpeed"]=enemy.Melee.AbilityAnimationSpeed,
                ["videoRecorded"]=recorded,["videoPath"]=capture.VideoPath,["failure"]=pass?null:"Review video, pre-roll or expected cue coverage failed"};
            File.WriteAllText(Path.Combine(output,"cue-events.json"),result.ToString());cases.Add(result);
        }
        finally
        {
            if(damageObserver!=null)playerActor.Health.OnDamageResolved-=damageObserver;
            capture.FrameObserved=null;capture.Complete();
            if(enemy!=null)
            {
                enemy.AbilityController.Cancel();if(savedSet!=null)enemy.AbilityController.Configure(savedSet,1,1);
                foreach(var pair in disabled)if(pair.Key!=null)pair.Key.enabled=pair.Value;
                if(enemy.IsLeased)service.Release(enemy);
            }
            if(practiceSet!=null)UnityEngine.Object.DestroyImmediate(practiceSet);
            if(knockdown!=null)knockdown.enabled=knockdownEnabled;
            melee.CancelCurrentAttackState();
        }
    }
}
