using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

public static partial class MonsterBodyContactVerifier
{
    const string Key="MonsterBodyContactVerifier.";
    static IEnumerator routine;
    static int lastFrame,playChecks;
    static double deadline;
    static readonly List<string> playErrors=new List<string>();
    static readonly List<object> playRows=new List<object>();
    static string PlayFolder=>SessionState.GetString(Key+"output","");
    static MonsterBodyContactVerifier()
    {
        EditorApplication.playModeStateChanged+=State;
        if(SessionState.GetBool(Key+"return",false))ScheduleReturn();
        else if(SessionState.GetBool(Key+"pending",false) && !EditorApplication.isPlayingOrWillChangePlaymode){SessionState.SetBool(Key+"return",true);ScheduleReturn();}
    }
    static string Scenes()=>JsonConvert.SerializeObject(Enumerable.Range(0,UnityEngine.SceneManagement.SceneManager.sceneCount).Select(i=>{var s=UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);return new{s.path,s.isDirty,s.rootCount};}));
    static bool OwnAccount()=>!string.IsNullOrEmpty(PlayFolder) && string.Equals(IsolatedSavePlayGuard.ActiveDirectory,Path.Combine(PlayFolder,"Account"),StringComparison.OrdinalIgnoreCase);
    public static string StartPlay(string directory)
    {
        MonsterBodyContactBuilder.RequireIdle();Assert(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY")),"Unoccupied settings required");
        Assert(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name=="PersistentScene","PersistentScene required");
        string folder=MonsterBodyContactBuilder.Output(directory);Assert(!SessionState.GetBool(Key+"pending",false),"Verifier already pending");
        SessionState.SetString(Key+"output",folder);SessionState.SetString(Key+"scenes",Scenes());SessionState.SetString(Key+"start",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(Key+"pending",true);SessionState.SetBool(Key+"return",false);SessionState.SetString(Key+"deadline",(EditorApplication.timeSinceStartup+600).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(Path.Combine(folder,"Settings"));Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY",Path.Combine(folder,"Settings"));
        File.WriteAllText(Path.Combine(folder,"play.json"),JsonConvert.SerializeObject(new{status="RUNNING",stage="boot"}));
        try{IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(folder,"Account"));return "STARTED owned isolated Play";}
        catch{SessionState.SetBool(Key+"return",true);ScheduleReturn();throw;}
    }
    static void State(PlayModeStateChange state)
    {
        if(!SessionState.GetBool(Key+"pending",false))return;
        if(state==PlayModeStateChange.EnteredPlayMode){
            playChecks=0;playErrors.Clear();playRows.Clear();lastFrame=-1;deadline=EditorApplication.timeSinceStartup+540;
            SessionState.SetBool(Key+"background",Application.runInBackground);Application.runInBackground=true;
            routine=VerifyPlay();Application.logMessageReceived+=Log;EditorApplication.update+=Tick;
        }
        if(state==PlayModeStateChange.ExitingPlayMode){
            EditorApplication.update-=Tick;Application.logMessageReceived-=Log;(routine as IDisposable)?.Dispose();routine=null;
            Application.runInBackground=SessionState.GetBool(Key+"background",false);
            if(!SessionState.GetBool(Key+"return",false)){playErrors.Add("Interrupted before completion");WritePlay("FAIL");SessionState.SetBool(Key+"return",true);}
        }
        if(state==PlayModeStateChange.EnteredEditMode)ScheduleReturn();
    }
    static void Log(string message,string stack,LogType type){if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert)playErrors.Add(message+"\n"+stack);}
    static void Check(bool value,string message){Assert(value,message);playChecks++;}
    static void WritePlay(string status){if(!string.IsNullOrEmpty(PlayFolder))File.WriteAllText(Path.Combine(PlayFolder,"play.json"),JsonConvert.SerializeObject(new{status,checks=playChecks,rows=playRows,errors=playErrors},Formatting.Indented));}
    static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();if(!EditorApplication.isPlaying || lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
        try{Assert(OwnAccount(),"Owned account required");Assert(EditorApplication.timeSinceStartup<deadline,"Runtime timeout");if(routine.MoveNext())return;
            Check(playErrors.Count==0,"No runtime exceptions");Finish("PASS");}
        catch(Exception e){playErrors.Add(e.ToString());Finish("FAIL");}
    }
    static void Finish(string status)
    {
        EditorApplication.update-=Tick;(routine as IDisposable)?.Dispose();routine=null;WritePlay(status);SessionState.SetBool(Key+"return",true);
        if(OwnAccount() && EditorApplication.isPlayingOrWillChangePlaymode)EditorApplication.ExitPlaymode();ScheduleReturn();
    }
    static object Field(object value,string name)=>value.GetType().GetField(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).GetValue(value);
    static Vector3[] Queued(BloodHitVfxService blood,int id)
    {
        var queue=(Array)Field(blood,"queue");int count=(int)Field(blood,"queued");var points=new List<Vector3>();
        for(int i=0;i<count;i++){var pending=queue.GetValue(i);if((int)Field(pending,"Target")==id)points.Add((Vector3)Field(pending,"Position"));}return points.ToArray();
    }
    static Vector3 RequestHit(EnemyActor actor,GameObject source,int sequence,Vector3 direction)
    {
        var target=actor.GetComponent<CombatTarget>();Vector3 raw=target.CurrentHurtVolume.Center+Vector3.up*20;
        Vector3 expected=CombatTargetVfxPlacement.ResolveContact(target,raw,direction,out _);
        var blood=Object.FindFirstObjectByType<BloodHitVfxService>();int queuedBefore=Queued(blood,actor.Health.GetInstanceID()).Length;
        CombatHitFeedbackService.Request(new CombatHitFeedbackRequest(source,sequence,null,false,WeaponElement.Fire,raw,false,worldDirection:direction,target:actor.Health,impactDirection:direction));
        bool suppress=actor.GetComponent<BloodHitTarget>().Profile.suppressBlood;
        var points=Queued(blood,actor.Health.GetInstanceID());
        Check(suppress?points.Length==queuedBefore:points.Length==queuedBefore+1,"Blood suppression/queue "+actor.Definition.EnemyId);
        if(!suppress)Check((points.Last()-expected).sqrMagnitude<.000001f,"Queued blood uses skin contact "+actor.Definition.EnemyId);
        Check(Object.FindObjectsByType<MeleeElementHitVfxController>(FindObjectsInactive.Exclude,FindObjectsSortMode.None)
            .Any(v=>(v.transform.position-expected).sqrMagnitude<.0001f),"Element VFX uses same skin contact "+actor.Definition.EnemyId);
        return expected;
    }
    static void CaptureRuntime(Camera camera,string file)
    {
        RenderTexture rt=null;Texture2D pixels=null;var previous=camera.targetTexture;var active=RenderTexture.active;
        try{rt=new RenderTexture(800,600,24,RenderTextureFormat.ARGB32);rt.Create();pixels=new Texture2D(800,600,TextureFormat.RGB24,false);camera.targetTexture=rt;camera.Render();
            RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,800,600),0,0);pixels.Apply();Directory.CreateDirectory(Path.GetDirectoryName(file));File.WriteAllBytes(file,pixels.EncodeToPNG());}
        finally{camera.targetTexture=previous;RenderTexture.active=active;if(pixels)Object.Destroy(pixels);if(rt){rt.Release();Object.Destroy(rt);}}
    }
    static IEnumerator VerifyPlay()
    {
        EnemyActor current=null;EnemySpawnService spawn=null;EnemyThemeTrialHarness arena=null;GameObject cameraRoot=null;Camera previous=null;PlayableGraph graph=default;
        try{
            while(PersistentSceneFlow.Instance==null || PersistentSceneFlow.Instance.IsSwitching || PersistentSceneFlow.Instance.CurrentSubSceneName!="HideoutScene" || PlayerInputFacade.Current==null)yield return null;
            var player=PlayerInputFacade.Current;arena=EnemyThemeTrialHarness.Current;Check(arena!=null,"Arena harness available");if(!arena.InArena)arena.ToggleArena();
            int ready=Time.frameCount+10;while(Time.frameCount<ready)yield return null;
            Check(EnemyDebugSpawnRuntimeContext.TryGetSpawnService(player.transform,out spawn),"Real spawn/pool service");
            foreach(var catalog in Resources.LoadAll<EnemyCatalog>("Enemies/Bosses"))spawn.RegisterAdditionalCatalog(catalog,out _);
            previous=Camera.main;previous.tag="Untagged";cameraRoot=new GameObject("Owned torso VFX camera");var camera=cameraRoot.AddComponent<Camera>();camera.CopyFrom(previous);camera.tag="MainCamera";camera.enabled=true;
            camera.orthographic=true;camera.orthographicSize=10;camera.cullingMask=-1;camera.nearClipPlane=.01f;camera.farClipPlane=200;
            Vector3 spot=player.transform.position+Vector3.forward*8;
            var blood=Object.FindFirstObjectByType<BloodHitVfxService>();Check(blood!=null,"Real blood service");MeleeElementHitVfxService.PrepareForElement(WeaponElement.Fire);
            ready=Time.frameCount+20;while(Time.frameCount<ready)yield return null;
            var definitions=MonsterBodyContactBuilder.Definitions();int sequence=10000;
            var baseline=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(PlayFolder,"baseline.json")))["rows"];
            foreach(var d in definitions){
                for(int lease=0;lease<2;lease++){
                    Check(spawn.TrySpawn(new EnemySpawnRequest(d,spot,Quaternion.identity,player.transform),out current),"Pool spawn "+d.EnemyId);
                    current.AI.enabled=false;current.Movement.StopMovement();current.Health.SetMaxHp(100000,true);
                    var placement=current.GetComponent<CombatTargetVfxPlacement>();Check(placement.HasBodyContacts,"Leased skin references "+d.EnemyId);
                    graph=MonsterBodyContactBuilder.Pose(current,d.AnimationProfile.Idle,0);
                    Vector3 center=placement.BodyContactCenter;camera.transform.position=center+new Vector3(6,8,10);camera.transform.LookAt(center);
                    var body=baseline.Single(r=>(string)r["id"]==d.EnemyId);var size=MonsterBodyContactBuilder.Vector(body["meshSize"]);
                    camera.orthographicSize=Mathf.Max(.5f,Mathf.Max(size.y,Mathf.Max(size.x,size.z)*.8f)*.72f);
                    int end=Time.frameCount+2;while(Time.frameCount<end)yield return null;
                    var styles=lease==0?new[]{BloodEffectStyle.Legacy,BloodEffectStyle.EffectsPack,BloodEffectStyle.Volumetric}:new[]{BloodEffectStyle.Volumetric};
                    foreach(var style in styles){
                        Check(BloodHitVfxService.SetStyle(style),"ABC pool ready "+d.EnemyId+" "+style);
                        int before=blood.PlayedCount;Vector3 emittedAt=RequestHit(current,player.gameObject,sequence++,Vector3.back);
                        end=Time.frameCount+3;while(Time.frameCount<end)yield return null;
                        bool suppress=current.GetComponent<BloodHitTarget>().Profile.suppressBlood;
                        Check(suppress || blood.PlayedCount>before,"Actual blood pool emission "+d.EnemyId+" "+style);
                        // A world-space splash stays at the contact at emission. The target can
                        // settle on the ground or animate during the following three frames.
                        if(!suppress)Check(blood.transform.Cast<Transform>().Any(t=>t.gameObject.activeSelf && (t.position-emittedAt).sqrMagnitude<.000001f),"Actual leased spray origin "+d.EnemyId+" "+style);
                        if(lease==0)CaptureRuntime(camera,Path.Combine(PlayFolder,"Runtime",d.EnemyId+"_"+style+".png"));
                        var profile=current.GetComponent<BloodHitTarget>().Profile;
                        int dropsBefore=blood.PackPlayedCount;
                        bool dropped=BloodHitVfxService.RequestBleed(current.Health,profile,current.transform.position,Vector3.forward);
                        Check(dropped==!profile.suppressBlood,"Bleed suppression "+d.EnemyId+" "+style);
                        if(!profile.suppressBlood && style!=BloodEffectStyle.Legacy){
                            Check(blood.PackPlayedCount>dropsBefore,"Real drop pool "+d.EnemyId+" "+style);
                            Check(blood.transform.Cast<Transform>().Any(t=>t.gameObject.activeSelf && (t.position-placement.BodyContactCenter).sqrMagnitude<.000001f),"Drops start in torso "+d.EnemyId+" "+style);
                        }
                    }
                    graph.Destroy();graph=default;
                    var pose=d.AnimationProfile.StunnedLoop?d.AnimationProfile.StunnedLoop:d.AnimationProfile.Hit;
                    graph=MonsterBodyContactBuilder.Pose(current,pose,.45);
                    camera.transform.LookAt(placement.BodyContactCenter);RequestHit(current,player.gameObject,sequence++,Vector3.right);
                    end=Time.frameCount+3;while(Time.frameCount<end)yield return null;
                    Vector3 deathCenter=placement.BodyContactCenter;
                    current.Health.TakeDamage(new DamageInfo(1000000,deathCenter,player.gameObject,Vector3.back));
                    Check(current.Health.IsDead,"Actual lethal damage "+d.EnemyId);
                    var deaths=Queued(blood,current.Health.GetInstanceID());bool noBlood=current.GetComponent<BloodHitTarget>().Profile.suppressBlood;
                    Check(noBlood || deaths.Length>0 && deaths.All(p=>(p-deathCenter).sqrMagnitude<.0001f),"All lethal jets start in animated torso "+d.EnemyId);
                    graph.Destroy();graph=default;spawn.Release(current);current=null;
                    end=Time.frameCount+2;while(Time.frameCount<end)yield return null;
                }
                playRows.Add(new{id=d.EnemyId,leases=2,bloodStyles=3,posedHit=true,bleed=true,lethal=true});WritePlay("RUNNING");
            }
        }finally{if(graph.IsValid())graph.Destroy();if(current && current.IsLeased && spawn)spawn.Release(current);if(cameraRoot)Object.Destroy(cameraRoot);if(previous)previous.tag="MainCamera";if(arena && arena.InArena)arena.ToggleArena();}
    }
    static void ScheduleReturn(){EditorApplication.update-=Return;EditorApplication.update+=Return;}
    static void Return()
    {
        string folder=PlayFolder;if(string.IsNullOrEmpty(folder)){EditorApplication.update-=Return;return;}
        double limit=double.Parse(SessionState.GetString(Key+"deadline","0"),System.Globalization.CultureInfo.InvariantCulture);
        if(EditorApplication.timeSinceStartup>limit+180){File.WriteAllText(Path.Combine(folder,"return.json"),JsonConvert.SerializeObject(new{status="BLOCKED",reason="Return deadline; shared state preserved"}));EditorApplication.update-=Return;return;}
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)return;
        string account=Path.Combine(folder,"Account"),settings=Path.Combine(folder,"Settings");
        foreach(string value in new[]{IsolatedSavePlayGuard.ActiveDirectory,Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","")})
            if(!string.IsNullOrEmpty(value) && !string.Equals(value,account,StringComparison.OrdinalIgnoreCase))return;
        string currentSettings=Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY");if(!string.IsNullOrEmpty(currentSettings) && currentSettings!=settings)return;
        try{
            IsolatedSavePlayGuard.UseRealAccount();Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY",null);
            bool ready=!IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
                && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
                && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""))
                && string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires",""));
            bool scenes=Scenes()==SessionState.GetString(Key+"scenes","");bool start=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)==SessionState.GetString(Key+"start","");
            File.WriteAllText(Path.Combine(folder,"return.json"),JsonConvert.SerializeObject(new{status=ready && scenes && start?"PASS":"FAIL",ready,scenesPreserved=scenes,startPreserved=start,scenes=Scenes(),pid=System.Diagnostics.Process.GetCurrentProcess().Id,compilationFailed=EditorUtility.scriptCompilationFailed},Formatting.Indented));
            foreach(string suffix in new[]{"output","scenes","start","deadline"})SessionState.EraseString(Key+suffix);
            SessionState.EraseBool(Key+"pending");SessionState.EraseBool(Key+"return");SessionState.EraseBool(Key+"background");EditorApplication.update-=Return;
        }catch(Exception e){File.WriteAllText(Path.Combine(folder,"return-error.txt"),e.ToString());EditorApplication.update-=Return;}
    }
}
