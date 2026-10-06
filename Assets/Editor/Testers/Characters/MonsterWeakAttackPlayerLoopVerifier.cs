using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Runs real coroutines, Animator and physics in an owned empty Play scene.
// Saved mode reads persisted actors/profiles; fixture mode keeps overrides in memory. SFX assets are never saved.
[InitializeOnLoad]
public static partial class MonsterWeakAttackPlayerLoopVerifier
{
    const string Key = "Overburst.WeakAttackPlayerLoop.";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    sealed class Plan
    {
        public string directory, fixture, previousStart, phase, token, testDefinition, attackBatch, themeActivationPath;
        public int expectedWeakCases;
        public int[] validationFrameRates;
        public string[] leaseDefinitionPaths;
        public string[] presentationCaseKeys;
        public bool background, fixedAnimator, stress, savedProfiles, leaseVerification, realPlayerParry, presentationCalibration, undeadCrowdVerification;
        public float captureDelta, fixedDelta, attackSpeed, timeScale;
        public double deadline;
        public JArray scenes;
    }
    static Plan plan;
    static readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
    static readonly JArray cases = new JArray();
    static EnemyMotor host;
    static Coroutine routine;
    static string failure;
    static MonsterWeakAttackPlayerLoopVerifier()
    {
        string saved = SessionState.GetString(Key + "plan", "");
        if (!string.IsNullOrEmpty(saved)) plan = JsonConvert.DeserializeObject<Plan>(saved);
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += Changed;
        AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
    }
    static string Workspace => Directory.GetParent(Application.dataPath).Parent.FullName;
    static string Account => Path.Combine(plan.directory, "Account");
    static bool Same(string a, string b) => !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b)
        && string.Equals(Path.GetFullPath(a).TrimEnd('/', '\\'), Path.GetFullPath(b).TrimEnd('/', '\\'), StringComparison.OrdinalIgnoreCase);
    static void Save() => SessionState.SetString(Key + "plan", JsonConvert.SerializeObject(plan));
    static JArray SceneEvidence()
    {
        var list = new JArray();
        for (int i=0;i<SceneManager.sceneCount;i++)
        { var s=SceneManager.GetSceneAt(i); list.Add(new JObject { ["path"]=s.path,["dirty"]=s.isDirty,["roots"]=s.rootCount }); }
        return list;
    }
    public static string Start(string outputDirectory, bool fixedAnimator = false, float attackSpeed = 1f, bool stress = false, bool savedProfiles = false)
        => StartInternal(outputDirectory,fixedAnimator,attackSpeed,stress,savedProfiles,false);
    public static string StartActorLease(string outputDirectory)
        => StartInternal(outputDirectory,false,1f,false,true,true);
    public static string StartNewActorLease(string outputDirectory,string definitionPath)
    {
        if(!definitionPath.StartsWith("Assets/ProjectOverburst/Resources/Enemies/Themes/Definitions/",StringComparison.Ordinal)
            ||AssetDatabase.LoadAssetAtPath<EnemyDefinition>(definitionPath)?.IsValid!=true)
            throw new ArgumentException("Valid saved project-owned review definition required.");
        return StartInternal(outputDirectory,false,1f,false,true,true,definitionPath);
    }
    public static string StartNewActorLeaseBatch(string outputDirectory,string[] definitionPaths)
    {
        if(definitionPaths==null||definitionPaths.Length==0||definitionPaths.Distinct().Count()!=definitionPaths.Length
            ||definitionPaths.Any(p=>!p.StartsWith("Assets/ProjectOverburst/Resources/Enemies/Themes/Definitions/",StringComparison.Ordinal)
                ||AssetDatabase.LoadAssetAtPath<EnemyDefinition>(p)?.IsValid!=true))
            throw new ArgumentException("Distinct saved project-owned definitions required.");
        return StartInternal(outputDirectory,false,1,false,true,true,null,false,null,definitionPaths);
    }
    public static string StartSavedAttackBatch(string outputDirectory,string authoringPath)
    {
        authoringPath=Path.GetFullPath(authoringPath);
        string allowed=Path.GetFullPath(Path.Combine(Workspace,"개인파일/코덱스산출"))+Path.DirectorySeparatorChar;
        if(!authoringPath.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||!File.Exists(authoringPath))
            throw new ArgumentException("Private native authoring batch required.");
        return StartInternal(outputDirectory,false,1,false,true,false,null,false,authoringPath);
    }
    public static string StartSavedAttackAndLeaseBatch(string outputDirectory,string authoringPath,string[] definitionPaths)
    {
        authoringPath=Path.GetFullPath(authoringPath);
        string allowed=Path.GetFullPath(Path.Combine(Workspace,"개인파일/코덱스산출"))+Path.DirectorySeparatorChar;
        if(!authoringPath.StartsWith(allowed,StringComparison.OrdinalIgnoreCase)||!File.Exists(authoringPath)
            ||definitionPaths==null||definitionPaths.Length==0||definitionPaths.Distinct().Count()!=definitionPaths.Length
            ||definitionPaths.Any(p=>!p.StartsWith("Assets/ProjectOverburst/Resources/Enemies/Themes/Definitions/",StringComparison.Ordinal)
                ||AssetDatabase.LoadAssetAtPath<EnemyDefinition>(p)?.IsValid!=true))
            throw new ArgumentException("Private attack batch and distinct saved definitions required.");
        return StartInternal(outputDirectory,false,1,false,true,false,null,false,authoringPath,definitionPaths);
    }
    static string StartInternal(string outputDirectory,bool fixedAnimator,float attackSpeed,bool stress,bool savedProfiles,bool leaseVerification,string testDefinition=null,bool realPlayerParry=false,string attackBatch=null,string[] leaseDefinitionPaths=null,string themeActivationPath=null)
    {
        if(float.IsNaN(attackSpeed) || float.IsInfinity(attackSpeed) || attackSpeed<=0f)throw new ArgumentException("Invalid attack speed.");
        if(plan!=null || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating
            || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "")))
            throw new InvalidOperationException("Idle Editor and unoccupied account required.");
        outputDirectory=Path.GetFullPath(outputDirectory);
        string allowed=Path.GetFullPath(Path.Combine(Workspace,"개인파일/코덱스산출"))+Path.DirectorySeparatorChar;
        if(!outputDirectory.StartsWith(allowed,StringComparison.OrdinalIgnoreCase) || File.Exists(Path.Combine(outputDirectory,"plan.json")))
            throw new ArgumentException("Use a fresh output directory.");
        var selectedBatch = string.IsNullOrEmpty(attackBatch) ? null : JObject.Parse(File.ReadAllText(attackBatch));
        var presentationCaseKeys = (bool?)selectedBatch?["verifyPresentationCalibration"] == true
            ? BuildPresentationCaseKeys(selectedBatch) : null;
        var frameRates = selectedBatch?["validationFrameRates"] is JArray configuredRates
            ? configuredRates.Values<int>().ToArray() : new[]{15,30,60};
        if(frameRates.Length==0 || frameRates.Distinct().Count()!=frameRates.Length
            || frameRates.Any(rate=>rate!=15 && rate!=30 && rate!=60))
            throw new ArgumentException("Batch validation frame rates must be distinct 15, 30 or 60.");
        int presentationCases=((bool?)selectedBatch?["verifyUndeadCrowd"]==true?(((bool?)selectedBatch?["verifyCrowd"]??true)?4:0)+(selectedBatch?["deathDefinitions"] is JArray selectedDeaths?selectedDeaths.Count:4):0)+(presentationCaseKeys?.Length??0);
        int weakCases=selectedBatch==null?0:selectedBatch["entries"].Where(r=>(string)r["role"]=="weak"
            &&((bool?)r["nativeContactGeometryAuthored"]==true||((string)r["visualType"]=="ranged"||(string)r["visualType"]=="channel")&&(bool?)r["nativeAuthoringComplete"]==true))
            .Sum(r=>(string)r["visualType"]=="ranged"&&(bool?)r["verifyCancelBeforeHit"]==true?2:1)*frameRates.Length+((bool?)selectedBatch?["verifyRakeLocomotion"]==true?3:0);
        Directory.CreateDirectory(outputDirectory);
        plan=new Plan { directory=outputDirectory,token=Guid.NewGuid().ToString("N"),phase="booting",
            previousStart=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),background=Application.runInBackground,
            captureDelta=Time.captureDeltaTime,fixedDelta=Time.fixedDeltaTime,deadline=EditorApplication.timeSinceStartup+600,
            fixedAnimator=fixedAnimator,attackSpeed=attackSpeed,timeScale=Time.timeScale,stress=stress,savedProfiles=savedProfiles,leaseVerification=leaseVerification,leaseDefinitionPaths=leaseDefinitionPaths?.ToArray(),testDefinition=testDefinition,realPlayerParry=realPlayerParry,themeActivationPath=themeActivationPath,attackBatch=attackBatch,presentationCalibration=(bool?)selectedBatch?["verifyPresentationCalibration"]==true,presentationCaseKeys=presentationCaseKeys,undeadCrowdVerification=(bool?)selectedBatch?["verifyUndeadCrowd"]==true,expectedWeakCases=presentationCases+weakCases+(!leaseVerification&&selectedBatch!=null?(leaseDefinitionPaths?.Length??0)*2:0),validationFrameRates=frameRates,scenes=SceneEvidence() };
        plan.fixture=realPlayerParry?"Assets/ProjectOverburst/00_Scenes/PersistentScene.unity":"Assets/Editor/Testers/Characters/WeakPlayerLoop_"+plan.token+".unity";
        cases.Clear(); failure=null; Save();
        File.WriteAllText(Path.Combine(outputDirectory,"plan.json"),JsonConvert.SerializeObject(plan,Formatting.Indented));
        Scene active=SceneManager.GetActiveScene(); Scene fixture=default;
        try
        {
            if(!realPlayerParry)
            {
                fixture=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
                if(!EditorSceneManager.SaveScene(fixture,plan.fixture))throw new InvalidOperationException("Fixture save failed.");
                EditorSceneManager.CloseScene(fixture,true);fixture=default;
            }
            SceneManager.SetActiveScene(active);
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(plan.fixture);
            Application.runInBackground=true;
            IsolatedSavePlayGuard.EnterIsolatedPlay(Account);
            return Path.Combine(outputDirectory,"plan.json");
        }
        catch
        {
            if(fixture.IsValid())EditorSceneManager.CloseScene(fixture,true);
            if(active.IsValid())SceneManager.SetActiveScene(active);
            plan.phase="returning"; Save(); throw;
        }
    }
    static bool OwnPlay => plan!=null && EditorApplication.isPlaying && Same(IsolatedSavePlayGuard.ActiveDirectory,Account);
    static void Tick()
    {
        if(plan==null)return;
        try
        {
            if(plan.phase=="booting" && EditorApplication.timeSinceStartup>plan.deadline)
            { Return("Boot timeout"); return; }
            if(plan.phase=="booting" && EditorApplication.isPlaying)
            {
                if(!OwnPlay)throw new InvalidOperationException("Isolated account mismatch.");
                var go=new GameObject("WeakAttackPlayerLoopHost"); owned.Add(go);
                host=go.AddComponent<EnemyMotor>(); go.GetComponent<Rigidbody>().isKinematic=true;
                plan.phase="running";
                // Import and domain-reload time belong to boot, not to the attack budget.
                plan.deadline=EditorApplication.timeSinceStartup+Math.Max(180,Math.Max(
                    (plan.leaseDefinitionPaths?.Length??1)*(plan.realPlayerParry?90:plan.leaseVerification||!string.IsNullOrEmpty(plan.attackBatch)?70:25)+90,
                    plan.expectedWeakCases*12+90));
                if(!string.IsNullOrEmpty(plan.themeActivationPath))plan.deadline=EditorApplication.timeSinceStartup+1200;
                Save();
                routine=host.StartCoroutine(Drive(RunCases()));
            }
            if(plan.phase=="running")
            {
                if(!OwnPlay){ Return("Play changed or interrupted"); return; }
                if(EditorApplication.timeSinceStartup>plan.deadline){ Return("PlayerLoop timeout"); return; }
                EditorApplication.QueuePlayerLoopUpdate();
            }
            if(plan.phase=="returning")FinishReturn();
        }
        catch(Exception error){ Return(error.ToString()); }
    }
    // Drive the nested enumerator only to catch exceptions; every yielded object
    // is scheduled by Unity on the runtime host, never by Editor.update.
    static IEnumerator Drive(IEnumerator body)
    {
        // Nested routines are driven on the same runtime Coroutine host. Unity still
        // schedules every non-enumerator yield, including physics and frame waits.
        var stack=new Stack<IEnumerator>();stack.Push(body);
        while(stack.Count>0)
        {
            var currentRoutine=stack.Peek();bool more;object current;
            try{more=currentRoutine.MoveNext();current=more?currentRoutine.Current:null;}
            catch(Exception error)
            {
                string cleanup="";
                while(stack.Count>0)try{(stack.Pop() as IDisposable)?.Dispose();}catch(Exception e){cleanup+="\nCleanup: "+e.Message;}
                Return(error+cleanup);yield break;
            }
            if(!more)
            {
                stack.Pop();
                try{(currentRoutine as IDisposable)?.Dispose();}catch(Exception error){Return(error.ToString());yield break;}
                continue;
            }
            if(current is IEnumerator nested){stack.Push(nested);continue;}
            yield return current;
        }
        Return(null);
    }
    static AnimationClip Source(JObject row) => AssetDatabase.LoadAllAssetsAtPath((string)row["sourcePath"]).OfType<AnimationClip>()
        .Single(c=>AssetDatabase.TryGetGUIDAndLocalFileIdentifier(c,out string guid,out long id)
            && guid==(string)row["sourceGuid"] && id==(long)row["sourceLocalId"]);
    static Vector3 Vector(JToken value)=>new Vector3((float)value[0],(float)value[1],(float)value[2]);
    static IEnumerator RunCases()
    {
        if(!string.IsNullOrEmpty(plan.themeActivationPath)){yield return RunActualThemeCases();yield break;}
        if(plan.realPlayerParry){yield return RunRealPlayerParryCases();yield break;}
        if(plan.leaseVerification){yield return RunLeaseCases();yield break;}
        var author=JObject.Parse(File.ReadAllText(string.IsNullOrEmpty(plan.attackBatch)?Path.Combine(Workspace,"개인파일/코덱스산출/Monsters/MonsterOverhaulV3/GOAL_A/20261004/attack-authoring.json"):plan.attackBatch));
        if((bool?)author["verifyUndeadCrowd"]==true){yield return RunUndeadCrowdCases(author);if((bool?)author["verifyPresentationCalibration"]==true)yield return RunPresentationCalibrationCases(author);yield break;}
        if((bool?)author["verifyPresentationCalibration"]==true){yield return RunPresentationCalibrationCases(author);yield break;}
        if((bool?)author["verifyRakeLocomotion"]==true){yield return RunRakeLocomotionCases();if(plan.leaseDefinitionPaths?.Length>0)yield return RunLeaseCases();yield break;}
        var originalIds=new[]{"runtime:CavernMutants_Cephalonops","runtime:CavernMutants_Ceratoferox","runtime:CavernMutants_Gasterobrach","runtime:CavernMutants_Gorhorrid"};
        var rows=author["entries"].OfType<JObject>().Where(r=>((bool?)r["nativeContactGeometryAuthored"]==true
            ||((string)r["visualType"]=="ranged"||(string)r["visualType"]=="channel")&&(bool?)r["nativeAuthoringComplete"]==true)
            &&(!string.IsNullOrEmpty(plan.attackBatch)||originalIds.Contains((string)r["cardKey"]))).ToArray();
        if(rows.Length==0||string.IsNullOrEmpty(plan.attackBatch)&&rows.Length!=8)throw new InvalidOperationException("Native authored attack batch is empty or unexpected.");
        var runs=from fps in plan.validationFrameRates??new[]{15,30,60}
            from row in rows where !plan.stress || (string)row["selectionKey"]=="06b6021ea737706a"
            from scenario in plan.stress?new[]{"cancel","freeze","hitstop","first-miss","cancel-on-hit"}:new[]{"normal"}
            select new {fps,row,scenario};
        foreach(var run in runs)
        {
            int fps=run.fps;JObject row=run.row;string scenario=run.scenario;
            Time.captureDeltaTime=1f/fps;
            if((string)row["visualType"]=="channel"){yield return RunPresentationChannelCase(row,fps);continue;}
            if((string)row["visualType"]=="ranged")
            {
                yield return RunProjectileCase(row,fps);
                if((bool?)row["verifyCancelBeforeHit"]==true)yield return RunProjectileCase(row,fps,true);
                continue;
            }
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>((string)row["actorGeometryPlacement"]["actorPrefabPath"]);
            var go=UnityEngine.Object.Instantiate(prefab,new Vector3(19000,0,19000),Quaternion.identity); owned.Add(go);
            foreach(var component in go.GetComponentsInChildren<MonoBehaviour>(true))
                if(component!=null && new[]{"EnemyAIController","EnemyAbilityController","EnemyCrowdAgent","EnemySensor","RunFallGuard"}.Contains(component.GetType().Name))component.enabled=false;
            var melee=go.GetComponent<EnemyMeleeAttackController>();var movement=go.GetComponent<EnemyMovement>();
            var driver=go.GetComponent<EnemyWeakAttackMotionDriver>()??go.AddComponent<EnemyWeakAttackMotionDriver>();driver.Configure(melee,movement);
            typeof(EnemyMeleeAttackController).GetMethod("ResolveReferences",Private).Invoke(melee,null);
            if(!plan.savedProfiles)typeof(EnemyMeleeAttackController).GetField("targetLayer",Private).SetValue(melee,(LayerMask)(~0));
            var actorBody=go.GetComponent<Rigidbody>();actorBody.useGravity=false;
            var animator=go.GetComponentsInChildren<Animator>(true).Single();animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
            if(plan.fixedAnimator)animator.updateMode=AnimatorUpdateMode.Fixed;
            var source=Source(row);
            var clip=(string)row["runtimeClipPath"]==(string)row["sourcePath"]?source:AssetDatabase.LoadAssetAtPath<AnimationClip>((string)row["runtimeClipPath"]);
            var controller=animator.runtimeAnimatorController as AnimatorController;
            if(controller==null)throw new InvalidOperationException("Native controller expected.");
            if(!plan.savedProfiles)
            {
                var slot=controller.layers[0].stateMachine.states.Single(s=>s.state.name=="Attack_1").state.motion as AnimationClip;
                var over=new AnimatorOverrideController(controller);owned.Add(over);over[slot]=clip;animator.runtimeAnimatorController=over;
            }
            var windows=row["contactWindowsNormalized"].Select(w=>new Vector2((float)w[0],(float)w[1])).ToArray();
            var frames=row["contactGeometry"]["phases"].Select(p=>p["frames"].Select(f=>new EnemyWeakAttackContactFrame((float)f["normalizedTime"],
                f["capsules"].Select(c=>new EnemyWeakAttackContactCapsule(Vector(c["a"]),Vector(c["b"]),(float)c["radius"])).ToArray())).ToArray()).ToArray();
            var policy=(EnemyWeakAttackMotionPolicy)Enum.Parse(typeof(EnemyWeakAttackMotionPolicy),(string)row["motionPolicy"]);
            float[] times=row["hitNormalizedTimes"].Select(v=>(float)v).ToArray();
            EnemyWeakAttackExecutionProfile profile;EnemyAbilityDefinition ability;
            if(plan.savedProfiles)
            {
                var actor=go.GetComponent<EnemyActor>();
                ability=Enumerable.Range(0,actor.Definition.AbilitySet.Count).Select(i=>actor.Definition.AbilitySet.GetAbility(i))
                    .Single(a=>a.WeakAttackExecution!=null && a.WeakAttackExecution.SelectionKey==(string)row["selectionKey"]);
                profile=ability.WeakAttackExecution;
                if(profile.RuntimeClip!=clip || !profile.ValidateAuthoring(out _))throw new InvalidOperationException("Saved profile does not match native row.");
            }
            else
            {
                profile=ScriptableObject.CreateInstance<EnemyWeakAttackExecutionProfile>();owned.Add(profile);
                profile.Configure((string)row["selectionKey"],source,clip,new Vector2(0,source.length),policy,10,
                    policy==EnemyWeakAttackMotionPolicy.ShortAdvance?.35f:0,new Vector2(.1f,.4f),AnimationCurve.Linear(0,0,1,1),"",windows,frames);
                ability=ScriptableObject.CreateInstance<EnemyAbilityDefinition>();owned.Add(ability);
                ability.Configure("PlayerLoop_"+row["selectionKey"],"Attack1",10,10,1,360,0,0,times[0],clip.length,1,false,
                    EnemyAbilityExecutionMode.MeleeArc,10,false,clip.length);
                ability.ConfigureAdditionalHits(times.Skip(1).ToArray());ability.ConfigureWeakAttackExecution(profile);
            }
            var victim=new GameObject("PlayerLoopControlledTarget");owned.Add(victim);
            var playerPrefab=plan.savedProfiles?AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab"):null;
            var playerVolume=playerPrefab!=null?playerPrefab.GetComponent<CombatTarget>().CurrentVolume:new CombatTargetVolume(Vector3.up,5,5);
            if(playerPrefab!=null)victim.layer=playerPrefab.layer;
            float startingDistance=plan.savedProfiles?profile.ApproachStartRange-.10f:1f;
            GameObject contactFloor=null;
            if(plan.savedProfiles){
                contactFloor=GameObject.CreatePrimitive(PrimitiveType.Cube);owned.Add(contactFloor);contactFloor.name="Owned saved attack ground";
                contactFloor.transform.position=go.transform.position-Vector3.up*.5f;contactFloor.transform.localScale=new Vector3(80,1,80);
            }
            Vector3 initialActorPosition=go.transform.position;

            // The saved player pivot is at the capsule center. Ground its lowest point
            // at the actor's floor height, as in the production Actor lease test.
            var playerBody=playerPrefab!=null?playerPrefab.GetComponentsInChildren<CapsuleCollider>(true).Single(c=>c.enabled&&!c.isTrigger):null;
            Vector3 bodyCenter=playerBody!=null?playerPrefab.transform.InverseTransformPoint(playerBody.transform.TransformPoint(playerBody.center)):playerVolume.Center;
            Vector3 bodyScale=playerBody!=null?playerBody.transform.lossyScale:Vector3.one;
            float bodyRadius=playerBody!=null?playerBody.radius*Mathf.Max(Mathf.Abs(bodyScale.x),Mathf.Abs(bodyScale.z)):playerVolume.Radius;
            float bodyHeight=playerBody!=null?playerBody.height*Mathf.Abs(bodyScale.y):playerVolume.HalfHeight*2;
            if(playerBody!=null&&playerBody.direction!=1)throw new InvalidOperationException("Saved player capsule axis changed.");
            float groundOffset=plan.savedProfiles?bodyHeight*.5f-bodyCenter.y:0f;
            victim.transform.position=go.transform.position+Vector3.forward*startingDistance+Vector3.up*groundOffset;
            var collider=victim.AddComponent<CapsuleCollider>();collider.center=bodyCenter;collider.radius=bodyRadius;collider.height=bodyHeight;
            foreach(var shape in go.GetComponentsInChildren<Collider>(true))Physics.IgnoreCollision(shape,collider);
            var target=victim.AddComponent<CombatTarget>();target.Configure(plan.savedProfiles?CombatTeam.PlayerParty:CombatTeam.Neutral,false);
            target.ConfigureVolume(playerVolume.Center,playerVolume.Radius,playerVolume.HalfHeight*2);
            var health=victim.GetComponent<CombatHealth>();
            var settings=new SerializedObject(health);settings.FindProperty("showDamageNumbers").boolValue=false;settings.ApplyModifiedPropertiesWithoutUndo();
            var hits=new JArray();var samples=new JArray();
            var bridge=go.GetComponent<EnemyAnimationBridge>();
            AnimatorUpdateMode beforeMode=animator.updateMode;
            health.OnDamaged+=(_,info)=>{bridge.TryGetAttackMotionTime(ability.AnimatorTrigger,clip,out float actual);
                hits.Add(new JObject{["frame"]=Time.frameCount,["fixedTime"]=Time.fixedTime,["phase"]=info.sourceAttackPhaseIndex,
                    ["damage"]=info.damage,["sequence"]=info.sourceAttackSequenceId,["clock"]=melee.WeakAttackNormalizedTime,["animator"]=actual,
                    ["animatorMode"]=animator.updateMode.ToString(),["repeatReactionSuppressed"]=info.suppressRepeatedAttackReaction});
                if(scenario=="cancel-on-hit")melee.CancelAttack();};
            yield return null;yield return null;
            melee.SetRuntimeAttackSpeedMultiplier(plan.attackSpeed);
            Physics.SyncTransforms();
            bool started=melee.TryStartAbility(victim.transform,ability,0);
            int startFrame=Time.frameCount;float deadline=Time.time+clip.length*4+2;
            bool acted=false,returnedTarget=false,pauseVerified=true;int targetPathIndex=0;
            while(started && melee.IsAttacking && Time.time<deadline)
            {
                bridge.TryGetAttackMotionTime(ability.AnimatorTrigger,clip,out float actual);
                samples.Add(new JObject{["frame"]=Time.frameCount,["time"]=Time.time,["delta"]=Time.deltaTime,["fixedTime"]=Time.fixedTime,
                    ["clock"]=melee.WeakAttackNormalizedTime,["animator"]=actual,["entered"]=melee.HasEnteredWeakAttack,
                    ["actorDistance"]=Vector3.Distance(go.transform.position,initialActorPosition),["targetDistance"]=Vector3.Distance(go.transform.position,victim.transform.position),["advanceConsumed"]=driver.ConsumedDistance});
                if(row["targetPath"] is JArray targetPath){while(targetPathIndex<targetPath.Count&&melee.WeakAttackNormalizedTime>=(float)targetPath[targetPathIndex]["normalizedTime"]){var destination=targetPath[targetPathIndex]["actorLocalPosition"];victim.transform.position=go.transform.TransformPoint(new Vector3((float)destination[0],groundOffset,(float)destination[2]));Physics.SyncTransforms();targetPathIndex++;}}
                if(!acted && melee.WeakAttackNormalizedTime>=.16f && scenario!="normal" && scenario!="cancel-on-hit")
                {
                    acted=true;
                    if(scenario=="cancel")melee.CancelAttack();
                    if(scenario=="freeze"){melee.SetStatusActionSpeedMultiplier(0);bridge.SetFrozen(true);}
                    if(scenario=="first-miss"){victim.transform.position+=Vector3.right*200;Physics.SyncTransforms();}
                    if(scenario=="hitstop")
                    {
                        float frozenClock=melee.WeakAttackNormalizedTime;int frozenHits=hits.Count;
                        Time.timeScale=0;
                        for(int pauseFrame=0;pauseFrame<4;pauseFrame++)yield return null;
                        pauseVerified=melee.WeakAttackNormalizedTime==frozenClock && hits.Count==frozenHits;
                        Time.timeScale=plan.timeScale;
                    }
                }
                if(scenario=="first-miss" && acted && !returnedTarget && melee.WeakAttackNormalizedTime>=.32f)
                { returnedTarget=true;victim.transform.position-=Vector3.right*200;Physics.SyncTransforms(); }
                yield return null;
            }
            if(scenario=="cancel" || scenario=="freeze" || scenario=="cancel-on-hit")
                for(int observe=0;observe<6;observe++)yield return null;
            int[] expected=scenario=="cancel" || scenario=="freeze"?Array.Empty<int>():scenario=="first-miss"?new[]{1}:
                scenario=="cancel-on-hit"?new[]{0}:Enumerable.Range(0,times.Length).ToArray();
            if(scenario=="normal"&&row["expectedContactPhases"] is JArray expectedContact){expected=expectedContact.Values<int>().ToArray();if(expected.Any(p=>p<0||p>=times.Length)||expected.Distinct().Count()!=expected.Length)throw new Exception("Invalid explicit contact fixture phases");}
            float damageMultiplier=(float)typeof(EnemyMeleeAttackController).GetField("definitionDamageMultiplier",Private).GetValue(melee);
            if(!ability.TryResolveWeakDamageBudget(go.GetComponent<EnemyRank>()?.Level??1,damageMultiplier,out var budget))throw new InvalidOperationException("Saved damage budget invalid.");
            float expectedDamage=expected.Sum(phase=>budget.ForPhase(phase));
            bool pass=started && hits.Count==expected.Length && hits.Select(h=>(int)h["phase"]).SequenceEqual(expected)
                && hits.All(h=>(float)h["clock"]>=windows[(int)h["phase"]].x && (float)h["clock"]<=windows[(int)h["phase"]].y+.0001f)
                && Mathf.Abs(hits.Sum(h=>(float)h["damage"])-expectedDamage)<.001f && animator.updateMode==beforeMode && pauseVerified
                && !melee.IsAttacking && melee.ActiveWeakExecution==null
                && hits.Select((h,index)=>(bool)h["repeatReactionSuppressed"]==(index>0)).All(value=>value);
            cases.Add(new JObject{["selectionKey"]=row["selectionKey"].DeepClone(),["clip"]=clip.name,["fps"]=fps,
                ["animatorMode"]=animator.updateMode.ToString(),["initialAnimatorMode"]=beforeMode.ToString(),["attackSpeed"]=melee.AbilityAnimationSpeed,
                ["fixedDelta"]=Time.fixedDeltaTime,["started"]=started,["startFrame"]=startFrame,
                ["scenario"]=scenario,["pauseVerified"]=pauseVerified,["expectedHits"]=expected.Length,["pass"]=pass,["hits"]=hits,["samples"]=samples});
            var last=(JObject)cases[cases.Count-1];last["savedProfile"]=plan.savedProfiles;last["startingDistance"]=startingDistance;
            last["targetRadius"]=collider.radius;last["targetHeight"]=collider.height;last["targetCenterY"]=collider.center.y;last["targetGroundOffsetY"]=groundOffset;last["expectedDamage"]=expectedDamage;last["expectedContactPhases"]=new JArray(expected);last["controlledTargetPath"]=row["targetPath"]?.DeepClone();
            WriteResult("RUNNING");
            melee.CancelAttack();UnityEngine.Object.Destroy(go);UnityEngine.Object.Destroy(victim);if(contactFloor!=null)UnityEngine.Object.Destroy(contactFloor);
            yield return null;
        }
        if(!string.IsNullOrEmpty(plan.attackBatch)&&plan.leaseDefinitionPaths?.Length>0)
            yield return RunLeaseCases();
    }

    // Production spawn/AI/ability/pool path. The target uses the saved player's body volume;
    // no attack timings, ranges, damage, controllers or profiles are overridden.
    static IEnumerator RunLeaseCases()
    {
        Time.captureDeltaTime=1f/60;
        var serviceRoot=new GameObject("V3 Actor lease services");owned.Add(serviceRoot);
        var poolRoot=new GameObject("V3 Inactive pool");poolRoot.transform.SetParent(serviceRoot.transform,false);poolRoot.SetActive(false);
        var pool=serviceRoot.AddComponent<EnemyPoolService>();pool.Configure(poolRoot.transform,0);
        var service=serviceRoot.AddComponent<EnemySpawnService>();
        var catalog=AssetDatabase.LoadAssetAtPath<EnemyCatalog>("Assets/ProjectOverburst/Resources/Enemies/Themes/Catalog.asset");
        if(plan.leaseDefinitionPaths?.Length>0||!string.IsNullOrEmpty(plan.testDefinition))
        {
            catalog=ScriptableObject.CreateInstance<EnemyCatalog>();owned.Add(catalog);
            catalog.Configure((plan.leaseDefinitionPaths??new[]{plan.testDefinition}).Select(AssetDatabase.LoadAssetAtPath<EnemyDefinition>).ToArray());
        }
        service.Configure(catalog,pool);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);owned.Add(floor);floor.name="V3 lease fixture ground";
        floor.transform.position=new Vector3(0,-.5f,0);floor.transform.localScale=new Vector3(100,1,100);
        var playerPrefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ProjectOverburst/03_Features/Player/Prefabs/PF_PlayerActor.prefab");
        var volume=playerPrefab.GetComponent<CombatTarget>().ResolveVolumeAtRootPosition(Vector3.zero);
        var playerBody=playerPrefab.GetComponentsInChildren<CapsuleCollider>(true).Single(c=>c.enabled&&!c.isTrigger);
        Vector3 bodyCenter=playerPrefab.transform.InverseTransformPoint(playerBody.transform.TransformPoint(playerBody.center));
        Vector3 bodyScale=playerBody.transform.lossyScale;
        if(playerBody.direction!=1||playerPrefab.transform.lossyScale!=Vector3.one)
            throw new InvalidOperationException("The saved player capsule basis changed; preserve and inspect it.");
        var targetRoot=new GameObject("Saved player collider and combat-volume AI target");owned.Add(targetRoot);targetRoot.layer=playerPrefab.layer;
        targetRoot.transform.position=new Vector3(0,0,4.5f);
        var capsule=targetRoot.AddComponent<CapsuleCollider>();capsule.center=bodyCenter;
        capsule.radius=playerBody.radius*Mathf.Max(Mathf.Abs(bodyScale.x),Mathf.Abs(bodyScale.z));
        capsule.height=playerBody.height*Mathf.Abs(bodyScale.y);
        var target=targetRoot.AddComponent<CombatTarget>();target.Configure(CombatTeam.PlayerParty,false);
        target.ConfigureVolume(volume.Center,volume.Radius,volume.HalfHeight*2);var health=targetRoot.GetComponent<CombatHealth>();health.SetMaxHp(100000,true);
        var so=new SerializedObject(health);so.FindProperty("showDamageNumbers").boolValue=false;so.ApplyModifiedPropertiesWithoutUndo();
        var damageEvents=new JArray();
        health.OnDamaged+=(_,info)=>damageEvents.Add(new JObject{["frame"]=Time.frameCount,["damage"]=info.damage,
            ["phase"]=info.sourceAttackPhaseIndex,["sequence"]=info.sourceAttackSequenceId});
        yield return null;yield return new WaitForFixedUpdate();
        var definitions=plan.leaseDefinitionPaths?.Length>0?plan.leaseDefinitionPaths.Select(AssetDatabase.LoadAssetAtPath<EnemyDefinition>).ToArray():string.IsNullOrEmpty(plan.testDefinition)
            ?new[]{"Cephalonops","Ceratoferox","Gasterobrach","Gorhorrid"}.Select(name=>AssetDatabase.LoadAssetAtPath<EnemyDefinition>(
                "Assets/ProjectOverburst/Resources/Enemies/Themes/Definitions/CavernMutants_"+name+".asset")).ToArray()
            :new[]{AssetDatabase.LoadAssetAtPath<EnemyDefinition>(plan.testDefinition)};
        foreach(var definition in definitions)
        {
            string id=definition.EnemyId;
            EnemyActor previous=null;uint lastVersion=0;
            for(int lease=0;lease<2;lease++)
            {
                targetRoot.transform.position=new Vector3(0,0,4.5f);health.SetMaxHp(100000,true);damageEvents.Clear();
                var request=new EnemySpawnRequest(definition,Vector3.zero,Quaternion.identity,targetRoot.transform,null,targetRoot.transform,null,1,1,71+lease);
                if(!service.TrySpawn(request,out var actor))throw new Exception("Production spawn failed: "+id);
                actor.Animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                bool reused=lease==0||actor==previous;
                bool fresh=actor.IsLeased&&actor.LeaseVersion>lastVersion&&!actor.Melee.IsAttacking
                    &&actor.Melee.ActiveWeakExecution==null&&!actor.GetComponent<EnemyWeakAttackMotionDriver>().IsActive&&!actor.AbilityController.IsExecuting
                    &&!actor.AnimationBridge.IsBlockingActionActive&&actor.transform.localScale==Vector3.one;
                var expected=Enumerable.Range(0,definition.AbilitySet.Count).Select(definition.AbilitySet.GetAbility)
                    .Where(a=>a.WeakAttackExecution!=null).Select(a=>a.WeakAttackExecution.SelectionKey).ToHashSet();
                var actual=new HashSet<string>();var states=new HashSet<string>();float moved=0;Vector3 previousPosition=actor.transform.position;
                float initialAttackRange=actor.AbilityController.AttackRange;
                float initialDistance=Mathf.Max(4.5f,initialAttackRange+1.25f);
                targetRoot.transform.position=new Vector3(0,0,initialDistance);
                actor.AI.RequestAggro(targetRoot.transform);float begin=Time.time,limit=Time.time+10;bool driverObserved=false,projectileObserved=false,channelObserved=false;
                while(Time.time<limit)
                {
                    moved+=Vector3.Distance(previousPosition,actor.transform.position);previousPosition=actor.transform.position;
                    states.Add(actor.AI.CurrentStateName);
                    var ability=actor.AbilityController.LastCommittedAbility;
                    if(ability!=null&&ability.WeakAttackExecution!=null)actual.Add(ability.WeakAttackExecution.SelectionKey);
                    driverObserved|=actor.GetComponent<EnemyWeakAttackMotionDriver>().IsActive;
                    projectileObserved|=actor.GetComponent<EnemyThemeSpecialExecutor>()?.IsExecuting==true;
                    channelObserved|=actor.GetComponent<EnemyChannelAbilityExecutor>()?.IsExecuting==true;
                    if(Time.time-begin>4&&actual.Count>0&&damageEvents.Count>0&&!actor.Melee.IsAttacking)break;
                    yield return null;
                }
                bool aiAttack=actual.Count>0&&actual.All(expected.Contains)&&damageEvents.Count>0;
                bool approached=initialDistance<=initialAttackRange+.01f||moved>.1f;
                float finalDistance=Vector2.Distance(new Vector2(actor.transform.position.x,actor.transform.position.z),
                    new Vector2(targetRoot.transform.position.x,targetRoot.transform.position.z));
                uint version=actor.LeaseVersion;previous=actor;lastVersion=version;
                service.Release(actor);yield return null;yield return new WaitForFixedUpdate();
                bool reset=!actor.IsLeased&&!actor.gameObject.activeSelf&&!actor.Melee.IsAttacking
                    &&actor.Melee.ActiveWeakExecution==null&&!actor.GetComponent<EnemyWeakAttackMotionDriver>().IsActive&&!actor.AbilityController.IsExecuting
                    &&actor.Animator.updateMode==definition.ActorPrefab.Animator.updateMode&&pool.LeasedCount==0&&pool.PendingReturnCount==0;
                                var shot=actor.GetComponent<EnemyThemeSpecialExecutor>();var channel=actor.GetComponent<EnemyChannelAbilityExecutor>();
                reset&=(shot==null||!shot.HasProjectile&&shot.LaunchCount==0&&shot.ImpactCount==0)
                    &&(channel==null||!channel.IsEmitting&&channel.PulseCount==0&&channel.DamageCount==0);
                bool pass=fresh&&reused&&aiAttack&&approached&&(driverObserved||projectileObserved||channelObserved)&&reset;
                cases.Add(new JObject{["id"]=id,["lease"]=lease,["leaseVersion"]=version,["freshState"]=fresh,["samePooledActor"]=reused,
                    ["actualAiAttack"]=aiAttack,["approached"]=approached,["driverObserved"]=driverObserved,["projectileExecutorObserved"]=projectileObserved,["channelExecutorObserved"]=channelObserved,["reset"]=reset,["pass"]=pass,["movedMeters"]=moved,
                    ["initialDistance"]=initialDistance,["initialAttackRange"]=initialAttackRange,["finalDistance"]=finalDistance,
                    ["playerCapsuleCenter"]=new JArray(capsule.center.x,capsule.center.y,capsule.center.z),
                    ["playerCapsuleRadius"]=capsule.radius,["playerCapsuleHeight"]=capsule.height,
                    ["states"]=JArray.FromObject(states),["weakSelections"]=JArray.FromObject(actual),["damageEvents"]=damageEvents.DeepClone(),
                    ["available"]=pool.AvailableCount,["created"]=pool.CreatedCount});
                WriteResult("RUNNING");
            }
        }
        UnityEngine.Object.Destroy(serviceRoot);UnityEngine.Object.Destroy(targetRoot);UnityEngine.Object.Destroy(floor);
        yield return null;
    }

    internal static string[] BuildPresentationCaseKeys(JObject batch)
    {
        if(!(batch?["definitions"] is JArray paths) || paths.Count==0)
            throw new ArgumentException("Presentation definitions must be nonempty.");
        var ids=new HashSet<string>(StringComparer.Ordinal);
        var assets=new HashSet<string>(StringComparer.Ordinal);
        foreach(var token in paths)
        {
            string path=token.Type==JTokenType.String?(string)token:null;
            var definition=string.IsNullOrEmpty(path)?null:AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path);
            if(path==null || !path.StartsWith("Assets/ProjectOverburst/Resources/Enemies/Themes/Definitions/",StringComparison.Ordinal)
                || definition==null || !definition.IsValid || string.IsNullOrEmpty(definition.EnemyId)
                || !assets.Add(AssetDatabase.AssetPathToGUID(path)) || !ids.Add(definition.EnemyId))
                throw new ArgumentException("Presentation definitions must be valid, saved and unique by asset and EnemyId.");
        }
        return ids.SelectMany(id=>new[]{id+"|calibrated-walk-run-back-stop",id+"|calibrated-pool-reuse"}).OrderBy(k=>k,StringComparer.Ordinal).ToArray();
    }
    internal static bool PresentationCoverageMatches(string[] expected,JArray results,out string reason)
    {
        var actual=(results??new JArray()).Where(c=>((string)c["scenario"]??"").StartsWith("calibrated-",StringComparison.Ordinal))
            .Select(c=>(string)c["id"]+"|"+(string)c["scenario"]).ToArray();
        bool valid=expected!=null && expected.Length>0 && expected.Distinct(StringComparer.Ordinal).Count()==expected.Length
            && actual.Length==expected.Length && actual.Distinct(StringComparer.Ordinal).Count()==actual.Length
            && new HashSet<string>(expected,StringComparer.Ordinal).SetEquals(actual);
        reason=valid?null:"Presentation case coverage is empty, duplicated, missing or unexpected.";
        return valid;
    }
    static void WriteResult(string state)
    {
        if(plan==null)return;
        File.WriteAllText(Path.Combine(plan.directory,"player-loop-results.json"),new JObject{["status"]=state,["failure"]=failure,
            ["cases"]=cases,["expectedPresentationCases"]=plan.presentationCaseKeys==null?null:JArray.FromObject(plan.presentationCaseKeys),["crowdDeathFixture"]=plan.undeadCrowdVerification,["controlledRenderTimeStep"]=true,["measuredPerformanceFps"]=false,["actualAnimatorPhysicsPlayerLoop"]=cases.Count>0,
            ["geometryFixture"]=plan.presentationCalibration?"Saved spawn, locomotion Animator and physics, ground clearance and pool reuse; AI selection disabled":!string.IsNullOrEmpty(plan.themeActivationPath)?"Actual PersistentScene player, seven authored map themes, real field spawn budgets and shared pool":plan.realPlayerParry?"Actual PersistentScene player, dungeon spawn service, saved Actor AI and real accepted heavy/parry":plan.leaseVerification?"Saved spawn service, catalog, AI, abilities and pool; actual saved player physical capsule and separate combat volume":plan.savedProfiles?"Saved actor/ability/profile/controller; native player-sized capsule at approach boundary":"Controlled oversized target; native authored shapes, reach10 only in memory",
            ["savedProfiles"]=plan.savedProfiles,["actorLeaseVerification"]=plan.leaseVerification,["testDefinition"]=plan.testDefinition,["leaseDefinitionPaths"]=plan.leaseDefinitionPaths==null?null:JArray.FromObject(plan.leaseDefinitionPaths),["realPlayerParry"]=plan.realPlayerParry&&string.IsNullOrEmpty(plan.themeActivationPath),["nativeAttackBatch"]=plan.attackBatch,["themeActivationPlan"]=plan.themeActivationPath,["fullGameRosterApplied"]=false,["newAudioApplied"]=false}.ToString());
    }
    static void Return(string error)
    {
        activeParryCapture?.Dispose();activeParryCapture=null;
        if(plan==null)return;
        if(error!=null)failure=error;
        if(plan.presentationCalibration && !PresentationCoverageMatches(plan.presentationCaseKeys,cases,out string coverageError))
        { failure=string.IsNullOrEmpty(failure)?coverageError:failure+"\n"+coverageError; error=failure; }
        WriteResult(error==null && cases.Count>0 && cases.Count==(!string.IsNullOrEmpty(plan.themeActivationPath)?7:!string.IsNullOrEmpty(plan.attackBatch)?plan.expectedWeakCases:plan.realPlayerParry?(plan.leaseDefinitionPaths?.Length??1):plan.leaseVerification?(plan.leaseDefinitionPaths?.Length>0?plan.leaseDefinitionPaths.Length*2:string.IsNullOrEmpty(plan.testDefinition)?8:2):plan.stress?15:24) && cases.All(c=>(bool)c["pass"])?"PASS_SCOPED_PLAYER_LOOP":"FAIL");
        plan.phase="returning";plan.deadline=EditorApplication.timeSinceStartup+120;Save();
        if(OwnPlay)EditorApplication.ExitPlaymode();
    }
    static void Changed(PlayModeStateChange state)
    {
        if(plan==null)return;
        if(state==PlayModeStateChange.ExitingPlayMode && plan.phase=="running")Return("Play interrupted");
        if(state==PlayModeStateChange.EnteredEditMode && plan.phase=="booting")Return("Boot cancelled");
    }
    static void BeforeReload()
    { if(plan!=null && plan.phase=="running")Return("Assembly reload interrupted the run"); }
    static void FinishReturn()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)return;
        string env=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable);
        string prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", "");
        if(!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory) || !string.IsNullOrEmpty(env)&&!Same(env,Account)
            || !string.IsNullOrEmpty(prepared)&&!Same(prepared,Account))
        {
            if(EditorApplication.timeSinceStartup>plan.deadline)
            {
                File.WriteAllText(Path.Combine(plan.directory,"return.json"),new JObject{["status"]="DEFERRED_FOREIGN_ACCOUNT",
                    ["reason"]="Another account is active or prepared; ownership was preserved.",["fixture"]=plan.fixture}.ToString());
                plan.phase="deferred";Save();
            }
            return;
        }
        if(host!=null && routine!=null)host.StopCoroutine(routine);
        for(int i=owned.Count-1;i>=0;i--)if(owned[i]!=null)UnityEngine.Object.DestroyImmediate(owned[i]);
        owned.Clear();host=null;routine=null;
        Time.captureDeltaTime=plan.captureDelta;
        // The verifier never changes fixedDeltaTime. Reassigning its float
        // getter would round Unity's rational timestep again and dirty settings.
        if(Time.timeScale!=plan.timeScale)Time.timeScale=plan.timeScale;
        Application.runInBackground=plan.background;
        if(AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)==plan.fixture)
            EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(plan.previousStart)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(plan.previousStart);
        IsolatedSavePlayGuard.UseRealAccount();
        if(!plan.realPlayerParry && plan.fixture=="Assets/Editor/Testers/Characters/WeakPlayerLoop_"+plan.token+".unity"
            &&AssetDatabase.LoadAssetAtPath<SceneAsset>(plan.fixture)!=null)AssetDatabase.DeleteAsset(plan.fixture);
        var now=SceneEvidence();
        File.WriteAllText(Path.Combine(plan.directory,"return.json"),new JObject{["status"]=!IsolatedSavePlayGuard.RequiresAccountChoice&&JToken.DeepEquals(plan.scenes,now)?"PASS":"FAIL",
            ["scenesBefore"]=plan.scenes,["scenesAfter"]=now,["guardChoice"]=IsolatedSavePlayGuard.RequiresAccountChoice,
            ["guardActive"]=IsolatedSavePlayGuard.ActiveDirectory,["guardPrepared"]=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""),
            ["guardExpires"]=SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires",""),["environment"]=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),
            ["startScene"]=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),["captureDelta"]=Time.captureDeltaTime,["fixedDelta"]=Time.fixedDeltaTime,
            ["registryCount"]=CombatTargetRegistry.RegisteredCount,["temporarySceneRemoved"]=plan.realPlayerParry||AssetDatabase.LoadAssetAtPath<SceneAsset>(plan.fixture)==null}.ToString());
        SessionState.EraseString(Key+"plan");plan=null;
    }
}
