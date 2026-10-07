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
    static MonsterParryVideoCapture activeParryCapture;
    const int PerfectParryFixtureVersion=8;
    static IEnumerator CaptureParryFrames(MonsterParryVideoCapture capture)
    { while(true) { yield return null;capture.CaptureFrame(); } }
    static void ParryRequire(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    public static string StartRealPlayerParryBatch(string outputDirectory,string[] definitionPaths)
    {
        if(definitionPaths==null||definitionPaths.Length==0||definitionPaths.Distinct().Count()!=definitionPaths.Length)
            throw new ArgumentException("Distinct saved parry targets required.");
        foreach(string path in definitionPaths)
        {
            var definition=AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path);
            if(!path.StartsWith("Assets/ProjectOverburst/Resources/Enemies/Themes/Definitions/",StringComparison.Ordinal)
                ||definition?.IsValid!=true||definition.AnimationProfile.ParryCollapse==null
                ||definition.AnimationProfile.StunnedLoop==null||definition.AnimationProfile.StunRecover==null)
                throw new ArgumentException("Saved approved parry roles required: "+path);
        }
        return StartInternal(outputDirectory,false,1,false,true,false,null,true,null,definitionPaths);
    }
    static IEnumerator RunRealPlayerParryCases()
    {
        EnemySpawnService service=null;MeleeRuntime melee=null;
        PlayerInputFacade player=null;PlayerActorRuntime playerActor=null;
        bool enteredDungeon=false;MonsterParryVideoCapture capture=null;Coroutine captureRoutine=null;
        try
        {
            float boot=Time.unscaledTime+180;
            while(PersistentSceneFlow.Instance==null||PersistentSceneFlow.Instance.IsSwitching||!WorldSessionState.IsHideout||PlayerInputFacade.Current==null||AccountGameplaySession.Current==null)
            {ParryRequire(Time.unscaledTime<boot,"Actual player boot timeout");yield return null;}
            ParryRequire(Same(Overburst.Persistence.AccountBootstrap.SaveDirectory,Account),"Actual player account mismatch");
            player=PlayerInputFacade.Current;playerActor=PlayerContext.GetOrCreate().CurrentActor;
            var weapon=AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
            ParryRequire(playerActor.Equipment.EquipWeaponItem(new ItemData(weapon,1,ItemGrade.Common)),"Actual greatsword equip failed");
            yield return null; // Let equipment/account synchronization finish before requesting a run.
            var entry=UnityEngine.Object.FindFirstObjectByType<DungeonDebugEntry>(FindObjectsInactive.Include);
            if(entry==null)entry=host.gameObject.AddComponent<DungeonDebugEntry>();
            ParryRequire(entry.TryEnter(1,false),"Actual dungeon entry failed: "+entry.Status+"; "+PersistentSceneFlow.Instance.RunEntryError);enteredDungeon=true;
            float dungeonBoot=Time.unscaledTime+180;
            while(entry.IsEntering||PersistentSceneFlow.Instance.IsSwitching){ParryRequire(Time.unscaledTime<dungeonBoot,"Dungeon boot timeout: "+entry.Status+"; "+PersistentSceneFlow.Instance.RunEntryError);yield return null;}
            ParryRequire(WorldSessionState.Phase==WorldPhase.Run,"Actual dungeon did not start");
            var world=UnityEngine.Object.FindFirstObjectByType<DiamondDungeonWorld>();
            foreach(var field in world.Fields)field.enabled=false;foreach(var evt in world.EventDirector.Events)evt.enabled=false;
            service=world.SpawnBudget.GetComponent<EnemySpawnService>();
            var ranks=new List<EnemyRank>();EnemyRank.CollectActive(ranks);
            foreach(var rank in ranks){var actor=rank.GetComponent<EnemyActor>();if(actor!=null&&actor.IsLeased)service.Release(actor);}
            var definitions=(plan.leaseDefinitionPaths??new[]{plan.testDefinition}).Select(AssetDatabase.LoadAssetAtPath<EnemyDefinition>).ToArray();
            var catalog=ScriptableObject.CreateInstance<EnemyCatalog>();owned.Add(catalog);catalog.Configure(definitions);
            ParryRequire(service.RegisterAdditionalCatalog(catalog,out string error),error);
            playerActor.Health.SetMaxHp(100000,true);
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
            melee=player.GetComponent<MeleeRuntime>();melee.SetManualInputEnabled(true);
            Time.captureDeltaTime=1f/60; // Owned deterministic simulation; the existing return restores its prior value.
            capture=new MonsterParryVideoCapture();activeParryCapture=capture;
            captureRoutine=host.StartCoroutine(CaptureParryFrames(capture));
            Vector3 playerCaptureAnchor=player.transform.position;
            Quaternion playerCaptureRotation=player.transform.rotation;
            foreach(var definition in definitions)
            {
                // Each take starts at the same valid dungeon floor. Cumulative
                // real attack/knockback displacement must not leave the arena.
                melee.CancelCurrentAttackState();
                var playerCapsule=player.GetComponent<CharacterController>();bool capsuleEnabled=playerCapsule!=null&&playerCapsule.enabled;
                if(capsuleEnabled)playerCapsule.enabled=false;
                player.transform.SetPositionAndRotation(playerCaptureAnchor,playerCaptureRotation);
                if(capsuleEnabled)playerCapsule.enabled=true;
                playerActor.Movement?.ResetMotionAfterTeleport();Physics.SyncTransforms();
                string output=Path.Combine(plan.directory,definition.EnemyId);Directory.CreateDirectory(output);
                var work=RunRealPlayerParryCase(definition,service,player,playerActor,melee,capture,output);
                string caseError=null;
                try
                {
                    while(true)
                    {
                        bool more=false;object current=null;
                        try { more=work.MoveNext();if(more)current=work.Current; }
                        catch(Exception e) { caseError=e.ToString(); }
                        if(caseError!=null||!more)break;
                        yield return current;
                    }
                }
                finally { (work as IDisposable)?.Dispose(); }
                if(caseError!=null)cases.Add(new JObject{["pass"]=false,["id"]=definition.EnemyId,["failure"]=caseError,
                    ["actualPlayerHeavyParry"]=false,["videoRecorded"]=false,["videoPath"]=capture.VideoPath});
                File.WriteAllText(Path.Combine(output,"case-result.json"),cases.Last.ToString());
                WriteResult("RUNNING");
                melee.CancelCurrentAttackState();yield return null;yield return new WaitForFixedUpdate();
            }
            PersistentSceneFlow.Instance.GetComponent<RunLifetimeDriver>().RequestAbandon();enteredDungeon=false;
            while(PersistentSceneFlow.Instance.IsSwitching||!WorldSessionState.IsHideout)yield return null;
        }
        finally
        {
            if(host!=null&&captureRoutine!=null)host.StopCoroutine(captureRoutine);
            capture?.Dispose();activeParryCapture=null;melee?.CancelCurrentAttackState();
            if(enteredDungeon&&PersistentSceneFlow.Instance!=null&&!PersistentSceneFlow.Instance.IsSwitching)
                PersistentSceneFlow.Instance.GetComponent<RunLifetimeDriver>()?.RequestAbandon();
        }
    }
    static IEnumerator RunRealPlayerParryCase(EnemyDefinition definition,EnemySpawnService service,PlayerInputFacade player,
        PlayerActorRuntime playerActor,MeleeRuntime melee,MonsterParryVideoCapture capture,string output)
    {
        EnemyActor enemy=null;EnemyAbilitySet captureSet=null,savedSet=null;var trace=new JArray();var states=new HashSet<string>();
        var target=player.GetComponent<CombatTarget>();
        try
        {
            var savedStrong=Enumerable.Range(0,definition.AbilitySet.Count).Select(i=>definition.AbilitySet.GetAbility(i)).First(a=>a.IsParryable);
            float captureDistance=Mathf.Clamp(Mathf.Max(1.4f,savedStrong.MinimumRange+.6f),1.4f,Mathf.Max(1.4f,savedStrong.Range-.1f));
            Vector3 spawn=player.transform.position+Vector3.forward*captureDistance;
            ParryRequire(Physics.Raycast(spawn+Vector3.up*4,Vector3.down,out var floor,9,LayerMask.GetMask("Default","Environment","Ground")),"Actual spawn floor missing");
            ParryRequire(service.TrySpawn(new EnemySpawnRequest(definition,floor.point+Vector3.up*.035f,Quaternion.LookRotation(Vector3.back),player.transform,context:EncounterContext.Test),out enemy),"Actual saved Actor spawn failed");
            enemy.Health.SetMaxHp(100000,true);enemy.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            // Capture the recovery even when the real perfect-parry elemental
            // follow-up is lethal. Damage and reaction code still run normally.
            enemy.Health.SetDamageDeathPrevention(host,true);
            string requestFile=Path.Combine(plan.directory,"request-saved-strong.json");
            var request=File.Exists(requestFile)?JObject.Parse(File.ReadAllText(requestFile)):null;
            bool requestStrongForCapture=request!=null&&request["targets"].Values<string>().Contains(definition.EnemyId);
            if(requestStrongForCapture&&(bool?)request["singleSavedStrongPractice"]==true)
            {
                // The same saved strong/Actor and real parry path in a practice
                // setup. Restore the complete saved set before testing resume.
                savedSet=enemy.AbilityController.AbilitySet;
                captureSet=ScriptableObject.CreateInstance<EnemyAbilitySet>();
                captureSet.Configure("NativeStrongCapture_"+definition.EnemyId,new[]{savedStrong});
                enemy.AbilityController.Configure(captureSet,1,1);
            }
            enemy.AI.RequestAggro(player.transform);yield return null;
            float attackDeadline=Time.time+35;EnemyAbilityDefinition strong=null;float originalAttackTime=0;
            float nextRangeCheck=0;int captureRangeRepositions=0;bool strongRequestedForCapture=false;
            while(true)
            {
                // After the required ordinary attack, approach can put the
                // stationary capture target inside a charge's minimum range
                // or the native sector's near inset. Step it out before the
                // recorded strong, retaining the full AI/ability selection.
                if(!capture.IsRecording&&!enemy.AbilityController.IsExecuting
                    &&!enemy.AbilityController.IsStrongAttackLocked&&Time.time>=nextRangeCheck)
                {
                    nextRangeCheck=Time.time+.25f;
                    Vector3 away=player.transform.position-enemy.transform.position;away.y=0;
                    float minimum=Mathf.Max(savedStrong.MinimumRange+.25f,EnemyAttackThreatGeometry.ResolveSectorInnerRadius(enemy,savedStrong)+.35f);
                    if(away.magnitude<minimum||requestStrongForCapture)
                    {
                        if(requestStrongForCapture)away=enemy.transform.forward;
                        if(away.sqrMagnitude<.001f)away=-enemy.transform.forward;
                        float distance=Mathf.Min(Mathf.Max(captureDistance,minimum+.2f),EnemyAttackThreatGeometry.ResolveStartRange(enemy,savedStrong)-.1f);
                        Vector3 position=enemy.transform.position+away.normalized*distance;
                        ParryRequire(distance>minimum,"Capture strong has no valid distance outside its near inset");
                        ParryRequire(Physics.Raycast(position+Vector3.up*4,Vector3.down,out var stepFloor,9,LayerMask.GetMask("Default","Environment","Ground")),"Capture strong has no valid floor outside its near inset");
                        var capsule=player.GetComponent<CharacterController>();bool enabled=capsule!=null&&capsule.enabled;
                        if(enabled)capsule.enabled=false;
                        player.transform.position=stepFloor.point+Vector3.up*.035f;
                        if(enabled)capsule.enabled=true;
                        playerActor.Movement?.ResetMotionAfterTeleport();Physics.SyncTransforms();
                        enemy.AbilityController.ClearPreparedAim();captureRangeRepositions++;
                    }
                    if(requestStrongForCapture)
                        strongRequestedForCapture|=enemy.AbilityController.TryStartAbility(savedStrong,player.transform);
                }
                strong=enemy.AbilityController.LastCommittedAbility;
                if(strong!=null&&strong.IsTelegraphedStrongAttack&&enemy.AbilityController.IsExecuting&&!capture.IsRecording)
                    capture.BeginTake(output,player.transform,enemy.transform);
                if(strong!=null&&strong.IsTelegraphedStrongAttack&&enemy.AbilityController.IsExecuting
                    &&enemy.AbilityController.IsParryThreatTo(target)
                    &&enemy.AnimationBridge.TryGetAttackNormalizedTime(strong.AnimatorTrigger,out originalAttackTime)&&originalAttackTime>=.05f)break;
                ParryRequire(Time.time<attackDeadline,"AI did not produce an animated, parryable strong attack");yield return null;
            }
            // The saved full ability set and AI choose the strong; actual player heavy input performs the parry.
            var controller=player.GetComponent<PlayerParryController>();int before=controller!=null?controller.SuccessCount:0;
            float health=playerActor.Health.CurrentHp;
            // Prepare the isolated gauge after run/account synchronization; the real accepted action owns the grade.
            var energy=melee.GetComponent<OverburstElementEnergy>();
            if(energy==null){energy=melee.gameObject.AddComponent<OverburstElementEnergy>();owned.Add(energy);}
            var equipment=melee.GetComponent<PlayerEquipment>();
            var gem=AssetDatabase.LoadAssetAtPath<ElementGemItemData>("Assets/ProjectOverburst/Resources/Items/ElementGems/EG_Fire_Common.asset");
            var equipGem=typeof(PlayerEquipment).GetMethod("SetElementGem",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            var amount=typeof(OverburstElementEnergy).GetField("<Amount>k__BackingField",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            ParryRequire(equipment!=null&&gem!=null&&equipGem!=null&&amount!=null,"Perfect-parry isolated gauge setup unavailable");
            equipGem.Invoke(equipment,new object[]{new ItemData(gem,1,ItemGrade.Common)});
            energy.BindWeapon(equipment.CurrentWeaponItem.runtimeInstanceId,equipment.ActiveElement,equipment.EquippedElementGem?.runtimeInstanceId,equipment.GemRevision);
            amount.SetValue(energy,energy.BaseMaximum);
            ParryRequire(energy.Normalized>=.8f,"Perfect-parry fixture charge did not reach the product threshold");
            ParryRequire(melee.TryStartHeavyAttack(enemy.transform.position-player.transform.position)==WeaponActionResult.Accepted,"Actual heavy input rejected");
            controller=player.GetComponent<PlayerParryController>();ParryRequire(controller!=null,"Player parry controller missing");
            ParryRequire(controller.ActionGrade==ParryGrade.Perfect,"Accepted heavy did not lock perfect-parry grade");
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
                    if(current.IsName(name)||enemy.Animator.IsInTransition(0)&&next.IsName(name)){if(states.Add(name))capture.Mark(name);if(name==EnemyAnimationBridge.StunRecoverStateName&&recoverFirst<0)recoverFirst=Time.time-started;}
                bool blocked=enemy.AnimationBridge.IsParryStunAnimating;
                if(blocked)lockPreserved&=!enemy.AbilityController.IsExecuting&&!enemy.Melee.IsAttacking;
                trace.Add(new JObject{["frame"]=Time.frameCount,["gameSeconds"]=Time.time-started,["blocked"]=blocked,["parryStunned"]=enemy.GetComponent<EnemyMovementReaction>().IsParryStunned,["ai"]=enemy.AI.CurrentStateName,["attacking"]=enemy.AbilityController.IsExecuting});
                if(!blocked&&states.Contains(EnemyAnimationBridge.StunRecoverStateName)&&current.IsName("Locomotion"))break;
                yield return null;
            }
            ParryRequire(!enemy.AnimationBridge.IsParryStunAnimating&&states.Contains("Locomotion"),"Recovery did not reach locomotion");
            ParryRequire(new[]{EnemyAnimationBridge.ParryCollapseStateName,EnemyAnimationBridge.StunnedLoopStateName,EnemyAnimationBridge.StunRecoverStateName}.All(states.Contains)&&lockPreserved,"Role states or action lock missing");
            if(savedSet!=null)enemy.AbilityController.Configure(savedSet,1,1);
            float resume=Time.time+10;bool resumed=false;
            while(Time.time<resume)
            {
                if(enemy.AbilityController.IsExecuting){resumed=true;break;}
                Vector3 direction=player.transform.position-enemy.transform.position;direction.y=0;
                trace.Add(new JObject{["phase"]="resume",["gameSeconds"]=Time.time-started,
                    ["ai"]=enemy.AI.CurrentStateName,["distance"]=enemy.AI.TargetDistance,["engagementRange"]=enemy.AI.AttackEnterRange,
                    ["facingDegrees"]=Vector3.Angle(enemy.transform.forward,direction),["motionBlocksAttack"]=enemy.AnimationBridge.BlocksAttackStart,
                    ["strongLocked"]=enemy.AbilityController.IsStrongAttackLocked,
                    ["position"]=JArray.FromObject(new[]{enemy.transform.position.x,enemy.transform.position.y,enemy.transform.position.z}),
                    ["cooldowns"]=JArray.FromObject(Enumerable.Range(0,definition.AbilitySet.Count).Select(i=>new {
                        id=definition.AbilitySet.GetAbility(i).AbilityId,
                        seconds=enemy.AbilityController.GetRemainingCooldown(definition.AbilitySet.GetAbility(i))}))});
                yield return null;
            }
            ParryRequire(resumed,"AI did not resume a normal saved attack");
            string resumedAbility=enemy.AbilityController.LastCommittedAbility.AbilityId;
            capture.Mark("resumed");yield return null;yield return null;
            bool recorded=capture.Complete();
            enemy.Health.SetDamageDeathPrevention(host,false);
            ParryRequire(!enemy.Health.IsDeathFromDamagePrevented,"Capture survival lease leaked before pool release");
            service.Release(enemy);yield return null;yield return new WaitForFixedUpdate();
            bool reset=!enemy.IsLeased&&!enemy.gameObject.activeSelf&&!enemy.AnimationBridge.IsParryStunAnimating&&!enemy.GetComponent<EnemyMovementReaction>().IsParryStunned&&!enemy.AbilityController.IsExecuting;
            ParryRequire(reset,"Parry state leaked into the pool");
            cases.Add(new JObject{["pass"]=recorded,["id"]=definition.EnemyId,["actualPlayerHeavyParry"]=true,["parrySuccessDelta"]=controller.SuccessCount-before,
                ["parryGrade"]=controller.ActionGrade.ToString(),["fixtureVersion"]=PerfectParryFixtureVersion,
                ["fixturePreventsLethalDamage"]=true,["captureSurvivalLeaseReturned"]=true,
                ["captureRangeRepositions"]=captureRangeRepositions,
                ["strongRequestedForCapture"]=strongRequestedForCapture,
                ["captureSingleStrongPractice"]=captureSet!=null,
                ["savedSetRestoredBeforeResume"]=savedSet==null||enemy.AbilityController.AbilitySet==savedSet,
                ["strongAbility"]=strong.AbilityId,["strongNormalizedAtInput"]=originalAttackTime,["allSavedAbilitiesRetained"]=captureSet==null,["states"]=JArray.FromObject(states),
                ["actionLockPreserved"]=lockPreserved,["recoverFirstSeconds"]=recoverFirst,["expectedHoldSeconds"]=expectedHold,["expectedRecoverSeconds"]=expectedRecover,
                ["resumedAbility"]=resumedAbility,["poolReset"]=reset,["playerDamageAtParry"]=0,["videoRecorded"]=recorded,["videoPath"]=capture.VideoPath});
            enemy=null;melee.CancelCurrentAttackState();
        }
        finally
        {
            capture.Complete();melee.CancelCurrentAttackState();
            if(enemy!=null&&savedSet!=null)enemy.AbilityController.Configure(savedSet,1,1);
            if(enemy!=null)enemy.Health.SetDamageDeathPrevention(host,false);
            if(enemy!=null&&enemy.IsLeased&&service!=null)service.Release(enemy);
            if(trace.Count>0)File.WriteAllText(Path.Combine(output,"parry-trace.json"),trace.ToString());
            if(captureSet!=null)UnityEngine.Object.DestroyImmediate(captureSet);
        }
    }
}
