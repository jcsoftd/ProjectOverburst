using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.VFX;
using Unity.Profiling;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class BloodEffectsPackPerformanceProbe
{
    const string Key = "Overburst.BloodEffectsPackPerformanceProbe.";
    static readonly List<string> checks = new List<string>();
    static readonly Stack<IEnumerator> work = new Stack<IEnumerator>();
    static readonly List<Object> owned = new List<Object>();
    static string output;
    static double deadline;
    static int frame, sequence;
    static BloodHitVfxService blood;
    static BloodGroundDecalService ground;
    static CombatHealth player;
    static Camera camera;
    static RenderTexture texture;
    static Texture2D pixels;
    static bool background, backgroundCaptured, randomCaptured;
    static readonly List<object> results = new List<object>();
    static readonly List<ProfilerRecorder> recorders = new List<ProfilerRecorder>();
    static readonly string[] metricNames = { "CPU Main Thread Frame Time", "CPU Render Thread Frame Time", "GPU Frame Time", "Draw Calls Count", "Batches Count", "SetPass Calls Count", "GC Allocated In Frame" };
    static readonly FrameTiming[] timing = new FrameTiming[1];
    static Camera[] priorCameras;
    static bool[] cameraEnabled;
    static Canvas[] priorCanvases;
    static bool[] canvasEnabled;
    static Material fixtureMaterial;
    static UnityEngine.Random.State randomState;
    static int targetRate, offered, spawnIndex, pendingBurst;
    static float nextBurst, emitUntil;
    static bool emitting;
    static BloodHitProfile profile;
    static long lastTimestamp;
    static readonly Vector3 center = new Vector3(5000f, 0f, 5000f);
    public static string Progress { get; private set; } = "idle";
    public static bool IsRunning => !string.IsNullOrEmpty(SessionState.GetString(Key + "return", "")) && !SessionState.GetBool(Key+"returnTimedOut",false);
    static BloodEffectsPackPerformanceProbe()
    {
        EditorApplication.playModeStateChanged += StateChanged;
        if (!string.IsNullOrEmpty(SessionState.GetString(Key + "pending", ""))) EditorApplication.update += AutoStart;
        if (!string.IsNullOrEmpty(SessionState.GetString(Key + "return", "")) && !SessionState.GetBool(Key+"returnTimedOut",false)) EditorApplication.update += Return;
    }
    public static void Start(string directory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) throw new InvalidOperationException("Idle Editor required");
        if (!string.IsNullOrEmpty(SessionState.GetString(Key+"return",""))) throw new InvalidOperationException("Previous return is pending; resume it first.");
        output = IsolatedSavePlayGuard.ValidateDirectory(directory); Directory.CreateDirectory(output);
        SessionState.SetString(Key + "pending",output);
        SessionState.SetString(Key + "return",output);
        SessionState.SetFloat(Key + "returnDeadline",(float)(EditorApplication.timeSinceStartup + 900));
        SessionState.SetString(Key + "startScene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        File.WriteAllText(Path.Combine(output,"edit-before.json"),JsonConvert.SerializeObject(Enumerable.Range(0,SceneManager.sceneCount).Select(i=> {var s=SceneManager.GetSceneAt(i);return new {s.path,s.isDirty};}),Formatting.Indented));
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        EditorApplication.update -= AutoStart; EditorApplication.update += AutoStart;
        SessionState.SetFloat(Key + "deadline",(float)(EditorApplication.timeSinceStartup + 240));
        IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output,"isolated-save"));
    }
    static void StateChanged(PlayModeStateChange state)
    {
        string pending=SessionState.GetString(Key+"pending","");
        if ((state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.EnteredEditMode) && (work.Count > 0 || !string.IsNullOrEmpty(pending)))
        { if(!string.IsNullOrEmpty(pending))output=pending;Finish(new Exception("User or external Play exit"),false); }
        if (state == PlayModeStateChange.EnteredEditMode && !string.IsNullOrEmpty(SessionState.GetString(Key + "return","")))
        { EditorApplication.update -= Return; EditorApplication.update += Return; }
    }
    static void AutoStart()
    {
        string pending = SessionState.GetString(Key + "pending","");
        if (string.IsNullOrEmpty(pending)) {EditorApplication.update -= AutoStart;return;}
        if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "deadline",0))
        {
            output=pending; SessionState.EraseString(Key+"pending"); EditorApplication.update -= AutoStart;
            Finish(new Exception("Startup timeout"),EditorApplication.isPlaying); return;
        }
        if (!EditorApplication.isPlaying || PlayerInputFacade.Current == null || Camera.main == null) return;
        if (!string.Equals(IsolatedSavePlayGuard.ActiveDirectory,Path.Combine(pending,"isolated-save"),StringComparison.OrdinalIgnoreCase)) return;
        output=pending; SessionState.EraseString(Key+"pending"); EditorApplication.update -= AutoStart;
        checks.Clear(); results.Clear(); frame=-1; sequence=100; deadline=EditorApplication.timeSinceStartup+600;
        background=Application.runInBackground; backgroundCaptured=true; Application.runInBackground=true;
        work.Push(Verify()); EditorApplication.update += Tick;
    }
    static void Tick()
    {
        if (Time.frameCount == frame && EditorApplication.timeSinceStartup < deadline) return;
        frame=Time.frameCount;
        try
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline) throw new Exception("Play timeout or exit");
            if (emitting) Spawn();
            while (work.Count>0)
            {
                var item=work.Peek();
                if (!item.MoveNext()) { (work.Pop() as IDisposable)?.Dispose();continue; }
                if (item.Current is IEnumerator nested) {work.Push(nested);continue;}
                return;
            }
            Finish(null,true);
        }
        catch(Exception error) { Finish(error,true); }
    }
    static IEnumerator Frames(int count) {for(int i=0;i<count;i++)yield return null;}
    static IEnumerator Wait(float seconds) {float until=Time.unscaledTime+seconds;while(Time.unscaledTime<until)yield return null;}

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        checks.Add(message);
    }
    static Vector3 Point(int index) => center + new Vector3((index % 8 - 3.5f) * .9f, .8f, (index / 8 - 2.5f) * .9f);
    static void Spawn()
    {
        if (Time.unscaledTime >= emitUntil) return;
        if (pendingBurst == 0 && Time.unscaledTime >= nextBurst)
        {
            nextBurst += 3f; pendingBurst = targetRate; spawnIndex = 0;
        }
        int thisFrame = Mathf.Min(16, pendingBurst);
        for (int i = 0; i < thisFrame; i++)
        {
            int at = spawnIndex++;
            BloodHitVfxService.RequestAt(profile, Point(at), Vector3.right, (CombatImpactShape)(at % 3), 1f, 0, targetId:10000 + at);
            offered++;
        }
        pendingBurst -= thisFrame;
    }
    static void MetricsStart()
    {
        foreach (string name in metricNames)
            recorders.Add(ProfilerRecorder.StartNew(name == "GC Allocated In Frame" ? ProfilerCategory.Memory : ProfilerCategory.Render, name, 1));
    }
    static object Summary(List<double> values)
    {
        if (values.Count == 0) return null;
        values.Sort();
        return new { samples=values.Count, mean=values.Average(), median=values[values.Count/2], p95=values[Math.Min(values.Count-1,(int)(values.Count*.95))] };
    }
    static IEnumerator Measure(string label, int order, bool pack, int concurrency, float seconds)
    {
        var values = metricNames.ToDictionary(n=>n,n=>new List<double>(1200));
        var frameValues = new List<double>(1200); var cpuTiming = new List<double>(1200); var gpuTiming = new List<double>(1200);
        var activeValues = new List<double>(1200); var groundValues = new List<double>(1200);
        int playedBefore=blood.PlayedCount, droppedBefore=blood.DroppedCount, groundBefore=ground.ShownCount;
        offered=0; pendingBurst=0; targetRate=concurrency; emitting=concurrency>0; nextBurst=Time.unscaledTime;
        float begin=Time.unscaledTime, until=begin+seconds;
        emitUntil=until;
        lastTimestamp=0;
        Progress=label + " " + (pack?"B":"A") + " load=" + concurrency;
        File.WriteAllText(Path.Combine(output,"progress.json"),JsonConvert.SerializeObject(new{Progress,order,time=DateTime.UtcNow},Formatting.Indented));
        while(Time.unscaledTime<until)
        {
            yield return null;
            FrameTimingManager.CaptureFrameTimings();
            if (Time.unscaledTime-begin<.25f) continue;
            frameValues.Add(Time.unscaledDeltaTime*1000.0);
            for(int i=0;i<recorders.Count;i++)
            {
                var recorder=recorders[i]; if(!recorder.Valid)continue;
                double value=recorder.LastValue;
                if(i<3){if(value<=0)continue;value/=1000000.0;}
                values[metricNames[i]].Add(value);
            }
            if(FrameTimingManager.GetLatestTimings(1,timing)>0 && (long)timing[0].frameStartTimestamp!=lastTimestamp)
            {
                lastTimestamp=(long)timing[0].frameStartTimestamp;
                if(timing[0].cpuMainThreadFrameTime>0)cpuTiming.Add(timing[0].cpuMainThreadFrameTime);
                if(timing[0].gpuFrameTime>0)gpuTiming.Add(timing[0].gpuFrameTime);
            }
            activeValues.Add(blood.ActiveCount); groundValues.Add(ground.ActiveCount);
        }
        emitting=false; pendingBurst=0;
        var metrics=values.ToDictionary(p=>p.Key,p=>Summary(p.Value));
        results.Add(new{label,order,pack,concurrency,seconds,frames=frameValues.Count,offered,played=blood.PlayedCount-playedBefore,dropped=blood.DroppedCount-droppedBefore,
            newGroundMarks=ground.ShownCount-groundBefore,metrics,frameMs=Summary(frameValues),frameTimingCpuMainMs=Summary(cpuTiming),frameTimingGpuMs=Summary(gpuTiming),activeSprays=Summary(activeValues),activeGround=Summary(groundValues)});
        Check(frameValues.Count>=30,"sufficient measured frames "+Progress);
        if(concurrency>0) Check(blood.PlayedCount>playedBefore,"effects played "+Progress);
        Check(offered==blood.PlayedCount-playedBefore+blood.DroppedCount-droppedBefore,"all offered requests accounted for "+Progress);
        Write(false,null);
    }
    static void Write(bool complete, Exception error)
    {
        File.WriteAllText(Path.Combine(output,"benchmark.json"),JsonConvert.SerializeObject(new{complete,success=complete && error==null,error=error?.ToString(),
            environment=new{Unity=Application.unityVersion,graphics=SystemInfo.graphicsDeviceName,api=SystemInfo.graphicsDeviceType.ToString(),cpu=SystemInfo.processorType,width=1920,height=1080,
                editor=true,frameTimingEnabled=FrameTimingManager.IsFeatureEnabled(),vSync=QualitySettings.vSyncCount,targetFps=Application.targetFrameRate},
            conditions="Current production A size / B 2x size. Fixed camera and same request grid. Prewarmed pools. Bursts every 3 seconds, at most16 requests per frame. Order A-B-B-A at each load. Scene/background Editor work remains part of timings. Captures are outside measured windows.",results,checks},Formatting.Indented));
    }
    static IEnumerator Verify()
    {
        yield return Wait(2f);
        blood=Object.FindFirstObjectByType<BloodHitVfxService>();ground=Object.FindFirstObjectByType<BloodGroundDecalService>();
        Check(blood && ground,"production blood services loaded");
        profile=Resources.Load<BloodHitProfile>("Combat/Blood/SpiderBrood");Check(profile,"blood profile loaded");
        var playerRoot=PlayerInputFacade.Current;
        float worldDeadline=Time.unscaledTime+60f;bool worldReady=false;
        while(Time.unscaledTime<worldDeadline)
        {
            worldReady=Physics.RaycastAll(playerRoot.transform.position+Vector3.forward*2f+Vector3.up*1.6f,Vector3.down,7f,LayerMask.GetMask("Ground","Default"),QueryTriggerInteraction.Ignore)
                .Any(hit=>hit.normal.y>=.7f && hit.collider.GetComponentInParent<CombatHealth>()==null);
            if(worldReady)break;yield return null;
        }
        Check(worldReady,"product world finished loading before benchmark");
        randomState=UnityEngine.Random.state; randomCaptured=true;
        priorCameras=Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);cameraEnabled=priorCameras.Select(c=>c.enabled).ToArray();
        var template=Camera.main;Check(template,"main camera ready");
        var cameraRoot=new GameObject("Owned blood performance camera");owned.Add(cameraRoot);camera=cameraRoot.AddComponent<Camera>();camera.CopyFrom(template);
        foreach(var prior in priorCameras)prior.enabled=false;
        camera.tag="MainCamera";camera.orthographic=true;camera.orthographicSize=5.4f;camera.nearClipPlane=.1f;camera.farClipPlane=100;
        camera.transform.position=center+new Vector3(0,9,-9);camera.transform.LookAt(center+Vector3.up*.4f);
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.13f,.13f,.13f);
        texture=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32);texture.Create();camera.targetTexture=texture;
        camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
        priorCanvases=Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);canvasEnabled=priorCanvases.Select(c=>c.enabled).ToArray();foreach(var canvas in priorCanvases)canvas.enabled=false;
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);owned.Add(floor);floor.name="Owned blood performance ground";floor.transform.position=center-Vector3.up*.05f;floor.transform.localScale=new Vector3(18,.1f,14);
        fixtureMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit")){hideFlags=HideFlags.HideAndDontSave};fixtureMaterial.SetColor("_BaseColor",new Color(.3f,.3f,.3f));floor.GetComponent<Renderer>().sharedMaterial=fixtureMaterial;
        var lightRoot=new GameObject("Owned blood performance light");owned.Add(lightRoot);var light=lightRoot.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1f;light.shadows=LightShadows.None;light.transform.rotation=Quaternion.Euler(65,-30,0);
        GameplayInputBlocker.SetBlocked(cameraRoot,true);
        yield return Frames(5);
        BloodHitVfxService.SetUniformRed(true);
        var clock=System.Diagnostics.Stopwatch.StartNew();Check(BloodHitVfxService.SetPackEnabled(true),"B pool ready");clock.Stop();
        File.WriteAllText(Path.Combine(output,"first-switch.json"),JsonConvert.SerializeObject(new{firstBPrepareMs=clock.Elapsed.TotalMilliseconds,includes="CPU pool preparation only; no GPU cold shader timing"},Formatting.Indented));
        foreach(bool pack in new[]{false,true})
        {
            BloodHitVfxService.SetPackEnabled(pack);
            for(int i=0;i<48;i++)
            {
                BloodHitVfxService.RequestAt(profile,Point(i),Vector3.right,(CombatImpactShape)(i%3),1f,0,targetId:10000+i);
                if(i%16==15)yield return Frames(1);
            }
            yield return Wait(3f);
        }
        MetricsStart();
        foreach(int load in new[]{1,24,48})
        {
            BloodHitVfxService.SetPackEnabled(false);yield return Frames(10);
            yield return Measure("baseline",-1,false,0,3f);
            int order=0;
            foreach(bool pack in new[]{false,true,true,false})
            {
                BloodHitVfxService.SetPackEnabled(pack);UnityEngine.Random.InitState(8341);
                yield return Frames(10);
                yield return Measure("blood",order++,pack,load,6f);
                if(order<=2)
                {
                    BloodHitVfxService.SetPackEnabled(pack);
                    for(int i=0;i<Mathf.Min(load,16);i++)BloodHitVfxService.RequestAt(profile,Point(i),Vector3.right,(CombatImpactShape)(i%3),1f,0,targetId:10000+i);
                    yield return Frames(4);
                    pixels=new Texture2D(1920,1080,TextureFormat.RGB24,false);
                    var priorActive=RenderTexture.active;
                    try{camera.Render();RenderTexture.active=texture;pixels.ReadPixels(new Rect(0,0,1920,1080),0,0);pixels.Apply();File.WriteAllBytes(Path.Combine(output,(pack?"B":"A")+"_"+load+".png"),pixels.EncodeToPNG());}
                    finally{RenderTexture.active=priorActive;Object.DestroyImmediate(pixels);pixels=null;}
                }
            }
        }
        BloodHitVfxService.SetPackEnabled(false);BloodHitVfxService.SetUniformRed(false);
        Progress="completed";
    }
    static void ReleaseFixture()
    {
        emitting=false;
        foreach(var recorder in recorders)recorder.Dispose();recorders.Clear();
        if(priorCameras!=null)for(int i=0;i<priorCameras.Length;i++)if(priorCameras[i])priorCameras[i].enabled=cameraEnabled[i];
        if(priorCanvases!=null)for(int i=0;i<priorCanvases.Length;i++)if(priorCanvases[i])priorCanvases[i].enabled=canvasEnabled[i];
        if(camera)GameplayInputBlocker.Unblock(camera.gameObject);
        if(fixtureMaterial){Object.DestroyImmediate(fixtureMaterial);fixtureMaterial=null;}
        if(randomCaptured){UnityEngine.Random.state=randomState;randomCaptured=false;}
    }
    static void Finish(Exception error,bool exit)
    {
        EditorApplication.update-=Tick;EditorApplication.update-=AutoStart;
        try
        {
            while(work.Count>0)
            {
                try{(work.Pop() as IDisposable)?.Dispose();}
                catch(Exception cleanup){error=error??cleanup;Debug.LogException(cleanup);}
            }
            Write(true,error);
        }
        catch(Exception report){error=error??report;Debug.LogException(report);}
        finally
        {
            try
            {
                ReleaseFixture();
                if(camera!=null)camera.targetTexture=null;
                if(texture!=null){texture.Release();Object.DestroyImmediate(texture);texture=null;}
                if(pixels!=null){Object.DestroyImmediate(pixels);pixels=null;}
                foreach(var item in owned)if(item!=null)Object.DestroyImmediate(item);owned.Clear();
            }
            catch(Exception cleanup){error=error??cleanup;Debug.LogException(cleanup);}
            finally
            {
                if(backgroundCaptured){Application.runInBackground=background;backgroundCaptured=false;}
                SessionState.EraseString(Key+"pending");SessionState.EraseFloat(Key+"deadline");
                try{File.WriteAllText(Path.Combine(output,"play-result.json"),JsonConvert.SerializeObject(new {success=error==null,checks,error=error?.ToString()},Formatting.Indented));}
                catch(Exception report){Debug.LogException(report);}
                string ownedAccount=Path.Combine(output,"isolated-save");
                if(exit && EditorApplication.isPlaying && string.Equals(IsolatedSavePlayGuard.ActiveDirectory,ownedAccount,StringComparison.OrdinalIgnoreCase))
                    EditorApplication.ExitPlaymode();
                if(!EditorApplication.isPlayingOrWillChangePlaymode){EditorApplication.update-=Return;EditorApplication.update+=Return;}
            }
        }
    }
    [MenuItem("OVERBURST/테스트/혈흔 A·B 측정 복귀 재시도")]
    public static void ResumeReturn()
    {
        if(string.IsNullOrEmpty(SessionState.GetString(Key+"return","")))return;
        SessionState.EraseBool(Key+"returnTimedOut");
        SessionState.SetFloat(Key+"returnDeadline",(float)(EditorApplication.timeSinceStartup+900));
        EditorApplication.update-=Return;EditorApplication.update+=Return;
        Progress="return pending";
    }

    static void Return()
    {
        string path=SessionState.GetString(Key+"return","");
        if(string.IsNullOrEmpty(path)){EditorApplication.update-=Return;return;}
        if(EditorApplication.timeSinceStartup>SessionState.GetFloat(Key+"returnDeadline",0))
        {
            EditorApplication.update-=Return;
            SessionState.SetBool(Key+"returnTimedOut",true);
            Progress="return timeout; ResumeReturn is available";
            try{File.WriteAllText(Path.Combine(path,"return-result.json"),"{\"success\":false,\"reason\":\"idle return timeout; ResumeReturn available\"}");}
            catch(Exception report){Debug.LogException(report);}
            return;
        }
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)return;
        string current=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)??"";
        string ownedAccount=Path.Combine(path,"isolated-save"),prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","");
        if(!string.IsNullOrEmpty(current) && !string.Equals(Path.GetFullPath(current),Path.GetFullPath(ownedAccount),StringComparison.OrdinalIgnoreCase))return;
        if(!string.IsNullOrEmpty(prepared) && !string.Equals(prepared,ownedAccount,StringComparison.OrdinalIgnoreCase))return;
        if(!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory))return;
        string startScene=SessionState.GetString(Key+"startScene","");
        EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(startScene)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(startScene);
        IsolatedSavePlayGuard.UseRealAccount();
        SessionState.EraseString(Key+"return");SessionState.EraseString(Key+"startScene");SessionState.EraseFloat(Key+"returnDeadline");SessionState.EraseBool(Key+"returnTimedOut");EditorApplication.update-=Return;
        File.WriteAllText(Path.Combine(path,"return-result.json"),JsonConvert.SerializeObject(new {success=!IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)),guardActive=IsolatedSavePlayGuard.ActiveDirectory,prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""),expires=SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires",""),startScene=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),scenes=Enumerable.Range(0,SceneManager.sceneCount).Select(i=>{var s=SceneManager.GetSceneAt(i);return new{s.path,s.isDirty};})},Formatting.Indented));
    }
}
