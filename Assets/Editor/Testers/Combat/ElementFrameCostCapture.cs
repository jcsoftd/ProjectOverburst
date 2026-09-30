using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;

// Bounded preallocated frame capture; writes only after sampling has stopped.
public static class ElementFrameCostCapture
{
    const int Capacity=40000;
    static readonly List<ProfilerRecorder> recorders=new List<ProfilerRecorder>();
    static readonly List<string> names=new List<string>();
    static readonly List<string> stages=new List<string>();
    static double[,] values;
    static int[] stageIds;
    static int count,lastFrame,stage;
    static double previous;
    static bool running;
    public static string Begin()
    {
        if(!Application.isPlaying||running)throw new Exception("idle Play capture required");
        names.Clear();recorders.Clear();stages.Clear();
        string[] wanted={"Main Thread","PlayerLoop","EditorLoop","GC.Collect","Destroy","Overburst.ElementHit.Maintenance","Overburst.TransientVfx.Create","Overburst.Cost.Pool.Release","Overburst.Cost.Pool.Acquire","Overburst.Cost.Heavy.PreparationRequest","ParticleSystem.UpdateJob","ParticleSystem.GeometryJob","ParticleSystem.WaitForPreviousRenderingToFinish","Gfx.WaitForPresentOnGfxThread","Gfx.WaitForRenderThread","WaitForTargetFPS","WaitForLastPresentation","Animators.Update","Physics.Simulate","GC Allocated In Frame"};
        var handles=new List<ProfilerRecorderHandle>();ProfilerRecorderHandle.GetAvailable(handles);
        foreach(var handle in handles)
        {
            var d=ProfilerRecorderHandle.GetDescription(handle);
            if(!wanted.Contains(d.Name))continue;
            names.Add(d.Name);recorders.Add(ProfilerRecorder.StartNew(d.Category,d.Name,1));
        }
        values=new double[Capacity,6+names.Count];stageIds=new int[Capacity];
        count=0;stage=-1;lastFrame=-1;previous=EditorApplication.timeSinceStartup;
        running=true;EditorApplication.update+=Sample;AssemblyReloadEvents.beforeAssemblyReload+=BeforeReload;
        return "Recording "+string.Join(",",names);
    }
    static void BeforeReload()=>Stop();
    static void Sample()
    {
        if(!Application.isPlaying||count>=Capacity){Stop();return;}
        if(lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
        string current=ElementNaturalChargeProbe.Progress;
        if(stage<0||stages[stage]!=current){stages.Add(current);stage=stages.Count-1;}
        double now=EditorApplication.timeSinceStartup;
        stageIds[count]=stage;values[count,0]=Time.frameCount;values[count,1]=Time.unscaledDeltaTime*1000d;
        values[count,2]=(now-previous)*1000d;values[count,3]=Time.timeScale;
        values[count,4]=MeleeElementPoolMaintenance.LastOperations;values[count,5]=Time.time;
        previous=now;
        for(int i=0;i<recorders.Count;i++)values[count,6+i]=recorders[i].LastValue/(names[i]=="GC Allocated In Frame"?1d:1000000d);
        count++;
    }
    public static string Stop()
    {
        if(!running)return "not running";
        running=false;EditorApplication.update-=Sample;AssemblyReloadEvents.beforeAssemblyReload-=BeforeReload;
        foreach(var recorder in recorders)recorder.Dispose();
        string path=Path.GetFullPath("../개인파일/코덱스산출/Combat/ElementStatusGoal20260927/ChargeFrameCosts.csv");
        using(var file=new StreamWriter(path,false))
        {
            file.WriteLine("stage,frame,unscaledDeltaMs,editorCallbackMs,timeScale,maintenanceOperations,gameTime,"+string.Join(",",names));
            for(int row=0;row<count;row++)
            {
                file.Write(stages[stageIds[row]].Replace(',',';'));
                for(int col=0;col<6+names.Count;col++){file.Write(',');file.Write(values[row,col].ToString("F5",CultureInfo.InvariantCulture));}
                file.WriteLine();
            }
        }
        values=null;stageIds=null;return path+" rows="+count;
    }
}
