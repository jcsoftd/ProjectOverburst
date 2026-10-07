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
public static class OverburstSettingsScrollVerifier
{
    const string K="Overburst.SettingsScrollVerifier.";
    const string Destination="../개인파일/코덱스산출/UI/20261007_SettingsScrollAndIcons/";
    static readonly List<string> checks=new List<string>();
    static readonly Stack<IEnumerator> work=new Stack<IEnumerator>();
    static readonly Dictionary<InputActionAsset,InputDevice[]> masks=new Dictionary<InputActionAsset,InputDevice[]>();
    static string output;static int frame=-1;static double deadline;
    static InputSettings originalInput,temporaryInput;
    static Mouse mouse;static Keyboard keyboard;static MouseState pointer;
    static OverburstSettingsScrollVerifier()
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
        OverburstSettingsGothicBuilder.RequireIdle(); output=Dir("Native"); checks.Clear();
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(OverburstGameMenuBuilder.PrefabPath);
        var p=prefab.GetComponentInChildren<OverburstSettingsPanel>(true);var v=p.GetComponent<OverburstSettingsGothicView>();
        Check(v && v.scrolls.Length==4,"four original categories retained");
        foreach(var scroll in v.scrolls)
        {
            var smooth=scroll.viewport.GetComponent<OverburstSettingsSmoothScroll>();
            Check(smooth && smooth.scroll==scroll && Mathf.Approximately(smooth.unitsPerNotch,76.8f),"viewport wheel handler bound "+scroll.name);
            Check(!scroll.GetComponent<OverburstSettingsSmoothScroll>() && scroll.movementType==ScrollRect.MovementType.Clamped,"native drag owner and finite bounds retained "+scroll.name);
        }
        foreach(var t in prefab.GetComponentsInChildren<Transform>(true))
            if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)>0)throw new InvalidOperationException("Missing script "+t.name);
        Check(true,"all installed prefab scripts present");
        Write("assets-result.json",new{success=true,checks}); return "PASS "+checks.Count;
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
        mouse=InputSystem.AddDevice<Mouse>("SettingsScrollVerifierMouse");keyboard=InputSystem.AddDevice<Keyboard>("SettingsScrollVerifierKeyboard");SessionState.SetInt(K+"mouse",mouse.deviceId);SessionState.SetInt(K+"keyboard",keyboard.deviceId);
        var module=EventSystem.current.GetComponent<InputSystemUIInputModule>();
        foreach(var asset in new[]{PlayerInputFacade.Current.RuntimeAsset,module.actionsAsset}.Where(a=>a!=null).Distinct()){masks[asset]=asset.devices.HasValue?asset.devices.Value.ToArray():null;asset.devices=new InputDevice[]{mouse,keyboard};}
        SessionState.SetString(K+"masks",JsonConvert.SerializeObject(masks.Select(p=>new{asset=p.Key.GetInstanceID(),devices=p.Value?.Select(d=>d.deviceId).ToArray()})));
        pointer=new MouseState();InputSystem.onBeforeUpdate+=Push;
    }
    static void Push(){if(mouse!=null && mouse.added){InputSystem.QueueStateEvent(mouse,pointer);pointer.scroll=Vector2.zero;}}
    static Vector2 Point(RectTransform r,float x=.5f,float y=.5f)=>RectTransformUtility.WorldToScreenPoint(null,r.TransformPoint(new Vector3(Mathf.Lerp(r.rect.xMin,r.rect.xMax,x),Mathf.Lerp(r.rect.yMin,r.rect.yMax,y))));
    static IEnumerator Click(Selectable control)
    {
        Canvas.ForceUpdateCanvases();var pos=Point((RectTransform)control.transform);var data=new PointerEventData(EventSystem.current){position=pos};var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(data,hits);
        Check(hits.Count>0 && hits[0].gameObject.GetComponentInParent<Selectable>()==control,"pointer raycast reaches "+control.name);
        pointer=new MouseState{position=pos};yield return Frames(2);pointer.buttons=1;yield return Frames(2);pointer.buttons=0;yield return Frames(3);
    }
    static void Scroll(ScrollRect s,float value){s.StopMovement();Canvas.ForceUpdateCanvases();s.verticalNormalizedPosition=value;}
    static IEnumerator Capture(string name){ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));yield return Frames(3);}
    static IEnumerator Wheel(ScrollRect scroll,float raw)
    {
        pointer=new MouseState{position=Point(scroll.viewport,.02f,.3f),scroll=new Vector2(0,raw)};
        yield return Frames(1);pointer.scroll=Vector2.zero;
    }
    static IEnumerator Settle(float seconds=.8f)
    {
        float end=Time.realtimeSinceStartup+seconds;
        while(Time.realtimeSinceStartup<end)yield return null;
    }
    static IEnumerator Verify()
    {
        yield return Frames(30);SetupInput();yield return Frames(4);
        Check(InputSystem.settings.scrollDeltaBehavior==InputSettings.ScrollDeltaBehavior.UniformAcrossAllPlatforms,"fixture sends one normalized wheel tick, matching current Input System settings");
        var menu=OverburstGameMenu.Instance;menu.Open();menu.OpenSettings();yield return Frames(5);
        var p=menu.settings;var v=p.GetComponent<OverburstSettingsGothicView>();
        yield return Click(p.tabs[3]);var scroll=p.keyScroll;
        Scroll(scroll,1);yield return Frames(3);
        Check(Mathf.Approximately(Time.timeScale,0f),"settings pause the game while wheel animation uses unscaled time");
        float before=scroll.content.anchoredPosition.y;
        var samples=new List<object>();var positions=new List<float>();
        yield return Wheel(scroll,-1);
        float start=Time.realtimeSinceStartup;
        while(Time.realtimeSinceStartup-start<.8f)
        {float y=scroll.content.anchoredPosition.y;positions.Add(y);samples.Add(new{time=Time.realtimeSinceStartup-start,y});yield return null;}
        Write("wheel-trace.json",new{before,step=76.8f,samples});
        Check(positions.Count>3 && positions[0]-before<76f && positions.Count(y=>y>before+.1f && y<before+76.5f)>=3,"one wheel notch advances over multiple frames without an instant jump");
        Check(positions.Zip(positions.Skip(1),(a,b)=>b>=a-.01f).All(x=>x),"wheel motion is monotonic without bouncing");
        Check(Mathf.Abs(scroll.content.anchoredPosition.y-before-76.8f)<.1f,"one physical wheel notch moves 32 design pixels");
        yield return Capture("settings-controls-smooth");
        yield return Wheel(scroll,-1);yield return Frames(2);float reversedAt=scroll.content.anchoredPosition.y;
        yield return Wheel(scroll,1);yield return Settle();
        Check(scroll.content.anchoredPosition.y<reversedAt,"opposite wheel direction responds immediately");
        Scroll(scroll,1);yield return Frames(2);
        yield return Wheel(scroll,-1);yield return Frames(2);yield return Click(p.tabs[0]);
        float bookmark=scroll.content.anchoredPosition.y;yield return Settle(.3f);yield return Click(p.tabs[3]);yield return Settle(.3f);
        Check(Mathf.Abs(scroll.content.anchoredPosition.y-bookmark)<.1f,"category switch cancels queued motion and preserves the visible bookmark");
        Scroll(scroll,1);yield return Frames(2);yield return Wheel(scroll,-1);yield return Frames(2);
        var finalRow=v.rows.Last(r=>r.tabIndex==3);v.SelectRow(finalRow,true);float focus=scroll.content.anchoredPosition.y;yield return Settle(.3f);
        Check(Mathf.Abs(scroll.content.anchoredPosition.y-focus)<.1f,"keyboard focus takes priority over a pending wheel target");
        var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport,finalRow.transform);
        Check(bounds.min.y>=scroll.viewport.rect.yMin-.1f && bounds.max.y<=scroll.viewport.rect.yMax+.1f,"keyboard-focused last row stays visible");
        Scroll(scroll,1);yield return Frames(2);yield return Wheel(scroll,-1);yield return Frames(2);
        var dragPoint=Point(scroll.viewport,.02f,.3f);pointer=new MouseState{position=dragPoint,buttons=1};yield return Frames(2);
        float dragStart=scroll.content.anchoredPosition.y;pointer.position=dragPoint-new Vector2(0,50);yield return Frames(3);pointer.buttons=0;yield return Frames(2);
        Check(scroll.content.anchoredPosition.y>dragStart+5,"native pointer dragging still moves the ScrollRect");
        scroll.StopMovement();float dragEnd=scroll.content.anchoredPosition.y;yield return Settle(.3f);
        Check(Mathf.Abs(scroll.content.anchoredPosition.y-dragEnd)<.1f,"released drag does not resume an earlier wheel target");
        yield return Wheel(scroll,-100);yield return Settle();
        float max=Mathf.Max(0,scroll.content.rect.height-scroll.viewport.rect.height);
        Check(Mathf.Abs(scroll.content.anchoredPosition.y-max)<.1f,"rapid wheel input clamps at the last row without overshoot");
        yield return Click(p.tabs[2]);yield return Click(v.foldouts[0].button);Scroll(p.combatScroll,1);yield return Frames(2);
        yield return Wheel(p.combatScroll,-1);yield return Frames(2);yield return Click(v.foldouts[0].button);yield return Settle();
        Check(p.combatScroll.content.anchoredPosition.y>=-.1f && p.combatScroll.content.anchoredPosition.y<=Mathf.Max(0,p.combatScroll.content.rect.height-p.combatScroll.viewport.rect.height)+.1f,"folding during scrolling clamps to the resized content");
        yield return Click(p.tabs[0]);float fixedY=v.scrolls[0].content.anchoredPosition.y;yield return Wheel(v.scrolls[0],-1);yield return Settle(.3f);
        Check(Mathf.Abs(v.scrolls[0].content.anchoredPosition.y-fixedY)<.1f,"a page that fits does not move on wheel input");
        yield return Click(p.tabs[3]);Scroll(scroll,1);yield return Frames(2);yield return Wheel(scroll,-1);yield return Frames(2);
        menu.CloseSettings();float closed=scroll.content.anchoredPosition.y;menu.OpenSettings();yield return Settle(.3f);
        Check(Mathf.Abs(scroll.content.anchoredPosition.y-closed)<.1f,"reopening settings leaves no queued wheel animation");
        menu.CloseSettings();menu.Close();yield return Frames(3);
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
        var m=InputSystem.GetDeviceById(SessionState.GetInt(K+"mouse",0));if(m is Mouse a && a.name=="SettingsScrollVerifierMouse")mouse=a;
        var b=InputSystem.GetDeviceById(SessionState.GetInt(K+"keyboard",0));if(b is Keyboard c && c.name=="SettingsScrollVerifierKeyboard")keyboard=c;
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
