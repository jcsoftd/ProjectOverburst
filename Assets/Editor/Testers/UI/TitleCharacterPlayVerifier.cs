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
public static class TitleCharacterPlayVerifier
{
    const string K="Overburst.TitleCharacterPlayVerifier.";
    const string Destination="../개인파일/코덱스산출/UI/20261011_TitleCharacterGoal/Evidence/Play/";
    static readonly List<string> checks=new List<string>();
    static readonly Stack<IEnumerator> work=new Stack<IEnumerator>();
    static readonly Dictionary<InputActionAsset,InputDevice[]> masks=new Dictionary<InputActionAsset,InputDevice[]>();
    static string output;static int frame=-1;static double deadline;
    static InputSettings originalInput,temporaryInput;
    static Mouse mouse;static Keyboard keyboard;static Gamepad gamepad;static MouseState pointer;
    static TitleCharacterPlayVerifier()
    {
        EditorApplication.playModeStateChanged+=State;
        if(!string.IsNullOrEmpty(SessionState.GetString(K+"pending","")))EditorApplication.update+=Begin;
        if(!string.IsNullOrEmpty(SessionState.GetString(K+"return","")))EditorApplication.update+=Return;
        if(SessionState.GetBool(K+"running",false))EditorApplication.update+=Interrupted;
    }
    public static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);checks.Add(message);}
    static string Dir(string name,string destination){string path=Path.GetFullPath(Path.Combine(destination,name));Directory.CreateDirectory(path);return path;}
    static void Write(string name,object data)=>File.WriteAllText(Path.Combine(output,name),JsonConvert.SerializeObject(data,Formatting.Indented));
    public static void Start(string label,string fixture=null)
    {
        AppearanceCustomizationBuilder.RequireIdle();
        if(!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY")))throw new InvalidOperationException("External settings directory active");
        SessionState.SetBool(K+"equipment",fixture!=null);string destination=Destination;
        output=Dir(label,destination);string save=Path.Combine(output,"isolated-save");Directory.CreateDirectory(save);
        if(fixture!=null)foreach(string name in new[]{"account-A.es3","account-B.es3"})if(File.Exists(Path.Combine(fixture,name)))File.Copy(Path.Combine(fixture,name),Path.Combine(save,name),false);
        Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY",Path.Combine(output,"isolated-settings"));

        Write("edit-before.json",new{pid=System.Diagnostics.Process.GetCurrentProcess().Id,scenes=Scenes(),input=InputSystem.settings.GetInstanceID(),startScene=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene)});
        SessionState.SetString(K+"pending",output);SessionState.SetString(K+"return",output);SessionState.SetString(K+"startScene",AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool(K+"background",Application.runInBackground);SessionState.SetFloat(K+"deadline",(float)EditorApplication.timeSinceStartup+240);
        SessionState.SetFloat(K+"returnDeadline",(float)EditorApplication.timeSinceStartup+1200);
        EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(label.StartsWith("Management",StringComparison.Ordinal)?OverburstUIWorkshopBuilder.ScenePath:"Assets/ProjectOverburst/00_Scenes/PersistentScene.unity");
        EditorApplication.update-=Begin;EditorApplication.update+=Begin;
        try{IsolatedSavePlayGuard.EnterIsolatedPlay(save);}catch(Exception error){Finish(error);}
    }
    public static IEnumerator Drag(RectTransform rect,Vector2 delta)
    {
        Canvas.ForceUpdateCanvases();var position=Point(rect);
        pointer=new MouseState{position=position};yield return Frames(2);
        pointer.buttons=1;yield return Frames(2);
        pointer.position=position+delta;pointer.delta=delta;yield return Frames(1);
        pointer.delta=Vector2.zero;yield return Frames(3);
        pointer.buttons=0;yield return Frames(3);
    }
    static object[] Scenes()=>Enumerable.Range(0,SceneManager.sceneCount).Select(i=>{var s=SceneManager.GetSceneAt(i);return (object)new{s.path,s.isDirty};}).ToArray();
    static bool OwnPlay()=>string.Equals(IsolatedSavePlayGuard.ActiveDirectory,Path.Combine(SessionState.GetString(K+"return",""),"isolated-save"),StringComparison.OrdinalIgnoreCase);
    static void Begin()
    {
        var pending=SessionState.GetString(K+"pending","");if(string.IsNullOrEmpty(pending)){EditorApplication.update-=Begin;return;}
        output=pending;
        if(EditorApplication.timeSinceStartup>SessionState.GetFloat(K+"deadline",0)){Finish(new Exception("Boot timeout"));return;}
        bool management=Path.GetFileName(output).StartsWith("Management",StringComparison.Ordinal);
        if(!EditorApplication.isPlaying || !OwnPlay() || (!management&&(PlayerInputFacade.Current==null || AppearanceCustomizationPanel.Instance==null || !TitleCharacterPanel.Instance || !PlayerContext.Instance?.CurrentActor)))return;
        EditorApplication.update-=Begin;SessionState.EraseString(K+"pending");SessionState.SetBool(K+"running",true);
        checks.Clear();Application.runInBackground=true;deadline=EditorApplication.timeSinceStartup+600;frame=-1;work.Push(management?VerifyManagement():Verify());EditorApplication.update+=Tick;
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
        mouse=InputSystem.AddDevice<Mouse>("TitleVerifierMouse");keyboard=InputSystem.AddDevice<Keyboard>("TitleVerifierKeyboard");SessionState.SetInt(K+"mouse",mouse.deviceId);SessionState.SetInt(K+"keyboard",keyboard.deviceId);
        gamepad=InputSystem.AddDevice<Gamepad>("TitleVerifierGamepad");SessionState.SetInt(K+"gamepad",gamepad.deviceId);
        var module=EventSystem.current.GetComponent<InputSystemUIInputModule>();
        foreach(var asset in new[]{PlayerInputFacade.Current.RuntimeAsset,module.actionsAsset}.Where(a=>a!=null).Distinct()){masks[asset]=asset.devices.HasValue?asset.devices.Value.ToArray():null;asset.devices=new InputDevice[]{mouse,keyboard,gamepad};}
        SessionState.SetString(K+"masks",JsonConvert.SerializeObject(masks.Select(p=>new{asset=p.Key.GetInstanceID(),devices=p.Value?.Select(d=>d.deviceId).ToArray()})));
        pointer=new MouseState();InputSystem.onBeforeUpdate+=Push;
    }
    static void Push(){if(mouse!=null && mouse.added)InputSystem.QueueStateEvent(mouse,pointer);}
    static Vector2 Point(RectTransform r,float x=.5f,float y=.5f)=>RectTransformUtility.WorldToScreenPoint(null,r.TransformPoint(new Vector3(Mathf.Lerp(r.rect.xMin,r.rect.xMax,x),Mathf.Lerp(r.rect.yMin,r.rect.yMax,y))));
    public static IEnumerator Click(Selectable control)
    {
        Canvas.ForceUpdateCanvases();var pos=Point((RectTransform)control.transform);var data=new PointerEventData(EventSystem.current){position=pos};var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(data,hits);
        bool reached=hits.Count>0 && hits[0].gameObject.GetComponentInParent<Selectable>()==control;
        if(!reached){File.WriteAllText(Path.Combine(output,"raycast-failure.json"),JsonConvert.SerializeObject(new{control=control.name,pos=new[]{pos.x,pos.y},hits=hits.Select(h=>new{h.gameObject.name,h.sortingOrder,h.depth,canvas=h.gameObject.GetComponentInParent<Canvas>()?.name,module=h.module.name,priority=h.module.sortOrderPriority}).ToArray()},Formatting.Indented));yield return Capture("raycast-failure");}
        Check(reached,"pointer raycast reaches "+control.name);
        pointer=new MouseState{position=pos};yield return Frames(2);pointer.buttons=1;yield return Frames(2);pointer.buttons=0;yield return Frames(3);
    }
    static void Scroll(ScrollRect s,float value){s.StopMovement();Canvas.ForceUpdateCanvases();s.verticalNormalizedPosition=value;}
    public static IEnumerator Capture(string name){ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));yield return Frames(3);}


    static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static EasySaveAccountStore TitleStore(TitleCharacterPanel panel)=>(EasySaveAccountStore)typeof(TitleCharacterPanel).GetField("store",Private).GetValue(panel);
    static IEnumerator Wait(Func<bool> condition,string name,int maximum=1800)
    {
        while(!condition()&&maximum-->0)yield return null;Check(condition(),name);
    }
    static IEnumerator KeyPress(Key key)
    {
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));yield return Frames(2);InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return Frames(3);
    }
    static IEnumerator TextInput(TMP_InputField field,string value)
    {
        Check(field.isFocused,"TMP name field owns keyboard input");field.ProcessEvent(new Event{type=EventType.KeyDown,keyCode=KeyCode.A,modifiers=EventModifiers.Control});
        foreach(char character in value){field.ProcessEvent(new Event{type=EventType.KeyDown,character=character,keyCode=KeyCode.None});yield return Frames(1);}
        field.ForceLabelUpdate();yield return Frames(2);Check(field.text==value,"Korean text events reach name field: "+value);
    }
    static IEnumerator Pad(GamepadButton button){InputSystem.QueueStateEvent(gamepad,new GamepadState().WithButton(button));yield return Frames(2);InputSystem.QueueStateEvent(gamepad,new GamepadState());yield return Frames(3);}
    static AppearanceChoiceButton Choice(AppearanceCustomizationPanel p,AppearanceChoiceKind kind,string id)=>p.choices.Single(c=>c.kind==kind&&c.optionId==id);
    static IEnumerator OpenCreation(TitleCharacterPanel title,string name)
    {
        yield return Click(title.create);yield return Click(title.classNext);yield return TextInput(title.nameInput,name);yield return Click(title.nameNext);yield return Frames(3);
        Check(AppearanceCustomizationPanel.IsOpen&&AppearanceCustomizationPanel.Instance.IsCreation,"shared existing panel opens creation context");
    }
    static IEnumerator Verify()
    {
        yield return Frames(12);SetupInput();yield return Frames(4);var title=TitleCharacterPanel.Instance;
        if(Path.GetFileName(output).StartsWith("Raycast",StringComparison.Ordinal)){yield return Click(title.start);yield return OpenCreation(title,"검증기사");yield return Click(AppearanceCustomizationPanel.Instance.creationRename);yield return TextInput(title.nameInput,"새벽기사");yield return Click(title.nameNext);Check(!title.NameDialogOpen,"rename dialog remains above original shared panel for pointer input");yield break;}
        Check(title&&title.Screen=="title"&&!AccountBootstrap.Ready&&AccountGameplaySession.Current==null,"Persistent boot waits at title before account projection and autosave");
        if(Path.GetFileName(output).StartsWith("Return",StringComparison.Ordinal))yield return VerifyHover(title.start,"title start");
        Check(GameplayInputBlocker.IsGameplayInputBlocked,"title blocks actor gameplay input");
        if(Path.GetFileName(output).StartsWith("Quit",StringComparison.Ordinal)){yield return Click(title.quit);Check(title.quitDialog.activeSelf,"confirmed quit opens expected modal");yield return Capture("quit-runtime");SessionState.SetBool(K+"expectedQuit",true);yield return Click(title.quitConfirm);yield return Frames(10);throw new InvalidOperationException("Confirmed quit did not stop Play");}
        yield return Click(title.quit);Check(title.quitDialog.activeSelf,"quit opens confirmation instead of immediate exit");yield return KeyPress(Key.Escape);Check(!title.quitDialog.activeSelf&&title.Screen=="title","Esc dismisses only quit confirmation");yield return Click(title.quit);yield return Pad(GamepadButton.DpadRight);Check(EventSystem.current.currentSelectedGameObject==title.quitConfirm.gameObject,"gamepad focus stays inside quit confirmation");yield return Pad(GamepadButton.DpadLeft);yield return Pad(GamepadButton.South);Check(!title.quitDialog.activeSelf&&title.Screen=="title","gamepad submit cancels quit and restores title");
        yield return Capture("title-runtime");yield return Click(title.settings);yield return Frames(10);
        Check(OverburstGameMenu.IsOpen&&OverburstGameMenu.Instance.settings.gameObject.activeSelf,"original settings panel opens from title");
        float volume=OverburstGameSettings.MasterVolume;yield return Click(OverburstGameMenu.Instance.settings.masterVolume);Check(Mathf.Abs(volume-OverburstGameSettings.MasterVolume)>.05f,"title settings slider changes actual sound preference");float selectedVolume=OverburstGameSettings.MasterVolume;
        yield return Click(OverburstGameMenu.Instance.settings.closeButton);yield return Frames(8);
        Check(!OverburstGameMenu.IsOpen&&title.Screen=="title"&&GameplayInputBlocker.IsGameplayInputBlocked,"settings returns to title and retains title input owner");
        yield return Click(title.settings);Check(Mathf.Abs(OverburstGameMenu.Instance.settings.masterVolume.value-selectedVolume)<.001f,"title settings reopen retains changed value");OverburstGameSettings.MasterVolume=volume;yield return Click(OverburstGameMenu.Instance.settings.closeButton);
        yield return Click(title.start);Check(title.Screen=="characters","start reaches actual roster");
        if(!SessionState.GetBool(K+"equipment",false))
        {
            Check(!title.enter.interactable&&title.rosterCount.text=="0명","empty account cannot enter gameplay before creation");
            yield return Click(title.create);yield return Click(title.classButtons[1]);yield return Wait(()=>!(bool)typeof(TitleCharacterPanel).GetField("rotating",Private).GetValue(title),"mage rotation settles",240);
            Check(title.classEnglish.text=="MAGE"&&!title.classNext.interactable,"white mage can be selected but cannot create");yield return Click(title.classNext);Check(!title.NameDialogOpen,"disabled class CTA does not open name input");
            yield return KeyPress(Key.RightArrow);yield return Wait(()=>title.classEnglish.text=="ARCHER"&&!(bool)typeof(TitleCharacterPanel).GetField("rotating",Private).GetValue(title),"keyboard selection rotates archer into front",240);
            yield return Pad(GamepadButton.DpadRight);yield return Wait(()=>title.classEnglish.text=="KNIGHT"&&!(bool)typeof(TitleCharacterPanel).GetField("rotating",Private).GetValue(title),"gamepad selection rotates knight into front",240);yield return Pad(GamepadButton.DpadDown);Check(EventSystem.current.currentSelectedGameObject==title.classNext.gameObject,"gamepad reaches class creation CTA");yield return Pad(GamepadButton.South);Check(title.NameDialogOpen,"gamepad submit opens actual name input");yield return KeyPress(Key.Escape);Check(!title.NameDialogOpen&&title.Screen=="classes","Esc returns from name to the same class screen");
            yield return Click(title.classButtons[0]);yield return Frames(4);yield return Click(title.classButtons[1]);yield return Frames(4);yield return Click(title.classButtons[0]);
            yield return Wait(()=>!(bool)typeof(TitleCharacterPanel).GetField("rotating",Private).GetValue(title),"interrupted rotation settles on latest knight choice",240);
            Check(title.classNext.interactable&&Mathf.Abs(Mathf.DeltaAngle(title.classPreviews[0].Model.transform.localEulerAngles.y,180))<.1f,"selected knight faces camera after interrupted rotation");yield return Capture("classes-runtime");
            yield return Click(title.classNext);yield return TextInput(title.nameInput,"a");yield return Click(title.nameNext);
            Check(title.NameDialogOpen&&!AppearanceCustomizationPanel.IsOpen&&title.nameError.text.Length>0,"invalid name keeps actual dialog with feedback");
            yield return TextInput(title.nameInput,"검증기사");yield return Capture("name-runtime");yield return Click(title.nameNext);yield return Frames(3);
            var p=AppearanceCustomizationPanel.Instance;Check(p.IsCreation&&p.Session!=null,"creation shares original appearance prefab and session");
            yield return Pad(GamepadButton.DpadRight);Check(EventSystem.current.currentSelectedGameObject==Choice(p,AppearanceChoiceKind.Face,p.catalog.faces[1].id).button.gameObject,"gamepad navigates only active creation choices");yield return Pad(GamepadButton.South);Check(p.Session.Draft.faceId==p.catalog.faces[1].id,"gamepad submit changes actual appearance draft");yield return Click(p.resetAppearance);
            yield return Click(p.back);Check(p.discardConfirmation.activeSelf,"unchanged default creation still asks before cancellation");yield return Click(p.continueEditing);
            yield return KeyPress(Key.Escape);Check(p.discardConfirmation.activeSelf,"Esc opens creation discard confirmation");yield return KeyPress(Key.Escape);Check(!p.discardConfirmation.activeSelf&&p.Session!=null&&title.Screen=="classes","Esc dismisses only the confirmation layer");
            yield return Click(p.back);yield return Click(p.discardAndClose);yield return Frames(3);
            Check(!AppearanceCustomizationPanel.IsOpen&&title.Screen=="characters"&&title.rosterCount.text=="0명"&&!File.Exists(Path.Combine(IsolatedSavePlayGuard.ActiveDirectory,"account-A.es3")),"cancel preserves empty account without checkpoint");
            yield return OpenCreation(title,"검증기사");p=AppearanceCustomizationPanel.Instance;
            yield return Click(Choice(p,AppearanceChoiceKind.Face,p.catalog.faces[0].id).button);yield return Click(Choice(p,AppearanceChoiceKind.BodyStyle,p.catalog.bodyStyles[2].id).button);
            yield return Click(p.faceFrame);yield return Click(p.fullFrame);yield return Capture("creation-runtime");
            var candidate=p.Session.CandidateForSave();yield return Click(p.creationRename);yield return TextInput(title.nameInput,"새벽기사");yield return Click(title.nameNext);
            Check(p.IsCreation&&p.Session.CandidateForSave().Equals(candidate)&&!title.NameDialogOpen,"rename retains appearance draft and closes only name modal");
            var store=TitleStore(title);store.FaultInjector=phase=>{if(phase=="before-write")throw new IOException("Owned creation Play failure");};
            try{yield return Click(p.apply);Check(p.IsCreation&&p.Session.CandidateForSave().Equals(candidate)&&p.feedback.text.Length>0,"failed creation save retains draft for retry");}
            finally{store.FaultInjector=null;}
            yield return Click(p.apply);yield return Frames(5);
            Check(!AppearanceCustomizationPanel.IsOpen&&title.Screen=="characters"&&title.rosterCount.text=="1명","successful creation closes preview before opening saved roster");
            var saved=new EasySaveAccountStore(IsolatedSavePlayGuard.ActiveDirectory).Load();Check(saved.characters.Single().name=="새벽기사"&&saved.appearance.Equals(candidate),"name appearance and first character persist in real ES3 checkpoint");
            var transaction=new AccountTransactions(saved,TitleStore(title),Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath));
            transaction.Execute("owned-progression-fixture",transaction.Revision,c=>{c.level=5;c.skillTree=AccountSkillTree.GrantEligible(null,5);var item=ItemSnapshotCodec.CopyValues(c.items[0]);item.instanceId=Guid.NewGuid().ToString("N");item.acquisitionOrder=c.nextAcquisitionOrder++;c.items.Add(item);c.stashTabs[0].slots[0]=item.instanceId;},true);
            yield return OpenCreation(title,"회귀기사");p=AppearanceCustomizationPanel.Instance;yield return Click(Choice(p,AppearanceChoiceKind.Face,p.catalog.faces[1].id).button);yield return Click(p.apply);yield return Frames(5);
            Check(title.rosterCount.text=="2명","second creation joins roster without replacing first character");
        }
        else
        {
            Check(title.rosterCount.text=="2명","second isolated boot reloads both saved characters");
            var first=title.rosterContent.GetComponentsInChildren<Button>().Single(b=>b.transform.Find("Name").GetComponent<TMP_Text>().text=="새벽기사");yield return Click(first);
        }
        string selected=title.SelectedId;yield return Capture("roster-runtime");yield return Click(title.enter);
        yield return Wait(()=>AccountBootstrap.Ready&&WorldSessionState.IsHideout&&!PersistentSceneFlow.Instance.IsSwitching,"selected character completes original MainScene boot",2400);yield return Frames(8);
        var account=AccountGameplaySession.Current;var state=account.Read();AccountInvariants.Validate(state,Resources.Load<AccountContentRegistry>(AccountContentRegistry.ResourcePath));
        Check(!TitleCharacterPanel.Instance&&state.activeCharacterId==selected&&state.characters.Count==2,"title releases itself and selected character reaches one runtime account session");
        Check(PlayerProgression.CurrentLevel==state.level&&state.level==(SessionState.GetBool(K+"equipment",false)?5:1),"actual player progression belongs to selected character");
        Check(!string.IsNullOrEmpty(state.stashTabs[0].slots[0])&&state.items.Exists(i=>i.instanceId==state.stashTabs[0].slots[0]),"shared stash item survives both character projections");
        Check(state.characters.Single(c=>c.id!=state.activeCharacterId).personal.level==(state.level==1?5:1),"dormant growth remains independent during runtime capture");
        Check(!GameplayInputBlocker.IsGameplayInputBlocked&&!Resources.FindObjectsOfTypeAll<Transform>().Any(t=>t.name=="Appearance Preview Rig")&&!Resources.FindObjectsOfTypeAll<RenderTexture>().Any(t=>t.name=="Owned Title Modal Backdrop"),"title models render textures and gameplay input owners return");
        var actor=PlayerContext.Instance.CurrentActor;
        if(Path.GetFileName(output).StartsWith("Return",StringComparison.Ordinal)){yield return ReturnToTitleRegression(account);yield break;}
        var npc=Object.FindObjectsByType<AppearanceStylistInteractable>(FindObjectsSortMode.None).Single();
        ActorTeleportUtility.TeleportSafely(actor.transform,npc.transform.position+npc.transform.forward*1.35f,Quaternion.LookRotation(-npc.transform.forward));yield return Frames(8);
        Check(actor.GetComponent<PlayerInteractionController>().Current==npc,"existing stylist selected by actual interaction resolver");yield return KeyPress(Key.F);
        var appearance=AppearanceCustomizationPanel.Instance;Check(AppearanceCustomizationPanel.IsOpen&&!appearance.IsCreation&&appearance.Owner==npc,"same shared panel returns to normal stylist context");
        Check(appearance.surface.transform.Find("Title").GetComponent<TMP_Text>().text=="외모 변경"&&!appearance.creationIdentity.activeSelf&&appearance.apply.GetComponentInChildren<TMP_Text>().text=="외모 적용","creation labels and controls restore for stylist");
        if(Path.GetFileName(output).StartsWith("Extension",StringComparison.Ordinal)){yield return ExistingExtensions(appearance);appearance.Close();yield break;}
        if(Path.GetFileName(output).StartsWith("Stylist",StringComparison.Ordinal)){yield return StylistRegression(appearance,account,npc,actor);yield break;}
        var before=account.ReadAppearance();yield return Click(Choice(appearance,AppearanceChoiceKind.Face,appearance.catalog.faces[2].id).button);yield return Click(appearance.back);yield return Click(appearance.discardAndClose);yield return Frames(4);
        Check(!AppearanceCustomizationPanel.IsOpen&&account.ReadAppearance().Equals(before)&&!GameplayInputBlocker.IsGameplayInputBlocked,"stylist discard preserves saved appearance and returns gameplay input");
        Check(PlayerAppearanceController.Instance&&PlayerAppearanceController.Instance.Error==null,"actual actor receives selected saved appearance");
        account.FlushPendingSave();var disk=new EasySaveAccountStore(AccountBootstrap.SaveDirectory).Load();Check(disk.activeCharacterId==selected&&disk.characters.Count==2,"runtime autosave retains roster and active character");yield return Capture("gameplay-runtime");
    }
    static IEnumerator VerifyManagement()
    {
        yield return Frames(12);
        Check(SceneManager.GetActiveScene().path==OverburstUIWorkshopBuilder.ScenePath,"actual management scene enters isolated Play");
        Check(!AccountBootstrap.Ready&&AccountGameplaySession.Current==null&&Object.FindObjectsByType<AccountAutosave>(FindObjectsSortMode.None).Length==0,"management creates no game account or save writer");
        Check(!TitleCharacterPanel.Instance&&!AppearanceCustomizationPanel.Instance&&!OverburstGameMenu.Instance&&!OverburstGameMenu.IsOpen,"exhibits do not replace game singleton or pause ownership");
        Check(Object.FindObjectsByType<TitleCharacterPanel>(FindObjectsInactive.Include,FindObjectsSortMode.None).Count(p=>p.managementPreview)==7,"seven connected title and creation exhibits survive Play");
        Check(Object.FindObjectsByType<OverburstGameMenu>(FindObjectsInactive.Include,FindObjectsSortMode.None).Count(p=>p.managementPreview)==3,"three connected menu and settings exhibits survive Play");
        Check(!File.Exists(Path.Combine(IsolatedSavePlayGuard.ActiveDirectory,"account-A.es3"))&&!File.Exists(Path.Combine(IsolatedSavePlayGuard.ActiveDirectory,"account-B.es3")),"management never writes a character account generation");
        yield return Frames(25);Check(!OverburstTimeEffectArbiter.IsPaused&&!AudioListener.pause,"management leaves time and audio available");
        yield return Capture("management-play");
    }
    static string PersonalExceptAppearance(AccountSnapshot value)
    {
        var json=JObject.FromObject(value);foreach(var key in new[]{"appearance","revision","lastTransactionId"})json.Remove(key);return json.ToString(Formatting.None);
    }
    static IEnumerator VerifyHover(Button button,string name)
    {
        var visual=button.GetComponent<AppearanceButtonVisual>();Check(visual,name+" owns hover visual");
        var label=visual.label?(Graphic)visual.label:visual.legacyLabel?(Graphic)visual.legacyLabel:visual.hoverBorder?(Graphic)visual.hoverBorder:visual.backing?visual.backing:visual.skin;
        Check(label,name+" has visible hover graphic");
        pointer=new MouseState{position=new Vector2(Screen.width-3,Screen.height-3)};yield return HoverSettled();
        var normal=label.color;pointer=new MouseState{position=Point((RectTransform)button.transform)};yield return HoverSettled();
        Check(visual.IsHovered,name+" actual pointer enters button");
        Check(Vector4.Distance(normal,label.color)>.015f,name+" visibly changes on hover");
        yield return Capture("hover-"+name.Replace(' ','-'));
        pointer=new MouseState{position=new Vector2(Screen.width-3,Screen.height-3)};yield return HoverSettled();
        Write("hover-"+name.Replace(' ','-')+".json",new { visual.IsHovered, normal=Rgba(normal), restored=Rgba(label.color), focused=typeof(AppearanceButtonVisual).GetField("focused",Private).GetValue(visual), target=Rgba((Color)typeof(AppearanceButtonVisual).GetField("labelTarget",Private).GetValue(visual)) });
        Check(!visual.IsHovered&&Vector4.Distance(normal,label.color)<.015f,name+" pointer exit restores normal graphic");
    }
    static IEnumerator HoverSettled(){float until=Time.unscaledTime+.65f;while(Time.unscaledTime<until)yield return null;}
    static float[] Rgba(Color c)=>new[]{c.r,c.g,c.b,c.a};
    static IEnumerator ReturnToTitleRegression(AccountGameplaySession original)
    {
        string first=original.Read().activeCharacterId;
        var originalActor=PlayerContext.Instance.CurrentActor;
        var menu=OverburstGameMenu.Instance;
        yield return KeyPress(Key.Escape);Check(OverburstGameMenu.IsOpen&&menu.titleButton,"ESC offers title return in hub");
        yield return VerifyHover(menu.titleButton,"ESC title return");
        Check(Point((RectTransform)menu.titleButton.transform).y>Point((RectTransform)menu.quitButton.transform).y,"visible menu order matches keyboard and pad order");
        Check(menu.settingsButton.navigation.selectOnDown==menu.titleButton&&menu.titleButton.navigation.selectOnDown==menu.quitButton,"keyboard and pad navigation includes title return");
        yield return Click(menu.titleButton);Check(menu.ModalOpen&&menu.modalCancel.gameObject==EventSystem.current.currentSelectedGameObject,"title return confirmation defaults to cancel");
        yield return Capture("title-return-confirm");yield return KeyPress(Key.Escape);
        Check(OverburstGameMenu.IsOpen&&!menu.ModalOpen&&AccountGameplaySession.Current==original&&OverburstTimeEffectArbiter.IsPaused,"cancel preserves the paused game session");
        Check(PlayerAccountInventoryService.SharedCurrencyService.TryAddCurrency(CurrencyType.Gold,7)&&original.HasPendingSave,"pending progress is present before title return");
        var tx=typeof(AccountGameplaySession).GetField("transactions",Private).GetValue(original);
        var store=(EasySaveAccountStore)typeof(AccountTransactions).GetField("store",Private).GetValue(tx);
        store.FaultInjector=phase=>{if(phase=="before-write")throw new IOException("Owned title return write failure");};
        try
        {
            yield return Click(menu.titleButton);yield return Click(menu.modalConfirm);yield return Frames(5);
            Check(menu.ModalOpen&&menu.modalHeadline.text.Contains("못했습니다")&&AccountBootstrap.Ready&&AccountGameplaySession.Current==original,"failed save keeps the live session and retry dialog");
            Check(SceneManager.GetSceneByName(PersistentSceneFlow.MainSceneName).isLoaded&&OverburstTimeEffectArbiter.IsPaused,"failed save leaves the world paused and loaded");
            yield return Capture("title-return-save-failure");
        }
        finally{store.FaultInjector=null;}
        yield return Click(menu.modalConfirm);
        yield return Wait(()=>TitleCharacterPanel.Instance&&TitleCharacterPanel.Instance.Screen=="title","successful retry opens original title");yield return Frames(6);
        Check(!AccountBootstrap.Ready&&AccountGameplaySession.Current==null&&!Overburst.Quests.QuestSession.Current,"title releases runtime account and quests");
        Check(Object.FindObjectsByType<AccountAutosave>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==0&&!SceneManager.GetSceneByName(PersistentSceneFlow.MainSceneName).isLoaded,"title unloads hub and old autosave");
        var actorPosition=originalActor.transform.position;yield return Frames(35);
        Check(Vector3.Distance(actorPosition,originalActor.transform.position)<.002f&&!originalActor.Movement.enabled,"actor cannot fall while title is open");
        var title=TitleCharacterPanel.Instance;yield return Click(title.start);
        var savedCharacters=new EasySaveAccountStore(AccountBootstrap.ResolveSaveDirectory()).Load();
        var secondEntry=savedCharacters.characters.Single(c=>c.id!=first);string second=secondEntry.id;
        int expectedLevel=secondEntry.personal.level,previousLevel=savedCharacters.level;
        yield return Click(title.rosterContent.Find("Character "+second).GetComponent<Button>());yield return Click(title.enter);
        yield return Wait(()=>AccountBootstrap.Ready&&WorldSessionState.IsHideout&&!PersistentSceneFlow.Instance.IsSwitching,"another character re-enters hub after title");yield return Frames(6);
        var current=AccountGameplaySession.Current;
        Check(current!=original&&current.Read().activeCharacterId==second&&current.Read().level==expectedLevel&&PlayerProgression.CurrentLevel==expectedLevel
            &&current.Read().characters.Single(c=>c.id==first).personal.level==previousLevel,"new selection replaces personal progression and session");
        Check(Overburst.Quests.QuestSession.Current.IsBoundTo(current)&&Object.FindObjectsByType<AccountAutosave>(FindObjectsSortMode.None).Length==1,"new selection owns exactly one quest and autosave session");
        Check(originalActor.Movement.enabled&&!OverburstTimeEffectArbiter.IsPaused&&!AudioListener.pause&&!GameplayInputBlocker.IsGameplayInputBlocked,"new selection restores movement time sound and input");
        Check(PlayerAppearanceController.Instance.TryApplyCurrent(),"selected appearance rebinds to the existing actor");
        yield return Capture("title-reentry-gameplay");
        var world=SceneManager.CreateScene("Owned Title Return Run");var host=new GameObject("Owned Return Run Gate");SceneManager.MoveGameObjectToScene(host,world);
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);floor.transform.SetParent(host.transform,false);floor.transform.localPosition=new Vector3(0,-.5f,0);floor.transform.localScale=new Vector3(50,1,50);
        var entry=new GameObject("Entry").transform;entry.SetParent(host.transform,false);entry.localPosition=Vector3.up*.2f;
        var gate=host.AddComponent<RunWorldGate>();
        Check(PersistentSceneFlow.Instance.EnterDebugRun(world.name,new MapInstanceState{level=1,grade=ItemGrade.Common}),"controlled additive run enters through the product scene flow");
        yield return Wait(()=>gate.Context!=null,"run preparation reaches gate");Check(gate.CompletePreparation(gate.Context.RunId,entry),"controlled run world reports readiness");
        yield return Wait(()=>WorldSessionState.Phase==WorldPhase.Run&&!PersistentSceneFlow.Instance.IsSwitching,"controlled run becomes active");yield return Frames(5);
        current=AccountGameplaySession.Current;var state=current.Read();
        var loot=ItemSnapshotCodec.CopyValues(state.items.First());loot.instanceId=Guid.NewGuid().ToString("N");loot.acquisitionOrder=state.nextAcquisitionOrder;loot.originRunId=state.run.runId;
        current.ExecuteState("owned-title-loot",c=>{c.items.Add(loot);c.inventory[c.inventory.FindIndex(0,c.unlockedSlots,string.IsNullOrEmpty)]=loot.instanceId;c.nextAcquisitionOrder++;});
        string runId=state.run.runId,stashItem=state.stashTabs[0].slots[0];
        yield return KeyPress(Key.Escape);menu=OverburstGameMenu.Instance;
        Check(menu.returnButton.gameObject.activeSelf&&menu.returnButton.navigation.selectOnDown==menu.titleButton,"run retains separate hub return and title return buttons");
        yield return Click(menu.titleButton);Check(menu.modalDescription.text.Contains("이번 탐험")&&menu.modalDescription.text.Contains("잃습니다"),"run title confirmation explains carried loot loss");yield return Capture("run-title-return-confirm");yield return Click(menu.modalConfirm);
        yield return Wait(()=>TitleCharacterPanel.Instance&&TitleCharacterPanel.Instance.Screen=="title","run returns to title");yield return Frames(6);
        var disk=new EasySaveAccountStore(AccountBootstrap.ResolveSaveDirectory()).Load();
        Check(disk.run.runId==runId&&disk.run.phase==RunPhase.Failed&&!disk.items.Any(i=>i.instanceId==loot.instanceId),"return fails the run durably and removes only its carried loot");
        Check(disk.stashTabs[0].slots[0]==stashItem&&disk.items.Any(i=>i.instanceId==stashItem),"shared stash survives run abandonment");
        Check(!world.isLoaded&&AccountGameplaySession.Current==null&&Object.FindObjectsByType<AccountAutosave>(FindObjectsInactive.Include,FindObjectsSortMode.None).Length==0,"second title return unloads the run and releases writers");
        yield return Capture("final-returned-title");
        title=TitleCharacterPanel.Instance;yield return Click(title.start);yield return Click(title.create);
        yield return VerifyHover(title.classButtons[1],"class mage");
        yield return Click(title.classButtons[1]);yield return Wait(()=>!(bool)typeof(TitleCharacterPanel).GetField("rotating",Private).GetValue(title),"hover regression mage settles");
        Check(!title.classNext.interactable,"hover does not enable coming-soon class creation");
        yield return Click(title.classButtons[0]);yield return Wait(()=>!(bool)typeof(TitleCharacterPanel).GetField("rotating",Private).GetValue(title),"hover regression knight settles");
        yield return VerifyHover(title.classNext,"class continue");yield return Click(title.classNext);yield return TextInput(title.nameInput,"호버검증");
        yield return VerifyHover(title.nameNext,"name continue");yield return Click(title.nameNext);yield return Frames(4);
        var appearance=AppearanceCustomizationPanel.Instance;yield return VerifyHover(Choice(appearance,AppearanceChoiceKind.Face,appearance.catalog.faces[1].id).button,"shared face option");
        yield return Click(appearance.back);yield return Click(appearance.discardAndClose);yield return Frames(4);
        Check(!AppearanceCustomizationPanel.IsOpen&&title.Screen=="characters"&&new EasySaveAccountStore(AccountBootstrap.ResolveSaveDirectory()).Load().characters.Count==2,"hover regression cancels creation without another character");
    }
    static IEnumerator StylistRegression(AppearanceCustomizationPanel p,AccountGameplaySession account,AppearanceStylistInteractable npc,PlayerActorRuntime actor)
    {
        account.FlushPendingSave();string preserved=PersonalExceptAppearance(account.Read());var saved=account.ReadAppearance();
        foreach(var choice in p.choices)
        {
            int limit=20;while(!choice.gameObject.activeInHierarchy&&limit-->0)yield return Click(choice.kind==AppearanceChoiceKind.Hair?p.hairNext:p.equipmentNext);
            Check(choice.gameObject.activeInHierarchy,"paged option reachable "+choice.kind+" "+choice.optionId);yield return Click(choice.button);
            Check(PersonalExceptAppearance(account.Read())==preserved&&account.ReadAppearance().Equals(saved),"preview option does not write account "+choice.kind+" "+choice.optionId);
        }
        yield return Click(p.headgearToggle);yield return Click(p.headgearToggle);
        yield return Click(p.faceFrame);yield return Click(p.upperFrame);yield return Click(p.fullFrame);
        var camera=(Camera)typeof(AppearanceCharacterPreview).GetField("viewCamera",Private).GetValue(p.preview);var original=p.preview.Model.transform.localRotation;float distance=Vector3.Distance(camera.transform.position,p.preview.Model.transform.root.position);
        yield return Drag(p.characterTarget.rectTransform,new Vector2(180,0));Check(Quaternion.Angle(original,p.preview.Model.transform.localRotation)>20,"real pointer rotates stylist preview");
        pointer.scroll=new Vector2(0,120);yield return Frames(2);pointer.scroll=Vector2.zero;yield return Frames(3);Check(Vector3.Distance(camera.transform.position,p.preview.Model.transform.root.position)<distance-.1f,"real wheel zooms stylist preview");
        yield return Click(p.resetView);Check(Quaternion.Angle(original,p.preview.Model.transform.localRotation)<.1f,"reset restores stylist orientation");
        var candidate=p.Session.CandidateForSave();var transaction=typeof(AccountGameplaySession).GetField("transactions",Private).GetValue(account);var store=(EasySaveAccountStore)typeof(AccountTransactions).GetField("store",Private).GetValue(transaction);
        store.FaultInjector=phase=>{if(phase=="before-write")throw new IOException("Owned stylist save failure");};
        try{yield return Click(p.apply);Check(p.Session!=null&&p.Session.CandidateForSave().Equals(candidate)&&account.ReadAppearance().Equals(saved),"failed stylist write retains draft and committed appearance");}finally{store.FaultInjector=null;}
        yield return Click(p.apply);yield return Frames(5);Check(!AppearanceCustomizationPanel.IsOpen&&account.ReadAppearance().Equals(candidate),"stylist retry applies appearance");
        Check(new EasySaveAccountStore(AccountBootstrap.SaveDirectory).Load().appearance.Equals(candidate)&&PersonalExceptAppearance(account.Read())==preserved,"stylist save preserves both characters growth inventory equipment and shared stash");
        PlayerContext.Instance.Bind(null);PlayerContext.Instance.Bind(actor);yield return Frames(3);Check(PlayerAppearanceController.Instance.TryApplyCurrent()&&PlayerAppearanceController.Instance.Error==null,"actor rebind restores saved look");
        for(int i=0;i<3;i++)
        {
            yield return KeyPress(Key.F);Check(AppearanceCustomizationPanel.IsOpen&&!p.IsCreation&&p.Session.Draft.Equals(candidate),"repeat stylist entry restores saved appearance "+i);
            if(i==0){yield return ExistingExtensions(p);p.ShowAppearance();}
            p.Close();yield return Frames(4);Check(!p.preview.Model&&!p.preview.Texture&&!GameplayInputBlocker.IsGameplayInputBlocked,"repeat close returns model texture and input "+i);
        }
        account.FlushPendingSave();var disk=new EasySaveAccountStore(AccountBootstrap.SaveDirectory).Load();Check(disk.characters.Count==2&&disk.activeCharacterId==account.Read().activeCharacterId&&disk.appearance.Equals(candidate),"stylist autosave preserves roster and active look");yield return Capture("stylist-final");
    }
    static IEnumerator ExistingExtensions(AppearanceCustomizationPanel panel)
    {
        // Existing extension verifiers use their original host's input helpers. Lend only our
        // owned devices/output during their coroutine, then restore every host field/callback.
        var host=typeof(AppearanceCustomizationPlayVerifier);const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Static;var fields=new[]{"mouse","keyboard","pointer","output"}.Select(n=>host.GetField(n,flags)).ToArray();var old=fields.Select(f=>f.GetValue(null)).ToArray();
        var inherited=(List<string>)host.GetField("checks",flags).GetValue(null);int count=inherited.Count;var push=(Action)Delegate.CreateDelegate(typeof(Action),host.GetMethod("Push",flags));
        fields[0].SetValue(null,mouse);fields[1].SetValue(null,keyboard);fields[2].SetValue(null,new MouseState());fields[3].SetValue(null,output);InputSystem.onBeforeUpdate-=Push;InputSystem.onBeforeUpdate+=push;
        try{foreach(var type in TypeCache.GetTypesDerivedFrom<AppearancePlayExtension>().Where(t=>!t.IsAbstract))yield return ((AppearancePlayExtension)Activator.CreateInstance(type)).Verify(panel,output);foreach(var check in inherited.Skip(count))Check(true,check);}
        finally{InputSystem.onBeforeUpdate-=push;for(int i=0;i<fields.Length;i++)fields[i].SetValue(null,old[i]);pointer=new MouseState();InputSystem.onBeforeUpdate+=Push;}
    }
    static void State(PlayModeStateChange state)
    {
        if(state==PlayModeStateChange.ExitingPlayMode && work.Count>0){bool expected=SessionState.GetBool(K+"expectedQuit",false);if(expected)Check(true,"confirmed title quit stops Editor Play");Finish(expected?null:new Exception("Play exited during verification"));}
        if(state==PlayModeStateChange.EnteredEditMode && !string.IsNullOrEmpty(SessionState.GetString(K+"return",""))){EditorApplication.update-=Return;EditorApplication.update+=Return;}
    }
    static void RestoreInput()
    {
        InputSystem.onBeforeUpdate-=Push;foreach(var p in masks)if(p.Key)p.Key.devices=p.Value;masks.Clear();SessionState.EraseString(K+"masks");
        if(mouse!=null && mouse.added)InputSystem.RemoveDevice(mouse);if(keyboard!=null && keyboard.added)InputSystem.RemoveDevice(keyboard);if(gamepad!=null&&gamepad.added)InputSystem.RemoveDevice(gamepad);mouse=null;keyboard=null;gamepad=null;
        if(originalInput){InputSystem.settings=originalInput;var type=typeof(InputSettings).Assembly.GetType("UnityEngine.InputSystem.InputSystemObject");if(type!=null)foreach(var state in Resources.FindObjectsOfTypeAll(type))(state as ISerializationCallbackReceiver)?.OnBeforeSerialize();}
        if(temporaryInput)Object.DestroyImmediate(temporaryInput);originalInput=null;temporaryInput=null;
        foreach(string field in new[]{"input","temporaryInput","mouse","keyboard","gamepad"})SessionState.EraseInt(K+field);
    }
    static void Finish(Exception error)
    {
        SessionState.EraseBool(K+"expectedQuit");
        EditorApplication.update-=Tick;EditorApplication.update-=Begin;EditorApplication.update-=Interrupted;while(work.Count>0)(work.Pop() as IDisposable)?.Dispose();
        bool own=OwnPlay();if(own && AppearanceCustomizationPanel.Instance)AppearanceCustomizationPanel.Instance.Close();RestoreInput();
        Application.runInBackground=SessionState.GetBool(K+"background",Application.runInBackground);SessionState.EraseBool(K+"background");SessionState.EraseString(K+"pending");SessionState.EraseBool(K+"running");SessionState.EraseBool(K+"equipment");SessionState.EraseFloat(K+"deadline");
        output=SessionState.GetString(K+"return",output);
        try{Write("play-result.json",new{success=error==null,checks,error=error?.ToString()});}
        finally
        {
            if(EditorApplication.isPlaying && own)EditorApplication.ExitPlaymode();if(!EditorApplication.isPlayingOrWillChangePlaymode){EditorApplication.update-=Return;EditorApplication.update+=Return;}
        }
    }
    static void Interrupted()
    {
        if(EditorApplication.isCompiling || EditorApplication.isUpdating)return;
        output=SessionState.GetString(K+"return","");if(string.IsNullOrEmpty(output)){EditorApplication.update-=Interrupted;return;}if(EditorApplication.isPlaying && !OwnPlay())return;
        var json=SessionState.GetString(K+"masks","");if(!string.IsNullOrEmpty(json))foreach(var r in Newtonsoft.Json.Linq.JArray.Parse(json)){var asset=EditorUtility.InstanceIDToObject((int)r["asset"]) as InputActionAsset;if(asset)masks[asset]=r["devices"].Type==Newtonsoft.Json.Linq.JTokenType.Null?null:r["devices"].Select(id=>InputSystem.GetDeviceById((int)id)).Where(d=>d!=null).ToArray();}
        temporaryInput=EditorUtility.InstanceIDToObject(SessionState.GetInt(K+"temporaryInput",0)) as InputSettings;if(temporaryInput && InputSystem.settings==temporaryInput)originalInput=EditorUtility.InstanceIDToObject(SessionState.GetInt(K+"input",0)) as InputSettings;
        var m=InputSystem.GetDeviceById(SessionState.GetInt(K+"mouse",0));if(m is Mouse a && a.name=="TitleVerifierMouse")mouse=a;
        var b=InputSystem.GetDeviceById(SessionState.GetInt(K+"keyboard",0));if(b is Keyboard c && c.name=="TitleVerifierKeyboard")keyboard=c;
        var g=InputSystem.GetDeviceById(SessionState.GetInt(K+"gamepad",0));if(g is Gamepad p&&p.name=="TitleVerifierGamepad")gamepad=p;
        Finish(new Exception("Verification interrupted by assembly reload"));
    }
    static void Return()
    {
        output=SessionState.GetString(K+"return","");if(string.IsNullOrEmpty(output)){EditorApplication.update-=Return;return;}
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !string.IsNullOrEmpty(IsolatedSavePlayGuard.ActiveDirectory))return;
        var env=Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable);var prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared","");
        foreach(var path in new[]{env,prepared})if(!string.IsNullOrEmpty(path) && !Path.GetFullPath(path).StartsWith(output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return;
        bool timedOut=EditorApplication.timeSinceStartup>SessionState.GetFloat(K+"returnDeadline",0);
        string scene=SessionState.GetString(K+"startScene","");var settingsDirectory=Environment.GetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY");if(!string.IsNullOrEmpty(settingsDirectory)&&Path.GetFullPath(settingsDirectory).StartsWith(output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))Environment.SetEnvironmentVariable("OVERBURST_SETTINGS_DIRECTORY",null);
        EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(scene)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(scene);IsolatedSavePlayGuard.UseRealAccount();
        SessionState.EraseString(K+"return");SessionState.EraseString(K+"startScene");SessionState.EraseFloat(K+"returnDeadline");EditorApplication.update-=Return;
        Write("return-result.json",new{reason=timedOut?"return timeout":null,success=!timedOut && !IsolatedSavePlayGuard.RequiresAccountChoice && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(IsolatedSavePlayGuard.Variable)),guard=IsolatedSavePlayGuard.RequiresAccountChoice,active=IsolatedSavePlayGuard.ActiveDirectory,prepared=SessionState.GetString("Overburst.IsolatedSavePlayGuard.prepared",""),expires=SessionState.GetString("Overburst.IsolatedSavePlayGuard.expires",""),input=InputSystem.settings.GetInstanceID(),startScene=AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),scenes=Scenes()});
    }
}
