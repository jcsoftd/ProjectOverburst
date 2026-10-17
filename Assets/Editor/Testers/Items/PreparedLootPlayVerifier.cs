using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using Object = UnityEngine.Object;

public static class PreparedLootPlayVerifier
{
    public static IEnumerator Verify(RunWorldGate gate, string output, Action<bool,string> check)
    {
        var reserve = gate.LootReserve;
        check(reserve != null, "Prepared loot belongs to the current map");
        var player = PlayerContext.Instance.CurrentActor;
        var health = player.GetComponent<CombatHealth>();
        var account = Overburst.Persistence.AccountGameplaySession.Current;
        int registered = WorldItemPickup.DiagnosticRegisteredCount;
        long revision = account.Revision;
        var owned = new List<WorldItemPickup>();
        bool rngPreserved = true;
        health.SetDamageDeathPrevention(reserve,true);
        try
        {
            double until = Time.realtimeSinceStartupAsDouble + 30;
            while (Enumerable.Range(0,3).Any(t=>reserve.ReadyCount((EnemyThemeTier)t)<18) && Time.realtimeSinceStartupAsDouble<until && reserve.Error==null)
            {
                string random=JsonUtility.ToJson(UnityEngine.Random.state);
                reserve.PrepareOne(); rngPreserved &= random==JsonUtility.ToJson(UnityEngine.Random.state);
                yield return null;
            }
            check(reserve.Error==null, "Item preparation has no exception: "+reserve.Error);
            check(rngPreserved, "Preparation does not consume combat or spawn RNG");
            check(WorldItemPickup.DiagnosticRegisteredCount==registered && account.Revision==revision,
                "Hidden preparation does not register pickups or award account items");
            check(Enumerable.Range(0,3).All(t=>reserve.ReadyCount((EnemyThemeTier)t)>=18 && reserve.ReadyCount((EnemyThemeTier)t)<=20),
                "Small, medium and elite each hold 18-20 actual prepared objects including eligible extras");
            check(reserve.GetComponentsInChildren<Camera>(true).Length==0, "Prepared loot never creates a runtime rendering camera");
            float rare=BagQuality.EquippedBonus(BagStat.RareGradeWeight);
            int before=reserve.ReadyCount(EnemyThemeTier.Small);
            check(!reserve.TryTake(EnemyThemeTier.Small,0,EnemyGradeType.Normal,rare+1,null,out _,out _)
                && reserve.ReadyCount(EnemyThemeTier.Small)==before, "Changed rarity bonus never consumes an old prepared item");
            var ids=new List<string>();
            for(int channel=0;channel<6;channel++)
            {
                int tier=channel%3;
                check(reserve.TryTake((EnemyThemeTier)tier,channel,tier==2?EnemyGradeType.Elite:EnemyGradeType.Normal,rare,null,out var pickup,out var extra),
                    "Prepared item available for tier "+tier);
                if(!pickup)continue;
                owned.Add(pickup);if(extra)Object.Destroy(extra.gameObject);
                var item=pickup.RuntimeItem;ids.Add(item.runtimeInstanceId);
                check(!pickup.gameObject.activeInHierarchy && !pickup.CanPickup && item.originRunId==gate.Context.RunId,
                    "Prepared item remains inactive, inaccessible and stamped for the correct run");
                var position=player.transform.position+new Vector3((channel%3-1)*1.2f,.3f,1.2f+(channel/3)*.8f);
                typeof(WorldItemDropFactory).GetMethod("ReleaseReserved",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)
                    .Invoke(null,new object[]{pickup,position,PlayerAccountInventoryService.SharedInventory,player.transform});
                check(ReferenceEquals(item,pickup.RuntimeItem), "Release reuses the same rolled identity and options");
            }
            for(int frame=0;frame<40;frame++)yield return null;
            foreach(var pickup in owned)
            {
                var shadow=pickup.GetComponent<WorldItemGroundShadow>();
                check(pickup.CanPickup && shadow && shadow.Surface.gameObject.activeInHierarchy,
                    "All six item channels land with their baked silhouettes");
                check(pickup.GetComponentsInChildren<Renderer>(true).All(r=>r.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.Off),
                    "Dropped item uses no realtime shadow-casting pass");
            }
            ScreenCapture.CaptureScreenshot(Path.Combine(output,"prepared-loot-play.png"));
            yield return null;
            foreach(var pickup in owned) check(pickup.TryPickup(PlayerAccountInventoryService.SharedInventory),"Prepared item can be acquired through the real account transaction");
            yield return null;
            check(owned.All(p=>!p),"Acquisition removes the item and its attached fake shadow");
            check(account.Revision>revision,"Prepared item pickup commits normal account changes");
            yield return VerifyExtraReserve(gate,check);
            File.WriteAllText(Path.Combine(output,"prepared-loot.json"),JsonConvert.SerializeObject(new {
                reserve.Hits,reserve.Misses,reserve.Prepared,reserve.BudgetOverruns,reserve.MaximumStepMilliseconds,
                ready=Enumerable.Range(0,3).Select(t=>reserve.ReadyCount((EnemyThemeTier)t)).ToArray(),rngPreserved,ids
            },Formatting.Indented));
        }
        finally {foreach(var pickup in owned)if(pickup)Object.Destroy(pickup.gameObject);health.SetDamageDeathPrevention(reserve,false);}
    }

    static IEnumerator VerifyExtraReserve(RunWorldGate gate,Action<bool,string> check)
    {
        var inventory=PlayerAccountInventoryService.SharedInventory;
        var previous=PlayerAccountInventoryService.EquippedBag;
        var bag=new ItemData(BagFarmingLoot.Definitions.First(x=>x.defaultGrade==ItemGrade.Mythic),21,ItemGrade.Mythic);
        bag.bagState.rows.Clear();
        foreach(var kind in new[]{BagStat.InventorySlots,BagStat.ExtraItemDrop,BagStat.ItemPickupDistance,BagStat.RareGradeWeight})
        {var row=new BagStatRoll{stat=kind};for(int n=0;n<(kind==BagStat.InventorySlots || kind==BagStat.ExtraItemDrop?6:4);n++)row.stars.Add(WeaponGradeStarType.Yellow);bag.bagState.rows.Add(row);}
        check(inventory.AddItem(bag),"Extra reserve fixture uses a valid owned bag");
        var owned=inventory.Items.First(x=>x!=null && x.runtimeInstanceId==bag.runtimeInstanceId);
        check(PlayerAccountInventoryService.TryEquipBagFromInventorySlot(inventory,inventory.Items.ToList().IndexOf(owned),owned,0),"Production equip enables the extra-drop bonus");
        WorldItemPickup primary=null,extra=null;
        try
        {
            var reserve=gate.LootReserve;
            float rare=BagQuality.EquippedBonus(BagStat.RareGradeWeight);
            check(!reserve.TryTake(EnemyThemeTier.Medium,0,EnemyGradeType.Normal,rare,null,out _,out _),"New equipped bonus rejects pre-bonus entries");
            double until=Time.realtimeSinceStartupAsDouble+30;
            while(Time.realtimeSinceStartupAsDouble<until)
            {
                reserve.PrepareOne();yield return null;
                if(reserve.TryTake(EnemyThemeTier.Medium,0,EnemyGradeType.Normal,rare,null,out primary,out extra))break;
            }
            check(primary && extra,"Reserve prepares a matching conditional extra after bonus change");
            if(primary && extra)
            {
                check(primary.RuntimeItem.baseData==extra.RuntimeItem.baseData && primary.RuntimeItem.level==extra.RuntimeItem.level
                    && primary.RuntimeItem.runtimeInstanceId!=extra.RuntimeItem.runtimeInstanceId,"Extra retains the parent weapon and level with a unique identity");
                check(primary.RuntimeItem.originRunId==gate.Context.RunId && extra.RuntimeItem.originRunId==gate.Context.RunId,
                    "Prepared parent and extra both retain run ownership");
            }
        }
        finally
        {
            if(primary)Object.Destroy(primary.gameObject);if(extra)Object.Destroy(extra.gameObject);
            if(previous!=null)
            {var original=inventory.Items.First(x=>x!=null && x.runtimeInstanceId==previous.runtimeInstanceId);check(PlayerAccountInventoryService.TryEquipBagFromInventorySlot(inventory,inventory.Items.ToList().IndexOf(original),original,0),"Previous bag restored after extra preparation");}
            else
            {int empty=Enumerable.Range(0,inventory.UnlockedSlotCount).First(i=>inventory.GetItemAt(i)==null);check(PlayerAccountInventoryService.TryUnequipBagToInventorySlot(inventory,empty,PlayerAccountInventoryService.EquippedBag,0),"Extra test bag unequipped before combat");}
        }
        // Let the production worker replace obsolete entries before the measured combat segment.
        double end=Time.realtimeSinceStartupAsDouble+30;
        while(Time.realtimeSinceStartupAsDouble<end)
        {
            bool preparing=gate.LootReserve.PrepareOne();yield return null;
            if(!preparing && gate.LootReserve.ReadyCount(EnemyThemeTier.Small)>=18
                && gate.LootReserve.ReadyCount(EnemyThemeTier.Medium)>=18 && gate.LootReserve.ReadyCount(EnemyThemeTier.Elite)>=18)break;
        }
    }
}
