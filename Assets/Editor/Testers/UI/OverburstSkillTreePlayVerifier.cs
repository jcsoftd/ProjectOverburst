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
public static class OverburstSkillTreePlayVerifier
{
    const string Key="Overburst.SkillTreePlayVerifier.";
    const string Guard="Overburst.IsolatedSavePlayGuard.";
    const string Weapon="Assets/ProjectOverburst/03_Features/Weapons/WP02_Greatsword/GRS024_TrainingIronGreatsword/GRS024_TrainingIronGreatsword.asset";
    static readonly List<string> checks=new List<string>();
    static IEnumerator work;
    static int lastFrame=-1;
    static double deadline;
    static string Output=>SessionState.GetString(Key+"output","");
    static bool Pending=>SessionState.GetBool(Key+"pending",false);
    static OverburstSkillTreePlayVerifier(){EditorApplication.playModeStateChanged+=State;EditorApplication.update+=Update;}
    static string Scenes()=>JsonConvert.SerializeObject(Enumerable.Range(0,SceneManager.sceneCount).Select(i=>{var s=SceneManager.GetSceneAt(i);return new{s.path,s.isDirty,roots=s.GetRootGameObjects().Select(r=>r.GetInstanceID()).OrderBy(x=>x).ToArray()};}).ToArray());
    public static string Run(string output)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating||Pending)throw new InvalidOperationException("Idle Editor required.");
        if(!string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory)||!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable))||!string.IsNullOrEmpty(SessionState.GetString(Guard+"prepared","")))throw new InvalidOperationException("Another isolated account is active/prepared.");
        if(SceneManager.GetActiveScene().name!="PersistentScene")throw new InvalidOperationException("PersistentScene required; user scenes are not opened or saved by this verifier.");
        var full=Path.GetFullPath(output);IsolatedSavePlayGuard.ValidateDirectory(Path.Combine(full,"IsolatedAccount"));Directory.CreateDirectory(full);
        SessionState.SetString(Key+"output",full);SessionState.SetString(Key+"scenes",Scenes());SessionState.SetString(Key+"startScene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(Key+"background",Application.runInBackground);SessionState.SetFloat(Key+"timeScale",Time.timeScale);
        SessionState.SetBool(Key+"pending",true);SessionState.SetBool(Key+"return",false);SessionState.SetBool(Key+"entered",false);SessionState.SetString(Key+"status","RUNNING");
        SessionState.SetString(Key+"bootDeadline",(EditorApplication.timeSinceStartup+180).ToString(CultureInfo.InvariantCulture));
        File.WriteAllText(Path.Combine(full,"play-results.json"),"{\"status\":\"RUNNING\"}");
        try{IsolatedSavePlayGuard.EnterIsolatedPlay(Path.Combine(full,"IsolatedAccount"));}catch{SessionState.SetBool(Key+"return",true);throw;}
        return "STARTED owned isolated skill-tree UI Play";
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
        if(SessionState.GetBool(Key+"return",false)){ReturnAccount();return;}
        if(!Pending)return;
        if(!EditorApplication.isPlayingOrWillChangePlaymode){SessionState.SetBool(Key+"return",true);return;}
        if(!EditorApplication.isPlaying)return;
        if(work==null){
            if(!string.Equals(IsolatedSavePlayGuard.ActiveDirectory,Path.Combine(Output,"IsolatedAccount"),StringComparison.OrdinalIgnoreCase))return;
            if(SessionState.GetBool(Key+"entered",false))Finish(new InvalidOperationException("Owned UI verification interrupted by script reload; normal-account return scheduled."));
            else if(double.TryParse(SessionState.GetString(Key+"bootDeadline",""),NumberStyles.Float,CultureInfo.InvariantCulture,out double bootDeadline)&&EditorApplication.timeSinceStartup>bootDeadline)Finish(new TimeoutException("Owned UI Play did not initialize within 180 seconds."));
            return;
        }
        EditorApplication.QueuePlayerLoopUpdate();if(lastFrame==Time.frameCount)return;lastFrame=Time.frameCount;
        try{if(EditorApplication.timeSinceStartup>deadline)throw new TimeoutException("Owned UI Play exceeded 180 seconds.");if(!work.MoveNext())Finish(null);}catch(Exception e){Finish(e);}
    }
    static void Finish(Exception error)
    {
        (work as IDisposable)?.Dispose();work=null;
        SessionState.SetString(Key+"status",error==null?"PASS_SCOPED":"FAIL");
        File.WriteAllText(Path.Combine(Output,"play-results.json"),JsonConvert.SerializeObject(new{status=SessionState.GetString(Key+"status",""),checks,error=error?.ToString(),width=Screen.width,height=Screen.height,combatEffects="NOT_APPLIED_UI_ONLY",playerBuild="NOT_RUN",humanFeel="NOT_RUN"},Formatting.Indented));
        SessionState.SetBool(Key+"return",true);
        string active=IsolatedSavePlayGuard.ActiveDirectory;
        if(EditorApplication.isPlaying&&string.Equals(active,Path.Combine(Output,"IsolatedAccount"),StringComparison.OrdinalIgnoreCase))EditorApplication.ExitPlaymode();
    }
    static void ReturnAccount()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        var current=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)??"";var prepared=SessionState.GetString(Guard+"prepared","");
        bool Own(string p)=>string.IsNullOrEmpty(p)||p.StartsWith(Output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
        if(!Own(current)||!Own(prepared)||!Own(IsolatedSavePlayGuard.ActiveDirectory))return;
        try{
            IsolatedSavePlayGuard.UseRealAccount();Application.runInBackground=SessionState.GetBool(Key+"background",false);Time.timeScale=SessionState.GetFloat(Key+"timeScale",1);
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
    static Vector2 Point(RectTransform r)=>RectTransformUtility.WorldToScreenPoint(r.GetComponentInParent<Canvas>().rootCanvas.worldCamera,r.position);
    static PointerEventData Pointer(RectTransform r)=>new PointerEventData(EventSystem.current){button=PointerEventData.InputButton.Left,position=Point(r)};
    static void Click(UnityEngine.UI.Button b){Check(b.interactable,"Native button available: "+b.name);var e=Pointer((RectTransform)b.transform);ExecuteEvents.Execute(b.gameObject,e,ExecuteEvents.pointerClickHandler);}
    static GameObject Hit(RectTransform r){var results=new List<RaycastResult>();EventSystem.current.RaycastAll(Pointer(r),results);return results.Count==0?null:results[0].gameObject;}
    static string Stats(PlayerActorRuntime p)=>JsonConvert.SerializeObject(new{p.Equipment.CurrentWeaponStats,maxHp=p.Health.MaxHp,speed=p.Movement.BaseMoveSpeed,items=PlayerContext.Instance.CurrentActorInventory.Items.Select(i=>i?.runtimeInstanceId).ToArray(),gem=p.Equipment.EquippedElementGem?.runtimeInstanceId});
    static IEnumerator Verify()
    {
        while(PersistentSceneFlow.Instance==null||PersistentSceneFlow.Instance.IsSwitching||!WorldSessionState.IsHideout||PlayerContext.Instance?.CurrentActor==null)yield return null;
        Check(AccountBootstrap.SaveDirectory.StartsWith(Output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Owned isolated account");
        var ui=OverburstSkillTreeUI.Instance;Check(ui!=null&&Object.FindObjectsByType<OverburstSkillTreeUI>(FindObjectsSortMode.None).Length==1,"Product GameUI installs exactly one authored skill tree");
        Check(EventSystem.current!=null&&!ui.IsOpen,"Native EventSystem and initially closed surface");
        while(!ui.entry.interactable)yield return null; yield return null;
        var input=PlayerInputFacade.Current;bool gameplay=input!=null&&input.IsGameplayEnabled;bool blocked=GameplayInputBlocker.IsGameplayInputBlocked;Check(!blocked,"Gameplay initially available");
        Canvas.ForceUpdateCanvases();Check(Hit((RectTransform)ui.entry.transform)==ui.entry.gameObject,"HUD entry native raycast path");Click(ui.entry);yield return null;
        Check(ui.IsOpen&&GameplayInputBlocker.IsGameplayInputBlocked&&input!=null&&!input.IsGameplayEnabled,"Entry click opens and owns gameplay input block");
        Check(ui.nodes.Length==49&&ui.Plan.Remaining==16,"49 common nodes with ephemeral planning budget");ui.HideTooltip();Canvas.ForceUpdateCanvases();
        foreach(var node in ui.nodes){var rect=(RectTransform)node.transform;Check(Hit(rect)==node.gameObject,"Native node raycast "+node.nodeId);ExecuteEvents.Execute(node.gameObject,Pointer(rect),ExecuteEvents.pointerEnterHandler);Check(ui.tooltip.gameObject.activeSelf&&ui.tipName.text==ui.Catalog.nodes.First(n=>n.id==node.nodeId).name,"Native hover "+node.nodeId);ExecuteEvents.Execute(node.gameObject,Pointer(rect),ExecuteEvents.pointerExitHandler);Check(!ui.tooltip.gameObject.activeSelf,"Native hover exit "+node.nodeId);}
        var p=PlayerContext.Instance.CurrentActor;var before=Stats(p);
        ui.SelectNode("W11",true);Click(ui.action);Check(ui.Plan.Has("W11")&&ui.Plan.Changed&&ui.Plan.Remaining==15,"Native plan action and cost");Click(ui.cancel);Check(!ui.Plan.Has("W11")&&!ui.Plan.Changed,"Native cancel");
        ui.SelectNode("W11",true);Click(ui.action);Click(ui.apply);Check(ui.Plan.Has("W11")&&!ui.Plan.Changed,"Native apply remains RAM planning");Check(Stats(p)==before,"Planner leaves actual inventory, weapon stats, HP and movement unchanged");
        ui.ResetMap();var e=Pointer(ui.viewport);e.scrollDelta=Vector2.up;ExecuteEvents.Execute(ui.viewport.gameObject,e,ExecuteEvents.scrollHandler);Check(Mathf.Approximately(ui.Zoom,1.1f),"Native wheel event zoom");Click(ui.zoomIn);Check(Mathf.Approximately(ui.Zoom,1.3f),"Native plus button zoom");Click(ui.zoomOut);Check(Mathf.Approximately(ui.Zoom,1.1f),"Native minus button zoom");
        e=Pointer(ui.viewport);ExecuteEvents.Execute(ui.viewport.gameObject,e,ExecuteEvents.beginDragHandler);e.position+=new Vector2(70,-40);var panBefore=ui.Pan;ExecuteEvents.Execute(ui.viewport.gameObject,e,ExecuteEvents.dragHandler);ExecuteEvents.Execute(ui.viewport.gameObject,e,ExecuteEvents.endDragHandler);Check(ui.Pan!=panBefore&&!ui.tooltip.gameObject.activeSelf,"Native drag pans and dismisses tooltip");Click(ui.center);Check(ui.Zoom==1&&ui.Pan==Vector2.zero,"Native center button");
        var selected=EventSystem.current.currentSelectedGameObject;var axis=new AxisEventData(EventSystem.current){moveDir=MoveDirection.Right,moveVector=Vector2.right};ExecuteEvents.Execute(selected,axis,ExecuteEvents.moveHandler);Check(EventSystem.current.currentSelectedGameObject!=selected&&ui.tooltip.gameObject.activeSelf,"Native directional focus and tooltip");
        ui.SelectNode("K_W",true);Canvas.ForceUpdateCanvases();float mapZoom=ui.Zoom;var wheel=Pointer(ui.effectScroll.viewport);wheel.scrollDelta=new Vector2(0,-10);ExecuteEvents.Execute(ui.effectScroll.gameObject,wheel,ExecuteEvents.scrollHandler);Canvas.ForceUpdateCanvases();Check(ui.effectScroll.verticalNormalizedPosition<1&&ui.Zoom==mapZoom,"Detail scrolling preserves map zoom");
        Click(ui.close);yield return null;
        var previousKeyboard=Keyboard.current;var ownedKeyboard=InputSystem.AddDevice<Keyboard>("OwnedSkillTreeVerifierKeyboard");
        try{
            Click(ui.entry);yield return null;InputSystem.QueueStateEvent(ownedKeyboard,new KeyboardState(UnityEngine.InputSystem.Key.NumpadPlus));yield return null;yield return null;Check(Mathf.Approximately(ui.Zoom,1.2f),"Actual plus key zoom");InputSystem.QueueStateEvent(ownedKeyboard,new KeyboardState());yield return null;InputSystem.QueueStateEvent(ownedKeyboard,new KeyboardState(UnityEngine.InputSystem.Key.NumpadMinus));yield return null;yield return null;Check(ui.Zoom==1,"Actual minus key zoom");InputSystem.QueueStateEvent(ownedKeyboard,new KeyboardState());yield return null;
            InputSystem.QueueStateEvent(ownedKeyboard,new KeyboardState(UnityEngine.InputSystem.Key.E));yield return null;yield return null;Check(ui.IsOpen&&!OverburstGameMenu.IsOpen&&!Object.FindFirstObjectByType<OverburstGameUI>().equipmentWindow.gameObject.activeInHierarchy,"Equipment key cannot open window underneath skill tree");InputSystem.QueueStateEvent(ownedKeyboard,new KeyboardState());yield return null;
            InputSystem.QueueStateEvent(ownedKeyboard,new KeyboardState(UnityEngine.InputSystem.Key.Escape));yield return null;yield return null;Check(!ui.IsOpen&&!OverburstGameMenu.IsOpen&&!GameplayInputBlocker.IsGameplayInputBlocked&&input.IsGameplayEnabled==gameplay,"Actual Escape closes only skill tree and restores input");InputSystem.QueueStateEvent(ownedKeyboard,new KeyboardState());yield return null;
        }finally{if(ownedKeyboard.added)InputSystem.RemoveDevice(ownedKeyboard);if(previousKeyboard!=null&&previousKeyboard.added)previousKeyboard.MakeCurrent();}
        Click(ui.entry);yield return null;
        var weapon=AssetDatabase.LoadAssetAtPath<WeaponItemData>(Weapon);var item=new ItemData(weapon,1,ItemGrade.Common);Check(p.Inventory.AddItem(item)&&p.Equipment.EquipWeaponItem(item),"Isolated actual weapon fixture");
        string allocation=string.Join(",",ui.Plan.Planned),topology=JsonConvert.SerializeObject(ui.Catalog.nodes.Select(n=>new{n.id,n.x,n.y,n.cost,n.requires}));
        foreach(var element in new[]{WeaponElement.Fire,WeaponElement.Ice,WeaponElement.Electric,WeaponElement.Dark,WeaponElement.Light}){
            var data=Resources.LoadAll<ElementGemItemData>("Items/ElementGems").Where(g=>g.element==element).OrderBy(g=>g.fixedGrade).First();var gem=new ItemData(data,1,data.fixedGrade);var inventory=PlayerContext.Instance.CurrentActorInventory;Check(inventory.AddItem(gem),"Isolated gem inventory "+element);int slot=Enumerable.Range(0,inventory.Items.Count).First(i=>inventory.GetItemAt(i)?.runtimeInstanceId==gem.runtimeInstanceId);
            Check(ElementGemEquipmentService.EquipFromInventorySlot(slot,gem.runtimeInstanceId),"Actual gem equipment service "+element);yield return null;ui.SelectNode("W01",true);ui.ShowTooltip("W01");yield return null;
            Check(ui.CurrentElement==element&&ui.elementLabel.text.Contains(OverburstSkillTreeCatalog.ElementName(element))&&ui.tipEffect.text==ui.Catalog.nodes.First(n=>n.id=="W01").effects[OverburstSkillTreeCatalog.ElementIndex(element)],"Real equipped gem detail and tooltip "+element);
            Check(ui.equippedIcon.sprite==ui.elementSprites[OverburstSkillTreeCatalog.ElementIndex(element)]&&ui.effectIcons.Take(5).Select((icon,i)=>icon.sprite==ui.elementSprites[i]).All(v=>v),"Equipped badge and comparison use actual HUD element icons "+element);
            Check(string.Join(",",ui.Plan.Planned)==allocation&&JsonConvert.SerializeObject(ui.Catalog.nodes.Select(n=>new{n.id,n.x,n.y,n.cost,n.requires}))==topology,"Shared tree and allocation preserved "+element);
            if(element==WeaponElement.Fire){
                ui.HideTooltip();ui.ResetMap();Canvas.ForceUpdateCanvases();yield return null;ScreenCapture.CaptureScreenshot(Path.Combine(Output,"ingame-skill-tree.png"));yield return null;
                ui.ShowTooltip("W01");yield return null;ScreenCapture.CaptureScreenshot(Path.Combine(Output,"ingame-tooltip.png"));yield return null;
                ui.HideTooltip();ui.ZoomAt(2,Vector2.zero);ui.SelectNode("W01",true);yield return null;ScreenCapture.CaptureScreenshot(Path.Combine(Output,"ingame-zoom200.png"));yield return null;ui.ResetMap();
                Check(ui.Plan.Toggle("S_W2"),"Owned pending-plan visual fixture");ui.Refresh();Check(ui.cancel.interactable&&ui.apply.interactable&&ui.cancel.GetComponent<CanvasGroup>().alpha==1&&ui.apply.GetComponent<CanvasGroup>().alpha==1,"Pending plan enables matched footer controls");yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(Output,"ingame-planning-active.png"));yield return null;ui.Plan.Cancel();ui.Refresh();Check(!ui.Plan.Changed&&ui.cancel.GetComponent<CanvasGroup>().alpha<1&&ui.apply.GetComponent<CanvasGroup>().alpha<1,"Applied plan dims inactive controls consistently");
            }
        }
        Check(ElementGemEquipmentService.UnequipToInventory(),"Actual gem unequip");ui.RefreshDetail();ui.ShowTooltip("W01");Check(ui.CurrentElement==WeaponElement.None&&ui.tipEffect.text==ui.Catalog.nodes.First(n=>n.id=="W01").effects[5],"Neutral gem fallback");
        ui.HideTooltip();Click(ui.close);yield return null;Check(!ui.IsOpen&&!GameplayInputBlocker.IsGameplayInputBlocked&&input.IsGameplayEnabled==gameplay,"Native close restores owned input");
        for(int i=0;i<3;i++){Click(ui.entry);yield return null;Check(ui.IsOpen&&!input.IsGameplayEnabled,"Repeat open "+i);Click(ui.close);yield return null;Check(!ui.IsOpen&&!GameplayInputBlocker.IsGameplayInputBlocked&&input.IsGameplayEnabled==gameplay,"Repeat close "+i);}
        var other=new GameObject("Owned Skill Tree Input Fixture");GameplayInputBlocker.Block(other);try{ui.Open();Check(!ui.IsOpen,"Respects other gameplay input owner");}finally{GameplayInputBlocker.Unblock(other);Object.Destroy(other);}
        double until=EditorApplication.timeSinceStartup+5;while(!File.Exists(Path.Combine(Output,"ingame-skill-tree.png"))&&EditorApplication.timeSinceStartup<until)yield return null;Check(File.Exists(Path.Combine(Output,"ingame-skill-tree.png"))&&File.Exists(Path.Combine(Output,"ingame-tooltip.png")),"Composited product UI captures saved");
    }
}
