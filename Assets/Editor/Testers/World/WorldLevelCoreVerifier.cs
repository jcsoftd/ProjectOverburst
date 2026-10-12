using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Overburst.Persistence;
using UnityEditor;
using UnityEngine;

public static class WorldLevelCoreVerifier
{
    public static string Run(string output)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling)throw new InvalidOperationException("Idle EditMode required");
        Directory.CreateDirectory(output);var checks=new List<string>();
        void Check(bool success,string label){if(!success)throw new InvalidOperationException(label);checks.Add(label);}
        void Throws<T>(Action action,string label) where T:Exception {try{action();}catch(T){checks.Add(label);return;}throw new InvalidOperationException("Expected "+typeof(T).Name+": "+label);}
        var registry=Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var originalPhase=WorldSessionState.Phase;
        var phase=typeof(WorldSessionState).GetMethod("SetPhase",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        try
        {
            int[] levels={1,9,10,19,20,35,89,90,100};int[] expected={1,1,2,2,3,4,9,10,10};
            for(int i=0;i<levels.Length;i++)Check(AccountWorldLevelCommands.EligibleStage(levels[i])==expected[i],"eligibility Lv"+levels[i]);
            for(int stage=1;stage<=10;stage++)Check(AccountWorldLevelCommands.MinimumMonsterLevel(stage)==(stage-1)*10+1&&AccountWorldLevelCommands.MaximumMonsterLevel(stage)==stage*10,"monster range "+stage);
            var initial=NewAccountFactory.Create(registry);initial.level=35;initial.skillTree=AccountSkillTree.GrantEligible(initial.skillTree,35);
            Check(initial.unlockedWorldLevel==1&&initial.appliedWorldLevel==1,"new character starts at stage1 despite level35");
            var legacy=JToken.Parse(Encoding.UTF8.GetString(ES3.Serialize(initial)));
            foreach(var token in legacy.SelectTokens("$..unlockedWorldLevel").Concat(legacy.SelectTokens("$..appliedWorldLevel")).ToArray())token.Parent.Remove();
            var legacyLoaded=ES3.Deserialize<AccountSnapshot>(Encoding.UTF8.GetBytes(legacy.ToString()));
            Check(legacyLoaded.unlockedWorldLevel==1&&legacyLoaded.appliedWorldLevel==1,"old ES3 payload with missing fields loads stage1 defaults");
            var store=new EasySaveAccountStore(Path.Combine(output,"Atomic"));store.Save(initial,"initial");
            var session=new AccountGameplaySession(null,registry,store,initial,true);
            phase.Invoke(null,new object[]{WorldPhase.Hideout});
            Check(session.UnlockWorldLevels(0),"unlock command commits");
            Check(session.Read().unlockedWorldLevel==4&&session.Read().appliedWorldLevel==1,"bulk unlock leaves applied stage unchanged");
            var reloaded=new EasySaveAccountStore(Path.Combine(output,"Atomic")).Load();
            Check(reloaded.unlockedWorldLevel==4&&reloaded.appliedWorldLevel==1,"unlock durable without waiting for autosave");
            long revision=session.Revision;
            Check(!session.UnlockWorldLevels(revision)&&session.Revision==revision,"repeat unlock has no revision or write");
            Throws<InvalidOperationException>(()=>session.ApplyWorldLevel(0,4),"stale confirmation rejected");
            Throws<InvalidOperationException>(()=>session.ApplyWorldLevel(revision,5),"locked stage rejected");
            Throws<InvalidOperationException>(()=>session.ApplyWorldLevel(revision,0),"invalid lower stage rejected");
            byte[] before=ES3.Serialize(session.Read());
            store.FaultInjector=p=>{if(p=="before-write")throw new IOException("fixture A/B write failure");};
            Throws<IOException>(()=>session.ApplyWorldLevel(revision,4),"failed checkpoint throws");
            Check(before.SequenceEqual(ES3.Serialize(session.Read())),"failed apply leaves full in-memory snapshot unchanged");
            Check(new EasySaveAccountStore(Path.Combine(output,"Atomic")).Load().appliedWorldLevel==1,"failed apply retains disk stage1");
            store.FaultInjector=p=>{if(p=="after-write")throw new IOException("fixture uncertain write");};
            Check(session.ApplyWorldLevel(revision,4),"uncertain completed write resolves by transaction readback");
            Check(new EasySaveAccountStore(Path.Combine(output,"Atomic")).Load().appliedWorldLevel==4,"applied stage4 survives new store instance");
            revision=session.Revision;Check(!session.ApplyWorldLevel(revision,4)&&session.Revision==revision,"repeat apply is a no-op");
            store.FaultInjector=null;
            phase.Invoke(null,new object[]{WorldPhase.Run});Throws<InvalidOperationException>(()=>session.ApplyWorldLevel(revision,1),"run blocks NPC changes");
            phase.Invoke(null,new object[]{WorldPhase.Hideout});
            var roster=session.Read();AccountCharacterCommands.EnsureLegacy(roster);string first=roster.activeCharacterId,newId=Guid.NewGuid().ToString("N");
            var catalog=AssetDatabase.LoadAssetAtPath<Overburst.Appearance.CharacterAppearanceCatalog>(AppearanceCustomizationBuilder.CatalogPath);
            AccountCharacterCommands.Create(roster,newId,"월드검사","knight",catalog.defaultAppearance.Copy(),registry);
            var shared=ES3.Serialize(roster.stashTabs);AccountCharacterCommands.Select(roster,newId);
            Check(roster.unlockedWorldLevel==1&&roster.appliedWorldLevel==1,"new character has independent stage1");
            roster.level=90;roster.skillTree=AccountSkillTree.GrantEligible(roster.skillTree,90);AccountWorldLevelCommands.Unlock(roster);AccountWorldLevelCommands.Apply(roster,10);
            AccountCharacterCommands.Select(roster,first);Check(roster.unlockedWorldLevel==4&&roster.appliedWorldLevel==4,"switch restores first character4/4");
            AccountCharacterCommands.Select(roster,newId);Check(roster.unlockedWorldLevel==10&&roster.appliedWorldLevel==10,"switch restores second character10/10");
            Check(shared.SequenceEqual(ES3.Serialize(roster.stashTabs)),"character world-level changes preserve shared stash");
            AccountInvariants.Validate(roster,registry);var rosterStore=new EasySaveAccountStore(Path.Combine(output,"Characters"));rosterStore.Save(roster,"roster");var rosterReload=rosterStore.Load();
            Check(rosterReload.unlockedWorldLevel==10&&rosterReload.appliedWorldLevel==10,"active character fields survive ES3 roundtrip");
            AccountCharacterCommands.Select(rosterReload,first);Check(rosterReload.unlockedWorldLevel==4&&rosterReload.appliedWorldLevel==4,"dormant character fields survive ES3 roundtrip");
            foreach(int invalid in new[]{0,11}){var corrupt=ItemSnapshotCodec.CopyValues(initial);corrupt.unlockedWorldLevel=invalid;Throws<InvalidDataException>(()=>AccountInvariants.Validate(corrupt,registry),"invalid unlocked saved value "+invalid);}
            var invalidApplied=ItemSnapshotCodec.CopyValues(initial);invalidApplied.appliedWorldLevel=2;Throws<InvalidDataException>(()=>AccountInvariants.Validate(invalidApplied,registry),"applied greater than unlocked rejected");
            var randomState=UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(73130);
                var definition=Resources.Load<MapItemData>("Items/Maps/Map_Diamond01");
                Check(definition&&registry.IdFor(definition)!=null,"outdoor map definition is registered");
                Check(MapDropPolicy.Roll(definition,registry,35,EnemyThemeTier.Elite,null)==null,"dungeon map roll still requires a run");
                foreach(int level in new[]{1,35,100})foreach(var tier in new[]{EnemyThemeTier.Small,EnemyThemeTier.Medium,EnemyThemeTier.Elite})
                {
                    var maps=Enumerable.Range(0,2000).Select(_=>MapDropPolicy.Roll(definition,registry,level,tier,"ignored-run",outdoor:true)).Where(m=>m!=null).ToArray();
                    Check(maps.Length>0,"outdoor map rolls available Lv"+level+" "+tier);
                    Check(maps.All(m=>m.grade<=ItemGrade.Legendary&&m.mapState.grade==m.grade),"outdoor map grade cap Lv"+level+" "+tier);
                    Check(maps.All(m=>m.level>=MapDropPolicy.MinimumLevel(level,tier)&&m.level<=MapDropPolicy.MaximumLevel(level,tier)&&m.mapState.level==m.level),"outdoor map level range Lv"+level+" "+tier);
                    Check(maps.All(m=>m.originRunId==null),"outdoor maps have no run ownership Lv"+level+" "+tier);
                }
                var dungeonMaps=Enumerable.Range(0,4000).Select(_=>MapDropPolicy.Roll(definition,registry,35,EnemyThemeTier.Elite,"retained-run")).Where(m=>m!=null).ToArray();
                Check(dungeonMaps.All(m=>m.originRunId=="retained-run")&&dungeonMaps.Any(m=>m.grade>ItemGrade.Legendary),"dungeon ownership and upper grades remain available");
                foreach(int level in new[]{1,35,100})foreach(var rank in new[]{EnemyGradeType.Normal,EnemyGradeType.Elite,EnemyGradeType.Boss})
                {
                    var rolls=Enumerable.Range(0,101).Select(i=>i/100f).ToArray();
                    Check(rolls.All(r=>FlaskLootPolicy.SelectGrade(r,level,rank==EnemyGradeType.Boss,rank!=EnemyGradeType.Normal,100,maximumGrade:ItemGrade.Legendary)<=ItemGrade.Legendary),"shared farming ceiling before roll Lv"+level+" "+rank);
                    var gems=rolls.Select(r=>ElementGemLootPolicy.Select(r,level,rank,rarePercent:100,maximumGrade:ItemGrade.Legendary)).ToArray();
                    Check(gems.All(g=>g&&g.fixedGrade<=ItemGrade.Legendary),"fixed-grade gem ceiling selects legal definitions Lv"+level+" "+rank);
                }
                Check(FlaskLootPolicy.SelectGrade(1,100,true,true)>ItemGrade.Legendary&&ElementGemLootPolicy.Select(1,100,EnemyGradeType.Boss).fixedGrade>ItemGrade.Legendary,"default dungeon farming upper grades remain available");
                var farming=new System.Collections.Generic.Dictionary<string,Func<ItemData>>
                {
                    ["flask"]=()=>FlaskLootPolicy.Roll(EnemyGradeType.Boss,100,maximumGrade:ItemGrade.Legendary),
                    ["gear"]=()=>GearLootPolicy.Roll(EnemyGradeType.Boss,100,maximumGrade:ItemGrade.Legendary),
                    ["weapon"]=()=>WeaponLootPolicy.Roll(EnemyGradeType.Boss,100,maximumGrade:ItemGrade.Legendary),
                    ["gem"]=()=>ElementGemLootPolicy.CreateRoll(100,EnemyGradeType.Boss,maximumGrade:ItemGrade.Legendary),
                    ["bag"]=()=>BagFarmingLoot.RollBag(null,100,ItemGrade.Common,0,ItemGrade.Legendary)
                };
                foreach(var pair in farming)
                {
                    var items=Enumerable.Range(0,pair.Key=="bag"?5000:1000).Select(_=>pair.Value()).Where(i=>i!=null).ToArray();
                    Check(items.Length>0&&items.All(i=>i.grade<=ItemGrade.Legendary),"created "+pair.Key+" items respect outdoor ceiling");
                    foreach(var item in items)ItemSnapshotCodec.Capture(item,registry);
                    Check(true,"created "+pair.Key+" rolls and fixed definitions pass real save codec validation");
                }
                Throws<ArgumentException>(()=>new EncounterContext("run",35,ItemGrade.Common,outdoorSessionId:"outdoor"),"mixed run and outdoor ownership rejected");
            }
            finally{UnityEngine.Random.state=randomState;}
            File.WriteAllText(Path.Combine(output,"result.json"),JObject.FromObject(new{status="PASS",count=checks.Count,checks}).ToString());return output;
        }
        catch(Exception error){File.WriteAllText(Path.Combine(output,"failure.txt"),error+"\n"+string.Join("\n",checks));throw;}
        finally{phase.Invoke(null,new object[]{originalPhase});}
    }
}
