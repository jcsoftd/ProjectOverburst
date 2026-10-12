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
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

[InitializeOnLoad]
public static class WorldLevelPlayVerifier
{
    const string K="Overburst.WorldLevelPlayVerifier.";
    static string output;
    static readonly List<string> checks=new List<string>();
    static readonly Stack<IEnumerator> work=new Stack<IEnumerator>();
    static readonly Dictionary<InputActionAsset,InputDevice[]> masks=new Dictionary<InputActionAsset,InputDevice[]>();
    static InputSettings originalInput,temporaryInput;
    static Mouse mouse;static Keyboard keyboard;static MouseState pointer;
    static int lastFrame=-1;static double deadline;
    static WorldLevelPlayVerifier()
    {
        EditorApplication.playModeStateChanged+=State;
        if(SessionState.GetString(K+"pending","")!="")EditorApplication.update+=Begin;
        if(SessionState.GetString(K+"return","")!="")EditorApplication.update+=Return;
    }
    static void Write(string name,object value)=>File.WriteAllText(Path.Combine(output,name),JsonConvert.SerializeObject(value,Formatting.Indented));
    static void Check(bool value,string label){if(!value)throw new InvalidOperationException(label);checks.Add(label);Write("progress.json",new{count=checks.Count,last=label,utc=DateTime.UtcNow});}
    public static void Start(string directory,bool replay=false,string fixture=null,bool graveHuntProgression=false)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||IsolatedSavePlayGuard.ActiveDirectory!=""
            ||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY")))throw new InvalidOperationException("Require idle real-account EditMode");
        output=Path.GetFullPath(directory);Directory.CreateDirectory(output);string save=Path.Combine(output,"isolated-save");Directory.CreateDirectory(save);
        if(fixture!=null){foreach(var f in new[]{"account-A.es3","account-B.es3"})if(File.Exists(Path.Combine(fixture,f)))File.Copy(Path.Combine(fixture,f),Path.Combine(save,f),false);}
        else
        {
            var registry=Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);var seed=NewAccountFactory.Create(registry);
            seed.level=graveHuntProgression?100:35;seed.skillTree=AccountSkillTree.GrantEligible(seed.skillTree,seed.level);AccountCharacterCommands.EnsureLegacy(seed);
            new EasySaveAccountStore(save).Save(seed,"world-level-fixture");
        }
        SessionState.SetString(K+"pending",output);SessionState.SetString(K+"return",output);SessionState.SetBool(K+"replay",replay);
        SessionState.SetBool(K+"graveHuntProgression",graveHuntProgression);
        SessionState.SetString(K+"startScene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));SessionState.SetBool(K+"background",Application.runInBackground);
        SessionState.SetFloat(K+"deadline",(float)EditorApplication.timeSinceStartup+300);
        Write("edit-before.json",new{scenes=Scenes(),input=InputSystem.settings.GetInstanceID(),previews=EditorSceneManager.previewSceneCount,startScene=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)});
        Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY",Path.Combine(output,"isolated-settings"));
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        EditorApplication.update-=Begin;EditorApplication.update+=Begin;
        IsolatedSavePlayGuard.EnterIsolatedPlay(save);
    }
    static object Scenes()=>Enumerable.Range(0,SceneManager.sceneCount).Select(i=>{var s=SceneManager.GetSceneAt(i);return new{s.path,s.isDirty,roots=s.GetRootGameObjects().Length};}).ToArray();
    static bool OwnPlay()=>IsolatedSavePlayGuard.ActiveDirectory==Path.Combine(SessionState.GetString(K+"return",""),"isolated-save");
    static void Begin()
    {
        output=SessionState.GetString(K+"pending","");if(output==""){EditorApplication.update-=Begin;return;}
        if(EditorApplication.timeSinceStartup>SessionState.GetFloat(K+"deadline",0)){Finish(new Exception("Boot timeout"));return;}
        if(!EditorApplication.isPlaying||!OwnPlay()||!TitleCharacterPanel.Instance||PlayerInputFacade.Current==null)return;
        EditorApplication.update-=Begin;SessionState.EraseString(K+"pending");checks.Clear();Application.runInBackground=true;
        if(SessionState.GetBool(K+"graveHuntProgression",false))
        {
            EditorApplication.LockReloadAssemblies();SessionState.SetBool(K+"reloadLocked",true);
            AssetDatabase.DisallowAutoRefresh();SessionState.SetBool(K+"refreshLocked",true);
        }
        deadline=EditorApplication.timeSinceStartup+300;work.Push(Verify());lastFrame=-1;EditorApplication.update+=Tick;
    }
    static void Tick()
    {
        if(Time.frameCount==lastFrame&&EditorApplication.timeSinceStartup<deadline)return;lastFrame=Time.frameCount;EditorApplication.QueuePlayerLoopUpdate();
        try
        {
            if(!EditorApplication.isPlaying||!OwnPlay()||EditorApplication.timeSinceStartup>deadline)throw new Exception("Owned Play ended or timeout");
            while(work.Count>0){var task=work.Peek();if(!task.MoveNext()){(work.Pop() as IDisposable)?.Dispose();continue;}if(task.Current is IEnumerator nested){work.Push(nested);continue;}return;}
            Finish(null);
        }
        catch(Exception error){Finish(error);}
    }
    static IEnumerator Frames(int count){for(int i=0;i<count;i++)yield return null;}
    static IEnumerator Wait(Func<bool> condition,string label,int frames=2400){while(!condition()&&frames-->0)yield return null;Check(condition(),label);}
    static T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner);
    static void SetupInput()
    {
        originalInput=InputSystem.settings;temporaryInput=Object.Instantiate(originalInput);temporaryInput.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        SessionState.SetInt(K+"input",originalInput.GetInstanceID());SessionState.SetInt(K+"temporaryInput",temporaryInput.GetInstanceID());
        temporaryInput.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;InputSystem.settings=temporaryInput;
        mouse=InputSystem.AddDevice<Mouse>("WorldLevelVerifierMouse");keyboard=InputSystem.AddDevice<Keyboard>("WorldLevelVerifierKeyboard");
        SessionState.SetInt(K+"mouse",mouse.deviceId);SessionState.SetInt(K+"keyboard",keyboard.deviceId);
        var module=EventSystem.current.GetComponent<InputSystemUIInputModule>();
        foreach(var asset in new[]{PlayerInputFacade.Current.RuntimeAsset,module.actionsAsset}.Where(a=>a).Distinct())
        {masks[asset]=asset.devices.HasValue?asset.devices.Value.ToArray():null;asset.devices=new InputDevice[]{mouse,keyboard};}
        SessionState.SetString(K+"masks",JsonConvert.SerializeObject(masks.Select(p=>new{asset=p.Key.GetInstanceID(),devices=p.Value?.Select(d=>d.deviceId).ToArray()})));
        pointer=new MouseState{position=new Vector2(40,50)};InputSystem.onBeforeUpdate+=Push;
    }
    static void Push(){if(mouse!=null&&mouse.added)InputSystem.QueueStateEvent(mouse,pointer);}
    static IEnumerator KeyPress(Key key){InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));yield return Frames(2);InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return Frames(4);}
    static IEnumerator Click(Selectable button,float x=.5f,float y=.5f)
    {
        Canvas.ForceUpdateCanvases();var rect=(RectTransform)button.transform;var position=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(new Vector3(Mathf.Lerp(rect.rect.xMin,rect.rect.xMax,x),Mathf.Lerp(rect.rect.yMin,rect.rect.yMax,y))));
        var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=position},hits);
        Write("last-raycast.json",new{button=button.name,position=new{x=position.x,y=position.y},hits=hits.Select(h=>new{h.gameObject.name,h.sortingOrder,h.depth}).ToArray()});
        Check(hits.Count>0&&hits[0].gameObject.GetComponentInParent<Selectable>()==button,"pointer reaches "+button.name);
        pointer=new MouseState{position=position};yield return Frames(2);pointer.buttons=1;yield return Frames(2);pointer.buttons=0;yield return Frames(4);
    }
    static IEnumerator Capture(string name){ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));yield return Frames(4);}
    static IEnumerator Verify()
    {
        yield return Frames(8);SetupInput();yield return Frames(4);
        yield return Click(TitleCharacterPanel.Instance.start);yield return Frames(4);yield return Click(TitleCharacterPanel.Instance.enter);
        yield return Wait(()=>AccountBootstrap.Ready&&WorldSessionState.IsHideout&&!PersistentSceneFlow.Instance.IsSwitching,"title boots isolated MainScene");yield return Frames(15);
        var actor=PlayerContext.Instance.CurrentActor;actor.Health.SetDamageDeathPrevention(actor,true);
        if(SessionState.GetBool(K+"graveHuntProgression",false))
        { yield return GraveHuntWorldLevelVerifier.VerifyPlay(output,Check);yield break; }
        var npc=Object.FindFirstObjectByType<WorldLevelNpcInteractable>();var outdoor=OutdoorWorldSpawner.Current;var account=AccountGameplaySession.Current;
        Check(npc&&outdoor,"real scene contains Rowen and outdoor controller");Check(outdoor.ActiveCount==0&&outdoor.PendingCount==0,"town boot does not prepopulate outdoors");
        ActorTeleportUtility.TeleportSafely(actor.transform,npc.transform.position+Vector3.right*1.4f,Quaternion.LookRotation(Vector3.left));yield return Frames(12);
        Check(actor.GetComponent<PlayerInteractionController>().Current==npc,"live resolver selects Rowen");yield return Capture("rowen-world-model");
        yield return KeyPress(Key.F);var ui=WorldLevelUI.Current;Check(ui&&ui.IsOpen&&ui.Owner==npc,"F opens real native world-level UI");
        Check(GameplayInputBlocker.IsGameplayInputBlocked,"world-level window blocks combat input");yield return Capture("ui-before-unlock");
        if(SessionState.GetBool(K+"replay",false))
        {
            Check(account.Read().unlockedWorldLevel==4&&account.Read().appliedWorldLevel==4,"reboot restores character world-level4/4");
            Check(account.Read().items.Any(i=>i.map!=null&&i.originRunId==null),"reboot retains acquired outdoor map");
            var buttons=ui.GetComponentsInChildren<Button>(true);
            yield return Click(buttons.First(b=>b.name=="leave-button"));Check(!ui.IsOpen&&!GameplayInputBlocker.IsGameplayInputBlocked,"leave button closes and releases input");
            yield return KeyPress(Key.F);yield return Click(buttons.First(b=>b.name=="close-window"));Check(!ui.IsOpen&&!GameplayInputBlocker.IsGameplayInputBlocked,"window X closes and releases input");
            yield return KeyPress(Key.F);yield return Click(Field<Button[]>(ui,"stages")[1]);yield return Click(Field<Button>(ui,"apply"));
            pointer=new MouseState{position=new Vector2(Screen.width*.5f,Screen.height*.57f)};yield return Frames(2);pointer.buttons=1;yield return Frames(2);pointer.buttons=0;yield return Frames(4);
            Check(ui.ConfirmationIsOpen,"clicking inside confirmation background keeps modal open");
            yield return Click(buttons.First(b=>b.name=="close-confirm"));Check(ui.IsOpen&&!ui.ConfirmationIsOpen&&account.AppliedWorldLevel==4,"confirmation X cancels without changing saved stage");
            yield return Click(Field<Button>(ui,"apply"));yield return Click(buttons.First(b=>b.name=="confirmation-scrim"),.05f,.95f);
            Check(ui.IsOpen&&!ui.ConfirmationIsOpen&&account.AppliedWorldLevel==4,"outside scrim cancels without changing saved stage");
            yield return Capture("ui-reboot-final");
            yield return KeyPress(Key.Escape);Check(!ui.IsOpen&&!GameplayInputBlocker.IsGameplayInputBlocked,"reboot window closes and releases input");yield break;
        }
        yield return Click(Field<Button>(ui,"unlock"));Check(account.Read().unlockedWorldLevel==4&&account.Read().appliedWorldLevel==1,"actual unlock click saves4 while retaining applied1");
        var disk=new EasySaveAccountStore(Path.Combine(output,"isolated-save")).Load();Check(disk.unlockedWorldLevel==4&&disk.appliedWorldLevel==1,"unlock click durable on disk");
        var stageButtons=Field<Button[]>(ui,"stages");
        for(int i=0;i<10;i++){yield return Click(stageButtons[i]);Check(ui.SelectedStage==i+1,"all ten rows preview stage "+(i+1));}
        yield return Click(stageButtons[3]);yield return Click(Field<Button>(ui,"apply"));Check(ui.ConfirmationIsOpen,"apply opens confirmation without changing account");
        Check(account.AppliedWorldLevel==1,"confirmation alone does not apply");yield return Capture("ui-confirmation");
        yield return KeyPress(Key.Escape);Check(ui.IsOpen&&!ui.ConfirmationIsOpen&&account.AppliedWorldLevel==1,"Esc cancels only confirmation");
        yield return Click(Field<Button>(ui,"apply"));yield return Click(Field<Button>(ui,"confirm"));
        Check(account.AppliedWorldLevel==4&&!ui.ConfirmationIsOpen,"actual confirmation applies stage4");
        Check(new EasySaveAccountStore(Path.Combine(output,"isolated-save")).Load().appliedWorldLevel==4,"apply click durable on disk");yield return Capture("ui-applied");
        yield return KeyPress(Key.F);Check(!ui.IsOpen&&!GameplayInputBlocker.IsGameplayInputBlocked,"F closes UI and releases input");
        yield return KeyPress(Key.F);Check(ui.IsOpen,"F can reopen same NPC");
        ActorTeleportUtility.TeleportSafely(actor.transform,npc.transform.position+Vector3.forward*10,actor.transform.rotation);yield return Frames(8);
        Check(!ui.IsOpen&&!GameplayInputBlocker.IsGameplayInputBlocked,"distance closes and releases window");
        var terrain=Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).First(t=>t.gameObject.scene.name==PersistentSceneFlow.MainSceneName);
        var position=new Vector3(154,0,76);position.y=terrain.SampleHeight(position)+terrain.transform.position.y;
        ActorTeleportUtility.TeleportSafely(actor.transform,position,Quaternion.identity);yield return Frames(30);
        yield return Wait(()=>outdoor.ActiveCount>0,"entered outdoor area gradually spawns actual GraveHunt enemies",1800);
        var enemies=Object.FindObjectsByType<EnemyActor>(FindObjectsSortMode.None).Where(e=>e.GetComponent<EnemyRank>().Encounter.IsOutdoor).ToArray();
        Check(enemies.Length>0&&enemies.All(e=>e.GetComponent<EnemyRank>().Level>=31&&e.GetComponent<EnemyRank>().Level<=40),"spawned enemies use applied31-40 range");
        Check(outdoor.ActiveCount<=150&&outdoor.VisitedCount>0,"outdoor active budget and visited-area tracking");
        yield return Capture("outdoor-spawns");var oldContext=enemies[0].GetComponent<EnemyRank>().Encounter;
        var existingPickups=Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).Select(p=>p.GetInstanceID()).ToHashSet();
        foreach(var enemy in enemies.Take(3)){enemy.GetComponent<CombatHealth>().TakeDamage(new DamageInfo(1000000,enemy.transform.position,actor.gameObject,suppressDefaultHitVfx:true));yield return Frames(3);}
        var drops=Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).Where(p=>!existingPickups.Contains(p.GetInstanceID())).ToArray();
        Check(drops.All(p=>p.Grade<=ItemGrade.Legendary&&!(p.RuntimeItem.baseData is CurrencyItemData)),"outdoor death drops respect grade ceiling and contain no currency");
        var definition=Resources.Load<MapItemData>("Items/Maps/Map_Diamond01");var registry=Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        ItemData map=null;for(int i=0;i<300&&map==null;i++)map=MapDropPolicy.Roll(definition,registry,35,EnemyThemeTier.Elite,null,outdoor:true);
        Check(map!=null,"actual outdoor map roll is available without run");
        var liveEnemy=enemies.First(e=>e&&e.Health&&!e.Health.IsDead);
        typeof(EnemyLootDropper).GetMethod("DropFarmingItem",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(liveEnemy.GetComponent<EnemyLootDropper>(),new object[]{map,actor.transform.position+Vector3.right});yield return Frames(4);
        var pickup=Object.FindObjectsByType<WorldItemPickup>(FindObjectsSortMode.None).FirstOrDefault(p=>p.RuntimeItem?.runtimeInstanceId==map.runtimeInstanceId);
        Check(pickup&&pickup.RuntimeItem.originRunId==null&&pickup.RuntimeItem.mapState.grade<=ItemGrade.Legendary,"native drop factory creates valid outdoor map pickup");
        Check(pickup.TryPickup(PlayerAccountInventoryService.FindSharedInventory()),"outdoor map pickup commits to real inventory projection");yield return Frames(3);
        Check(account.Read().items.Any(i=>i.instanceId==map.runtimeInstanceId),"acquired outdoor map is in authoritative account");
        Check(account.ReadRun()==null||!AccountInvariants.IsRunning(account.ReadRun().phase),"outdoor combat does not create a dungeon run");
        ActorTeleportUtility.TeleportSafely(actor.transform,npc.transform.position+Vector3.right*1.4f,Quaternion.identity);yield return Frames(12);
        Check(outdoor.ActiveCount==0&&outdoor.PendingCount==0&&outdoor.VisitedCount==0,"town return resets owned enemies pending queue and visited areas");
        Check(!oldContext.CanGrantRewards,"old outdoor generation cannot grant rewards after reset");
        Check(account.FlushPendingSave(),"outdoor progression checkpoint flushes");
        yield return KeyPress(Key.F);Check(ui.IsOpen,"NPC remains usable after outdoor return");
        yield return Click(stageButtons[1]);yield return Click(Field<Button>(ui,"apply"));yield return Click(Field<Button>(ui,"cancel"));
        Check(account.AppliedWorldLevel==4&&!ui.ConfirmationIsOpen,"cancel button preserves applied stage4");
        yield return Click(Field<Button>(ui,"apply"));
        var store=Field<EasySaveAccountStore>(Field<AccountTransactions>(account,"transactions"),"store");
        var snapshotBeforeFailure=ES3.Serialize(account.Read());
        try
        {
            store.FaultInjector=p=>{if(p=="before-write")throw new IOException("isolated UI checkpoint failure");};
            yield return Click(Field<Button>(ui,"confirm"));
            Check(account.AppliedWorldLevel==4&&snapshotBeforeFailure.SequenceEqual(ES3.Serialize(account.Read()))&&!ui.ConfirmationIsOpen,"failed UI checkpoint leaves full account unchanged and closes confirmation");
            Check(new EasySaveAccountStore(Path.Combine(output,"isolated-save")).Load().appliedWorldLevel==4,"failed UI checkpoint retains disk applied4");
        }
        finally{store.FaultInjector=null;}
        yield return Click(Field<Button>(ui,"apply"));account.ExecuteState("worldlevel-stale-confirmation",s=>{});yield return Frames(2);
        yield return Click(Field<Button>(ui,"confirm"));Check(account.AppliedWorldLevel==4&&!ui.ConfirmationIsOpen,"stale real confirmation rejected without applying selected2");
        actor.Health.SetDamageDeathPrevention(actor,false);var lootBefore=ES3.Serialize(account.Read().items);
        actor.Health.TakeDamage(new DamageInfo(1000000,actor.transform.position,null,suppressDefaultHitVfx:true));yield return Frames(8);
        Check(!ui.IsOpen&&!GameplayInputBlocker.IsGameplayInputBlocked,"death closes world-level UI and releases input");
        Check(lootBefore.SequenceEqual(ES3.Serialize(account.Read().items)),"non-run death retains acquired inventory snapshot");
    }
    static void RestoreInput()
    {
        if(masks.Count==0&&SessionState.GetString(K+"masks","")!="")
            foreach(var row in Newtonsoft.Json.Linq.JArray.Parse(SessionState.GetString(K+"masks","")))
            {
                var asset=EditorUtility.InstanceIDToObject((int)row["asset"]) as InputActionAsset;
                if(asset)masks[asset]=row["devices"].Type==Newtonsoft.Json.Linq.JTokenType.Null?null:row["devices"].Select(id=>InputSystem.GetDeviceById((int)id)).Where(d=>d!=null).ToArray();
            }
        if(!temporaryInput)temporaryInput=EditorUtility.InstanceIDToObject(SessionState.GetInt(K+"temporaryInput",0)) as InputSettings;
        if(!originalInput&&temporaryInput)originalInput=EditorUtility.InstanceIDToObject(SessionState.GetInt(K+"input",0)) as InputSettings;
        if(mouse==null){var device=InputSystem.GetDeviceById(SessionState.GetInt(K+"mouse",0));if(device is Mouse m&&m.name=="WorldLevelVerifierMouse")mouse=m;}
        if(keyboard==null){var device=InputSystem.GetDeviceById(SessionState.GetInt(K+"keyboard",0));if(device is Keyboard k&&k.name=="WorldLevelVerifierKeyboard")keyboard=k;}
        InputSystem.onBeforeUpdate-=Push;foreach(var pair in masks)if(pair.Key)pair.Key.devices=pair.Value;masks.Clear();
        if(mouse!=null&&mouse.added)InputSystem.RemoveDevice(mouse);if(keyboard!=null&&keyboard.added)InputSystem.RemoveDevice(keyboard);mouse=null;keyboard=null;
        if(temporaryInput&&originalInput&&InputSystem.settings==temporaryInput)
        {
            InputSystem.settings=originalInput;
            var type=typeof(InputSettings).Assembly.GetType("UnityEngine.InputSystem.InputSystemObject");
            if(type!=null)foreach(var state in Resources.FindObjectsOfTypeAll(type))(state as ISerializationCallbackReceiver)?.OnBeforeSerialize();
        }
        if(temporaryInput)Object.DestroyImmediate(temporaryInput);temporaryInput=null;originalInput=null;
        SessionState.EraseString(K+"masks");foreach(var field in new[]{"input","temporaryInput","mouse","keyboard"})SessionState.EraseInt(K+field);
    }
    static void Finish(Exception error)
    {
        EditorApplication.update-=Tick;EditorApplication.update-=Begin;
        while(work.Count>0)try{(work.Pop() as IDisposable)?.Dispose();}catch(Exception cleanup){if(error==null)error=cleanup;}
        SessionState.EraseString(K+"pending");
        output=SessionState.GetString(K+"return",output);if(WorldLevelUI.Current)WorldLevelUI.Current.Close();RestoreInput();
        Write("play-result.json",new{success=error==null,count=checks.Count,checks,error=error?.ToString()});
        if(SessionState.GetBool(K+"refreshLocked",false)){AssetDatabase.AllowAutoRefresh();SessionState.EraseBool(K+"refreshLocked");}
        if(SessionState.GetBool(K+"reloadLocked",false)){EditorApplication.UnlockReloadAssemblies();SessionState.EraseBool(K+"reloadLocked");}
        if(EditorApplication.isPlaying&&OwnPlay())EditorApplication.ExitPlaymode();EditorApplication.update-=Return;EditorApplication.update+=Return;
    }
    static void State(PlayModeStateChange state){if(state==PlayModeStateChange.EnteredEditMode){EditorApplication.update-=Return;EditorApplication.update+=Return;}}
    static void Return()
    {
        output=SessionState.GetString(K+"return","");if(output==""){EditorApplication.update-=Return;return;}
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||IsolatedSavePlayGuard.ActiveDirectory!="")return;
        RestoreInput();
        string scene=SessionState.GetString(K+"startScene","");EditorSceneManager.playModeStartScene=scene==""?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(scene);
        if(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY")==Path.Combine(output,"isolated-settings"))Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY",null);
        Application.runInBackground=SessionState.GetBool(K+"background",false);IsolatedSavePlayGuard.UseRealAccount();
        Write("return-result.json",new{success=!IsolatedSavePlayGuard.RequiresAccountChoice&&string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)),scenes=Scenes(),previews=EditorSceneManager.previewSceneCount,input=InputSystem.settings.GetInstanceID(),startScene=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)});
        SessionState.EraseString(K+"return");SessionState.EraseString(K+"startScene");SessionState.EraseFloat(K+"deadline");SessionState.EraseBool(K+"background");SessionState.EraseBool(K+"replay");SessionState.EraseBool(K+"graveHuntProgression");EditorApplication.update-=Return;
    }
}
