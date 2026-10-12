using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Overburst.Persistence;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class OverburstHudMenuPlayVerifier
{
    const string Key="Overburst.HudMenuPlayVerifier.";
    const string Guard="Overburst.IsolatedSavePlayGuard.";
    const string Weapon="Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS024_TrainingIronGreatsword/GRS024_TrainingIronGreatsword.asset";
    static readonly List<string> checks=new List<string>();
    static IEnumerator work;
    static int lastFrame=-1;
    static double deadline;
    static string Output=>SessionState.GetString(Key+"output","");
    static bool Pending=>SessionState.GetBool(Key+"pending",false);
    public static string RunWhenIdle(string output)
    {
        if(Pending||SessionState.GetBool(Key+"waiting",false))throw new InvalidOperationException("Owned verification already pending.");
        string full=Path.GetFullPath(output);Directory.CreateDirectory(full);
        SessionState.SetString(Key+"requestedOutput",full);SessionState.SetString(Key+"waitDeadline",(EditorApplication.timeSinceStartup+300).ToString(CultureInfo.InvariantCulture));SessionState.SetBool(Key+"waiting",true);
        return "QUEUED owned verification; waits for idle Editor and returned account for at most 300 seconds.";
    }
    static OverburstHudMenuPlayVerifier(){EditorApplication.playModeStateChanged+=State;EditorApplication.update+=Update;}
    static string Scenes()=>JsonConvert.SerializeObject(Enumerable.Range(0,SceneManager.sceneCount).Select(i=>{var s=SceneManager.GetSceneAt(i);return new{s.path,s.isDirty,roots=s.GetRootGameObjects().Select(r=>r.GetInstanceID()).OrderBy(x=>x).ToArray()};}).ToArray());
    public static string Run(string output)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||Pending)throw new InvalidOperationException("Idle Editor required.");
        if(!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))||!string.IsNullOrEmpty(SessionState.GetString(Guard+"prepared","")))throw new InvalidOperationException("Another isolated account is active/prepared.");
        if(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY")))throw new InvalidOperationException("Another settings directory is active.");
        var full=Path.GetFullPath(output);IsolatedSavePlayGuard.ValidateDirectory(Path.Combine(full,"IsolatedAccount"));Directory.CreateDirectory(full);
        var registry=Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath);
        var seed=NewAccountFactory.Create(registry);AccountCharacterCommands.EnsureLegacy(seed);
        new EasySaveAccountStore(Path.Combine(full,"IsolatedAccount")).Save(seed,seed.lastTransactionId);
        SessionState.SetString(Key+"output",full);SessionState.SetString(Key+"scenes",Scenes());SessionState.SetString(Key+"startScene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(Key+"background",Application.runInBackground);SessionState.SetFloat(Key+"timeScale",Time.timeScale);
        SessionState.SetBool(Key+"pending",true);SessionState.SetBool(Key+"return",false);SessionState.SetBool(Key+"entered",false);SessionState.SetString(Key+"status","RUNNING");
        SessionState.SetString(Key+"bootDeadline",(EditorApplication.timeSinceStartup+180).ToString(CultureInfo.InvariantCulture));
        File.WriteAllText(Path.Combine(full,"play-results.json"),"{\"status\":\"RUNNING\"}");
        try{EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(full,"IsolatedAccount"));}catch{SessionState.SetBool(Key+"return",true);throw;}
        return "STARTED owned isolated HUD menu Play";
    }
    static void State(PlayModeStateChange state)
    {
        if(!Pending)return;
        if(state==PlayModeStateChange.EnteredPlayMode){SessionState.SetBool(Key+"entered",true);checks.Clear();lastFrame=-1;deadline=EditorApplication.timeSinceStartup+180;Application.runInBackground=true;work=Verify();}
        if(state==PlayModeStateChange.ExitingPlayMode){(work as IDisposable)?.Dispose();work=null;Application.runInBackground=SessionState.GetBool(Key+"background",false);SessionState.SetBool(Key+"return",true);}
        if(state==PlayModeStateChange.EnteredEditMode){SessionState.SetBool(Key+"return",true);}
    }
    static void Update()
    {
        if(SessionState.GetBool(Key+"waiting",false))
        {
            bool timedOut=double.TryParse(SessionState.GetString(Key+"waitDeadline",""),NumberStyles.Float,CultureInfo.InvariantCulture,out double until)&&EditorApplication.timeSinceStartup>until;
            if(!timedOut&&(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||IsolatedSavePlayGuard.RequiresAccountChoice||!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))||!string.IsNullOrEmpty(SessionState.GetString(Guard+"prepared",""))))return;
            string requested=SessionState.GetString(Key+"requestedOutput","");SessionState.EraseBool(Key+"waiting");SessionState.EraseString(Key+"requestedOutput");SessionState.EraseString(Key+"waitDeadline");
            try{if(timedOut)throw new TimeoutException("Shared Editor did not become idle; no Play or account was changed.");Run(requested);}
            catch(Exception e){File.WriteAllText(Path.Combine(requested,"wait-result.json"),JsonConvert.SerializeObject(new{status="DEFERRED",error=e.ToString()},Formatting.Indented));}
            return;
        }
        if(SessionState.GetBool(Key+"return",false)){ReturnAccount();return;}
        if(!Pending)return;
        if(!EditorApplication.isPlayingOrWillChangePlaymode){SessionState.SetBool(Key+"return",true);return;}
        if(!EditorApplication.isPlaying)return;
        if(work==null){
            if(!string.Equals(IsolatedSavePlayGuard.ActiveDirectory,Path.Combine(Output,"IsolatedAccount"),StringComparison.OrdinalIgnoreCase))return;
            if(SessionState.GetBool(Key+"entered",false))Finish(new InvalidOperationException("Owned HUD menu verification interrupted by script reload; normal-account return scheduled."));
            else if(double.TryParse(SessionState.GetString(Key+"bootDeadline",""),NumberStyles.Float,CultureInfo.InvariantCulture,out double bootDeadline)&&EditorApplication.timeSinceStartup>bootDeadline)Finish(new TimeoutException("Owned UI Play did not initialize within 180 seconds."));
            return;
        }
        EditorApplication.QueuePlayerLoopUpdate();if(lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
        try{if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Owned UI Play exceeded 180 seconds.");if(!work.MoveNext())Finish(null);}catch(Exception e){Finish(e);}
    }
    static void Finish(Exception error)
    {
        var completedWork = work; work = null;
        try
        {
            try { (completedWork as IDisposable)?.Dispose(); }
            catch (Exception cleanupError) { error = error == null ? cleanupError : new AggregateException(error, cleanupError); }
            SessionState.SetString(Key+"status",error==null?"PASS_SCOPED":"FAIL");
            File.WriteAllText(Path.Combine(Output,"play-results.json"),JsonConvert.SerializeObject(new{status=SessionState.GetString(Key+"status",""),checks,error=error?.ToString(),width=Screen.width,height=Screen.height,combatEffects="NOT_APPLIED_UI_ONLY",playerBuild="NOT_RUN",humanFeel="NOT_RUN"},Formatting.Indented));
        }
        catch (Exception reportError)
        {
            SessionState.SetString(Key+"status","FAIL");
            Debug.LogError("[OverburstHudMenuPlayVerifier] Verification result could not be recorded: " + reportError);
        }
        finally
        {
            SessionState.SetBool(Key+"return",true);
            string active=IsolatedSavePlayGuard.ActiveDirectory;
            if(EditorApplication.isPlaying&&string.Equals(active,Path.Combine(Output,"IsolatedAccount"),StringComparison.OrdinalIgnoreCase))EditorApplication.ExitPlaymode();
        }
    }
    static void ReturnAccount()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        var current=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)??"";var prepared=SessionState.GetString(Guard+"prepared","");
        bool Own(string p)=>string.IsNullOrEmpty(p)||p.StartsWith(Output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        if(!Own(current)||!Own(prepared)||!Own(IsolatedSavePlayGuard.ActiveDirectory))return;
        try{
            IsolatedSavePlayGuard.UseRealAccount();Application.runInBackground=SessionState.GetBool(Key+"background",false);Time.timeScale=SessionState.GetFloat(Key+"timeScale",1);
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key+"startScene",""));
            bool startScene=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)==SessionState.GetString(Key+"startScene","");
            bool scenes=Scenes()==SessionState.GetString(Key+"scenes","");
            SessionState.SetBool(Key+"pending",false);SessionState.SetBool(Key+"return",false);
            var returned=new{status=!IsolatedSavePlayGuard.RequiresAccountChoice&&startScene&&scenes?"PASS_SCOPED":"FAIL",blocked=IsolatedSavePlayGuard.RequiresAccountChoice,environment=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)??"",active=IsolatedSavePlayGuard.ActiveDirectory,prepared=SessionState.GetString(Guard+"prepared",""),expires=SessionState.GetString(Guard+"expires",""),pending=Pending,returnPending=SessionState.GetBool(Key+"return",false),startScenePreserved=startScene,userScenesPreserved=scenes,backgroundRestored=Application.runInBackground==SessionState.GetBool(Key+"background",false),timeScaleRestored=Time.timeScale==SessionState.GetFloat(Key+"timeScale",1)};
            File.WriteAllText(Path.Combine(Output,"play-return.json"),JsonConvert.SerializeObject(returned,Formatting.Indented));
            if(SessionState.GetString(Key+"status","")=="RUNNING")File.WriteAllText(Path.Combine(Output,"play-results.json"),"{\"status\":\"INTERRUPTED_OR_BOOT_CANCELLED\"}");
            foreach(string suffix in new[]{"output","scenes","startScene","status","bootDeadline"})SessionState.EraseString(Key+suffix);
            SessionState.EraseBool(Key+"pending");SessionState.EraseBool(Key+"return");SessionState.EraseBool(Key+"entered");SessionState.EraseBool(Key+"background");SessionState.EraseFloat(Key+"timeScale");
        }catch(Exception e){File.WriteAllText(Path.Combine(Output,"play-return.json"),JsonConvert.SerializeObject(new{status="FAIL",error=e.ToString()},Formatting.Indented));SessionState.SetBool(Key+"return",false);}
    }
    static void Check(bool yes,string name){if(!yes)throw new InvalidOperationException(name);checks.Add(name);}
    static Vector2 Point(RectTransform r)=>RectTransformUtility.WorldToScreenPoint(r.GetComponentInParent<Canvas>().rootCanvas.worldCamera,r.TransformPoint(r.rect.center));
    static PointerEventData Pointer(RectTransform r)=>new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,position=Point(r)};
    static void Click(UnityEngine.UI.Button b){Check(b.interactable,"Native button available: "+b.name);var e=Pointer((RectTransform)b.transform);ExecuteEvents.Execute(b.gameObject,e,ExecuteEvents.pointerClickHandler);}
    static GameObject Hit(RectTransform r){var results=new List<RaycastResult>();EventSystem.current.RaycastAll(Pointer(r),results);return results.Count==0?null:results[0].gameObject;}
    static string Stats(PlayerActorRuntime p)=>JsonConvert.SerializeObject(new{p.Equipment.CurrentWeaponStats,maxHp=p.Health.MaxHp,speed=p.Movement.BaseMoveSpeed,items=PlayerContext.Instance.CurrentActorInventory.Items.Select(i=>i?.runtimeInstanceId).ToArray(),gem=p.Equipment.EquippedElementGem?.runtimeInstanceId});

    static IEnumerator Verify()
    {
        while(!TitleCharacterPanel.Instance||!PlayerInputFacade.Current||!PlayerContext.Instance?.CurrentActor)yield return null;
        Click(TitleCharacterPanel.Instance.start);for(int frame=0;frame<4;frame++)yield return null;
        Click(TitleCharacterPanel.Instance.enter);
        while(PersistentSceneFlow.Instance==null||PersistentSceneFlow.Instance.IsSwitching||!WorldSessionState.IsHideout||PlayerContext.Instance?.CurrentActor==null)yield return null;
        Check(AccountBootstrap.SaveDirectory.StartsWith(Output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Owned isolated account");
        var ui=OverburstHudMenu.Instance;var game=Object.FindFirstObjectByType<OverburstGameUI>();var tree=OverburstSkillTreeUI.Instance;var menu=OverburstGameMenu.Instance;
        Check(ui&&game&&tree&&menu,"Existing product presenters and corner menu installed");
        Check(Object.FindObjectsByType<OverburstHudMenu>(FindObjectsSortMode.None).Length==1&&ui.Rows.Length==5,"Exactly one menu with five rows");
        var input=PlayerInputFacade.Current;bool gameplay=input.IsGameplayEnabled;float time=Time.timeScale;bool audioPause=AudioListener.pause;
        Check(input.RuntimeAsset.FindAction("Gameplay/Inventory",true).bindings[0].effectivePath=="<Keyboard>/i","Inventory defaults to I");
        Check(input.RuntimeAsset.FindAction("Gameplay/MapToggle",true).bindings[0].effectivePath=="<Keyboard>/tab","Expanded map defaults to Tab");
        yield return null;yield return null;
        Check(Hit((RectTransform)ui.trigger.transform)==ui.trigger.gameObject,"Actual raycast reaches menu button");
        Click(ui.trigger);yield return null;
        while(ui.popup.alpha<.999f)yield return null;
        Check(ui.IsOpen&&!GameplayInputBlocker.IsGameplayInputBlocked&&input.IsGameplayEnabled==gameplay&&Time.timeScale==time,"Expanded navigation leaves gameplay available");
        Check(!ui.outside.gameObject.activeInHierarchy,"No full-screen outside-click blocker");
        var dock=ui.dock.anchoredPosition;
        var hits=new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=new Vector2(100,Screen.height-100)},hits);
        Check(hits.All(h=>!h.gameObject.transform.IsChildOf(ui.transform)),"Clicks away from corner pass through menu");
        foreach(var row in ui.Rows)Check(Hit((RectTransform)row.transform)==row.gameObject,"Actual raycast reaches row "+row.label.text);
        ui.outside.onClick.Invoke();yield return null;Check(ui.IsOpen,"Outside clicks leave menu expanded");
        var previousKeyboard=Keyboard.current;var keyboard=InputSystem.AddDevice<Keyboard>("OwnedHudMenuVerifierKeyboard");
        var originalSettings=InputSystem.settings;var settings=Object.Instantiate(originalSettings);
        settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;InputSystem.settings=settings;
        try
        {
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(UnityEngine.InputSystem.Key.Tab));yield return null;yield return null;
            Check(input.RuntimeAsset.FindAction("Gameplay/MapToggle",true).IsPressed()&&!game.inventory.IsVisible&&ui.IsOpen,"Actual Tab reaches map action without opening inventory");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;yield return null;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(UnityEngine.InputSystem.Key.I));yield return null;yield return null;
            Check(game.inventory.IsVisible&&ui.IsOpen,"Actual I opens inventory with menu expanded");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;yield return null;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(UnityEngine.InputSystem.Key.Escape));yield return null;yield return null;
            Check(!game.inventory.IsVisible&&ui.IsOpen&&!OverburstGameMenu.IsOpen,"Escape closes inventory and preserves menu");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;yield return null;
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(UnityEngine.InputSystem.Key.C));yield return null;yield return null;
            Check(game.equipmentWindow.gameObject.activeInHierarchy&&ui.IsOpen,"Actual C opens equipment with menu expanded");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;yield return null;game.CloseEquipment();
        }
        finally{InputSystem.settings=originalSettings;Object.Destroy(settings);if(keyboard.added)InputSystem.RemoveDevice(keyboard);if(previousKeyboard!=null&&previousKeyboard.added)previousKeyboard.MakeCurrent();}
        yield return null;
        Click(ui.Rows[0].button);yield return null;
        Check(game.inventory.IsVisible&&ui.IsOpen&&ui.trigger.gameObject.activeInHierarchy,"Inventory row preserves expanded list and close button");
        Click(ui.Rows[1].button);yield return null;
        Check(game.inventory.IsVisible&&game.equipmentWindow.gameObject.activeInHierarchy&&ui.IsOpen,"Inventory and equipment coexist after consecutive row clicks");
        Check(ui.dock.anchoredPosition==dock&&Hit((RectTransform)ui.Rows[0].transform)==ui.Rows[0].gameObject,"Corner position and row interaction survive overlapping windows");
        ScreenCapture.CaptureScreenshot(Path.Combine(Output,"ingame-menu-two-windows.png"));yield return null;yield return null;
        Click(ui.Rows[0].button);yield return null;Check(!game.inventory.IsVisible&&ui.IsOpen,"Repeated inventory row closes only inventory");
        Click(ui.trigger);yield return null;Check(!ui.IsOpen&&game.equipmentWindow.gameObject.activeInHierarchy&&GameplayInputBlocker.IsGameplayInputBlocked,"Explicit menu close leaves equipment and its input owner intact");
        Click(ui.trigger);yield return null;Check(ui.IsOpen,"Menu reopens while equipment remains open");game.CloseEquipment();yield return null;
        Click(ui.Rows[2].button);yield return null;Check(tree.IsOpen&&ui.IsOpen&&GameplayInputBlocker.IsGameplayInputBlocked,"Skill tree keeps expanded navigation and owns combat input");Check(Hit((RectTransform)ui.Rows[0].transform)==ui.Rows[0].gameObject,"Corner menu remains reachable above skill tree");tree.Close();yield return null;
        Click(ui.Rows[3].button);yield return null;Check(QuestMerchantUI.Current.Mode==QuestMerchantUI.WindowMode.Journal&&ui.IsOpen,"Quest journal keeps expanded navigation");QuestMerchantUI.Current.Close();yield return null;
        Click(ui.Rows[4].button);yield return null;Check(OverburstGameMenu.IsOpen&&menu.settings.gameObject.activeSelf&&ui.IsOpen,"Settings keeps expanded navigation");
        Check(Time.timeScale==0&&AudioListener.pause,"Settings retains pause ownership");Check(Hit((RectTransform)ui.trigger.transform)==ui.trigger.gameObject,"Corner close button remains reachable above settings");menu.Close();yield return null;
        Check(ui.IsOpen&&Mathf.Approximately(Time.timeScale,time)&&AudioListener.pause==audioPause&&input.IsGameplayEnabled==gameplay&&!GameplayInputBlocker.IsGameplayInputBlocked,"Closing settings preserves navigation and returns its own input/time/audio");
        for(int i=0;i<3;i++){Click(ui.trigger);yield return null;Check(!ui.IsOpen,"Explicit collapse "+i);Click(ui.trigger);yield return null;Check(ui.IsOpen,"Repeat expand "+i);}
        ui.enabled=false;yield return null;Check(!ui.IsOpen&&!GameplayInputBlocker.IsGameplayInputBlocked,"Disable closes navigation without stray blockers");ui.enabled=true;yield return null;
        Check(File.Exists(Path.Combine(Output,"ingame-menu-two-windows.png")),"Composited inventory/equipment/menu capture saved");
    }
}
