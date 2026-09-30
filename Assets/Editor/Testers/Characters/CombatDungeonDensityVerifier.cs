using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using Object=UnityEngine.Object;

public static partial class CombatBalanceGoal3Verifier
{
    static IEnumerator VerifyDungeonDensity()
    {
        while(PersistentSceneFlow.Instance==null||PersistentSceneFlow.Instance.IsSwitching||!WorldSessionState.IsHideout)yield return null;
        Check(AccountBootstrap.SaveDirectory.StartsWith(Output,StringComparison.OrdinalIgnoreCase),"isolated account");
        deadline=EditorApplication.timeSinceStartup+1000;
        var actor=PlayerContext.Instance.CurrentActor;var player=PlayerInputFacade.Current;
        Check(actor.Equipment.EquipWeaponItem(new ItemData(AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset"),1,ItemGrade.Common)),"fixture weapon");
        var ui=Object.FindFirstObjectByType<DebugPanelToggleUI>(FindObjectsInactive.Include);
        ui.GetType().GetMethod("SetExpanded",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(ui,new object[]{true});
        var debug=Object.FindFirstObjectByType<DungeonDebugEntry>();
        var entryUi=Object.FindFirstObjectByType<DungeonDebugEntryUI>();
        Check(debug!=null&&entryUi!=null,"debug button attached");
        entryUi.Open();for(int i=0;i<5;i++)yield return null;
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Output,"debug-ui.png"));yield return null;entryUi.Close();yield return null;
        Check(!debug.TryEnter(0,true)&&!debug.TryEnter(101,true),"range rejected");
        for(int level=1;level<=100;level++)
        {
            var items=DungeonDebugEntry.PrepareItems(level);
            Check(items.Count==8&&items.All(x=>x.level==level),"loadout level "+level);
            Check(items.Count(x=>x.baseData is GearItemData g&&g.kind==GearKind.Earring)==2,"two earrings");
            var snapshot=ItemSnapshotCodec.Capture(items[0],Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath));
            Check(ItemSnapshotCodec.Restore(snapshot,Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath)).baseData==items[0].baseData,"weapon ID restore");
            for(int grade=0;grade<=6;grade++)
            {
                var map=new MapInstanceState{level=level,grade=(ItemGrade)grade};
                Check(MapSpawnPolicy.WaveSize(map)>=30&&MapSpawnPolicy.AliveLimit(map)<=149,"spawn bounds");
            }
        }
        var registry=Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var definitions=registry.Entries.Where(e=>e.asset is BaseItemData).Select(e=>(BaseItemData)e.asset)
            .Where(d=>!(d is CurrencyItemData)&&WeaponContentPolicy.IsAllowedItemData(d)).GroupBy(d=>d.GetType()).Select(g=>g.First()).ToArray();
        int effectChecks=0;
        foreach(var definition in definitions)
            for(int grade=0;grade<8;grade++)
            {
                var item=new ItemData(definition,1,ItemGrade.Common);item.grade=(ItemGrade)grade;
                var pickup=WorldItemDropFactory.CreateWorldPickup(item,player.transform.position+Vector3.forward*5,PlayerAccountInventoryService.SharedInventory,player.transform,null);
                Check(pickup!=null&&pickup.GradeEffect!=null,"mandatory grade effect "+definition.GetType()+grade);
                var effect=pickup.GradeEffect;
                if(grade==7){var line=effect.GetComponent<LineRenderer>();Check(line!=null&&line.startColor==GradeConfig.GetGradeColor((ItemGrade)grade),"unmapped grade color fallback");}
                effect.SetActive(false);yield return null;yield return null;Check(effect.activeSelf,"disabled effect repaired");
                pickup.gameObject.SetActive(false);Check(!effect.activeSelf,"effect hidden with pickup");
                pickup.gameObject.SetActive(true);Check(effect.activeSelf&&pickup.GradeEffect==effect,"effect reused on enable");
                Object.Destroy(effect);yield return null;yield return null;
                Check(pickup.GradeEffect!=null,"effect restored if removed");
                effect=pickup.GradeEffect;Object.Destroy(pickup.gameObject);yield return null;
                Check(effect==null||!effect.activeSelf,"effect cleanup");effectChecks++;
            }
        var reuse=WorldItemDropFactory.CreateWorldPickup(new ItemData(definitions.OfType<FlaskItemData>().First(),1,ItemGrade.Common),player.transform.position+Vector3.forward*5,PlayerAccountInventoryService.SharedInventory,player.transform,null);
        var oldEffect=reuse.GradeEffect;
        reuse.Initialize(new ItemData(definitions.OfType<WeaponItemData>().First(),1,ItemGrade.Common),PlayerAccountInventoryService.SharedInventory,player.transform,null);
        Check(reuse.GradeEffect!=oldEffect,"same grade different definition rebuilds VFX scale");
        reuse.Initialize(null,PlayerAccountInventoryService.SharedInventory,player.transform,null);Check(reuse.GradeEffect==null&&!oldEffect.activeSelf,"invalid pickup cleanup");Object.Destroy(reuse.gameObject);
        results.Add(new{mandatoryLootEffects=effectChecks,itemTypes=definitions.Select(d=>d.GetType().Name).ToArray()});
        for(int i=0;i<200;i++)Check(MapThemeCatalog.RollThemeId()!="DeathHarvest","disabled roll");
        Check(MapThemeCatalog.ResolveForRun("DeathHarvest")!=null&&MapThemeCatalog.ResolveForRun("DeathHarvest").ThemeId!="DeathHarvest","old map fallback");
        results.Add(new{levels=100,loadoutItems=800,policyCombinations=700,disabledTheme=true});
        BalanceInput input=new BalanceInput(player);
        try
        {
            for(int entryIndex=0;entryIndex<3;entryIndex++)
            {
                int level=entryIndex==1?100:1;
                bool drops=entryIndex<2;
                Check(actor.Equipment.EquipWeaponItem(new ItemData(AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset"),1,ItemGrade.Common)),"fixture weapon per entry");
                float started=Time.realtimeSinceStartup;
                entryUi.Open();
                var panel=(GameObject)typeof(DungeonDebugEntryUI).GetField("panel",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(entryUi);
                panel.GetComponentInChildren<TMPro.TMP_InputField>().text=level.ToString();
                panel.GetComponentInChildren<UnityEngine.UI.Toggle>().isOn=drops;
                panel.GetComponentsInChildren<UnityEngine.UI.Button>().First(b=>b.name=="입장").onClick.Invoke();
                Check(debug.IsEntering,"debug UI entry: "+debug.Status);
                Check(!debug.TryEnter(level,drops),"duplicate entry blocked");
                while(PersistentSceneFlow.Instance.IsSwitching||debug.IsEntering)yield return null;
                Check(WorldSessionState.Phase==WorldPhase.Run,debug.Status);
                float loadSeconds=Time.realtimeSinceStartup-started;
                var world=Object.FindFirstObjectByType<DiamondDungeonWorld>();var budget=world.SpawnBudget;
                var service=budget.GetComponent<EnemySpawnService>();
                actor.Health.SetMaxHp(1000000,true);
                foreach(var f in world.Fields)f.enabled=false;
                foreach(var e in world.EventDirector.Events)e.enabled=false;
                var pickups=new List<WorldItemPickup>();WorldItemPickup.CopyActivePickups(pickups);
                string runId=AccountGameplaySession.Current.ReadRun().runId;
                var loadout=pickups.Where(p=>p.RuntimeItem.originRunId==runId).ToArray();
                Check(loadout.Length==(drops?8:0),"drop toggle "+loadout.Length+" / "+debug.Status);
                Check(loadout.All(p=>p.GradeEffect!=null),"debug drops all grade VFX");
                Check(loadout.All(p=>p.RuntimeItem.level==level&&(p.transform.position-player.transform.position).magnitude<8),"nearby exact level");
                if(!drops)
                {
                    results.Add(new{uncheckedEntry=true,level,drops=loadout.Length,loadSeconds});
                    PersistentSceneFlow.Instance.GetComponent<RunLifetimeDriver>().RequestAbandon();
                    while(PersistentSceneFlow.Instance.IsSwitching||!WorldSessionState.IsHideout)yield return null;
                    continue;
                }
                float settle=Time.unscaledTime+2;while(Time.unscaledTime<settle)yield return null;
                var core=player.GetComponent<PlayerPickupInteractor>();var melee=player.GetComponent<MeleeRuntime>();
                PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
                // A real nameplate pointer event in both combat modes (including a temporary hover label).
                foreach(var mode in new[]{WorldLootInteractionMode.CombatAutoLegendary,WorldLootInteractionMode.CombatForcedHidden})
                {
                    var drop=loadout.First(p=>p!=null&&p.CanPickup&&p.RuntimeItem.baseData is GearItemData);
                    typeof(PlayerPickupInteractor).GetField("sessionMode",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,mode);
                    WarpPlayer(player,drop.transform.position+Vector3.back*.7f,Vector3.forward);
                    var renderer=drop.GetComponentInChildren<Renderer>();input.Aim=renderer!=null?renderer.bounds.center:drop.transform.position;
                    for(int i=0;i<6;i++)yield return null;
                    var field=typeof(WorldItemNameplateRowView).GetField("pickup",BindingFlags.Instance|BindingFlags.NonPublic);
                    var row=Object.FindObjectsByType<WorldItemNameplateRowView>(FindObjectsSortMode.None).FirstOrDefault(r=>field.GetValue(r)==drop);
                    Check(row!=null,"hover label visible: "+mode);
                    Check(row.Background.raycastTarget,"combat label raycast");
                    string id=drop.RuntimeItem.runtimeInstanceId;
                    var data=new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left};
                    ExecuteEvents.Execute(row.gameObject,data,ExecuteEvents.pointerEnterHandler);
                    ExecuteEvents.Execute(row.gameObject,data,ExecuteEvents.pointerDownHandler);input.Weak=true;
                    for(int i=0;i<3;i++)yield return null;
                    Check(PlayerAccountInventoryService.SharedInventory.Items.Any(x=>x!=null&&x.runtimeInstanceId==id),"combat click acquired");
                    Check(!melee.IsAttackInProgress,"click leaked weak attack");
                    input.Weak=false;ExecuteEvents.Execute(row.gameObject,data,ExecuteEvents.pointerUpHandler);core.NotifyPrimaryPointerReleased();yield return null;
                    results.Add(new{combatClick=mode.ToString(),acquired=true,noAttackLeak=true});
                }
                var distant=loadout.First(p=>p!=null&&p.CanPickup&&p.RuntimeItem.baseData is GearItemData);
                WarpPlayer(player,distant.transform.position+Vector3.back*5,Vector3.forward);yield return null;
                Check(core.RequestPickupByLabelPointerDown(distant)==WorldLootPickupRequestResult.AutoMovePending,"combat distant click begins auto move");
                core.NotifyPrimaryPointerReleased();float moveUntil=Time.unscaledTime+10;
                while(distant!=null&&Time.unscaledTime<moveUntil)yield return null;
                Check(distant==null,"combat distant pickup completed");
                results.Add(new{combatAutoMove=true});
                var weaponDrop=loadout.First(p=>p!=null&&p.RuntimeItem.baseData is WeaponItemData);
                var weaponItem=weaponDrop.RuntimeItem;
                WarpPlayer(player,weaponDrop.transform.position+Vector3.back*.6f,Vector3.forward);
                input.Aim=weaponDrop.transform.position;
                for(int i=0;i<3;i++)yield return null;
                // Heavy attack rejects both item inputs; the rejected F press must not be deferred.
                PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
                melee.SetManualInputEnabled(true);float ready=Time.unscaledTime+5;WeaponActionResult accepted;
                while((accepted=melee.TryStartHeavyAttack(Vector3.forward))!=WeaponActionResult.Accepted){Check(Time.unscaledTime<ready,"F heavy setup "+accepted);yield return null;}
                Check(core.RequestPickupByLabelPointerDown(weaponDrop)==WorldLootPickupRequestResult.ActionBusy,"label blocked during heavy");
                Check(!core.SuppressPrimaryAttackUntilRelease,"blocked label did not suppress combat");
                input.Interact=true;for(int i=0;i<3;i++)yield return null;
                Check(weaponDrop!=null,"F blocked during heavy");
                melee.CancelCurrentAttackState();for(int i=0;i<3;i++)yield return null;
                Check(weaponDrop!=null,"blocked F was not deferred");
                input.Interact=false;yield return null;input.Interact=true;float fLimit=Time.unscaledTime+2;
                while(weaponDrop!=null&&Time.unscaledTime<fLimit)yield return null;
                input.Interact=false;Check(weaponDrop==null,"F while combat idle");
                Check(actor.Equipment.EquipWeaponItem(weaponItem),"unmodeled catalog weapon equip");
                Check(actor.Equipment.CurrentWeaponItem==weaponItem,"equipped same item");
                foreach(var drop in loadout)if(drop!=null)Object.Destroy(drop.gameObject);
                yield return null;
                var untouched=world.Fields.Where(f=>!f.HasTriggered).Take(2).ToArray();
                Check(untouched.Length==2,"two untouched fields");
                var first=untouched[0];first.enabled=true;
                var second=untouched[1];second.enabled=true;
                WarpPlayer(player,first.transform.position,Vector3.forward);
                int createdBefore=service.Pool.CreatedCount;
                Check(first.SpawnOnce(player.transform),"first field queued");Check(second.SpawnOnce(player.transform),"second field queued");
                var samples=new List<double>();var frames=new List<float>();int maxSpawns=0,peak=0;
                float until=Time.unscaledTime+8;
                while(Time.unscaledTime<until)
                {
                    samples.Add(budget.LastSpawnMilliseconds);frames.Add(Time.unscaledDeltaTime*1000);
                    maxSpawns=Math.Max(maxSpawns,budget.SpawnedThisFrame);peak=Math.Max(peak,budget.AliveCount);
                    Check(budget.AliveCount<=budget.AliveLimit,"population cap");yield return null;
                }
                Check(maxSpawns<=2&&peak==budget.AliveLimit,"density reached cap "+peak+"/"+budget.AliveLimit);
                Check(service.Pool.CreatedCount==createdBefore,"combat pool miss "+(service.Pool.CreatedCount-createdBefore));
                Check(budget.PendingCount>0,"overflow must remain pending");
                int before=first.SpawnedCount+second.SpawnedCount;
                var enemies=Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None).Where(e=>e.IsLeased&&!e.Health.IsDead).Take(10).ToArray();
                foreach(var e in enemies)e.Health.TakeDamage(new DamageInfo(e.Health.MaxHp*2,e.transform.position));
                float refill=Time.unscaledTime+10;
                while(first.SpawnedCount+second.SpawnedCount<before+10&&Time.unscaledTime<refill)yield return null;
                Check(first.SpawnedCount+second.SpawnedCount>=before+10,"pending refill after death");
                Check(service.Pool.CreatedCount==createdBefore,"refill pool miss "+(service.Pool.CreatedCount-createdBefore));
                samples.Sort();frames.Sort();
                results.Add(new{spawnTest=true,level,theme=world.Theme.ThemeId,wave=MapSpawnPolicy.WaveSize(AccountGameplaySession.Current.ReadRun().map),loadSeconds,peak,maxSpawns,createdDuringCombat=service.Pool.CreatedCount-createdBefore,spawnMaxMs=samples.Last(),spawnP95Ms=samples[(int)(samples.Count*.95)],frameP95Ms=frames[(int)(frames.Count*.95)],queuedRefill=true});
                if(level==1)
                {
                    first.enabled=false;second.enabled=false;
                    foreach(var e in Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None).Where(e=>e.IsLeased).ToArray())service.Release(e);
                    yield return null;
                    var hunt=world.EventDirector.Events.First(e=>e.Kind==MapEventKind.Hunt);hunt.enabled=true;
                    Check(hunt.Activate(player.transform),"hunt activation");
                    Check(hunt.Phase==MapEventPhase.Active&&hunt.IsSpawning&&hunt.LivingCount==0,"queued hunt not prematurely complete");
                    float eventUntil=Time.unscaledTime+30;int kills=0;
                    while(hunt.Phase==MapEventPhase.Active&&Time.unscaledTime<eventUntil)
                    {
                        foreach(var enemy in Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None).Where(e=>e.IsLeased&&!e.Health.IsDead).ToArray())
                        {enemy.Health.TakeDamage(new DamageInfo(enemy.Health.MaxHp*2,enemy.transform.position));kills++;}
                        Check(budget.AliveCount<=budget.AliveLimit,"event shared cap");yield return null;
                    }
                    Check(hunt.Phase==MapEventPhase.Complete&&kills==60,"two hunt waves finish "+kills);hunt.enabled=false;
                    var guard=world.EventDirector.Events.First(e=>e.Kind==MapEventKind.Guard);guard.enabled=true;
                    budget.enabled=false;Check(guard.Activate(player.transform),"guard queued");
                    float remaining=guard.GuardRemaining;for(int i=0;i<12;i++)yield return null;
                    Check(guard.GuardRemaining==remaining&&guard.Phase==MapEventPhase.Active,"guard waits for first spawn");
                    guard.enabled=false;Check(budget.PendingCount==0,"disabled event cancels queue");budget.enabled=true;
                    results.Add(new{huntWaves=2,huntKills=kills,guardWaitsForSpawn=true,eventCancellation=true});
                }
                PersistentSceneFlow.Instance.GetComponent<RunLifetimeDriver>().RequestAbandon();
                while(PersistentSceneFlow.Instance.IsSwitching||!WorldSessionState.IsHideout)yield return null;
                Check(!PlayerPickupInteractor.IsPrimaryAttackSuppressed,"suppression restored on return");
            }
        }
        finally{input.Dispose();}
    }
}
