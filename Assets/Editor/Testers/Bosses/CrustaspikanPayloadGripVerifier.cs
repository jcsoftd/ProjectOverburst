using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Verifies saved payload grips through extraction, frozen hold, carry, release and cancellation without generating a temporary scene asset.
[InitializeOnLoad]
public static class CrustaspikanPayloadGripVerifier
{
    const string Key = "Overburst.CrustaspikanPayloadGripVerifier.plan";
    sealed class Plan { public string output, phase, previousStart; public JArray scenes; public bool background; public float capture, timeScale; public double deadline; }
    static Plan plan;
    static readonly List<Object> owned = new List<Object>();
    static readonly List<EnemyActor> leases = new List<EnemyActor>();
    static readonly JArray cases = new JArray();
    static EnemySpawnService service;
    static EnemyMotor host;
    static Coroutine routine;
    static CombatTarget previousPlayerTarget;
    static readonly Vector3 Origin = new Vector3(1600, .035f, 1600);
    static string Account => Path.Combine(plan.output, "Account");
    static bool OwnPlay => plan != null && string.Equals(IsolatedSavePlayGuard.ActiveDirectory, Account, StringComparison.OrdinalIgnoreCase);
    static bool ForeignReservation => Different(IsolatedSavePlayGuard.ActiveDirectory)
        || Different(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""))
        || Different(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable));
    static bool Different(string directory) => !string.IsNullOrEmpty(directory) && !string.Equals(directory, Account, StringComparison.OrdinalIgnoreCase);
    static JArray Scenes() => new JArray(Enumerable.Range(0, SceneManager.sceneCount).Select(i => SceneManager.GetSceneAt(i)).Select(s => new JObject { ["path"] = s.path, ["dirty"] = s.isDirty, ["roots"] = s.rootCount }));
    static void Require(bool pass, string error) { if (!pass) throw new InvalidOperationException(error); }
    static void Persist() => SessionState.SetString(Key, JsonConvert.SerializeObject(plan));
    static CrustaspikanPayloadGripVerifier()
    {
        string saved = SessionState.GetString(Key, ""); if (!string.IsNullOrEmpty(saved)) plan = JsonConvert.DeserializeObject<Plan>(saved);
        EditorApplication.update += Tick;
    }
    public static string Start(string output)
    {
        CrustaspikanMotionPlaybackBuilder.RequireIdle();
        Require(plan == null && !EditorUtility.scriptCompilationFailed, "A verifier is pending or compilation failed.");
        output = IsolatedSavePlayGuard.ValidateDirectory(output);
        Require(!Directory.Exists(output), "Fresh private evidence directory required."); Directory.CreateDirectory(output);
        plan = new Plan { output = output, phase = "booting", scenes = Scenes(), previousStart = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
            background = Application.runInBackground, capture = Time.captureDeltaTime, timeScale = Time.timeScale, deadline = EditorApplication.timeSinceStartup + 600 };
        cases.Clear(); Persist(); File.WriteAllText(Path.Combine(output, "plan.json"), JsonConvert.SerializeObject(plan, Formatting.Indented));
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        Application.runInBackground = true; IsolatedSavePlayGuard.EnterIsolatedPlay(Account); return "Started isolated payload grip checks.";
    }
    static void Tick()
    {
        if (plan == null) return;
        if (plan.phase == "returning") { Return(); return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode && !OwnPlay || ForeignReservation) return;
        if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating
            && (plan.phase == "running" || plan.phase == "booting" && IsolatedSavePlayGuard.RequiresAccountChoice))
        {
            // Play exit reloads this static verifier; retain recorded cases before returning an interrupted run.
            string evidence = Path.Combine(plan.output, "result.json");
            if (cases.Count == 0 && File.Exists(evidence))
                foreach (var row in JObject.Parse(File.ReadAllText(evidence))["cases"]) cases.Add(row.DeepClone());
            Finish("Owned Play stopped before verification completed."); return;
        }
        if (EditorApplication.timeSinceStartup > plan.deadline)
        {
            Finish("Range verification timed out."); return;
        }
        if (plan.phase != "booting" || !EditorApplication.isPlaying || !OwnPlay) return;
        plan.phase = "running"; Persist(); previousPlayerTarget = EnemyStrongAttackWarning.PlayerTarget;
        var root = new GameObject("Owned boss range verifier"); owned.Add(root); host = root.AddComponent<EnemyMotor>();
        root.GetComponent<Rigidbody>().isKinematic = true; routine = host.StartCoroutine(Drive(Run()));
    }
    static IEnumerator Drive(IEnumerator body)
    {
        var stack = new Stack<IEnumerator>(); stack.Push(body);
        while (stack.Count > 0)
        {
            bool more; object current;
            try { more = stack.Peek().MoveNext(); current = more ? stack.Peek().Current : null; }
            catch (Exception error) { foreach (var item in stack) (item as IDisposable)?.Dispose(); Finish(error.ToString()); yield break; }
            if (!more) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
            if (current is IEnumerator nested) stack.Push(nested); else yield return current;
        }
        Finish(null);
    }
    static void Position(Transform target, Vector3 point) { target.position = point; Physics.SyncTransforms(); }
    static void Record(JObject row) { row["pass"] = true; cases.Add(row); Write("RUNNING", null); }
    static void Write(string status, string error) => File.WriteAllText(Path.Combine(plan.output, "result.json"), new JObject { ["status"] = status, ["failure"] = error, ["cases"] = cases }.ToString());
    static IEnumerator Run()
    {
        float wait = Time.realtimeSinceStartup + 120f;
        while (Time.realtimeSinceStartup < wait && (PersistentSceneFlow.Instance == null || PersistentSceneFlow.Instance.IsSwitching || !WorldSessionState.IsHideout || PlayerContext.GetOrCreate().CurrentActor == null)) yield return null;
        Require(Time.realtimeSinceStartup < wait, "Isolated hideout boot timed out.");
        Require(string.Equals(Overburst.Persistence.AccountBootstrap.SaveDirectory, Account, StringComparison.OrdinalIgnoreCase), "Account differs.");
        var collection = AssetDatabase.LoadAssetAtPath<EnemyBossMaterialCollection>(CrustaspikanMaterialBuilder.CollectionPath);
        service = EnemySpawnService.Current;
        if (service == null)
        {
            var root = new GameObject("Owned range spawn service"); owned.Add(root);
            var inactive = new GameObject("Owned inactive actors"); inactive.transform.SetParent(root.transform, false); inactive.SetActive(false);
            var pool = root.AddComponent<EnemyPoolService>(); pool.Configure(inactive.transform, 0);
            service = root.AddComponent<EnemySpawnService>(); service.Configure(collection.catalog, pool);
        }
        Require(service.RegisterAdditionalCatalog(collection.catalog, out var reason), reason);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(floor); floor.transform.position = Origin + Vector3.down * .535f; floor.transform.localScale = new Vector3(100, 1, 100);
        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab");
        var physical = playerPrefab.GetComponentsInChildren<CapsuleCollider>(true).Single(c => c.enabled && !c.isTrigger);
        var victim = new GameObject("Owned saved player physical body"); owned.Add(victim); victim.layer = playerPrefab.layer;
        var capsule = victim.AddComponent<CapsuleCollider>(); capsule.center = playerPrefab.transform.InverseTransformPoint(physical.transform.TransformPoint(physical.center));
        capsule.radius = physical.radius * Mathf.Max(Mathf.Abs(physical.transform.lossyScale.x), Mathf.Abs(physical.transform.lossyScale.z)); capsule.height = physical.height * Mathf.Abs(physical.transform.lossyScale.y);
        var target = victim.AddComponent<CombatTarget>(); target.Configure(CombatTeam.PlayerParty, false);
        var volume = playerPrefab.GetComponent<CombatTarget>().ResolveVolumeAtRootPosition(Vector3.zero); target.ConfigureVolume(volume.Center, volume.Radius, volume.HalfHeight * 2);
        var health = victim.GetComponent<CombatHealth>(); health.SetMaxHp(100000, true); EnemyStrongAttackWarning.PlayerTarget = target;
        Time.timeScale = 1f; Time.captureDeltaTime = 0f;
        yield return ThrowCase(collection, target, health, capsule, EnemyBossThrowPayload.Rock, false);
        yield return ThrowCase(collection, target, health, capsule, EnemyBossThrowPayload.Elite, true);
        foreach (string scenario in new[] { "cancel-dig", "cancel-held", "target-lost", "disable-held" })
            yield return CancelCase(collection, target, scenario);
    }
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static Transform Held(EnemyBossCompositePatternExecutor executor)
    {
        var visual = typeof(EnemyBossCompositePatternExecutor).GetField("held", Private).GetValue(executor);
        return visual == null ? null : ((GameObject)visual.GetType().GetField("root").GetValue(visual)).transform;
    }
    static EnemyBossPayloadGrip Grip(EnemyBossCompositePatternExecutor executor) => (EnemyBossPayloadGrip)typeof(EnemyBossCompositePatternExecutor).GetField("eliteGrip", Private).GetValue(executor);
    static Camera reviewCamera;
    static RenderTexture reviewSurface;
    static Texture2D reviewPixels;
    static JObject Measure(EnemyActor actor, EnemyBossCompositePatternExecutor executor)
    {
        var body=Held(executor); Require(body != null, "Held payload absent.");
        var nodes=actor.Animator.GetComponentsInChildren<Transform>(true);
        var l=nodes.Single(n=>n.name=="Crustaspikan_ L Hand");var r=nodes.Single(n=>n.name=="Crustaspikan_ R Hand");
        var set=executor.Patterns;
        return new JObject{["leftError"]=Vector3.Distance(l.position,body.TransformPoint(set.eliteLeftHandGrip)),
            ["rightError"]=Vector3.Distance(r.position,body.TransformPoint(set.eliteRightHandGrip)),["gap"]=Vector3.Distance(l.position,r.position),
            ["position"]=new JArray(body.position.x,body.position.y,body.position.z),["applied"]=Grip(executor)?.IsApplied??false};
    }
    static void Capture(string group,int index,float time,JArray frames,bool close=false)
    {
        if(reviewCamera==null)
        {
            var root=new GameObject("Owned payload grip review camera");owned.Add(root);reviewCamera=root.AddComponent<Camera>();
            reviewCamera.enabled=false;reviewCamera.fieldOfView=46;reviewCamera.nearClipPlane=.1f;reviewCamera.farClipPlane=110;
            reviewCamera.clearFlags=CameraClearFlags.SolidColor;reviewCamera.backgroundColor=new Color(.08f,.10f,.13f);
            var lr=new GameObject("Owned payload review light");owned.Add(lr);var light=lr.AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.7f;light.transform.rotation=Quaternion.Euler(35,-25,0);
            reviewSurface=new RenderTexture(1200,800,24);reviewSurface.Create();reviewPixels=new Texture2D(1200,800,TextureFormat.RGB24,false);
            Directory.CreateDirectory(Path.Combine(plan.output,"Frames"));
        }
        reviewCamera.transform.position=Origin+new Vector3(20,17,25);reviewCamera.transform.LookAt(Origin+new Vector3(0,6,4));
        var previous=RenderTexture.active;
        try
        {
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(reviewCamera,new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest{destination=reviewSurface});
            RenderTexture.active=reviewSurface;reviewPixels.ReadPixels(new Rect(0,0,1200,800),0,0);reviewPixels.Apply(false,false);
            string name=group+"-"+index.ToString("D4")+".jpg";File.WriteAllBytes(Path.Combine(plan.output,"Frames",name),reviewPixels.EncodeToJPG(91));
            frames.Add(new JObject{["name"]=name,["time"]=time});
        }
        finally{RenderTexture.active=previous;}
    }
    static IEnumerator ThrowCase(EnemyBossMaterialCollection collection,CombatTarget target,CombatHealth health,CapsuleCollider capsule,EnemyBossThrowPayload kind,bool carry)
    {
        Position(target.transform,Origin+Vector3.forward*15f);
        Require(service.TrySpawn(new EnemySpawnRequest(collection.actorDefinition,Origin,Quaternion.identity,target.transform,context:EncounterContext.Test),out var actor),"Payload actor spawn failed.");
        leases.Add(actor);actor.AI.enabled=false;actor.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        foreach(var c in actor.GetComponentsInChildren<Collider>(true))Physics.IgnoreCollision(c,capsule);
        var executor=actor.GetComponent<EnemyBossCompositePatternExecutor>();var basic=actor.GetComponent<EnemyBossMaterialExecutor>();
        var material=collection.attacks.Single(m=>m.runtimeClip.name=="ThrowRock");
        var frames=new JArray();var samples=new JArray();var damage=new JArray();float start=Time.realtimeSinceStartup,next=start,deadline=start+45f;int index=0;bool buried=false;bool ownershipRecorded=false;
        Action<CombatHealth,DamageInfo> observer=(_,info)=>damage.Add(new JObject{["damage"]=info.damage,["knockdown"]=info.knocksDownPlayer});health.OnDamaged+=observer;
        try
        {
            Require(executor.TryBeginPreparation(kind,9200+(int)kind,0,target.transform),"Preparation rejected.");
            while(!executor.TryGetPreparedStartContext(out _)&&Time.realtimeSinceStartup<deadline)
            {
                yield return new WaitForEndOfFrame();
                Require(!basic.IsRockHeld && basic.SupportPresentationOwner==executor,"Composite support spawned a second held rock.");
                if(actor.AnimationBridge.TryReadMotion(basic.PlaybackHandle,out var ownedSample) && ownedSample.Normalized*ownedSample.Clip.length*ownedSample.Clip.frameRate>=EnemyBossPayloadSocket.RockRevealFrame && !ownershipRecorded)
                {
                    Require(kind==EnemyBossThrowPayload.Rock?executor.IsRockHeld:executor.IsEliteHeld,"Composite preparation did not own its held visual.");
                    Record(new JObject{["case"]=kind+"-single-presentation-owner",["basicRockActive"]=false});ownershipRecorded=true;
                }
                if(kind==EnemyBossThrowPayload.Elite&&executor.IsEliteHeld)
                {
                    var ground=(Vector3)typeof(EnemyBossCompositePatternExecutor).GetField("extractionGround",Private).GetValue(executor);
                    buried|=Held(executor).position.y<ground.y;
                    var row=Measure(actor,executor);if(actor.AnimationBridge.TryReadMotion(basic.PlaybackHandle,out var s))row["frame"]=s.Normalized*s.Clip.length*s.Clip.frameRate;
                    samples.Add(row);
                }
                if(Time.realtimeSinceStartup>=next){Capture(kind.ToString(),index++,Time.realtimeSinceStartup-start,frames);next=Time.realtimeSinceStartup+.08f;}
            }
            Require(executor.TryGetPreparedStartContext(out var context),"Preparation timed out.");yield return new WaitForEndOfFrame();
            if(kind==EnemyBossThrowPayload.Elite)
            {
                var initial=Measure(actor,executor);Require(buried&&(float)initial["leftError"]<.03f&&(float)initial["rightError"]<.03f&& !basic.IsRockHeld,"Elite grip did not fit the visible body.");
                var first=Held(executor).position;float drift=0;
                for(int n=0;n<30;n++){yield return new WaitForEndOfFrame();var row=Measure(actor,executor);drift=Mathf.Max(drift,Vector3.Distance(first,Held(executor).position));Require((float)row["leftError"]<.03f&&(float)row["rightError"]<.03f,"Frozen grip drifted from body.");}
                Require(drift<.005f,"Frozen grip fed back into payload anchor.");Record(new JObject{["case"]="frozen-elite-grip",["frames"]=30,["drift"]=drift,["grip"]=initial});
            }
            else
            {
                var held=Held(executor).gameObject;
                Require(executor.IsRockHeld&&!basic.IsRockHeld&&Mathf.Abs(held.transform.localScale.x-1.75f)<.001f&&Mathf.Abs(collection.boulderOffset.y-.9f)<.001f,"Rock size/height not applied.");
                Record(new JObject{["case"]="rock-size-and-height",["scale"]=held.transform.localScale.x,["offsetY"]=collection.boulderOffset.y,["renderDiameter"]=held.GetComponent<Renderer>().bounds.size.x});
            }
            if(carry)
            {
                foreach(var direction in new[]{Vector3.forward,Vector3.back})
                {
                    Require(executor.TryBeginCarry(direction),"Elite carry rejected.");actor.Movement.SetMoveFacingPolicy(true);actor.Movement.SetDestination(actor.transform.position+direction*3f,.2f,direction.z<0?EnemyLocomotionMode.Backpedal:EnemyLocomotionMode.Walk);
                    float until=Time.time+.8f;float maxError=0;
                    while(Time.time<until){yield return new WaitForEndOfFrame();var row=Measure(actor,executor);maxError=Mathf.Max(maxError,(float)row["leftError"],(float)row["rightError"]);if(Time.realtimeSinceStartup>=next){Capture(kind.ToString(),index++,Time.realtimeSinceStartup-start,frames);next=Time.realtimeSinceStartup+.08f;}}
                    Require(maxError<.1f,"Carry hand/body contact was lost.");actor.Movement.StopMovement();Require(executor.TryEndCarry(),"Carry stop rejected.");
                    float holdDeadline=Time.realtimeSinceStartup+3f;while(!executor.TryGetPreparedStartContext(out context)&&Time.realtimeSinceStartup<holdDeadline)yield return null;Require(executor.TryGetPreparedStartContext(out context),"Carry did not restore preparation.");
                    Record(new JObject{["case"]=direction.z<0?"elite-carry-back":"elite-carry-forward",["maximumError"]=maxError});
                }
            }
            Capture(kind.ToString(),index++,Time.realtimeSinceStartup-start,frames);
            Require(actor.AbilityController.TryStartAbility(material.ability,target.transform,context),"Prepared throw rejected.");
            bool released=false;Vector3 launch=default;float launchScale=0,maxGripError=0;
            while(actor.AbilityController.IsExecuting&&Time.realtimeSinceStartup<deadline)
            {
                yield return new WaitForEndOfFrame();
                if(!released&&executor.ActiveFlightCount>0)
                {
                    foreach(var f in (IEnumerable)typeof(EnemyBossCompositePatternExecutor).GetField("flights",Private).GetValue(executor)){launch=(Vector3)f.GetType().GetField("start").GetValue(f);var v=f.GetType().GetField("visual").GetValue(f);launchScale=((GameObject)v.GetType().GetField("root").GetValue(v)).transform.localScale.x;break;}released=true;
                }
                if(kind==EnemyBossThrowPayload.Elite&&executor.IsEliteHeld){var row=Measure(actor,executor);maxGripError=Mathf.Max(maxGripError,(float)row["leftError"],(float)row["rightError"]);row["throwNormalized"]=executor.NormalizedTime;samples.Add(row);}
                if(Time.realtimeSinceStartup>=next){Capture(kind.ToString(),index++,Time.realtimeSinceStartup-start,frames);next=Time.realtimeSinceStartup+.08f;}
            }
            Require(!actor.AbilityController.IsExecuting&&released&&executor.LastFailure==null,"Throw failed or timed out: "+executor.LastFailure);
            Require(damage.Count==1&&(bool)damage[0]["knockdown"]==(kind==EnemyBossThrowPayload.Elite),"Existing landing damage/knockdown changed.");
            Require(executor.SummonedCount==(kind==EnemyBossThrowPayload.Elite?1:0)&&!(Grip(executor)?.IsApplied??false),"Release retained grip or summon did not land.");
            Require(Mathf.Abs(launchScale-(kind==EnemyBossThrowPayload.Elite?executor.Patterns.elite.visualScale:1.75f))<.001f,"Launch scale differs from held size.");
            if(kind==EnemyBossThrowPayload.Elite)Require(maxGripError<.18f,"Throw grip exceeded reachable body contact.");
            Require(basic.SupportPresentationOwner==null,"Completed throw retained presentation ownership.");
            Record(new JObject{["case"]=kind+"-prepared-throw",["launchHeight"]=launch.y-Origin.y,["launchScale"]=launchScale,["maximumGripError"]=maxGripError,["damage"]=damage,["summoned"]=executor.SummonedCount});
            File.WriteAllText(Path.Combine(plan.output,kind+"-frames.json"),frames.ToString());File.WriteAllText(Path.Combine(plan.output,kind+"-samples.json"),samples.ToString());
        }
        finally{health.OnDamaged-=observer;if(actor.IsLeased)service.Release(actor);}
    }
    static IEnumerator CancelCase(EnemyBossMaterialCollection collection,CombatTarget target,string scenario)
    {
        Position(target.transform,Origin+Vector3.forward*15f);
        Require(service.TrySpawn(new EnemySpawnRequest(collection.actorDefinition,Origin,Quaternion.identity,target.transform,context:EncounterContext.Test),out var actor),"Reused actor spawn failed.");
        leases.Add(actor);actor.AI.enabled=false;
        var executor=actor.GetComponent<EnemyBossCompositePatternExecutor>();
        try
        {
            Require(executor.TryBeginPreparation(EnemyBossThrowPayload.Elite,9300,0,target.transform),"Reused preparation rejected.");
            float deadline=Time.realtimeSinceStartup+12;
            while((scenario=="cancel-dig"?!executor.IsEliteHeld:!executor.TryGetPreparedStartContext(out _))&&Time.realtimeSinceStartup<deadline)yield return null;
            Require(Time.realtimeSinceStartup<deadline,"Cancel checkpoint missing.");yield return new WaitForEndOfFrame();
            if(scenario=="target-lost")target.gameObject.SetActive(false);else if(scenario=="disable-held")executor.enabled=false;else executor.Cancel();
            yield return null;yield return new WaitForEndOfFrame();
            Require(actor.GetComponent<EnemyBossMaterialExecutor>().SupportPresentationOwner==null,"Cancel retained presentation ownership.");
            Require(!executor.IsEliteHeld&&!executor.HasPreparation&&executor.ActiveVisualCount==0&&!(Grip(executor)?.IsApplied??false),"Cancel/disable retained held body or bone corrections.");
            Record(new JObject{["case"]=scenario,["lease"]=actor.LeaseVersion,["gripRestored"]=true});
        }
        finally{target.gameObject.SetActive(true);executor.enabled=true;if(actor.IsLeased)service.Release(actor);}
    }
    static void Finish(string error)
    {
        if (plan == null) return;
        foreach (var actor in leases) if (actor != null && actor.IsLeased) service?.Release(actor); leases.Clear();
        EnemyStrongAttackWarning.PlayerTarget = previousPlayerTarget; Write(error == null ? "PASS" : "FAIL", error);
        if (reviewSurface != null) { reviewSurface.Release(); Object.DestroyImmediate(reviewSurface); reviewSurface = null; }
        if (reviewPixels != null) { Object.DestroyImmediate(reviewPixels); reviewPixels = null; }
        plan.phase = "returning"; Persist(); if (OwnPlay) EditorApplication.isPlaying = false;
    }
    static void Return()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)) return;
        var prepared = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""); var env = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable);
        if (!string.IsNullOrEmpty(prepared) && !string.Equals(prepared, Account, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(env) && !string.Equals(env, Account, StringComparison.OrdinalIgnoreCase)) return;
        if (host != null && routine != null) host.StopCoroutine(routine);
        for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]); owned.Clear(); host = null; routine = null;
        Time.captureDeltaTime = plan.capture; Time.timeScale = plan.timeScale; Application.runInBackground = plan.background;
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(plan.previousStart) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(plan.previousStart); IsolatedSavePlayGuard.UseRealAccount();
        File.WriteAllText(Path.Combine(plan.output, "return.json"), new JObject { ["status"] = JToken.DeepEquals(plan.scenes, Scenes()) && !IsolatedSavePlayGuard.RequiresAccountChoice ? "PASS" : "FAIL", ["scenesBefore"] = plan.scenes, ["scenesAfter"] = Scenes(), ["guardChoice"] = IsolatedSavePlayGuard.RequiresAccountChoice,
            ["active"] = IsolatedSavePlayGuard.ActiveDirectory, ["prepared"] = SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""), ["expires"] = SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", ""), ["environment"] = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) }.ToString());
        SessionState.EraseString(Key); plan = null;
    }
}
