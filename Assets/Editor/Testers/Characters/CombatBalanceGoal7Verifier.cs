using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Overburst.Persistence;
using Object=UnityEngine.Object;

public static partial class CombatBalanceGoal3Verifier
{
    static IEnumerator VerifyGoal7()
    {
        BalanceInput input=null;MeleeRuntime melee=null;
        try {
            while(PersistentSceneFlow.Instance==null||PersistentSceneFlow.Instance.IsSwitching||PersistentSceneFlow.Instance.CurrentSubSceneName!="HideoutScene")yield return null;
            Check(AccountBootstrap.SaveDirectory.StartsWith(Output,StringComparison.OrdinalIgnoreCase),"Isolated account");
            deadline=EditorApplication.timeSinceStartup+900;
            var player=PlayerInputFacade.Current;var actor=PlayerContext.GetOrCreate().CurrentActor;
            var account=AccountGameplaySession.Current;
            Check(account.ExecuteState("balance-level-one",s=>{s.level=1;s.experience=0;}),"Reset isolated progression to1");
            var weapon=AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
            Check(actor.Equipment.EquipWeaponItem(new ItemData(weapon,1,ItemGrade.Common,element:WeaponElement.Fire)),"Equip common greatsword");
            var gear=AssetDatabase.FindAssets("t:GearItemData",new[]{"Assets/ProjectOverburst/Resources/Items/Gear"}).Select(g=>AssetDatabase.LoadAssetAtPath<GearItemData>(AssetDatabase.GUIDToAssetPath(g))).ToArray();
            for(int slot=0;slot<6;slot++) {
                var g=gear.First(x=>GearItemData.Fits(x.kind,(GearSlot)slot)&&x.AppearsAtLevel(1));
                Check(actor.Equipment.EquipGearItemToSlot(new ItemData(g,1,ItemGrade.Common),slot,out _),"Equip gear "+slot);
            }
            var life=new ItemData(Resources.Load<FlaskItemData>("Items/Flasks/Flask_Life"),1,ItemGrade.Common);
            Check(AccountGameplaySession.AcquireWorldItem(PlayerAccountInventoryService.SharedInventory,life),"Acquire flask");
            var flask=player.GetComponent<PlayerFlaskController>();Check(flask.TryEquip(0,life,out string reason),"Equip flask "+reason);
            actor.Health.ResetHealth();var before=account.Read();float baseHp=actor.Health.MaxHp;
            Check(Object.FindFirstObjectByType<MapDungeonPortal>().EnterLevelOne(),"Enter level1 map");
            while(PersistentSceneFlow.Instance.IsSwitching||WorldSessionState.Phase!=WorldPhase.Run)yield return null;
            var world=Object.FindFirstObjectByType<DiamondDungeonWorld>();Check(world!=null,"Map world");
            string themeId=world.Theme.ThemeId;
            string runId=account.ReadRun().runId;
            PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);
            melee=player.GetComponent<MeleeRuntime>();melee.SetManualInputEnabled(true);input=new BalanceInput(player);
            var field=world.Fields.First(f=>f.Band==0);
            WarpPlayer(player,field.transform.position,Vector3.forward);
            if(!field.HasTriggered)Check(field.SpawnOnce(player.transform),"Field spawn");
            int hits=0,weakHits=0,heavyHits=0,potions=0,pickups=0;float damageTaken=0;float begin=Time.unscaledTime,nextTrace=begin;
            void PlayerHit(CombatHealth h,DamageInfo d){hits++;damageTaken+=d.damage;}
            actor.Health.OnDamaged+=PlayerHit;
            // Travel is automated; all enemy HP is removed by the production weapon/input path.
            foreach(int stage in new[]{0,1}) {
                var boss=GameObject.Find("CapsuleBoss_Temporary").GetComponent<CombatHealth>();
                var observed=new HashSet<CombatHealth>();
                void EnemyHit(CombatHealth h,DamageInfo d){if((d.playerAttackKind&PlayerAttackKind.Weak)!=0)weakHits++;else heavyHits++;}
                float limit=Time.unscaledTime+240;
                while(stage==0?field.RemainingCount>0:!boss.IsDead) {
                    if(Time.unscaledTime>=nextTrace||actor.Health.IsDead){nextTrace=Time.unscaledTime+5;results.Add(new{stage,elapsed=Time.unscaledTime-begin,hp=actor.Health.CurrentHp,maxHp=actor.Health.MaxHp,hits,weakHits,heavyHits,potions,living=field.RemainingCount,activeFields=world.Fields.Count(f=>f.HasTriggered),energy=player.GetComponent<OverburstElementEnergy>()?.Amount,gameplay=player.IsGameplayEnabled,blocked=GameplayInputBlocker.IsGameplayInputBlocked,melee.IsAttackInProgress,held=player.AttackHeld,can=melee.CanUseCurrentWeapon,mode=player.GetComponent<PlayerMovement>().IsMeleeCombatLocomotionMode});}
                    Check(Time.unscaledTime<limit,"Combat stage timeout "+stage);Check(!actor.Health.IsDead,"Player died in map");
                    CombatHealth target=stage==1?boss:Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None).Where(e=>e.IsLeased&&!e.Health.IsDead).OrderBy(e=>(e.transform.position-player.transform.position).sqrMagnitude).Select(e=>e.Health).FirstOrDefault();
                    if(target==null){yield return null;continue;}
                    if(observed.Add(target))target.OnDamaged+=EnemyHit;
                    Vector3 targetPosition=target.transform.position;Vector3 direction=targetPosition-player.transform.position;direction.y=0;
                    if(!melee.IsAttackInProgress&&direction.magnitude>2.7f){var destination=targetPosition-direction.normalized*1.5f;destination.y=player.transform.position.y;WarpPlayer(player,destination,direction.normalized);}
                    input.Aim=targetPosition;
                    var energy=player.GetComponent<OverburstElementEnergy>();bool heavy=energy!=null&&energy.Amount>=80;
                    bool click=Time.unscaledTime*6%1<.55f;input.Weak=!heavy&&click;input.Heavy=heavy&&click;
                    bool danger=Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None).Any(e=>e.IsLeased&&e.AbilityController.IsOrdinaryHitProtected&&(e.transform.position-player.transform.position).sqrMagnitude<36);
                    input.Roll=danger&&!melee.IsHeavyAttackInProgress&&click;
                    if(actor.Health.NormalizedHp<.7f&&flask.TryUse(0,out _))potions++;
                    var drops=new List<WorldItemPickup>();WorldItemPickup.CopyActivePickups(drops);
                    foreach(var drop in drops.Where(d=>d.RuntimeItem!=null&&d.RuntimeItem.originRunId==runId).ToArray())if(drop.TryPickup(PlayerAccountInventoryService.SharedInventory))pickups++;
                    yield return null;
                }
                foreach(var h in observed)if(h!=null)h.OnDamaged-=EnemyHit;
                input.Weak=input.Heavy=input.Roll=false;melee.CancelCurrentAttackState();yield return null;
            }
            float pickupUntil=Time.unscaledTime+1;
            while(Time.unscaledTime<pickupUntil){var drops=new List<WorldItemPickup>();WorldItemPickup.CopyActivePickups(drops);foreach(var drop in drops.Where(d=>d.RuntimeItem!=null&&d.RuntimeItem.originRunId==runId).ToArray())if(drop.TryPickup(PlayerAccountInventoryService.SharedInventory))pickups++;yield return null;}
            actor.Health.OnDamaged-=PlayerHit;
            PlayerProgression.Current.FlushPendingExperience();
            var done=account.Read();Check(done.run.phase==RunPhase.BossCleared,"Boss settlement");
            Check(done.level>before.level||done.experience>before.experience,"Kill XP granted");
            var exits=Object.FindObjectsByType<DungeonExitPortal>(FindObjectsSortMode.None);
            Check(exits.Length==1&&exits[0].TryInteract(actor)==InteractionExecutionResult.StartedTransition,"Extract portal");
            while(PersistentSceneFlow.Instance.IsSwitching||!WorldSessionState.IsHideout)yield return null;
            Check(account.ReadRun().phase==RunPhase.Extracted,"Run extracted");
            Check(account.FlushPendingSave(),"Flush isolated account");
            var saved=account.Read();var reread=new EasySaveAccountStore(AccountBootstrap.SaveDirectory).Load();
            Check(JsonUtility.ToJson(saved)==JsonUtility.ToJson(reread),"A/B fresh store read matches live account");
            var map=PlayerAccountInventoryService.SharedInventory.Items.FirstOrDefault(i=>i!=null&&i.baseData is MapItemData&&i.mapState!=null);
            Check(map!=null&&map.mapState.level>=2,"Next map reward");
            results.Add(new{scope="assisted travel, production input/collision, one band0 field then temporary boss",theme=themeId,baseHp,hits,damageTaken,weakHits,heavyHits,potions,pickups,elapsed=Time.unscaledTime-begin,level=saved.level,xp=saved.experience,nextMapLevel=map.mapState.level,saveReadback=true});
            var upgraded=PlayerAccountInventoryService.SharedInventory.Items.FirstOrDefault(i=>i!=null&&i.baseData is GearItemData);
            if(upgraded!=null){var g=(GearItemData)upgraded.baseData;int slot=Enumerable.Range(0,6).First(s=>GearItemData.Fits(g.kind,(GearSlot)s));var inventory=PlayerAccountInventoryService.SharedInventory;var old=actor.Equipment.GetGearSlotItem(slot);Check(GearEquipmentService.EquipFromInventorySlot(inventory.FindFirstMatchingItemIndex(upgraded),slot),"Loot gear equip");Check(GearEquipmentService.UnequipToInventory(slot),"Gear unequip");Check(GearEquipmentService.EquipFromInventorySlot(inventory.FindFirstMatchingItemIndex(old),slot),"Gear restore");results.Add(new{gearSwap=true});}
            Check(Object.FindFirstObjectByType<MapDungeonPortal>().EnterSelectedMap(map.runtimeInstanceId),"Next map entry");
            while(PersistentSceneFlow.Instance.IsSwitching||WorldSessionState.Phase!=WorldPhase.Run)yield return null;
            Check(account.ReadRun().map.level>=2,"Next map context");
            PersistentSceneFlow.Instance.GetComponent<RunLifetimeDriver>().RequestAbandon();
            while(PersistentSceneFlow.Instance.IsSwitching||!WorldSessionState.IsHideout)yield return null;
            results.Add(new{nextMapEntry=true,abandonReturned=true});
        } finally {input?.Dispose();melee?.CancelCurrentAttackState();}
    }
}
