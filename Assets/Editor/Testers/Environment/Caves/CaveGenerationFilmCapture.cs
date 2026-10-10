using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Caves;
using UnityEditor;
using UnityEditor.Media;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// A presentation capture of the shipping runtime generator; it never saves a generated map.
[InitializeOnLoad]
public static partial class CaveGenerationFilmCapture
{
    const string Key="Overburst.CaveFilm.";
    const int Width=2560,Height=1440,Fps=60;
    static string output;
    static IEnumerator routine;
    static AsyncOperation waiting;
    static Camera camera;
    static RenderTexture target;
    static Texture2D pixels;
    static MediaEncoder encoder;
    static int frames;
    static bool priorBackground,recordGeneration;
    static double takeStarted,nextSample;
    static CaveWorld world;
    static GameObject temporaryLight;
    static readonly List<object> maps=new List<object>();
    static readonly List<object> stages=new List<object>();
    static readonly List<string> errors=new List<string>();
    static CaveGenerationFilmCapture(){if(SessionState.GetString(Key+"output","")!="")EditorApplication.update+=Tick;}
    static void Write(string file,object data)=>File.WriteAllText(Path.Combine(output,file),JsonConvert.SerializeObject(data,Formatting.Indented));
    public static void Start(string directory, string completedTake = null)
    {
        CavePlatformMapBuilder.Guard();
        var stage=PrefabStageUtility.GetCurrentPrefabStage();
        if(stage!=null&&stage.scene.isDirty)throw new InvalidOperationException("Save the edited prefab before filming.");
        output=Path.GetFullPath(directory);Directory.CreateDirectory(output);Directory.CreateDirectory(Path.Combine(output,"Raw"));
        if(File.Exists(Path.Combine(output,"capture.json")))throw new InvalidOperationException("Use a new take directory.");
        if (!string.IsNullOrEmpty(completedTake))
        {
            var prior = Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(Path.Combine(completedTake,"maps.json")));
            if (prior.Count != 9) throw new InvalidOperationException("Resume expects nine completed captures.");
            for (int i=0;i<prior.Count;i++)
            {
                foreach (string suffix in new[]{"-overview.mp4","-detail.mp4",".png"})
                    File.Copy(Path.Combine(completedTake,"Raw","map-"+i+suffix),Path.Combine(output,"Raw","map-"+i+suffix));
                foreach(string prefix in new[]{"angles-","attempts-"})
                    File.Copy(Path.Combine(completedTake,prefix+i+".json"),Path.Combine(output,prefix+i+".json"));
            }
            File.Copy(Path.Combine(completedTake,"maps.json"),Path.Combine(output,"maps.json"));
        }
        SessionState.SetString(Key+"output",output);
        SessionState.SetString(Key+"start",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetString(Key+"prefab",stage?.assetPath??"");
        SessionState.SetFloat(Key+"deadline",(float)EditorApplication.timeSinceStartup+1800);
        SessionState.SetBool(Key+"finished",false);
        if(stage!=null)StageUtility.GoToMainStage();
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(CaveMainPortalBuilder.RunPath(9));
        EditorApplication.update-=Tick;EditorApplication.update+=Tick;
        IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output,"Account"));
    }
    public static void StartTour(string directory) { Start(directory); SessionState.SetBool(Key+"technical",true); }
    public static void StartBoundary(string directory) { StartTour(directory); SessionState.SetBool(Key+"boundaryOnly",true); }
    static void Log(string message,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+trace);}
    static void Tick()
    {
        output=SessionState.GetString(Key+"output","");if(output==""){EditorApplication.update-=Tick;return;}
        if(SessionState.GetBool(Key+"finished",false))
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            string active=IsolatedSavePlayGuard.ActiveDirectory,env=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),own=Path.Combine(output,"Account");
            if((!string.IsNullOrEmpty(active)&&active!=own)||(!string.IsNullOrEmpty(env)&&env!=own))return;
            IsolatedSavePlayGuard.UseRealAccount();
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key+"start",""));
            string prefab=SessionState.GetString(Key+"prefab","");
            if(prefab!=""){var stage=PrefabStageUtility.OpenPrefab(prefab);Selection.activeGameObject=stage.prefabContentsRoot;CavePlatformBoundaryEditor.Frame(stage.prefabContentsRoot.GetComponent<CavePlatformBoundary>());}
            Write("return.json",new{status="PASS",blocked=IsolatedSavePlayGuard.RequiresAccountChoice,active=IsolatedSavePlayGuard.ActiveDirectory,env=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),prefab,scenes=Enumerable.Range(0,SceneManager.sceneCount).Select(i=>{var s=SceneManager.GetSceneAt(i);return new{s.path,s.isDirty};}).ToArray()});
            SessionState.EraseString(Key+"output");SessionState.EraseString(Key+"start");SessionState.EraseString(Key+"prefab");SessionState.EraseFloat(Key+"deadline");SessionState.EraseBool(Key+"finished");SessionState.EraseBool(Key+"technical");SessionState.EraseBool(Key+"boundaryOnly");EditorApplication.update-=Tick;return;
        }
        try
        {
            if(EditorApplication.timeSinceStartup>SessionState.GetFloat(Key+"deadline",0))throw new TimeoutException("Cave film timeout.");
            if(!EditorApplication.isPlaying||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            EditorApplication.QueuePlayerLoopUpdate();
            if(routine==null){maps.Clear();stages.Clear();errors.Clear();priorBackground=Application.runInBackground;Application.runInBackground=true;Application.logMessageReceived+=Log;routine=SessionState.GetBool(Key+"technical",false)?TourFilm():Film();}
            if(waiting!=null&&!waiting.isDone){CaptureGeneration();return;}waiting=null;
            if(!routine.MoveNext()){Finish(null);return;}
            waiting=routine.Current as AsyncOperation;CaptureGeneration();
        }catch(Exception error){Finish(error.ToString());}
    }
    static void Finish(string error)
    {
        try{(routine as IDisposable)?.Dispose();}catch(Exception e){error+="\n"+e;}
        routine=null;waiting=null;recordGeneration=false;EndTake();
        if(target){target.Release();Object.DestroyImmediate(target);}if(pixels)Object.DestroyImmediate(pixels);if(camera)Object.DestroyImmediate(camera.gameObject);
        Time.captureFramerate=0;Application.runInBackground=priorBackground;Application.logMessageReceived-=Log;
        Write("capture.json",new{status=error==null&&errors.Count==0?"PASS":"FAIL",error,errors,maps,stages,width=Width,height=Height,fps=Fps});
        SessionState.SetBool(Key+"finished",true);EditorApplication.ExitPlaymode();
    }
    static void BeginTake(string name)
    {
        EndTake();frames=0;takeStarted=EditorApplication.timeSinceStartup;nextSample=takeStarted;
        encoder=new MediaEncoder(Path.Combine(output,"Raw",name+".mp4"),new VideoTrackEncoderAttributes{frameRate=new MediaRational(Fps),width=Width,height=Height,includeAlpha=false,targetBitRate=48000000,bitRateMode=VideoBitrateMode.High});
    }
    static void EndTake(){encoder?.Dispose();encoder=null;}
    static void Frame(int copies=1)
    {
        var previous=RenderTexture.active;
        try{
            RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=target});
            RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,Width,Height),0,0,false);pixels.Apply(false,false);
            for(int i=0;i<copies;i++){if(!encoder.AddFrame(pixels))throw new IOException("Video frame rejected.");frames++;}
        }finally{RenderTexture.active=previous;}
    }
    static void CaptureGeneration()
    {
        if(!recordGeneration||encoder==null||EditorApplication.timeSinceStartup<nextSample)return;
        double now=EditorApplication.timeSinceStartup;nextSample=now+.1;
        if(temporaryLight&&world&&world.transform.Find(CaveRuntimeDressing.LightingName))temporaryLight.SetActive(false);
        int needed=Math.Max(1,(int)((now-takeStarted)*Fps)-frames);
        Frame(needed);
        var generator=world?world.GetComponent<CaveRuntimeGenerator>():null;
        stages.Add(new{seconds=frames/(float)Fps,stage=generator?.Stage,platforms=world?.courts.Count??0,connections=world?.passages.Count??0});
    }
    static Bounds FloorBounds(CaveWorld value)
    {
        var rs=value.courts.SelectMany(c=>c.tile.transform.Find("Platform").GetComponentsInChildren<Renderer>()).Where(r=>r.enabled).ToArray();
        var b=rs[0].bounds;foreach(var r in rs.Skip(1))b.Encapsulate(r.bounds);return b;
    }
    static void View(Bounds b,float yaw,float pitch,float fill=1.18f)
    {
        var q=Quaternion.Euler(pitch,yaw,0);float v=0,h=0;
        for(int i=0;i<8;i++){var e=b.extents;var p=Quaternion.Inverse(q)*new Vector3((i&1)==0?-e.x:e.x,(i&2)==0?-e.y:e.y,(i&4)==0?-e.z:e.z);v=Mathf.Max(v,Mathf.Abs(p.y));h=Mathf.Max(h,Mathf.Abs(p.x));}
        camera.orthographicSize=Mathf.Max(v,h/camera.aspect)*fill+3;camera.transform.SetPositionAndRotation(b.center+q*Vector3.back*450,q);
    }
    static IEnumerator Generate(CaveGenerationAssets assets,int count,int seed)
    {
        world=new GameObject("Filmed live cave").AddComponent<CaveWorld>();world.authoredLayout=true;
        var gen=world.gameObject.AddComponent<CaveRuntimeGenerator>();gen.assets=assets;
        var build=gen.Generate(count,seed);
        try{while(build.MoveNext()){yield return build.Current;}}
        finally{(build as IDisposable)?.Dispose();}
        if(!gen.IsReady||world.courts.Count!=count)throw new InvalidOperationException("Live generation did not finish.");
    }
    static void SetupCamera()
    {
        camera=new GameObject("Owned cave film camera").AddComponent<Camera>();camera.enabled=false;camera.orthographic=true;camera.aspect=Width/(float)Height;camera.nearClipPlane=.1f;camera.farClipPlane=1000;camera.backgroundColor=new Color(.012f,.018f,.026f);camera.clearFlags=CameraClearFlags.SolidColor;camera.allowHDR=true;
        var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;data.requiresDepthTexture=true;
        var pipeline=new SerializedObject(GraphicsSettings.currentRenderPipeline);var renderers=pipeline.FindProperty("m_RendererDataList");int index=-1;
        for(int i=0;i<renderers.arraySize;i++)if(renderers.GetArrayElementAtIndex(i).objectReferenceValue&&AssetDatabase.GetAssetPath(renderers.GetArrayElementAtIndex(i).objectReferenceValue)==CaveDressingBuilder.Root+"/CaveDemo1_AuthoredRenderer.asset")index=i;
        if(index<0)throw new InvalidOperationException("Cave renderer missing.");data.SetRenderer(index);
        target=new RenderTexture(Width,Height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);target.Create();pixels=new Texture2D(Width,Height,TextureFormat.RGBA32,false);
    }
    static IEnumerator Film()
    {
        var bootstrap=Object.FindFirstObjectByType<CaveWorld>();if(!bootstrap)throw new InvalidOperationException("Live entry scene missing.");
        var assets=bootstrap.GetComponent<CaveRuntimeGenerator>().assets;Object.Destroy(bootstrap.gameObject);yield return null;
        SetupCamera();
        int[] counts={9,9,9,12,12,12,15,15,15,20,20,20};int[] seeds={-284089667,1556245222,73242,1904484630,73145,19216,-1619063537,19216,73146,-284089667,1556245222,1904485018};Bounds firstBounds=default;
        if(File.Exists(Path.Combine(output,"maps.json")))
        {
            var saved=Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(Path.Combine(output,"maps.json")));
            maps.AddRange(saved.Cast<object>());
            foreach(var m in saved) seeds[(int)m["index"]]=(int)m["seed"];
            var c=saved[0]["bounds"]["center"].ToObject<float[]>();var s=saved[0]["bounds"]["size"].ToObject<float[]>();
            firstBounds=new Bounds(new Vector3(c[0],c[1],c[2]),new Vector3(s[0],s[1],s[2]));
        }
        for(int take=maps.Count;take<counts.Length;take++)
        {
            Write("progress.json",new{stage="generating",take,count=counts[take],seed=seeds[take]});
            bool completed=false;var rejected=new List<object>();
            for(int attempt=0;attempt<20&&!completed;attempt++)
            {
                Write("progress.json",new{stage="generating",take,count=counts[take],seed=seeds[take],attempt});
                Exception failure=null;var generation=Generate(assets,counts[take],seeds[take]);
                try{while(true){bool more=false;try{more=generation.MoveNext();}catch(Exception e){failure=e;}if(failure!=null||!more)break;yield return generation.Current;}}
                finally{(generation as IDisposable)?.Dispose();}
                if(failure==null)completed=true;
                else{rejected.Add(new{seed=seeds[take],error=failure.Message});Write("attempts-"+take+".json",rejected);Object.Destroy(world.gameObject);world=null;yield return null;yield return Resources.UnloadUnusedAssets();seeds[take]+=97;}
            }
            Write("attempts-"+take+".json",rejected);
            if(!completed)throw new InvalidOperationException("No valid capture layout after twenty generation attempts: "+take);
            var b=FloorBounds(world);if(take==0)firstBounds=b;
            maps.Add(new{index=take,count=counts[take],seed=seeds[take],generationMilliseconds=world.generationMilliseconds,bounds=new{center=V(b.center),size=V(b.size)},nodes=world.courts.Select(c=>new{name=c.name,position=V(c.center)}).ToArray(),edges=world.passages.Select(p=>new{p.a,p.b}).ToArray()});
            Write("maps.json",maps);Write("progress.json",new{stage="filming",take});
            var terrain=world.GetComponentInChildren<Terrain>();if(terrain)terrain.enabled=false;float yaw=b.size.z>b.size.x?90:0;
            Time.captureFramerate=Fps;BeginTake("map-"+take+"-overview");
            for(int f=0;f<Fps*3;f++){View(b,yaw+Mathf.Lerp(-5,5,f/(float)(Fps*3-1)),67);Frame();if(f==Fps*3/2)File.WriteAllBytes(Path.Combine(output,"Raw","map-"+take+".png"),pixels.EncodeToPNG());yield return null;}EndTake();
            if(terrain)terrain.enabled=true;
            var connection=world.generatedRoot.GetComponentsInChildren<CaveRigidConnection>().Where(c=>c.stairCount==0).OrderByDescending(c=>Mathf.Abs(SelectedAngle(c))).FirstOrDefault() ?? world.generatedRoot.GetComponentsInChildren<CaveRigidConnection>().First();
            var close=new Bounds((connection.start+connection.end)*.5f,new Vector3(32,16,32));var projections=new List<object>();BeginTake("map-"+take+"-detail");
            for(int f=0;f<Fps*2;f++){float t=f/(float)(Fps*2-1);View(close,Mathf.Lerp(-10,8,t),Mathf.Lerp(62,67,t),1);Frame();projections.Add(ProjectConnection(connection));yield return null;}EndTake();Write("angles-"+take+".json",projections);Time.captureFramerate=0;
            Object.Destroy(world.gameObject);world=null;yield return null;yield return Resources.UnloadUnusedAssets();
        }
        // Record a new real generation with a camera fitted from the identical seed's completed bounds.
        temporaryLight=Object.Instantiate(assets.lighting);View(firstBounds,firstBounds.size.z>firstBounds.size.x?90:0,67);BeginTake("generation");recordGeneration=true;
        var final=Generate(assets,counts[0],seeds[0]);try{while(final.MoveNext())yield return final.Current;}finally{(final as IDisposable)?.Dispose();}
        CaptureGeneration();recordGeneration=false;EndTake();Object.Destroy(temporaryLight);
        Write("progress.json",new{stage="complete",maps=maps.Count});
    }
    static float SelectedAngle(CaveRigidConnection c)
    {
        var p=c.boundaryA.Data.ports[c.portA];var d=c.boundaryA.transform.InverseTransformPoint(c.start)-p.pivot;
        return Mathf.DeltaAngle(p.heading,Mathf.Atan2(d.z,d.x)*Mathf.Rad2Deg);
    }
    static object ProjectConnection(CaveRigidConnection c)
    {
        float[] Screen(Vector3 p){var v=camera.WorldToViewportPoint(p+Vector3.up*.25f);return new[]{v.x*Width,(1-v.y)*Height};}
        var port=c.boundaryA.Data.ports[c.portA];var t=c.boundaryA.transform;float radius=port.radius+Mathf.Min(port.deckLength,7);
        return new{pivot=Screen(t.TransformPoint(port.pivot)),arc=Enumerable.Range(0,25).Select(i=>{float a=Mathf.Lerp(port.MinAngle,port.MaxAngle,i/24f)*Mathf.Deg2Rad;return Screen(t.TransformPoint(port.pivot+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*radius));}).ToArray(),start=Screen(c.start),end=Screen(c.end),selected=SelectedAngle(c),allowed=port.halfAngle,length=Vector3.Distance(c.start,c.end),stairs=c.stairCount};
    }
    static float[] V(Vector3 v)=>new[]{v.x,v.y,v.z};
}
