using System;
using System.IO;
using Overburst.DebugTools.Performance;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

public static class CombatSpawnQualityVerifier
{
    public static string Start(string observation="Detailed",int count=50,bool town=true,bool combat=true,float seconds=10,string element="Fire")
    {
        var profile=CombatPerformanceProfile.Smoke();
        profile.label="CSP-G00-"+observation+"-"+count;
        profile.csp=true;profile.cspTown=town;profile.cspCombat=combat;profile.cspObservation=observation;
        profile.themes=new[]{"GraveHunt"};profile.enemyCounts=new[]{count};profile.elements=new[]{(WeaponElement)Enum.Parse(typeof(WeaponElement),element)};
        profile.baselineSeconds=3;profile.warmupSeconds=2;profile.measureSeconds=seconds;profile.approachSeconds=3;profile.tailSeconds=5;
        profile.uncapped=true;profile.frameCapacity=32768;profile.eventCapacity=65536;profile.spawnPerFrame=2;profile.enemyHp=100000;
        return CombatPerformanceSession.Start(profile);
    }
    public static object Verify(string output)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||CombatPerformanceSession.Busy||CombatSpawnQualitySuite.Busy||IsolatedSavePlayGuard.RequiresAccountChoice||!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))||!string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","")))throw new InvalidOperationException("CSP Native verifier requires idle returned Editor");
        var original=CombatPerformanceVerifier.VerifyCore(output);
        var checks=new System.Collections.Generic.List<string>();
        void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks.Add(name);}
        var stats=CombatPerformanceStats.Calculate(new[]{10d,20,30,40,50},5);
        Check(stats.mean==30&&stats.median==30&&stats.p95==50&&stats.p99==50,"known quantiles");
        foreach(var mode in new[]{"Detailed","Minimal","WallOnly"}){new CombatPerformanceProfile{csp=true,cspObservation=mode}.Validate();Check(true,mode+" valid");}
        var bindingA=UnityEngine.ScriptableObject.CreateInstance<UnityEngine.InputSystem.InputActionAsset>();
        var bindingB=UnityEngine.ScriptableObject.CreateInstance<UnityEngine.InputSystem.InputActionAsset>();
        try
        {
            bindingA.AddActionMap("UI").AddAction("QuestList",UnityEngine.InputSystem.InputActionType.Button,"<Keyboard>/j");
            bindingB.AddActionMap("UI").AddAction("QuestList",UnityEngine.InputSystem.InputActionType.Button,"<Keyboard>/j");
            Check(bindingA.ToJson()!=bindingB.ToJson(),"fresh dynamic input IDs differ");
            Check(CombatPerformanceRunner.CspInputContract(bindingA)==CombatPerformanceRunner.CspInputContract(bindingB),"semantic input contract ignores generated IDs only");
            bindingB.FindAction("QuestList").ApplyBindingOverride("<Keyboard>/k");
            Check(CombatPerformanceRunner.CspInputContract(bindingA)!=CombatPerformanceRunner.CspInputContract(bindingB),"effective binding change remains detectable");
        }
        finally{UnityEngine.Object.DestroyImmediate(bindingA);UnityEngine.Object.DestroyImmediate(bindingB);}
        bool rejected=false;try{new CombatPerformanceProfile{csp=true,cspObservation="bad"}.Validate();}catch(ArgumentException){rejected=true;}Check(rejected,"bad mode rejected");
        rejected=false;try{new CombatPerformanceProfile{csp=true,cspTown=false,cspCombat=false}.Validate();}catch(ArgumentException){rejected=true;}Check(rejected,"empty scenario rejected");
        Check(CombatSpawnQualityTrace.Sink==null,"no residual trace sink");
        var node=new GameObject("CSP owned health fixture"){hideFlags=HideFlags.HideAndDontSave};
        try
        {
            var health=node.AddComponent<CombatHealth>();
            typeof(CombatHealth).GetField("showDamageNumbers",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(health,false);
            health.SetMaxHp(100,true);
            var trace=new System.Collections.Generic.List<string>();
            Action<CombatHealth,DamageInfo,float,bool> resolved=(h,d,loss,fatal)=>trace.Add("resolved:"+loss);
            Action<CombatHealth,DamageInfo> damaged=(h,d)=>trace.Add("damaged");
            Action<CombatHealth,float,float> changed=(h,current,max)=>trace.Add("health:"+current);
            health.OnDamageResolved+=resolved;health.OnDamaged+=damaged;health.OnHealthChanged+=changed;
            health.TakeDamage(new DamageInfo(10,Vector3.zero,triggersOnHitEffects:false,suppressDefaultHitVfx:true));
            Check(health.CurrentHp==90,"health actual loss");
            Check(trace.Count==3&&trace[0]=="resolved:10"&&trace[1]=="damaged"&&trace[2]=="health:90","actual damage event order");
            trace.Clear();health.TakeDamage(new DamageInfo(0,Vector3.zero,triggersOnHitEffects:false,suppressDefaultHitVfx:true));Check(trace.Count==0,"zero damage produces no notifications");
            health.OnDamaged+=damaged;trace.Clear();health.TakeDamage(new DamageInfo(1,Vector3.zero,triggersOnHitEffects:false,suppressDefaultHitVfx:true));Check(trace.FindAll(x=>x=="damaged").Count==2,"duplicate subscriptions retained");
            health.OnDamaged-=damaged;trace.Clear();health.TakeDamage(new DamageInfo(1,Vector3.zero,triggersOnHitEffects:false,suppressDefaultHitVfx:true));Check(trace.FindAll(x=>x=="damaged").Count==1,"single unsubscribe removes one subscription");
            health.OnDamageResolved-=resolved;health.OnDamaged-=damaged;health.OnHealthChanged-=changed;
            trace.Clear();health.TakeDamage(new DamageInfo(1,Vector3.zero,triggersOnHitEffects:false,suppressDefaultHitVfx:true));Check(trace.Count==0,"observer teardown complete");
        }
        finally{UnityEngine.Object.DestroyImmediate(node);}
        var previous=CombatSpawnQualityTrace.Sink;
        try
        {
            CombatSpawnQualityTrace.Sink=e=>throw new InvalidOperationException("expected observer failure");
            CombatSpawnQualityTrace.Emit(999);Check(CombatSpawnQualityTrace.Failure!=null,"trace observer exception contained");
        }
        finally{CombatSpawnQualityTrace.Sink=previous;CombatSpawnQualityTrace.Failure=null;}
        var recordProfile=new CombatPerformanceProfile{frameCapacity=128,eventCapacity=128};
        var result=new CombatPerformanceSegment();
        using(var record=new CombatPerformanceRecorder(recordProfile,result,null,metricNames:Array.Empty<string>()))
        {
            record.Tick();record.Tick();
            for(int i=0;i<130;i++)record.Event(999);
            record.End(Path.Combine(output,"CapacityFixture"));
        }
        Check(result.frames==0,"same-frame tick does not duplicate completed intervals");
        Check(result.droppedEvents==2&&result.validity=="INCOMPLETE","event overflow is retained and invalidates record");

        CombatPerformancePaths.SaveJson(Path.Combine(output,"CspNative.json"),new Result{status="PASS",checks=checks.ToArray()});
        return new{status="PASS",original,checks=checks.Count};
    }
    [Serializable] sealed class Result{public string status;public string[]checks;}
}
