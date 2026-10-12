using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.Persistence;
using Overburst.Quests;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class PlayerLevelUpFeedbackVerifier
{
    const string K = "Overburst.PlayerLevelUpFeedbackVerifier.";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
    static readonly List<string> checks = new List<string>();
    static readonly Stack<IEnumerator> tasks = new Stack<IEnumerator>();
    static string output;
    static double deadline;
    static int frame = -1, events;
    static GameObject fixture;
    static PlayerProgression progression;
    static PlayerActorRuntime actor;
    static PlayerLevelUpFeedback feedback;
    static PlayerLevelUpNotification notification;
    static readonly List<Object> temporaryAssets = new List<Object>();
    static PlayerLevelUpFeedbackVerifier()
    {
        EditorApplication.playModeStateChanged += State;
        if (SessionState.GetString(K + "pending", "") != "") EditorApplication.update += Begin;
        if (SessionState.GetString(K + "return", "") != "") EditorApplication.update += Return;
    }
    static void Write(string name, object value) => File.WriteAllText(Path.Combine(output, name), JsonConvert.SerializeObject(value, Formatting.Indented));
    static void Check(bool pass, string name)
    { if (!pass) throw new InvalidOperationException(name); checks.Add(name); Write("progress.json", new { count = checks.Count, last = name }); }
    static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private).GetValue(obj);
    static object Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Private).Invoke(obj, args);
    static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Private).SetValue(obj, value);
    static object Scenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(i => {
        var s = SceneManager.GetSceneAt(i); return new { s.path, s.isDirty, roots = s.GetRootGameObjects().Select(g => g.GetInstanceID()).OrderBy(id => id).ToArray() }; }).ToArray();

    public static string Native(string directory)
    {
        AppearanceCustomizationBuilder.RequireIdle(); output = Path.GetFullPath(directory); Directory.CreateDirectory(output); checks.Clear();
        var prefab = Resources.Load<GameObject>(PlayerLevelUpNotification.ResourcePath);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerLevelUpFeedbackBuilder.Source).transform.Find("Notification (Level)");
        Check(prefab != null && prefab.GetComponent<PlayerLevelUpNotification>() != null, "Runtime resource/view resolves");
        Check(prefab.GetComponentsInChildren<Transform>(true).All(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0), "No missing script");
        var view = prefab.GetComponent<PlayerLevelUpNotification>();
        var panel = Field<RectTransform>(view, "panel");
        Check(panel != null && Field<Text>(view, "levelText") != null && Field<CanvasGroup>(view, "visibility") != null, "All serialized references resolve");
        Check(panel.anchoredPosition == new Vector2(0, 260) && panel.localScale == Vector3.one * .55f, "Approved top placement and authored scale");
        var oldGraphics = source.GetComponentsInChildren<Graphic>(true);
        var graphics = panel.GetComponentsInChildren<Graphic>(true);
        Check(graphics.Length == oldGraphics.Length && graphics.Length == 14, "All fourteen existing graphics retained");
        for (int i = 0; i < graphics.Length; i++)
        {
            Check(graphics[i].color == oldGraphics[i].color && graphics[i].material == oldGraphics[i].material, "Existing tint/material " + graphics[i].name);
            if (graphics[i] is Image image) Check(image.sprite == ((Image)oldGraphics[i]).sprite && image.type == ((Image)oldGraphics[i]).type, "Existing sprite/type " + image.name);
            if (graphics[i] is Text text)
            { var original = (Text)oldGraphics[i]; Check(text.font == original.font && text.fontSize == original.fontSize && text.text == original.text, "Existing typography/text " + text.name); }
        }
        Check(graphics.All(g => !g.raycastTarget) && !Field<CanvasGroup>(view,"visibility").blocksRaycasts
            && !Field<CanvasGroup>(view,"visibility").interactable && prefab.GetComponent<GraphicRaycaster>() == null, "Notification cannot capture pointer or input");
        var clip = CombatActionSfxService.ResolveNamedClip("PlayerLevelUp");
        Check(clip != null && clip.frequency == 48000 && clip.channels == 2 && Math.Abs(clip.length - 3.638354f) < .001f, "Confirmed stereo clip resolves unchanged duration");
        using (var sha = System.Security.Cryptography.SHA256.Create())
            Check(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(PlayerLevelUpFeedbackBuilder.Audio))).Replace("-", "").ToLowerInvariant()
                == "1e915c7c8f3c3634c3eb4ff478e10b6b8ebdfb6408ea8bfbd95806897ba8b6f4", "Approved WAV bytes preserved");
        var settings = ((AudioImporter)AssetImporter.GetAtPath(PlayerLevelUpFeedbackBuilder.Audio)).defaultSampleSettings;
        Check(settings.compressionFormat == AudioCompressionFormat.PCM && settings.preloadAudioData, "PCM preload import preserves selected waveform");
        var zoom = Resources.Load<GameObject>(PlayerLevelUpFeedback.ZoomBlurResourcePath);
        Check(zoom != null && zoom.GetComponent<ParticleSystem>() != null, "Zoom resource resolves a particle effect");
        var zoomMain = zoom.GetComponent<ParticleSystem>().main;
        Check(!zoomMain.loop && zoomMain.useUnscaledTime && zoomMain.maxParticles == 1 && Mathf.Abs(zoomMain.startLifetime.constant - .22f) < .001f, "Zoom is a short single particle using real time");
        var zoomMat = zoom.GetComponent<ParticleSystemRenderer>().sharedMaterial;
        var parry = Resources.Load<GameObject>("Combat/VFX/PF_ParryZoomBlur_Normal").GetComponent<ParticleSystemRenderer>().sharedMaterial;
        Check(zoomMat.shader == parry.shader && zoomMat.shader.isSupported && zoomMat != parry, "Original Easy Zoom shader with an independent supported material");
        Check(Mathf.Abs(zoomMat.GetFloat("_Strength")-.035f)<.0001f && zoomMat.GetFloat("_Strength") < parry.GetFloat("_Strength") && zoomMat.GetFloat("_Opacity") < parry.GetFloat("_Opacity"), "Level zoom remains milder than existing parry");
        Check(zoomMat.GetFloat("_Samples") == 8 && parry.GetFloat("_Strength") == .14f && parry.GetFloat("_Opacity") == .85f, "Eight samples and approved normal parry values preserved");
        var levelEffect = Resources.Load<GameObject>("VFX/PF_PlayerLevelUpVfx");
        var glow = levelEffect.GetComponent<PlayerLevelUpBodyGlow>();
        Check(glow != null && levelEffect.GetComponentsInChildren<Transform>(true).All(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0), "VFX resource contains a fully bound Glow component without missing scripts");
        var glowMaterial = Field<Material>(glow,"sourceMaterial");
        Check(glowMaterial != null && glowMaterial.shader.isSupported && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(glowMaterial.shader))=="ef6400b7c5649584f899c7e52666052d", "Original package Glow shader resolves and is supported");
        var red=Field<AnimationCurve>(glow,"red"); var green=Field<AnimationCurve>(glow,"green"); var blue=Field<AnimationCurve>(glow,"blue");
        Check(red.length==4 && green.length==4 && blue.length==4 && Mathf.Abs(red.keys.Last().time-2.2666667f)<.0001f, "Original RGB glow curves retain four keys and complete duration");
        Check(Mathf.Abs(red.Evaluate(.55f)-5.9921575f)<.0001f && Mathf.Abs(green.Evaluate(.55f)-3.532962f)<.0001f && Mathf.Abs(blue.Evaluate(.55f)-1.035294f)<.0001f && red.Evaluate(0)<.001f && red.Evaluate(2.2666667f)<.001f, "Original body glow rises to its golden peak and fades out");
        Check(levelEffect.GetComponentsInChildren<Animator>(true).Length==0 && levelEffect.GetComponentsInChildren<Animation>(true).Length==0, "Level effect cannot play a character pose");
        var graph = levelEffect.GetComponentInChildren<UnityEngine.VFX.VisualEffect>();
        Check(graph != null && graph.visualEffectAsset != null && graph.HasSkinnedMeshRenderer("SkinnedMeshRenderer") && graph.HasVector3("Transform_position"), "Original skinned Graph and transform properties retained");
        var result = new { status = "PASS", scope = "NATIVE", count = checks.Count, checks };
        Write("native-results.json", result); return JsonConvert.SerializeObject(result);
    }

    public static void Start(string directory)
    {
        AppearanceCustomizationBuilder.RequireIdle();
        if (IsolatedSavePlayGuard.ActiveDirectory != "" || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY"))) throw new InvalidOperationException("Another account/settings session owns the Editor");
        output = Path.GetFullPath(directory); if (Directory.Exists(output)) throw new InvalidOperationException("Use a new owned Play folder");
        Directory.CreateDirectory(output); var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var seed = NewAccountFactory.Create(registry); seed.level = 17; seed.experience = OverburstGrowthRules.ExperienceToNext(17) - 10;
        seed.skillTree = AccountSkillTree.GrantEligible(seed.skillTree, seed.level); AccountCharacterCommands.EnsureLegacy(seed);
        new EasySaveAccountStore(Path.Combine(output,"Account")).Save(seed,"level-up-fixture");
        SessionState.SetString(K+"pending",output); SessionState.SetString(K+"return",output);
        SessionState.SetString(K+"start",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(K+"background",Application.runInBackground); SessionState.SetFloat(K+"deadline",(float)EditorApplication.timeSinceStartup+180);
        Write("edit-before.json",new { scenes=Scenes(), start=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene), input=UnityEngine.InputSystem.InputSystem.settings.GetInstanceID() });
        Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY",Path.Combine(output,"Settings"));
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        EditorApplication.update-=Begin; EditorApplication.update+=Begin;
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output,"Account")); }
        catch { Finish(new Exception("Owned Play could not start")); throw; }
    }
    static bool OwnPlay => IsolatedSavePlayGuard.ActiveDirectory == Path.Combine(SessionState.GetString(K+"return",""),"Account");
    static void Begin()
    {
        output=SessionState.GetString(K+"pending",""); if(output=="") { EditorApplication.update-=Begin; return; }
        if(EditorApplication.timeSinceStartup>SessionState.GetFloat(K+"deadline",0)) { Finish(new Exception("Boot timeout")); return; }
        if(!EditorApplication.isPlaying || !OwnPlay || TitleCharacterPanel.Instance==null) return;
        EditorApplication.update-=Begin; SessionState.EraseString(K+"pending"); checks.Clear(); events=0;
        Application.runInBackground=true; deadline=EditorApplication.timeSinceStartup+180; tasks.Push(Verify()); frame=-1;
        EditorApplication.update+=Tick;
    }
    static void Tick()
    {
        if(Time.frameCount==frame && EditorApplication.timeSinceStartup<deadline) return;
        frame=Time.frameCount; EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if(!EditorApplication.isPlaying || !OwnPlay || EditorApplication.timeSinceStartup>deadline) throw new Exception("Play ended or timed out");
            while(tasks.Count>0) { var task=tasks.Peek(); if(!task.MoveNext()) { (tasks.Pop() as IDisposable)?.Dispose(); continue; }
                if(task.Current is IEnumerator nested) { tasks.Push(nested); continue; } return; }
            Finish(null);
        }
        catch(Exception error) { Finish(error); }
    }
    static IEnumerator Frames(int n) { while(n-->0) yield return null; }
    static IEnumerator Seconds(float seconds) { float end=Time.unscaledTime+seconds; while(Time.unscaledTime<end) yield return null; }
    static IEnumerator Wait(Func<bool> condition,string name,int n=1800) { while(!condition() && n-->0) yield return null; Check(condition(),name); }
    static IEnumerator Capture(string name) { ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png")); yield return Frames(4); }
    static void Leveled(int before,int after) => events++;
    static int LevelVoices() => Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Count(a=>a.isPlaying && a.clip==CombatActionSfxService.ResolveNamedClip("PlayerLevelUp"));
    static void Grant(int xp) { progression.AddExperience(xp); Check(progression.FlushPendingExperience(),"Experience transaction committed"); }

    static IEnumerator Verify()
    {
        yield return Frames(6); TitleCharacterPanel.Instance.start.onClick.Invoke(); yield return Frames(3); TitleCharacterPanel.Instance.enter.onClick.Invoke();
        yield return Wait(()=>AccountBootstrap.Ready && WorldSessionState.IsHideout && !PersistentSceneFlow.Instance.IsSwitching,"Real title-to-town boot with isolated account");
        yield return Frames(12); actor=PlayerContext.Instance.CurrentActor; progression=PlayerProgression.Current; feedback=PlayerContext.Instance.GetComponent<PlayerLevelUpFeedback>();
        Check(actor != null && feedback != null && progression.Level==17,"Bootstrap binds real player progression/feedback");
        yield return ZoomCases();
        var bodies=actor.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.enabled && r.gameObject.activeInHierarchy && r.sharedMesh!=null).ToArray();
        var originalMaterials=bodies.Select(r=>r.sharedMaterials).ToArray();
        var animator=actor.GetComponentInChildren<Animator>(); var controller=animator.runtimeAnimatorController;
        int poseBefore=animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
        progression.LeveledUp+=Leveled; actor.Health.RestoreLivingHealth(7); Grant(10);
        Check(animator.runtimeAnimatorController==controller && animator.GetCurrentAnimatorStateInfo(0).fullPathHash==poseBefore,"Level-up preserves character controller and current pose");
        Check(events==1 && progression.Level==18 && actor.Health.CurrentHp==actor.Health.MaxHp,"Single level commit refills final maximum HP and emits once");
        Check(LevelVoices()==1,"One selected sound voice starts");
        yield return Seconds(.4f); notification=Field<PlayerLevelUpNotification>(feedback,"notification");
        Check(notification != null && notification.IsVisible && Field<Text>(notification,"levelText").text=="레벨 18","Real HUD shows approved level18 notification");
        Check(Math.Abs(Field<CanvasGroup>(notification,"visibility").alpha-1)<.001,"Entrance finishes at full opacity");
        Check(Field<GameObject>(PlayerContext.Instance.GetComponent<PlayerLevelUpVfx>(),"activeEffect") != null,"Existing VFX still starts on real skinned player");
        Check(!GameplayInputBlocker.IsGameplayInputBlocked && Time.timeScale==1,"Notification preserves gameplay input/time");
        var shells=actor.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r=>r.name=="LevelUpBodyGlow").ToArray();
        Check(shells.Length==bodies.Length && shells.Length>1,"Every visible modular body part receives glow");
        Check(!OverburstWorldHighlight.CollectPlayerModelRenderers(actor.transform).Any(r=>r.name==PlayerLevelUpBodyGlow.RendererName),"Temporary glow surfaces are excluded from the player's occlusion silhouette");
        Check(shells.All(r=>r.sharedMesh==r.transform.parent.GetComponent<SkinnedMeshRenderer>().sharedMesh && r.rootBone==r.transform.parent.GetComponent<SkinnedMeshRenderer>().rootBone && r.bones.SequenceEqual(r.transform.parent.GetComponent<SkinnedMeshRenderer>().bones)),"Glow follows the same actual animated mesh and bones");
        Check(shells.All(r=>r.sharedMaterials.Length==r.sharedMesh.subMeshCount && r.sharedMaterials.All(m=>m==shells[0].sharedMaterial)),"Glow covers every submesh with one owned shared material");
        Check(bodies.Select((r,i)=>r.sharedMaterials.SequenceEqual(originalMaterials[i])).All(x=>x),"All original appearance materials remain untouched");
        Check(shells[0].sharedMaterial.GetColor("_Color").r>3f,"Actual player glow animates to visible HDR gold");
        var activeGraph=Field<UnityEngine.VFX.VisualEffect>(PlayerContext.Instance.GetComponent<PlayerLevelUpVfx>(),"activeVisualEffect");
        Check(activeGraph.GetSkinnedMeshRenderer("SkinnedMeshRenderer")!=null && activeGraph.aliveParticleCount>0,"Actual GPU Graph produces particles with real body binding");
        yield return Capture("level18-runtime");
        Grant(OverburstGrowthRules.ExperienceToNext(18)+OverburstGrowthRules.ExperienceToNext(19)+OverburstGrowthRules.ExperienceToNext(20));
        yield return Frames(2); Check(events==2 && progression.Level==21 && Field<Text>(notification,"levelText").text=="레벨 21","One multi-level grant updates the same panel once");
        Check(Object.FindObjectsByType<PlayerLevelUpNotification>(FindObjectsSortMode.None).Length==1 && LevelVoices()==1,"Repeated level reward reuses one panel and does not overlap audio");
        actor.Health.RestoreLivingHealth(9); Grant(1); Check(events==2 && actor.Health.CurrentHp==9,"XP without a level does not refill HP or replay");
        yield return Seconds(2.0f); float alpha=Field<CanvasGroup>(notification,"visibility").alpha;
        Check(alpha>0 && alpha<1,"Exit fades rather than vanishing");
        Grant(progression.ExperienceToNext-progression.Experience); yield return Frames(2);
        Check(Field<CanvasGroup>(notification,"visibility").alpha>=alpha && Field<CanvasGroup>(notification,"visibility").alpha>0,"Reward during exit reverses without an alpha flash");
        yield return Seconds(.16f); Check(Field<CanvasGroup>(notification,"visibility").alpha==1,"Exit interruption returns to full opacity");
        yield return Seconds(2.4f); Check(!notification.IsVisible && Field<CanvasGroup>(notification,"visibility").alpha==0,"UI hides after its unscaled fade sequence");
        Check(!actor.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r=>r.name=="LevelUpBodyGlow"),"Glow ends and releases temporary body renderers after its curve");
        Call(progression,"ApplyAccountProgression",progression.Level,progression.Experience,false); Check(events==3,"Plain account projection does not emit level feedback");
        actor.Health.SetRunMapModifiers(.5f,.1f); actor.Health.RestoreLivingHealth(5); Grant(progression.ExperienceToNext-progression.Experience);
        Check(actor.Health.CurrentHp==actor.Health.MaxHp,"Full level HP refill survives maxHP and healing penalties"); actor.Health.SetRunMapModifiers(1,1);
        yield return PulseCases();
        yield return RewardCases();
        CombatActionSfxService.StopLevelUp(); Time.timeScale=0; actor.Health.RestoreLivingHealth(5); Grant(progression.ExperienceToNext-progression.Experience);
        yield return Seconds(2.9f); Check(!notification.IsVisible && actor.Health.CurrentHp==actor.Health.MaxHp,"Paused UI still completes using real time and HP refills");
        Check(Field<int>(feedback,"pendingPulses")==1,"Paused world queues exactly one pulse"); Time.timeScale=1; yield return Frames(2);
        Check(Field<int>(feedback,"pendingPulses")==0,"Resume consumes the pending pulse once");
        Grant(progression.ExperienceToNext-progression.Experience); yield return Frames(2); RunWalkableContext.Clear(); yield return Frames(2);
        Check(!notification.IsVisible && LevelVoices()==0 && Field<ParticleSystem>(feedback,"zoomBlur")==null,"Run revision discards previous UI/audio/zoom state");
        Check(Field<GameObject>(PlayerContext.Instance.GetComponent<PlayerLevelUpVfx>(),"activeEffect")==null && !actor.GetComponentsInChildren<SkinnedMeshRenderer>().Any(r=>r.name=="LevelUpBodyGlow"),"Run revision also releases Graph and body glow");
        int before=events; Set(actor.Health,"currentHp",0f); typeof(CombatHealth).GetProperty("IsDead").SetValue(actor.Health,true);
        Grant(progression.ExperienceToNext-progression.Experience); yield return Frames(2);
        Check(events==before+1 && actor.Health.IsDead && actor.Health.CurrentHp==0 && !notification.IsVisible && LevelVoices()==0,"Dead actor keeps committed growth without resurrection/feedback");
        Check(Field<GameObject>(PlayerContext.Instance.GetComponent<PlayerLevelUpVfx>(),"activeEffect")==null,"Dead actor never starts Graph or body glow");
        actor.Health.ResetHealth();
    }

    static IEnumerator ZoomCases()
    {
        var prefab = Resources.Load<GameObject>(PlayerLevelUpFeedback.ZoomBlurResourcePath);
        long requests = TransientVfxPool.GetStatistics(prefab).Requests;
        Time.timeScale = 0;
        Call(feedback,"PlayZoomBlur"); Call(feedback,"PlayZoomBlur");
        var particle = Field<ParticleSystem>(feedback,"zoomBlur");
        Check(particle != null && particle.isPlaying && TransientVfxPool.GetStatistics(prefab).Active == 1, "Real actor starts one pooled zoom particle");
        Check(TransientVfxPool.GetStatistics(prefab).Requests == requests+1, "Same-frame repeated zoom never stacks screen effects");
        yield return Capture("zoom-runtime"); yield return Seconds(.4f);
        Check(TransientVfxPool.GetStatistics(prefab).Active == 0, "Zoom naturally returns to pool while world is paused");
        Time.timeScale = 1;
    }

    static EnemyMovementReaction Enemy(string name,Vector3 local,EnemyHitWeight? weight=null,EnemyGradeType grade=EnemyGradeType.Normal)
    {
        var go=new GameObject(name); go.transform.SetParent(fixture.transform,false); go.transform.localPosition=local;
        var body=go.AddComponent<Rigidbody>(); body.isKinematic=true; body.useGravity=false;
        go.AddComponent<CombatAffiliation>().Configure(CombatTeam.Enemy); go.AddComponent<CombatHealth>(); go.AddComponent<CombatTarget>();
        var rank=go.AddComponent<EnemyRank>(); typeof(EnemyRank).GetProperty("GradeType").SetValue(rank,grade);
        var reaction=go.AddComponent<EnemyMovementReaction>();
        if(weight.HasValue) {
            var profile=ScriptableObject.CreateInstance<EnemyMovementProfile>(); var hit=ScriptableObject.CreateInstance<EnemyHitWeightProfile>();
            hit.Configure(weight.Value,.1f,.5f,.16f,.2f,0,.2f,.35f); Set(profile,"hitWeightProfile",hit);
            var movement=go.AddComponent<EnemyMovement>(); Set(movement,"profile",profile); movement.enabled=false;
            temporaryAssets.Add(profile); temporaryAssets.Add(hit); reaction.ResolveReferences();
        }
        return reaction;
    }
    static IEnumerator PulseCases()
    {
        fixture=new GameObject("OwnedLevelUpPhysicsFixture"); fixture.transform.position=new Vector3(800,10,800);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.SetParent(fixture.transform,false); floor.transform.localPosition=new Vector3(0,-.26f,0); floor.transform.localScale=new Vector3(30,.5f,30);
        ActorTeleportUtility.TeleportSafely(actor.transform,fixture.transform.position,Quaternion.identity); yield return Frames(3);
        Vector3 origin=actor.transform.position; fixture.transform.position=origin;
        var light=Enemy("Light",new Vector3(2,0,0),EnemyHitWeight.Light);
        var standard=Enemy("Standard",new Vector3(-2,0,0),EnemyHitWeight.Standard);
        var heavy=Enemy("Heavy",new Vector3(0,0,2),EnemyHitWeight.Heavy);
        var elite=Enemy("Elite",new Vector3(-1.5f,0,-1.5f),null,EnemyGradeType.Elite);
        var greater=Enemy("GreaterElite",new Vector3(1.4f,0,1.4f),null,EnemyGradeType.GreaterElite);
        var boss=Enemy("Boss",new Vector3(-1.4f,0,-1.4f),null,EnemyGradeType.Boss);
        var frozen=Enemy("Frozen",new Vector3(-1.4f,0,1.4f)); frozen.GetComponent<EnemyMotor>().SetFrozen(true);
        var edge=Enemy("RadiusBoundary",new Vector3(4,0,0)); var outside=Enemy("Outside",new Vector3(4.01f,0,1));
        var upper=Enemy("OtherFloor",new Vector3(1,1.51f,0)); var onHeight=Enemy("HeightBoundary",new Vector3(-1,1.5f,0));
        var dead=Enemy("Dead",new Vector3(0,0,3)); Set(dead.GetComponent<CombatHealth>(),"currentHp",0f); typeof(CombatHealth).GetProperty("IsDead").SetValue(dead.GetComponent<CombatHealth>(),true);
        var behind=Enemy("BehindWall",new Vector3(0,0,-3));
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name="PulseOcclusionWall"; wall.transform.SetParent(fixture.transform,false);wall.transform.localPosition=new Vector3(0,1,-2.5f);wall.transform.localScale=new Vector3(.8f,2,.1f);
        var stopped=Enemy("SweepStop",new Vector3(3,0,1));
        var stopWall=GameObject.CreatePrimitive(PrimitiveType.Cube);stopWall.transform.SetParent(fixture.transform,false);stopWall.transform.localPosition=new Vector3(3.8f,1,1.2f);stopWall.transform.localScale=new Vector3(.1f,2,1);
        var overlap=Enemy("Overlap",Vector3.zero); var parried=Enemy("Parried",new Vector3(1.5f,0,-1.5f));parried.ApplyParryStun(2f);
        var expected=new[]{(light,1.2f),(standard,.8f),(heavy,.5f),(elite,.4f),(greater,.25f)};
        Physics.SyncTransforms(); int before=events; Grant(progression.ExperienceToNext-progression.Experience);
        Check(events==before+1,"Pulse tied to one committed event");
        foreach(var row in expected) { var target=Field<Vector3>(row.Item1,"knockbackTargetPosition"); Check(Math.Abs(Vector3.Distance(target,row.Item1.transform.position)-row.Item2)<.005f,"Requested distance "+row.Item1.name); }
        Check(expected.All(r=>r.Item1.BlocksAttack),"All ordinary/elite tiers receive forced attack block");
        Check(!boss.IsKnockbackActive && !frozen.IsKnockbackActive && !dead.IsKnockbackActive && !upper.IsKnockbackActive && !outside.IsKnockbackActive,"Boss/frozen/dead/other-floor/outside exceptions");
        Check(edge.IsKnockbackActive && onHeight.IsKnockbackActive,"Radius4m and height1.5m inclusive boundaries");
        Check(!behind.IsKnockbackActive,"Solid wall occludes the pulse");
        Check(overlap.IsKnockbackActive && overlap.BlocksAttack
            && !float.IsNaN(Field<Vector3>(overlap,"knockbackTargetPosition").x),"Overlapped centers receive a bounded stable reaction");
        Check(parried.ParryStunRemaining>1.9f,"Longer parry stun preserved");
        Check(Vector3.Distance(Field<Vector3>(stopped,"knockbackTargetPosition"),stopped.transform.position)<.8f,"Capsule sweep clips at a nearby solid wall");
        Check(expected.All(r=>r.Item1.GetComponent<CombatHealth>().CurrentHp==r.Item1.GetComponent<CombatHealth>().MaxHp),"Pulse never applies damage");
        yield return Seconds(.09f); foreach(var row in expected) row.Item1.TickFixed(); yield return Frames(1);
        Check(light.transform.position.x>origin.x+2.5f && standard.transform.position.x<origin.x-2.3f,"Real reaction moves kinematic enemy roots outward");
        var late=Enemy("LateArrival",new Vector3(0,0,1));yield return Frames(2);Check(!late.IsKnockbackActive,"Late arrival is not hit by a persistent field");
        foreach(var reaction in fixture.GetComponentsInChildren<EnemyMovementReaction>()) reaction.gameObject.SetActive(false);
        var gap=Enemy("NarrowGap",new Vector3(1,0,0));bool[,] cells=new bool[40,40];for(int x=0;x<40;x++)for(int z=0;z<40;z++)cells[x,z]=x!=26;
        RunWalkableContext.SetCurrent(new RunWalkableArea(cells,40,40,origin.x-4,origin.z-4,.2f));Physics.SyncTransforms();
        Grant(progression.ExperienceToNext-progression.Experience);
        Check(Field<Vector3>(gap,"knockbackTargetPosition").x<origin.x+1.21f,"Walkable path prefix stops before a narrow forbidden cell");
        RunWalkableContext.Clear(); gap.gameObject.SetActive(false);
        var definition=Resources.Load<EnemyDefinition>("Enemies/Themes/Definitions/CavernMutants_Limadon");
        var product=EnemySpawnService.Current.Spawn(new EnemySpawnRequest(definition,actor.transform.position+Vector3.forward*1.2f,
            Quaternion.LookRotation(Vector3.back),actor.transform,spawnParent:fixture.transform));
        Check(product!=null,"Actual product enemy leases through the existing spawn/pool path");
        try
        {
            product.AI.enabled=false; product.Health.SetDamageDeathPrevention(product,true);
            yield return Frames(3);
            bool started=false;int attempts=120;
            while(!started && attempts-->0) { started=product.AbilityController.TryStart(actor.transform); if(!started) yield return null; }
            Check(started && product.AbilityController.IsExecuting,"Actual monster attack begins before the level reward");
            float hp=product.Health.CurrentHp;Vector3 start=product.transform.position;
            Grant(progression.ExperienceToNext-progression.Experience);
            Check(!product.AbilityController.IsExecuting && !product.Melee.IsAttacking,"Level-up cancels the active product attack");
            yield return Seconds(.2f);
            Check(product.Health.CurrentHp==hp && (product.transform.position-start).sqrMagnitude>.001f,"Product monster moves through reaction without damage");
            Check(product.AnimationBridge.HasAnimator,"Product reaction retains its authored animator");
            yield return Capture("product-monster-pulse");
        }
        finally { if(product!=null) product.RequestPoolRelease(); }
        Object.Destroy(fixture);fixture=null;yield return Frames(2);
    }

    static IEnumerator RewardCases()
    {
        var account=AccountGameplaySession.Current; var registry=Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var quest=QuestCatalog.All.First(q=>q.rewardExperience>0 && string.IsNullOrEmpty(q.prerequisiteId) && q.minimumLevel<=progression.Level);
        Check(account.ExecuteQuest("level-up-prepare-quest",account.Revision,s=>{
            AccountQuestCommands.Accept(s,quest,quest.giverMerchantId,registry);var q=s.quests.First(x=>x.questId==quest.id);
            foreach(var objective in q.objectives)objective.current=objective.required;
            s.experience=OverburstGrowthRules.ExperienceToNext(s.level)-Math.Min(quest.rewardExperience,OverburstGrowthRules.ExperienceToNext(s.level));return true;
        },true,true),"Real quest reward fixture prepared without a level change");
        int before=events;actor.Health.RestoreLivingHealth(8);
        Check(QuestSession.Current.TurnIn(quest.id,quest.turnInMerchantId),"Actual quest turn-in commits");yield return Frames(2);
        Check(events==before+1 && actor.Health.CurrentHp==actor.Health.MaxHp && notification.IsVisible,"Quest reward emits level UI and full HP once");
        Check(!QuestSession.Current.TurnIn(quest.id,quest.turnInMerchantId) && events==before+1,"Duplicate quest cannot replay or pay");
        var mapItem=MapGenerationPolicy.CreateItem(Resources.Load<MapItemData>("Items/Maps/Map_Diamond01"),registry,1,ItemGrade.Common);
        Check(account.ExecuteState("level-up-prepare-card",s=>{
            AccountRunCommands.PrepareEntry(s,"level-up-card-run",mapItem.mapState,null,true);AccountRunCommands.Activate(s,"level-up-card-run");
        }),"Actual account run command prepares the card fixture");
        before=events;actor.Health.RestoreLivingHealth(8);
        Check(new AccountRunSession(account).ClaimEventCard("level-up-card-run","level-up-card",progression.ExperienceToNext-progression.Experience),"Actual XP card claim commits");yield return Frames(2);
        Check(events==before+1 && actor.Health.CurrentHp==actor.Health.MaxHp && notification.IsVisible,"Card reward emits level UI and full HP once");
        bool rejected=false;try { rejected=!new AccountRunSession(account).ClaimEventCard("level-up-card-run","level-up-card",10000); }catch(InvalidOperationException) { rejected=true; }
        Check(rejected && events==before+1,"Duplicate card does not replay");
        var store=new EasySaveAccountStore(Path.Combine(output,"Account"));var saved=store.Load();Check(saved.level==progression.Level,"Committed rewarded level is durable on isolated disk");
        int level=progression.Level;before=events;Call(progression,"ApplyAccountProgression",level,progression.Experience,false);Check(events==before,"Restoring committed level stays silent");
        yield return Capture("reward-runtime");
    }

    static void Finish(Exception error)
    {
        EditorApplication.update-=Tick;EditorApplication.update-=Begin;
        foreach(var task in tasks) (task as IDisposable)?.Dispose();tasks.Clear();SessionState.EraseString(K+"pending");
        if(progression!=null)progression.LeveledUp-=Leveled;Time.timeScale=1;
        if(fixture!=null)Object.DestroyImmediate(fixture);fixture=null;
        foreach(var asset in temporaryAssets)if(asset!=null)Object.DestroyImmediate(asset);temporaryAssets.Clear();
        output=SessionState.GetString(K+"return",output);Write("play-results.json",new{status=error==null?"PASS":"FAIL",scope="ISOLATED_PRODUCT_AND_PHYSICS_FIXTURE",count=checks.Count,checks,error=error?.ToString()});
        if(EditorApplication.isPlaying && OwnPlay) EditorApplication.ExitPlaymode();
        EditorApplication.update-=Return;EditorApplication.update+=Return;
    }
    static void State(PlayModeStateChange state)
    {
        if(state==PlayModeStateChange.EnteredEditMode && SessionState.GetString(K+"return","")!="")
        {
            if(tasks.Count>0) Finish(new Exception("Play was interrupted before verification completed"));
            EditorApplication.update-=Return;EditorApplication.update+=Return;
        }
    }
    static void Return()
    {
        output=SessionState.GetString(K+"return","");if(output==""){EditorApplication.update-=Return;return;}
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||IsolatedSavePlayGuard.ActiveDirectory!="")return;
        string env=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)??"";
        if(env.Length>0 && env!=Path.Combine(output,"Account")){Write("return-results.json",new{status="BLOCKED_FOREIGN_ACCOUNT"});EditorApplication.update-=Return;return;}
        if(!File.Exists(Path.Combine(output,"play-results.json")))
            Write("play-results.json",new{status="FAIL_INTERRUPTED",scope="INCOMPLETE_PLAY",reason="Play or domain reload interrupted the owned verifier before it wrote its result"});
        string scene=SessionState.GetString(K+"start","");EditorSceneManager.playModeStartScene=scene==""?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(scene);
        if(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY")==Path.Combine(output,"Settings"))Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY",null);
        Application.runInBackground=SessionState.GetBool(K+"background",false);IsolatedSavePlayGuard.UseRealAccount();
        Write("return-results.json",new{status="PASS",playing=EditorApplication.isPlaying,transitioning=EditorApplication.isPlayingOrWillChangePlaymode,
            guardReady=!IsolatedSavePlayGuard.RequiresAccountChoice,environment=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),
            scenes=Scenes(),start=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),input=UnityEngine.InputSystem.InputSystem.settings.GetInstanceID()});
        foreach(var key in new[]{"return","start","pending"})SessionState.EraseString(K+key);SessionState.EraseFloat(K+"deadline");SessionState.EraseBool(K+"background");EditorApplication.update-=Return;
    }
}
