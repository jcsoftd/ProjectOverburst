using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class PlayerScreenSpaceVfxVerifier
{
    const string K = "Overburst.PlayerScreenSpaceVfxVerifier.";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
    static readonly List<string> checks = new List<string>();
    static readonly Stack<IEnumerator> tasks = new Stack<IEnumerator>();
    static string output;
    static double deadline;
    static int frame = -1;
    static GameObject fixture;
    static PlayerScreenSpaceVfx screen;
    static CombatHealth health;
    static PlayerScreenSpaceVfxVerifier()
    {
        EditorApplication.playModeStateChanged += State;
        if (SessionState.GetString(K + "pending", "") != "") EditorApplication.update += Begin;
        if (SessionState.GetString(K + "return", "") != "") EditorApplication.update += Return;
    }
    static void Write(string name, object value) => File.WriteAllText(Path.Combine(output, name), JsonConvert.SerializeObject(value, Formatting.Indented));
    static void Check(bool pass, string name)
    { if (!pass) throw new InvalidOperationException(name); checks.Add(name); Write("progress.json", new {count=checks.Count,last=name}); }
    static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private).GetValue(obj);
    static object Scenes() => Enumerable.Range(0,SceneManager.sceneCount).Select(i=>{var s=SceneManager.GetSceneAt(i);
        return new {s.path,s.isDirty,roots=s.GetRootGameObjects().Select(g=>g.GetInstanceID()).OrderBy(x=>x).ToArray()};}).ToArray();
    public static string Native(string directory)
    {
        AppearanceCustomizationBuilder.RequireIdle();output=Path.GetFullPath(directory);Directory.CreateDirectory(output);checks.Clear();
        var prefab=Resources.Load<GameObject>(PlayerScreenSpaceVfx.ResourcePath);
        Check(prefab!=null && prefab.GetComponent<PlayerScreenSpaceVfx>()!=null,"Runtime resource resolves presenter");
        Check(prefab.GetComponentsInChildren<Transform>(true).All(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0),"No missing scripts");
        Check(prefab.GetComponentsInChildren<MonoBehaviour>(true).Length==1 && prefab.GetComponentsInChildren<UnityEngine.Animations.ParentConstraint>(true).Length==0,"No supplier callbacks or camera constraints");
        var effects=prefab.GetComponentsInChildren<ParticleSystem>(true);Check(effects.Length==3,"Exactly three approved effects; no 4-8 connection");
        foreach(var effect in effects)
        {
            Check(effect.main.maxParticles==1 && !effect.main.loop && !effect.main.playOnAwake && effect.main.useUnscaledTime,"Single owned particle "+effect.name);
            var material=effect.GetComponent<ParticleSystemRenderer>().sharedMaterial;
            Check(material!=null && material.shader.isSupported && !ShaderUtil.ShaderHasError(material.shader),"Supported original material "+effect.name);
            var source=AssetDatabase.LoadAssetAtPath<GameObject>(PlayerScreenSpaceVfxBuilder.SourceRoot+effect.name+".prefab").GetComponentsInChildren<ParticleSystem>(true).Single(p=>p.emission.enabled);
            Check(material==source.GetComponent<ParticleSystemRenderer>().sharedMaterial,"Approved source texture/shader retained "+effect.name);
        }
        var view=prefab.GetComponent<PlayerScreenSpaceVfx>();
        foreach(var name in new[]{"damage","healing","lowHealth"})Check(Field<ParticleSystem>(view,name)!=null,"Serialized effect resolves "+name);
        Check(Mathf.Abs(Field<ParticleSystem>(view,"damage").main.startLifetime.constant-.38f)<.001f,"Damage lifetime .38 seconds");
        Check(float.IsPositiveInfinity(Field<ParticleSystem>(view,"lowHealth").main.startLifetime.constant),"Low HP retains border until explicit stop");
        Check(Mathf.Abs(PlayerScreenSpaceVfx.LowHealthEnter-.30f)<.001f && Mathf.Abs(PlayerScreenSpaceVfx.LowHealthExit-.33f)<.001f,"Low-HP hysteresis is 30/33 percent");
        var preview=EditorSceneManager.NewPreviewScene();GameObject root=null;
        try
        {
            root=new GameObject("Owned heal event fixture");SceneManager.MoveGameObjectToScene(root,preview);
            var hp=root.AddComponent<CombatHealth>();hp.ResetHealth();hp.RestoreLivingHealth(90);
            int count=0;float actual=0;hp.OnHealed+=(target,amount)=>{count++;actual=amount;};
            hp.Heal(20);Check(count==1 && actual==10 && hp.CurrentHp==100,"Heal event uses capped actual HP gain");
            hp.Heal(20);hp.Heal(0);hp.Heal(-5);Check(count==1,"Full HP and nonpositive heal stay silent");
            hp.RestoreLivingHealth(25);hp.ResetHealth();hp.SetUnmodifiedMaxHp(120,true);Check(count==1,"Restore/reset/max-HP refill emit no heal event");
            hp.RestoreLivingHealth(60);hp.Heal(5,false);Check(count==1 && hp.CurrentHp==65,"Stat growth preserves healing without feedback");
            hp.SetRunMapModifiers(1,.5f);hp.Heal(10);Check(count==2 && actual==5,"Heal event reflects run healing penalty once");
        }
        finally {if(root!=null)Object.DestroyImmediate(root);EditorSceneManager.ClosePreviewScene(preview);}
        Write("native-results.json",new{status="PASS",count=checks.Count,checks});return "PASS "+checks.Count;
    }
    public static void Start(string directory)
    {
        AppearanceCustomizationBuilder.RequireIdle();
        if(IsolatedSavePlayGuard.ActiveDirectory!="" || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY")))throw new InvalidOperationException("Another account/settings session owns Editor");
        output=Path.GetFullPath(directory);if(Directory.Exists(output))throw new InvalidOperationException("Use a new owned Play folder");Directory.CreateDirectory(output);
        new EasySaveAccountStore(Path.Combine(output,"Account")).Save(NewAccountFactory.Create(Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath)),"screen-vfx-fixture");
        SessionState.SetString(K+"pending",output);SessionState.SetString(K+"return",output);
        SessionState.SetString(K+"start",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));SessionState.SetBool(K+"background",Application.runInBackground);
        SessionState.SetFloat(K+"deadline",(float)EditorApplication.timeSinceStartup+240);
        SessionState.SetFloat(K+"returnDeadline",(float)EditorApplication.timeSinceStartup+480);
        Write("edit-before.json",new{scenes=Scenes(),start=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),input=UnityEngine.InputSystem.InputSystem.settings.GetInstanceID()});
        Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY",Path.Combine(output,"Settings"));
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        EditorApplication.update-=Begin;EditorApplication.update+=Begin;
        try{IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output,"Account"));}catch(Exception error){Finish(error);throw;}
    }
    static bool OwnPlay => IsolatedSavePlayGuard.ActiveDirectory==Path.Combine(SessionState.GetString(K+"return",""),"Account");
    static void Begin()
    {
        output=SessionState.GetString(K+"pending","");if(output==""){EditorApplication.update-=Begin;return;}
        if(EditorApplication.timeSinceStartup>SessionState.GetFloat(K+"deadline",0)){Finish(new Exception("Boot timeout"));return;}
        if(!EditorApplication.isPlaying || !OwnPlay || TitleCharacterPanel.Instance==null)return;
        EditorApplication.update-=Begin;SessionState.EraseString(K+"pending");checks.Clear();Application.runInBackground=true;
        deadline=EditorApplication.timeSinceStartup+180;tasks.Push(Verify());frame=-1;EditorApplication.update+=Tick;
    }
    static void Tick()
    {
        if(Time.frameCount==frame && EditorApplication.timeSinceStartup<deadline)return;frame=Time.frameCount;EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if(!EditorApplication.isPlaying || !OwnPlay || EditorApplication.timeSinceStartup>deadline)throw new Exception("Owned Play timeout/exit");
            while(tasks.Count>0){var task=tasks.Peek();if(!task.MoveNext()){(tasks.Pop() as IDisposable)?.Dispose();continue;}
                if(task.Current is IEnumerator nested){tasks.Push(nested);continue;}return;}Finish(null);
        }catch(Exception error){Finish(error);}
    }
    static IEnumerator Frames(int n){while(n-->0)yield return null;}
    static IEnumerator Seconds(float duration){float end=Time.unscaledTime+duration;while(Time.unscaledTime<end)yield return null;}
    static IEnumerator Wait(Func<bool> condition,string name){float end=Time.unscaledTime+75;while(!condition() && Time.unscaledTime<end)yield return null;Check(condition(),name);}
    static IEnumerator Capture(string name){ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));yield return Frames(4);}
    static bool Hidden => !screen.DamageVisible && !screen.HealingVisible && !screen.LowHealthVisible;
    static IEnumerator Verify()
    {
        yield return Frames(6);TitleCharacterPanel.Instance.start.onClick.Invoke();yield return Frames(3);TitleCharacterPanel.Instance.enter.onClick.Invoke();
        yield return Wait(()=>AccountBootstrap.Ready && WorldSessionState.IsHideout && !PersistentSceneFlow.Instance.IsSwitching,"Real title-to-town isolated boot");yield return Frames(12);
        screen=Object.FindFirstObjectByType<PlayerScreenSpaceVfx>();health=PlayerContext.Instance.CurrentActorHealth;
        Check(screen!=null && health!=null && screen.OutputCamera!=null && PlayerScreenSpaceVfx.Handles(health),"Product HUD creates and binds presenter to actual output camera");
        var damageUi=Object.FindFirstObjectByType<PlayerDamageFeedback>();var healUi=Object.FindFirstObjectByType<PlayerHealFeedback>();
        Check(screen.OutputCamera==QuarterViewCamera.ActiveInstance.CinemachineRig.OutputCamera,"Cinemachine output camera used");
        Check(screen.GetComponentsInChildren<ParticleSystem>(true).Length==3,"Only fixed three particles allocated");
        health.ResetHealth();yield return Frames(2);Check(Hidden,"Full HP/reset starts with clear screen");yield return Capture("00-baseline");
        health.TakeDamage(new DamageInfo(health.MaxHp*.05f,health.transform.position));yield return Seconds(.09f);
        Check(screen.DamageVisible && !screen.LowHealthVisible && !screen.HealingVisible,"Actual direct damage starts Blood Quick exclusively");
        Check(damageUi.GetComponent<CanvasGroup>().alpha==0,"Legacy damage UI suppressed while native effect handles damage");yield return Capture("01-damage");
        yield return Seconds(.45f);Check(Hidden && Field<ParticleSystem>(screen,"damage").particleCount==0,"Damage expires and clears particle");
        health.TakeDamage(new DamageInfo(health.MaxHp*.01f,health.transform.position,isDamageOverTime:true,triggersOnHitEffects:false));yield return Frames(2);Check(!screen.DamageVisible,"DoT produces no direct-hit screen flash");
        health.TakeDamage(new DamageInfo(0,health.transform.position));yield return Frames(2);Check(Hidden,"Zero damage produces no effect");
        health.SetDamageImmunity(screen,true);health.TakeDamage(new DamageInfo(10,health.transform.position));health.SetDamageImmunity(screen,false);yield return Frames(2);Check(Hidden,"Cancelled/immune damage produces no effect");
        health.RestoreLivingHealth(health.MaxHp*.5f);yield return Frames(2);Check(!screen.HealingVisible,"Health restore remains silent");
        PlayerHealFeedback.ApplyHeal(health,health.MaxHp*.1f);yield return Frames(2);Check(screen.HealingVisible && !screen.DamageVisible,"Actual item-style healing starts Healing Infinity");
        Check(healUi.GetComponent<CanvasGroup>().alpha==0,"Heal numbers preserved and legacy UI does not stack");yield return Capture("02-healing");
        var greenParticle=new ParticleSystem.Particle[1];Field<ParticleSystem>(screen,"healing").GetParticles(greenParticle);Color green=greenParticle[0].startColor;
        Check(green.g>green.r*3f && green.g>green.b*2f,"Actual healing particle carries green tint");
        yield return Seconds(.85f);Check(Hidden && Field<ParticleSystem>(screen,"healing").particleCount==0,"Infinite heal source explicitly stopped after .75 seconds");
        health.RestoreLivingHealth(health.MaxHp*.30f);yield return Frames(2);Check(screen.LowHealthVisible,"Low HP starts at exactly 30 percent");
        yield return Seconds(.65f);Check(screen.LowHealthVisible && Field<ParticleSystem>(screen,"lowHealth").particleCount==1,"Low HP survives source's original .5-second lifetime");
        var ui=Object.FindFirstObjectByType<OverburstGameUI>();
        Check(ui.hud.Find("Action Bar Unit Frame/Bar (Health)/Text Group/Percentage Text").GetComponent<UnityEngine.UI.Text>().text==$"{health.CurrentHp:N0} / {health.MaxHp:N0}","Actual low-HP HUD label refreshed before evidence capture");yield return Capture("03-low-health");
        health.RestoreLivingHealth(health.MaxHp*.32f);yield return Frames(2);Check(screen.LowHealthVisible,"32 percent retains border without threshold flicker");
        health.RestoreLivingHealth(health.MaxHp*.34f);yield return Frames(2);Check(Hidden,"Above 33 percent clears low HP");
        health.RestoreLivingHealth(health.MaxHp*.28f);health.Heal(health.MaxHp*.01f);yield return Frames(2);Check(screen.HealingVisible && !screen.LowHealthVisible,"Heal owns frame instead of blending green/red");
        yield return Seconds(.85f);Check(screen.LowHealthVisible,"Low HP returns when healed HP remains below threshold");
        health.ResetHealth();yield return Frames(2);health.RestoreLivingHealth(health.MaxHp*.5f);
        for(int i=0;i<12;i++){health.Heal(health.MaxHp*.0001f);yield return Frames(1);}
        Check(screen.HealingVisible && Field<ParticleSystem>(screen,"healing").particleCount==1,"Continuous flask/lifesteal-sized heals reuse one mild particle");
        yield return Seconds(.85f);Check(Hidden,"Small healing clears when regeneration stops");
        var flasks=PlayerFlaskController.Current;var regeneration=Resources.Load<FlaskItemData>("Items/Flasks/Flask_Regeneration");
        Check(flasks!=null && flasks.Effects.Add("screen-vfx-regeneration",regeneration,new FlaskStats(.05f,0f,.3f,0f),Time.time),"Actual flask controller receives bounded regeneration fixture");
        yield return Seconds(.15f);Check(screen.HealingVisible && Field<ParticleSystem>(screen,"healing").particleCount==1,"Actual flask tick starts mild reusable healing");
        yield return Seconds(1.1f);Check(flasks.Effects.Count==0 && Hidden,"Actual flask expiration stops healing screen");
        health.Heal(health.MaxHp);yield return Seconds(.85f);health.Heal(10);yield return Frames(2);Check(Hidden,"Full-HP heal remains silent in product");
        for(int i=0;i<8;i++){health.TakeDamage(new DamageInfo(health.MaxHp*.005f,health.transform.position));yield return Frames(1);}
        Check(Field<ParticleSystem>(screen,"damage").particleCount==1 && screen.GetComponentsInChildren<ParticleSystem>(true).Length==3,"Rapid damage reuses one fixed instance");
        OverburstTimeEffectArbiter.SetPaused(true);float stamp=Field<float>(screen,"damageStart");yield return Seconds(.5f);
        Check(screen.DamageVisible && Mathf.Abs(OverburstGameClock.UnscaledTime-stamp)<.38f,"Menu pause preserves remaining flash time");
        OverburstTimeEffectArbiter.SetPaused(false);Time.timeScale=.05f;yield return Seconds(.45f);Check(!screen.DamageVisible,"Hit-stop/slow motion does not stretch screen flash");Time.timeScale=1;
        health.RestoreLivingHealth(health.MaxHp*.2f);yield return Frames(2);OverburstGameSettings.HitEffectScale=0;yield return Frames(2);Check(Hidden,"Zero hit-effect setting suppresses damage and low HP");OverburstGameSettings.HitEffectScale=1;
        fixture=new GameObject("Owned nonplayer heal fixture");var other=fixture.AddComponent<CombatHealth>();other.RestoreLivingHealth(25);other.Heal(20);yield return Frames(2);Check(!screen.HealingVisible,"Enemy/unbound heal does not flash current player screen");Object.Destroy(fixture);fixture=null;
        var actor=PlayerContext.Instance.CurrentActor;PlayerContext.Instance.Bind(null);yield return Frames(2);
        // CurrentActor intentionally resolves actor zero again; disabling the presenter must still clear its owned effects.
        screen.enabled=false;Check(Hidden && screen.GetComponentsInChildren<ParticleSystem>(true).All(p=>p.particleCount==0),"Disable clears all effects and unsubscribes");screen.enabled=true;PlayerContext.Instance.Bind(actor);yield return Frames(2);
        WorldPhase phase=WorldSessionState.Phase;typeof(WorldSessionState).GetMethod("SetPhase",Private).Invoke(null,new object[]{WorldPhase.Loading});yield return Frames(2);Check(Hidden,"Loading transition clears all effects");
        typeof(WorldSessionState).GetMethod("SetPhase",Private).Invoke(null,new object[]{phase});health.ResetHealth();yield return Frames(2);
        Check(PersistentSceneFlow.Instance.TryReturnToTitle(out string titleError),"Actual town-to-title transition accepted "+titleError);
        yield return Wait(()=>TitleCharacterPanel.Instance!=null && TitleCharacterPanel.Instance.start.gameObject.activeInHierarchy && WorldSessionState.Phase==WorldPhase.Booting,"Actual title reached after content unload");
        Check(Hidden,"Title transition leaves all three effects clear");
        TitleCharacterPanel.Instance.start.onClick.Invoke();yield return Frames(3);TitleCharacterPanel.Instance.enter.onClick.Invoke();
        yield return Wait(()=>AccountBootstrap.Ready && WorldSessionState.IsHideout && !PersistentSceneFlow.Instance.IsSwitching,"Actual title-to-town reentry completed");yield return Frames(12);
        screen=Object.FindFirstObjectByType<PlayerScreenSpaceVfx>();health=PlayerContext.Instance.CurrentActorHealth;
        Check(Object.FindObjectsByType<PlayerScreenSpaceVfx>(FindObjectsSortMode.None).Length==1 && PlayerScreenSpaceVfx.Handles(health) && Hidden,"Reentry binds new actor/camera with one presenter and no stale effects");
        health.TakeDamage(new DamageInfo(health.MaxHp*10,health.transform.position));yield return Frames(2);Check(health.IsDead && Hidden,"Lethal damage/death clears screen for death UI");yield return Capture("04-death-clear");
    }
    static void Finish(Exception error)
    {
        EditorApplication.update-=Tick;EditorApplication.update-=Begin;foreach(var task in tasks)(task as IDisposable)?.Dispose();tasks.Clear();SessionState.EraseString(K+"pending");
        OverburstTimeEffectArbiter.SetPaused(false);Time.timeScale=1;if(fixture!=null)Object.DestroyImmediate(fixture);fixture=null;
        output=SessionState.GetString(K+"return",output);Write("play-results.json",new{status=error==null?"PASS":"FAIL",scope="ISOLATED_PRODUCT_PLUS_TRANSITION_FIXTURE",count=checks.Count,checks,error=error?.ToString()});
        if(EditorApplication.isPlaying && OwnPlay)EditorApplication.ExitPlaymode();EditorApplication.update-=Return;EditorApplication.update+=Return;
    }
    static void State(PlayModeStateChange state)
    {
        if(state==PlayModeStateChange.EnteredEditMode && SessionState.GetString(K+"return","")!="")
        {if(tasks.Count>0)Finish(new Exception("Play interrupted"));EditorApplication.update-=Return;EditorApplication.update+=Return;}
    }
    static void Return()
    {
        output=SessionState.GetString(K+"return","");if(output==""){EditorApplication.update-=Return;return;}
        if(EditorApplication.timeSinceStartup>SessionState.GetFloat(K+"returnDeadline",0)){Write("return-results.json",new{status="FAIL_RETURN_TIMEOUT"});EditorApplication.update-=Return;return;}
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || IsolatedSavePlayGuard.ActiveDirectory!="")return;
        string env=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)??"";
        if(env.Length>0 && env!=Path.Combine(output,"Account")){Write("return-results.json",new{status="BLOCKED_FOREIGN_ACCOUNT"});EditorApplication.update-=Return;return;}
        if(!File.Exists(Path.Combine(output,"play-results.json")))Write("play-results.json",new{status="FAIL_INTERRUPTED"});
        string scene=SessionState.GetString(K+"start","");EditorSceneManager.playModeStartScene=scene==""?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(scene);
        if(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY")==Path.Combine(output,"Settings"))Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY",null);
        Application.runInBackground=SessionState.GetBool(K+"background",false);IsolatedSavePlayGuard.UseRealAccount();
        Write("return-results.json",new{status="PASS",playing=EditorApplication.isPlaying,transitioning=EditorApplication.isPlayingOrWillChangePlaymode,
            guardReady=!IsolatedSavePlayGuard.RequiresAccountChoice,environment=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),active=IsolatedSavePlayGuard.ActiveDirectory,
            scenes=Scenes(),start=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),input=UnityEngine.InputSystem.InputSystem.settings.GetInstanceID()});
        foreach(var key in new[]{"return","start","pending"})SessionState.EraseString(K+key);SessionState.EraseFloat(K+"deadline");SessionState.EraseFloat(K+"returnDeadline");SessionState.EraseBool(K+"background");EditorApplication.update-=Return;
    }
}
