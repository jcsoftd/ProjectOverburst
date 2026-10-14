using System;
using System.Collections.Generic;
using System.IO;
using Overburst.DebugTools.Performance;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class CombatSpawnQualitySuite
{
    const string Key="Overburst.CSP.Suite.";
    [Serializable] public sealed class Step {public string mode="Detailed",element="Fire",role="baseline";public int count=50;public float seconds=10;public bool town=true,combat=true;}
    [Serializable] sealed class Plan {public int index;public string folder,active,status="RUNNING",reason="";public List<Step>steps=new List<Step>();public List<string>runs=new List<string>();}
    [Serializable] sealed class ReturnedStatus {public string status;}
    static Plan plan;
    static CombatSpawnQualitySuite(){string folder=SessionState.GetString(Key+"folder","");if(!string.IsNullOrEmpty(folder)&&File.Exists(Path.Combine(folder,"suite.json")))plan=JsonUtility.FromJson<Plan>(File.ReadAllText(Path.Combine(folder,"suite.json")));EditorApplication.update+=Tick;}
    public static bool Busy=>plan!=null&&plan.status=="RUNNING";
    public static string Status=>plan==null?"IDLE":plan.status+" "+plan.index+"/"+plan.steps.Count;
    static void Save(){CombatPerformancePaths.SaveJson(Path.Combine(plan.folder,"suite.json"),plan);}
    public static string StartBaseline()
    {
        if(Busy||CombatPerformanceSession.Busy||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)throw new InvalidOperationException("idle returned Editor required");
        plan=new Plan{folder=Path.Combine(CombatPerformancePaths.OutputRoot,"CSP_Suite_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"))};Directory.CreateDirectory(plan.folder);
        var modes=new[]{"WallOnly","Minimal","Detailed"};
        for(int i=0;i<3;i++)for(int j=0;j<3;j++)plan.steps.Add(new Step{mode=modes[(i+j)%3]});
        SessionState.SetString(Key+"folder",plan.folder);Save();return plan.folder;
    }
    public static string StartValidation()
    {
        if(Busy||CombatPerformanceSession.Busy||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)throw new InvalidOperationException("idle returned Editor required");
        plan=new Plan{folder=Path.Combine(CombatPerformancePaths.OutputRoot,"CSP_Validation_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"))};Directory.CreateDirectory(plan.folder);
        foreach(string element in new[]{"Fire","Ice","Electric","Dark","Light"})plan.steps.Add(new Step{element=element,count=8,town=false,seconds=3,role="quality-smoke"});
        SessionState.SetString(Key+"folder",plan.folder);Save();return plan.folder;
    }
    // Five times the core 50-enemy combat/death fixture. Outdoor remains its production 50-enemy cohort.
    public static string StartStress()
    {
        if(Busy||CombatPerformanceSession.Busy||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)throw new InvalidOperationException("idle returned Editor required");
        plan=new Plan{folder=Path.Combine(CombatPerformancePaths.OutputRoot,"CSP_Stress_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"))};Directory.CreateDirectory(plan.folder);
        plan.steps.Add(new Step{mode="Detailed",count=250,seconds=10,town=true,combat=true,role="stress-five-times-combat-death"});
        SessionState.SetString(Key+"folder",plan.folder);Save();return plan.folder;
    }
    [MenuItem("Tools/OVERBURST/CSP/기준선 9회")]
    static void BaselineMenu(){Debug.Log(StartBaseline());}
    [MenuItem("Tools/OVERBURST/CSP/5원소 계약 검증")]
    static void ValidationMenu(){Debug.Log(StartValidation());}
    [MenuItem("Tools/OVERBURST/CSP/250마리 스트레스 1회")]
    static void StressMenu(){Debug.Log(StartStress());}
    [MenuItem("Tools/OVERBURST/CSP/본인 배치 중단")]
    static void StopMenu(){Stop();}
    public static void Stop()
    {
        if(!Busy)return;plan.status="CANCELLED";plan.reason="user cancellation";Save();
        if(CombatPerformanceSession.Busy&&CombatPerformanceSession.ActiveFolder==plan.active)CombatPerformanceSession.Stop();
        SessionState.EraseString(Key+"folder");
    }
    static void Tick()
    {
        if(!Busy||EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||CombatPerformanceSession.Busy)return;
        try
        {
            if(!string.IsNullOrEmpty(plan.active))
            {
                var returned=Path.Combine(plan.active,"editor-return.json");if(!File.Exists(returned))throw new InvalidOperationException("owned return evidence missing");
                if(JsonUtility.FromJson<ReturnedStatus>(File.ReadAllText(returned))?.status!="PASS")throw new InvalidOperationException("owned return failed");
                var run=CombatPerformanceReport.Read(plan.active);plan.runs.Add(plan.active);plan.active="";plan.index++;Save();
                if(run.status!="COMPLETE")throw new InvalidOperationException("run "+run.id+" "+run.status+": "+run.reason);
            }
            if(plan.index==plan.steps.Count){plan.status="COMPLETE";Save();SessionState.EraseString(Key+"folder");return;}
            if(IsolatedSavePlayGuard.RequiresAccountChoice||!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)))return;
            var s=plan.steps[plan.index];plan.active=CombatSpawnQualityVerifier.Start(s.mode,s.count,s.town,s.combat,s.seconds,s.element);Save();
        }
        catch(Exception error){plan.status="FAILED";plan.reason=error.ToString();Save();SessionState.EraseString(Key+"folder");}
    }
}
