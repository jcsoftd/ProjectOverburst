using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static partial class MainTownDestructionVerifier
{
    const string Key="Overburst.MainTownDestructionVerifier.";
    static string Output=>SessionState.GetString(Key+"output","");
    static readonly List<string> checks=new List<string>(),errors=new List<string>();
    static IEnumerator routine;static int frame;static InputSettings originalInput,ownedInput;
    static bool OwnPlay=>EditorApplication.isPlaying&&Output!=""&&IsolatedSavePlayGuard.ActiveDirectory==Path.Combine(Output,"Save");
    static MainTownDestructionVerifier(){if(Output!="")Subscribe();}
    static void Subscribe(){EditorApplication.update-=Pump;EditorApplication.update+=Pump;}
    static void Check(bool value,string text){if(!value)throw new InvalidOperationException(text);checks.Add(text);}
    static void Write(string file,object value)=>File.WriteAllText(Path.Combine(Output,file),JsonConvert.SerializeObject(value,Formatting.Indented));

    public static void Run(string directory)
    {
        MainTownDestructionBuilder.RequireScene();
        if(Output!=""||IsolatedSavePlayGuard.RequiresAccountChoice||IsolatedSavePlayGuard.ActiveDirectory!=""||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))||SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","")!="")throw new InvalidOperationException("Another account owns this Editor.");
        directory=Path.GetFullPath(directory);string allowed=Path.GetFullPath(Path.Combine(Application.dataPath,"../../개인파일/코덱스산출"))+Path.DirectorySeparatorChar;
        if(!directory.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||Directory.Exists(directory))throw new ArgumentException("Use a fresh artifact directory.");
        Directory.CreateDirectory(directory);checks.Clear();errors.Clear();SessionState.SetString(Key+"output",directory);
        SessionState.SetString(Key+"startScene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));SessionState.SetBool(Key+"background",Application.runInBackground);
        SessionState.SetInt(Key+"pid",System.Diagnostics.Process.GetCurrentProcess().Id);SessionState.SetFloat(Key+"deadline",(float)EditorApplication.timeSinceStartup+600);
        SessionState.SetBool(Key+"started",false);SessionState.SetBool(Key+"return",false);Subscribe();
        try{Application.runInBackground=true;EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(MainTownDestructionBuilder.Town);Write("progress.json",new{status="BOOTING"});IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(Output,"Save"));}
        catch(Exception error){Finish("FAIL",error);}
    }
    public static void Cancel(){if(Output!="")Finish("CANCELLED",new OperationCanceledException());}
    static void Pump()
    {
        if(Output==""){EditorApplication.update-=Pump;return;}if(SessionState.GetBool(Key+"return",false)){Return();return;}
        if(EditorApplication.timeSinceStartup>SessionState.GetFloat(Key+"deadline",0)){Finish("FAIL",new TimeoutException("MainScene destruction timed out."));return;}
        if(!OwnPlay){if(SessionState.GetBool(Key+"started",false)&&!EditorApplication.isPlayingOrWillChangePlaymode)Finish("INTERRUPTED",new OperationCanceledException("Play ended."));return;}
        EditorApplication.QueuePlayerLoopUpdate();
        if(!Overburst.Persistence.AccountBootstrap.Ready||PlayerContext.Instance?.CurrentActor==null||PersistentSceneFlow.Instance==null||PersistentSceneFlow.Instance.IsSwitching||PersistentSceneFlow.Instance.CurrentSubSceneName!=PersistentSceneFlow.MainSceneName)return;
        if(!SessionState.GetBool(Key+"started",false))
        {
            SessionState.SetBool(Key+"started",true);originalInput=InputSystem.settings;ownedInput=Object.Instantiate(originalInput);ownedInput.hideFlags=HideFlags.DontSave;
            ownedInput.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;ownedInput.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;InputSystem.settings=ownedInput;
            Application.logMessageReceived+=Log;routine=Trial();
        }
        if(routine==null){Finish("FAIL",new InvalidOperationException("Domain reload interrupted the trial."));return;}
        if(frame==Time.frameCount)return;frame=Time.frameCount;
        try{if(!routine.MoveNext())Finish("PASS",null);}catch(Exception error){Finish("FAIL",error);}
    }

    static IEnumerator Trial()
    {
        if(Path.GetFileName(Output).StartsWith("Collision")) { var collision=CollisionTrial(); try { while(collision.MoveNext())yield return null; } finally { (collision as IDisposable)?.Dispose(); } yield break; }
        if(Path.GetFileName(Output).StartsWith("Performance")) { var performance=PerformanceTrial(); try { while(performance.MoveNext())yield return null; } finally { (performance as IDisposable)?.Dispose(); } yield break; }
        var controller=Object.FindFirstObjectByType<MainTownDestruction>();
        Check(controller!=null&&controller.EntryCount>671,"Saved MainScene boots with placed props and Terrain trees");
        Check(controller.catalog.definitions.Length==77,"All 77 source definitions load");
        Check(controller.RuntimeTerrain!=controller.OriginalTerrain&&controller.terrain.terrainData==controller.RuntimeTerrain,"Authored Terrain is isolated from Play changes");
        Write("runtime-baseline.json",new{controller.EntryCount,controller.ActiveTargetCount,controller.CreatedTargetCount,terrainBytes=UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(controller.RuntimeTerrain),definitions=controller.catalog.definitions.Select(d=>new{name=d.source.name,pieces=d.debris.GetComponent<MainTownDebris>().bodies.Length})});
        var actor=PlayerContext.Instance.CurrentActor;
        var weapon=AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
        Check(actor.Equipment.EquipWeaponItem(new ItemData(weapon,1,ItemGrade.Common)),"Real greatsword equips");
        typeof(PlayerEquipment).GetMethod("SetElementGem",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(actor.Equipment,new object[]{null});
        PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);var melee=actor.PlayerKit.MeleeRuntime;melee.SetManualInputEnabled(true);
        var cameraRoot=new GameObject("Owned MainScene destruction evidence camera");var camera=cameraRoot.AddComponent<Camera>();camera.CopyFrom(Camera.main);camera.enabled=false;camera.fieldOfView=48;
        var data=cameraRoot.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();data.renderPostProcessing=true;data.renderShadows=true;
        var rt=new RenderTexture(960,540,24);rt.Create();var texture=new Texture2D(960,540,TextureFormat.RGB24,false);
        void Capture(string file){var prior=RenderTexture.active;try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,960,540),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(Output,file),texture.EncodeToPNG());}finally{camera.targetTexture=null;RenderTexture.active=prior;}}
        try
        {
            string[] examples={"DF_Barrel_Wood_01","DF_Wood_Box_05","DF_Storage_Jar_03","DF_Broken_Tree_Trunk_01","DF_Aspen_01_Dense","DF_Pine_01","DF_FallenDeadTree_01","DF_Branch_Group_01"};
            for(int example=0;example<examples.Length;example++)
            {
                int index=Enumerable.Range(0,controller.EntryCount).Where(i=>controller.catalog.definitions[controller.DefinitionFor(i)].source.name==examples[example]).OrderBy(i=>Vector3.Distance(controller.PositionFor(i),actor.transform.position)).First();
                controller.RestoreAll();var position=controller.PositionFor(index);var definition=controller.catalog.definitions[controller.DefinitionFor(index)];
                var direction=Approach(controller,index,actor.transform,5f);string id=example.ToString("00")+"-"+examples[example];
                ActorTeleportUtility.TeleportSafely(actor.transform,position-direction*9,Quaternion.LookRotation(direction));float until=Time.time+.7f;while(Time.time<until)yield return null;
                controller.RefreshNearby(actor.transform.position);var request=new WeaponActionRequest(WeaponActionSource.PlayerInput,null,direction);
                Check(melee.TryStartAction(request,out _)==WeaponActionResult.Accepted,id+": distant weak starts");until=Time.time+1.6f;while(Time.time<until)yield return null;
                Check(!controller.IsBroken(index),id+": out-of-range weak leaves original intact");
                ActorTeleportUtility.TeleportSafely(actor.transform,position-direction*1.5f,Quaternion.LookRotation(direction));until=Time.time+.7f;while(Time.time<until)yield return null;controller.RefreshNearby(actor.transform.position);
                var target=controller.TargetFor(index);Check(target!=null,id+": local neutral target activates");float hp=target.CurrentHp,actorHp=actor.Health.CurrentHp;
                float height=Mathf.Clamp(definition.bounds.size.y,.8f,5f);camera.transform.position=position-direction*7+Vector3.Cross(Vector3.up,direction)*4+Vector3.up*(height*.65f+2);camera.transform.LookAt(position+Vector3.up*(height*.35f));Capture(id+"-before.png");
                Check(melee.TryStartAction(request,out _)==WeaponActionResult.Accepted,id+": actual weak starts");int shot=0;float next=0;until=Time.time+3.8f;
                while(Time.time<until){if(Time.time>=next){Capture(id+"-frame-"+shot++.ToString("000")+".png");next=Time.time+.1f;}yield return null;}
                Check(controller.IsBroken(index)&&!target.gameObject.activeSelf,id+": actual weak breaks original and unregisters target");
                Check(target.CurrentHp==hp&&actor.Health.CurrentHp==actorHp,id+": neutral destruction preserves target/player HP");
                Check(!controller.TryBreak(index,new DamageInfo(10,position,actor.gameObject,direction,0)),id+": repeated contact cannot break twice");
                Check(TransientVfxPool.GetStatistics(definition.debris).Active==0,id+": debris returns after three seconds");
                controller.RestoreAll();controller.RefreshNearby(actor.transform.position);Check(controller.TargetFor(index)==target,id+": restored entry retains its own target identity");
                ActorTeleportUtility.TeleportSafely(actor.transform,position-direction*5f,Quaternion.LookRotation(direction));until=Time.time+.7f;while(Time.time<until)yield return null;
                Check(melee.TryStartHeavyAttack(direction)==WeaponActionResult.Accepted,id+": real heavy starts");until=Time.time+2.5f;while(Time.time<until)yield return null;
                Check(controller.IsBroken(index),id+": real heavy breaks restored original");
                until=Time.time+2f;while(Time.time<until)yield return null;controller.RestoreAll();
                Write("progress.json",new{status="ACTUAL_ATTACKS",completed=example+1,total=examples.Length,checks=checks.Count});
            }
            var costs=new List<object>();
            for(int d=0;d<controller.catalog.definitions.Length;d++)
            {
                var definition=controller.catalog.definitions[d];int index=Enumerable.Range(0,controller.EntryCount).First(i=>controller.DefinitionFor(i)==d);var position=controller.PositionFor(index);
                controller.RestoreAll();controller.RefreshNearby(position);var own=controller.TargetFor(index);Check(own!=null,definition.source.name+": entry target activates");
                controller.RefreshNearby(position+Vector3.one*10000);controller.RefreshNearby(position);Check(controller.TargetFor(index)==own,definition.source.name+": movement never rebinds a stale target to another entry");
                Check(!controller.TryBreak(index,new DamageInfo(10,position,actor.gameObject,Vector3.forward,0,false,false,true)),definition.source.name+": DoT excluded");
                Check(!controller.TryBreak(index,new DamageInfo(0,position,actor.gameObject,Vector3.forward,0)),definition.source.name+": zero damage excluded");
                TransientVfxPool.PrepareOne(definition.debris,2);TransientVfxPool.PrepareOne(definition.debris,2);var before=TransientVfxPool.GetStatistics(definition.debris);
                var clock=System.Diagnostics.Stopwatch.StartNew();Check(controller.TryBreak(index,new DamageInfo(10,position,actor.gameObject,Vector3.forward,0)),definition.source.name+": direct contact breaks once");clock.Stop();
                var after=TransientVfxPool.GetStatistics(definition.debris);Check(after.Created==before.Created&&after.Misses==before.Misses,definition.source.name+": warmed contact reuses existing debris");
                costs.Add(new{name=definition.source.name,terrain=controller.IsTerrainTree(index),milliseconds=clock.Elapsed.TotalMilliseconds,pieces=definition.debris.GetComponent<MainTownDebris>().bodies.Length});
                if(d%8==7||d==controller.catalog.definitions.Length-1){float until=Time.time+3.4f;while(Time.time<until)yield return null;Check(controller.catalog.definitions.Take(d+1).All(x=>TransientVfxPool.GetStatistics(x.debris).Active==0),"Completed debris batch returns to idle pool");}
            }
            controller.RestoreAll();Check(controller.BrokenCount==0,"All authored originals restore");
            var sw=System.Diagnostics.Stopwatch.StartNew();for(int i=0;i<200;i++)controller.RefreshNearby(actor.transform.position);sw.Stop();
            Write("performance.json",new{scope="Editor; warmed single-contact main-thread Stopwatch; excludes capture and Player FPS",costs,nearbyRefreshMeanMs=sw.Elapsed.TotalMilliseconds/200,controller.ActiveTargetCount,controller.CreatedTargetCount,controller.EntryCount});
            var original=controller.OriginalTerrain;var terrain=controller.terrain;controller.enabled=false;Check(controller.ActiveTargetCount==0,"Disable unregisters nearby targets");controller.enabled=true;controller.RefreshNearby(actor.transform.position);Check(controller.ActiveTargetCount>0,"Reenable rebuilds nearby targets");
            Object.Destroy(controller.gameObject);yield return null;Check(terrain.terrainData==original&&terrain.GetComponent<TerrainCollider>().terrainData==original,"Controller destruction returns original Terrain and its collider");
            Check(errors.Count==0,"No new runtime errors");
        }
        finally{rt.Release();Object.Destroy(rt);Object.Destroy(texture);Object.Destroy(cameraRoot);}
    }
    static Vector3 Approach(MainTownDestruction controller,int index,Transform actor,float distance)
    {
        var position=controller.PositionFor(index);var roots=controller.placements.Where(p=>p.root!=null).Select(p=>p.root).ToArray();
        for(int i=0;i<36;i++)
        {
            var direction=Quaternion.AngleAxis(i*10,Vector3.up)*Vector3.forward;var start=position-direction*distance;var bottom=start+Vector3.up*.55f;var top=start+Vector3.up*1.65f;
            bool Blocks(Collider c)=>!(c is TerrainCollider)&&!c.transform.IsChildOf(actor)&&!roots.Any(r=>(r.position-position).sqrMagnitude<.01f&&c.transform.IsChildOf(r));
            if(Physics.OverlapCapsule(bottom,top,.35f,~0,QueryTriggerInteraction.Ignore).Any(Blocks))continue;
            if(Physics.CapsuleCastAll(bottom,top,.35f,direction,2f,~0,QueryTriggerInteraction.Ignore).Any(h=>Blocks(h.collider)))continue;
            return direction;
        }
        throw new InvalidOperationException("No clear approach for "+controller.catalog.definitions[controller.DefinitionFor(index)].source.name);
    }
    static void Log(string text,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(text);}
    static void Finish(string status,Exception error)
    {
        try{(routine as IDisposable)?.Dispose();routine=null;}
        finally{Application.logMessageReceived-=Log;if(originalInput!=null)InputSystem.settings=originalInput;if(ownedInput!=null)Object.DestroyImmediate(ownedInput);originalInput=null;ownedInput=null;Write("play-result.json",new{status,error=error?.ToString(),checks,errors});SessionState.SetBool(Key+"return",true);SessionState.SetFloat(Key+"returnAfter",(float)EditorApplication.timeSinceStartup+.5f);if(OwnPlay)EditorApplication.ExitPlaymode();}
    }
    static void Return()
    {
        if(OwnPlay){EditorApplication.ExitPlaymode();return;}if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.timeSinceStartup<SessionState.GetFloat(Key+"returnAfter",0))return;
        if(System.Diagnostics.Process.GetCurrentProcess().Id!=SessionState.GetInt(Key+"pid",0))throw new InvalidOperationException("Editor owner changed.");
        string expected=Path.Combine(Output,"Save"),current=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)??"",prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","");
        if((current!=""&&current!=expected)||(prepared!=""&&prepared!=expected)){Write("return.json",new{status="DEFERRED",reason="Foreign account preparation"});return;}
        string start=SessionState.GetString(Key+"startScene","");EditorSceneManager.playModeStartScene=start==""?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(start);Application.runInBackground=SessionState.GetBool(Key+"background",false);IsolatedSavePlayGuard.UseRealAccount();
        Write("return.json",new{status=IsolatedSavePlayGuard.RequiresAccountChoice?"FAIL":"PASS",IsolatedSavePlayGuard.RequiresAccountChoice,IsolatedSavePlayGuard.ActiveDirectory,environment=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""),expires=SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires",""),compilationFailed=EditorUtility.scriptCompilationFailed,startScene=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)});
        foreach(string suffix in new[]{"output","startScene"})SessionState.EraseString(Key+suffix);foreach(string suffix in new[]{"background","started","return"})SessionState.EraseBool(Key+suffix);foreach(string suffix in new[]{"deadline","returnAfter"})SessionState.EraseFloat(Key+suffix);SessionState.EraseInt(Key+"pid");EditorApplication.update-=Pump;
    }
}

