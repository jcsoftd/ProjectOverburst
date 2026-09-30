using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Unity.Profiling;
using UnityEngine;
using UnityEditor;

public sealed class ChainBatchPerfProbe : ScriptableObject
{
    public static string Progress="idle";
    static ChainBatchPerfProbe active;
    public static bool IsRunning => active != null;
    GameObject gameObject;
    Transform transform => gameObject.transform;
    IEnumerator routine;
    int previousFrame = -1;
    static void AdvanceEditor()
    {
        if(active==null)return;
        if(!Application.isPlaying){DestroyImmediate(active);return;}
        if(active.previousFrame==Time.frameCount)return;
        active.previousFrame=Time.frameCount;
        try { active.Update(); if(!active.routine.MoveNext()) DestroyImmediate(active); }
        catch(Exception e){Progress="FAIL "+e;DestroyImmediate(active);}
    }
    GameObject prefab, actors;
    Camera camera;
    RenderTexture target;
    Camera[] otherCameras;
    bool[] previousEnabled;
    bool oldBackground, oldBatch;
    readonly List<ChainElectricityLiteVfxController> originals=new List<ChainElectricityLiteVfxController>();
    readonly List<string> rows=new List<string>();
    int count; bool batchMode, running;
    float nextSpawn;
    ProfilerRecorder originalCost,batchCost,drawCalls,gc;
    readonly FrameTiming[] timing=new FrameTiming[1];
    public static void Begin()
    {
        if(!Application.isPlaying)throw new Exception("Play required");
        if(active!=null)throw new Exception("probe already live");
        active=CreateInstance<ChainBatchPerfProbe>();active.gameObject=new GameObject("ChainBatchPerfProbe");active.routine=active.Start();
        EditorApplication.update+=AdvanceEditor;
    }
    IEnumerator Start()
    {
        oldBackground=Application.runInBackground;Application.runInBackground=true;
        oldBatch=ChainElectricityBatchRenderer.Enabled;
        otherCameras=UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        previousEnabled=new bool[otherCameras.Length];for(int i=0;i<otherCameras.Length;i++){previousEnabled[i]=otherCameras[i].enabled;otherCameras[i].enabled=false;}
        prefab=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath("6a732a6fd9d7cfd409ddc5d0b20337a8"));
        actors=new GameObject("originals");actors.transform.SetParent(transform);
        for(int i=0;i<200;i++)
        {
            Vector3 from=From(i),to=from+new Vector3(3,0,.5f);
            var g=Instantiate(prefab,(from+to)*.5f,Quaternion.LookRotation(to-from),actors.transform);
            g.transform.localScale=new Vector3(1,1,(to-from).magnitude);
            originals.Add(g.GetComponentInChildren<ChainElectricityLiteVfxController>(true));g.SetActive(false);
        }
        var cg=new GameObject("quarter view render camera");cg.transform.SetParent(transform);camera=cg.AddComponent<Camera>();
        camera.orthographic=true;camera.orthographicSize=11;camera.transform.position=new Vector3(5000,20,4980);camera.transform.LookAt(new Vector3(5000,0,5000));
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.012f,.018f,.028f);camera.farClipPlane=100;
        target=new RenderTexture(1920,1080,24);camera.targetTexture=target;
        originalCost=ProfilerRecorder.StartNew(ProfilerCategory.Scripts,"Overburst.Chain.Original.Update",1);
        batchCost=ProfilerRecorder.StartNew(ProfilerCategory.Scripts,"Overburst.Chain.Batch.Update",1);
        drawCalls=ProfilerRecorder.StartNew(ProfilerCategory.Render,"Draw Calls Count",1);
        gc=ProfilerRecorder.StartNew(ProfilerCategory.Memory,"GC Allocated In Frame",1);
        rows.Add("Isolated original-vs-GPU link fixture. 1920x1080 RT, orthographic quarter view. No combat AI. Editor frame timings include editor overhead.");
        rows.Add(SystemInfo.graphicsDeviceName+" / "+SystemInfo.processorType);
        foreach(int n in new[]{50,100,200})foreach(bool useBatch in new[]{false,true})
        {
            count=n;batchMode=useBatch;ChainElectricityBatchRenderer.Enabled=useBatch;
            for(int i=0;i<200;i++)originals[i].transform.parent.parent.gameObject.SetActive(!useBatch&&i<n);
            running=true;nextSpawn=Time.time;
            Progress="warming "+n+" "+(useBatch?"GPU":"original");
            for(int i=0;i<60;i++)yield return null;
            var costs=new List<double>(120);var frames=new List<double>(120);var draws=new List<double>(120);var allocations=new List<double>(120);var gpuTimes=new List<double>(120);
            Progress="measuring "+n+" "+(useBatch?"GPU":"original");
            for(int i=0;i<120;i++)
            {
                yield return null;
                var recorder=useBatch?batchCost:originalCost;
                if(recorder.Valid)costs.Add(recorder.LastValue/1000000.0);
                frames.Add(Time.unscaledDeltaTime*1000.0);
                if(drawCalls.Valid)draws.Add(drawCalls.LastValue);
                if(gc.Valid)allocations.Add(gc.LastValue);
                FrameTimingManager.CaptureFrameTimings();if(FrameTimingManager.GetLatestTimings(1,timing)>0&&timing[0].gpuFrameTime>0)gpuTimes.Add(timing[0].gpuFrameTime);
            }
            rows.Add(n+" "+(useBatch?"GPU":"original")+" updateCpuMs="+Summary(costs)+" editorFrameMs="+Summary(frames)+" drawCalls="+Summary(draws)+" GCBytes="+Summary(allocations)+" GPUms="+Summary(gpuTimes));
            running=false;for(int i=0;i<200;i++)originals[i].StopAndClearVfx();
            for(int i=0;i<25;i++)yield return null;
        }
        string path=Path.GetFullPath("../개인파일/코덱스산출/Combat/ElementStatusGoal20260927/ChainBatchPerformance.txt");
        File.WriteAllLines(path,rows);Progress="complete: "+path;
        Destroy(gameObject);
    }
    static string Summary(List<double> values)
    {
        if(values.Count==0)return "UNAVAILABLE";values.Sort();double sum=0;foreach(double n in values)sum+=n;
        return "avg "+(sum/values.Count).ToString("F4")+" p95 "+values[Math.Min(values.Count-1,(int)(values.Count*.95))].ToString("F4");
    }
    static Vector3 From(int i)=>new Vector3(4992+(i%10)*1.4f,0,4993+(i/10)*.7f);
    void Update()
    {
        if(!running||Time.time<nextSpawn)return;nextSpawn=Time.time+.3f;
        for(int i=0;i<count;i++)if(batchMode)
        {
            Vector3 from=From(i);if(!ChainElectricityBatchRenderer.TrySpawn(prefab,from,from+new Vector3(3,0,.5f)))throw new Exception("batch spawn failed");
        }
        else originals[i].RestartVfx();
    }
    void OnDestroy()
    {
        EditorApplication.update-=AdvanceEditor; if(active==this)active=null;
        running=false;originalCost.Dispose();batchCost.Dispose();drawCalls.Dispose();gc.Dispose();
        ChainElectricityBatchRenderer.Enabled=oldBatch;Application.runInBackground=oldBackground;
        if(otherCameras!=null)for(int i=0;i<otherCameras.Length;i++)if(otherCameras[i]!=null)otherCameras[i].enabled=previousEnabled[i];
        if(camera!=null)camera.targetTexture=null;if(target!=null){target.Release();DestroyImmediate(target);}
        if(gameObject!=null)DestroyImmediate(gameObject);
    }
}
