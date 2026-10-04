using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;
public static partial class MonsterWeakAttackPlayerLoopVerifier
{
    public static string StartRealPlayerParry(string outputDirectory,string definitionPath)
    {
        if(!definitionPath.StartsWith("Assets/ProjectOverburst/Resources/Enemies/Themes/Definitions/",StringComparison.Ordinal)
            ||AssetDatabase.LoadAssetAtPath<EnemyDefinition>(definitionPath)?.IsValid!=true)throw new ArgumentException("Saved review Actor required.");
        return StartInternal(outputDirectory,false,1,false,true,false,definitionPath,true);
    }
    static void ParryRequire(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    static IEnumerator RunRealPlayerParryCases()
    {
        EnemySpawnService service=null;EnemyActor enemy=null;MeleeRuntime melee=null;
        var trace=new JArray();var states=new HashSet<string>();bool enteredDungeon=false;
        try
        {
            float boot=Time.unscaledTime+60;
            while(PersistentSceneFlow.Instance==null||PersistentSceneFlow.Instance.IsSwitching||!WorldSessionState.IsHideout||PlayerInputFacade.Current==null||AccountGameplaySession.Current==null)
            {ParryRequire(Time.unscaledTime<boot,"Actual player boot timeout");yield return null;}
            ParryRequire(Same(Overburst.Persistence.AccountBootstrap.SaveDirectory,Account),"Actual player account mismatch");
            var player=PlayerInputFacade.Current;var playerActor=PlayerContext.GetOrCreate().CurrentActor;
            var weapon=AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
            ParryRequire(playerActor.Equipment.EquipWeaponItem(new ItemData(weapon,1,ItemGrade.Common)),"Actual greatsword equip failed");
            yield return null; // Let equipment/account synchronization finish before requesting a run.
            var entry=UnityEngine.Object.FindFirstObjectByType<DungeonDebugEntry>(FindObjectsInactive.Include);
            if(entry==null)entry=host.gameObject.AddComponent<DungeonDebugEntry>();
            ParryRequire(entry.TryEnter(1,false),"Actual dungeon entry failed: "+entry.Status+"; "+PersistentSceneFlow.Instance.RunEntryError);enteredDungeon=true;
            while(entry.IsEntering||PersistentSceneFlow.Instance.IsSwitching){ParryRequire(Time.unscaledTime<boot,"Dungeon boot timeout");yield return null;}
            ParryRequire(WorldSessionState.Phase==WorldPhase.Run,"Actual dungeon did not start");
            var world=UnityEngine.Object.FindFirstObjectByType<DiamondDungeonWorld>();
            foreach(var field in world.Fields)field.enabled=false;foreach(var evt in world.EventDirector.Events)evt.enabled=false;
            service=world.SpawnBudget.GetComponent<EnemySpawnService>();
            var ranks=new List<EnemyRank>();EnemyRank.CollectActive(ranks);
            foreach(var rank in ranks){var actor=rank.GetComponent<EnemyActor>();if(actor!=null&&actor.IsLeased)service.Release(actor);}
            var definition=AssetDatabase.LoadAssetAtPath<EnemyDefinition>(plan.testDefinition);
            var catalog=ScriptableObject.CreateInstance<EnemyCatalog>();owned.Add(catalog);catalog.Configure(new[]{definition});
            ParryRequire(service.RegisterAdditionalCatalog(catalog,out string error),error);
            playerActor.Health.SetMaxHp(100000,true);
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
            melee=player.GetComponent<MeleeRuntime>();melee.SetManualInputEnabled(true);
            var target=player.GetComponent<CombatTarget>();
            Vector3 spawn=player.transform.position+Vector3.forward*1.4f;
            ParryRequire(Physics.Raycast(spawn+Vector3.up*4,Vector3.down,out var floor,9,LayerMask.GetMask("Default","Environment","Ground")),"Actual spawn floor missing");
            ParryRequire(service.TrySpawn(new EnemySpawnRequest(definition,floor.point+Vector3.up*.035f,Quaternion.LookRotation(Vector3.back),player.transform,context:EncounterContext.Test),out enemy),"Actual saved Actor spawn failed");
            enemy.Health.SetMaxHp(100000,true);enemy.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            enemy.AI.RequestAggro(player.transform);yield return null;
            float attackDeadline=Time.time+35;EnemyAbilityDefinition strong=null;float originalAttackTime=0;
            while(true)
            {
                strong=enemy.AbilityController.LastCommittedAbility;
                if(strong!=null&&strong.IsTelegraphedStrongAttack&&enemy.AbilityController.IsExecuting
                    &&enemy.AbilityController.IsParryThreatTo(target)
                    &&enemy.AnimationBridge.TryGetAttackNormalizedTime(strong.AnimatorTrigger,out originalAttackTime)&&originalAttackTime>=.05f)break;
                ParryRequire(Time.time<attackDeadline,"AI did not produce an animated, parryable strong attack");yield return null;
            }
            // The saved full ability set and AI choose the strong. No stun/Animator call or cadence override.
            var controller=player.GetComponent<PlayerParryController>();int before=controller!=null?controller.SuccessCount:0;
            float health=playerActor.Health.CurrentHp;
            ParryRequire(melee.TryStartHeavyAttack(enemy.transform.position-player.transform.position)==WeaponActionResult.Accepted,"Actual heavy input rejected");
            controller=player.GetComponent<PlayerParryController>();ParryRequire(controller!=null,"Player parry controller missing");
            float parryDeadline=Time.unscaledTime+1;
            while(controller.SuccessCount==before){ParryRequire(Time.unscaledTime<parryDeadline,"Real parry judgment did not succeed");yield return null;}
            ParryRequire(controller.SuccessCount==before+1&&!enemy.AbilityController.IsExecuting&&enemy.GetComponent<EnemyMovementReaction>().IsParryStunned,"Real success/cancel/stun mismatch");
            ParryRequire(playerActor.Health.CurrentHp==health,"Player took damage during successful parry");
            float started=Time.time;float expectedHold=definition.AnimationProfile.ParryCollapse.length/EnemyAnimationBridge.ParryCollapseSpeed+EnemyAnimationBridge.ParryStunnedSeconds;
            float expectedRecover=definition.AnimationProfile.StunRecover.length;bool lockPreserved=true;float recoverFirst=-1;
            float deadline=Time.time+expectedHold+expectedRecover+4;
            while(Time.time<deadline)
            {
                var current=enemy.Animator.GetCurrentAnimatorStateInfo(0);var next=enemy.Animator.GetNextAnimatorStateInfo(0);
                foreach(string name in new[]{EnemyAnimationBridge.ParryCollapseStateName,EnemyAnimationBridge.StunnedLoopStateName,EnemyAnimationBridge.StunRecoverStateName,"Locomotion"})
                    if(current.IsName(name)||enemy.Animator.IsInTransition(0)&&next.IsName(name)){states.Add(name);if(name==EnemyAnimationBridge.StunRecoverStateName&&recoverFirst<0)recoverFirst=Time.time-started;}
                bool blocked=enemy.AnimationBridge.IsParryStunAnimating;
                if(blocked)lockPreserved&=!enemy.AbilityController.IsExecuting&&!enemy.Melee.IsAttacking;
                trace.Add(new JObject{["frame"]=Time.frameCount,["gameSeconds"]=Time.time-started,["blocked"]=blocked,["parryStunned"]=enemy.GetComponent<EnemyMovementReaction>().IsParryStunned,["ai"]=enemy.AI.CurrentStateName,["attacking"]=enemy.AbilityController.IsExecuting});
                if(!blocked&&states.Contains(EnemyAnimationBridge.StunRecoverStateName)&&current.IsName("Locomotion"))break;
                yield return null;
            }
            ParryRequire(!enemy.AnimationBridge.IsParryStunAnimating&&states.Contains("Locomotion"),"Recovery did not reach locomotion");
            ParryRequire(new[]{EnemyAnimationBridge.ParryCollapseStateName,EnemyAnimationBridge.StunnedLoopStateName,EnemyAnimationBridge.StunRecoverStateName}.All(states.Contains)&&lockPreserved,"Role states or action lock missing");
            float resume=Time.time+10;bool resumed=false;
            while(Time.time<resume){if(enemy.AbilityController.IsExecuting){resumed=true;break;}yield return null;}
            ParryRequire(resumed,"AI did not resume a normal saved attack");
            string resumedAbility=enemy.AbilityController.LastCommittedAbility.AbilityId;
            service.Release(enemy);yield return null;yield return new WaitForFixedUpdate();
            bool reset=!enemy.IsLeased&&!enemy.gameObject.activeSelf&&!enemy.AnimationBridge.IsParryStunAnimating&&!enemy.GetComponent<EnemyMovementReaction>().IsParryStunned&&!enemy.AbilityController.IsExecuting;
            ParryRequire(reset,"Parry state leaked into the pool");
            cases.Add(new JObject{["pass"]=true,["id"]=definition.EnemyId,["actualPlayerHeavyParry"]=true,["parrySuccessDelta"]=controller.SuccessCount-before,
                ["strongAbility"]=strong.AbilityId,["strongNormalizedAtInput"]=originalAttackTime,["allSavedAbilitiesRetained"]=true,["states"]=JArray.FromObject(states),
                ["actionLockPreserved"]=lockPreserved,["recoverFirstSeconds"]=recoverFirst,["expectedHoldSeconds"]=expectedHold,["expectedRecoverSeconds"]=expectedRecover,
                ["resumedAbility"]=resumedAbility,["poolReset"]=reset,["playerDamageAtParry"]=0,["videoRecorded"]=false});
            enemy=null;melee.CancelCurrentAttackState();
            File.WriteAllText(Path.Combine(plan.directory,"parry-trace.json"),trace.ToString());
            PersistentSceneFlow.Instance.GetComponent<RunLifetimeDriver>().RequestAbandon();enteredDungeon=false;
            while(PersistentSceneFlow.Instance.IsSwitching||!WorldSessionState.IsHideout)yield return null;
        }
        finally
        {
            melee?.CancelCurrentAttackState();
            if(enemy!=null&&enemy.IsLeased&&service!=null)service.Release(enemy);
            if(enteredDungeon&&PersistentSceneFlow.Instance!=null&&!PersistentSceneFlow.Instance.IsSwitching)PersistentSceneFlow.Instance.GetComponent<RunLifetimeDriver>()?.RequestAbandon();
            if(trace.Count>0)File.WriteAllText(Path.Combine(plan.directory,"parry-trace.json"),trace.ToString());
        }
    }
}
