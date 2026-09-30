using System;
using System.Collections;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Overburst.Persistence;
using Object=UnityEngine.Object;
public static partial class CombatBalanceGoal3Verifier
{
    static IEnumerator VerifyReentry()
    {
        while(PersistentSceneFlow.Instance==null||PersistentSceneFlow.Instance.IsSwitching||PersistentSceneFlow.Instance.CurrentSubSceneName!="HideoutScene")yield return null;
        Check(AccountBootstrap.SaveDirectory.StartsWith(Output,StringComparison.OrdinalIgnoreCase),"Isolated reentry");
        var account=AccountGameplaySession.Current;var actor=PlayerContext.GetOrCreate().CurrentActor;var p=PlayerInputFacade.Current;
        Check(account.Read().level==3&&PlayerProgression.CurrentLevel==3,"Saved level projected");
        Check(actor.Equipment.CurrentWeaponData!=null&&Enumerable.Range(0,7).All(i=>actor.Equipment.GetGearSlotItem(i)!=null),"Weapon and gear restored");
        var flask=p.GetComponent<PlayerFlaskController>();Check(flask.GetItem(0)!=null,"Flask restored");
        actor.Health.TakeDamage(new DamageInfo(actor.Health.MaxHp*.5f,p.transform.position));
        float hp=actor.Health.CurrentHp;Check(flask.TryUse(0,out string reason),"Use life flask "+reason);
        Check(actor.Health.CurrentHp>hp,"Flask heals");
        Check(!flask.TryUse(0,out _),"Duplicate flask use rejected");
        results.Add(new{freshPlayReentry=true,level=PlayerProgression.CurrentLevel,hpBeforeFlask=hp,hpAfterFlask=actor.Health.CurrentHp,weapon=actor.Equipment.CurrentWeaponData.name,flaskCooldown=flask.CooldownRemaining(0)});
        var ui=Object.FindFirstObjectByType<EnemyThemeDebugUI>(FindObjectsInactive.Include);ui.gameObject.SetActive(true);if(!ui.InArena)ui.ToggleArena();
        Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(p.transform,out var spawn),"Wall spawn service");
        foreach(var table in ui.tables)Check(spawn.RegisterAdditionalCatalog(table.Catalog,out string error),error);
        var defs=ui.tables.SelectMany(t=>t.Entries).Select(e=>e.definition).Distinct().ToArray();
        EnemyActor enemy=null;GameObject wall=null;EnemyAbilitySet fixture=null;BalanceInput input=null;
        try {
            var origin=p.transform.position;
            Check(spawn.TrySpawn(new EnemySpawnRequest(defs.First(d=>d.EnemyId.Contains("Larvae")),origin+Vector3.forward*4,Quaternion.identity,p.transform),out enemy),"Wall enemy");
            enemy.AI.enabled=false;enemy.Movement.StopMovement();yield return null;yield return null;
            float startZ=enemy.transform.position.z;
            var target=enemy.GetComponent<CombatTarget>();float radius=enemy.GetComponentsInChildren<Collider>().Where(c=>c.enabled&&!c.isTrigger).Max(c=>c.bounds.max.z)-startZ;
            wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.name="Balance temporary wall";wall.layer=0;
            wall.transform.position=new Vector3(enemy.transform.position.x,enemy.transform.position.y+1.5f,startZ+radius+.3f+.15f);wall.transform.localScale=new Vector3(5,3,.3f);Physics.SyncTransforms();
            enemy.GetComponent<EnemyMovementReaction>().ApplyKnockbackDistance(Vector3.forward,.5f);
            float until=Time.time+.4f;while(Time.time<until)yield return null;
            float wallTravel=enemy.transform.position.z-startZ;
            results.Add(new{wallTravel,requested=.5f,radius});Check(wallTravel<.4f,"Weak knockback crosses wall");
            spawn.Release(enemy);enemy=null;Object.Destroy(wall);wall=null;yield return null;
            input=new BalanceInput(p);PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
            var modes=defs.SelectMany(d=>Enumerable.Range(0,d.AbilitySet.Count).Select(i=>new{d,a=d.AbilitySet.GetAbility(i)})).Where(x=>x.a.IsTelegraphedStrongAttack).GroupBy(x=>x.a.ExecutionMode).Select(g=>g.First()).ToArray();
            foreach(var sample in modes){
                WarpPlayer(p,origin,Vector3.forward);var a=sample.a;
                Check(spawn.TrySpawn(new EnemySpawnRequest(sample.d,origin+Vector3.forward*Mathf.Lerp(a.MinimumRange,a.Range,.65f),Quaternion.LookRotation(Vector3.back),p.transform,context:new EncounterContext(null,100,ItemGrade.Common)),out enemy),"Dodge target");
                enemy.AI.enabled=false;enemy.Movement.StopMovement();
                fixture=ScriptableObject.CreateInstance<EnemyAbilitySet>();using(var so=new SerializedObject(fixture)){so.FindProperty("abilitySetId").stringValue="dodge-fixture";var arr=so.FindProperty("abilities");arr.arraySize=1;arr.GetArrayElementAtIndex(0).objectReferenceValue=a;so.ApplyModifiedPropertiesWithoutUndo();}
                float speed=enemy.Melee.AbilityAnimationSpeed;enemy.AbilityController.Configure(fixture,enemy.RuntimeStats.DamageMultiplier,speed);
                float ready=Time.time+3;while(!enemy.AbilityController.TryStart(p.transform)){Check(Time.time<ready,"Dodge start");yield return null;}
                float attackAt=Time.time;while(Time.time<attackAt+a.ResolveFirstImpactTime(speed)-.15f)yield return null;
                int damageEvents=0;void OnRollDamage(CombatHealth h,DamageInfo d){damageEvents++;}actor.Health.OnDamaged+=OnRollDamage;
                float beforeHp=actor.Health.CurrentHp;input.Aim=enemy.transform.position;input.Right=true;yield return null;input.Roll=true;var evade=p.GetComponent<PlayerEvadeController>();float untilRoll=Time.unscaledTime+1;
                while(!evade.IsEvading){Check(Time.unscaledTime<untilRoll,"Input roll");yield return null;}input.Roll=input.Right=false;
                float end=attackAt+a.ResolveExecutionDuration(speed)+.5f;while(Time.time<end)yield return null;
                actor.Health.OnDamaged-=OnRollDamage;
                results.Add(new{spatialRoll=a.ExecutionMode.ToString(),monsterLevel=100,hpBefore=beforeHp,hpAfter=actor.Health.CurrentHp,damageEvents});
                Check(damageEvents==0,"Spatial roll failed "+a.ExecutionMode);
                enemy.AbilityController.Cancel();spawn.Release(enemy);enemy=null;Object.Destroy(fixture);fixture=null;
                until=Time.unscaledTime+.5f;while(Time.unscaledTime<until)yield return null;
            }
        } finally {input?.Dispose();if(enemy!=null&&enemy.IsLeased)spawn.Release(enemy);if(wall!=null)Object.Destroy(wall);if(fixture!=null)Object.Destroy(fixture);if(ui.InArena)ui.ToggleArena();}
        Check(Object.FindFirstObjectByType<MapDungeonPortal>().EnterLevelOne(),"Death run entry");
        while(PersistentSceneFlow.Instance.IsSwitching||WorldSessionState.Phase!=WorldPhase.Run)yield return null;
        actor.Health.TakeDamage(new DamageInfo(1000000,p.transform.position));Check(actor.Health.IsDead,"Player death");
        while(PersistentSceneFlow.Instance.IsSwitching||!WorldSessionState.IsHideout)yield return null;
        Check(account.ReadRun().phase==RunPhase.Failed&&!actor.Health.IsDead,"Death settled and returned");
        Check(PlayerProgression.CurrentLevel==3,"Death preserves XP");
        Check(account.FlushPendingSave(),"Final isolated save");
        var read=new EasySaveAccountStore(AccountBootstrap.SaveDirectory).Load();Check(JsonUtility.ToJson(account.Read())==JsonUtility.ToJson(read),"Death readback");
        results.Add(new{deathReturn=true,progressionPreserved=true,finalReadback=true});
    }
}
