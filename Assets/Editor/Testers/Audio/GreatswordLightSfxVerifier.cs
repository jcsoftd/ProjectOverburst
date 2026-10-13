#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using Overburst.Persistence;
using Overburst.DebugTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

[InitializeOnLoad]
public static class GreatswordLightSfxVerifier
{
 const string ComboPath="Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/Common/Combos/GreatswordComboSet01.asset";
 const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Set(MeleeRuntime actor,string name,object value)=>typeof(MeleeRuntime).GetField(name,Flags).SetValue(actor,value);
 static T Get<T>(MeleeRuntime actor,string name)=>(T)typeof(MeleeRuntime).GetField(name,Flags).GetValue(actor);
 static object Call(MeleeRuntime actor,string name,params object[] values)=>typeof(MeleeRuntime).GetMethod(name,Flags).Invoke(actor,values);
 static List<string> native;
 static void N(bool pass,string message){if(!pass)throw new Exception(message);native.Add(message);}
 public static string Native(string directory)
 {
  AppearanceCustomizationBuilder.RequireIdle();native=new List<string>();var combo=AssetDatabase.LoadAssetAtPath<MeleeComboDefinition>(ComboPath);string before=EditorJsonUtility.ToJson(combo);
  var scene=EditorSceneManager.NewPreviewScene();var go=new GameObject("Owned swing cue probe");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go,scene);var actor=go.AddComponent<MeleeRuntime>();float scaleBefore=Time.timeScale;
  try
  {
   int[] counts={1,1,2,3};var clips=new HashSet<AudioClip>();
   for(int i=0;i<4;i++)
   {
    var step=combo.steps[i];N(step.TryValidateSwingSfx(out _),"Valid authored cue "+i);N(step.swingSfxCues.Length==counts[i],"Cue count "+i);
    foreach(var cue in step.swingSfxCues){N(clips.Add(cue.clip),"Unique derivative "+cue.clip.name);N(cue.peakSeconds<=.065f,"Short measured rise "+cue.clip.name);N(cue.alignmentProgress==.25f,"VFX alignment "+cue.clip.name);}
    foreach(float entry in new[]{0f,step.continuationStartNormalizedTime})foreach(float speed in new[]{1f,1.2f,1.5f})foreach(float scale in new[]{1f,.65f,.2f})
    {
     Time.timeScale=scale;Set(actor,"activeAttackStep",step);Set(actor,"activeAttackPhases",step.attackPhases);Set(actor,"activeAttackUsesCombo",true);Set(actor,"activeAttackIsHeavy",false);Set(actor,"isAttacking",true);float duration=step.animationClip.length/(combo.baseAnimationSpeed*step.animationSpeedMultiplier*speed);Set(actor,"attackDuration",duration);
     N((bool)Call(actor,"BeginSwingSfx",entry),"Opt in "+i+"/"+entry+"/"+speed+"/"+scale);
     N(Mathf.Abs(Get<float>(actor,"swingVoiceAt")-OverburstGameClock.UnscaledTime-.03f)<.005f,"30ms voice preparation "+i);
     for(int p=0;p<step.swingSfxCues.Length;p++)
     {
      var phase=step.attackPhases[p];var cue=step.swingSfxCues[p];float target=Mathf.Lerp(phase.SafeStart,phase.SafeEnd,cue.alignmentProgress);float elapsed=step.playbackAcceleration.ToElapsed(target)-cue.peakSeconds*scale/duration;
      Call(actor,"TickSwingSfx",step.playbackAcceleration.ToClipProgress(elapsed-.00001f));N((Get<int>(actor,"handledSwingSfx")&(1<<p))==0,"Not early "+i+"/"+p+"/"+speed+"/"+scale);
      Call(actor,"TickSwingSfx",step.playbackAcceleration.ToClipProgress(elapsed+.00001f));N((Get<int>(actor,"handledSwingSfx")&(1<<p))!=0,"Actual Tick reached "+i+"/"+p+"/"+speed+"/"+scale);
      int mask=Get<int>(actor,"handledSwingSfx");Call(actor,"TickSwingSfx",step.playbackAcceleration.ToClipProgress(elapsed+.00002f));N(Get<int>(actor,"handledSwingSfx")==mask,"No duplicate/retry "+i+"/"+p);
     }
     Call(actor,"ResetSwingSfx");N(!Get<bool>(actor,"usePhaseSwingSfx")&&Get<int>(actor,"handledSwingSfx")==0&&Get<float>(actor,"swingVoiceAt")<0,"Cancel/reset "+i);
    }
    Time.timeScale=0;Call(actor,"BeginSwingSfx",0f);Call(actor,"TickSwingSfx",.8f);N(Get<int>(actor,"handledSwingSfx")==0,"Frozen pending "+i);Time.timeScale=1;
    Call(actor,"BeginSwingSfx",0f);Call(actor,"TickSwingSfx",1f);N(Get<int>(actor,"handledSwingSfx")==((1<<counts[i])-1),"Skipped expired windows "+i);
    var invalid=step;invalid.swingSfxCues=(MeleeSwingSfxCueData[])step.swingSfxCues.Clone();invalid.swingSfxCues[0].peakSeconds=float.NaN;N(!invalid.TryValidateSwingSfx(out _),"NaN rejected "+i);invalid.swingSfxCues[0]=step.swingSfxCues[0];invalid.swingSfxCues[0].clip=null;N(!invalid.TryValidateSwingSfx(out _),"Missing clip rejected "+i);
    Set(actor,"activeAttackStep",step);Call(actor,"BeginSwingSfx",1f);N(Get<int>(actor,"handledSwingSfx")==((1<<counts[i])-1),"Late entry does not replay "+i);
   }
   var legacy=combo.steps[0];legacy.swingSfxCues=null;Set(actor,"activeAttackStep",legacy);N(!(bool)Call(actor,"BeginSwingSfx",0f),"Legacy route retained");
   using(var session=new Overburst.EditorTools.ComboMaker.ComboMakerSession())
   {
    session.Load(combo);N(session.Validate().Count==0,"Combo Maker accepts authored cues");var snapshot=session.Capture();session.Load(combo,snapshot,snapshot);N(session.Capture()==snapshot,"Working copy/recovery preserves cues");
    var clone=ScriptableObject.CreateInstance<MeleeComboDefinition>();try{EditorUtility.CopySerialized(session.Working,clone);N(EditorJsonUtility.ToJson(clone)==snapshot,"Save copy preserves references");}finally{UnityEngine.Object.DestroyImmediate(clone);}
    var s=session.Working.steps[2];s.swingSfxCues=new[]{s.swingSfxCues[0]};session.Working.steps[2]=s;N(session.Validate().Any(e=>e.Contains("휘두름음")),"Mismatch blocks Apply");
   }
   N(EditorJsonUtility.ToJson(combo)==before,"Original combo preserved by verifier");Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"native.json"),JsonConvert.SerializeObject(new{status="PASS",count=native.Count,checks=native},Formatting.Indented));return "PASS Native "+native.Count;
  }
  finally{Time.timeScale=scaleBefore;UnityEngine.Object.DestroyImmediate(go);EditorSceneManager.ClosePreviewScene(scene);}
 }
    private const string K = "Overburst.GreatswordLightSfxVerifier.";
    private static readonly List<string> checks = new List<string>();
    private static readonly Stack<IEnumerator> tasks = new Stack<IEnumerator>();
    private static VisualPlayContext context;
    private static double deadline;
    private static int frame = -1;
    private static string output;
    static GreatswordLightSfxVerifier()
    {
        EditorApplication.playModeStateChanged += State;
        if (SessionState.GetString(K + "pending", "") != "") EditorApplication.update += Begin;
        if (SessionState.GetString(K + "return", "") != "") EditorApplication.update += Return;
    }
    private static void Check(bool pass, string message)
    {
        if (!pass) throw new InvalidOperationException(message);
        checks.Add(message);
    }
    private static object Scenes() => Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount)
        .Select(i => UnityEngine.SceneManagement.SceneManager.GetSceneAt(i)).Select(s => new { s.path, s.isDirty }).ToArray();
    private static void Write(string name, object value) => File.WriteAllText(Path.Combine(output, name), JsonConvert.SerializeObject(value, Formatting.Indented));

    private static string AccountHash()
    {
        string root=Path.Combine(Application.persistentDataPath,"Account");
        if(!Directory.Exists(root))return "MISSING";
        using(var sha=SHA256.Create())return string.Join("\n",Directory.GetFiles(root,"*",SearchOption.AllDirectories).OrderBy(p=>p,StringComparer.Ordinal).Select(p=>p+":"+BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(p)))));
    }
    public static void Start(string directory)
    {
        AppearanceCustomizationBuilder.RequireIdle();
        if (IsolatedSavePlayGuard.ActiveDirectory != "" || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY"))) throw new InvalidOperationException("Another account owns Editor");
        if(SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","")!="")throw new InvalidOperationException("Another prepared account owns Editor");
        output = Path.GetFullPath(directory);
        if (Directory.Exists(output)) throw new InvalidOperationException("Use a new owned validation directory");
        Directory.CreateDirectory(output);
        var registry = Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var seed = NewAccountFactory.Create(registry); AccountCharacterCommands.EnsureLegacy(seed);
        new EasySaveAccountStore(Path.Combine(output, "Account")).Save(seed, "light-swing-fixture");
        SessionState.SetString(K + "pending", output); SessionState.SetString(K + "return", output);
        SessionState.SetString(K + "start", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(K + "background", Application.runInBackground);
        SessionState.SetString(K+"realHash",AccountHash());
        SessionState.SetFloat(K + "deadline", (float)EditorApplication.timeSinceStartup + 180);
        Write("before.json", new { scenes = Scenes(), input = InputSystem.settings.GetInstanceID(), start = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) });
        Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY", Path.Combine(output, "Settings"));
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        EditorApplication.update -= Begin; EditorApplication.update += Begin;
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output, "Account")); }
        catch (Exception error) { Finish(error); throw; }
    }
    private static bool OwnPlay => IsolatedSavePlayGuard.ActiveDirectory == Path.Combine(SessionState.GetString(K + "return", ""), "Account");
    private static void Begin()
    {
        output = SessionState.GetString(K + "pending", "");
        if (output == "") { EditorApplication.update -= Begin; return; }
        if (EditorApplication.timeSinceStartup > SessionState.GetFloat(K + "deadline", 0)) { Finish(new TimeoutException("Boot")); return; }
        if (!EditorApplication.isPlaying || !OwnPlay || TitleCharacterPanel.Instance == null) return;
        EditorApplication.update -= Begin; SessionState.EraseString(K + "pending");
        Application.runInBackground = true; deadline = EditorApplication.timeSinceStartup + 120; checks.Clear();
        tasks.Push(Verify()); EditorApplication.update += Tick;
    }
    private static void Tick()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        if (Time.frameCount == frame && EditorApplication.timeSinceStartup < deadline) return;
        frame = Time.frameCount;
        try
        {
            if (!OwnPlay || !EditorApplication.isPlaying || EditorApplication.timeSinceStartup > deadline) throw new TimeoutException("Owned Play ended or stalled");
            while (tasks.Count > 0)
            {
                IEnumerator task = tasks.Peek();
                if (!task.MoveNext()) { (tasks.Pop() as IDisposable)?.Dispose(); continue; }
                if (task.Current is IEnumerator nested) { tasks.Push(nested); continue; }
                return;
            }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    private static IEnumerator Frames(int count) { while (count-- > 0) yield return null; }
    private static IEnumerator Wait(Func<bool> condition, string label)
    { int frames = 1800; while (!condition() && frames-- > 0) yield return null; Check(condition(), label); }


    private static HashSet<AudioClip> seen;
    private static List<object> observed;
    private static void Observe(string label)
    {
        var service=typeof(CombatActionSfxService).GetField("instance",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null) as CombatActionSfxService;
        if(service==null)return;
        var voices=(AudioSource[])typeof(CombatActionSfxService).GetField("voices",Flags).GetValue(service);
        foreach(var voice in voices)
        {
            if(voice==null||!voice.isPlaying||voice.clip==null||!voice.clip.name.StartsWith("GreatswordLight"))continue;
            Check(voice.clip.name.Contains("_Swing"),"No compound light WAV "+label);
            if(!seen.Add(voice.clip))continue;
            Check(Mathf.Approximately(voice.volume,.9f)&&Mathf.Approximately(voice.pitch,1f),"Volume/timbre preserved "+voice.clip.name);
            observed.Add(new{label,clip=voice.clip.name,frame=Time.frameCount,progress=(float)Call(context.Melee,"GetAttackNormalizedTime"),timeScale=Time.timeScale,timeSamples=voice.timeSamples});
        }
    }
    private static void NewCase(){context.ReleaseInput();context.Melee.CancelCurrentAttackState();seen=new HashSet<AudioClip>();}
    private static IEnumerator Watch(Func<bool> until,string label)
    {
        double end=EditorApplication.timeSinceStartup+15;
        while(!until()&&EditorApplication.timeSinceStartup<end){Observe(label);yield return null;}
        Observe(label);Check(until(),label);
    }
    private static void ReleaseAttack(){context.ReleaseInput();context.Actor.GetComponent<PlayerInputFacade>()?.CombatInputs?.ClearAttack();}
    private static IEnumerator FullCombo(bool late,string label)
    {
        NewCase();yield return context.Wait(.35f);context.Melee.CancelCurrentAttackState();seen.Clear();int voice=PlayerVoiceService.Current.PlayedCount;
        if(late)
        {
            for(int i=0;i<4;i++)
            {
                context.MouseButton(true,false);int step=i;
                yield return Watch(()=>context.Melee.IsAttackInProgress&&Get<int>(context.Melee,"comboStepIndex")==step,label+" entry "+i);
                ReleaseAttack();yield return Watch(()=>!context.Melee.IsAttackInProgress,label+" complete "+i);yield return context.Wait(.025f);
            }
        }
        else
        {
            context.MouseButton(true,false);yield return Watch(()=>context.Melee.IsAttackInProgress&&Get<int>(context.Melee,"comboStepIndex")==0,label+" first accepted");
            float accepted=Get<float>(context.Melee,"swingVoiceAt")-.03f;
            Check(PlayerVoiceService.Current.PlayedCount==voice,label+" voice not immediate");
            yield return Watch(()=>PlayerVoiceService.Current.PlayedCount>voice,label+" delayed voice");
            float delay=OverburstGameClock.UnscaledTime-accepted;Check(delay>=.029f && delay<.03f+Time.unscaledDeltaTime*2+.01f,label+" voice delay within a frame: "+delay);
            yield return Watch(()=>context.Melee.IsAttackInProgress&&Get<int>(context.Melee,"comboStepIndex")==3,label+" reaches fourth");
            ReleaseAttack();yield return Watch(()=>!context.Melee.IsAttackInProgress,label+" fourth complete");
        }
        Write("observations-"+label+".json",observed);Check(seen.Count==7,label+" exactly 7 swing clips: "+seen.Count+" "+string.Join(",",seen.Select(c=>c.name)));Check(PlayerVoiceService.Current.PlayedCount-voice==4,label+" exactly 4 attack voices");
        ScreenCapture.CaptureScreenshot(Path.Combine(output,label+".png"));yield return Frames(3);
    }
    private static IEnumerator Third(bool pause,string label)
    {
        NewCase();yield return context.Wait(.35f);context.Melee.CancelCurrentAttackState();seen.Clear();Set(context.Melee,"comboStepIndex",1);Set(context.Melee,"lastComboWindowTime",Time.time);int voice=PlayerVoiceService.Current.PlayedCount;
        context.MouseButton(true,false);yield return Watch(()=>seen.Count==1,label+" first sound");ReleaseAttack();
        if(pause)
        {
            var menu=OverburstGameMenu.Instance;menu.Open();yield return Frames(3);Check(Time.timeScale==0,label+" menu freezes combat");
            yield return context.Wait(.6f);Observe(label);Check(seen.Count==1,label+" no future sound while paused");menu.Close();
            yield return Watch(()=>!context.Melee.IsAttackInProgress,label+" resumes");Check(seen.Count==2,label+" second sound after resume");
        }
        else
        {
            context.Melee.CancelCurrentAttackState();yield return context.Wait(.8f);Observe(label);Check(seen.Count==1,label+" cancel suppresses second sound");Check(!Get<bool>(context.Melee,"usePhaseSwingSfx"),label+" reset pending state");
        }
        Check(PlayerVoiceService.Current.PlayedCount-voice==1,label+" one voice");
    }
    private static IEnumerator Verify()
    {
        observed=new List<object>();yield return Frames(6);TitleCharacterPanel.Instance.start.onClick.Invoke();yield return Frames(3);TitleCharacterPanel.Instance.enter.onClick.Invoke();
        yield return Wait(()=>AccountBootstrap.Ready&&WorldSessionState.IsHideout&&!PersistentSceneFlow.Instance.IsSwitching,"Isolated title-to-town");
        context=new VisualPlayContext(new VisualPlayEntry{caseId="LIGHT-SFX",label="Light swing synchronization"},new VisualPlayState());yield return context.Prepare();context.CloseWindows();context.Aim(context.Actor.transform.position+Vector3.forward*4);context.Gem(WeaponElement.None);yield return context.Wait(.3f);
        Check(PlayerVoiceService.Current!=null,"Production voice service");
        yield return FullCombo(false,"direct");yield return FullCombo(true,"late");yield return Third(false,"cancel");yield return Third(true,"pause");yield return FullCombo(false,"repeat");
        Write("observations.json",observed);ReleaseAttack();context.Melee.CancelCurrentAttackState();
    }
    private static void Finish(Exception error)
    {
        EditorApplication.update -= Begin; EditorApplication.update -= Tick;
        try { context?.Dispose(); }
        catch (Exception cleanup) { error = error == null ? cleanup : new AggregateException(error, cleanup); }
        finally
        {
            context = null; tasks.Clear(); SessionState.EraseString(K + "pending");
            SessionState.SetFloat(K+"returnDeadline",(float)EditorApplication.timeSinceStartup+90);
            SessionState.SetString(K + "result", JsonConvert.SerializeObject(new { status = error == null ? "PASS" : "FAIL", count = checks.Count, checks, error = error?.ToString() }, Formatting.Indented));
            if (EditorApplication.isPlayingOrWillChangePlaymode && OwnPlay) EditorApplication.ExitPlaymode();
            EditorApplication.update -= Return; EditorApplication.update += Return;
        }
    }
    private static void State(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode || SessionState.GetString(K + "return", "") == "") return;
        if (SessionState.GetString(K + "result", "") == "") Finish(new Exception("Owned verification interrupted"));
        EditorApplication.update -= Return; EditorApplication.update += Return;
    }
    private static void Return()
    {
        output = SessionState.GetString(K + "return", ""); if (output == "") { EditorApplication.update -= Return; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || IsolatedSavePlayGuard.ActiveDirectory != "")
        {
            if(EditorApplication.timeSinceStartup>SessionState.GetFloat(K+"returnDeadline",float.MaxValue))
            { Write("return.json",new{status="DEFERRED",reason="Editor busy beyond return deadline"});EditorApplication.update-=Return; }
            return;
        }
        string env = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable) ?? "";
        if (env.Length > 0 && env != Path.Combine(output, "Account")) { EditorApplication.update -= Return; return; }
        string start = SessionState.GetString(K + "start", "");
        EditorSceneManager.playModeStartScene = start == "" ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(start);
        if (Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY") == Path.Combine(output, "Settings")) Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY", null);
        Application.runInBackground = SessionState.GetBool(K + "background", false); IsolatedSavePlayGuard.UseRealAccount();
        File.WriteAllText(Path.Combine(output, "play.json"), SessionState.GetString(K + "result", ""));
        var before=JObject.Parse(File.ReadAllText(Path.Combine(output,"before.json")));
        bool preserved=AccountHash()==SessionState.GetString(K+"realHash","") && JsonConvert.SerializeObject(Scenes())==JsonConvert.SerializeObject(before["scenes"])
            && InputSystem.settings.GetInstanceID()==(int)before["input"];
        Write("return.json", new { status = preserved ? "PASS" : "FAIL", realAccountAndScenesInputPreserved=preserved, playing = EditorApplication.isPlaying, transitioning = EditorApplication.isPlayingOrWillChangePlaymode,
            guardReady = !IsolatedSavePlayGuard.RequiresAccountChoice, save = Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),
            scenes = Scenes(), input = InputSystem.settings.GetInstanceID(), start = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) });
        foreach (string key in new[] { "return", "start", "result", "pending", "realHash" }) SessionState.EraseString(K + key);
        SessionState.EraseFloat(K + "deadline"); SessionState.EraseFloat(K+"returnDeadline"); SessionState.EraseBool(K + "background"); EditorApplication.update -= Return;
    }
}
#endif
