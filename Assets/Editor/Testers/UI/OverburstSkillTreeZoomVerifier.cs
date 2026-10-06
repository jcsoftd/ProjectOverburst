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
public static class OverburstSkillTreeZoomVerifier
{
    const string Key="Overburst.SkillTreeZoomVerifier.";
    const string Guard="Overburst.IsolatedSavePlayGuard.";
    static readonly List<string> checks=new List<string>();
    static IEnumerator work;
    static int lastFrame=-1;
    static double deadline;
    static string Output=>SessionState.GetString(Key+"output","");
    static bool Pending=>SessionState.GetBool(Key+"pending",false);
    static OverburstSkillTreeZoomVerifier(){EditorApplication.playModeStateChanged+=State;EditorApplication.update+=Update;}
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
        File.WriteAllText(Path.Combine(Output,"play-results.json"),JsonConvert.SerializeObject(new{status=SessionState.GetString(Key+"status",""),checks,error=error?.ToString(),width=Screen.width,height=Screen.height,scope="ZOOM_UI_ONLY_NO_ALLOCATION",playerBuild="NOT_RUN",humanFeel="NOT_RUN"},Formatting.Indented));
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
    static IEnumerable Steps(IEnumerator routine)
    {
        try { while (routine.MoveNext()) yield return routine.Current; }
        finally { (routine as IDisposable)?.Dispose(); }
    }
    static IEnumerator OpenFromMenu()
    {
        var menu = OverburstHudMenu.Instance;
        Check(menu != null, "Vertical HUD menu installed");
        Click(menu.trigger); yield return null;
        while (menu.popup.alpha < .999f) yield return null;
        var index = Array.FindIndex(menu.entries, e => e.destination == OverburstHudMenu.Destination.SkillTree);
        Check(index >= 0 && Hit((RectTransform)menu.Rows[index].transform) == menu.Rows[index].gameObject, "Actual HUD skill-tree row raycast");
        Click(menu.Rows[index].button); yield return null;
        Check(OverburstSkillTreeUI.IsWindowOpen && !menu.IsOpen, "HUD row transfers input ownership to skill tree");
    }
    static IEnumerator Verify()
    {
        while(PersistentSceneFlow.Instance==null||PersistentSceneFlow.Instance.IsSwitching||!WorldSessionState.IsHideout||PlayerContext.Instance?.CurrentActor==null)yield return null;
        Check(AccountBootstrap.Ready&&AccountBootstrap.SaveDirectory.StartsWith(Output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Owned isolated account");
        var ui=OverburstSkillTreeUI.Instance;var session=AccountGameplaySession.Current;var input=PlayerInputFacade.Current;bool gameplay=input.IsGameplayEnabled;
        var baseline=JsonConvert.SerializeObject(session.ReadSkillTree());
        Check(ui!=null&&ui.nodes.Length==53,"Current product 53-node prefab");
        foreach(var step in Steps(OpenFromMenu()))yield return step;
        Check(Mathf.Approximately(ui.Zoom,1.2f)&&ui.zoomLabel.text=="120%"&&ui.Zoom>ui.FitZoom,"Actual HUD opens readable 120% default");
        Check(!input.IsGameplayEnabled&&GameplayInputBlocker.IsGameplayInputBlocked,"Tree owns gameplay input");
        ui.HideTooltip();ScreenCapture.CaptureScreenshot(Path.Combine(Output,"ingame-default120.png"));yield return null;
        Click(ui.center);yield return null;Canvas.ForceUpdateCanvases();
        Check(Mathf.Approximately(ui.Zoom,ui.FitZoom)&&ui.Pan==Vector2.zero,"Separate whole-tree fit");
        foreach(var node in ui.nodes){ui.HideTooltip();Canvas.ForceUpdateCanvases();Check(Hit((RectTransform)node.transform)==node.gameObject,"Full-map node raycast "+node.nodeId);}
        ScreenCapture.CaptureScreenshot(Path.Combine(Output,"ingame-fit.png"));yield return null;
        var e=Pointer(ui.viewport);e.scrollDelta=Vector2.up;float prior=ui.Zoom;ExecuteEvents.Execute(ui.viewport.gameObject,e,ExecuteEvents.scrollHandler);
        Check(Mathf.Approximately(ui.Zoom,prior+.1f),"Native wheel zoom after fit");
        Click(ui.zoomIn);Check(ui.Zoom>prior+.1f,"Zoom button increases map");Click(ui.zoomOut);
        var oldPan=ui.Pan;e=Pointer(ui.viewport);ExecuteEvents.Execute(ui.viewport.gameObject,e,ExecuteEvents.beginDragHandler);e.position+=new Vector2(90,-45);ExecuteEvents.Execute(ui.viewport.gameObject,e,ExecuteEvents.dragHandler);ExecuteEvents.Execute(ui.viewport.gameObject,e,ExecuteEvents.endDragHandler);
        Check(ui.Pan!=oldPan,"Native drag moves map");
        ui.ZoomAt(20,Vector2.zero);Check(Mathf.Approximately(ui.Zoom,2.5f),"Max zoom 250%");
        foreach(var id in new[]{"K_W","K_H","K_D","K_Q"}){
            ui.SelectNode(id,true);yield return null;Canvas.ForceUpdateCanvases();ui.HideTooltip();var node=ui.nodes.First(n=>n.nodeId==id);
            Check(Hit((RectTransform)node.transform)==node.gameObject,"Selecting outer key brings target into view "+id);
            ExecuteEvents.Execute(node.gameObject,Pointer((RectTransform)node.transform),ExecuteEvents.pointerEnterHandler);
            Check(ui.tooltip.gameObject.activeSelf&&ui.tipName.text==ui.Catalog.nodes.First(n=>n.id==id).name,"Outer key hover at max zoom "+id);
            Check(!ui.action.interactable&&ui.actionLabel.text.Contains("확장 예정"),"Reserved key remains read only "+id);
        }
        ui.HideTooltip();Click(ui.center);yield return null;Click(ui.close);yield return null;
        Check(!ui.IsOpen&&!GameplayInputBlocker.IsGameplayInputBlocked&&input.IsGameplayEnabled==gameplay,"Close restores own input");
        foreach(var step in Steps(OpenFromMenu()))yield return step;
        Check(Mathf.Approximately(ui.Zoom,1.2f)&&ui.zoomLabel.text=="120%","Reopen restores 120% default");
        Click(ui.close);yield return null;
        Check(JsonConvert.SerializeObject(session.ReadSkillTree())==baseline,"Zoom and selection never change account allocation/points");
        Check(!ui.IsOpen&&!GameplayInputBlocker.IsGameplayInputBlocked&&input.IsGameplayEnabled==gameplay,"Repeated close releases own input");
    }
}
