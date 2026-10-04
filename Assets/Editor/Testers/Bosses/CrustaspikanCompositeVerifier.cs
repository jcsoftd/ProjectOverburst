using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class CrustaspikanCompositeVerifier
{
    const string Key="Overburst.CrustaspikanCompositeVerifier.";
    sealed class Plan
    {public string output,phase,token,fixture,previousStart;public bool realPlayer,background,extras,geometry;public float captureDelta,timeScale;public double deadline;public JArray scenes;}
    static Plan plan;
    static readonly List<Object> owned=new List<Object>();
    static readonly JArray cases=new JArray();
    static EnemyMotor host;
    static Coroutine routine;
    static string failure;
    static string Account=>Path.Combine(plan.output,"Account");
    static bool Same(string a,string b)=>!string.IsNullOrEmpty(a)&&!string.IsNullOrEmpty(b)&&string.Equals(Path.GetFullPath(a).TrimEnd('/','\\'),Path.GetFullPath(b).TrimEnd('/','\\'),StringComparison.OrdinalIgnoreCase);
    static JArray Scenes()=>new JArray(Enumerable.Range(0,SceneManager.sceneCount).Select(i=>SceneManager.GetSceneAt(i)).Select(s=>new JObject{["path"]=s.path,["dirty"]=s.isDirty,["roots"]=s.rootCount}));
    static void Require(bool pass,string text){if(!pass)throw new InvalidOperationException(text);}
    static void Persist()=>SessionState.SetString(Key+"plan",JsonConvert.SerializeObject(plan));
    static void Result(string state)=>File.WriteAllText(Path.Combine(plan.output,"result.json"),new JObject{["status"]=state,["failure"]=failure,["realPlayer"]=plan.realPlayer,["cases"]=cases}.ToString());
    static CrustaspikanCompositeVerifier()
    {
        string saved=SessionState.GetString(Key+"plan","");if(!string.IsNullOrEmpty(saved))plan=JsonConvert.DeserializeObject<Plan>(saved);
        EditorApplication.update+=Tick;EditorApplication.playModeStateChanged+=Changed;AssemblyReloadEvents.beforeAssemblyReload+=Reload;
    }
    public static string Start(string output,bool realPlayer=false,bool extras=false,bool geometry=false)
    {
        Require(plan==null&&!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling&&!EditorApplication.isUpdating,"Idle Editor required.");
        Require(string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)&&string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))&&string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","")),"Unoccupied account required.");
        output=Path.GetFullPath(output);string allowed=Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath).Parent.FullName,"개인파일/코덱스산출"))+Path.DirectorySeparatorChar;
        Require(output.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)&&!File.Exists(Path.Combine(output,"plan.json")),"Fresh private output required.");Directory.CreateDirectory(output);
        plan=new Plan{output=output,phase="booting",token=Guid.NewGuid().ToString("N"),realPlayer=realPlayer,extras=extras,geometry=geometry,background=Application.runInBackground,captureDelta=Time.captureDeltaTime,timeScale=Time.timeScale,deadline=EditorApplication.timeSinceStartup+1200,scenes=Scenes(),previousStart=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)};
        plan.fixture=realPlayer?"Assets/ProjectOverburst/00_Scenes/PersistentScene.unity":"Assets/Editor/Testers/Bosses/CrustaspikanCompositeFixture_"+plan.token+".unity";
        cases.Clear();failure=null;Persist();File.WriteAllText(Path.Combine(output,"plan.json"),JsonConvert.SerializeObject(plan,Formatting.Indented));
        Scene active=SceneManager.GetActiveScene(),fixture=default;
        try
        {
            if(!realPlayer){fixture=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);Require(EditorSceneManager.SaveScene(fixture,plan.fixture),"Fixture save failed.");EditorSceneManager.CloseScene(fixture,true);fixture=default;}
            SceneManager.SetActiveScene(active);EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(plan.fixture);Application.runInBackground=true;IsolatedSavePlayGuard.EnterIsolatedPlay(Account);return Path.Combine(output,"plan.json");
        }
        catch(Exception error){if(fixture.IsValid())EditorSceneManager.CloseScene(fixture,true);if(active.IsValid())SceneManager.SetActiveScene(active);Return(error.ToString());throw;}
    }
    static bool OwnPlay=>plan!=null&&EditorApplication.isPlaying&&Same(IsolatedSavePlayGuard.ActiveDirectory,Account);
    static void Tick()
    {
        if(plan==null)return;
        try
        {
            if(plan.phase=="booting"&&EditorApplication.isPlaying)
            {Require(OwnPlay,"Account mismatch.");var root=new GameObject("Crustaspikan material verifier");owned.Add(root);host=root.AddComponent<EnemyMotor>();root.GetComponent<Rigidbody>().isKinematic=true;plan.phase="running";Persist();routine=host.StartCoroutine(Drive(CompositeCases()));}
            if(plan.phase=="running"){Require(OwnPlay,"Own Play interrupted.");EditorApplication.QueuePlayerLoopUpdate();}
            if(plan.phase!="returning"&&EditorApplication.timeSinceStartup>plan.deadline)Return("Verifier timeout.");
            if(plan.phase=="returning")FinishReturn();
        }
        catch(Exception error){Return(error.ToString());}
    }
    static IEnumerator Drive(IEnumerator body)
    {
        var stack=new Stack<IEnumerator>();stack.Push(body);
        while(stack.Count>0)
        {
            var current=stack.Peek();bool more;object value;
            try{more=current.MoveNext();value=more?current.Current:null;}
            catch(Exception error){while(stack.Count>0)try{(stack.Pop() as IDisposable)?.Dispose();}catch{}Return(error.ToString());yield break;}
            if(!more){stack.Pop();(current as IDisposable)?.Dispose();continue;}
            if(value is IEnumerator nested){stack.Push(nested);continue;}yield return value;
        }
        Return(null);
    }
    static void Position(Transform transform,Vector3 position)
    {transform.position=position;var body=transform.GetComponent<Rigidbody>();if(body!=null){body.position=position;body.linearVelocity=Vector3.zero;body.angularVelocity=Vector3.zero;}Physics.SyncTransforms();}
    static EnemySpawnService Service(EnemyBossMaterialCollection collection)
    {
        var existing=EnemySpawnService.Current;if(existing!=null){Require(existing.RegisterAdditionalCatalog(collection.catalog,out var reason),reason);return existing;}
        var root=new GameObject("Owned boss material services");owned.Add(root);var inactive=new GameObject("Pool").transform;inactive.SetParent(root.transform,false);inactive.gameObject.SetActive(false);
        var pool=root.AddComponent<EnemyPoolService>();pool.Configure(inactive,0);var service=root.AddComponent<EnemySpawnService>();service.Configure(collection.catalog,pool);return service;
    }
    static EnemyActor Spawn(EnemySpawnService service,EnemyBossMaterialCollection collection,Vector3 position,Transform target)
    {
        Require(service.TrySpawn(new EnemySpawnRequest(collection.actorDefinition,position,Quaternion.identity,target,context:EncounterContext.Test),out var actor),"Saved actor spawn failed.");
        actor.AI.enabled=false;actor.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;return actor;
    }

    static Camera overview;
    static Material floorMaterial,bodyMaterial;
    static EnemyBossCompositePatternExecutor composite;
    static readonly JArray hits=new JArray();
    static CombatTarget victimTarget;
    static void Record(string scenario,EnemyBossAttackMaterial material,JObject details=null)
    {var row=details??new JObject();row["pass"]=true;row["scenario"]=scenario;row["id"]=material!=null?material.materialId:"";cases.Add(row);Result("RUNNING");}
    static void Capture(string path)
    {
        RenderTexture render=null;Texture2D pixels=null;var previous=RenderTexture.active;
        try{
            render=new RenderTexture(960,540,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);render.Create();
            RenderPipeline.SubmitRenderRequest(overview,new UniversalRenderPipeline.SingleCameraRequest{destination=render});
            RenderTexture.active=render;pixels=new Texture2D(960,540,TextureFormat.RGBA32,false);pixels.ReadPixels(new Rect(0,0,960,540),0,0,false);pixels.Apply(false,false);File.WriteAllBytes(path,pixels.EncodeToPNG());
        }finally{RenderTexture.active=previous;if(render!=null){render.Release();Object.DestroyImmediate(render);}if(pixels!=null)Object.DestroyImmediate(pixels);}
    }
    static IEnumerator Cast(EnemyActor actor,EnemyBossAttackMaterial material,GameObject victim,string scenario,int expectedSummons=0,bool movie=false)
    {
        composite=actor.GetComponent<EnemyBossCompositePatternExecutor>();hits.Clear();var capsule=victim.GetComponent<CapsuleCollider>();
        foreach(var collider in actor.GetComponentsInChildren<Collider>(true))Physics.IgnoreCollision(collider,capsule);
        Position(victim.transform,new Vector3(0,.035f,12f));yield return null;yield return new WaitForFixedUpdate();
        int initialSpawn=composite.SummonedCount,initialDamage=composite.DamageCount;float start=Time.time;bool stopped=false,wallBuilt=false;
        GameObject wall=null;int peakParticles=0,peakFlights=0;var beamPhases=new HashSet<int>();
        Require(actor.AbilityController.TryStartAbility(material.ability,victim.transform),"Composite start rejected: "+scenario+" / "+material.name);
        string variant=material.delivery==EnemyBossMaterialDelivery.Boulder?(composite.IsEliteHeld?"-elite":"-rock"):"";
        string frames=Path.Combine(plan.output,"Frames",material.runtimeClip.name+"-"+scenario+variant);if(movie)Directory.CreateDirectory(frames);float nextCapture=Time.time;int frame=0;
        float limit=Time.time+material.runtimeClip.length+5f;
        while(composite.IsExecuting){
            Require(Time.time<limit,"Composite timeout: "+composite.LastFailure);float t=composite.NormalizedTime;
            if(scenario=="evade" && t>=material.strikes[0].contactStart-.04f)Position(victim.transform,new Vector3(20f,.035f,-12f));
            if(scenario=="cancel" && t>=material.strikes[0].contactStart*.5f){actor.AbilityController.Cancel();stopped=true;}
            if(scenario=="air-cancel" && composite.ActiveFlightCount>0){actor.AbilityController.Cancel();stopped=true;}
            if(scenario=="death" && composite.ActiveFlightCount>0){actor.Health.TakeDamage(new DamageInfo(1000000f,actor.transform.position,victim,Vector3.forward));stopped=true;}
            if(scenario=="wall" && !wallBuilt && t>=material.strikes[0].contactStart-.04f){wall=GameObject.CreatePrimitive(PrimitiveType.Cube);owned.Add(wall);wall.name="Owned stream blocker";wall.transform.position=new Vector3(0,4,6);wall.transform.localScale=new Vector3(10,8,.5f);Physics.SyncTransforms();wallBuilt=true;}
            peakParticles=Mathf.Max(peakParticles,composite.BloodParticleCount);peakFlights=Mathf.Max(peakFlights,composite.ActiveFlightCount);
            if(composite.ActiveBeamPhase>=0)beamPhases.Add(composite.ActiveBeamPhase);
            if(movie && Time.time>=nextCapture){Capture(Path.Combine(frames,frame++.ToString("D4")+".png"));nextCapture+=.125f;}
            yield return new WaitForFixedUpdate();
        }
        if(movie){float until=Time.time+1.4f;while(Time.time<until){if(Time.time>=nextCapture){Capture(Path.Combine(frames,frame++.ToString("D4")+".png"));nextCapture+=.125f;}yield return null;}}
        if(stopped){int count=hits.Count;int spawned=composite.SummonedCount;yield return new WaitForSeconds(1.4f);Require(hits.Count==count&&composite.SummonedCount==spawned&&composite.ActiveFlightCount==0&&composite.ActiveVisualCount==0,"Cancelled/dead cast left damage or payload.");}
        Require(composite.LastFailure==null,"Execution failure: "+composite.LastFailure);
        if(scenario=="contact"){
            Require(hits.Count==material.strikes.Length,"Damage phase count mismatch: "+material.name+" / "+hits.Count);
            for(int i=0;i<material.strikes.Length;i++)Require(hits.OfType<JObject>().Count(x=>(int)x["phase"]==i)==1,"Repeated damage within a sweep phase.");
            Require(composite.SummonedCount-initialSpawn==expectedSummons,"Monster count mismatch.");
            if(material.delivery==EnemyBossMaterialDelivery.Spit)Require(peakParticles>0 && beamPhases.Count==material.strikes.Length,"Blood pack stream/sweep phases not executed.");
        }
        if(scenario=="evade"||scenario=="wall"||scenario=="cancel")Require(hits.Count==0,"Damage through evade/wall/cancel.");
        if(scenario=="cancel")Require(composite.SummonedCount==initialSpawn,"Summons before cancelled emission.");
        var details=new JObject{["damageEvents"]=hits.DeepClone(),["summoned"]=composite.SummonedCount-initialSpawn,["peakParticles"]=peakParticles,["peakFlights"]=peakFlights,["beamPhases"]=new JArray(beamPhases),["seconds"]=Time.time-start,["movieFrames"]=frame};
        if(wall!=null)Object.Destroy(wall);Record(scenario,material,details);
    }
    static IEnumerator CompositeCases()
    {
        Time.captureDeltaTime=1f/60;
        var collection=AssetDatabase.LoadAssetAtPath<EnemyBossMaterialCollection>(CrustaspikanMaterialBuilder.CollectionPath);
        var set=AssetDatabase.LoadAssetAtPath<EnemyBossCompositePatternSet>(CrustaspikanCompositeBuilder.PatternPath);Require(set!=null&&set.IsValid,"Saved composite patterns invalid.");
        var service=Service(collection);Require(service.RegisterAdditionalCatalog(set.summonCatalog,out string reason),reason);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);owned.Add(floor);floor.name="Owned composite arena floor";floor.transform.position=Vector3.down*.5f;floor.transform.localScale=new Vector3(100,1,100);
        floorMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit")){color=new Color(.16f,.2f,.22f)};owned.Add(floorMaterial);floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;
        var lightRoot=new GameObject("Owned composite light");owned.Add(lightRoot);var light=lightRoot.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2f;light.transform.rotation=Quaternion.Euler(45,-30,0);
        var cameraRoot=new GameObject("Owned composite overview");owned.Add(cameraRoot);overview=cameraRoot.AddComponent<Camera>();overview.enabled=false;overview.orthographic=true;overview.orthographicSize=10f;overview.farClipPlane=150f;overview.backgroundColor=new Color(.045f,.065f,.08f);overview.clearFlags=CameraClearFlags.SolidColor;
        overview.transform.position=new Vector3(20,16,24);overview.transform.LookAt(new Vector3(0,4,5));overview.GetUniversalAdditionalCameraData().renderType=CameraRenderType.Base;
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab");var physical=prefab.GetComponentsInChildren<CapsuleCollider>(true).Single(x=>x.enabled&&!x.isTrigger);
        var victim=GameObject.CreatePrimitive(PrimitiveType.Capsule);owned.Add(victim);victim.name="Saved player physical capsule";victim.layer=prefab.layer;
        bodyMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit")){color=new Color(.9f,.6f,.2f)};owned.Add(bodyMaterial);victim.GetComponent<Renderer>().sharedMaterial=bodyMaterial;
        var capsule=victim.GetComponent<CapsuleCollider>();capsule.center=prefab.transform.InverseTransformPoint(physical.transform.TransformPoint(physical.center));capsule.radius=physical.radius;capsule.height=physical.height;
        victimTarget=victim.AddComponent<CombatTarget>();victimTarget.Configure(CombatTeam.PlayerParty,false);var volume=prefab.GetComponent<CombatTarget>().ResolveVolumeAtRootPosition(Vector3.zero);victimTarget.ConfigureVolume(volume.Center,volume.Radius,volume.HalfHeight*2);
        victim.GetComponent<CombatHealth>().SetMaxHp(100000,true);EnemyStrongAttackWarning.PlayerTarget=victimTarget;
        victim.GetComponent<CombatHealth>().OnDamaged+=(_,info)=>{if(composite!=null&&composite.CurrentMaterial!=null&&info.enemyAbility==composite.CurrentMaterial.ability)hits.Add(new JObject{["phase"]=info.sourceAttackPhaseIndex,["sequence"]=info.sourceAttackSequenceId,["damage"]=info.damage,["normalized"]=composite.NormalizedTime});};
        yield return null;yield return new WaitForFixedUpdate();EnemyActor previous=null;uint lastLease=0;
        foreach(var entry in set.spitPatterns){foreach(var scenario in new[]{"contact","evade","cancel","air-cancel"}){
            var actor=Spawn(service,collection,Vector3.up*.035f,victim.transform);composite=actor.GetComponent<EnemyBossCompositePatternExecutor>();Require(composite!=null,"Composite prefab component missing.");
            Require(actor.LeaseVersion>lastLease,"Lease did not advance.");if(previous!=null)Require(previous==actor,"Boss pool did not reuse its actor.");lastLease=actor.LeaseVersion;previous=actor;
            yield return Cast(actor,entry.material,victim,scenario,3,scenario=="contact");
            int live=composite.LiveAddCount;if(scenario=="contact")Require(live==3,"Live add count mismatch.");
            service.Release(actor);yield return null;Require(composite.ActiveFlightCount==0&&composite.ActiveVisualCount==0&&composite.LiveAddCount==0&&composite.BloodParticleCount==0,"Pool return leaked composite objects.");Record("pool-return",entry.material);
        }}
        var blocked=Spawn(service,collection,Vector3.up*.035f,victim.transform);yield return Cast(blocked,set.spitPatterns[1].material,victim,"wall");service.Release(blocked);yield return null;
        foreach(var payload in new[]{EnemyBossThrowPayload.Rock,EnemyBossThrowPayload.Elite}){
            var actor=Spawn(service,collection,Vector3.up*.035f,victim.transform);composite=actor.GetComponent<EnemyBossCompositePatternExecutor>();composite.SetNextThrowPayload(payload);
            yield return Cast(actor,set.throwMaterial,victim,"contact",payload==EnemyBossThrowPayload.Elite?1:0,true);
            Require(payload==EnemyBossThrowPayload.Elite?composite.EliteThrowCount==1:composite.RockThrowCount==1,"Wrong throw payload.");
            service.Release(actor);yield return null;Require(composite.LiveAddCount==0&&composite.ActiveVisualCount==0,"Throw return leaked payload.");Record("throw-pool-return-"+payload,set.throwMaterial);
        }
        var killed=Spawn(service,collection,Vector3.up*.035f,victim.transform);killed.GetComponent<EnemyBossCompositePatternExecutor>().SetNextThrowPayload(EnemyBossThrowPayload.Elite);
        yield return Cast(killed,set.throwMaterial,victim,"death");if(killed.IsLeased)service.Release(killed);yield return null;
        var prepared=Spawn(service,collection,Vector3.up*.035f,victim.transform);composite=prepared.GetComponent<EnemyBossCompositePatternExecutor>();var basic=prepared.GetComponent<EnemyBossMaterialExecutor>();composite.SetNextThrowPayload(EnemyBossThrowPayload.Elite);
        yield return null;Require(basic.TryPlayMotion("UnearthRock",true),"Elite preparation rejected.");float limit=Time.time+10f;bool seen=false;
        while(basic.IsExecuting){Require(Time.time<limit,"Preparation timeout.");seen|=composite.IsEliteHeld;yield return null;}
        yield return new WaitForEndOfFrame();Require(seen&&composite.IsEliteHeld&&!basic.IsRockHeld,"Elite ground/held substitution failed.");Capture(Path.Combine(plan.output,"elite-extraction-held.png"));
        Record("elite-ground-extraction-and-hold",set.throwMaterial);yield return Cast(prepared,set.throwMaterial,victim,"contact",1);service.Release(prepared);yield return null;
        var tuned=Spawn(service,collection,Vector3.up*.035f,victim.transform);var runtimeSet=Object.Instantiate(set);owned.Add(runtimeSet);
        var runtimeMaterial=Object.Instantiate(set.spitPatterns[0].material);owned.Add(runtimeMaterial);runtimeMaterial.tuning.damageMultiplier=.5f;runtimeMaterial.tuning.animationSpeedMultiplier=1.5f;runtimeSet.spitPatterns[0].material=runtimeMaterial;
        tuned.GetComponent<EnemyBossCompositePatternExecutor>().Configure(runtimeSet);yield return Cast(tuned,runtimeMaterial,victim,"contact",3);
        Require(hits.OfType<JObject>().All(x=>Mathf.Abs((float)x["damage"]-5f)<.001f),"Composite damage coefficient ignored.");
        Require(Mathf.Approximately(set.spitPatterns[0].material.DamageMultiplier,1f)&&Mathf.Approximately(set.spitPatterns[0].material.AnimationSpeedMultiplier,1f),"Runtime tuning mutated authored source.");
        Record("runtime-speed-and-damage-tuning",runtimeMaterial);service.Release(tuned);yield return null;
        // Landed actors are the real pool leases and retain the encounter target after their wake lock.
        var join=Spawn(service,collection,Vector3.up*.035f,victim.transform);yield return Cast(join,set.spitPatterns[1].material,victim,"contact",3);
        composite=join.GetComponent<EnemyBossCompositePatternExecutor>();Require(composite.LiveAddCount==3,"Actual add leases missing.");
        var addActors=Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None).Where(x=>x!=join&&x.IsLeased&&x.Definition!=null&&set.summonCatalog.TryGet(x.Definition.EnemyId,out _)).ToArray();
        Require(addActors.Length==3&&addActors.All(x=>x.AI.enabled&&x.AbilityController!=null&&x.Health!=null),"Landed adds did not enter their real combat AI.");Record("landed-add-real-ai",set.spitPatterns[1].material);
        service.Release(join);yield return null;
        Require(Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None).All(x=>!x.IsLeased),"Owned fixture left leased actors.");
        Record("all-owned-leases-returned",null);
    }
    static void Return(string error)
    {
        if(plan==null)return;if(!string.IsNullOrEmpty(error))failure=error;Result(failure==null?"PASS":"FAIL");plan.phase="returning";Persist();if(OwnPlay)EditorApplication.isPlaying=false;
    }
    static void Changed(PlayModeStateChange state){if(plan!=null&&state==PlayModeStateChange.EnteredEditMode){plan.phase="returning";Persist();}}
    static void Reload(){if(plan!=null){if(plan.phase=="running"){failure="Own verifier interrupted by compilation.";plan.phase="returning";Result("FAIL");}Persist();}}
    static void FinishReturn()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        string env=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","");
        if(!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)||!string.IsNullOrEmpty(env)&&!Same(env,Account)||!string.IsNullOrEmpty(prepared)&&!Same(prepared,Account))return;
        if(host!=null&&routine!=null)host.StopCoroutine(routine);for(int i=owned.Count-1;i>=0;i--)if(owned[i]!=null)Object.DestroyImmediate(owned[i]);owned.Clear();host=null;routine=null;
        Time.captureDeltaTime=plan.captureDelta;if(Time.timeScale!=plan.timeScale)Time.timeScale=plan.timeScale;Application.runInBackground=plan.background;
        if(AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)==plan.fixture)EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(plan.previousStart)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(plan.previousStart);
        IsolatedSavePlayGuard.UseRealAccount();if(!plan.realPlayer&&plan.fixture=="Assets/Editor/Testers/Bosses/CrustaspikanCompositeFixture_"+plan.token+".unity"&&AssetDatabase.LoadAssetAtPath<SceneAsset>(plan.fixture)!=null)AssetDatabase.DeleteAsset(plan.fixture);
        var after=Scenes();File.WriteAllText(Path.Combine(plan.output,"return.json"),new JObject{["status"]=!IsolatedSavePlayGuard.RequiresAccountChoice&&JToken.DeepEquals(plan.scenes,after)?"PASS":"FAIL",["scenesBefore"]=plan.scenes,["scenesAfter"]=after,["guardChoice"]=IsolatedSavePlayGuard.RequiresAccountChoice,["active"]=IsolatedSavePlayGuard.ActiveDirectory,["prepared"]=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""),["expires"]=SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires",""),["environment"]=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),["startScene"]=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)}.ToString());
        SessionState.EraseString(Key+"plan");plan=null;
    }
}
