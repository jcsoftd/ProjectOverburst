using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Appearance;
using Overburst.Persistence;
using UnityEditor;
using Overburst.Quests;

public static class TitleCharacterDataVerifier
{
    public const string Output="../개인파일/코덱스산출/UI/20261011_TitleCharacterGoal/Evidence/Data";
    static readonly List<string> checks=new List<string>();
    static void Check(bool condition,string name){if(!condition)throw new InvalidOperationException(name);checks.Add(name);}
    static void Reject(Action action,string name){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}catch(InvalidOperationException){rejected=true;}catch(InvalidDataException){rejected=true;}Check(rejected,name);}
    static byte[] Bytes(object value)=>ES3.Serialize(value);
    public static void Run()
    {
        AppearanceCustomizationBuilder.RequireIdle();checks.Clear();Directory.CreateDirectory(Path.GetFullPath(Output));
        string run=DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        try
        {
            var registry=UnityEngine.Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
            var catalog=AssetDatabase.LoadAssetAtPath<CharacterAppearanceCatalog>(AppearanceCustomizationBuilder.CatalogPath);
            var look=catalog.defaultAppearance.Copy();
            var legacy=NewAccountFactory.Create(registry);legacy.level=7;legacy.skillTree=AccountSkillTree.GrantEligible(null,7);legacy.appearance=look.Copy();
            string oldWeapon=legacy.weapons[0];
            var shared=ItemSnapshotCodec.CopyValues(legacy.items[0]);shared.instanceId=Guid.NewGuid().ToString("N");shared.acquisitionOrder=legacy.nextAcquisitionOrder++;
            legacy.items.Add(shared);legacy.stashTabs[0].slots[0]=shared.instanceId;
            AccountInvariants.Validate(legacy,registry);
            var store=new EasySaveAccountStore(Path.GetFullPath(Output+"/"+run));store.Save(legacy,legacy.lastTransactionId);
            var loaded=store.Load();Check(loaded.characters==null&&loaded.level==7,"legacy format retains personal data before migration");
            var transactions=new AccountTransactions(loaded,store,registry);
            string id=Guid.NewGuid().ToString("N");
            var commonBefore=Bytes(loaded.stashTabs);
            transactions.Execute("create-"+id,transactions.Revision,c=>AccountCharacterCommands.Create(c,id,"새기사","knight",look,registry));
            var state=transactions.Read();
            Check(state.activeCharacterId==AccountCharacterCommands.LegacyId&&state.level==7&&state.weapons[0]==oldWeapon,"creating preserves legacy progression and equipment");
            Check(Bytes(state.stashTabs).SequenceEqual(commonBefore)&&state.characters.Count==2,"create adds personal record without copying or altering stash");
            Check(state.characters.Single(c=>c.id==state.activeCharacterId).personal==null,"active personal data has exactly one owner");
            transactions.Execute("select-new",transactions.Revision,c=>AccountCharacterCommands.Select(c,id));
            state=transactions.Read();string newWeapon=state.weapons[0];
            Check(state.level==1&&newWeapon!=oldWeapon&&state.appearance.Equals(look),"new character has separate progression equipment appearance");
            Check(state.stashTabs[0].slots[0]==shared.instanceId&&state.items.Exists(i=>i.instanceId==shared.instanceId),"select preserves shared stash and item identity");
            Check(!state.items.Exists(i=>i.instanceId==oldWeapon)&&state.characters.Single(c=>c.id==AccountCharacterCommands.LegacyId).personal.weapons[0]==oldWeapon,"previous personal items move out of active ownership");
            transactions.Execute("level-new",transactions.Revision,c=>{c.level=4;c.skillTree=AccountSkillTree.GrantEligible(c.skillTree,4);});
            transactions.Execute("select-legacy",transactions.Revision,c=>AccountCharacterCommands.Select(c,AccountCharacterCommands.LegacyId));
            state=transactions.Read();Check(state.level==7&&state.weapons[0]==oldWeapon,"switch back restores original character");
            transactions.Execute("select-new-again",transactions.Revision,c=>AccountCharacterCommands.Select(c,id));
            Check(transactions.Read().level==4,"second character changes remain independent");
            transactions.Execute("stash-withdraw",transactions.Revision,c=>{c.inventory[0]=c.stashTabs[0].slots[0];c.stashTabs[0].slots[0]=null;});
            transactions.Execute("select-after-withdraw",transactions.Revision,c=>AccountCharacterCommands.Select(c,AccountCharacterCommands.LegacyId));
            state=transactions.Read();Check(state.stashTabs[0].slots[0]==null&&!state.items.Exists(i=>i.instanceId==shared.instanceId),"withdrawn shared item becomes personal without duplication");
            Check(state.characters.Single(c=>c.id==id).personal.inventory[0]==shared.instanceId,"withdrawn item follows receiving character");
            var freshStore=new EasySaveAccountStore(Path.GetFullPath(Output+"/"+run));state=freshStore.Load();AccountInvariants.Validate(state,registry);
            Check(state.characters.Count==2&&state.activeCharacterId==AccountCharacterCommands.LegacyId,"real ES3 generations restore roster and selection");
            var copy=transactions.Read();copy.characters.Single(c=>c.id==id).personal.level=2;
            Check(transactions.Read().characters.Single(c=>c.id==id).personal.level==4,"public account read deep copies dormant data");
            foreach(var invalid in new[]{"a","공 백","<>위험","abcdefghijklmn","a/b","가\n나"})Reject(()=>AccountCharacterCommands.ValidateName(invalid),"invalid name rejected: "+invalid.Replace("\n","\\n"));
            Check(AccountCharacterCommands.ValidateName(" 가나12 ")=="가나12","name is trimmed and validated");
            var nonCanonical=transactions.Read();nonCanonical.characters.Single(c=>c.id==id).name=" 새기사 ";Reject(()=>AccountInvariants.Validate(nonCanonical,registry),"stored noncanonical name cannot bypass normalized uniqueness");
            Reject(()=>AccountCharacterCommands.Create(copy,Guid.NewGuid().ToString("N"),"새기사","knight",look,registry),"duplicate name rejected");
            Reject(()=>AccountCharacterCommands.Create(copy,Guid.NewGuid().ToString("N"),"새마법사","mage",look,registry),"unavailable class rejected at data boundary");
            Reject(()=>AccountCharacterCommands.Select(copy,"missing"),"unknown selection rejected");
            var duplicate=transactions.Read();var dormant=duplicate.characters.Single(c=>c.id==id).personal;
            dormant.items[0].instanceId=duplicate.weapons[0];dormant.weapons[0]=duplicate.weapons[0];
            Reject(()=>AccountInvariants.Validate(duplicate,registry),"cross character duplicate item rejected");
            var nested=transactions.Read();nested.characters.Single(c=>c.id==id).personal.characters=new List<AccountCharacterSnapshot>();
            Reject(()=>AccountInvariants.Validate(nested,registry),"recursive roster rejected");
            var authoritative=Bytes(transactions.Read());long revision=transactions.Revision;
            store.FaultInjector=phase=>{if(phase=="before-write")throw new IOException("Owned injected write failure");};
            bool failed=false;try{transactions.Execute("failed-switch",revision,c=>AccountCharacterCommands.Select(c,id));}catch(IOException){failed=true;}
            Check(failed&&Bytes(transactions.Read()).SequenceEqual(authoritative)&&transactions.Revision==revision,"write failure preserves authoritative snapshot and revision");
            store.FaultInjector=null;
            Check(transactions.Execute("failed-switch",revision,c=>AccountCharacterCommands.Select(c,id)),"same operation can retry after failure");
            var disk=new EasySaveAccountStore(Path.GetFullPath(Output+"/"+run)).Load();Check(disk.activeCharacterId==id&&disk.level==4,"retry persists correct selected character");
            var first=AccountCharacterCommands.First(Guid.NewGuid().ToString("N"),"첫기사","knight",look,registry);
            Check(first.characters.Count==1&&first.characters[0].personal==null&&first.level==1,"empty account creates one active character without fake legacy");
            ExtendedChecks(registry,catalog,first,Path.GetFullPath(Output+"/"+run));
            File.WriteAllText(Path.GetFullPath(Output+"/result.json"),JsonConvert.SerializeObject(new{status="PASS",checks=checks.Count,cases=checks,save=run,utc=DateTime.UtcNow,player="NOT_RUN"},Formatting.Indented));
        }
        catch(Exception error){File.WriteAllText(Path.GetFullPath(Output+"/result.json"),JsonConvert.SerializeObject(new{status="FAIL",checks=checks.Count,cases=checks,error=error.ToString()},Formatting.Indented));throw;}
    }
    static void ExtendedChecks(AccountContentRegistry registry,CharacterAppearanceCatalog catalog,AccountSnapshot first,string folder)
    {
        var known="schemaVersion profileId revision nextAcquisitionOrder level experience inventoryCapacity bagExperienceCarry bagGoldCarry unlockedSlots baseUnlockedSlots stashCapacity currentStashTab activeWeaponSlot items inventory stashTabs weapons gear bags flasks quickSlots merchants quests trackedQuestId run lastTransactionId bossClearCount elementalGemInstanceId skillTree appearance activeCharacterId characters".Split(' ');
        Check(typeof(AccountSnapshot).GetFields().Select(f=>f.Name).OrderBy(n=>n).SequenceEqual(known.OrderBy(n=>n)),"every current account field has an explicit personal shared or header ownership decision");
        Check(AccountCharacterCommands.ValidateName("가나")=="가나","decomposed Korean name normalizes like HTML");
        string dir=folder+"/FailureBoundaries";var store=new EasySaveAccountStore(dir);first.lastTransactionId="first-boundary";store.Save(first,first.lastTransactionId);
        var tx=new AccountTransactions(store.Load(),store,registry);string id=Guid.NewGuid().ToString("N");
        var look=catalog.defaultAppearance.Copy();look.faceId=catalog.faces[0].id;
        tx.Execute("new-boundary",tx.Revision,c=>AccountCharacterCommands.Create(c,id,"다른기사","knight",look,registry),true);
        var baseline=Bytes(tx.Read());long revision=tx.Revision;
        using(var locked=new FileStream(Path.Combine(dir,"account.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        {
            bool rejected=false;try{tx.Execute("locked-select",revision,c=>AccountCharacterCommands.Select(c,id),true);}catch(IOException){rejected=true;}
            Check(rejected&&Bytes(tx.Read()).SequenceEqual(baseline),"actual filesystem writer lock rejects without committing personal switch");
        }
        tx.Execute("locked-select",revision,c=>AccountCharacterCommands.Select(c,id),true);
        store.FaultInjector=phase=>{if(phase=="after-write")throw new IOException("Owned ambiguous completion");};
        tx.Execute("uncertain",tx.Revision,c=>{c.experience=3;},true);store.FaultInjector=null;
        Check(new EasySaveAccountStore(dir).Load().experience==3,"after-write exception resolves by actual durable readback");
        var staleStore=new EasySaveAccountStore(dir);var stale=staleStore.Load();
        tx.Execute("external-newer",tx.Revision,c=>{c.experience=4;},true);
        bool conflict=false;try{staleStore.Save(stale,"stale-writer");}catch(IOException){conflict=true;}
        Check(conflict&&new EasySaveAccountStore(dir).Load().experience==4,"external writer conflict preserves newer generation");
        var same=tx.Read();var current=same.characters.Single(c=>c.id==id);var saved=same.appearance.Copy();
        AccountCharacterCommands.Create(same,id,current.name,"knight",saved,registry);
        Check(same.characters.Count==2,"same generated ID and content are idempotent");
        var changed=saved.Copy();changed.faceId=catalog.faces[1].id;
        Reject(()=>AccountCharacterCommands.Create(same,id,current.name,"knight",changed,registry),"same generated ID with different appearance is rejected");
        var quest=UnityEngine.ScriptableObject.CreateInstance<QuestDefinition>();
        try
        {
            quest.id="test.character-owned";quest.title="캐릭터 의뢰";quest.description="격리 검증";quest.giverMerchantId=quest.turnInMerchantId="merchant.main-town.weapon";
            quest.objectives=new[]{new QuestObjectiveDefinition{kind=QuestObjectiveKind.KillEnemy,targetId="enemy.any-run",required=10,label="사냥"}};quest.rewardGold=100;
            tx.Execute("quest-new",tx.Revision,c=>AccountQuestCommands.Accept(c,quest,quest.giverMerchantId,registry),true);
            tx.Execute("old-quest",tx.Revision,c=>AccountCharacterCommands.Select(c,first.activeCharacterId),true);
            Check(tx.Read().quests.Count==0&&tx.Read().trackedQuestId==null,"quest progression and tracked selection belong to each character");
            tx.Execute("new-quest",tx.Revision,c=>AccountCharacterCommands.Select(c,id),true);
            Check(tx.Read().quests.Single().questId==quest.id&&tx.Read().trackedQuestId==quest.id&&tx.Read().appearance.Equals(look),"switch restores separate quest tracker and appearance");
        }
        finally{UnityEngine.Object.DestroyImmediate(quest);}
        var running=tx.Read();running.run=new RunSnapshot{runId="owned-interrupted",phase=RunPhase.Active,map=new MapInstanceState{level=1,grade=ItemGrade.Common}};
        Reject(()=>AccountCharacterCommands.Select(running,first.activeCharacterId),"unfinished run prevents moving personal data");
        AccountRunCommands.RecoverInterrupted(running);AccountCharacterCommands.Select(running,first.activeCharacterId);AccountInvariants.Validate(running,registry);
        Check(running.characters.Single(c=>c.id==id).personal.run.phase==RunPhase.Failed,"existing interrupted run recovery occurs before character switch");
        string corrupt=folder+"/Corrupt";Directory.CreateDirectory(corrupt);byte[] bad=System.Text.Encoding.UTF8.GetBytes("owned-invalid-account-generation");File.WriteAllBytes(corrupt+"/account-A.es3",bad);File.WriteAllBytes(corrupt+"/account-B.es3",bad);
        Reject(()=>new EasySaveAccountStore(corrupt).Load(),"both corrupt generations reject rather than create a new account");
        Check(File.ReadAllBytes(corrupt+"/account-A.es3").SequenceEqual(bad)&&File.ReadAllBytes(corrupt+"/account-B.es3").SequenceEqual(bad),"corrupt original generations remain byte-identical");
        var old=ItemSnapshotCodec.CopyValues(tx.Read());old.schemaVersion=2;foreach(var entry in old.characters)if(entry.personal!=null)entry.personal.schemaVersion=2;
        string legacy=folder+"/Schema2";Directory.CreateDirectory(legacy);var envelope=new AccountSaveEnvelope{schemaVersion=2,profileId=old.profileId,generation=1,transactionId="schema2-owned",writtenAtUtcTicks=DateTime.UtcNow.Ticks,payload=ES3.Serialize(old)};envelope.checksum=EasySaveAccountStore.Checksum(envelope);
        ES3.Save("account",envelope,new ES3Settings(legacy+"/account-A.es3"));var original=File.ReadAllBytes(legacy+"/account-A.es3");var upgraded=new EasySaveAccountStore(legacy).Load();AccountInvariants.Validate(upgraded,registry);
        Check(upgraded.schemaVersion==EasySaveAccountStore.SchemaVersion&&upgraded.characters.All(c=>c.personal==null||c.personal.schemaVersion==EasySaveAccountStore.SchemaVersion),"schema2 character roster migrates root and dormant personal data together");
        Check(File.ReadAllBytes(legacy+"/account-A.es3").SequenceEqual(original)&&File.ReadAllBytes(legacy+"/schema2-original/account-A.es3.generation-1").SequenceEqual(original),"schema2 original and migration backup preserve exact bytes");
    }
}
