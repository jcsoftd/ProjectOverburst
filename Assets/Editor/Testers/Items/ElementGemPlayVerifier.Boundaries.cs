using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;
using UnityEditor;
using Overburst.Persistence;

public static partial class ElementGemPlayVerifier
{
    static void FreshRealAccount(bool reboot)
    {
        var registry=Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var session=AccountGameplaySession.Current;
        var state=session.Read(); AccountInvariants.Validate(state,registry);
        Check(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY")),"real account environment");
        Check(Path.GetFullPath(AccountBootstrap.SaveDirectory)==Path.GetFullPath(Path.Combine(Application.persistentDataPath,"Account")),"actual account directory");
        Check(state.level==1&&state.experience==0&&state.schemaVersion==2&&state.gear.Count==6,"fresh actual progression/schema/gear");
        Check(state.elementalGemInstanceId==null&&state.items.Count==1&&state.items[0].contentId==NewAccountFactory.StarterWeaponId,"explicit neutral starter only; gem grant deferred");
        var equipment=PlayerContext.Instance.CurrentActorEquipment;
        Check(equipment.ActiveElement==WeaponElement.None&&equipment.EquippedElementGem==null,"normal Play neutral equipment");
        Check(!PlayerPrefs.HasKey("Overburst.PlayerLevel.v1")&&!PlayerPrefs.HasKey("Overburst.PlayerExperience.v1"),"retired progression prefs not reimported");
        session.FlushPendingSave(); var disk=new EasySaveAccountStore(AccountBootstrap.SaveDirectory).Load();
        Check(JsonConvert.SerializeObject(disk)==JsonConvert.SerializeObject(state),"actual account persisted without quality reroll");
        string baseline=Path.Combine(SessionState.GetString(Key,""),"GOAL07_ActualBaseline.json");
        if(reboot) Check(File.ReadAllText(baseline)==JsonConvert.SerializeObject(state),"actual account reboot retains exact identity/state");
        else File.WriteAllText(baseline,JsonConvert.SerializeObject(state));
    }
    static object Field(object owner,string name) => owner.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(owner);
    static void Step(MonoBehaviour component) => component.GetType().GetMethod("Update",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(component,null);
    static void Boundaries()
    {
        var session=AccountGameplaySession.Current; var inventory=PlayerAccountInventoryService.SharedInventory;
        var equipment=PlayerContext.Instance.CurrentActorEquipment; var energy=equipment.GetComponent<OverburstElementEnergy>() ?? equipment.gameObject.AddComponent<OverburstElementEnergy>();
        var registry=Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var transactions=(AccountTransactions)Field(session,"transactions"); var store=(EasySaveAccountStore)Field(transactions,"store");
        var gem=EquipGem(WeaponElement.Fire,ElementGemArchetype.Weak); string quality=JsonConvert.SerializeObject(gem.gemState);
        var snap=new ElementGemAttackSnapshot(equipment); energy.RecordConfirmedHit(snap.WeaponId,snap.Element,900001,100);
        float charge=energy.Amount; int revision=equipment.GemRevision;
        energy.BindWeapon(snap.WeaponId,snap.Element,snap.GemId,snap.GemRevision);
        Check(energy.Amount==charge&&equipment.GemRevision==revision,"same gem bind preserves action and energy");
        var gold=MerchantTradeItemUtility.CreateGoldItem(7);
        var currency=WorldItemDropFactory.CreateCurrencyWorldPickupFromExistingItem(gold,new Vector3(7000,0,7000),inventory);
        Check(currency.TryPickup(inventory)&&snap.IsCurrent&&energy.Amount==charge&&JsonConvert.SerializeObject(equipment.EquippedElementGem.gemState)==quality,"actual currency acquisition fast path keeps gem context and quality");
        var weapon=equipment.CurrentWeaponItem; string wid=weapon.runtimeInstanceId;
        Check(session.ExecuteState("gem-no-weapon-"+Guid.NewGuid().ToString("N"),s=>{s.weapons[0]=null;s.inventory[s.inventory.FindIndex(x=>x==null)]=wid;}),"remove equipped weapon through account projection");
        Check(equipment.ActiveElement==WeaponElement.None&&equipment.EquippedElementGem.runtimeInstanceId==gem.runtimeInstanceId&&energy.Amount==0&&new ElementGemAttackSnapshot(equipment).IsCurrent,"no weapon keeps gem stats and disables element");
        Check(session.ExecuteState("gem-weapon-return-"+Guid.NewGuid().ToString("N"),s=>{s.inventory[s.inventory.IndexOf(wid)]=null;s.weapons[0]=wid;}),"restore equipped weapon");
        var current=new ElementGemAttackSnapshot(equipment); energy.RecordConfirmedHit(current.WeaponId,current.Element,900002,100);charge=energy.Amount;
        Check(!energy.TryCommitDischarge(100,out _,snap)&&energy.Amount==charge,"stale attack cannot consume new context energy");
        session.FlushPendingSave();
        var common=registry.Resolve<ElementGemItemData>("item.elementgem.fire.common");
        var normal=new ItemData(common,11,common.fixedGrade);var pickup=WorldItemDropFactory.CreateWorldPickup(normal,new Vector3(7000,0,7000),inventory,equipment.transform);
        store.FaultInjector=p=>{if(p=="before-write")throw new IOException("ElementGemVerifier expected deferred flush fault");};
        try
        {
            Check(pickup.TryPickup(inventory)&&session.Read().items.Any(x=>x.instanceId==normal.runtimeInstanceId),"normal world pickup commits memory before delayed write");
            bool rejected=false;try{session.FlushPendingSave();}catch(IOException){rejected=true;}
            Check(rejected&&session.HasPendingSave&&inventory.Items.Any(x=>x?.runtimeInstanceId==normal.runtimeInstanceId),"failed periodic flush preserves acquired gem and dirty state");
        }
        finally {store.FaultInjector=null;session.FlushPendingSave();if(pickup!=null)UnityEngine.Object.DestroyImmediate(pickup.gameObject);}
        var legendary=registry.Resolve<ElementGemItemData>("item.elementgem.ice.legendary");
        var high=new ItemData(legendary,25,legendary.fixedGrade);pickup=WorldItemDropFactory.CreateWorldPickup(high,new Vector3(7000,0,7000),inventory,equipment.transform);
        store.FaultInjector=p=>{if(p=="before-write")throw new IOException("ElementGemVerifier expected checkpoint fault");};
        try {Check(!pickup.TryPickup(inventory)&&pickup!=null&&!session.Read().items.Any(x=>x.instanceId==high.runtimeInstanceId),"checkpoint failure leaves world gem and does not publish ownership");}
        finally {store.FaultInjector=null;}
        store.FaultInjector=p=>{if(p=="after-write")throw new IOException("ElementGemVerifier expected uncertain write");};
        try {Check(pickup.TryPickup(inventory)&&session.Read().items.Count(x=>x.instanceId==high.runtimeInstanceId)==1&&session.PersistedRevision==session.Revision,"uncertain checkpoint readback publishes exactly once");}
        finally {store.FaultInjector=null;if(pickup!=null)UnityEngine.Object.DestroyImmediate(pickup.gameObject);}
        long before=session.Revision; int notifications=0;
        Action<AccountSnapshot> listener=s=>{notifications++;throw new InvalidOperationException("ElementGemVerifier expected listener fault");};
        transactions.Committed+=listener;
        try {Check(inventory.AddItem(new ItemData(common,1,common.fixedGrade))&&session.Revision==before+1&&notifications==1,"notification exception cannot repeat or rollback authority");}
        finally {transactions.Committed-=listener;}
        int owned=session.Read().items.Count;
        Check(session.ExecuteState("gem-overflow-"+Guid.NewGuid().ToString("N"),s=>s.unlockedSlots=s.baseUnlockedSlots=1),"fixture bag-loss overflow");
        Check(inventory.IsOverCapacity&&session.Read().items.Count==owned,"overflow preserves existing owned items; unlocked="+inventory.UnlockedSlotCount+" base="+session.Read().baseUnlockedSlots+" owned="+session.Read().items.Count+" before="+owned+" projection="+session.ProjectionError);
        var overflow=new ItemData(common,1,common.fixedGrade);pickup=WorldItemDropFactory.CreateWorldPickup(overflow,new Vector3(7000,0,7000),inventory,equipment.transform);
        try {Check(!pickup.TryPickup(inventory)&&session.Read().items.Count==owned,"overflow rejects new world gem without ownership or payment");}
        finally {if(pickup!=null)UnityEngine.Object.DestroyImmediate(pickup.gameObject);}
        Check(inventory.ConsumeItem(inventory.Items.First(x=>x?.runtimeInstanceId==normal.runtimeInstanceId),1),"existing overflow item can be cleared");
        Check(session.ExecuteState("gem-capacity-return-"+Guid.NewGuid().ToString("N"),s=>s.unlockedSlots=s.baseUnlockedSlots=s.inventoryCapacity),"return fixture capacity");
        var life=new ElementGemAttackSnapshot(equipment);equipment.GetComponent<CombatHealth>().ResetHealth();
        Check(!life.IsCurrent,"source life reset cannot revive an old attack");
        // Use existing derived damage and target registry, retaining the shared two-hit and seven-hop budgets.
        var fixtures=new List<GameObject>();
        try
        {
            for(int i=0;i<9;i++)
            {var go=new GameObject("ElementGemDerivedFixture");go.transform.position=new Vector3(8000+i*.3f,0,8000);go.AddComponent<CombatHealth>().SetMaxHp(100000,true);go.AddComponent<CombatAffiliation>().Configure(CombatTeam.Enemy);go.AddComponent<CombatTarget>();go.AddComponent<ElementalStatusController>();fixtures.Add(go);}
            foreach(var element in new[]{WeaponElement.Fire,WeaponElement.Electric})
            {
                EquipGem(element,ElementGemArchetype.Heavy);var attack=new ElementGemAttackSnapshot(equipment);
                foreach(var go in fixtures)
                {go.GetComponent<CombatHealth>().ResetHealth();var status=go.GetComponent<ElementalStatusController>();status.ClearAllStatuses();for(int j=0;j<5;j++)status.TryApplyDirectHit(new ElementalStatusApplication(element,100,equipment.gameObject,attack.WeaponId,true,false,go.transform.position,Vector3.forward,attack));}
                var batch=new ElementDischargeBatch();batch.Capture(equipment.GetComponent<CombatTarget>(),element,3,attack);batch.ConfirmInitial(fixtures[0].GetComponent<CombatHealth>());
                energy.Clear();float hp=fixtures[1].GetComponent<CombatHealth>().CurrentHp;
                if(element==WeaponElement.Fire)batch.ExecuteFire(100,equipment.gameObject,null);else batch.ExecuteLightning(100,1,equipment.gameObject,null);
                Check(batch.SecondaryHits>0&&batch.SecondaryHits<=18&&energy.Amount==0&&fixtures[1].GetComponent<CombatHealth>().CurrentHp<hp,"actual derived chain damage with bounded hits and no recharge "+element);
                Check(fixtures.All(x=>x.GetComponent<CombatHealth>().CurrentHp>=100000-1000),"derived damage remains within single common-bonus bounds "+element);
            }
            EquipGem(WeaponElement.Dark,ElementGemArchetype.Heavy,"gem.dark.corrosion_max_stacks");var dark=new ElementGemAttackSnapshot(equipment);
            var donor=fixtures[0].GetComponent<ElementalStatusController>();donor.ClearAllStatuses();
            for(int j=0;j<7;j++)donor.TryApplyDirectHit(new ElementalStatusApplication(WeaponElement.Dark,100,equipment.gameObject,dark.WeaponId,true,false,fixtures[0].transform.position,Vector3.forward,dark));
            energy.RecordConfirmedHit(dark.WeaponId,dark.Element,900004,100);Check(energy.TryCommitDischarge(100,out var barrage,dark),"dark barrage committed snapshot");
            var camera=Camera.main;Check(camera!=null,"actual barrage camera");var position=camera.transform.position;var rotation=camera.transform.rotation;
            try
            {
                camera.transform.position=new Vector3(8002,4,7990);camera.transform.LookAt(new Vector3(8002,1,8000));
                int id=DarkBarrageScheduler.PrepareSlam(barrage,equipment.gameObject,CombatTeam.PlayerParty,fixtures[0].transform.position,3,3,default);
                int stacks=donor.GetStackCount(WeaponElement.Dark);
                DarkBarrageScheduler.ConfirmSlamHit(id,donor.GetInstanceID(),stacks,donor,donor.LifecycleVersion,false);
                DarkBarrageScheduler.ConfirmSlamHit(id,donor.GetInstanceID(),stacks,donor,donor.LifecycleVersion,false);
                DarkBarrageScheduler.CompleteSlam(id);
                Check(stacks>5&&DarkBarrageScheduler.LastStackSum==stacks&&DarkBarrageScheduler.LastShotCount==stacks&&DarkBarrageScheduler.LastDonorCount==1,"extended corrosion creates bounded ammo once per donor");
                Check(Mathf.Abs(DarkBarrageScheduler.LastShotDamage-barrage.FirstBlastDamage*OverburstElementTuning.Current.SafeDarkBarrageShotDamage*(1+FlaskCombatModifiers.Bonus(equipment.gameObject,FlaskEffect.DarkBurstDamage)+dark.Modifiers.ProjectileDamage/100f))<.02f,"dark projectile bonus shares raw heavy base");
            }
            finally {camera.transform.SetPositionAndRotation(position,rotation);}
            EquipGem(WeaponElement.Light,ElementGemArchetype.Weak);var light=new ElementGemAttackSnapshot(equipment);
            Step(UnityEngine.Object.FindFirstObjectByType<DarkBarrageScheduler>());
            Check(DarkBarrageScheduler.ActiveCount==0,"gem switch retires stale dark barrage");
            LightTripleImpactScheduler.Submit(equipment.gameObject,CombatTeam.PlayerParty,fixtures[0].transform.position,2,100,2,3,2,100,light);
            Check(LightTripleImpactScheduler.PendingCount>0,"light delayed queue exists");
            ElementGemEquipmentService.UnequipToInventory();Step(UnityEngine.Object.FindFirstObjectByType<LightTripleImpactScheduler>());
            Check(LightTripleImpactScheduler.PendingCount==0,"gem removal drains stale delayed light damage and sparkle");
        }
        finally {foreach(var go in fixtures)UnityEngine.Object.DestroyImmediate(go);LightTripleImpactScheduler.ClearAll();DarkBarrageScheduler.ClearAll();}
        Check(MeleeElementSfxService.ResolveEnergyVolume(0)==0&&MeleeElementSfxService.ResolveEnergyVolume(1)==0&&Mathf.Abs(MeleeElementSfxService.ResolveEnergyVolume(30)-29f/59f)<.0001f&&MeleeElementSfxService.ResolveEnergyVolume(60)==1,"existing element SFX 0/1/30/60 scaling");
        AccountInvariants.Validate(session.Read(),registry);
    }
}
