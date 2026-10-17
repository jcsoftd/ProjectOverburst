using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

public static class TesterSupportItemVerifier
{
    static readonly BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

    static ItemSnapshot Charm(AccountSnapshot state) => state.items.SingleOrDefault(x => x.contentId == TesterSupportItemData.ContentId && state.inventory.Contains(x.instanceId));
    static void RemoveCharm(AccountSnapshot state)
    {
        var item = Charm(state);
        state.inventory[state.inventory.IndexOf(item.instanceId)] = null;
        state.items.Remove(item);
    }

    public static string VerifyNative(string output)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new Exception("Idle Editor required");
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        Action<bool, string> require = (value, label) => { if (!value) throw new Exception(label); checks.Add(label); };
        var instanceField = typeof(PlayerAccountInventoryService).GetField("instance", PrivateStatic);
        var previousService = instanceField.GetValue(null);
        var sessionProperty = typeof(AccountGameplaySession).GetProperty("Current");
        var previousSession = sessionProperty.GetValue(null);
        var random = Random.state;
        var debug = CombatDebugSettings.RunLootOverride;
        var fixture = new GameObject("TesterSupportNative") { hideFlags = HideFlags.HideAndDontSave };
        string failure = null;
        try
        {
            // EditMode after Play can retain a detached session; this fixture owns only its local inventory.
            sessionProperty.SetValue(null, null);
            var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
            var data = registry.Resolve<TesterSupportItemData>(TesterSupportItemData.ContentId);
            require(data.icon != null && data.sellPrice == 0 && data.weight == 0, "saved item icon, zero weight and value");
            var state = NewAccountFactory.Create(registry);
            require(state.testerSupportGranted && Charm(state) != null, "new account grants one charm in inventory");
            NewAccountFactory.GrantTesterSupport(state, registry);
            require(state.items.Count(x => x.contentId == TesterSupportItemData.ContentId) == 1, "starter grant is idempotent");
            require(TesterSupportItemData.Multiplier(state) == 2, "saved inventory has multiplier two");
            var questState=ItemSnapshotCodec.CopyValues(state);
            var questXp=typeof(Overburst.Quests.AccountQuestCommands).GetMethod("GrantExperience",PrivateStatic);
            questXp.Invoke(null,new object[]{questState,11});
            require(questState.experience==11,"quest reward ignores tester support multiplier");
            var stashed=Charm(questState); questState.inventory[questState.inventory.IndexOf(stashed.instanceId)]=null;
            questState.stashTabs[0].slots[0]=stashed.instanceId;
            questXp.Invoke(null,new object[]{questState,11});
            require(questState.experience==22 && TesterSupportItemData.Multiplier(questState)==1,"quest reward remains unchanged after stashing the support item");
            var item = ItemSnapshotCodec.Restore(Charm(state), registry);
            string tooltip = SimpleItemTooltipBuilder.Build(item);
            require(tooltip.Contains("사냥 경험치 획득량") && tooltip.Contains("아이템 드롭확률") && tooltip.Contains("2배") && tooltip.Contains("즉시 효과가 해제"), "normal item tooltip explains hunting-only XP, drop bonus and removal");
            var store = new EasySaveAccountStore(Path.Combine(output, "LegacyAccount"));
            RemoveCharm(state); state.testerSupportGranted = false;
            store.Save(state, "old-character");
            state = MapAccountMigration.Load(new EasySaveAccountStore(Path.Combine(output, "LegacyAccount")), registry);
            require(state.testerSupportGranted && Charm(state) != null, "old character gets one durable grant");
            RemoveCharm(state);
            new EasySaveAccountStore(Path.Combine(output, "DiscardedAccount")).Save(state, "discarded");
            var reloaded = MapAccountMigration.Load(new EasySaveAccountStore(Path.Combine(output, "DiscardedAccount")), registry);
            require(Charm(reloaded) == null && reloaded.testerSupportGranted, "discarded charm does not return after disk reload");
            var other = NewAccountFactory.Create(registry);
            AccountCharacterCommands.EnsureLegacy(reloaded);
            string otherId = Guid.NewGuid().ToString("N");
            foreach (var saved in other.items) saved.acquisitionOrder = reloaded.nextAcquisitionOrder++;
            other.nextAcquisitionOrder = reloaded.nextAcquisitionOrder;
            reloaded.characters.Add(new AccountCharacterSnapshot { id=otherId, name="지원검사", personal=other });
            AccountCharacterCommands.Select(reloaded, otherId);
            require(reloaded.testerSupportGranted && Charm(reloaded) != null, "character switch restores own grant state");
            AccountCharacterCommands.Select(reloaded, AccountCharacterCommands.LegacyId);
            require(reloaded.testerSupportGranted && Charm(reloaded) == null, "switching back preserves discarded state");
            AccountInvariants.Validate(reloaded, registry);
            other = reloaded.characters.Single(x => x.id == otherId).personal;
            RemoveCharm(other); other.testerSupportGranted = false;
            new EasySaveAccountStore(Path.Combine(output,"DormantAccount")).Save(reloaded,"dormant");
            reloaded=MapAccountMigration.Load(new EasySaveAccountStore(Path.Combine(output,"DormantAccount")),registry);
            require(Charm(reloaded.characters.Single(x=>x.id==otherId).personal)!=null && Charm(reloaded)==null, "dormant old character grant preserves active discard and global order");
            var full=NewAccountFactory.Create(registry); RemoveCharm(full); full.testerSupportGranted=false;
            for(int i=0;i<full.unlockedSlots;i++)
            {
                var filler=new ItemData(data,1,ItemGrade.Common); filler.EnsureAcquisitionOrder();
                var saved=ItemSnapshotCodec.Capture(filler,registry); saved.acquisitionOrder=full.nextAcquisitionOrder++;
                full.items.Add(saved); full.inventory[i]=saved.instanceId;
            }
            NewAccountFactory.GrantTesterSupport(full,registry);
            require(!full.testerSupportGranted && full.inventory.Take(full.unlockedSlots).All(x=>!string.IsNullOrEmpty(x)), "full inventory defers grant without replacing any item");
            full.items.RemoveAll(x=>x.instanceId==full.inventory[0]); full.inventory[0]=null;
            NewAccountFactory.GrantTesterSupport(full,registry);
            require(full.testerSupportGranted && full.inventory[0]!=null, "deferred grant uses a newly freed slot");

            var inventory=fixture.AddComponent<PlayerInventory>();
            var service=fixture.AddComponent<PlayerAccountInventoryService>(); service.Bind(inventory,null,null);
            instanceField.SetValue(null,service);
            var progression=fixture.AddComponent<PlayerProgression>();
            require(TesterSupportItemData.CurrentMultiplier==1,"empty live inventory has no bonus");
            require(inventory.AddItem(item),"live inventory accepts charm");
            require(TesterSupportItemData.CurrentMultiplier==2,"live inventory activates bonus");
            var duplicate=new ItemData(data,1,ItemGrade.Common); require(inventory.AddItem(duplicate),"second charm can be held");
            require(TesterSupportItemData.CurrentMultiplier==2,"two charms do not stack");
            inventory.RemoveItem(duplicate);
            require(Mathf.Abs(TesterSupportItemData.ApplyDropChance(.2f*1.5f)-.6f)<.00001f,"drop bonus multiplies after other modifiers");
            require(TesterSupportItemData.ApplyDropChance(.8f)==1 && TesterSupportItemData.ApplyDropChance(0)==0,"probability cap and zero preserved");
            progression.AddExperience(11);
            require(progression.Experience==11,"non-kill experience ignores the support multiplier");
            progression.AddKillExperience(10,50,1.5f);
            require(progression.Experience==56,"kill experience final multiplier preserves fractional carry");
            inventory.RemoveItem(item); progression.AddExperience(11);
            require(progression.Experience==67 && TesterSupportItemData.CurrentMultiplier==1,"removal immediately restores experience and drop baseline");
            inventory.AddItem(item);
            progression.AddExperience(progression.ExperienceToNext-progression.Experience);
            require(progression.Level==2 && progression.Experience==0,"exact level command bypasses reward multiplier");
            inventory.RemoveItem(item);
            CombatDebugSettings.SetRunLootOverride(RunLootDebugOverride.Default);
            foreach(bool boosted in new[]{false,true})
            {
                if(boosted) require(inventory.AddItem(item),"reacquire activates support again");
                const int trials=6000; var counts=new int[5]; Random.InitState(1016);
                for(int i=0;i<trials;i++)
                {
                    if(WeaponLootPolicy.Roll((EnemyRank)null,25)!=null) counts[0]++;
                    if(GearLootPolicy.Roll((EnemyRank)null,25)!=null) counts[1]++;
                    if(ElementGemLootPolicy.Roll((EnemyRank)null,25)!=null) counts[2]++;
                    if(FlaskLootPolicy.Roll((EnemyRank)null,25)!=null) counts[3]++;
                    if(BagFarmingLoot.RollBag(null,25,ItemGrade.Common,0)!=null) counts[4]++;
                }
                double[] rates={.03,.06,.02,.02,.01};
                for(int kind=0;kind<5;kind++)
                {
                    double p=rates[kind]*(boosted?2:1);
                    require(Math.Abs(counts[kind]-trials*p)<6*Math.Sqrt(trials*p*(1-p))+3,"production source "+kind+" distribution ×"+(boosted?2:1));
                }
                CombatDebugSettings.SetRunLootOverride(RunLootDebugOverride.Never);
                require(WeaponLootPolicy.Roll((EnemyRank)null,25)==null && GearLootPolicy.Roll((EnemyRank)null,25)==null,"Never remains zero with support ×"+(boosted?2:1));
                CombatDebugSettings.SetRunLootOverride(RunLootDebugOverride.Default);
                foreach(var terrain in new[]{MapTerrainKind.Desert,MapTerrainKind.Cave})
                {
                    var map=new MapInstanceState {generationVersion=1,terrain=terrain,level=25,grade=ItemGrade.Rare,
                        options=new List<MapOptionRoll>{new MapOptionRoll{optionId=MapOptionPolicy.ExtraDrop,value=.5f}}};
                    const int mapTrials=10000;
                    for(int kind=0;kind<5;kind++)
                    {
                        int dropped=0; Random.InitState(10160+kind);
                        for(int i=0;i<mapTrials;i++) if(MapFarmingLoot.Roll((MapFarmingKind)kind,EnemyGradeType.Normal,map,0)!=null) dropped++;
                        double p=MapFarmingLoot.Chance((MapFarmingKind)kind,EnemyGradeType.Normal,map)*(boosted?2:1);
                        require(Math.Abs(dropped-mapTrials*p)<6*Math.Sqrt(mapTrials*p*(1-p))+3,"map "+terrain+" source "+kind+" final distribution ×"+(boosted?2:1));
                    }
                    var definition=Resources.Load<MapItemData>("Items/Maps/Map_Diamond01");
                    int maps=0;
                    for(int i=0;i<mapTrials;i++) if(MapDropPolicy.Roll(definition,registry,map,EnemyThemeTier.Elite,"tester-map")!=null) maps++;
                    double mapP=MapDropPolicy.ChanceForMap(EnemyThemeTier.Elite,map)*(boosted?2:1);
                    require(Math.Abs(maps-mapTrials*mapP)<6*Math.Sqrt(mapTrials*mapP*(1-mapP))+3,"natural map probability ×"+(boosted?2:1)+" "+terrain);
                }
                var entry=new DropTableEntry{itemData=data,dropChance=.2f};
                int tableDrops=0; const int tableTrials=6000;
                for(int i=0;i<tableTrials;i++) if(entry.ShouldDrop()) tableDrops++;
                double tableP=.2*(boosted?2:1);
                require(Math.Abs(tableDrops-tableTrials*tableP)<6*Math.Sqrt(tableTrials*tableP*(1-tableP))+3,"authored table probability ×"+(boosted?2:1));
            }
        }
        catch(Exception error){failure=error.ToString();}
        finally
        {
            Object.DestroyImmediate(fixture); instanceField.SetValue(null,previousService);
            sessionProperty.SetValue(null, previousSession);
            Random.state=random; CombatDebugSettings.SetRunLootOverride(debug);
            File.WriteAllText(Path.Combine(output,"Native.json"),JsonConvert.SerializeObject(new{status=failure==null?"PASS":"FAIL",checks,failure},Formatting.Indented));
        }
        return failure==null?"PASS: "+checks.Count+" native checks":"FAIL: "+failure;
    }

    public static void VerifyPlay(Action<bool,string> require)
    {
        var inventory=PlayerAccountInventoryService.SharedInventory;
        var session=AccountGameplaySession.Current;
        var progression=PlayerProgression.Current;
        var item=inventory.Items.Single(x=>x?.baseData is TesterSupportItemData);
        require(item!=null && TesterSupportItemData.CurrentMultiplier==2,"boot grants charm and activates support");
        require(SimpleItemTooltipBuilder.Build(item).Contains("즉시 효과가 해제"),"production tooltip includes removal explanation");
        progression.AddExperience(9); require(progression.FlushPendingExperience(),"direct XP commits without hunting bonus");
        require(progression.Experience==9,"non-kill account XP stays unchanged while holding support");
        progression.AddKillExperience(10,50,1.5f); require(progression.FlushPendingExperience(),"boosted fractional kill XP commits");
        require(progression.Experience==54,"account kill XP multiplies after density and additive bonuses");
        var fixture=new GameObject("TesterSupportDropSlot",typeof(RectTransform));
        var old=new List<WorldItemPickup>(); WorldItemPickup.CopyActivePickups(old);
        var ids=new HashSet<int>(old.Select(x=>x.GetInstanceID()));
        try
        {
            var slot=fixture.AddComponent<SlotUI>();
            int index=inventory.Items.ToList().FindIndex(x=>x==item);
            slot.Init(null,index,false); slot.SetDisplayItem(item);
            var result=InventoryWorldDrop.TryDropInventorySlotToWorld(new InventoryWorldDropRequest {
                Inventory=inventory,SourceSlot=slot,FallbackTransform=PlayerContext.Instance.CurrentActor.transform });
            require(result.Succeeded,"real inventory world drop succeeds");
            require(TesterSupportItemData.CurrentMultiplier==1,"world drop immediately removes support");
            progression.AddExperience(9); require(progression.FlushPendingExperience() && progression.Experience==63,"non-kill XP stays unchanged after discard");
            require(session.FlushPendingSave(),"discarded state persists");
            var registry=Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
            var reloaded=MapAccountMigration.Load(new EasySaveAccountStore(AccountBootstrap.SaveDirectory),registry);
            require(reloaded.testerSupportGranted && Charm(reloaded)==null,"disk reconnect does not regrant discarded charm");
            var pickups=new List<WorldItemPickup>(); WorldItemPickup.CopyActivePickups(pickups);
            var pickup=pickups.Single(x=>!ids.Contains(x.GetInstanceID()) && x.RuntimeItem?.runtimeInstanceId==item.runtimeInstanceId);
            require(pickup.TryPickup(inventory),"discarded charm can be picked up through real acquisition path");
            require(TesterSupportItemData.CurrentMultiplier==2,"pickup restores support immediately");
            var debugType=typeof(PlayerProgression).Assembly.GetType("PlayerDebugModule");
            debugType.GetMethod("ToNextLevel",PrivateStatic).Invoke(null,null);
            require(progression.FlushPendingExperience() && progression.Level==2 && progression.Experience==0,"real next-level debug command reaches exact level under support");
            var target=debugType.GetField("targetLevel",PrivateStatic);
            int previousTarget=(int)target.GetValue(null);
            try
            {
                target.SetValue(null,5);
                debugType.GetMethod("RaiseToTarget",PrivateStatic).Invoke(null,null);
                require(progression.FlushPendingExperience() && progression.Level==5 && progression.Experience==0,"real target-level debug command reaches exact target under support");
            }
            finally {target.SetValue(null,previousTarget);}
            require(inventory.RemoveItem(inventory.Items.Single(x=>x?.baseData is TesterSupportItemData)),"inventory removal succeeds");
            require(TesterSupportItemData.CurrentMultiplier==1,"second removal leaves no lingering buff");
        }
        finally
        {
            Object.Destroy(fixture);
            var pickups=new List<WorldItemPickup>(); WorldItemPickup.CopyActivePickups(pickups);
            foreach(var pickup in pickups) if(pickup && !ids.Contains(pickup.GetInstanceID())) Object.Destroy(pickup.gameObject);
        }
    }
}
