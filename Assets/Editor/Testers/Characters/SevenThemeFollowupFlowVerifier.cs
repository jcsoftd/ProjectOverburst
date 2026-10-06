using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

// Uses the saved production actor/AI/physics in a temporary empty Play scene.
// Captures are diagnostics, not a frame-rate benchmark or direct player-input test.
// Persistent ownership survives reloads; only this fixture's resources/account are returned.
[InitializeOnLoad]
public static class SevenThemeFollowupFlowVerifier
{
    const string Key="Overburst.SevenThemeFollowupFlow.plan";
    const string ModeKey="Overburst.SevenThemeFollowupFlow.run";
    static string Folder=>Path.GetFullPath(Path.Combine(Application.dataPath,"../../개인파일/코덱스산출/Monsters/20261006_SevenThemeImprovement/GOAL_01",SessionState.GetString(ModeKey,"BaselineNative")));
    static string Account=>Path.Combine(Folder,"Account");
    static EnemyMotor host;
    static double lastTraceWrite;
    static string currentId;
    static bool inPlayExit;
    static SevenThemeFollowupFlowVerifier()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += Changed;
        AssemblyReloadEvents.beforeAssemblyReload += Reloading;
    }
    static bool OwnPlay => EditorApplication.isPlaying && Same(IsolatedSavePlayGuard.ActiveDirectory);
    static void Changed(PlayModeStateChange state)
    {
        if(state==PlayModeStateChange.ExitingPlayMode && running)
        { inPlayExit=true; try{Finish("INTERRUPTED_PLAY_EXIT");}finally{inPlayExit=false;} }
    }
    static void Reloading(){if(running)Finish("INTERRUPTED_DOMAIN_RELOAD");}
    static void Tick()
    {
        var raw=SessionState.GetString(Key,"");if(string.IsNullOrEmpty(raw))return;
        if(EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        var p=JObject.Parse(raw);string stage=(string)p["status"];
        if(stage=="booting" && OwnPlay){Run();return;}
        if(stage=="booting" && EditorApplication.timeSinceStartup>(double)p["deadline"])
        {p["status"]="returning";p["result"]="BOOT_TIMEOUT";SessionState.SetString(Key,p.ToString());return;}
        if(stage=="running" && running)
        {
            if(EditorApplication.timeSinceStartup-lastTraceWrite>2 && trace!=null && currentId!=null)
            {SaveCurrentTrace();lastTraceWrite=EditorApplication.timeSinceStartup;}
            if(EditorApplication.timeSinceStartup>(double)p["deadline"])Finish("RUN_TIMEOUT");
            return;
        }
        if(stage!="returning")return;
        if(OwnPlay){EditorApplication.ExitPlaymode();return;}
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        string env=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable);
        string prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","");
        if(!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(env)&&!Same(env) || !string.IsNullOrEmpty(prepared)&&!Same(prepared))return;
        Return();
    }
    static void SaveCurrentTrace()
    {
        string dir=Path.Combine(Folder,currentId);Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir,"trace.json"),trace.ToString());
    }
    static readonly List<Object> owned=new List<Object>();
    static readonly JArray results=new JArray();
    static EnemyActor actor;
    static Transform target;
    static EnemySpawnService service;
    static Capture camera;
    static JArray trace;
    static JArray damages;
    static string phase;
    static float begin;
    static int lastFrame=-1;
    static bool running;
    static readonly HashSet<string> RecordIds=new HashSet<string>{"SpiderBrood_Formickarce","V3_Hideoplast","V3_Skorpmare","V3_Anglerox","V3_Gasterodonte","V3_Deinodonte","V3_Perderos"};
    static JArray V(Vector3 p)=>new JArray(p.x,p.y,p.z);
    static JArray Scenes(){var a=new JArray();for(int i=0;i<SceneManager.sceneCount;i++){var s=SceneManager.GetSceneAt(i);a.Add(new JObject{["path"]=s.path,["dirty"]=s.isDirty,["roots"]=s.rootCount});}return a;}
    static bool Same(string p)=>string.Equals(p,Account,StringComparison.OrdinalIgnoreCase);
    public static object Start(string run)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||BuildPipeline.isBuildingPlayer
            ||!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            ||!string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""))
            ||!string.IsNullOrEmpty(SessionState.GetString("Overburst.WeakAttackPlayerLoop.plan",""))||!string.IsNullOrEmpty(SessionState.GetString(Key,"")))
            throw new InvalidOperationException("Idle, unreserved shared Editor required.");
        if(run!="BaselineNative"&&run!="AfterNative")throw new InvalidOperationException("Owned baseline or result only.");
        SessionState.SetString(ModeKey,run);
        Directory.CreateDirectory(Folder);
        if(File.Exists(Path.Combine(Folder,"plan.json")))throw new InvalidOperationException("Fresh audit output required.");
        var p=new JObject{["scenes"]=Scenes(),["startScene"]=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),["captureDelta"]=Time.captureDeltaTime,["timeScale"]=Time.timeScale,
            ["background"]=Application.runInBackground,["startedUtc"]=DateTime.UtcNow,["status"]="booting",["account"]=Account,["deadline"]=EditorApplication.timeSinceStartup+120};
        SessionState.SetString(Key,p.ToString());File.WriteAllText(Path.Combine(Folder,"plan.json"),p.ToString());
        var active=SceneManager.GetActiveScene();var fixture=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
        string fixturePath="Assets/Editor/Testers/Characters/SevenThemeFollowup_"+Guid.NewGuid().ToString("N")+".unity";
        try{if(!EditorSceneManager.SaveScene(fixture,fixturePath))throw new InvalidOperationException("Owned fixture save failed.");}
        finally{EditorSceneManager.CloseScene(fixture,true);if(active.IsValid())SceneManager.SetActiveScene(active);}
        p["fixture"]=fixturePath;p["fixtureGuid"]=AssetDatabase.AssetPathToGUID(fixturePath);SessionState.SetString(Key,p.ToString());
        File.WriteAllText(Path.Combine(Folder,"plan.json"),p.ToString());
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(fixturePath);
        Application.runInBackground=true;IsolatedSavePlayGuard.EnterIsolatedPlay(Account);
        return new {status="owned Play requested",next="Call Run after owned Play boot; this fixture does not save scenes, assets or source."};
    }
    public static object Run()
    {
        if(!EditorApplication.isPlaying||!Same(IsolatedSavePlayGuard.ActiveDirectory)||running)throw new InvalidOperationException("Own Play only; a single fixture runner.");
        var p=JObject.Parse(SessionState.GetString(Key,""));p["status"]="running";p["deadline"]=EditorApplication.timeSinceStartup+900;SessionState.SetString(Key,p.ToString());
        running=true;results.Clear();owned.Clear();currentId=null;var go=new GameObject("Private audit coroutine host");owned.Add(go);host=go.AddComponent<EnemyMotor>();go.GetComponent<Rigidbody>().isKinematic=true;
        EditorApplication.update+=Observe;
        host.StartCoroutine(Drive(Work()));
        return new {status="running",scope="Saved production AI/Animator/physics on flat diagnostic floor with saved player-sized capsule target; 12 selected actors, no live input/player reaction; deterministic capture time, not FPS benchmark"};
    }
    static void Interrupted(){Finish("INTERRUPTED_DOMAIN_RELOAD");}
    static IEnumerator Drive(IEnumerator body)
    {
        var stack=new Stack<IEnumerator>();stack.Push(body);
        while(stack.Count>0)
        {
            var e=stack.Peek();bool more;object value;
            try{more=e.MoveNext();value=more?e.Current:null;}
            catch(Exception ex){while(stack.Count>0){try{(stack.Pop() as IDisposable)?.Dispose();}catch{}}Finish(ex.ToString());yield break;}
            if(!more){stack.Pop();(e as IDisposable)?.Dispose();continue;}
            if(value is IEnumerator next){stack.Push(next);continue;}yield return value;
        }
        Finish("COMPLETE_SCOPED");
    }
    static IEnumerator Work()
    {
        Time.captureDeltaTime=1f/30;
        var lightObject=new GameObject("Owned diagnostic light");owned.Add(lightObject);
        var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.shadows=LightShadows.Soft;light.transform.rotation=Quaternion.Euler(45,35,0);
        Vector3 origin=Vector3.zero;
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);owned.Add(floor);floor.name="Private 1m-grid flat audit floor";floor.transform.position=origin+Vector3.down*.5f;floor.transform.localScale=new Vector3(90,1,90);
        var floorMat=new Material(Shader.Find("Universal Render Pipeline/Lit"));owned.Add(floorMat);floorMat.color=new Color(.23f,.28f,.32f);floor.GetComponent<Renderer>().sharedMaterial=floorMat;
        var gridMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit"));owned.Add(gridMaterial);gridMaterial.color=new Color(.12f,.16f,.20f);
        for(int i=-20;i<=20;i+=2)for(int axis=0;axis<2;axis++)
        {
            var line=GameObject.CreatePrimitive(PrimitiveType.Cube);owned.Add(line);Object.DestroyImmediate(line.GetComponent<Collider>());
            line.name="Owned diagnostic 2m ground grid";line.transform.position=new Vector3(axis==0?i:0,.002f,axis==1?i:0);
            line.transform.localScale=new Vector3(axis==0?.012f:40,.002f,axis==1?.012f:40);line.GetComponent<Renderer>().sharedMaterial=gridMaterial;
        }
        var root=new GameObject("Private production spawn and pool services");owned.Add(root);var inactive=new GameObject("Private inactive pool");inactive.transform.SetParent(root.transform,false);inactive.SetActive(false);
        var pool=root.AddComponent<EnemyPoolService>();pool.Configure(inactive.transform,0);service=root.AddComponent<EnemySpawnService>();
        var catalog=AssetDatabase.LoadAssetAtPath<EnemyCatalog>("Assets/ProjectOverburst/Resources/Enemies/Themes/Catalog.asset");service.Configure(catalog,pool);
        var playerPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab");var source=playerPrefab.GetComponentsInChildren<CapsuleCollider>(true).Single(c=>c.enabled&&!c.isTrigger);
        var volume=playerPrefab.GetComponent<CombatTarget>().ResolveVolumeAtRootPosition(Vector3.zero);
        var t=new GameObject("Saved player capsule diagnostic target");owned.Add(t);target=t.transform;t.layer=playerPrefab.layer;t.tag="Player";
        var capsule=t.AddComponent<CapsuleCollider>();capsule.center=playerPrefab.transform.InverseTransformPoint(source.transform.TransformPoint(source.center));capsule.height=source.height*Mathf.Abs(source.transform.lossyScale.y);capsule.radius=source.radius*Mathf.Max(Mathf.Abs(source.transform.lossyScale.x),Mathf.Abs(source.transform.lossyScale.z));
        var combat=t.AddComponent<CombatTarget>();combat.Configure(CombatTeam.PlayerParty,false);combat.ConfigureVolume(volume.Center,volume.Radius,volume.HalfHeight*2);var health=t.GetComponent<CombatHealth>();health.SetMaxHp(1e9f,true);
        health.OnDamaged+=(_,d)=>damages?.Add(new JObject{["time"]=Time.time-begin,["phase"]=d.sourceAttackPhaseIndex,["sequence"]=d.sourceAttackSequenceId,["damage"]=d.damage});
        var visual=GameObject.CreatePrimitive(PrimitiveType.Capsule);visual.transform.SetParent(target,false);Object.Destroy(visual.GetComponent<Collider>());visual.transform.localPosition=capsule.center;visual.transform.localScale=new Vector3(capsule.radius*2,capsule.height/2,capsule.radius*2);
        var mat=new Material(Shader.Find("Universal Render Pipeline/Lit"));owned.Add(mat);mat.color=new Color(.96f,.69f,.28f);visual.GetComponent<Renderer>().sharedMaterial=mat;
        yield return null;yield return new WaitForFixedUpdate();
        string[] ids=new[]{"V3_Anglerox","V3_Hideoplast","V3_Deinodonte","V3_Perderos","SpiderBrood_Formickarce","DeathHarvest_RakeBrute","DeathHarvest_Reaper","V3_Skorpmare","V3_darkKnight2","V3_Gasterodonte","V3_Kapeloproboskid","V3_Onyscidus"};
        var defs=ids.Select(id=>{if(!catalog.TryGet(id,out var definition))throw new InvalidOperationException("Saved catalog ID missing: "+id);return definition;}).ToArray();
        foreach(var d in defs)
        {
            currentId=d.EnemyId;trace=new JArray();damages=new JArray();begin=Time.time;phase="approach";target.position=origin+Vector3.forward*22;health.SetMaxHp(1e9f,true);Physics.SyncTransforms();
            if(!service.TrySpawn(new EnemySpawnRequest(d,origin+Vector3.up*.035f,Quaternion.identity,target,context:EncounterContext.Test),out actor))throw new InvalidOperationException("Spawn failed: "+d.EnemyId);
            actor.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;actor.AI.RequestAggro(target);yield return null;yield return new WaitForFixedUpdate();
            if(actor.AI.Target!=target)throw new InvalidOperationException("Diagnostic target resolver differs; exclude this fixture.");
            if(RecordIds.Contains(d.EnemyId))camera=new Capture(Path.Combine(Folder,d.EnemyId),target,actor);
            Vector3 first=actor.transform.position;float until=Time.time+6f;
            while(Time.time<until){yield return null;}
            phase="orbit";if(camera!=null)camera.Mark(phase);float start=Time.time;float radius=Mathf.Min(6,Mathf.Max(3,actor.AI.AttackExitRange+.5f));
            while(Time.time-start<3.5f){float a=(Time.time-start)*80*Mathf.Deg2Rad;target.position=origin+new Vector3(Mathf.Sin(a),0,Mathf.Cos(a))*radius;Physics.SyncTransforms();yield return null;}
            phase="behind";if(camera!=null)camera.Mark(phase);target.position=actor.transform.position-actor.transform.forward*Mathf.Max(1.3f,Mathf.Min(3,actor.AI.AttackEnterRange*.8f));target.position=new Vector3(target.position.x,0,target.position.z);Physics.SyncTransforms();
            yield return new WaitForSeconds(1.8f);
            phase="attack_retreat";if(camera!=null)camera.Mark(phase);target.position=actor.transform.position+actor.transform.forward*Mathf.Clamp(actor.AI.AttackEnterRange*.65f,1.1f,2.6f);target.position=new Vector3(target.position.x,0,target.position.z);Physics.SyncTransforms();
            float wait=Time.time+6;while(!actor.AbilityController.IsExecuting&&Time.time<wait)yield return null;
            var selected=actor.AbilityController.LastCommittedAbility;bool attackFound=actor.AbilityController.IsExecuting;float at=Time.time;float attackPhase=-1;
            if(attackFound)
            {
                yield return new WaitForSeconds(.12f);actor.AnimationBridge.TryGetAttackNormalizedTime(selected.AnimatorTrigger,out attackPhase);
                target.position=actor.transform.position+actor.transform.forward*Mathf.Min(11,actor.AI.AttackExitRange+1.0f);target.position=new Vector3(target.position.x,0,target.position.z);Physics.SyncTransforms();
            }
            yield return new WaitForSeconds(.35f);
            bool cancelled=attackFound&&!actor.AbilityController.IsExecuting&&actor.AI.CurrentStateName=="Chase";
            float earlyCheckTime=Time.time;string earlyState=actor.AI.CurrentStateName;
            // Observe the whole attack and recovery, not only the original early cancellation check.
            float tailLimit=Time.time+Mathf.Min(8f,Mathf.Max(3f,(selected!=null?selected.AttackAnimationDuration:1f)+1.5f));
            while(Time.time<tailLimit)yield return null;
            float approachMoved=Vector3.Distance(first,actor.transform.position);
            phase="death";if(camera!=null)camera.Mark(phase);actor.Health.TakeDamage(new DamageInfo(actor.Health.CurrentHp+1e8f,actor.transform.position,target.gameObject,Vector3.forward,suppressDefaultHitVfx:true));
            bool death=actor.Health.IsDead;float deathUntil=Time.time+Mathf.Min(5.5f,Mathf.Max(2,d.AnimationProfile.Death.length+.5f));
            while(actor.IsLeased&&Time.time<deathUntil)yield return null;
            camera?.Dispose();camera=null;uint old=actor.LeaseVersion;service.Release(actor);yield return null;yield return new WaitForFixedUpdate();
            bool reset=!actor.IsLeased&&!actor.gameObject.activeSelf&&!actor.AbilityController.IsExecuting&&!actor.Melee.IsAttacking&&!actor.AnimationBridge.IsParryStunAnimating;
            string outdir=Path.Combine(Folder,d.EnemyId);Directory.CreateDirectory(outdir);File.WriteAllText(Path.Combine(outdir,"trace.json"),trace.ToString());
            results.Add(new JObject{["id"]=d.EnemyId,["name"]=d.DisplayName,["moved"]=approachMoved,["attackObserved"]=attackFound,
                ["retreatAbility"]=selected!=null?selected.AbilityId:null,["phaseBeforeTargetLeft"]=attackPhase,["retreatCancelledAndChased"]=cancelled,["earlyCheckTime"]=earlyCheckTime-begin,["earlyState"]=earlyState,["hasTurnAnimation"]=d.MovementProfile.HasTurnAnimation,
                ["deathObserved"]=death,["poolReset"]=reset,["damageEvents"]=damages,["targetIsRealPlayer"]=false,["savedPlayerCapsule"] = new JObject{["center"]=V(capsule.center),["radius"]=capsule.radius,["height"]=capsule.height}});
            actor=null;phase="none";Write("RUNNING");
        }
    }
    static void Observe()
    {
        if(!running||!EditorApplication.isPlaying||!Same(IsolatedSavePlayGuard.ActiveDirectory))return;
        if(actor==null||!actor.IsLeased||Time.frameCount==lastFrame)return;lastFrame=Time.frameCount;
        Vector3 delta=target.position-actor.transform.position;delta.y=0;var a=actor.AbilityController.LastCommittedAbility;float p=-1;if(a!=null)actor.AnimationBridge.TryGetAttackNormalizedTime(a.AnimatorTrigger,out p);
        var info=actor.Animator.GetCurrentAnimatorStateInfo(0);
        trace.Add(new JObject{["time"]=Time.time-begin,["frame"]=Time.frameCount,["phase"]=phase,["root"]=V(actor.transform.position),["target"]=V(target.position),["yaw"]=actor.transform.eulerAngles.y,
            ["facingError"]=Vector3.Angle(actor.transform.forward,delta),["distance"]=delta.magnitude,["ai"]=actor.AI.CurrentStateName,["ability"]=a!=null?a.AbilityId:null,
            ["executing"]=actor.AbilityController.IsExecuting,["attackPhase"]=p,["animationPhase"]=info.normalizedTime,["blend"]=actor.Animator.IsInTransition(0),["locomotion"]=actor.Animator.GetFloat("Locomotion"),
            ["moveSpeed"]=actor.Animator.parameters.Any(q=>q.name=="MoveAnimSpeed")?actor.Animator.GetFloat("MoveAnimSpeed"):-1,["destination"]=actor.Movement.HasDestination,["debugState"]=actor.AI.CurrentDebugStateName,["targetMatchesFixture"]=actor.AI.Target==target,
            ["actionLocked"]=actor.Movement.IsActionLocked,["blocked"]=actor.AnimationBridge.IsBlockingActionActive,["hp"]=actor.Health.CurrentHp,["dead"]=actor.Health.IsDead,["interpolation"]=actor.GetComponent<Rigidbody>().interpolation.ToString()});
        camera?.Frame();
    }
    static void Write(string status)=>File.WriteAllText(Path.Combine(Folder,"results.json"),new JObject{["status"]=status,["cases"]=results,["assetWrites"]=0,["actualProductionAIPhysicsAnimator"]=true,["actualPlayerInput"]=false,["scope"]="Flat floor diagnostic target movement, not final game camera, obstacles or performance; original attacks/settings unchanged"}.ToString());
    static void Finish(string reason)
    {
        if(!running)return;running=false;EditorApplication.update-=Observe;
        try{camera?.Dispose();camera=null;if(trace!=null && currentId!=null)SaveCurrentTrace();Write(reason);}
        finally{foreach(var value in owned)if(value!=null)Object.DestroyImmediate(value);owned.Clear();host=null;actor=null;}
        var p=JObject.Parse(SessionState.GetString(Key,""));p["status"]="returning";p["result"]=reason;SessionState.SetString(Key,p.ToString());
        if(!inPlayExit && OwnPlay)EditorApplication.ExitPlaymode();
    }
    public static object Return()
    {
        var raw=SessionState.GetString(Key,"");if(string.IsNullOrEmpty(raw))throw new InvalidOperationException("No owned return reservation.");
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||BuildPipeline.isBuildingPlayer)throw new InvalidOperationException("Wait for safe EditMode.");
        string active=IsolatedSavePlayGuard.ActiveDirectory,env=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","");
        if(!string.IsNullOrEmpty(active)||!string.IsNullOrEmpty(env)&&!Same(env)||!string.IsNullOrEmpty(prepared)&&!Same(prepared))throw new InvalidOperationException("Foreign account reservation preserved.");
        var p=JObject.Parse(raw);Time.captureDeltaTime=(float)p["captureDelta"];if(Time.timeScale!=(float)p["timeScale"])Time.timeScale=(float)p["timeScale"];Application.runInBackground=(bool)p["background"];
        if(AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)==(string)p["fixture"])EditorSceneManager.playModeStartScene=string.IsNullOrEmpty((string)p["startScene"])?null:AssetDatabase.LoadAssetAtPath<SceneAsset>((string)p["startScene"]);
        string fixture=(string)p["fixture"];
        if(!string.IsNullOrEmpty(fixture) && fixture.StartsWith("Assets/Editor/Testers/Characters/SevenThemeFollowup_",StringComparison.Ordinal)
            && AssetDatabase.AssetPathToGUID(fixture)==(string)p["fixtureGuid"])AssetDatabase.DeleteAsset(fixture);
        IsolatedSavePlayGuard.UseRealAccount();SessionState.EraseString(Key);var now=Scenes();
        var result=new JObject{["status"]=JToken.DeepEquals(p["scenes"],now)&&!IsolatedSavePlayGuard.RequiresAccountChoice?"PASS":"FAIL",["before"]=p["scenes"],["after"]=now,["environment"]=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),["guardChoice"]=IsolatedSavePlayGuard.RequiresAccountChoice,["active"]=IsolatedSavePlayGuard.ActiveDirectory,["prepared"]=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""),["startScene"]=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),["captureDelta"]=Time.captureDeltaTime};
        File.WriteAllText(Path.Combine(Folder,"return.json"),result.ToString());SessionState.EraseString(ModeKey);return result;
    }
    sealed class Capture:IDisposable
    {
        Camera cam;RenderTexture rt;Texture2D pixels;MediaEncoder encoder;string directory;Transform player;EnemyActor enemy;int frame,frames;string mark;
        public Capture(string dir,Transform p,EnemyActor e)
        {
            directory=dir;Directory.CreateDirectory(dir);player=p;enemy=e;var obj=new GameObject("Owned AI diagnostic capture camera");cam=obj.AddComponent<Camera>();if(Camera.main!=null)cam.CopyFrom(Camera.main);cam.enabled=false;cam.orthographic=true;cam.nearClipPlane=.1f;cam.farClipPlane=200;cam.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            rt=new RenderTexture(960,540,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);rt.Create();pixels=new Texture2D(960,540,TextureFormat.RGBA32,false);
            encoder=new MediaEncoder(Path.Combine(dir,"ai-scenarios.mp4"),new VideoTrackEncoderAttributes{frameRate=new MediaRational(15),width=960,height=540,includeAlpha=false,targetBitRate=4500000,bitRateMode=UnityEditor.VideoBitrateMode.High});frame=Time.frameCount;mark="approach";
        }
        public void Mark(string p){mark=p;}
        public void Frame()
        {
            if((Time.frameCount-frame)%2!=0||cam==null||enemy==null)return;
            Vector3 center=(player.position+enemy.transform.position)*.5f+Vector3.up;float distance=Vector3.Distance(player.position,enemy.transform.position);
            if(phase=="approach"){center=enemy.transform.position+Vector3.up;distance=4.5f;}
            Quaternion rotation=Quaternion.Euler(38,45,0);cam.orthographicSize=Mathf.Max(4.2f,distance*.45f+2);cam.transform.SetPositionAndRotation(center+rotation*Vector3.back*50,rotation);
            var prior=RenderTexture.active;try{RenderPipeline.SubmitRenderRequest(cam,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});RenderTexture.active=rt;pixels.ReadPixels(new Rect(0,0,960,540),0,0,false);pixels.Apply(false,false);encoder.AddFrame(pixels);frames++;if(mark!=null){File.WriteAllBytes(Path.Combine(directory,mark+".png"),pixels.EncodeToPNG());mark=null;}}finally{RenderTexture.active=prior;}
        }
        public void Dispose(){encoder?.Dispose();encoder=null;if(rt!=null){rt.Release();Object.DestroyImmediate(rt);rt=null;}if(pixels!=null){Object.DestroyImmediate(pixels);pixels=null;}if(cam!=null){Object.DestroyImmediate(cam.gameObject);cam=null;}File.WriteAllText(Path.Combine(directory,"video.json"),new JObject{["frames"]=frames,["fps"]=15,["duration"]=frames/15f,["actualPlayer"]=false,["scope"]="Production AI with a diagnostic player-sized capsule; no audio"}.ToString());}
    }
}
