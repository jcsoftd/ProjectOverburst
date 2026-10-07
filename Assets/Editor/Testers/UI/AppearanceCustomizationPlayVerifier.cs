using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Overburst.Appearance;
using Overburst.Persistence;
using Newtonsoft.Json.Linq;
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
public static class AppearanceCustomizationPlayVerifier
{
    const string K="Overburst.AppearanceCustomizationPlayVerifier.";
    const string Destination="../개인파일/코덱스산출/UI/20261006_AppearanceCustomizationGoal/Play/";
    static readonly List<string> checks=new List<string>();
    static readonly Stack<IEnumerator> work=new Stack<IEnumerator>();
    static readonly Dictionary<InputActionAsset,InputDevice[]> masks=new Dictionary<InputActionAsset,InputDevice[]>();
    static string output;static int frame=-1;static double deadline;
    static InputSettings originalInput,temporaryInput;
    static Mouse mouse;static Keyboard keyboard;static MouseState pointer;
    static AppearanceCustomizationPlayVerifier()
    {
        EditorApplication.playModeStateChanged+=State;
        if(!string.IsNullOrEmpty(SessionState.GetString(K+"pending","")))EditorApplication.update+=Begin;
        if(!string.IsNullOrEmpty(SessionState.GetString(K+"return","")))EditorApplication.update+=Return;
        if(SessionState.GetBool(K+"running",false))EditorApplication.update+=Interrupted;
    }
    public static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);checks.Add(message);}
    static string Dir(string name){string path=Path.GetFullPath(Destination+name);Directory.CreateDirectory(path);return path;}
    static void Write(string name,object data)=>File.WriteAllText(Path.Combine(output,name),JsonConvert.SerializeObject(data,Formatting.Indented));
    public static void Start(string label)
    {
        AppearanceCustomizationBuilder.RequireIdle();
        if(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY")))throw new InvalidOperationException("External settings directory active");
        output=Dir(label);string save=Path.Combine(output,"isolated-save");Directory.CreateDirectory(save);

        Write("edit-before.json",new{pid=System.Diagnostics.Process.GetCurrentProcess().Id,scenes=Scenes(),input=InputSystem.settings.GetInstanceID(),startScene=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)});
        SessionState.SetString(K+"pending",output);SessionState.SetString(K+"return",output);SessionState.SetString(K+"startScene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(K+"background",Application.runInBackground);SessionState.SetFloat(K+"deadline",(float)EditorApplication.timeSinceStartup+240);
        SessionState.SetFloat(K+"returnDeadline",(float)EditorApplication.timeSinceStartup+1200);
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
        if(!EditorApplication.isPlaying || !OwnPlay() || PlayerInputFacade.Current==null || AppearanceCustomizationPanel.Instance==null || !AccountBootstrap.Ready || !WorldSessionState.IsHideout || !PlayerContext.Instance?.CurrentActor || Object.FindObjectsByType<AppearanceStylistInteractable>(FindObjectsSortMode.None).Length!=1)return;
        EditorApplication.update-=Begin;SessionState.EraseString(K+"pending");SessionState.SetBool(K+"running",true);
        checks.Clear();Application.runInBackground=true;deadline=EditorApplication.timeSinceStartup+600;frame=-1;work.Push(Verify());EditorApplication.update+=Tick;
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
    public static IEnumerator Frames(int count){for(int i=0;i<count;i++)yield return null;}
    static void SetupInput()
    {
        originalInput=InputSystem.settings;temporaryInput=Object.Instantiate(originalInput);
        SessionState.SetInt(K+"input",originalInput.GetInstanceID());SessionState.SetInt(K+"temporaryInput",temporaryInput.GetInstanceID());
        temporaryInput.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;temporaryInput.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;InputSystem.settings=temporaryInput;
        mouse=InputSystem.AddDevice<Mouse>("AppearanceVerifierMouse");keyboard=InputSystem.AddDevice<Keyboard>("AppearanceVerifierKeyboard");SessionState.SetInt(K+"mouse",mouse.deviceId);SessionState.SetInt(K+"keyboard",keyboard.deviceId);
        var module=EventSystem.current.GetComponent<InputSystemUIInputModule>();
        foreach(var asset in new[]{PlayerInputFacade.Current.RuntimeAsset,module.actionsAsset}.Where(a=>a!=null).Distinct()){masks[asset]=asset.devices.HasValue?asset.devices.Value.ToArray():null;asset.devices=new InputDevice[]{mouse,keyboard};}
        SessionState.SetString(K+"masks",JsonConvert.SerializeObject(masks.Select(p=>new{asset=p.Key.GetInstanceID(),devices=p.Value?.Select(d=>d.deviceId).ToArray()})));
        pointer=new MouseState();InputSystem.onBeforeUpdate+=Push;
    }
    static void Push(){if(mouse!=null && mouse.added)InputSystem.QueueStateEvent(mouse,pointer);}
    static Vector2 Point(RectTransform r,float x=.5f,float y=.5f)=>RectTransformUtility.WorldToScreenPoint(null,r.TransformPoint(new Vector3(Mathf.Lerp(r.rect.xMin,r.rect.xMax,x),Mathf.Lerp(r.rect.yMin,r.rect.yMax,y))));
    public static IEnumerator Click(Selectable control)
    {
        Canvas.ForceUpdateCanvases();var pos=Point((RectTransform)control.transform);var data=new PointerEventData(EventSystem.current){position=pos};var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(data,hits);
        Check(hits.Count>0 && hits[0].gameObject.GetComponentInParent<Selectable>()==control,"pointer raycast reaches "+control.name);
        pointer=new MouseState{position=pos};yield return Frames(2);pointer.buttons=1;yield return Frames(2);pointer.buttons=0;yield return Frames(3);
    }
    static void Scroll(ScrollRect s,float value){s.StopMovement();Canvas.ForceUpdateCanvases();s.verticalNormalizedPosition=value;}
    public static IEnumerator Capture(string name){ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));yield return Frames(3);}

    static object[] Ancestors(Transform leaf)
    {
        var result=new List<object>();
        for(var t=leaf;t;t=t.parent){var c=t.GetComponent<Canvas>();var g=t.GetComponent<CanvasGroup>();var r=t as RectTransform;
            result.Add(new{name=t.name,active=t.gameObject.activeInHierarchy,position=t.position.ToString(),scale=t.lossyScale.ToString(),rect=r?r.rect.ToString():null,canvas=c?new{enabled=c.enabled,mode=c.renderMode.ToString(),order=c.sortingOrder}:null,group=g?new{alpha=g.alpha,blocks=g.blocksRaycasts,interactable=g.interactable}:null});}
        return result.ToArray();
    }
    static IEnumerator VerifyPreviewPointer(AppearanceCustomizationPanel panel)
    {
        Canvas.ForceUpdateCanvases();var pos=Point(panel.characterTarget.rectTransform,.52f,.56f);
        var data=new PointerEventData(EventSystem.current){position=pos};var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(data,hits);
        Write("pointer-geometry.json",new{point=pos.ToString(),screen=new[]{Screen.width,Screen.height},hits=hits.Select(h=>new{name=h.gameObject.name,depth=h.depth,order=h.sortingOrder,distance=h.distance}),canvas=panel.rootCanvas.name,mode=panel.rootCanvas.renderMode.ToString(),rootCanvas=panel.rootCanvas.rootCanvas.name,canvasEnabled=panel.rootCanvas.enabled,canvasRect=panel.rootCanvas.GetComponent<RectTransform>().rect.ToString(),characterRect=panel.characterTarget.rectTransform.rect.ToString(),raw=panel.characterTarget.IsActive(),ancestors=Ancestors(panel.transform)});
        Check(hits.Count>0&&hits[0].gameObject.GetComponentInParent<AppearanceCharacterPreview>()==panel.preview,"actual character region receives pointer drag and wheel");
        var camera=(Camera)typeof(AppearanceCharacterPreview).GetField("viewCamera",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(panel.preview);
        var before=panel.preview.Model.transform.localRotation;float distance=Vector3.Distance(camera.transform.position,panel.preview.Model.transform.root.position);
        var head=panel.preview.Model.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
        Check(camera.transform.position.y>head.position.y+.15f,"full body camera is above character head for descending beauty angle");
        pointer=new MouseState{position=pos};yield return Frames(2);pointer.buttons=1;yield return Frames(2);
        pointer.position=pos+new Vector2(180,0);pointer.delta=new Vector2(180,0);yield return Frames(1);pointer.delta=Vector2.zero;yield return Frames(3);pointer.buttons=0;yield return Frames(3);
        Check(Quaternion.Angle(before,panel.preview.Model.transform.localRotation)>20,"actual mouse drag rotates preview character");
        Check(Vector3.SignedAngle(before*Vector3.forward,panel.preview.Model.transform.localRotation*Vector3.forward,Vector3.up)<-20,"rightward drag uses the user requested reversed rotation");
        foreach(var attachment in panel.preview.Model.GetComponentsInChildren<UnityEngine.Animations.ParentConstraint>())
        {
            if(attachment.name!="Constraint_Head"||attachment.sourceCount!=1||!attachment.constraintActive)continue;
            var source=attachment.GetSource(0).sourceTransform;
            Check(Vector3.Distance(attachment.transform.position,source.TransformPoint(attachment.GetTranslationOffset(0)))<.0005f&&Quaternion.Angle(attachment.transform.rotation,source.rotation*Quaternion.Euler(attachment.GetRotationOffset(0)))<.1f,"hair follows head while pointer rotates character");
        }
        pointer.scroll=new Vector2(0,120);yield return Frames(2);pointer.scroll=Vector2.zero;yield return Frames(3);
        Check(Vector3.Distance(camera.transform.position,panel.preview.Model.transform.root.position)<distance-.1f,"actual mouse wheel zooms preview camera");
        yield return Capture("appearance-runtime-rotated-zoomed");yield return Click(panel.resetView);
        Check(Quaternion.Angle(before,panel.preview.Model.transform.localRotation)<.1f&&Mathf.Abs(Vector3.Distance(camera.transform.position,panel.preview.Model.transform.root.position)-distance)<.01f,"reset view restores original rotation and zoom");
    }

    static string OtherAccount(AccountSnapshot snapshot)
    {
        var value=JObject.FromObject(snapshot);foreach(var key in new[]{"appearance","revision","lastTransactionId"})value.Remove(key);return value.ToString(Formatting.None);
    }
    static AppearanceChoiceButton Choice(AppearanceCustomizationPanel p,AppearanceChoiceKind kind,string id)=>p.choices.Single(c=>c.kind==kind&&c.optionId==id);
    static EasySaveAccountStore Store(AccountGameplaySession account)
    {
        var tx=typeof(AccountGameplaySession).GetField("transactions",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(account);
        return (EasySaveAccountStore)typeof(AccountTransactions).GetField("store",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(tx);
    }
    static IEnumerator OpenByInteraction(AppearanceStylistInteractable npc,PlayerActorRuntime actor)
    {
        ActorTeleportUtility.TeleportSafely(actor.transform,npc.transform.position+npc.transform.forward*1.35f,Quaternion.LookRotation(-npc.transform.forward));
        yield return Frames(8);
        var interaction=actor.GetComponent<PlayerInteractionController>();Check(interaction&&interaction.Current==npc,"stylist selected by actual interaction resolver");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F));yield return Frames(3);InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return Frames(4);
        Check(AppearanceCustomizationPanel.IsOpen&&AppearanceCustomizationPanel.Instance.Owner==npc,"F opens installed appearance UI from hideout NPC");
    }
    static IEnumerator Verify()
    {
        yield return Frames(20);SetupInput();yield return Frames(4);
        var p=AppearanceCustomizationPanel.Instance;var c=p.catalog;var actor=PlayerContext.Instance.CurrentActor;
        var npc=Object.FindObjectsByType<AppearanceStylistInteractable>(FindObjectsSortMode.None).Single();
        var account=AccountGameplaySession.Current;account.FlushPendingSave();var before=account.Read();string untouched=OtherAccount(before);
        var adapter=actor.GetComponentInChildren<P09CharacterVisualAdapter>(true);var controller=adapter.Animator.runtimeAnimatorController;var socket=adapter.RightHandWeaponSocket;
        Check(PlayerAppearanceController.Instance&&PlayerAppearanceController.Instance.Error==null,"appearance controller binds real player");
        yield return OpenByInteraction(npc,actor);
        Check(p.Session.PreviewBody==AppearancePreviewBody.Equipment&&p.Session.EquipmentExampleId==c.equipmentExamples[0].id,"first entry shows equipment 1 on preview only");
        Check(GameplayInputBlocker.IsGameplayInputBlocked,"appearance UI blocks gameplay input");
        var screen=p.surface.GetComponent<RectTransform>();var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(p.rootCanvas.transform,screen);
        var canvasRect=p.rootCanvas.GetComponent<RectTransform>();
        Check(canvasRect.rect.width>100&&canvasRect.rect.height>100&&Mathf.Abs(canvasRect.rect.width*canvasRect.lossyScale.x-Screen.width)<2&&Mathf.Abs(canvasRect.rect.height*canvasRect.lossyScale.y-Screen.height)<2&&bounds.size.x>=canvasRect.rect.width-2&&bounds.size.y>=canvasRect.rect.height-2,"product surface fills actual screen with nonzero canvas geometry");
        AppearanceCustomizationVerifier.CheckStageBackground(p,Check);
        AppearanceCustomizationVerifier.CheckControlsLayout(p,Check);
        yield return Capture("appearance-runtime-default");
        yield return VerifyPreviewPointer(p);
        yield return Click(Choice(p,AppearanceChoiceKind.Face,c.faces[0].id).button);
        yield return Click(p.back);Check(p.discardConfirmation.activeSelf,"back asks to discard changed appearance");
        yield return Click(p.continueEditing);Check(!p.discardConfirmation.activeSelf&&p.Session!=null,"continue retains appearance draft");
        yield return Click(p.back);yield return Click(p.discardAndClose);Check(!AppearanceCustomizationPanel.IsOpen&&OtherAccount(account.Read())==untouched&&Equals(account.ReadAppearance(),before.appearance),"discard leaves saved appearance and account untouched");
        yield return OpenByInteraction(npc,actor);
        yield return Click(Choice(p,AppearanceChoiceKind.Face,c.faces[0].id).button);
        yield return Click(Choice(p,AppearanceChoiceKind.BodyStyle,c.bodyStyles[2].id).button);
        yield return Click(Choice(p,AppearanceChoiceKind.Equipment,c.equipmentExamples[0].id).button);
        Check(p.Session.PreviewBody==AppearancePreviewBody.Equipment&&OtherAccount(account.Read())==untouched,"equipment example changes preview only");
        var candidate=p.Session.CandidateForSave();var store=Store(account);var savedBefore=account.ReadAppearance();
        store.FaultInjector=phase=>{if(phase=="before-write")throw new IOException("Appearance verifier owned save failure");};
        try{yield return Click(p.apply);Check(p.Session!=null&&p.Session.CandidateForSave().Equals(candidate)&&Equals(account.ReadAppearance(),savedBefore),"failed save keeps draft and committed appearance unchanged");}
        finally{store.FaultInjector=null;}
        yield return Click(p.apply);yield return Frames(5);
        Check(!AppearanceCustomizationPanel.IsOpen&&account.ReadAppearance().Equals(candidate),"apply commits selected appearance and closes UI");
        Check(new EasySaveAccountStore(AccountBootstrap.SaveDirectory).Load().appearance.Equals(candidate),"real account store reloads applied appearance");
        Check(OtherAccount(account.Read())==untouched&&adapter.Animator.runtimeAnimatorController==controller&&adapter.RightHandWeaponSocket==socket,"apply preserves inventory equipment progression controller and weapon socket");
        var style=c.bodyStyles.Single(s=>s.id==candidate.bodyShapeId);
        Check(adapter.ModelRoot.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="bust_01_L"||t.name=="bust_01_R").All(t=>Vector3.Distance(t.localScale,style.scale)<.0001f),"actual player displays committed upper body style");
        PlayerContext.Instance.Bind(null);PlayerContext.Instance.Bind(actor);yield return Frames(3);
        Check(PlayerAppearanceController.Instance.TryApplyCurrent()&&PlayerAppearanceController.Instance.Error==null,"actor rebind reapplies persisted appearance");
        yield return OpenByInteraction(npc,actor);
        foreach(var type in TypeCache.GetTypesDerivedFrom<AppearancePlayExtension>().Where(t=>!t.IsAbstract))yield return ((AppearancePlayExtension)Activator.CreateInstance(type)).Verify(p,output);
        p.ShowAppearance();var dev=p.GetComponentInChildren<AppearanceDeveloperPreview>(true);Check(dev&&dev.iconButton&&dev.gameObject.activeInHierarchy,"isolated developer shortcut is present");
        yield return Click(dev.iconButton);Check(p.Session.PreviewBody==AppearancePreviewBody.Nude,"unlabeled developer icon toggles temporary body preview");
        yield return Click(dev.iconButton);Check(p.Session.PreviewBody==AppearancePreviewBody.Equipment&&p.Session.EquipmentExampleId==c.equipmentExamples[0].id,"developer icon restores previous equipment preview");
        for(int i=0;i<3;i++)
        {
            p.Close();yield return Frames(3);Check(p.Session==null&&!p.preview.Model&&!p.preview.Texture&&!GameplayInputBlocker.IsGameplayInputBlocked,"close releases preview model RT and gameplay input "+i);
            yield return OpenByInteraction(npc,actor);Check(p.Session.PreviewBody==AppearancePreviewBody.Equipment&&p.Session.EquipmentExampleId==c.equipmentExamples[0].id&&p.Session.Draft.Equals(candidate),"repeat entry starts equipment 1 and saved appearance "+i);
        }
        p.Close();yield return Frames(3);Check(!Resources.FindObjectsOfTypeAll<Transform>().Any(t=>t.name=="Appearance Preview Rig"),"owned preview rigs are released");
        Check(OtherAccount(account.Read())==untouched&&account.ReadAppearance().Equals(candidate),"preview animations developer switch and repeated entry do not write account");
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
        bool own=OwnPlay();if(own && AppearanceCustomizationPanel.Instance)AppearanceCustomizationPanel.Instance.Close();RestoreInput();
        Application.runInBackground=SessionState.GetBool(K+"background",Application.runInBackground);SessionState.EraseBool(K+"background");SessionState.EraseString(K+"pending");SessionState.EraseBool(K+"running");SessionState.EraseFloat(K+"deadline");
        output=SessionState.GetString(K+"return",output);Write("play-result.json",new{success=error==null,checks,error=error?.ToString()});
        if(EditorApplication.isPlaying && own)EditorApplication.ExitPlaymode();if(!EditorApplication.isPlayingOrWillChangePlaymode){EditorApplication.update-=Return;EditorApplication.update+=Return;}
    }
    static void Interrupted()
    {
        if(EditorApplication.isCompiling || EditorApplication.isUpdating)return;
        output=SessionState.GetString(K+"return","");if(string.IsNullOrEmpty(output)){EditorApplication.update-=Interrupted;return;}if(EditorApplication.isPlaying && !OwnPlay())return;
        var json=SessionState.GetString(K+"masks","");if(!string.IsNullOrEmpty(json))foreach(var r in Newtonsoft.Json.Linq.JArray.Parse(json)){var asset=EditorUtility.InstanceIDToObject((int)r["asset"]) as InputActionAsset;if(asset)masks[asset]=r["devices"].Type==Newtonsoft.Json.Linq.JTokenType.Null?null:r["devices"].Select(id=>InputSystem.GetDeviceById((int)id)).Where(d=>d!=null).ToArray();}
        temporaryInput=EditorUtility.InstanceIDToObject(SessionState.GetInt(K+"temporaryInput",0)) as InputSettings;if(temporaryInput && InputSystem.settings==temporaryInput)originalInput=EditorUtility.InstanceIDToObject(SessionState.GetInt(K+"input",0)) as InputSettings;
        var m=InputSystem.GetDeviceById(SessionState.GetInt(K+"mouse",0));if(m is Mouse a && a.name=="AppearanceVerifierMouse")mouse=a;
        var b=InputSystem.GetDeviceById(SessionState.GetInt(K+"keyboard",0));if(b is Keyboard c && c.name=="AppearanceVerifierKeyboard")keyboard=c;
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

public abstract class AppearancePlayExtension
{
    public abstract IEnumerator Verify(AppearanceCustomizationPanel panel,string output);
}
