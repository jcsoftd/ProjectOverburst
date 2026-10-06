using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TMPro;
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

// Own session keys, input devices and output. Never changes another tester's callbacks or shared scene.
[InitializeOnLoad]
public static class OverburstSettingsGothicVerifier
{
    const string K="Overburst.SettingsGothicVerifier.";
    const string Destination="../개인파일/코덱스산출/UI/20261006_SettingsGothic/";
    static readonly List<string> checks=new List<string>();
    static readonly Stack<IEnumerator> work=new Stack<IEnumerator>();
    static readonly Dictionary<InputActionAsset,InputDevice[]> masks=new Dictionary<InputActionAsset,InputDevice[]>();
    static string output;static int frame=-1;static double deadline;
    static InputSettings originalInput,temporaryInput;
    static Mouse mouse;static Keyboard keyboard;static MouseState pointer;
    static OverburstSettingsGothicVerifier()
    {
        EditorApplication.playModeStateChanged+=State;
        if(!string.IsNullOrEmpty(SessionState.GetString(K+"pending","")))EditorApplication.update+=Begin;
        if(!string.IsNullOrEmpty(SessionState.GetString(K+"return","")))EditorApplication.update+=Return;
        if(SessionState.GetBool(K+"running",false))EditorApplication.update+=Interrupted;
    }
    static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);checks.Add(message);}
    static string Dir(string name){string path=Path.GetFullPath(Destination+name);Directory.CreateDirectory(path);return path;}
    static void Write(string name,object data)=>File.WriteAllText(Path.Combine(output,name),JsonConvert.SerializeObject(data,Formatting.Indented));
    public static string AssetsCheck()
    {
        OverburstSettingsGothicBuilder.RequireIdle();output=Dir("Native");checks.Clear();
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(OverburstGameMenuBuilder.PrefabPath);
        var p=prefab.GetComponentInChildren<OverburstSettingsPanel>(true);var v=p.GetComponent<OverburstSettingsGothicView>();
        Check(v && v.panel==p && p.tabs.Length==4 && p.pages.Length==4,"four categories and original settings owner retained");
        Check(v.foldouts.Length==3 && v.scrolls.Length==4,"three foldouts and four finite scroll views");
        Check(p.bloodRows.All(r=>Enum.IsDefined(typeof(BloodComparisonTuning.Control),r.control)),"only supported tuning controls bound");
        foreach(var t in prefab.GetComponentsInChildren<Transform>(true))Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0,"script present "+t.name);
        foreach(var r in v.rows)
        {
            Check(r && r.owner==v && r.icon && r.highlight && r.outline,"row presentation linked "+r.title);
            var label=r.transform.Find("Label") as RectTransform;var control=r.transform.Find("Control") as RectTransform;
            if(!control && r.key)control=(RectTransform)r.key.keyButton.transform;
            if(label && control){var a=RectTransformUtility.CalculateRelativeRectTransformBounds(r.transform,label);var b=RectTransformUtility.CalculateRelativeRectTransformBounds(r.transform,control);Check(a.max.x<=b.min.x,"separate text and input columns "+r.title);}
        }
        foreach(var t in p.GetComponentsInChildren<TMP_Text>(true))Check(t.font && t.font.material && t.font.atlasTextures[0],"TMP font resources "+t.name);
        Write("assets-result.json",new{success=true,checks,rows=v.rows.Length,bloodRows=p.bloodRows.Length});return "PASS "+checks.Count;
    }
    public static void Start(string label)
    {
        OverburstSettingsGothicBuilder.RequireIdle();
        if(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY")))throw new InvalidOperationException("External settings directory active");
        output=Dir(label);string save=Path.Combine(output,"isolated-save");Directory.CreateDirectory(save);
        File.WriteAllText(Path.Combine(save,OverburstGameSettings.FileName),"{\"version\":1,\"masterVolume\":0.62,\"cameraShake\":0.4}");
        Write("edit-before.json",new{pid=System.Diagnostics.Process.GetCurrentProcess().Id,scenes=Scenes(),input=InputSystem.settings.GetInstanceID(),startScene=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)});
        SessionState.SetString(K+"pending",output);SessionState.SetString(K+"return",output);SessionState.SetString(K+"startScene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(K+"background",Application.runInBackground);SessionState.SetFloat(K+"deadline",(float)EditorApplication.timeSinceStartup+240);
        SessionState.SetFloat(K+"returnDeadline",(float)EditorApplication.timeSinceStartup+600);
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        EditorApplication.update-=Begin;EditorApplication.update+=Begin;
        try{IsolatedSavePlayGuard.EnterIsolatedPlay(save);}catch(Exception error){Finish(error);}
    }
    static object[] Scenes()=>Enumerable.Range(0,SceneManager.sceneCount).Select(i=>{var s=SceneManager.GetSceneAt(i);return (object)new{s.path,s.isDirty};}).ToArray();
    static bool OwnPlay()=>string.Equals(IsolatedSavePlayGuard.ActiveDirectory,Path.Combine(SessionState.GetString(K+"return",""),"isolated-save"),StringComparison.OrdinalIgnoreCase);
    static void Begin()
    {
        var pending=SessionState.GetString(K+"pending","");if(string.IsNullOrEmpty(pending)){EditorApplication.update-=Begin;return;}
        output=pending;
        if(EditorApplication.timeSinceStartup>SessionState.GetFloat(K+"deadline",0)){Finish(new Exception("Boot timeout"));return;}
        if(!EditorApplication.isPlaying || !OwnPlay() || PlayerInputFacade.Current==null || OverburstGameMenu.Instance==null)return;
        EditorApplication.update-=Begin;SessionState.EraseString(K+"pending");SessionState.SetBool(K+"running",true);
        checks.Clear();Application.runInBackground=true;deadline=EditorApplication.timeSinceStartup+180;frame=-1;work.Push(Verify());EditorApplication.update+=Tick;
    }
    static void Tick()
    {
        if(Time.frameCount==frame && EditorApplication.timeSinceStartup<deadline)return;frame=Time.frameCount;
        try
        {
            if(!EditorApplication.isPlaying || !OwnPlay() || EditorApplication.timeSinceStartup>deadline)throw new Exception("Owned Play exit or timeout");
            while(work.Count>0){var task=work.Peek();if(!task.MoveNext()){(work.Pop() as IDisposable)?.Dispose();continue;}if(task.Current is IEnumerator nested){work.Push(nested);continue;}return;}
            Finish(null);
        }catch(Exception error){Finish(error);}
    }
    static IEnumerator Frames(int count){for(int i=0;i<count;i++)yield return null;}
    static void SetupInput()
    {
        originalInput=InputSystem.settings;temporaryInput=Object.Instantiate(originalInput);
        SessionState.SetInt(K+"input",originalInput.GetInstanceID());SessionState.SetInt(K+"temporaryInput",temporaryInput.GetInstanceID());
        temporaryInput.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;temporaryInput.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;InputSystem.settings=temporaryInput;
        mouse=InputSystem.AddDevice<Mouse>("SettingsGothicVerifierMouse");keyboard=InputSystem.AddDevice<Keyboard>("SettingsGothicVerifierKeyboard");SessionState.SetInt(K+"mouse",mouse.deviceId);SessionState.SetInt(K+"keyboard",keyboard.deviceId);
        var module=EventSystem.current.GetComponent<InputSystemUIInputModule>();
        foreach(var asset in new[]{PlayerInputFacade.Current.RuntimeAsset,module.actionsAsset}.Where(a=>a!=null).Distinct()){masks[asset]=asset.devices.HasValue?asset.devices.Value.ToArray():null;asset.devices=new InputDevice[]{mouse,keyboard};}
        SessionState.SetString(K+"masks",JsonConvert.SerializeObject(masks.Select(p=>new{asset=p.Key.GetInstanceID(),devices=p.Value?.Select(d=>d.deviceId).ToArray()})));
        pointer=new MouseState();InputSystem.onBeforeUpdate+=Push;
    }
    static void Push(){if(mouse!=null && mouse.added)InputSystem.QueueStateEvent(mouse,pointer);}
    static Vector2 Point(RectTransform r,float x=.5f,float y=.5f)=>RectTransformUtility.WorldToScreenPoint(null,r.TransformPoint(new Vector3(Mathf.Lerp(r.rect.xMin,r.rect.xMax,x),Mathf.Lerp(r.rect.yMin,r.rect.yMax,y))));
    static IEnumerator Click(Selectable control)
    {
        Canvas.ForceUpdateCanvases();var pos=Point((RectTransform)control.transform);var data=new PointerEventData(EventSystem.current){position=pos};var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(data,hits);
        Check(hits.Count>0 && hits[0].gameObject.GetComponentInParent<Selectable>()==control,"pointer raycast reaches "+control.name);
        pointer=new MouseState{position=pos};yield return Frames(2);pointer.buttons=1;yield return Frames(2);pointer.buttons=0;yield return Frames(3);
    }
    static void Scroll(ScrollRect s,float value){s.StopMovement();Canvas.ForceUpdateCanvases();s.verticalNormalizedPosition=value;}
    static IEnumerator Capture(string name){ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));yield return Frames(3);}
    static IEnumerator Verify()
    {
        yield return Frames(30);SetupInput();yield return Frames(4);
        var menu=OverburstGameMenu.Instance;menu.Open();menu.OpenSettings();yield return Frames(5);var p=menu.settings;var v=p.GetComponent<OverburstSettingsGothicView>();
        Check(v && v.rows.All(r=>r.owner==v),"actual installed menu uses concept B");
        pointer=new MouseState{position=Point((RectTransform)p.tabs[1].transform)};yield return Frames(3);
        Check(v.tabBackgrounds[1].color.a>.5f && !p.tabs[1].isOn,"inactive category has actual pointer hover feedback");
        foreach(int tab in new[]{0,1,2,3}){yield return Click(p.tabs[tab]);Check(p.pages[tab].activeSelf && p.pages.Where((_,i)=>i!=tab).All(page=>!page.activeSelf),"pointer category "+tab);yield return Capture("settings-tab-"+tab);}
        yield return Click(p.tabs[2]);
        Check(v.foldouts.All(f=>!f.expanded && !f.body.gameObject.activeSelf),"advanced controls start collapsed");
        Check(p.combatScroll.content.rect.height<=p.combatScroll.viewport.rect.height+2,"default combat options fit viewport");
        var shake=v.rows.First(r=>r.slider==p.cameraShake);v.SelectRow(shake);yield return Frames(3);
        Check(v.helpTitle.text==shake.title && v.helpValue.text=="40%","help shows seeded selected value");
        yield return Capture("settings-B-default");
        var track=(RectTransform)p.cameraShake.transform;var start=Point(track,.26f);var end=Point(track,.48f);
        pointer=new MouseState{position=start};yield return Frames(2);pointer.buttons=1;yield return Frames(2);pointer.position=end;yield return Frames(3);pointer.buttons=0;yield return Frames(3);
        Check(Mathf.Approximately(OverburstGameSettings.CameraShakeScale,p.cameraShake.value) && v.helpValue.text==Mathf.RoundToInt(p.cameraShake.value*100)+"%","actual slider drag updates setting and help");
        yield return Click(v.foldouts[0].button);Check(v.foldouts[0].body.gameObject.activeInHierarchy && p.combatScroll.content.rect.height>p.combatScroll.viewport.rect.height,"flow expands without overlapping following groups");
        p.edgeBlur.isOn=false;yield return Frames(2);Check(!p.explorationEdgeBlurIntensity.interactable && !p.combatEdgeBlurIntensity.interactable,"disabled parent retains dependent control policy");p.edgeBlur.isOn=true;
        Check(!p.motionBlur.interactable && !p.motionBlurIntensity.interactable,"unavailable motion blur remains disabled");
        pointer=new MouseState{position=Point(p.combatScroll.viewport),scroll=new Vector2(0,-120)};yield return Frames(3);pointer.scroll=Vector2.zero;yield return Frames(2);
        Check(p.combatScroll.verticalNormalizedPosition<.99f,"actual mouse wheel scrolls expanded options");
        Scroll(p.combatScroll,1);yield return Frames(3);
        yield return Capture("settings-B-screen-details");yield return Click(v.foldouts[0].button);
        Scroll(p.combatScroll,1);yield return Frames(3);yield return Click(v.foldouts[2].button);Scroll(p.combatScroll,0);yield return Frames(3);
        var number=p.bloodRows[0];float before=BloodComparisonTuning.Value(number.control),bookmark=p.combatScroll.verticalNormalizedPosition;
        yield return Click(number.increase);Check(Mathf.Approximately(BloodComparisonTuning.Value(number.control),before+.1f),"actual numeric plus changes one tenth");Check(Mathf.Abs(p.combatScroll.verticalNormalizedPosition-bookmark)<.01f,"numeric pointer edit retains scroll");
        yield return Click(number.decrease);Check(Mathf.Approximately(BloodComparisonTuning.Value(number.control),before),"actual numeric minus restores value");yield return Capture("settings-B-blood-details");
        yield return Click(p.tabs[3]);Scroll(p.keyScroll,1);yield return Frames(3);var key=p.keyRows[0];yield return Click(key.keyButton);Check(p.keyPrompt.activeInHierarchy && p.ConsumesEscape,"actual key button enters rebinding");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Escape));yield return Frames(3);InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return Frames(3);Check(!p.keyPrompt.activeSelf && !p.ConsumesEscape,"escape cancels rebinding without closing settings");
        yield return Click(key.keyButton);InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F8));yield return Frames(12);InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return Frames(3);
        Check(!p.keyPrompt.activeSelf && key.CurrentPath(PlayerInputFacade.Current.RuntimeAsset)=="<Keyboard>/f8" && key.keyText.text=="F8" && v.helpValue.text=="F8","actual key rebind updates button and help");
        yield return Capture("settings-B-key-rebound");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.DownArrow));yield return Frames(2);InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return Frames(3);
        Check(EventSystem.current.currentSelectedGameObject!=key.keyButton.gameObject && v.SelectedRow && v.SelectedRow.tabIndex==3 && v.helpTitle.text==v.SelectedRow.title,"actual keyboard navigation moves focus and help together");
        Write("bookmark-before-return.json",new{bookmark,actual=p.combatScroll.verticalNormalizedPosition,height=p.combatScroll.content.rect.height,selected=EventSystem.current.currentSelectedGameObject.name});
        yield return Click(p.tabs[2]);Write("bookmark-after-return.json",new{bookmark,actual=p.combatScroll.verticalNormalizedPosition,height=p.combatScroll.content.rect.height});Check(Mathf.Abs(p.combatScroll.verticalNormalizedPosition-bookmark)<.01f,"combat bookmark survives category changes after keyboard navigation");
        for(int i=0;i<3;i++){yield return Click(p.footerCloseButton);Check(!p.gameObject.activeSelf,"footer closes settings "+i);menu.OpenSettings();yield return Frames(3);Check(p.gameObject.activeSelf && v.CurrentTab()==2,"repeat entry retains selected category "+i);}
        menu.CloseSettings();menu.Close();yield return Frames(3);
        string file=Path.Combine(output,"isolated-save",OverburstGameSettings.FileName);Check(File.Exists(file),"settings saved to owned isolated directory");
        var json=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));Check(Mathf.Approximately((float)json["cameraShake"],p.cameraShake.value),"saved camera value matches actual UI");
    }
    static void State(PlayModeStateChange state)
    {
        if(state==PlayModeStateChange.ExitingPlayMode && work.Count>0)Finish(new Exception("Play exited during verification"));
        if(state==PlayModeStateChange.EnteredEditMode && !string.IsNullOrEmpty(SessionState.GetString(K+"return",""))){EditorApplication.update-=Return;EditorApplication.update+=Return;}
    }
    static void RestoreInput()
    {
        InputSystem.onBeforeUpdate-=Push;foreach(var p in masks)if(p.Key)p.Key.devices=p.Value;masks.Clear();SessionState.EraseString(K+"masks");
        if(mouse!=null && mouse.added)InputSystem.RemoveDevice(mouse);if(keyboard!=null && keyboard.added)InputSystem.RemoveDevice(keyboard);mouse=null;keyboard=null;
        if(originalInput){InputSystem.settings=originalInput;var type=typeof(InputSettings).Assembly.GetType("UnityEngine.InputSystem.InputSystemObject");if(type!=null)foreach(var state in Resources.FindObjectsOfTypeAll(type))(state as ISerializationCallbackReceiver)?.OnBeforeSerialize();}
        if(temporaryInput)Object.DestroyImmediate(temporaryInput);originalInput=null;temporaryInput=null;
        foreach(string field in new[]{"input","temporaryInput","mouse","keyboard"})SessionState.EraseInt(K+field);
    }
    static void Finish(Exception error)
    {
        EditorApplication.update-=Tick;EditorApplication.update-=Begin;EditorApplication.update-=Interrupted;while(work.Count>0)(work.Pop() as IDisposable)?.Dispose();
        bool own=OwnPlay();if(own && OverburstGameMenu.Instance){OverburstGameMenu.Instance.settings.CancelRebind();OverburstGameMenu.Instance.Close();}RestoreInput();
        Application.runInBackground=SessionState.GetBool(K+"background",Application.runInBackground);SessionState.EraseBool(K+"background");SessionState.EraseString(K+"pending");SessionState.EraseBool(K+"running");SessionState.EraseFloat(K+"deadline");
        output=SessionState.GetString(K+"return",output);
        try{Write("play-result.json",new{success=error==null,checks,error=error?.ToString()});}
        finally
        {
            if(EditorApplication.isPlaying && own)EditorApplication.ExitPlaymode();
            if(!EditorApplication.isPlayingOrWillChangePlaymode){EditorApplication.update-=Return;EditorApplication.update+=Return;}
        }
    }
    static void Interrupted()
    {
        if(EditorApplication.isCompiling || EditorApplication.isUpdating)return;
        output=SessionState.GetString(K+"return","");if(string.IsNullOrEmpty(output)){EditorApplication.update-=Interrupted;return;}if(EditorApplication.isPlaying && !OwnPlay())return;
        var json=SessionState.GetString(K+"masks","");if(!string.IsNullOrEmpty(json))foreach(var r in Newtonsoft.Json.Linq.JArray.Parse(json)){var asset=EditorUtility.InstanceIDToObject((int)r["asset"]) as InputActionAsset;if(asset)masks[asset]=r["devices"].Type==Newtonsoft.Json.Linq.JTokenType.Null?null:r["devices"].Select(id=>InputSystem.GetDeviceById((int)id)).Where(d=>d!=null).ToArray();}
        temporaryInput=EditorUtility.InstanceIDToObject(SessionState.GetInt(K+"temporaryInput",0)) as InputSettings;if(temporaryInput && InputSystem.settings==temporaryInput)originalInput=EditorUtility.InstanceIDToObject(SessionState.GetInt(K+"input",0)) as InputSettings;
        var m=InputSystem.GetDeviceById(SessionState.GetInt(K+"mouse",0));if(m is Mouse a && a.name=="SettingsGothicVerifierMouse")mouse=a;
        var b=InputSystem.GetDeviceById(SessionState.GetInt(K+"keyboard",0));if(b is Keyboard c && c.name=="SettingsGothicVerifierKeyboard")keyboard=c;
        Finish(new Exception("Verification interrupted by assembly reload"));
    }
    static void Return()
    {
        output=SessionState.GetString(K+"return","");if(string.IsNullOrEmpty(output)){EditorApplication.update-=Return;return;}
        if(EditorApplication.timeSinceStartup>SessionState.GetFloat(K+"returnDeadline",0)){EditorApplication.update-=Return;Write("return-result.json",new{success=false,reason="return timeout"});return;}
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory))return;
        var env=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable);var prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","");
        foreach(var path in new[]{env,prepared})if(!string.IsNullOrEmpty(path) && !Path.GetFullPath(path).StartsWith(output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return;
        string scene=SessionState.GetString(K+"startScene","");EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(scene)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(scene);IsolatedSavePlayGuard.UseRealAccount();
        SessionState.EraseString(K+"return");SessionState.EraseString(K+"startScene");SessionState.EraseFloat(K+"returnDeadline");EditorApplication.update-=Return;
        Write("return-result.json",new{success=!IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)),guard=IsolatedSavePlayGuard.RequiresAccountChoice,active=IsolatedSavePlayGuard.ActiveDirectory,prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""),expires=SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires",""),input=InputSystem.settings.GetInstanceID(),startScene=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),scenes=Scenes()});
    }
}
