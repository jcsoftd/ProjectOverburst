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
public static class MonsterWeakAttackPlayerLoopVerifier
{
    const string Key = "Overburst.WeakAttackPlayerLoop.";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    sealed class Plan
    {
        public string directory, fixture, previousStart, phase, token;
        public bool background, fixedAnimator, stress, savedProfiles;
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
        Directory.CreateDirectory(outputDirectory);
        plan=new Plan { directory=outputDirectory,token=Guid.NewGuid().ToString("N"),phase="booting",
            previousStart=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),background=Application.runInBackground,
            captureDelta=Time.captureDeltaTime,fixedDelta=Time.fixedDeltaTime,deadline=EditorApplication.timeSinceStartup+180,
            fixedAnimator=fixedAnimator,attackSpeed=attackSpeed,timeScale=Time.timeScale,stress=stress,savedProfiles=savedProfiles,scenes=SceneEvidence() };
        plan.fixture="Assets/Editor/Testers/Characters/WeakPlayerLoop_"+plan.token+".unity";
        cases.Clear(); failure=null; Save();
        File.WriteAllText(Path.Combine(outputDirectory,"plan.json"),JsonConvert.SerializeObject(plan,Formatting.Indented));
        Scene active=SceneManager.GetActiveScene(); Scene fixture=default;
        try
        {
            fixture=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            if(!EditorSceneManager.SaveScene(fixture,plan.fixture))throw new InvalidOperationException("Fixture save failed.");
            EditorSceneManager.CloseScene(fixture,true); fixture=default;
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
                plan.phase="running"; Save();
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
        while(true)
        {
            bool more; object current;
            try { more=body.MoveNext(); current=more?body.Current:null; }
            catch(Exception error){ (body as IDisposable)?.Dispose(); Return(error.ToString()); yield break; }
            if(!more)break;
            yield return current;
        }
        (body as IDisposable)?.Dispose(); Return(null);
    }
    static AnimationClip Source(JObject row) => AssetDatabase.LoadAllAssetsAtPath((string)row["sourcePath"]).OfType<AnimationClip>()
        .Single(c=>AssetDatabase.TryGetGUIDAndLocalFileIdentifier(c,out string guid,out long id)
            && guid==(string)row["sourceGuid"] && id==(long)row["sourceLocalId"]);
    static Vector3 Vector(JToken value)=>new Vector3((float)value[0],(float)value[1],(float)value[2]);
    static IEnumerator RunCases()
    {
        var author=JObject.Parse(File.ReadAllText(Path.Combine(Workspace,"개인파일/코덱스산출/Monsters/MonsterOverhaulV3/GOAL_A/20261004/attack-authoring.json")));
        var rows=author["entries"].OfType<JObject>().Where(r=>(bool?)r["nativeContactGeometryAuthored"]==true).ToArray();
        if(rows.Length!=8)throw new InvalidOperationException("Expected eight measured native attacks.");
        var runs=from fps in new[]{15,30,60}
            from row in rows where !plan.stress || (string)row["selectionKey"]=="06b6021ea737706a"
            from scenario in plan.stress?new[]{"cancel","freeze","hitstop","first-miss","cancel-on-hit"}:new[]{"normal"}
            select new {fps,row,scenario};
        foreach(var run in runs)
        {
            int fps=run.fps;JObject row=run.row;string scenario=run.scenario;
            Time.captureDeltaTime=1f/fps;
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
            victim.transform.position=go.transform.position+Vector3.forward*startingDistance;
            var collider=victim.AddComponent<CapsuleCollider>();collider.center=playerVolume.Center;collider.radius=playerVolume.Radius;collider.height=playerVolume.HalfHeight*2;
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
            bool acted=false,returnedTarget=false,pauseVerified=true;
            while(started && melee.IsAttacking && Time.time<deadline)
            {
                bridge.TryGetAttackMotionTime(ability.AnimatorTrigger,clip,out float actual);
                samples.Add(new JObject{["frame"]=Time.frameCount,["time"]=Time.time,["delta"]=Time.deltaTime,["fixedTime"]=Time.fixedTime,
                    ["clock"]=melee.WeakAttackNormalizedTime,["animator"]=actual,["entered"]=melee.HasEnteredWeakAttack});
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
            last["targetRadius"]=playerVolume.Radius;last["expectedDamage"]=expectedDamage;
            WriteResult("RUNNING");
            melee.CancelAttack();UnityEngine.Object.Destroy(go);UnityEngine.Object.Destroy(victim);
            yield return null;
        }
    }
    static void WriteResult(string state)
    {
        if(plan==null)return;
        File.WriteAllText(Path.Combine(plan.directory,"player-loop-results.json"),new JObject{["status"]=state,["failure"]=failure,
            ["cases"]=cases,["controlledRenderTimeStep"]=true,["measuredPerformanceFps"]=false,["actualAnimatorPhysicsPlayerLoop"]=true,
            ["geometryFixture"]=plan.savedProfiles?"Saved actor/ability/profile/controller; native player-sized capsule at approach boundary":"Controlled oversized target; native authored shapes, reach10 only in memory",
            ["savedProfiles"]=plan.savedProfiles,["fullGameRosterApplied"]=false,["newAudioApplied"]=false}.ToString());
    }
    static void Return(string error)
    {
        if(plan==null)return;
        if(error!=null)failure=error;
        WriteResult(error==null && cases.Count==(plan.stress?15:24) && cases.All(c=>(bool)c["pass"])?"PASS_SCOPED_PLAYER_LOOP":"FAIL");
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
        if(AssetDatabase.LoadAssetAtPath<SceneAsset>(plan.fixture)!=null)AssetDatabase.DeleteAsset(plan.fixture);
        var now=SceneEvidence();
        File.WriteAllText(Path.Combine(plan.directory,"return.json"),new JObject{["status"]=!IsolatedSavePlayGuard.RequiresAccountChoice&&JToken.DeepEquals(plan.scenes,now)?"PASS":"FAIL",
            ["scenesBefore"]=plan.scenes,["scenesAfter"]=now,["guardChoice"]=IsolatedSavePlayGuard.RequiresAccountChoice,
            ["guardActive"]=IsolatedSavePlayGuard.ActiveDirectory,["guardPrepared"]=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""),
            ["guardExpires"]=SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires",""),["environment"]=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),
            ["startScene"]=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),["captureDelta"]=Time.captureDeltaTime,["fixedDelta"]=Time.fixedDeltaTime,
            ["registryCount"]=CombatTargetRegistry.RegisteredCount,["temporarySceneRemoved"]=AssetDatabase.LoadAssetAtPath<SceneAsset>(plan.fixture)==null}.ToString());
        SessionState.EraseString(Key+"plan");plan=null;
    }
}
