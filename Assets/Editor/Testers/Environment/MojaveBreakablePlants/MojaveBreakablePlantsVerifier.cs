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
public static class MojaveBreakablePlantsVerifier
{
    const string Key="Overburst.MojaveBreakablePlantsVerifier.";
    static string Output=>SessionState.GetString(Key+"output","");
    static readonly List<string> checks=new List<string>(),errors=new List<string>();
    static IEnumerator routine;static int frame;static InputSettings originalInput,ownedInput;
    static bool OwnPlay=>EditorApplication.isPlaying&&Output!=""&&IsolatedSavePlayGuard.ActiveDirectory==Path.Combine(Output,"Save");
    static MojaveBreakablePlantsVerifier(){if(Output!="")Subscribe();}
    static void Subscribe(){EditorApplication.update-=Pump;EditorApplication.update+=Pump;}
    static void Check(bool value,string text){if(!value)throw new InvalidOperationException(text);checks.Add(text);}
    static void Write(string file,object value)=>File.WriteAllText(Path.Combine(Output,file),JsonConvert.SerializeObject(value,Formatting.Indented));

    static Vector3 HeavyApproach(MojaveBreakablePlant plant,Transform actor)
    {
        // A fixed retreat can enter an existing tent or crate. Preserve the town and
        // select an unobstructed real approach for the existing heavy's 2m travel.
        for(int i=0;i<36;i++)
        {
            var direction=Quaternion.AngleAxis(i*10,Vector3.up)*plant.transform.forward;
            var start=plant.transform.position-direction*5;
            var bottom=start+Vector3.up*.55f;var top=start+Vector3.up*1.65f;
            bool Blocks(Collider collider)=>!(collider is TerrainCollider)&&!collider.transform.IsChildOf(plant.transform)&&!collider.transform.IsChildOf(actor);
            if(Physics.OverlapCapsule(bottom,top,.35f,~0,QueryTriggerInteraction.Ignore).Any(Blocks))continue;
            if(Physics.CapsuleCastAll(bottom,top,.35f,direction,2f,~0,QueryTriggerInteraction.Ignore).Any(hit=>Blocks(hit.collider)))continue;
            return direction;
        }
        throw new InvalidOperationException(plant.name+": no clear heavy approach; preserve town obstacles.");
    }

    public static object AssetsCheck()
    {
        MojaveBreakablePlantsBuilder.RequireIdle();var rows=new List<object>();
        foreach(string path in MojaveBreakablePlantsBuilder.PrefabPaths())
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);var plant=prefab.GetComponent<MojaveBreakablePlant>();
            if(plant==null)throw new InvalidOperationException("Missing adapter: "+path);
            var data=new SerializedObject(plant);var intact=(GameObject)data.FindProperty("intactVisual").objectReferenceValue;
            var bodies=prefab.GetComponentsInChildren<Rigidbody>(true).Where(x=>!x.transform.IsChildOf(intact.transform)).ToArray();
            if(plant.FragmentCount<3||plant.FragmentCount>4||bodies.Length!=plant.FragmentCount||bodies.Any(x=>x.gameObject.activeSelf||!x.isKinematic||x.useGravity||!(x.GetComponent<Collider>() is BoxCollider)||x.GetComponent<Collider>().enabled))throw new InvalidOperationException("Invalid dormant pieces: "+path);
            if(!intact.activeSelf||data.FindProperty("target").objectReferenceValue!=prefab.GetComponent<CombatTarget>()||data.FindProperty("intactCollider").objectReferenceValue!=prefab.GetComponent<BoxCollider>())throw new InvalidOperationException("Invalid original/target: "+path);
            if(prefab.GetComponentsInChildren<Transform>(true).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject))!=0)throw new InvalidOperationException("Missing script: "+path);
            foreach(var body in bodies)
            {
                var mesh=body.GetComponent<MeshFilter>().sharedMesh;var materials=body.GetComponent<MeshRenderer>().sharedMaterials;
                if(mesh==null||mesh.vertexCount<3||mesh.colors.Length!=mesh.vertexCount||mesh.uv.Length!=mesh.vertexCount||materials.Length!=mesh.subMeshCount||materials.Any(x=>x==null||x.shader==null||x.shader.name=="Hidden/InternalErrorShader"))throw new InvalidOperationException("Invalid fragment channels/materials: "+path);
            }
            rows.Add(new{path,pieces=plant.FragmentCount,source=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(intact),lod=intact.GetComponentInChildren<LODGroup>(true)?.lodCount??0,plant.BlocksMovement});
        }
        if(rows.Count!=14)throw new InvalidOperationException("Expected 14 inspected cactus/tree prefabs.");
        var scene=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(MojaveBreakablePlantsBuilder.TownPath);
        var group=scene.GetRootGameObjects().Single(x=>x.name==MojaveBreakablePlantsBuilder.GroupName);MojaveBreakablePlantsBuilder.ValidateGroup(group);
        string file=MojaveBreakablePlantsBuilder.Artifact+"/Evidence/native-prefab-check.json";
        File.WriteAllText(file,JsonConvert.SerializeObject(new{status="PASS",count=rows.Count,examples=group.transform.childCount,rows},Formatting.Indented));
        return new{status="PASS",count=rows.Count,examples=group.transform.childCount};
    }

    public static void Run(string directory)
    {
        MojaveBreakablePlantsBuilder.RequireIdle();
        if(Output!=""||IsolatedSavePlayGuard.RequiresAccountChoice||IsolatedSavePlayGuard.ActiveDirectory!=""||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))||SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","")!="")throw new InvalidOperationException("Another account owns this Editor.");
        directory=Path.GetFullPath(directory);string allowed=Path.GetFullPath(Path.Combine(Application.dataPath,"../../개인파일/코덱스산출"))+Path.DirectorySeparatorChar;
        if(!directory.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||Directory.Exists(directory))throw new ArgumentException("Use a fresh artifact directory.");
        Directory.CreateDirectory(directory);checks.Clear();errors.Clear();SessionState.SetString(Key+"output",directory);
        SessionState.SetString(Key+"startScene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));SessionState.SetBool(Key+"background",Application.runInBackground);
        SessionState.SetInt(Key+"pid",System.Diagnostics.Process.GetCurrentProcess().Id);SessionState.SetFloat(Key+"deadline",(float)EditorApplication.timeSinceStartup+420);
        SessionState.SetBool(Key+"started",false);SessionState.SetBool(Key+"return",false);Subscribe();
        try{Application.runInBackground=true;EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(MojaveBreakablePlantsBuilder.TownPath);Write("progress.json",new{status="BOOTING"});IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(Output,"Save"));}
        catch(Exception error){Finish("FAIL",error);}
    }
    public static void Cancel(){if(Output!="")Finish("CANCELLED",new OperationCanceledException());}
    static void Pump()
    {
        if(Output==""){EditorApplication.update-=Pump;return;}if(SessionState.GetBool(Key+"return",false)){Return();return;}
        if(EditorApplication.timeSinceStartup>SessionState.GetFloat(Key+"deadline",0)){Finish("FAIL",new TimeoutException("Mojave trial timed out."));return;}
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
        var group=GameObject.Find(MojaveBreakablePlantsBuilder.GroupName);Check(group!=null,"Saved Mojave group loads through real MainScene boot");
        var plants=group.GetComponentsInChildren<MojaveBreakablePlant>();Check(plants.Length==MojaveBreakablePlantsBuilder.Examples.Length,"All eight representative cacti/trees load");
        var actor=PlayerContext.Instance.CurrentActor;var weapon=AssetDatabase.LoadAssetAtPath<WeaponItemData>("Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS01_AzureStarblade/GRS01_AzureStarblade.asset");
        Check(actor.Equipment.EquipWeaponItem(new ItemData(weapon,1,ItemGrade.Common)),"Existing greatsword equips in isolated account");
        typeof(PlayerEquipment).GetMethod("SetElementGem",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(actor.Equipment,new object[]{null});
        PlayerCombatModeController.GetOrCreate().EnterCombatMode(PlayerCombatModeReason.System);var melee=actor.PlayerKit.MeleeRuntime;melee.SetManualInputEnabled(true);
        var cameraRoot=new GameObject("Owned Mojave trial evidence camera");var camera=cameraRoot.AddComponent<Camera>();camera.CopyFrom(Camera.main);camera.enabled=false;camera.fieldOfView=42;
        var cameraData=cameraRoot.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();cameraData.renderPostProcessing=true;cameraData.renderShadows=true;
        var rt=new RenderTexture(960,540,24);rt.Create();var texture=new Texture2D(960,540,TextureFormat.RGB24,false);GameObject paletteRoot=null;
        void Capture(string file){var prior=RenderTexture.active;try{camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;texture.ReadPixels(new Rect(0,0,960,540),0,0);texture.Apply();File.WriteAllBytes(Path.Combine(Output,file),texture.EncodeToPNG());}finally{camera.targetTexture=null;RenderTexture.active=prior;}}
        void Aim(MojaveBreakablePlant plant){float height=plant.GetComponent<BoxCollider>().size.y;camera.transform.position=plant.transform.position+new Vector3(2.1f,Mathf.Max(2.1f,height+1),4.2f);camera.transform.LookAt(plant.transform.position+Vector3.up*Mathf.Max(.4f,height*.4f));}
        try
        {
            var center=plants.Select(p=>p.transform.position).Aggregate(Vector3.zero,(sum,p)=>sum+p)/plants.Length;
            camera.transform.position=center+new Vector3(10,18,20);camera.transform.LookAt(center);Capture("overview-before.png");
            for(int index=0;index<plants.Length;index++)
            {
                var plant=plants[index];plant.ResetPlant();string id=MojaveBreakablePlantsBuilder.Examples[index];var direction=plant.transform.forward;
                var request=new WeaponActionRequest(WeaponActionSource.PlayerInput,null,direction);float until;
                ActorTeleportUtility.TeleportSafely(actor.transform,plant.transform.position-direction*7,Quaternion.LookRotation(direction));until=Time.time+.6f;while(Time.time<until)yield return null;
                Check(melee.TryStartAction(request,out _)==WeaponActionResult.Accepted,id+": distant weak starts");until=Time.time+1.6f;while(Time.time<until)yield return null;
                Check(!plant.Broken,id+": distant attack does not break plant");
                ActorTeleportUtility.TeleportSafely(actor.transform,plant.transform.position-direction*1.5f,Quaternion.LookRotation(direction));until=Time.time+.6f;while(Time.time<until)yield return null;
                Aim(plant);Capture(id+"-before.png");int priorBreaks=plant.BreakCount;float hp=plant.CurrentHp,actorHp=actor.Health.CurrentHp;
                Check(melee.TryStartAction(request,out _)==WeaponActionResult.Accepted,id+": actual close weak starts");int shot=0;float next=0;until=Time.time+2;
                while(Time.time<until){if(Time.time>=next){Capture(id+"-frame-"+shot++.ToString("000")+".png");next=Time.time+.08f;}yield return null;}
                Check(plant.Broken&&plant.BreakCount==priorBreaks+1&&!plant.BlocksMovement,id+": actual weak breaks once and removes blocking");
                var bodies=plant.GetComponentsInChildren<Rigidbody>();Check(bodies.Length==plant.FragmentCount&&bodies.All(x=>!x.isKinematic&&x.useGravity),id+": all three/four reusable pieces move with gravity");
                Check(plant.CurrentHp==hp&&actor.Health.CurrentHp==actorHp,id+": plant and player HP remain unchanged");
                ((IDamageable)plant).TakeDamage(new DamageInfo(20,plant.transform.position,actor.gameObject,direction,0));Check(plant.BreakCount==priorBreaks+1,id+": repeated contact cannot break twice");
                until=Time.time+1.3f;
                while(Time.time<until){if(Time.time>=next){Capture(id+"-frame-"+shot++.ToString("000")+".png");next=Time.time+.08f;}yield return null;}
                Check(plant.DebrisCleared&&plant.GetComponentsInChildren<Rigidbody>().Length==0,id+": pieces clear after three seconds");Capture(id+"-cleared.png");
                plant.ResetPlant();Check(!plant.Broken&&plant.GetComponent<CombatTarget>().enabled,id+": reset restores original and target");Capture(id+"-reset.png");
                Write(id+"-attack-position.json",new{plant=new[]{plant.transform.position.x,plant.transform.position.y,plant.transform.position.z},afterWeak=new[]{actor.transform.position.x,actor.transform.position.y,actor.transform.position.z},distanceAfterWeak=Vector3.Dot(plant.transform.position-actor.transform.position,direction)});
                var heavyDirection=HeavyApproach(plant,actor.transform);
                ActorTeleportUtility.TeleportSafely(actor.transform,plant.transform.position-heavyDirection*5f,Quaternion.LookRotation(heavyDirection));
                until=Time.time+.6f;while(Time.time<until)yield return null;Check(melee.TryStartHeavyAttack(heavyDirection)==WeaponActionResult.Accepted,id+": uncharged heavy starts");
                until=Time.time+2.5f;while(Time.time<until&&!plant.Broken)yield return null;
                Write(id+"-heavy-position.json",new{startDistance=5f,direction=new[]{heavyDirection.x,heavyDirection.y,heavyDirection.z},actorAfterHeavy=new[]{actor.transform.position.x,actor.transform.position.y,actor.transform.position.z},plant.Broken,plant.BreakCount});
                Check(plant.Broken&&plant.BreakCount==priorBreaks+2,id+": actual heavy breaks restored plant");
                until=Time.time+1;while(Time.time<until)yield return null;plant.ResetPlant();Write("progress.json",new{status="TESTING",completed=index+1,total=plants.Length,id,checks=checks.Count});
            }
            paletteRoot=new GameObject("Owned fourteen cactus/tree variant probe");paletteRoot.transform.position=plants[0].transform.position+Vector3.up*12;
            var variants=MojaveBreakablePlantsBuilder.PrefabPaths().Select((path,index)=>{var instance=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path),paletteRoot.transform);instance.transform.localPosition=new Vector3(index%8*3,0,index/8*3);return instance.GetComponent<MojaveBreakablePlant>();}).ToArray();
            Check(variants.Length==14,"All fourteen cactus/tree variants instantiate in Play");
            foreach(var plant in variants){((IDamageable)plant).TakeDamage(new DamageInfo(20,plant.transform.position,actor.gameObject,Vector3.forward,0,false,false,true));Check(!plant.Broken,plant.name+": damage-over-time is ignored");((IDamageable)plant).TakeDamage(new DamageInfo(20,plant.transform.position,actor.gameObject,Vector3.forward,0));Check(plant.Broken&&plant.BreakCount==1,plant.name+": direct damage activates its own pieces");}
            float deadline=Time.time+3.4f;while(Time.time<deadline)yield return null;Check(variants.All(p=>p.DebrisCleared),"All fourteen cactus/tree variants clear their pieces");foreach(var plant in variants)plant.ResetPlant();Check(variants.All(p=>!p.Broken&&p.GetComponent<CombatTarget>().enabled),"All fourteen cactus/tree variants restore");Object.Destroy(paletteRoot);paletteRoot=null;
            foreach(var plant in plants)((IDamageable)plant).TakeDamage(new DamageInfo(20,plant.transform.position,actor.gameObject,Vector3.forward,0));
            var keyboard=InputSystem.AddDevice<Keyboard>("OwnedMojaveProbeKeyboard");
            try{InputSystem.QueueStateEvent(keyboard,new KeyboardState());for(int i=0;i<4;i++)yield return null;InputSystem.QueueStateEvent(keyboard,new KeyboardState(UnityEngine.InputSystem.Key.F9));for(int i=0;i<8;i++)yield return null;InputSystem.QueueStateEvent(keyboard,new KeyboardState());Check(plants.All(p=>!p.Broken),"F9 restores all eight Mojave cactus/tree examples");}
            finally{InputSystem.RemoveDevice(keyboard);}
            camera.transform.position=center+new Vector3(10,18,20);camera.transform.LookAt(center);Capture("overview-reset.png");Check(errors.Count==0,"No new runtime errors");
        }
        finally{if(paletteRoot!=null)Object.Destroy(paletteRoot);rt.Release();Object.Destroy(rt);Object.Destroy(texture);Object.Destroy(cameraRoot);}
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
