using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Overburst.Appearance;
using Overburst.Persistence;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Uses the existing one-Play account guard and product title/scene-flow paths.
[InitializeOnLoad]
public static class PlayerDeathVerifier
{
    const string Key = "Overburst.PlayerDeathVerifier.";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> checks = new List<string>();
    static readonly Stack<IEnumerator> tasks = new Stack<IEnumerator>();
    static string output;
    static int lastFrame = -1;
    static Gamepad pad;
    static EasySaveAccountStore store;
    static RunLifetimeDriver driver;
    static PlayerActorRuntime actor;
    static double deadline;
    static PlayerDeathVerifier()
    {
        EditorApplication.playModeStateChanged += State;
        if (SessionState.GetString(Key+"pending", "") != "") EditorApplication.update += Begin;
        if (SessionState.GetString(Key+"return", "") != "") EditorApplication.update += Return;
    }
    static string Hash(string path) { using(var sha=System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
    static void Write(string name, object value) => File.WriteAllText(Path.Combine(output, name), JsonConvert.SerializeObject(value, Formatting.Indented));
    static void Check(bool pass, string name) { if(!pass) throw new InvalidOperationException(name); checks.Add(name); Write("progress.json",new {count=checks.Count,last=name}); }
    static T Field<T>(object value,string name) => (T)value.GetType().GetField(name,Private).GetValue(value);
    static void Set(object value,string name,object next) => value.GetType().GetField(name,Private).SetValue(value,next);
    static void Phase(WorldPhase phase) => typeof(WorldSessionState).GetMethod("SetPhase",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{phase});
    static object Scenes() => Enumerable.Range(0,SceneManager.sceneCount).Select(i=>new {SceneManager.GetSceneAt(i).path,SceneManager.GetSceneAt(i).isDirty,roots=SceneManager.GetSceneAt(i).GetRootGameObjects().Select(g=>g.GetInstanceID()).OrderBy(x=>x).ToArray()}).ToArray();
    public static object Native(string directory)
    {
        AppearanceCustomizationBuilder.RequireIdle(); output=Path.GetFullPath(directory); Directory.CreateDirectory(output); checks.Clear();
        var p=Resources.Load<OverburstDeathScreen>(OverburstDeathScreen.ResourcePath);
        Check(p && p.gameObject.activeSelf,"Death resource resolves and root is active");
        Check(p.GetComponentsInChildren<Transform>(true).All(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0),"No missing script");
        Check(p.canvas && p.worldBackdrop && p.returnButton && p.sound && p.lowBoom150 && p.headingGroup && p.detailsGroup && p.actionGroup && p.location && p.message && p.loss && p.keep && p.status,"All UI/audio serialized references resolve");
        var title=p.GetComponentsInChildren<TMP_Text>(true).Single(t=>t.text=="DEFEATED");
        Check(title.fontSize==112 && title.font.faceInfo.familyName.Contains("Noto Serif KR"),"Approved title and serif family preserved");
        Check(p.lowBoom150.frequency==48000 && p.lowBoom150.channels==2 && Math.Abs(p.lowBoom150.length-2.674104f)<.001f,"Selected audio remains 48 kHz stereo at original length");
        Check(Hash(PlayerDeathUIBuilder.Root+"/LowBoom_150.wav")=="65fa7ea19377a0678dbe2f81adb5552fb964fcf7532ecb7ccb99869318c76d86","Approved two 75 percent layers retained byte-for-byte");
        var importer=(AudioImporter)AssetImporter.GetAtPath(PlayerDeathUIBuilder.Root+"/LowBoom_150.wav");
        Check(!importer.forceToMono && importer.defaultSampleSettings.compressionFormat==AudioCompressionFormat.PCM,"Import preserves stereo and boosted waveform");
        var result=new {status="PASS",scope="NATIVE",count=checks.Count,checks=checks.ToArray()};Write("native-results.json",result);return result;
    }
    public static void Start(string directory)
    {
        AppearanceCustomizationBuilder.RequireIdle();
        if(IsolatedSavePlayGuard.ActiveDirectory!="" || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY")) || SessionState.GetString("Overburst.UIPrefabPlayBoot.account", "")!="" || SessionState.GetString("Overburst.VisualPlay.Session.plan", "")!="") throw new InvalidOperationException("Another Editor session is prepared");
        output=Path.GetFullPath(directory); if(Directory.Exists(output)) throw new InvalidOperationException("Fresh Play evidence folder required");
        Directory.CreateDirectory(output);
        var real=Path.Combine(Application.persistentDataPath,"Account");
        Write("before.json",new {pid=System.Diagnostics.Process.GetCurrentProcess().Id,scenes=Scenes(),start=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),input=InputSystem.settings.GetInstanceID(),background=Application.runInBackground,account=real,files=Directory.Exists(real)?Directory.GetFiles(real,"*",SearchOption.AllDirectories).OrderBy(p=>p).Select(p=>new{path=p,hash=Hash(p)}).ToArray():null});
        var registry=Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var catalog=Resources.Load<CharacterAppearanceCatalog>(CharacterAppearanceCatalog.ResourcePath);
        var seed=AccountCharacterCommands.First(Guid.NewGuid().ToString("N"),"사망검증기사","knight",catalog.defaultAppearance,registry);
        new EasySaveAccountStore(Path.Combine(output,"Account")).Save(seed,"death-fixture");
        SessionState.SetString(Key+"pending",output);SessionState.SetString(Key+"return",output);
        SessionState.SetFloat(Key+"deadline",(float)EditorApplication.timeSinceStartup+300);
        Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY",Path.Combine(output,"Settings"));
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        EditorApplication.update-=Begin;EditorApplication.update+=Begin;
        try { IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(output,"Account")); }
        catch(Exception error) { Finish(error);throw; }
    }
    static bool OwnPlay => IsolatedSavePlayGuard.ActiveDirectory==Path.Combine(SessionState.GetString(Key+"return", ""),"Account");
    static void Begin()
    {
        output=SessionState.GetString(Key+"pending", "");if(output==""){EditorApplication.update-=Begin;return;}
        if(EditorApplication.timeSinceStartup>SessionState.GetFloat(Key+"deadline",0)){Finish(new TimeoutException("Title boot"));return;}
        if(!EditorApplication.isPlaying || !OwnPlay || !TitleCharacterPanel.Instance)return;
        EditorApplication.update-=Begin;SessionState.EraseString(Key+"pending");checks.Clear();Application.runInBackground=true;
        deadline=EditorApplication.timeSinceStartup+300;tasks.Push(Verify());lastFrame=-1;EditorApplication.update+=Tick;
    }
    static void Tick()
    {
        if(Time.frameCount==lastFrame && EditorApplication.timeSinceStartup<deadline)return;
        lastFrame=Time.frameCount;EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if(!EditorApplication.isPlaying || !OwnPlay || EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Owned Play ended or timed out");
            while(tasks.Count>0){var task=tasks.Peek();if(!task.MoveNext()){(tasks.Pop() as IDisposable)?.Dispose();continue;}if(task.Current is IEnumerator next){tasks.Push(next);continue;}return;}
            Finish(null);
        }
        catch(Exception error){Finish(error);}
    }
    static IEnumerator Frames(int n){while(n-->0)yield return null;}
    static IEnumerator Seconds(float seconds){float end=Time.unscaledTime+seconds;while(Time.unscaledTime<end)yield return null;}
    static IEnumerator Wait(Func<bool> condition,string name,float seconds=90){float end=Time.unscaledTime+seconds;while(!condition()&&Time.unscaledTime<end)yield return null;Check(condition(),name);}
    static IEnumerator Capture(string name){ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));yield return Frames(4);}
    static void Kill(){actor.Health.TakeDamage(new DamageInfo(100000000,actor.transform.position,triggersOnHitEffects:false,suppressDefaultHitVfx:true));Check(actor.Health.IsDead,"Actual lethal damage sets death");}
    static IEnumerator DeathReady(string name)
    {
        yield return Wait(()=>driver.DeathScreen && driver.DeathScreen.IsOpen,"Death presentation opens",10);
        yield return Wait(()=>driver.DeathScreen.returnButton.interactable && OverburstTimeEffectArbiter.IsPaused,"Unscaled presentation reaches manual return and freezes world",10);
        Check(driver.DeathScreen.worldBackdrop.enabled && driver.DeathScreen.worldBackdrop.texture,"Frozen game-camera backdrop captured");
        Check(GameplayInputBlocker.IsGameplayInputBlocked && Cursor.visible && Cursor.lockState==CursorLockMode.None,"Combat input blocked and UI cursor available");
        yield return Capture(name);
    }
    static IEnumerator ReturnTown(bool submit=false)
    {
        if(submit)ExecuteEvents.Execute(driver.DeathScreen.returnButton.gameObject,new BaseEventData(EventSystem.current),ExecuteEvents.submitHandler);
        else driver.DeathScreen.returnButton.onClick.Invoke();
        driver.RequestDeathReturn(); // duplicate confirm must remain harmless
        yield return Wait(()=>WorldSessionState.IsHideout && !PersistentSceneFlow.Instance.IsSwitching && !driver.DeathScreen.IsOpen,"Manual return completes through product scene flow");
        actor=PlayerContext.Instance.CurrentActor;
        Check(!actor.Health.IsDead && actor.Health.CurrentHp==actor.Health.MaxHp,"Town return restores full HP and living state");
        Check(!OverburstTimeEffectArbiter.IsPaused && !GameplayInputBlocker.IsGameplayInputBlocked,"Pause and input ownership released");
        Check(!driver.DeathScreen.worldBackdrop.texture && !driver.DeathScreen.sound.isPlaying,"Snapshot and death voice released");
        Check(Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Any(c=>c.isActiveAndEnabled && !c.transform.IsChildOf(driver.DeathScreen.transform)),"Ordinary HUD canvas restored");
        yield return Frames(5);
    }
    static IEnumerator Verify()
    {
        yield return Frames(5);TitleCharacterPanel.Instance.start.onClick.Invoke();yield return Frames(3);TitleCharacterPanel.Instance.enter.onClick.Invoke();
        yield return Wait(()=>AccountBootstrap.Ready && WorldSessionState.IsHideout && !PersistentSceneFlow.Instance.IsSwitching,"Real title-to-town boot with isolated account");yield return Frames(12);
        driver=Object.FindFirstObjectByType<RunLifetimeDriver>();actor=PlayerContext.Instance.CurrentActor;
        var account=AccountGameplaySession.Current;store=Field<EasySaveAccountStore>(Field<AccountTransactions>(account,"transactions"),"store");
        Check(driver && actor,"Persistent driver and current player available");
        var initial=account.Read();long outdoorRevision=account.Revision;
        var ui=Object.FindFirstObjectByType<OverburstGameUI>();ui.inventory.SetVisible(true);ui.ToggleEquipment();
        pad=InputSystem.AddDevice<Gamepad>("DeathVerifierPad");InputSystem.QueueStateEvent(pad,new GamepadState().WithButton(GamepadButton.South));yield return Frames(3);
        Check(PlayerInputFacade.Current.UiSubmitHeld,"Held gamepad submit reaches product input");Kill();yield return Seconds(2.2f);
        Check(driver.DeathScreen.IsOpen && !driver.DeathScreen.returnButton.interactable && !PersistentSceneFlow.Instance.IsSwitching,"Held pre-death submit cannot auto-return");
        InputSystem.QueueStateEvent(pad,new GamepadState());yield return Frames(3);yield return DeathReady("outdoor-death");
        Write("outdoor-account-diff.json",new {before=initial,after=account.Read(),outdoorRevision,revision=account.Revision,settled=driver.DeathSettled,error=driver.LastError});
        var outdoorAfter=account.Read();var expected=JObject.FromObject(initial);var actual=JObject.FromObject(outdoorAfter);
        // Fresh-town NPC registration may add merchants and commit a new revision while the death unfolds.
        foreach(var metadata in new[]{"revision","lastTransactionId","merchants"}){expected.Remove(metadata);actual.Remove(metadata);}
        Check(driver.DeathSettled && JToken.DeepEquals(expected,actual) && initial.merchants.All(m=>outdoorAfter.merchants.Any(n=>JToken.DeepEquals(JToken.FromObject(m),JToken.FromObject(n)))),"Outdoor death preserves possessions, XP and existing merchant state");
        int sound=driver.DeathScreen.SoundPlayCount;Kill();driver.Poll(DateTime.UtcNow.Ticks);yield return Frames(3);
        Check(driver.DeathScreen.SoundPlayCount==sound && Object.FindObjectsByType<OverburstDeathScreen>(FindObjectsSortMode.None).Length==1,"Repeated lethal notifications keep one screen and one sound");
        InputSystem.QueueStateEvent(pad,new GamepadState().WithButton(GamepadButton.East));yield return Frames(3);
        Check(!OverburstGameMenu.IsOpen && driver.DeathScreen.IsOpen,"UI cancel cannot dismiss death or open pause menu");InputSystem.QueueStateEvent(pad,new GamepadState());
        yield return ReturnTown(true);yield return Capture("outdoor-return");
        yield return MapCase(false);yield return MapCase(true);
        yield return Capture("final-town-return");
        var position=actor.transform.position;
        Write("runtime-scope.json",new {screen=new[]{Screen.width,Screen.height},scene=WorldSessionState.ContentScene.name,voices=driver.DeathScreen.SoundPlayCount,player=new[]{position.x,position.y,position.z},scope="Product title, real map scene entry, lethal damage, settlement fault injection, manual UI return; no Player build"});
    }
    static IEnumerator MapCase(bool completed)
    {
        var account=AccountGameplaySession.Current;var registry=Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        ItemData map=null;
        for(int attempt=0;attempt<64 && (map==null || map.mapState.terrain!=MapTerrainKind.Cave);attempt++)
            map=MapGenerationPolicy.CreateItem(Resources.Load<MapItemData>("Items/Maps/Map_Diamond01"),registry,1,ItemGrade.Common);
        Check(map.mapState.terrain==MapTerrainKind.Cave,"Known current cave scene selected; unrelated stale desert minimap excluded");
        Check(PersistentSceneFlow.Instance.EnterDebugRun(MapTerrainCatalog.SceneNameFor(map.mapState),map.mapState),"Actual map scene entry accepted");
        yield return Wait(()=>!PersistentSceneFlow.Instance.IsSwitching && WorldSessionState.Phase==WorldPhase.Run,"Actual map world ready");yield return Frames(8);actor=PlayerContext.Instance.CurrentActor;
        var run=account.ReadRun();var id=run.runId;var before=account.Read();
        var template=before.items.First(x=>x.map==null && x.count>0);var lost=ItemSnapshotCodec.CopyValues(template);lost.instanceId=Guid.NewGuid().ToString("N");lost.acquisitionOrder=0;
        var sent=ItemSnapshotCodec.CopyValues(lost);sent.instanceId=Guid.NewGuid().ToString("N");
        Check(account.ExecuteState("death-loot-"+id,s=>{AccountRunCommands.Acquire(s,id,lost);AccountRunCommands.Acquire(s,id,sent);AccountRunCommands.Transfer(s,id,"death-transfer",sent.instanceId,registry);}),"Real acquire and stash-transfer transactions prepared");
        var preserved=account.Read();int level=preserved.level,xp=preserved.experience;long revision=account.Revision;
        if(completed)
        {
            var reward=ItemSnapshotCodec.Capture(map,registry);reward.instanceId=AccountRunCommands.CompletionRewardId(id);reward.originRunId=id;
            Check(account.ExecuteState("death-complete-"+id,s=>AccountRunCommands.CompleteMap(s,id,DateTime.UtcNow.Ticks,0,actor.transform.position,reward)),"Completed farming run prepared through product completion command");
            Check(driver.RequestPortalExit(),"Successful exit requested in lethal frame");
        }
        else store.FaultInjector=stage=>{if(stage=="before-write")throw new IOException("Death verifier injected save failure");};
        int voices=driver.DeathScreen.SoundPlayCount;Kill();yield return DeathReady(completed?"completed-map-death":"map-save-error");
        if(!completed)
        {
            Check(!driver.DeathSettled && account.ReadRun().phase==RunPhase.Active && driver.DeathScreen.buttonLabel.text=="정산 다시 시도","Failed durable save keeps the run and exposes retry");
            driver.DeathScreen.returnButton.onClick.Invoke();Check(!PersistentSceneFlow.Instance.IsSwitching,"Save retry failure cannot transition to town");
            store.FaultInjector=null;driver.DeathScreen.returnButton.onClick.Invoke();yield return Frames(3);
            Check(driver.DeathSettled && !PersistentSceneFlow.Instance.IsSwitching,"Successful save retry needs a separate return confirmation");
        }
        var settled=account.Read();
        Check(settled.run.phase==RunPhase.Failed && !settled.items.Any(x=>x.instanceId==lost.instanceId) && settled.items.Any(x=>x.instanceId==sent.instanceId),"Death discards only unextracted loot and preserves transferred loot");
        Check(before.items.All(x=>settled.items.Any(y=>y.instanceId==x.instanceId)) && settled.level==level && settled.experience==xp,"Existing possessions and confirmed XP preserved");
        var disk=new EasySaveAccountStore(Path.Combine(output,"Account")).Load();Check(disk.run.phase==RunPhase.Failed && !disk.items.Any(x=>x.instanceId==lost.instanceId),"Failed run settlement is durable on isolated disk");
        if(!completed)
        {
            Set(PersistentSceneFlow.Instance,"isSwitching",true);try{driver.RequestDeathReturn();}finally{Set(PersistentSceneFlow.Instance,"isSwitching",false);}
            Check(driver.DeathScreen.buttonLabel.text=="마을 이동 다시 시도" && driver.DeathScreen.IsOpen,"Busy scene flow retains death screen with return retry");
            yield return Capture("map-return-retry");
        }
        Check(driver.DeathScreen.SoundPlayCount==voices+1,"One selected Low Boom per death including retries");
        yield return ReturnTown();
    }
    static void Finish(Exception error)
    {
        EditorApplication.update-=Tick;EditorApplication.update-=Begin;SessionState.EraseString(Key+"pending");
        foreach(var task in tasks)(task as IDisposable)?.Dispose();tasks.Clear();if(store!=null)store.FaultInjector=null;
        if(pad!=null){InputSystem.RemoveDevice(pad);pad=null;}
        output=SessionState.GetString(Key+"return",output);Write("play-results.json",new{status=error==null?"PASS":"FAIL",count=checks.Count,checks=checks.ToArray(),error=error?.ToString(),phase=WorldSessionState.Phase.ToString(),scene=WorldSessionState.ContentScene.name,entryError=PersistentSceneFlow.Instance?.RunEntryError});
        // Every success/failure ends the owned Play immediately; restoration waits for actual EditMode.
        if(EditorApplication.isPlaying && OwnPlay)EditorApplication.ExitPlaymode();
        EditorApplication.update-=Return;EditorApplication.update+=Return;
    }
    static void State(PlayModeStateChange state)
    {if(state==PlayModeStateChange.EnteredEditMode && SessionState.GetString(Key+"return", "")!=""){EditorApplication.update-=Return;EditorApplication.update+=Return;}}
    static void Return()
    {
        output=SessionState.GetString(Key+"return", "");if(output==""){EditorApplication.update-=Return;return;}
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || IsolatedSavePlayGuard.ActiveDirectory!="")return;
        var before=JObject.Parse(File.ReadAllText(Path.Combine(output,"before.json")));string env=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)??"";
        if(env.Length>0 && env!=Path.Combine(output,"Account")){Write("return-results.json",new{status="BLOCKED_FOREIGN_ACCOUNT"});EditorApplication.update-=Return;return;}
        string start=(string)before["start"];EditorSceneManager.playModeStartScene=start==""?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(start);
        if(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY")==Path.Combine(output,"Settings"))Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY",null);
        Application.runInBackground=(bool)before["background"];IsolatedSavePlayGuard.UseRealAccount();
        var files=before["files"] as JArray;bool account=files!=null?files.All(f=>File.Exists((string)f["path"]) && Hash((string)f["path"])==(string)f["hash"]) && Directory.GetFiles((string)before["account"],"*",SearchOption.AllDirectories).Length==files.Count:!Directory.Exists((string)before["account"]);
        bool scenes=JToken.DeepEquals(before["scenes"],JToken.FromObject(Scenes())),input=(int)before["input"]==InputSystem.settings.GetInstanceID();
        Write("return-results.json",new {status=account&&scenes&&input?"PASS":"FAIL",realAccountUnchanged=account,scenes,input,playing=EditorApplication.isPlaying,transitioning=EditorApplication.isPlayingOrWillChangePlaymode,guardReady=!IsolatedSavePlayGuard.RequiresAccountChoice,environment=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable),start=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared", ""),expires=SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires", "")});
        SessionState.EraseString(Key+"return");SessionState.EraseFloat(Key+"deadline");EditorApplication.update-=Return;
    }
}
