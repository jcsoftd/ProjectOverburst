using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Overburst.Persistence;

[InitializeOnLoad]
public static class WeaponBalanceMigrationFixVerifier
{
    const string Key="WeaponBalanceMigrationFix";
    const string Root=@"D:\JC Program\유니티\개인프로젝트\프로젝트 오버버스트\개인파일\코덱스산출\Persistence\20260929_WeaponMigrationFix";
    static double deadline;
    static int readyFrames;
    static readonly List<string> errors=new List<string>();
    static string Output=>SessionState.GetString(Key+".output","");
    static WeaponBalanceMigrationFixVerifier(){EditorApplication.playModeStateChanged+=State;}
    static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
    static string Json(object value)=>Newtonsoft.Json.JsonConvert.SerializeObject(value);
    static string Identity(AccountSnapshot state)
    {
        var copy=ItemSnapshotCodec.CopyValues(state);copy.revision=0;copy.lastTransactionId=null;
        foreach(var item in copy.items)
        {
            // The existing, approved necklace migration swaps HP main/critical-damage secondary;
            // normalize only these recorded legacy necklaces when comparing immutable content.
            if(item.contentId=="594d23216f47ec246a43a99577e3c980"&&item.gearRolls!=null&&item.gearRolls[0].stat==GearStat.MaxHealth)
            {item.gearRolls[0].stat=GearStat.CriticalDamage;for(int i=1;i<item.gearRolls.Count;i++)if(item.gearRolls[i].stat==GearStat.CriticalDamage)item.gearRolls[i].stat=GearStat.MaxHealth;}
            item.balanceVersion=0;
            if(item.weaponRolls!=null)foreach(var row in item.weaponRolls){row.positiveTotalValue=0;row.negativeTotalValue=0;}
        }
        return Json(copy);
    }
    static ItemSnapshot Copy(ItemSnapshot x)=>ItemSnapshotCodec.CopyValues(x);
    public static object VerifyData(string output)
    {
        Directory.CreateDirectory(output);
        var registry=Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var source=new EasySaveAccountStore(Path.Combine(Root,"OriginalAccount")).Load();
        string original=Json(source),signature=Identity(source);
        var migrated=ItemBalanceMigration.UpgradeAccount(source,registry,out bool changed);
        Check(changed&&Json(source)==original,"original snapshot modified");
        Check(Identity(migrated)==signature,"item identity/stars/container/progression changed");
        AccountInvariants.Validate(migrated,registry);
        Check(System.Object.ReferenceEquals(ItemBalanceMigration.UpgradeAccount(migrated,registry,out bool again),migrated)&&!again,"migration is not idempotent");
        var problematic=migrated.items.First(x=>x.contentId=="d9b32fb76d97abf4b8e2023ad8af775f"&&x.grade==ItemGrade.Mythic);
        var runtime=ItemSnapshotCodec.Restore(problematic,registry);
        string before=Json(runtime.weaponGradeStatRolls);
        var stats=WeaponStatCalculator.Calculate(runtime);runtime.EnsureRuntimeState();
        Check(Json(runtime.weaponGradeStatRolls)==before,"runtime rerolled stars");
        Check(Mathf.Abs(stats.critDamageMultiplier-2f)<.0001f,"critical damage cap");
        var roundTrip=new EasySaveAccountStore(Path.Combine(output,"RoundTrip"));roundTrip.Save(migrated,"migration-regression");
        var loaded=new EasySaveAccountStore(Path.Combine(output,"RoundTrip")).Load();AccountInvariants.Validate(loaded,registry);
        Check(Json(loaded)==Json(migrated),"ES3 roundtrip differs");
        int rejected=0;
        Action<Action<ItemSnapshot>> reject=mutate=>{var bad=Copy(problematic);mutate(bad);bool failed=false;try{ItemSnapshotCodec.Validate(bad,registry);}catch(InvalidDataException){failed=true;}Check(failed,"corruption accepted "+rejected);rejected++;};
        reject(x=>x.weaponRolls[0].starRolls[0].starType=(WeaponGradeStarType)99);
        reject(x=>x.qualityProfile=(MeleeStarDistributionProfile)99);
        reject(x=>x.weaponRolls[0].positiveTotalValue=float.NaN);
        reject(x=>x.weaponRolls[0].negativeTotalValue=float.PositiveInfinity);
        reject(x=>x.weaponRolls[0].positiveStarCount++);
        reject(x=>x.weaponRolls[0].starRolls[0].starType=WeaponGradeStarType.Red);
        reject(x=>x.weaponRolls[1].statType=x.weaponRolls[0].statType);
        reject(x=>x.weaponRolls.RemoveAt(0));
        reject(x=>x.weaponRolls[0]=null);
        reject(x=>x.weaponRolls[0].positiveTotalValue+=.1f);
        var legacy=Copy(source.items.First(x=>x.instanceId==problematic.instanceId));legacy.weaponRolls[0].positiveTotalValue+=1;
        string badOriginal=Json(legacy);bool migrationRejected=false;
        try{ItemBalanceMigration.Upgrade(legacy,registry.Resolve<BaseItemData>(legacy.contentId));}catch(InvalidDataException){migrationRejected=true;}
        Check(migrationRejected&&Json(legacy)==badOriginal,"bad legacy totals mutated/accepted");
        var data=registry.Resolve<WeaponItemData>(problematic.contentId);
        var clone=UnityEngine.Object.Instantiate(data);
        var random=UnityEngine.Random.state;
        int generated=0;
        try
        {
            clone.baseStats.criticalDamageMultiplier=2f;clone.baseStats.criticalChance=60f;
            Check(WeaponGradeStatRoller.HasFormalMeleeGradeRolls(clone,problematic.grade,problematic.weaponRolls,problematic.qualityProfile),"rebalance invalidates stored stars");
            var capped=WeaponStatCalculator.Calculate(clone,runtime);
            Check(capped.critDamageMultiplier==2f&&capped.critChance==60f,"rebalance application caps");
            clone.baseStats=data.baseStats;
            UnityEngine.Random.InitState(9029);
            for(int grade=0;grade<8;grade++)for(int i=0;i<100;i++)
            {
                var item=new ItemData(clone,50,(ItemGrade)grade);
                Check(WeaponGradeStatRoller.HasFormalMeleeGradeRolls(clone,item.grade,item.weaponGradeStatRolls,item.meleeStarDistributionProfile),"new roll invalid");
                foreach(var row in item.weaponGradeStatRolls)Check(row.positiveTotalValue<=WeaponGradeStatRoller.GetMeleeMaximumPositiveGradeValue(clone,row.statType)+.0001f,"new roll exceeds cap");
                generated++;
            }
        }
        finally{UnityEngine.Random.state=random;UnityEngine.Object.DestroyImmediate(clone);}
        var report=new{status="PASS",items=source.items.Count,sourceRevision=source.revision,sourceRunPhase=source.run?.phase.ToString(),identityAndStarsPreserved=true,idempotent=true,criticalDamage=stats.critDamageMultiplier,corruptionCases=rejected+1,newRollCases=generated,roundTrip=true};
        File.WriteAllText(Path.Combine(output,"data-results.json"),Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
        return report;
    }
    public static void Run(string output)
    {
        Check(!EditorApplication.isPlaying,"already playing");
        VerifyData(output);
        string account=Path.Combine(output,"Account");Directory.CreateDirectory(account);
        foreach(var file in Directory.GetFiles(Path.Combine(Root,"OriginalAccount"),"*.es3"))File.Copy(file,Path.Combine(account,Path.GetFileName(file)),false);
        SessionState.SetString(Key+".output",output);SessionState.SetString(Key+".oldEnv",Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY")??"");
        SessionState.SetBool(Key,true);Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY",account);EditorApplication.EnterPlaymode();
    }
    public static void Reenter(string output)
    {
        Check(!EditorApplication.isPlaying,"already playing");
        File.Copy(Path.Combine(output,"play-results.json"),Path.Combine(output,"first-play-results.json"),false);
        SessionState.SetString(Key+".output",output);SessionState.SetString(Key+".oldEnv",Environment.GetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY")??"");
        SessionState.SetBool(Key,true);Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY",Path.Combine(output,"Account"));EditorApplication.EnterPlaymode();
    }
    static void State(PlayModeStateChange state)
    {
        if(!SessionState.GetBool(Key,false))return;
        if(state==PlayModeStateChange.EnteredPlayMode){SessionState.SetBool(Key+".background",Application.runInBackground);Application.runInBackground=true;deadline=EditorApplication.timeSinceStartup+150;readyFrames=0;errors.Clear();Application.logMessageReceived+=Log;EditorApplication.update+=Tick;}
        if(state==PlayModeStateChange.ExitingPlayMode){Application.logMessageReceived-=Log;EditorApplication.update-=Tick;Application.runInBackground=SessionState.GetBool(Key+".background",false);}
        if(state==PlayModeStateChange.EnteredEditMode){Environment.SetEnvironmentVariable("OVERBURST_SAVE_DIRECTORY",SessionState.GetString(Key+".oldEnv",""));SessionState.SetBool(Key,false);}
    }
    static void Log(string text,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(text);}
    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            Check(EditorApplication.timeSinceStartup<deadline,"boot timeout");
            if(!string.IsNullOrEmpty(AccountBootstrap.Error))throw new InvalidOperationException(AccountBootstrap.Error);
            if(!AccountBootstrap.Ready||PersistentSceneFlow.Instance==null||PersistentSceneFlow.Instance.IsSwitching||!WorldSessionState.IsHideout)return;
            if(++readyFrames<20)return;
            Check(AccountBootstrap.SaveDirectory.StartsWith(Output,StringComparison.OrdinalIgnoreCase),"account not isolated");
            var registry=Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
            var source=new EasySaveAccountStore(Path.Combine(Root,"OriginalAccount")).Load();
            var expected=ItemBalanceMigration.UpgradeAccount(source,registry,out _);
            if(expected.run!=null&&expected.run.phase!=RunPhase.Extracted&&expected.run.phase!=RunPhase.Failed)AccountRunCommands.RecoverInterrupted(expected);
            var state=AccountGameplaySession.Current.Read();AccountInvariants.Validate(state,registry);
            Check(Identity(expected)==Identity(state),"boot changed retained items or progression");
            var stored=new EasySaveAccountStore(AccountBootstrap.SaveDirectory).Load();AccountInvariants.Validate(stored,registry);
            Check(Identity(state)==Identity(stored),"boot saved state mismatch");
            Check(errors.Count==0,string.Join(" | ",errors));
            Finish("PASS",new{ready=AccountBootstrap.Ready,scene=PersistentSceneFlow.Instance.CurrentSubSceneName,items=state.items.Count,revision=state.revision,starsAndIdentityPreserved=true});
        }
        catch(Exception e){Finish("FAIL "+e,null);}
    }
    static void Finish(string status,object result)
    {
        File.WriteAllText(Path.Combine(Output,"play-results.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new{status,result,errors},Newtonsoft.Json.Formatting.Indented));
        EditorApplication.update-=Tick;EditorApplication.ExitPlaymode();
    }
}

